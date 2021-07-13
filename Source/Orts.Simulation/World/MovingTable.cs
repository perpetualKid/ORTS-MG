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
using System.Diagnostics;
using System.IO;

using Microsoft.Xna.Framework;

using Orts.Common;
using Orts.Common.Position;
using Orts.Formats.Msts.Parsers;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;

namespace Orts.Simulation.World
{
    /// <summary>
    /// Reads file ORTSTurntables.dat and creates the instances of turntables and transfertables
    /// </summary>
    public static class MovingTableFile
    {
        public static IEnumerable<MovingTable> ReadTurntableFile(string filePath)
        {
            IList<MovingTable> result = new List<MovingTable>();
            if (!File.Exists(filePath))
                return result;

            Trace.Write(" TURNTBL");

            using (STFReader stf = new STFReader(filePath, false))
            {
                int count = stf.ReadInt(null);
                stf.ParseBlock(new STFReader.TokenProcessor[] {
                    new STFReader.TokenProcessor("turntable", ()=>{
                        if (--count < 0)
                            STFException.TraceWarning(stf, "Skipped extra Turntable");
                        else
                            result.Add(new TurnTable(stf));
                    }),
                    new STFReader.TokenProcessor("transfertable", ()=>{
                        if (--count < 0)
                            STFException.TraceWarning(stf, "Skipped extra Transfertable");
                        else
                            result.Add(new TransferTable(stf));
                    }),
                });
                if (count > 0)
                    STFException.TraceWarning(stf, count + " missing Turntable(s)");
            }

            return result;
        }
    }

    public abstract class MovingTable
    {
        // Fixed data
        public string WFile { get; protected set; }
        public int UID { get; protected set; }
        public float Length { get; protected set; }

        private protected int[] trackNodesIndex;
        private protected int[] trackVectorSectionsIndex;
        private protected bool[] trackNodesOrientation; // true if forward, false if backward

        public int TrackShapeIndex { get; protected set; }

        private protected WorldPosition position = WorldPosition.None;
        // Dynamic data
        public ref readonly WorldPosition WorldPosition => ref position;
        public IList<string> Animations { get; } = new List<string>();
        private protected Vector3 offset;
        public ref readonly Vector3 CenterOffset => ref offset; // shape offset of center of moving table;
        public bool ContinuousMotion { get; protected set; } // continuous motion on
        public int ConnectedTrackEnd { get; protected set; }  // 
        public bool GoToTarget { get; set; } //TODO 2021-06-21 should be protected set but TTTurnTable needs this
        public bool GoToAutoTarget { get; set; }
        public int? TurntableFrameRate { get; set; }
        public bool SendNotifications { get; set; } = true;      // send simulator confirmations
        public bool InUse { get; set; }                  // turntable is in use (used in auto mode for timetable)
        public Queue<int> WaitingTrains { get; } = new Queue<int>();    // Queue of trains waiting to access table

        // additions to manage rotation or transfer of wagons
        public IList<TrainOnMovingTable> TrainsOnMovingTable { get; } = new List<TrainOnMovingTable>(); // List of trains on turntable or transfertable
        private protected Matrix animationXNAMatrix = Matrix.Identity;
        private protected List<Matrix> relativeCarPositions;
        private protected Vector3 relativeFrontTravellerXNALocation;
        private protected Vector3 relativeRearTravellerXNALocation;
        private protected Vector3 finalFrontTravellerXNALocation;
        private protected Vector3 finalRearTravellerXNALocation;

        internal virtual void Save(BinaryWriter outf)
        {
            outf.Write(ContinuousMotion);
            outf.Write(GoToTarget);
            outf.Write(GoToAutoTarget);
            outf.Write(TurntableFrameRate.HasValue);
            if (TurntableFrameRate.HasValue)
                outf.Write(TurntableFrameRate.Value);
            outf.Write(ConnectedTrackEnd);
            outf.Write(SendNotifications);
            outf.Write(InUse);
            SaveVector(outf, relativeFrontTravellerXNALocation);
            SaveVector(outf, relativeRearTravellerXNALocation);
            SaveVector(outf, finalFrontTravellerXNALocation);
            SaveVector(outf, finalRearTravellerXNALocation);
            outf.Write(TrainsOnMovingTable.Count);
            foreach (TrainOnMovingTable trainOnMovingTable in TrainsOnMovingTable)
                trainOnMovingTable.Save(outf);
            outf.Write(WaitingTrains.Count);
            foreach (int waitingTrain in WaitingTrains)
                outf.Write(waitingTrain);
        }


        private static void SaveVector(BinaryWriter outf, Vector3 vector)
        {
            outf.Write(vector.X);
            outf.Write(vector.Y);
            outf.Write(vector.Z);
        }

        /// <summary>
        /// Restores the general variable parameters
        /// Called from within the Simulator class.
        /// </summary>
        internal virtual void Restore(BinaryReader inf, Simulator simulator)
        {
            ContinuousMotion = inf.ReadBoolean();
            GoToTarget = inf.ReadBoolean();
            GoToAutoTarget = inf.ReadBoolean();
            TurntableFrameRate = null;
            if (inf.ReadBoolean())
                TurntableFrameRate = inf.ReadInt32();
            ConnectedTrackEnd = inf.ReadInt32();
            SendNotifications = inf.ReadBoolean();
            InUse = inf.ReadBoolean();
            relativeFrontTravellerXNALocation = RestoreVector(inf);
            relativeRearTravellerXNALocation = RestoreVector(inf);
            finalFrontTravellerXNALocation = RestoreVector(inf);
            finalRearTravellerXNALocation = RestoreVector(inf);
            int trainsOnMovingTable = inf.ReadInt32();
            while (trainsOnMovingTable > 0)
            {
                TrainOnMovingTable trainOnMovingTable = new TrainOnMovingTable(null);
                trainOnMovingTable.Restore(inf);
                trainsOnMovingTable--;
                TrainsOnMovingTable.Add(trainOnMovingTable);
            }

            int trainsWaiting = inf.ReadInt32();
            for (int waitingTrain = 0; waitingTrain < trainsWaiting - 1; waitingTrain++)
                WaitingTrains.Enqueue(waitingTrain);
        }

        private static Vector3 RestoreVector(BinaryReader inf)
        {
            return new Vector3(inf.ReadSingle(), inf.ReadSingle(), inf.ReadSingle());
        }

        public virtual void Update()
        {

        }

        public virtual bool CheckMovingTableAligned(Train train, bool forward)
        {
            return false;
        }

        /// <summary>
        /// CheckTrainOnTurntable: checks if actual player train is on turntable
        /// </summary>
        public bool CheckTrainOnMovingTable(Train train)
        {
            if (train == null)
                return false;
            string tableType = this is TurnTable ? Simulator.Catalog.GetString("turntable") : Simulator.Catalog.GetString("transfertable");
            int trainIndex = (TrainsOnMovingTable as List<TrainOnMovingTable>)?.FindIndex(x => x.Train.Number == train.Number) ?? -1;
            if (WorldLocation.Within(train.FrontTDBTraveller.WorldLocation, WorldPosition.WorldLocation, Length / 2))
            {
                if (trainIndex == -1 || !TrainsOnMovingTable[trainIndex].FrontOnBoard)
                {
                    if (trainIndex == -1)
                    {
                        TrainOnMovingTable trainOnTurntable = new TrainOnMovingTable(train);
                        trainIndex = TrainsOnMovingTable.Count;
                        TrainsOnMovingTable.Add(trainOnTurntable);
                    }
                    if (!TrainsOnMovingTable[trainIndex].BackOnBoard)
                    {
                        // check if turntable aligned with train
                        bool aligned = CheckMovingTableAligned(train, true);
                        if (!aligned)
                        {
                            TrainsOnMovingTable[trainIndex].SetFrontState(true);
                            Simulator.Instance.Confirmer.Warning(Simulator.Catalog.GetString("Train slipped into non aligned {0}", tableType));
                            train.SetTrainOutOfControl(OutOfControlReason.SlippedIntoTurnTable);
                            train.SpeedMpS = 0;
                            foreach (TrainCar car in train.Cars)
                                car.SpeedMpS = 0;
                            return false;
                        }
                    }
                    if (SendNotifications)
                        Simulator.Instance.Confirmer.Information(Simulator.Catalog.GetString("Train front on {0}", tableType));
                }
                TrainsOnMovingTable[trainIndex].SetFrontState(true);
            }
            else
                if (trainIndex != -1 && TrainsOnMovingTable[trainIndex].FrontOnBoard)
            {
                if (SendNotifications) 
                    Simulator.Instance.Confirmer.Information(Simulator.Catalog.GetString("Train front outside {0}", tableType));
                if (TrainsOnMovingTable[trainIndex].BackOnBoard) 
                    TrainsOnMovingTable[trainIndex].SetFrontState(false);
                else
                {
                    TrainsOnMovingTable.RemoveAt(trainIndex);
                    trainIndex = -1;
                }
            }
            if (WorldLocation.Within(train.RearTDBTraveller.WorldLocation, WorldPosition.WorldLocation, Length / 2))
            {
                if (trainIndex == -1 || !TrainsOnMovingTable[trainIndex].BackOnBoard)
                {
                    if (trainIndex == -1)
                    {
                        TrainOnMovingTable trainOnTurntable = new TrainOnMovingTable(train);
                        trainIndex = TrainsOnMovingTable.Count;
                        TrainsOnMovingTable.Add(trainOnTurntable);
                    }
                    if (!TrainsOnMovingTable[trainIndex].FrontOnBoard)
                    {
                        // check if turntable aligned with train
                        bool aligned = CheckMovingTableAligned(train, false);
                        if (!aligned)
                        {
                            TrainsOnMovingTable[trainIndex].SetBackState(true);
                            Simulator.Instance.Confirmer.Warning(Simulator.Catalog.GetString("Train slipped into non aligned {0}", tableType));
                            train.SetTrainOutOfControl(OutOfControlReason.SlippedIntoTurnTable);
                            train.SpeedMpS = 0;
                            foreach (TrainCar car in train.Cars) 
                                car.SpeedMpS = 0;
                            return false;
                        }
                    }
                    Simulator.Instance.Confirmer.Information(Simulator.Catalog.GetString("Train rear on {0}", tableType));
                }
                TrainsOnMovingTable[trainIndex].SetBackState(true);
            }
            else
                if (trainIndex != -1 && TrainsOnMovingTable[trainIndex].BackOnBoard)
            {
                if (SendNotifications) 
                    Simulator.Instance.Confirmer.Information(Simulator.Catalog.GetString("Train rear outside {0}", tableType));
                if (TrainsOnMovingTable[trainIndex].FrontOnBoard) 
                    TrainsOnMovingTable[trainIndex].SetBackState(false);
                else
                {
                    TrainsOnMovingTable.RemoveAt(trainIndex);
                    trainIndex = -1;
                }
            }
            if (Simulator.Instance.ActivityRun != null && !train.IsPathless && train.TrainType != TrainType.Static && trainIndex != -1 &&
                TrainsOnMovingTable[trainIndex].FrontOnBoard && TrainsOnMovingTable[trainIndex].BackOnBoard && train.SpeedMpS <= 0.1f && train.ControlMode != TrainControlMode.Manual &&
                train.TCRoute.ActiveSubPath == train.TCRoute.TCRouteSubpaths.Count - 1 && train.TCRoute.TCRouteSubpaths[train.TCRoute.ActiveSubPath].Count > 1 &&
                (train.PresentPosition[Direction.Forward].RouteListIndex == train.TCRoute.TCRouteSubpaths[train.TCRoute.ActiveSubPath].Count - 2 ||
                train.PresentPosition[Direction.Backward].RouteListIndex == train.TCRoute.TCRouteSubpaths[train.TCRoute.ActiveSubPath].Count - 2))
                train.IsPathless = true;
            return false;
        }

        public virtual void StartContinuous(bool clockwise)
        {

        }

        public virtual void ComputeTarget(bool clockwise)
        {

        }

        public void ReInitTrainPositions(Matrix animationXNAMatrix)
        {
            this.animationXNAMatrix = animationXNAMatrix;
            if (this == Simulator.Instance.ActiveMovingTable && TrainsOnMovingTable.Count == 1)
            {
                Train train = TrainsOnMovingTable[0].Train;
                if (TrainsOnMovingTable[0].FrontOnBoard && TrainsOnMovingTable[0].BackOnBoard && Math.Abs(train.SpeedMpS) < 0.1)
                {
                    Matrix invAnimationXNAMatrix = Matrix.Invert(this.animationXNAMatrix);
                    relativeCarPositions = new List<Matrix>();
                    foreach (TrainCar trainCar in train.Cars)
                    {
                        trainCar.WorldPosition = trainCar.WorldPosition.NormalizeTo(WorldPosition.TileX, WorldPosition.TileZ);
                        Matrix relativeCarPosition = Matrix.Multiply(trainCar.WorldPosition.XNAMatrix, invAnimationXNAMatrix);
                        relativeCarPositions.Add(relativeCarPosition);
                    }

                }
            }
        }

        public void RecalculateTravellerXNALocations(Matrix animationXNAMatrix)
        {
            finalFrontTravellerXNALocation = Vector3.Transform(relativeFrontTravellerXNALocation, animationXNAMatrix);
            finalRearTravellerXNALocation = Vector3.Transform(relativeRearTravellerXNALocation, animationXNAMatrix);
        }

        public int FindExitNode(int trackNodeIndex)
        {
            for (int i = 0; i < trackNodesIndex.Length; i++)
            {
                if (trackNodesIndex[i] == trackNodeIndex)
                {
                    return i;
                }
            }
            return -1;
        }

        public bool TrackNodeOrientation(int trackNodeIndex)
        {
            return trackNodesOrientation[trackNodeIndex];
        }
    }

    public class TrainOnMovingTable
    {
        public Train Train { get; private set; }
        public bool FrontOnBoard { get; private set; }
        public bool BackOnBoard { get; private set; }

        public TrainOnMovingTable(Train train)
        {
            Train = train;
        }

        internal void Save(BinaryWriter outf)
        {
            outf.Write(Train.Number);
            outf.Write(FrontOnBoard);
            outf.Write(BackOnBoard);
        }

        internal void Restore(BinaryReader inf, Train train = null)
        {
            Train = Simulator.Instance.Trains.GetTrainByNumber(inf.ReadInt32());
            Train = train ?? Train;
            FrontOnBoard = inf.ReadBoolean();
            BackOnBoard = inf.ReadBoolean();
        }

        public void SetFrontState(bool frontOnBoard)
        {
            FrontOnBoard = frontOnBoard;
        }

        public void SetBackState(bool backOnBoard)
        {
            BackOnBoard = backOnBoard;
        }
    }
}
