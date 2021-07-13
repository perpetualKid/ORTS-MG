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
using Orts.Simulation.World;

namespace Orts.Simulation
{
    /// <summary>
    /// Reads file ORTSTurntables.dat and creates the instances of the turntables
    /// </summary>
    public class TransferTable : MovingTable
    {
        private readonly List<float> offsets = new List<float>();

        public float Width { get; private set; }

        // Dynamic data
        public MidpointDirection MotionDirection { get; private set; }

        public float XPos { get; set; } // X Position of animated part, to be compared with X positions of endpoints
        public bool Connected { get; private set; } = true; // Transfertable is connected to a track
        public float TargetX { get; private set; } //final target for Viewer;

        private int connectedTarget = -1; // index of trackend connected
        private bool saveConnected = true; // Transfertable is connected to a track

        internal TransferTable(STFReader stf)
        {
            string animation;
            Matrix location = Matrix.Identity;
            location.M44 = 100_000_000; //WorlPosition not yet defined, will be loaded when loading related tile;
            stf.MustMatch("(");
            stf.ParseBlock(new[] {
                new STFReader.TokenProcessor("wfile", () => {
                    WFile = stf.ReadStringBlock(null);
                    position = new WorldPosition(int.Parse(WFile.Substring(1, 7), CultureInfo.InvariantCulture), int.Parse(WFile.Substring(8, 7), CultureInfo.InvariantCulture), location);
                }),
                new STFReader.TokenProcessor("uid", ()=>{ UID = stf.ReadIntBlock(-1); }),
                new STFReader.TokenProcessor("animation", ()=>{ animation = stf.ReadStringBlock(null);
                                                                Animations.Add(animation);}),
                new STFReader.TokenProcessor("length", ()=>{ Length = stf.ReadFloatBlock(STFReader.Units.None , null);}),
                new STFReader.TokenProcessor("xoffset", ()=>{ offset.X = stf.ReadFloatBlock(STFReader.Units.None , null);}),
                new STFReader.TokenProcessor("zoffset", ()=>{ offset.Z = -stf.ReadFloatBlock(STFReader.Units.None , null);}),
                new STFReader.TokenProcessor("trackshapeindex", ()=>
                {
                    TrackShapeIndex = stf.ReadIntBlock(-1);
                    InitializeOffsetsAndTrackNodes();
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
            outf.Write((int)MotionDirection);
            outf.Write(XPos);
            outf.Write(Connected);
            outf.Write(saveConnected);
            outf.Write(connectedTarget);
            outf.Write(TargetX);
        }


        /// <summary>
        /// Restores the general variable parameters
        /// Called from within the Simulator class.
        /// </summary>
        internal override void Restore(BinaryReader inf, Simulator simulator)
        {
            base.Restore(inf, simulator);
            MotionDirection = (MidpointDirection)inf.ReadInt32();
            XPos = inf.ReadSingle();
            Connected = inf.ReadBoolean();
            saveConnected = inf.ReadBoolean();
            connectedTarget = inf.ReadInt32();
            TargetX = inf.ReadSingle();
        }

        protected void InitializeOffsetsAndTrackNodes()
        {
            TrackShape trackShape = Simulator.Instance.TSectionDat.TrackShapes[(uint)TrackShapeIndex];
            uint nSections = trackShape.SectionIndices[0].SectionsCount;
            trackNodesIndex = new int[trackShape.SectionIndices.Length];
            trackNodesOrientation = new bool[trackNodesIndex.Length];
            trackVectorSectionsIndex = new int[trackNodesIndex.Length];
            int i = 0;
            foreach (SectionIndex sectionIdx in trackShape.SectionIndices)
            {
                offsets.Add(sectionIdx.Offset.X);
                trackNodesIndex[i] = -1;
                trackVectorSectionsIndex[i] = -1;
                i++;
            }
            TrackNode[] trackNodes = Simulator.Instance.TrackDatabase.TrackDB.TrackNodes;
            for (int j = 1; j < trackNodes.Length; j++)
            {
                if (trackNodes[j] is TrackVectorNode tvn && tvn.TrackVectorSections != null)
                {
                    int trackVectorSection = Array.FindIndex(tvn.TrackVectorSections, trVectorSection =>
                        (trVectorSection.Location.TileX == WorldPosition.TileX && trVectorSection.Location.TileZ == WorldPosition.TileZ && trVectorSection.WorldFileUiD == UID));
                    if (trackVectorSection >= 0)
                    {
                        if (tvn.TrackVectorSections.Length > (int)nSections)
                        {
                            i = tvn.TrackVectorSections[trackVectorSection].Flag1 / 2;
                            trackNodesIndex[i] = j;
                            trackVectorSectionsIndex[i] = trackVectorSection;
                            trackNodesOrientation[i] = tvn.TrackVectorSections[trackVectorSection].Flag1 % 2 == 0;

                        }
                    }
                }
            }
            XPos = offset.X;
            // Compute width of transfer table
            Width = trackShape.SectionIndices[^1].Offset.X - trackShape.SectionIndices[0].Offset.X;
        }

        /// <summary>
        /// Computes the nearest transfertable exit in the actual direction
        /// Returns the Y angle to be compared.
        /// </summary>
        public override void ComputeTarget(bool clockwise)
        {
            if (!ContinuousMotion)
                return;
            ContinuousMotion = false;
            GoToTarget = false;
            MotionDirection = clockwise ? MidpointDirection.Forward : MidpointDirection.Reverse;

            float offsetTarget = (int)MotionDirection * 1.4f;
            Connected = false;
            if (offsets.Count <= 0)
            {
                MotionDirection = MidpointDirection.N;
                connectedTarget = -1;
            }
            else
            {
                if (MotionDirection == MidpointDirection.Forward)
                {
                    for (int i = offsets.Count - 1; i >= 0; i--)
                    {
                        if (trackNodesIndex[i] != -1 && trackVectorSectionsIndex[i] != -1)
                        {
                            float offsetDiff = offsets[i] - XPos;
                            if (offsetDiff < offsetTarget && offsetDiff >= 0)
                            {
                                connectedTarget = i;
                                break;
                            }
                            else if (offsetDiff < 0)
                            {
                                MotionDirection = MidpointDirection.N;
                                connectedTarget = -1;
                                break;
                            }
                        }
                    }
                }
                else if (MotionDirection == MidpointDirection.Reverse)
                {
                    for (int i = 0; i <= offsets.Count - 1; i++)
                    {
                        if (trackNodesIndex[i] != -1 && trackVectorSectionsIndex[i] != -1)
                        {
                            float offsetDiff = offsets[i] - XPos;
                            if (offsetDiff > offsetTarget && offsetDiff <= 0)
                            {
                                connectedTarget = i;
                                break;
                            }
                            else if (offsetDiff > 0)
                            {
                                MotionDirection = MidpointDirection.N;
                                connectedTarget = -1;
                                break;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Starts continuous movement
        /// </summary>
        public override void StartContinuous(bool clockwise)
        {
            if (TrainsOnMovingTable.Count > 1 || (TrainsOnMovingTable.Count == 1 && TrainsOnMovingTable[0].FrontOnBoard ^ TrainsOnMovingTable[0].BackOnBoard))
            {
                MotionDirection = MidpointDirection.N;
                ContinuousMotion = false;
                Simulator.Instance.Confirmer.Warning(Simulator.Catalog.GetString("Train partially on transfertable, can't transfer"));
                return;
            }
            if (TrainsOnMovingTable.Count == 1 && TrainsOnMovingTable[0].FrontOnBoard && TrainsOnMovingTable[0].BackOnBoard)
            {
                // Preparing for transfer
                Train train = TrainsOnMovingTable[0].Train;
                if (Math.Abs(train.SpeedMpS) > 0.1 || (train.LeadLocomotiveIndex != -1 && (train.LeadLocomotive.ThrottlePercent >= 1 || !(train.LeadLocomotive.Direction == MidpointDirection.N
                    || Math.Abs(train.MUReverserPercent) <= 1))) || (train.ControlMode != TrainControlMode.Manual && train.ControlMode != TrainControlMode.TurnTable &&
                    train.ControlMode != TrainControlMode.Explorer && train.ControlMode != TrainControlMode.Undefined))
                {
                    Simulator.Instance.Confirmer.Warning(Simulator.Catalog.GetString("Transfer can't start: check throttle, speed, direction and control mode"));
                    return;
                }
                if (train.ControlMode == TrainControlMode.Manual || train.ControlMode == TrainControlMode.Explorer || train.ControlMode == TrainControlMode.Undefined)
                {
                    saveConnected = Connected ^ !trackNodesOrientation[ConnectedTrackEnd];
                    Matrix invAnimationXNAMatrix = Matrix.Invert(animationXNAMatrix);
                    relativeCarPositions = new List<Matrix>();
                    foreach (TrainCar trainCar in train.Cars)
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
                    train.ControlMode = TrainControlMode.TurnTable;
                }
                Simulator.Instance.Confirmer.Information(Simulator.Catalog.GetString("Transfertable starting transferring train"));
                // Computing position of cars relative to center of transfertable

            }
            MotionDirection = clockwise ? MidpointDirection.Forward : MidpointDirection.Reverse;
            ContinuousMotion = true;
        }

        public void ComputeCenter(in WorldPosition worldPosition)
        {
            Vector3 movingCenterOffset = CenterOffset;
            movingCenterOffset.X = XPos;
            VectorExtension.Transform(movingCenterOffset, worldPosition.XNAMatrix, out Vector3 originCoordinates);
            position = worldPosition.SetTranslation(originCoordinates.X, originCoordinates.Y, originCoordinates.Z);
        }

        public void TransferTrain(Matrix animationXNAMatrix)
        {
            if ((MotionDirection != MidpointDirection.N || GoToTarget) && TrainsOnMovingTable.Count == 1 && TrainsOnMovingTable[0].FrontOnBoard &&
                TrainsOnMovingTable[0].BackOnBoard && TrainsOnMovingTable[0].Train.ControlMode == TrainControlMode.TurnTable)
            {
                // Move together also train
                int relativeCarPositions = 0;
                foreach (TrainCar traincar in TrainsOnMovingTable[0].Train.Cars)
                {
                    traincar.WorldPosition = new WorldPosition(traincar.WorldPosition.TileX, traincar.WorldPosition.TileZ,
                        Matrix.Multiply(base.relativeCarPositions[relativeCarPositions], animationXNAMatrix));
                    relativeCarPositions++;
                }
            }
        }

        public override void Update()
        {
            foreach (TrainOnMovingTable trainOnMovingTable in TrainsOnMovingTable)
            {
                if (trainOnMovingTable.FrontOnBoard ^ trainOnMovingTable.BackOnBoard)
                {
                    MotionDirection = MidpointDirection.N;
                    ContinuousMotion = false;
                    return;
                }
            }

            if (ContinuousMotion)
            {
                Connected = false;
                ConnectedTrackEnd = -1;
                GoToTarget = false;
            }
            else
            {
                if (MotionDirection != MidpointDirection.N)
                {
                    Connected = false;
                    if (connectedTarget != -1)
                    {
                        if (Math.Abs(offsets[connectedTarget] - XPos) < 0.005)
                        {
                            Connected = true;
                            MotionDirection = MidpointDirection.N;
                            ConnectedTrackEnd = connectedTarget;
                            Simulator.Instance.Confirmer.Information(Simulator.Catalog.GetString("Transfertable connected"));
                            GoToTarget = true;
                            TargetX = offsets[connectedTarget];
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
            Traveller.TravellerDirection direction = Traveller.TravellerDirection.Forward;
            direction = saveConnected ^ !trackNodesOrientation[ConnectedTrackEnd] ? direction : Traveller.TravellerDirection.Backward;
            GoToTarget = false;
            if (TrainsOnMovingTable.Count == 1)
            {
                Train train = TrainsOnMovingTable[0].Train;
                if (train.ControlMode == TrainControlMode.TurnTable)
                    train.ReenterTrackSections(trackNodesIndex[ConnectedTrackEnd], finalFrontTravellerXNALocation, finalRearTravellerXNALocation, direction);
            }
        }

        /// <summary>
        /// CheckMovingTableAligned: checks if transfertable aligned with entering train
        /// </summary>
        public override bool CheckMovingTableAligned(Train train, bool forward)
        {
            if (null == train)
                throw new ArgumentNullException(nameof(train));
            return Connected && trackVectorSectionsIndex[ConnectedTrackEnd] != -1 && trackNodesIndex[ConnectedTrackEnd] != -1 &&
                (trackNodesIndex[ConnectedTrackEnd] == train.FrontTDBTraveller.TN.Index || trackNodesIndex[ConnectedTrackEnd] == train.RearTDBTraveller.TN.Index);
        }

        /// <summary>
        /// PerformUpdateActions: actions to be performed at every animation step
        /// </summary>
        public void PerformUpdateActions(Matrix absAnimationMatrix, in WorldPosition worldPosition)
        {
            TransferTrain(absAnimationMatrix);
            if (GoToTarget && TrainsOnMovingTable.Count == 1 && TrainsOnMovingTable[0].Train.ControlMode == TrainControlMode.TurnTable)
            {
                RecalculateTravellerXNALocations(absAnimationMatrix);
            }
            if (GoToTarget)
            {
                ComputeCenter(worldPosition);
                TargetExactlyReached();
            }
        }
    }

}
