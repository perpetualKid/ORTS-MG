// COPYRIGHT 2010, 2011, 2012, 2013, 2014, 2015, 2016 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Microsoft.Xna.Framework;

using Orts.Common;
using Orts.Common.Position;
using Orts.Common.Xna;
using Orts.Formats.Msts.Models;
using Orts.Formats.Msts.Parsers;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;

namespace Orts.Simulation.World
{
    public class TurnTable : MovingTable
    {
        // Fixed data
        private readonly List<float> angles = new List<float>();
        // Dynamic data
        public Rotation RotationDirection { get; set; }
        public Rotation AutoRotationDirection { get; set; }

        public float YAngle { get; set; } // Y angle of animated part, to be compared with Y angles of endpoints
        public bool ForwardConnected { get; private set; } = true; // Platform has its forward part connected to a track
        public bool RearConnected { get; private set; } // Platform has its rear part connected to a track
        public int ForwardConnectedTarget { get; set; } = -1; // index of trackend connected
        public int RearConnectedTarget { get; set; } = -1; // index of trackend connected
        public float TargetY { get; private set; } //final target for Viewer;

        private bool saveForwardConnected = true; // Platform has its forward part connected to a track
        private bool saveRearConnected; // Platform has its rear part connected to a track

        internal TurnTable(STFReader stf)
        {
            string animation;
            Matrix location = Matrix.Identity;
            location.M44 = 100_000_000; //WorlPosition not yet defined, will be loaded when loading related tile;
            stf.MustMatch("(");
            stf.ParseBlock(new[] {
                new STFReader.TokenProcessor("wfile", ()=>{
                    WFile = stf.ReadStringBlock(null);
                    position = new WorldPosition(int.Parse(WFile.Substring(1, 7), CultureInfo.InvariantCulture), int.Parse(WFile.Substring(8, 7), CultureInfo.InvariantCulture), location);
                }),
                new STFReader.TokenProcessor("uid", ()=>{ UID = stf.ReadIntBlock(-1); }),
                new STFReader.TokenProcessor("animation", ()=>{ animation = stf.ReadStringBlock(null);
                                                                Animations.Add(animation);}),
                new STFReader.TokenProcessor("diameter", ()=>{ Length = stf.ReadFloatBlock(STFReader.Units.None , null);}),
                new STFReader.TokenProcessor("xoffset", ()=>{ offset.X += stf.ReadFloatBlock(STFReader.Units.None , null);}),
                new STFReader.TokenProcessor("zoffset", ()=>{ offset.Z = -stf.ReadFloatBlock(STFReader.Units.None , null);}),
                new STFReader.TokenProcessor("trackshapeindex", ()=>
                {
                    TrackShapeIndex = stf.ReadIntBlock(-1);
                    InitializeAnglesAndTrackNodes();
                }),
             });
        }

        /// <summary>
        /// Saves the general variable parameters
        /// Called from within the Simulator class.
        /// </summary>
        internal override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            outf.Write((int)RotationDirection);
            outf.Write((int)AutoRotationDirection);
            outf.Write(YAngle);
            outf.Write(ForwardConnected);
            outf.Write(RearConnected);
            outf.Write(saveForwardConnected);
            outf.Write(saveRearConnected);
            outf.Write(ForwardConnectedTarget);
            outf.Write(RearConnectedTarget);
            outf.Write(TargetY);
        }


        /// <summary>
        /// Restores the general variable parameters
        /// Called from within the Simulator class.
        /// </summary>
        internal override void Restore(BinaryReader inf, Simulator simulator)
        {
            base.Restore(inf, simulator);
            RotationDirection = (Rotation)inf.ReadInt32();
            AutoRotationDirection = (Rotation)inf.ReadInt32();
            YAngle = inf.ReadSingle();
            ForwardConnected = inf.ReadBoolean();
            RearConnected = inf.ReadBoolean();
            saveForwardConnected = inf.ReadBoolean();
            saveRearConnected = inf.ReadBoolean();
            ForwardConnectedTarget = inf.ReadInt32();
            RearConnectedTarget = inf.ReadInt32();
            TargetY = inf.ReadSingle();
        }

        private void InitializeAnglesAndTrackNodes()
        {
            TrackShape trackShape = Simulator.Instance.TSectionDat.TrackShapes[(uint)TrackShapeIndex];
            uint nSections = Simulator.Instance.TSectionDat.TrackShapes[(uint)TrackShapeIndex].SectionIndices[0].SectionsCount;
            trackNodesIndex = new int[Simulator.Instance.TSectionDat.TrackShapes[(uint)TrackShapeIndex].SectionIndices.Length];
            trackNodesOrientation = new bool[trackNodesIndex.Length];
            trackVectorSectionsIndex = new int[trackNodesIndex.Length];
            int i = 0;
            foreach (SectionIndex sectionIdx in trackShape.SectionIndices)
            {
                angles.Add(MathHelper.ToRadians((float)sectionIdx.AngularOffset));
                trackNodesIndex[i] = -1;
                trackVectorSectionsIndex[i] = -1;
                i++;
            }
            TrackNode[] trackNodes = Simulator.Instance.TrackDatabase.TrackDB.TrackNodes;
            for (int j = 1; j < trackNodes.Length; j++)
                if (trackNodes[j] is TrackVectorNode tvn && tvn.TrackVectorSections != null)
                {
                    int trackVectorSection = Array.FindIndex(tvn.TrackVectorSections, trVectorSection =>
                        trVectorSection.Location.TileX == WorldPosition.TileX && trVectorSection.Location.TileZ == WorldPosition.TileZ && trVectorSection.WorldFileUiD == UID);
                    if (trackVectorSection >= 0)
                        if (tvn.TrackVectorSections.Length > (int)nSections)
                        {
                            i = tvn.TrackVectorSections[trackVectorSection].Flag1 / 2;
                            trackNodesIndex[i] = j;
                            trackVectorSectionsIndex[i] = trackVectorSection;
                            trackNodesOrientation[i] = tvn.TrackVectorSections[trackVectorSection].Flag1 % 2 == 0;

                        }
                }
        }

        /// <summary>
        /// Computes the nearest turntable exit in the actual direction
        /// Returns the Y angle to be compared.
        /// </summary>
        public override void ComputeTarget(bool clockwise)
        {
            if (!ContinuousMotion)
                return;
            ContinuousMotion = false;
            GoToTarget = false;
            RotationDirection = clockwise ? Rotation.Clockwise : Rotation.CounterClockwise;

            float forwardAngleDiff = (int)RotationDirection * 3.5f;
            float rearAngleDiff = (int)RotationDirection * 3.5f;
            ForwardConnected = false;
            RearConnected = false;
            if (angles.Count == 0)
            {
                RotationDirection = Rotation.None;
                ForwardConnectedTarget = -1;
                RearConnectedTarget = -1;
            }
            else
            {
                if (RotationDirection == Rotation.Clockwise)
                {
                    for (int i = angles.Count - 1; i >= 0; i--)
                        if (trackNodesIndex[i] != -1 && trackVectorSectionsIndex[i] != -1)
                        {
                            float angleDiff = MathHelper.WrapAngle(angles[i] + YAngle);
                            if (angleDiff < forwardAngleDiff && angleDiff >= 0)
                            {
                                ForwardConnectedTarget = i;
                                forwardAngleDiff = angleDiff;
                            }
                            angleDiff = MathHelper.WrapAngle(angles[i] + YAngle + (float)Math.PI);
                            if (angleDiff < rearAngleDiff && angleDiff >= 0)
                            {
                                RearConnectedTarget = i;
                                rearAngleDiff = angleDiff;
                            }
                        }
                    if (forwardAngleDiff < 0.1 || rearAngleDiff < 0.1)
                    {
                        if (forwardAngleDiff < rearAngleDiff && Math.Abs(forwardAngleDiff - rearAngleDiff) > 0.01)
                            RearConnectedTarget = -1;
                        else if (forwardAngleDiff > rearAngleDiff && Math.Abs(forwardAngleDiff - rearAngleDiff) > 0.01)
                            ForwardConnectedTarget = -1;
                    }
                    else
                    {
                        RotationDirection = Rotation.None;
                        ForwardConnectedTarget = -1;
                        RearConnectedTarget = -1;
                    }
                }
                else if (RotationDirection == Rotation.CounterClockwise)
                {
                    for (int i = 0; i <= angles.Count - 1; i++)
                        if (trackNodesIndex[i] != -1 && trackVectorSectionsIndex[i] != -1)
                        {
                            float thisAngleDiff = MathHelper.WrapAngle(angles[i] + YAngle);
                            if (thisAngleDiff > forwardAngleDiff && thisAngleDiff <= 0)
                            {
                                ForwardConnectedTarget = i;
                                forwardAngleDiff = thisAngleDiff;
                            }
                            thisAngleDiff = MathHelper.WrapAngle(angles[i] + YAngle + (float)Math.PI);
                            if (thisAngleDiff > rearAngleDiff && thisAngleDiff <= 0)
                            {
                                RearConnectedTarget = i;
                                rearAngleDiff = thisAngleDiff;
                            }
                        }
                    if (forwardAngleDiff > -0.1 || rearAngleDiff > -0.1)
                    {
                        if (forwardAngleDiff > rearAngleDiff && Math.Abs(forwardAngleDiff - rearAngleDiff) > 0.01)
                            RearConnectedTarget = -1;
                        else if (forwardAngleDiff < rearAngleDiff && Math.Abs(forwardAngleDiff - rearAngleDiff) > 0.01)
                            ForwardConnectedTarget = -1;
                    }
                    else
                    {
                        RotationDirection = Rotation.None;
                        ForwardConnectedTarget = -1;
                        RearConnectedTarget = -1;
                    }
                }
            }
        }

        /// <summary>
        /// Starts continuous movement
        /// 
        /// </summary>
        /// 
        public override void StartContinuous(bool clockwise)
        {
            if (TrainsOnMovingTable.Count > 1 || TrainsOnMovingTable.Count == 1 && TrainsOnMovingTable[0].FrontOnBoard ^ TrainsOnMovingTable[0].BackOnBoard)
            {
                RotationDirection = Rotation.None;
                ContinuousMotion = false;
                if (SendNotifications)
                    Simulator.Instance.Confirmer.Warning(Simulator.Catalog.GetString("Train partially on turntable, can't rotate"));
                return;
            }
            if (TrainsOnMovingTable.Count == 1 && TrainsOnMovingTable[0].FrontOnBoard && TrainsOnMovingTable[0].BackOnBoard)
            {
                // Preparing for rotation
                Train train = TrainsOnMovingTable[0].Train;
                if (Math.Abs(train.SpeedMpS) > 0.1 || train.LeadLocomotiveIndex != -1 && (train.LeadLocomotive.ThrottlePercent >= 1 || !(train.LeadLocomotive.Direction == MidpointDirection.N
                    || Math.Abs(train.MUReverserPercent) <= 1)) || train.ControlMode != TrainControlMode.Manual && train.ControlMode != TrainControlMode.TurnTable &&
                    train.ControlMode != TrainControlMode.Explorer && train.ControlMode != TrainControlMode.Undefined)
                {
                    if (SendNotifications)
                        Simulator.Instance.Confirmer.Warning(Simulator.Catalog.GetString("Rotation can't start: check throttle, speed, direction and control mode"));
                    return;
                }
                if (train.ControlMode == TrainControlMode.Manual || train.ControlMode == TrainControlMode.Explorer || train.ControlMode == TrainControlMode.Undefined)
                {
                    ComputeTrainPosition(train);
                    train.ControlMode = TrainControlMode.TurnTable;
                }
                if (SendNotifications)
                    Simulator.Instance.Confirmer.Information(Simulator.Catalog.GetString("Turntable starting rotation with train"));

            }
            RotationDirection = clockwise ? Rotation.Clockwise : Rotation.CounterClockwise;
            ContinuousMotion = true;
        }

        // Computing position of cars relative to center of platform
        public void ComputeTrainPosition(Train train)
        {
            saveForwardConnected = ForwardConnected ^ !trackNodesOrientation[ConnectedTrackEnd];
            saveRearConnected = RearConnected;
            Matrix invAnimationXNAMatrix = Matrix.Invert(animationXNAMatrix);
            relativeCarPositions = new List<Matrix>();
            foreach (TrainCar trainCar in train?.Cars ?? throw new ArgumentNullException(nameof(train)))
            {
                trainCar.WorldPosition = trainCar.WorldPosition.NormalizeTo(WorldPosition.TileX, WorldPosition.TileZ);
                Matrix relativeCarPosition = Matrix.Multiply(trainCar.WorldPosition.XNAMatrix, invAnimationXNAMatrix);
                relativeCarPositions.Add(relativeCarPosition);
            }
            Vector3 XNALocation = train.FrontTDBTraveller.Location;
            XNALocation.Z = -XNALocation.Z;
            XNALocation.X += 2048 * (train.FrontTDBTraveller.TileX - WorldPosition.TileX);
            XNALocation.Z -= 2048 * (train.FrontTDBTraveller.TileZ - WorldPosition.TileZ);
            relativeFrontTravellerXNALocation = Vector3.Transform(XNALocation, invAnimationXNAMatrix);
            XNALocation = train.RearTDBTraveller.Location;
            XNALocation.Z = -XNALocation.Z;
            XNALocation.X += 2048 * (train.RearTDBTraveller.TileX - WorldPosition.TileX);
            XNALocation.Z -= 2048 * (train.RearTDBTraveller.TileZ - WorldPosition.TileZ);
            relativeRearTravellerXNALocation = Vector3.Transform(XNALocation, invAnimationXNAMatrix);
        }

        public void ComputeCenter(in WorldPosition worldPosition)
        {
            VectorExtension.Transform(offset, worldPosition.XNAMatrix, out Vector3 centerCoordinates);
            position = worldPosition.SetTranslation(centerCoordinates.X, centerCoordinates.Y, centerCoordinates.Z);
        }

        public void RotateTrain(Matrix animationXNAMatrix)
        {
            if ((RotationDirection != Rotation.None || GoToTarget || GoToAutoTarget) && TrainsOnMovingTable.Count == 1 && TrainsOnMovingTable[0].FrontOnBoard &&
                TrainsOnMovingTable[0].BackOnBoard && TrainsOnMovingTable[0].Train.ControlMode == TrainControlMode.TurnTable)
            {
                // Rotate together also train
                int relativeCarPositions = 0;
                foreach (TrainCar traincar in TrainsOnMovingTable[0].Train.Cars)
                {
                    traincar.WorldPosition = new WorldPosition(traincar.WorldPosition.TileX, traincar.WorldPosition.TileZ,
                        Matrix.Multiply(base.relativeCarPositions[relativeCarPositions], animationXNAMatrix));
                    relativeCarPositions++;
                }
            }
        }

        public void AutoRotateTable(double elapsedClockSeconds)
        {
            GoToAutoTarget = true;

            double angleStep = (YAngle / Math.PI * 1800.0 + 3600) % 3600.0;
            float usedFrameRate = TurntableFrameRate ?? 30f;

            if (AutoRotationDirection == Rotation.Clockwise)
                angleStep -= elapsedClockSeconds * usedFrameRate;
            else if (AutoRotationDirection == Rotation.CounterClockwise)
                angleStep += elapsedClockSeconds * usedFrameRate;

            YAngle = TargetY = (float)MathHelperD.WrapAngle(angleStep / 1800.0 * Math.PI);
        }

        public override void Update()
        {
            foreach (TrainOnMovingTable trainOnTurntable in TrainsOnMovingTable)
            {
                if (trainOnTurntable.FrontOnBoard ^ trainOnTurntable.BackOnBoard)
                {
                    RotationDirection = Rotation.None;
                    ContinuousMotion = false;
                    return;
                }
            }

            if (ContinuousMotion)
            {
                ForwardConnected = false;
                RearConnected = false;
                ConnectedTrackEnd = -1;
                GoToTarget = false;
            }
            else
            {
                if (RotationDirection != Rotation.None || AutoRotationDirection != Rotation.None)
                {
                    ForwardConnected = false;
                    RearConnected = false;
                    if (ForwardConnectedTarget != -1)
                    {
                        if (Math.Abs(MathHelper.WrapAngle(angles[ForwardConnectedTarget] + YAngle)) < 0.005)
                        {
                            ForwardConnected = true;
                            GoToTarget = RotationDirection != Rotation.None;  // only set if not in auto mode
                            RotationDirection = Rotation.None;
                            AutoRotationDirection = Rotation.None;
                            ConnectedTrackEnd = ForwardConnectedTarget;
                            if (SendNotifications)
                                Simulator.Instance.Confirmer.Information(Simulator.Catalog.GetString("Turntable forward connected"));
                            TargetY = -angles[ForwardConnectedTarget];
                        }
                    }
                    else if (RearConnectedTarget != -1)
                    {
                        if (Math.Abs(MathHelper.WrapAngle(angles[RearConnectedTarget] + YAngle + (float)Math.PI)) < 0.005)
                        {
                            RearConnected = true;
                            GoToTarget = RotationDirection != Rotation.None;  // only set if not in auto mode
                            RotationDirection = Rotation.None;
                            AutoRotationDirection = Rotation.None;
                            ConnectedTrackEnd = RearConnectedTarget;
                            if (SendNotifications)
                                Simulator.Instance.Confirmer.Information(Simulator.Catalog.GetString("Turntable backward connected"));
                            TargetY = -MathHelper.WrapAngle(angles[RearConnectedTarget] + (float)Math.PI);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// TargetExactlyReached: if train on board, it can exit the turntable
        /// </summary>
        public void TargetExactlyReached()
        {
            Traveller.TravellerDirection direction = ForwardConnected ? Traveller.TravellerDirection.Forward : Traveller.TravellerDirection.Backward;
            direction = saveForwardConnected ^ !trackNodesOrientation[ConnectedTrackEnd] ? direction : direction == Traveller.TravellerDirection.Forward ? Traveller.TravellerDirection.Backward : Traveller.TravellerDirection.Forward;
            GoToTarget = false;
            if (TrainsOnMovingTable.Count == 1)
            {
                Train train = TrainsOnMovingTable[0].Train;
                if (train.ControlMode == TrainControlMode.TurnTable)
                    train.ReenterTrackSections(trackNodesIndex[ConnectedTrackEnd], finalFrontTravellerXNALocation, finalRearTravellerXNALocation, direction);
            }
        }

        /// <summary>
        /// CheckMovingTableAligned: checks if turntable aligned with entering train
        /// </summary>
        public override bool CheckMovingTableAligned(Train train, bool forward)
        {
            //Traveller.TravellerDirection direction;
            //if ((ForwardConnected || RearConnected) && trackVectorSectionsIndex[ConnectedTrackEnd] != -1 && trackNodesIndex[ConnectedTrackEnd] != -1 &&
            //    (trackNodesIndex[ConnectedTrackEnd] == train.FrontTDBTraveller.TN.Index || trackNodesIndex[ConnectedTrackEnd] == train.RearTDBTraveller.TN.Index))
            //{
            //    direction = ForwardConnected ? Traveller.TravellerDirection.Forward : Traveller.TravellerDirection.Backward;
            //    return true;
            //}
            //else
            //{
            //    direction = Traveller.TravellerDirection.Forward;
            //}
            //return false;
            if (null == train)
                throw new ArgumentNullException(nameof(train));

            return (ForwardConnected || RearConnected) && trackVectorSectionsIndex[ConnectedTrackEnd] != -1 && trackNodesIndex[ConnectedTrackEnd] != -1 &&
                (trackNodesIndex[ConnectedTrackEnd] == train.FrontTDBTraveller.TN.Index || trackNodesIndex[ConnectedTrackEnd] == train.RearTDBTraveller.TN.Index);
        }

        /// <summary>
        /// PerformUpdateActions: actions to be performed at every animation step
        /// </summary>
        public void PerformUpdateActions(Matrix absAnimationMatrix)
        {
            RotateTrain(absAnimationMatrix);
            if ((GoToTarget || GoToAutoTarget) && TrainsOnMovingTable.Count == 1 && TrainsOnMovingTable[0].Train.ControlMode == TrainControlMode.TurnTable)
                RecalculateTravellerXNALocations(absAnimationMatrix);
            if (GoToTarget)
                TargetExactlyReached();
        }

        public (float startAngle, float endAngle) FindConnectingDirections(int requestedExit)
        {
            return (angles[ConnectedTrackEnd], angles[requestedExit]);
        }
    }
}
