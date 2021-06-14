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
using System.IO;

using Microsoft.Xna.Framework;

using Orts.Common;
using Orts.Common.Position;
using Orts.Common.Xna;
using Orts.Formats.Msts.Models;
using Orts.Formats.Msts.Parsers;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.Signalling;

namespace Orts.Simulation
{
    /// <summary>
    /// Reads file ORTSTurntables.dat and creates the instances of the turntables
    /// </summary>
    /// 



    public class Transfertable : MovingTable
    {
        public float Width;
        public List<float> Offsets = new List<float>();
        // Dynamic data
        public bool Forward; // forward motion on
        public bool Reverse; // reverse motion on
        public float XPos; // X Position of animated part, to be compared with X positions of endpoints
        public bool Connected = true; // Transfertable is connected to a track
        public bool SaveConnected = true; // Transfertable is connected to a track
        public int ConnectedTarget = -1; // index of trackend connected
        public float TargetX; //final target for Viewer;

        public SignalEnvironment signalRef { get; protected set; }

        public Transfertable(STFReader stf, Simulator simulator): base(stf, simulator)
        {
            signalRef = Simulator.SignalEnvironment;
            string animation;
            Matrix position = Matrix.Identity;
            position.M44 = 100000000; //WorlPosition not yet defined, will be loaded when loading related tile;
            stf.MustMatch("(");
            stf.ParseBlock(new[] {
                new STFReader.TokenProcessor("wfile", () => {
                    WFile = stf.ReadStringBlock(null);
                    WorldPosition = new WorldPosition(int.Parse(WFile.Substring(1, 7)), int.Parse(WFile.Substring(8, 7)), position);
                }),
                new STFReader.TokenProcessor("uid", ()=>{ UID = stf.ReadIntBlock(-1); }),
                new STFReader.TokenProcessor("animation", ()=>{ animation = stf.ReadStringBlock(null);
                                                                Animations.Add(animation.ToLower());}),
                new STFReader.TokenProcessor("length", ()=>{ Length = stf.ReadFloatBlock(STFReader.Units.None , null);}),
                new STFReader.TokenProcessor("xoffset", ()=>{ CenterOffset.X = stf.ReadFloatBlock(STFReader.Units.None , null);}),
                new STFReader.TokenProcessor("zoffset", ()=>{ CenterOffset.Z = -stf.ReadFloatBlock(STFReader.Units.None , null);}),
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
        public override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            outf.Write(Forward);
            outf.Write(Reverse);
            outf.Write(XPos);
            outf.Write(Connected);
            outf.Write(SaveConnected);
            outf.Write(ConnectedTarget);
            outf.Write(TargetX);
        }


        /// <summary>
        /// Restores the general variable parameters
        /// Called from within the Simulator class.
        /// </summary>
        public override void Restore(BinaryReader inf, Simulator simulator)
        {
            base.Restore(inf, simulator);
            Forward = inf.ReadBoolean();
            Reverse = inf.ReadBoolean();
            XPos = inf.ReadSingle();
            Connected = inf.ReadBoolean();
            SaveConnected = inf.ReadBoolean();
            ConnectedTarget = inf.ReadInt32();
            TargetX = inf.ReadSingle();
        }

        protected void InitializeOffsetsAndTrackNodes()
        {
            var trackShape = Simulator.TSectionDat.TrackShapes[(uint)TrackShapeIndex];
            var nSections = trackShape.SectionIndices[0].SectionsCount;
            MyTrackNodesIndex = new int[trackShape.SectionIndices.Length];
            MyTrackNodesOrientation = new bool[MyTrackNodesIndex.Length];
            MyTrVectorSectionsIndex = new int[MyTrackNodesIndex.Length];
            var iMyTrackNodes = 0;
            foreach (var sectionIdx in trackShape.SectionIndices)
            {
                Offsets.Add(sectionIdx.Offset.X);
                MyTrackNodesIndex[iMyTrackNodes] = -1;
                MyTrVectorSectionsIndex[iMyTrackNodes] = -1;
                iMyTrackNodes++;
            }
            var trackNodes = Simulator.TDB.TrackDB.TrackNodes;
            int iTrackNode = 0;
            for (iTrackNode = 1; iTrackNode < trackNodes.Length; iTrackNode++)
            {
                if (trackNodes[iTrackNode] is TrackVectorNode tvn && tvn.TrackVectorSections != null)
                {
                    var iTrVectorSection = Array.FindIndex(tvn.TrackVectorSections, trVectorSection =>
                        (trVectorSection.Location.TileX == WorldPosition.TileX && trVectorSection.Location.TileZ == WorldPosition.TileZ && trVectorSection.WorldFileUiD == UID));
                    if (iTrVectorSection >= 0)
                    {
                        if (tvn.TrackVectorSections.Length > (int)nSections)
                        {
                            iMyTrackNodes = tvn.TrackVectorSections[iTrVectorSection].Flag1 / 2;
                            MyTrackNodesIndex[iMyTrackNodes] = iTrackNode;
                            MyTrVectorSectionsIndex[iMyTrackNodes] = iTrVectorSection;
                            MyTrackNodesOrientation[iMyTrackNodes] = tvn.TrackVectorSections[iTrVectorSection].Flag1 % 2 == 0 ? true : false;

                        }
                    }
                }
            }
            XPos = CenterOffset.X;
            // Compute width of transfer table
            Width = trackShape.SectionIndices[trackShape.SectionIndices.Length - 1].Offset.X - trackShape.SectionIndices[0].Offset.X;
        }

        /// <summary>
        /// Computes the nearest transfertable exit in the actual direction
        /// Returns the Y angle to be compared.
        /// </summary>
        public override void ComputeTarget(bool clockwise)
        {
            if (!Continuous) return;
            Continuous = false;
            GoToTarget = false;
            Forward = clockwise;
            Reverse = !clockwise;
            if (Forward)
            {
                var offsetDiff = 1.4f;
                Connected = false;
                if (Offsets.Count <= 0)
                {
                    Forward = false;
                    ConnectedTarget = -1;
                }
                else
                {
                    for (int iOffset = Offsets.Count - 1; iOffset >= 0; iOffset--)
                    {
                        if (MyTrackNodesIndex[iOffset] != -1 && MyTrVectorSectionsIndex[iOffset] != -1)
                        {
                            var thisOffsetDiff = Offsets[iOffset] - XPos;
                            if (thisOffsetDiff < offsetDiff && thisOffsetDiff >= 0)
                            {
                                ConnectedTarget = iOffset;
                                break;
                            }
                            else if (thisOffsetDiff < 0)
                            {
                                Forward = false;
                                ConnectedTarget = -1;
                                break;
                            }
                        }
                    }
                }
            }
            else if (Reverse)
            {
                var offsetDiff = -1.4f;
                Connected = false;
                if (Offsets.Count <= 0)
                {
                    Reverse = false;
                    ConnectedTarget = -1;
                }
                else
                {
                    for (int iOffset = 0; iOffset <= Offsets.Count - 1; iOffset++)
                    {
                        if (MyTrackNodesIndex[iOffset] != -1 && MyTrVectorSectionsIndex[iOffset] != -1)
                        {
                            var thisOffsetDiff = Offsets[iOffset] - XPos;
                            if (thisOffsetDiff > offsetDiff && thisOffsetDiff <= 0)
                            {
                                ConnectedTarget = iOffset;
                                break;
                            }
                            else if (thisOffsetDiff > 0)
                            {
                                Reverse = false;
                                ConnectedTarget = -1;
                                break;
                            }
                        }
                    }
                }

            }
            return;
        }

        /// <summary>
        /// Starts continuous movement
        /// 
        /// </summary>
        /// 
        public override void StartContinuous(bool clockwise)
        {
            if (TrainsOnMovingTable.Count > 1 || (TrainsOnMovingTable.Count == 1 && TrainsOnMovingTable[0].FrontOnBoard ^ TrainsOnMovingTable[0].BackOnBoard))
            {
                Forward = false;
                Reverse = false;
                Continuous = false;
                Simulator.Confirmer.Warning(Simulator.Catalog.GetString("Train partially on transfertable, can't transfer"));
                return;
            }
            if (TrainsOnMovingTable.Count == 1 && TrainsOnMovingTable[0].FrontOnBoard && TrainsOnMovingTable[0].BackOnBoard)
            {
                // Preparing for transfer
                var train = TrainsOnMovingTable[0].Train;
                if (Math.Abs(train.SpeedMpS) > 0.1 || (train.LeadLocomotiveIndex != -1 && (train.LeadLocomotive.ThrottlePercent >= 1 || !(train.LeadLocomotive.Direction == MidpointDirection.N 
                 || Math.Abs(train.MUReverserPercent) <= 1))) || (train.ControlMode != TrainControlMode.Manual && train.ControlMode != TrainControlMode.TurnTable &&
                 train.ControlMode != TrainControlMode.Explorer && train.ControlMode != TrainControlMode.Undefined))
                {
                    Simulator.Confirmer.Warning(Simulator.Catalog.GetString("Transfer can't start: check throttle, speed, direction and control mode"));
                    return;
                }
                if (train.ControlMode == TrainControlMode.Manual || train.ControlMode == TrainControlMode.Explorer || train.ControlMode == TrainControlMode.Undefined)
                {
                    SaveConnected = Connected ^ !MyTrackNodesOrientation[ConnectedTrackEnd];
                    var invAnimationXNAMatrix = Matrix.Invert(AnimationXNAMatrix);
                    RelativeCarPositions = new List<Matrix>();
                    foreach (TrainCar trainCar in train.Cars)
                    {
                        var relativeCarPosition = Matrix.Identity;
                        trainCar.WorldPosition = trainCar.WorldPosition.NormalizeTo(WorldPosition.TileX, WorldPosition.TileZ);
                        relativeCarPosition = Matrix.Multiply(trainCar.WorldPosition.XNAMatrix, invAnimationXNAMatrix);
                        RelativeCarPositions.Add(relativeCarPosition);
                    }
                    var XNALocation = train.FrontTDBTraveller.Location;
                    XNALocation.Z = -XNALocation.Z;
                    XNALocation.X = XNALocation.X + 2048 * (train.FrontTDBTraveller.TileX - WorldPosition.TileX);
                    XNALocation.Z = XNALocation.Z - 2048 * (train.FrontTDBTraveller.TileZ - WorldPosition.TileZ);
                    RelativeFrontTravellerXNALocation = Vector3.Transform(XNALocation, invAnimationXNAMatrix);
                    XNALocation = train.RearTDBTraveller.Location;
                    XNALocation.Z = -XNALocation.Z;
                    XNALocation.X = XNALocation.X + 2048 * (train.RearTDBTraveller.TileX - WorldPosition.TileX);
                    XNALocation.Z = XNALocation.Z - 2048 * (train.RearTDBTraveller.TileZ - WorldPosition.TileZ);
                    RelativeRearTravellerXNALocation = Vector3.Transform(XNALocation, invAnimationXNAMatrix);
                    train.ControlMode = TrainControlMode.TurnTable;
                }
                Simulator.Confirmer.Information (Simulator.Catalog.GetString("Transfertable starting transferring train"));
                // Computing position of cars relative to center of transfertable

             }
             Forward = clockwise;
             Reverse = !clockwise;
             Continuous = true;
        }

        public void ComputeCenter(in WorldPosition worldPosition)
        {
            Vector3 movingCenterOffset = CenterOffset;
            movingCenterOffset.X = XPos;
            VectorExtension.Transform(movingCenterOffset, worldPosition.XNAMatrix, out Vector3 originCoordinates);
            WorldPosition = worldPosition.SetTranslation(originCoordinates.X, originCoordinates.Y, originCoordinates.Z);
        }

        public void TransferTrain(Matrix animationXNAMatrix)
        {
            AnimationXNAMatrix = animationXNAMatrix;
            if ((Forward || Reverse || GoToTarget) && TrainsOnMovingTable.Count == 1 && TrainsOnMovingTable[0].FrontOnBoard &&
                TrainsOnMovingTable[0].BackOnBoard && TrainsOnMovingTable[0].Train.ControlMode == TrainControlMode.TurnTable)
            {
                // Move together also train
                var iRelativeCarPositions = 0;
                foreach (TrainCar traincar in TrainsOnMovingTable[0].Train.Cars)
                {
                    traincar.WorldPosition = new WorldPosition(traincar.WorldPosition.TileX, traincar.WorldPosition.TileZ, 
                        Matrix.Multiply(RelativeCarPositions[iRelativeCarPositions], AnimationXNAMatrix));
                    iRelativeCarPositions++;
                }
            }
        }

        public override void Update()
        {
            foreach (var trainOnMovingTable in TrainsOnMovingTable)
                if (trainOnMovingTable.FrontOnBoard ^ trainOnMovingTable.BackOnBoard)
                {
                    Forward = false;
                    Reverse = false;
                    Continuous = false;
                    return;
                }
            if (Continuous)
            {
                Connected = false;
                ConnectedTrackEnd = -1;
                GoToTarget = false;
            }
            else
            {
                if (Forward)
                {
                    Connected = false;
                    if (ConnectedTarget != -1)
                    {
                        if (Offsets[ConnectedTarget] - XPos < 0.005)
                        {
                            Connected = true;
                            Forward = false;
                            ConnectedTrackEnd = ConnectedTarget;
                            Simulator.Confirmer.Information (Simulator.Catalog.GetString("Transfertable connected"));
                            GoToTarget = true;
                            TargetX = Offsets[ConnectedTarget];
                        }
                    }
                 }
                else if (Reverse)
                {
                    Connected = false;
                    if (ConnectedTarget != -1)
                    {
                        if (XPos - Offsets[ConnectedTarget] < 0.005)
                        {
                            Connected = true;
                            Reverse = false;
                            ConnectedTrackEnd = ConnectedTarget;
                            Simulator.Confirmer.Information(Simulator.Catalog.GetString("Transfertable connected"));
                            GoToTarget = true;
                            TargetX = Offsets[ConnectedTarget];
                        }
                    }
                }
            }
        }

        /// <summary>
        /// TargetExactlyReached: if train on board, it can exit the turntable
        /// </summary>
        /// 
        public void TargetExactlyReached()
        {
            Traveller.TravellerDirection direction = Traveller.TravellerDirection.Forward;
            direction = SaveConnected ^ !MyTrackNodesOrientation[ConnectedTrackEnd]? direction : (direction == Traveller.TravellerDirection.Forward ? Traveller.TravellerDirection.Backward : Traveller.TravellerDirection.Forward);
            GoToTarget = false;
            if (TrainsOnMovingTable.Count == 1)
            {
                var train = TrainsOnMovingTable[0].Train;
                if (train.ControlMode == TrainControlMode.TurnTable)
                    train.ReenterTrackSections(MyTrackNodesIndex[ConnectedTrackEnd], FinalFrontTravellerXNALocation, FinalRearTravellerXNALocation, direction);
            }
        }

        /// <summary>
        /// CheckMovingTableAligned: checks if transfertable aligned with entering train
        /// </summary>
        /// 

        public override bool CheckMovingTableAligned(Train train, bool forward)
        {
            if ((Connected) && MyTrVectorSectionsIndex[ConnectedTrackEnd] != -1 && MyTrackNodesIndex[ConnectedTrackEnd] != -1 &&
                (MyTrackNodesIndex[ConnectedTrackEnd] == train.FrontTDBTraveller.TN.Index || MyTrackNodesIndex[ConnectedTrackEnd] == train.RearTDBTraveller.TN.Index))
            {
            return true;
            }
            return false;
        }

        /// <summary>
        /// PerformUpdateActions: actions to be performed at every animation step
        /// </summary>
        /// 
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
