using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Orts.Common;
using Orts.Common.Calc;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.Timetables;

namespace Orts.Simulation.Signalling
{
    //================================================================================================//
    /// <summary>
    /// StationStop class
    /// Class to hold information on station stops
    /// <\summary>

#pragma warning disable CA1036 // Override methods on comparable types
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    public class StationStop : IComparable<StationStop>
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning restore CA1036 // Override methods on comparable types
    {
        // variables for activity mode only
        private const int NumSecPerPass = 10; // number of seconds to board of a passengers
        private const int DefaultFreightStopTime = 20; // MSTS stoptime for freight trains

        // common variables
        public StationStopType StopType { get; }

        public int PlatformReference { get; }
        public PlatformDetails PlatformItem { get; }

        internal int SubrouteIndex { get; set; }
        internal int RouteIndex { get; set; }
        internal int TrackCircuitSectionIndex { get; set; }
        internal TrackDirection Direction { get; }
        internal int ExitSignal { get; set; }
        internal bool HoldSignal { get; set; }
        internal bool NoWaitSignal { get; set; }
        internal bool CallOnAllowed { get; set; }
        internal bool NoClaimAllowed { get; set; }
        internal float StopOffset { get; set; }
        public float DistanceToTrainM { get; internal set; }
        public int ArrivalTime { get; internal set; }
        public int DepartTime { get; internal set; }
        public int ActualArrival { get; internal set; }
        public int ActualDepart { get; internal set; }
        internal bool Passed { get; set; }

        // variables for timetable mode only
        internal bool Terminal { get; set; }                                            // station is terminal - train will run to end of platform
        internal int? ActualMinStopTime { get; set; }                                   // actual minimum stop time
        internal float? KeepClearFront { get; set; }                                    // distance to be kept clear ahead of train
        internal float? KeepClearRear { get; set; }                                     // distance to be kept clear behind train
        internal bool ForcePosition { get; set; }                                       // front or rear clear position must be forced
        internal bool CloseupSignal { get; set; }                                       // train may close up to signal within normal clearing distance
        internal bool Closeup { get; set; }                                             // train may close up to other train in platform
        internal bool RestrictPlatformToSignal { get; set; }                            // restrict end of platform to signal position
        internal bool ExtendPlatformToSignal { get; set; }                              // extend end of platform to next signal position
        internal bool EndStop { get; set; }                                             // train terminates at station
        internal List<int> ConnectionsWaiting { get; private set; }                     // List of trains waiting
        internal Dictionary<int, int> ConnectionsAwaited { get; private set; }          // List of awaited trains : key = trainno., value = arr time
        internal Dictionary<int, WaitInfo> ConnectionDetails { get; private set; }      // Details of connection : key = trainno., value = wait info

        // Constructor
        public StationStop(int platformReference, PlatformDetails platformItem, int subrouteIndex, int routeIndex,
            int tcSectionIndex, TrackDirection direction, int exitSignal, bool holdSignal, bool noWaitSignal, bool noClaimAllowed, float stopOffset,
            int arrivalTime, int departTime, bool terminal, int? actualMinStopTime, float? keepClearFront, float? keepClearRear,
            bool forcePosition, bool closeupSignal, bool closeup,
            bool restrictPlatformToSignal, bool extendPlatformToSignal, bool endStop, StationStopType actualStopType)
        {
            StopType = actualStopType;
            PlatformReference = platformReference;
            PlatformItem = platformItem;
            SubrouteIndex = subrouteIndex;
            RouteIndex = routeIndex;
            TrackCircuitSectionIndex = tcSectionIndex;
            Direction = direction;
            ExitSignal = exitSignal;
            HoldSignal = holdSignal;
            NoWaitSignal = noWaitSignal;
            NoClaimAllowed = noClaimAllowed;
            StopOffset = stopOffset;
            if (actualStopType == StationStopType.Station)
            {
                ArrivalTime = Math.Max(0, arrivalTime);
                DepartTime = Math.Max(0, departTime);
            }
            else
            // times may be <0 for waiting point
            {
                ArrivalTime = arrivalTime;
                DepartTime = departTime;
            }
            ActualArrival = -1;
            ActualDepart = -1;
            DistanceToTrainM = 9999999f;
            Passed = false;

            Terminal = terminal;
            ActualMinStopTime = actualMinStopTime;
            KeepClearFront = keepClearFront;
            KeepClearRear = keepClearRear;
            ForcePosition = forcePosition;
            CloseupSignal = closeupSignal;
            Closeup = closeup;
            RestrictPlatformToSignal = restrictPlatformToSignal;
            ExtendPlatformToSignal = extendPlatformToSignal;
            EndStop = endStop;

            CallOnAllowed = false;
        }

        public StationStop(int trackcircuitSectionIndex, TrackDirection direction, int exitSignal, bool holdSignal, float stopOffset, int routeIndex)
        {
            TrackCircuitSectionIndex = trackcircuitSectionIndex;
            Direction = direction;
            ExitSignal = exitSignal;
            HoldSignal = holdSignal;
            StopOffset = stopOffset;
            RouteIndex = routeIndex;
        }

        //================================================================================================//
        //
        // Restore
        //

        public StationStop(BinaryReader inf)
        {
            if (null == inf)
                throw new ArgumentNullException(nameof(inf));

            StopType = (StationStopType)inf.ReadInt32();
            PlatformReference = inf.ReadInt32();

            if (PlatformReference >= 0)
            {
                if (Simulator.Instance.SignalEnvironment.PlatformXRefList.TryGetValue(PlatformReference, out int platformIndex))
                {
                    PlatformItem = Simulator.Instance.SignalEnvironment.PlatformDetailsList[platformIndex];
                }
                else
                {
                    Trace.TraceInformation("Cannot find platform {0}", PlatformReference);
                }
            }
            else
            {
                PlatformItem = null;
            }

            SubrouteIndex = inf.ReadInt32();
            RouteIndex = inf.ReadInt32();
            TrackCircuitSectionIndex = inf.ReadInt32();
            Direction = (TrackDirection)inf.ReadInt32();
            ExitSignal = inf.ReadInt32();
            HoldSignal = inf.ReadBoolean();
            NoWaitSignal = inf.ReadBoolean();
            NoClaimAllowed = inf.ReadBoolean();
            CallOnAllowed = inf.ReadBoolean();
            StopOffset = inf.ReadSingle();
            ArrivalTime = inf.ReadInt32();
            DepartTime = inf.ReadInt32();
            ActualArrival = inf.ReadInt32();
            ActualDepart = inf.ReadInt32();
            DistanceToTrainM = 9999999f;
            Passed = inf.ReadBoolean();

            int totalConnectionsWaiting = inf.ReadInt32();
            if (totalConnectionsWaiting > 0)
            {
                ConnectionsWaiting = new List<int>();
                for (int i = 0; i <= totalConnectionsWaiting - 1; i++)
                {
                    ConnectionsWaiting.Add(inf.ReadInt32());
                }
            }

            int totalConnectionsAwaited = inf.ReadInt32();
            if (totalConnectionsAwaited > 0)
            {
                ConnectionsAwaited = new Dictionary<int, int>();
                for (int i = 0; i <= totalConnectionsAwaited - 1; i++)
                {
                    ConnectionsAwaited.Add(inf.ReadInt32(), inf.ReadInt32());
                }
            }

            int totalConnectionDetails = inf.ReadInt32();
            if (totalConnectionDetails > 0)
            {
                ConnectionDetails = new Dictionary<int, WaitInfo>();
                for (int i = 0; i <= totalConnectionDetails - 1; i++)
                {
                    ConnectionDetails.Add(inf.ReadInt32(), new WaitInfo(inf));
                }
            }

            if (inf.ReadBoolean())
            {
                ActualMinStopTime = inf.ReadInt32();
            }
            else
            {
                ActualMinStopTime = null;
            }

            if (inf.ReadBoolean())
            {
                KeepClearFront = inf.ReadSingle();
            }
            else
            {
                KeepClearFront = null;
            }

            if (inf.ReadBoolean())
            {
                KeepClearRear = inf.ReadSingle();
            }
            else
            {
                KeepClearRear = null;
            }

            Terminal = inf.ReadBoolean();
            ForcePosition = inf.ReadBoolean();
            CloseupSignal = inf.ReadBoolean();
            Closeup = inf.ReadBoolean();
            RestrictPlatformToSignal = inf.ReadBoolean();
            ExtendPlatformToSignal = inf.ReadBoolean();
            EndStop = inf.ReadBoolean();
        }

        // Compare To (to allow sort)
        public int CompareTo(StationStop other)
        {
            if (other == null)
                return 1;

            int result = SubrouteIndex.CompareTo(other.SubrouteIndex);
            if (result != 0)
                return result / Math.Abs(result);
            result = RouteIndex.CompareTo(other.RouteIndex);
            if (result != 0)
                return result / Math.Abs(result);
            result = StopOffset.CompareTo(other.StopOffset);
            if (result != 0)
                return result / Math.Abs(result);
            return 0;
        }

        public override bool Equals(object obj)
        {
            return obj is StationStop stop && CompareTo(stop) == 0;
        }

        // Save
        public void Save(BinaryWriter outf)
        {
            if (outf == null)
                throw new ArgumentNullException(nameof(outf));

            outf.Write((int)StopType);
            outf.Write(PlatformReference);
            outf.Write(SubrouteIndex);
            outf.Write(RouteIndex);
            outf.Write(TrackCircuitSectionIndex);
            outf.Write((int)Direction);
            outf.Write(ExitSignal);
            outf.Write(HoldSignal);
            outf.Write(NoWaitSignal);
            outf.Write(NoClaimAllowed);
            outf.Write(CallOnAllowed);
            outf.Write(StopOffset);
            outf.Write(ArrivalTime);
            outf.Write(DepartTime);
            outf.Write(ActualArrival);
            outf.Write(ActualDepart);
            outf.Write(Passed);

            outf.Write(ConnectionsWaiting?.Count ?? 0);
            foreach (int i in ConnectionsWaiting ?? Enumerable.Empty<int>())
            {
                outf.Write(i);
            }

            outf.Write(ConnectionsAwaited?.Count ?? 0);
            foreach (KeyValuePair<int, int> awaitedConnection in ConnectionsAwaited ?? Enumerable.Empty<KeyValuePair<int, int>>())
            {
                outf.Write(awaitedConnection.Key);
                outf.Write(awaitedConnection.Value);
            }

            outf.Write(ConnectionDetails?.Count ?? 0);
            foreach (KeyValuePair<int, WaitInfo> connectionDetails in ConnectionDetails ?? Enumerable.Empty<KeyValuePair<int, WaitInfo>>())
            {
                outf.Write(connectionDetails.Key);
                connectionDetails.Value.Save(outf);
            }

            if (ActualMinStopTime.HasValue)
            {
                outf.Write(true);
                outf.Write(ActualMinStopTime.Value);
            }
            else
            {
                outf.Write(false);
            }

            if (KeepClearFront.HasValue)
            {
                outf.Write(true);
                outf.Write(KeepClearFront.Value);
            }
            else
            {
                outf.Write(false);
            }
            if (KeepClearRear.HasValue)
            {
                outf.Write(true);
                outf.Write(KeepClearRear.Value);
            }
            else
            {
                outf.Write(false);
            }

            outf.Write(Terminal);
            outf.Write(ForcePosition);
            outf.Write(CloseupSignal);
            outf.Write(Closeup);
            outf.Write(RestrictPlatformToSignal);
            outf.Write(ExtendPlatformToSignal);
            outf.Write(EndStop);
        }

        /// <summary>
        ///  create copy
        /// </summary>
        /// <returns></returns>
        public StationStop CreateCopy()
        {
            return (StationStop)MemberwiseClone();
        }

        /// <summary>
        /// Calculate actual depart time
        /// Make special checks for stops arount midnight
        /// </summary>
        /// <param name="presentTime"></param>
        internal int CalculateDepartTime(Train train)
        {
            const int eightHundredHours = 8 * 3600;
            const int sixteenHundredHours = 16 * 3600;

            // preset depart to booked time
            ActualDepart = DepartTime;

            // correct arrival for stop around midnight
            if (ActualArrival < eightHundredHours && ArrivalTime > sixteenHundredHours) // arrived after midnight, expected to arrive before
            {
                ActualArrival += (24 * 3600);
            }
            else if (ActualArrival > sixteenHundredHours && ArrivalTime < eightHundredHours) // arrived before midnight, expected to arrive before
            {
                ActualArrival -= (24 * 3600);
            }

            // correct stop time for stop around midnight
            int stopTime = DepartTime - ArrivalTime;
            if (DepartTime < eightHundredHours && ArrivalTime > sixteenHundredHours) // stop over midnight
            {
                stopTime += (24 * 3600);
            }

            bool validSched;
            // compute boarding time (depends on train type)
            (validSched, stopTime) = train.ComputeTrainBoardingTime(this, stopTime);

            // correct departure time for stop around midnight
            int correctedTime = ActualArrival + stopTime;
            if (validSched)
            {
                ActualDepart = Time.Compare.Latest(DepartTime, correctedTime);
            }
            else
            {
                ActualDepart = correctedTime;
                if (ActualDepart < 0)
                {
                    ActualDepart += (24 * 3600);
                    ActualArrival += (24 * 3600);
                }
            }
            if (ActualDepart != correctedTime)
            {
                stopTime += ActualDepart - correctedTime;
                if (stopTime > 24 * 3600) stopTime -= 24 * 3600;
                else if (stopTime < 0) stopTime += 24 * 3600;

            }
            return stopTime;
        }

        //================================================================================================//
        /// <summary>
        /// <CScomment> Compute boarding time for passenger train. Solution based on number of carriages within platform.
        /// Number of carriages computed considering an average Traincar length...
        /// ...moreover considering that carriages are in the train part within platform (MSTS apparently does so).
        /// Player train has more sophisticated computing, as in MSTS.
        /// As of now the position of the carriages within the train is computed here at every station together with the statement if the carriage is within the 
        /// platform boundaries. To be evaluated if the position of the carriages within the train could later be computed together with the CheckFreight method</CScomment>
        /// <\summary>
        internal int ComputeStationBoardingTime(Train train)
        {
            int passengerCarsWithinPlatform = train.PassengerCarsNumber;
            int stopTime = DefaultFreightStopTime;
            if (passengerCarsWithinPlatform == 0) 
                return stopTime; // pure freight train
            float distancePlatformHeadtoTrainHead = -train.StationStops[0].StopOffset + PlatformItem.TrackCircuitOffset[Location.FarEnd, train.StationStops[0].Direction] + train.StationStops[0].DistanceToTrainM;
            float trainPartOutsidePlatformForward = distancePlatformHeadtoTrainHead < 0 ? -distancePlatformHeadtoTrainHead : 0;
            if (trainPartOutsidePlatformForward >= train.Length) 
                return (int)PlatformItem.MinWaitingTime; // train actually passed platform; should not happen
            float distancePlatformTailtoTrainTail = distancePlatformHeadtoTrainHead - PlatformItem.Length + train.Length;
            float trainPartOutsidePlatformBackward = distancePlatformTailtoTrainTail > 0 ? distancePlatformTailtoTrainTail : 0;
            if (trainPartOutsidePlatformBackward >= train.Length) 
                return (int)PlatformItem.MinWaitingTime; // train actually stopped before platform; should not happen
            if (train == Simulator.Instance.OriginalPlayerTrain)
            {
                if (trainPartOutsidePlatformForward == 0 && trainPartOutsidePlatformBackward == 0) 
                    passengerCarsWithinPlatform = train.PassengerCarsNumber;
                else
                {
                    if (trainPartOutsidePlatformForward > 0)
                    {
                        float walkingDistance = 0.0f;
                        int trainCarIndex = 0;
                        while (walkingDistance <= trainPartOutsidePlatformForward && passengerCarsWithinPlatform > 0 && trainCarIndex < train.Cars.Count - 1)
                        {
                            float walkingDistanceBehind = walkingDistance + train.Cars[trainCarIndex].CarLengthM;
                            if ((train.Cars[trainCarIndex].WagonType != TrainCar.WagonTypes.Freight && train.Cars[trainCarIndex].WagonType != TrainCar.WagonTypes.Tender && !train.Cars[trainCarIndex].IsDriveable) ||
                               (train.Cars[trainCarIndex].IsDriveable && train.Cars[trainCarIndex].HasPassengerCapacity))
                            {
                                if ((trainPartOutsidePlatformForward - walkingDistance) > 0.67 * train.Cars[trainCarIndex].CarLengthM) 
                                    passengerCarsWithinPlatform--;
                            }
                            walkingDistance = walkingDistanceBehind;
                            trainCarIndex++;
                        }
                    }
                    if (trainPartOutsidePlatformBackward > 0 && passengerCarsWithinPlatform > 0)
                    {
                        float walkingDistance = 0.0f;
                        int trainCarIndex = train.Cars.Count - 1;
                        while (walkingDistance <= trainPartOutsidePlatformBackward && passengerCarsWithinPlatform > 0 && trainCarIndex >= 0)
                        {
                            float walkingDistanceBehind = walkingDistance + train.Cars[trainCarIndex].CarLengthM;
                            if ((train.Cars[trainCarIndex].WagonType != TrainCar.WagonTypes.Freight && train.Cars[trainCarIndex].WagonType != TrainCar.WagonTypes.Tender && !train.Cars[trainCarIndex].IsDriveable) ||
                               (train.Cars[trainCarIndex].IsDriveable && train.Cars[trainCarIndex].HasPassengerCapacity))
                            {
                                if ((trainPartOutsidePlatformBackward - walkingDistance) > 0.67 * train.Cars[trainCarIndex].CarLengthM) 
                                    passengerCarsWithinPlatform--;
                            }
                            walkingDistance = walkingDistanceBehind;
                            trainCarIndex--;
                        }
                    }
                }
            }
            else
            {
                passengerCarsWithinPlatform = train.Length - trainPartOutsidePlatformForward - trainPartOutsidePlatformBackward > 0 ?
                    train.PassengerCarsNumber : (int)Math.Min((train.Length - trainPartOutsidePlatformForward - trainPartOutsidePlatformBackward) / train.Cars.Count + 0.33,
                    train.PassengerCarsNumber);
            }
            if (passengerCarsWithinPlatform > 0)
            {
                int actualNumPassengersWaiting = PlatformItem.NumPassengersWaiting;
                if (train.TrainType != TrainType.AiPlayerHosting)
                    actualNumPassengersWaiting = RandomizePassengersWaiting(PlatformItem.NumPassengersWaiting, train);
                stopTime = Math.Max(NumSecPerPass * actualNumPassengersWaiting / passengerCarsWithinPlatform, DefaultFreightStopTime);
            }
            else 
                stopTime = 0; // no passenger car stopped within platform: sorry, no countdown starts

            return stopTime;
        }

        //================================================================================================//
        /// <summary>
        /// CheckScheduleValidity
        /// Quite frequently in MSTS activities AI trains have invalid values (often near midnight), because MSTS does not consider them anyway
        /// As OR considers them, it is wise to discard the least credible values, to avoid AI trains stopping for hours
        /// </summary>
        internal bool CheckScheduleValidity(Train train)
        {
            if (train.TrainType != TrainType.Ai) 
                return true;
            return (!(ArrivalTime == DepartTime && Math.Abs(ArrivalTime - ActualArrival) > 14400));
        }

        //================================================================================================//
        /// <summary>
        /// RandomizePassengersWaiting
        /// Randomizes number of passengers waiting for train, and therefore boarding time
        /// Randomization can be upwards or downwards
        /// </summary>
        private int RandomizePassengersWaiting(int actualNumPassengersWaiting, Train train)
        {
            if (Simulator.Instance.Settings.ActRandomizationLevel > 0)
            {
                int randms = DateTime.UtcNow.Millisecond % 10;
                if (randms >= 6 - Simulator.Instance.Settings.ActRandomizationLevel)
                {
                    if (randms < 8)
                    {
                        actualNumPassengersWaiting += Train.RandomizedDelay(2 * PlatformItem.NumPassengersWaiting *
                            Simulator.Instance.Settings.ActRandomizationLevel); // real passenger number may be up to 3 times the standard.
                    }
                    else
                    // less passengers than standard
                    {
                        actualNumPassengersWaiting -= Train.RandomizedDelay(PlatformItem.NumPassengersWaiting *
                            Simulator.Instance.Settings.ActRandomizationLevel / 6);
                    }
                }
            }
            return actualNumPassengersWaiting;
        }

        internal void EnsureListsExists()
        {
            if (null == ConnectionsWaiting)
                ConnectionsWaiting = new List<int>();
            if (null == ConnectionsAwaited)
                ConnectionsAwaited = new Dictionary<int, int>();
            if (null == ConnectionDetails)
                ConnectionDetails = new Dictionary<int, WaitInfo>();
        }

    }
}
