// COPYRIGHT 2012, 2013 by the Open Rails project.
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

// This file is the responsibility of the 3D & Environment Team. 

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Orts.Common.Position;
using Orts.Formats.Msts.Models;
using Orts.Simulation.AIs;
using Orts.Simulation.Physics;

namespace Orts.Simulation.World
{
    public class LevelCrossings
    {
        public Dictionary<int, LevelCrossingItem> TrackCrossingItems { get; }
        public Dictionary<int, LevelCrossingItem> RoadCrossingItems { get; }
        public Dictionary<LevelCrossingItem, LevelCrossingItem> RoadToTrackCrossingItems { get; } = new Dictionary<LevelCrossingItem, LevelCrossingItem>();

        public LevelCrossings(Simulator simulator)
        {
            TrackCrossingItems = simulator?.TrackDatabase?.TrackDB?.TrackNodes != null && simulator.TrackDatabase.TrackDB.TrackItems != null
                ? GetLevelCrossingsFromDB(simulator.TrackDatabase.TrackDB.TrackNodes, simulator.TrackDatabase.TrackDB.TrackItems) : new Dictionary<int, LevelCrossingItem>();
            RoadCrossingItems = simulator.RoadDatabase?.RoadTrackDB?.TrackNodes != null && simulator.RoadDatabase.RoadTrackDB.TrItemTable != null
                ? GetLevelCrossingsFromDB(simulator.RoadDatabase.RoadTrackDB.TrackNodes, simulator.RoadDatabase.RoadTrackDB.TrItemTable) : new Dictionary<int, LevelCrossingItem>();
        }

        private static Dictionary<int, LevelCrossingItem> GetLevelCrossingsFromDB(IEnumerable<TrackNode> trackNodes, IList<TrackItem> trItemTable)
        {
            return (from trackNode in trackNodes
                    where trackNode is TrackVectorNode tvn && tvn.TrackItemIndices.Length > 0
                    from itemRef in (trackNode as TrackVectorNode)?.TrackItemIndices.Distinct()
                    where trItemTable[itemRef] != null && (trItemTable[itemRef] is Formats.Msts.Models.LevelCrossingItem || trItemTable[itemRef] is RoadLevelCrossingItem)
                    select new KeyValuePair<int, LevelCrossingItem>(itemRef, new LevelCrossingItem(trackNode, trItemTable[itemRef])))
                    .ToDictionary((_) => _.Key, (_) => _.Value);
        }

        /// <summary>
        /// Creates a level crossing from its track and road component IDs.
        /// </summary>
        /// <param name="position">Position of the level crossing object for error reporting.</param>
        /// <param name="trackIDs">List of TrItem IDs (from the track database) for the track crossing items.</param>
        /// <param name="roadIDs">List of TrItem IDs (from the road database) for the road crossing items.</param>
        /// <param name="warningTime">Time that gates should be closed prior to a train arriving (seconds).</param>
        /// <param name="minimumDistance">Minimum distance from the gates that a train is allowed to stop and have the gates open (meters).</param>
        /// <returns>The level crossing object comprising of the specified track and road items plus warning and distance configuration.</returns>
        public LevelCrossing CreateLevelCrossing(in WorldPosition position, IEnumerable<int> trackIDs, IEnumerable<int> roadIDs, float warningTime, float minimumDistance)
        {
            LevelCrossingItem[] trackItems = trackIDs.Select(id => TrackCrossingItems[id]).ToArray();
            LevelCrossingItem[] roadItems = roadIDs.Select(id => RoadCrossingItems[id]).ToArray();
            if (trackItems.Length != roadItems.Length)
                Trace.TraceWarning("{0} level crossing contains {1} rail and {2} road items; expected them to match.", position, trackItems.Length, roadItems.Length);
            if (trackItems.Length >= roadItems.Length)
                for (int i = 0; i < roadItems.Length; i++)
                    if (!RoadToTrackCrossingItems.ContainsKey(roadItems[i]))
                        RoadToTrackCrossingItems.Add(roadItems[i], trackItems[i]);
            return new LevelCrossing(trackItems.Union(roadItems), warningTime, minimumDistance);
        }

        public void Update(double elapsedTime)
        {
            foreach (Train train in Simulator.Instance.Trains)
                UpdateCrossings(train, elapsedTime);
        }

        private void UpdateCrossings(Train train, double elapsedTime)
        {
            _ = elapsedTime;
            float speedMpS = train.SpeedMpS;
            float absSpeedMpS = Math.Abs(speedMpS);
            float maxSpeedMpS = train.AllowedMaxSpeedMpS;
            const float minCrossingActivationSpeed = 5.0f;  //5.0MpS is equalivalent to 11.1mph.  This is the estimated min speed that MSTS uses to activate the gates when in range.


            bool validTrain = false;
            bool validStaticConsist = false;

            // We only care about crossing items which are:
            //   a) Grouped properly.
            //   b) Within the maximum activation distance of front/rear of the train.
            // Separate tests are performed for present speed and for possible maximum speed to avoid anomolies if train accelerates.
            // Special test is also done to check on section availability to avoid closure beyond signal at danger.

            foreach (LevelCrossingItem crossing in TrackCrossingItems.Values.Where(ci => ci.CrossingGroup != null))
            {
                float predictedDist = crossing.CrossingGroup.WarningTime * absSpeedMpS;
                float maxPredictedDist = crossing.CrossingGroup.WarningTime * (maxSpeedMpS - absSpeedMpS) / 2; // added distance if train accelerates to maxspeed
                float minimumDist = crossing.CrossingGroup.MinimumDistance;
                float totalDist = predictedDist + minimumDist + 1;
                float totalMaxDist = predictedDist + maxPredictedDist + minimumDist + 1;

                float reqDist = 0f; // actual used distance
                float adjustDist = 0f;


                //  The first 2 tests are critical for STATIC CONSISTS, but at the same time should be mandatory since there should always be checks for any null situation.
                //  The first 2 tests cover a situation where a STATIC consist is found on a crossing attached to a road where other crossings are attached to the same road.
                //  These tests will only allow the activation of the crossing that should be activated but prevent the actvation of the other crossings which was the issue.
                //  The source of the issue is not known yet since this only happens with STATIC consists.

                // The purpose of this test is to validate the static consist that is within vicinity of the crossing.
                if (train.TrainType == TrainType.Static)
                {
                    // An issue was found where a road sharing more than one crossing would have all crossings activated instead of the one crossing when working with STATIC consists.
                    // The test below corrects this, but the source of the issue is not understood.
                    if (!WorldLocation.Within(crossing.Location, train.FrontTDBTraveller.WorldLocation, minimumDist + train.Length / 2) && !WorldLocation.Within(crossing.Location, train.RearTDBTraveller.WorldLocation, minimumDist + train.Length / 2))
                        continue;
                    if (WorldLocation.Within(crossing.Location, train.FrontTDBTraveller.WorldLocation, minimumDist + train.Length / 2) || WorldLocation.Within(crossing.Location, train.RearTDBTraveller.WorldLocation, minimumDist + train.Length / 2))
                        foreach (RollingStocks.TrainCar scar in train.Cars)
                            if (WorldLocation.Within(crossing.Location, scar.WorldPosition.WorldLocation, minimumDist))
                                validStaticConsist = true;
                }

                if (train.TrainType != TrainType.Static && WorldLocation.Within(crossing.Location, train.FrontTDBTraveller.WorldLocation, totalDist) || WorldLocation.Within(crossing.Location, train.RearTDBTraveller.WorldLocation, totalDist))
                {
                    validTrain = true;
                    reqDist = totalDist;
                }
                else if (train.TrainType != TrainType.Static && WorldLocation.Within(crossing.Location, train.FrontTDBTraveller.WorldLocation, totalMaxDist) || WorldLocation.Within(crossing.Location, train.RearTDBTraveller.WorldLocation, totalMaxDist))
                {
                    validTrain = true;
                    reqDist = totalMaxDist;
                }

                if (train.TrainType == TrainType.Static && !validStaticConsist && !crossing.StaticConsists.Contains(train))
                    continue;

                if (train.TrainType != TrainType.Static && !validTrain && !crossing.Trains.Contains(train))
                    continue;

                // Distances forward from the front and rearwards from the rear.
                float frontDist = crossing.DistanceTo(train.FrontTDBTraveller, reqDist);
                if (frontDist < 0 && train.TrainType != TrainType.Static)
                {
                    frontDist = -crossing.DistanceTo(new Traveller(train.FrontTDBTraveller, Traveller.TravellerDirection.Backward), reqDist + train.Length);
                    if (frontDist > 0)
                    {
                        // Train cannot find crossing.
                        crossing.RemoveTrain(train);
                        continue;
                    }
                }

                float rearDist = -frontDist - train.Length;

                if (train is AITrain aiTrain && (aiTrain.LevelCrossingHornPattern?.ShouldActivate(crossing.CrossingGroup, absSpeedMpS, Math.Min(frontDist, Math.Abs(rearDist))) ?? false))
                    //  Add generic actions if needed
                    aiTrain.AuxActionsContainer.CheckGenActions(GetType(), crossing.Location, rearDist, frontDist, crossing.TrackIndex, aiTrain.LevelCrossingHornPattern);

                // The tests below is to allow the crossings operate like the crossings under MSTS
                // Tests as follows
                // Train speed is 0.  This was the initial issue that was found under one the MSTS activities.  Activity should start without gates being activated.
                // There are 2 tests for train speed between 0 and 5.0MpS(11.1mph).  Covering forward movement and reverse movement.  
                // The last 2 tests is for testing trains running at line speed, forward or reverse.

                // The crossing only becomes active if the train has been added to the list such as crossing.AddTrain(train).
                // Note: With the conditions below, OR's crossings operates like the crossings in MSTS, with exception to the simulation of the timout below.

                // MSTS did not simulate a timeout, I introduced a simple timout using speedMpS.

                // Depending upon future development in this area, it would probably be best to have the current operation in its own class followed by any new region specific operations. 

                // Recognizing static consists at crossings.
                if (train.TrainType == TrainType.Static && validStaticConsist)
                {
                    // This process is to raise the crossing gates if a loose consist rolls through the crossing.
                    if (speedMpS > 0)
                    {
                        frontDist = crossing.DistanceTo(train.FrontTDBTraveller, minimumDist);
                        rearDist = crossing.DistanceTo(train.RearTDBTraveller, minimumDist);

                        if (frontDist < 0 && rearDist < 0)
                            crossing.RemoveTrain(train);
                    }
                    //adjustDist is used to allow static cars to be placed closer to the crossing without activation.
                    // One example would be industry sidings with a crossing nearby.  This was found in a custom route.
                    if (minimumDist >= 20)
                        adjustDist = minimumDist - 13.5f;
                    else if (minimumDist < 20)
                        adjustDist = minimumDist - 6.5f;
                    frontDist = crossing.DistanceTo(train.FrontTDBTraveller, adjustDist);
                    rearDist = crossing.DistanceTo(train.RearTDBTraveller, adjustDist);
                    // Static consist passed the crossing.
                    if (frontDist < 0 && rearDist < 0)
                        rearDist = crossing.DistanceTo(new Traveller(train.RearTDBTraveller, Traveller.TravellerDirection.Backward), adjustDist);

                    // Testing distance before crossing
                    if (frontDist > 0 && frontDist <= adjustDist)
                        crossing.AddTrain(train);

                    // Testing to check if consist is straddling the crossing.
                    else if (frontDist < 0 && rearDist > 0)
                        crossing.AddTrain(train);

                    // This is an odd test because a custom route has a particular
                    // crossing object placement that is creating different results.
                    // Testing to check if consist is straddling the crossing.
                    else if (frontDist < 0 && rearDist < 0)
                        crossing.AddTrain(train);

                    // Testing distance when past crossing.
                    else if (rearDist <= adjustDist && rearDist > 0)
                        crossing.AddTrain(train);

                    else
                        crossing.RemoveTrain(train);
                }

                // Train is stopped.
                else if ((train is AITrain || train.TrainType == TrainType.Player || train.TrainType == TrainType.Remote) && Math.Abs(speedMpS) <= Simulator.MaxStoppedMpS && frontDist <= reqDist && (train.ReservedTrackLengthM <= 0 || frontDist < train.ReservedTrackLengthM) && rearDist <= minimumDist)
                    // First test is to simulate a timeout if a train comes to a stop before minimumDist
                    if (frontDist > minimumDist && Simulator.Instance.Trains.Contains(train))
                        crossing.RemoveTrain(train);
                    // This test is to factor in the train sitting on the crossing at the start of the activity.
                    else
                        crossing.AddTrain(train);

                // Train is travelling toward crossing below 11.1mph.
                else if ((train is AITrain || train.TrainType == TrainType.Player || train.TrainType == TrainType.Static || train.TrainType == TrainType.Remote) && speedMpS > 0 && speedMpS <= minCrossingActivationSpeed && frontDist <= reqDist && (train.ReservedTrackLengthM <= 0 || frontDist < train.ReservedTrackLengthM) && rearDist <= minimumDist)
                    // This will allow a slow train to approach to the crossing's minmum distance without activating the crossing.
                    if (frontDist <= minimumDist + 65f) // Not all crossing systems operate the same so adding an additional 65 meters is only an option to improve operation.
                        crossing.AddTrain(train);

                    // Checking for reverse movement when train is approaching crossing while travelling under 11.1mph.
                    else if ((train is AITrain || train.TrainType == TrainType.Player) && speedMpS < 0 && absSpeedMpS <= minCrossingActivationSpeed && rearDist <= reqDist && (train.ReservedTrackLengthM <= 0 || rearDist < train.ReservedTrackLengthM) && frontDist <= minimumDist)
                        // This will allow a slow train to approach a crossing to a certain point without activating the system.
                        // First test covers front of train clearing crossing.
                        // Second test covers rear of train approaching crossing.
                        if (frontDist > 9.5) // The value of 9.5 which is within minimumDist is used to test against frontDist to give the best possible distance the gates should deactivate.
                            crossing.RemoveTrain(train);
                        else if (rearDist <= minimumDist + 65f) // Not all crossing systems operate the same so adding an additional 65 meters is only an option to improve operation.
                            crossing.AddTrain(train);

                        // Checking for reverse movement through crossing when train is travelling above 11.1mph.
                        else if ((train is AITrain || train.TrainType == TrainType.Player || train.TrainType == TrainType.Remote) && speedMpS < 0 && absSpeedMpS > minCrossingActivationSpeed && rearDist <= reqDist && (train.ReservedTrackLengthM <= 0 || rearDist < train.ReservedTrackLengthM) && frontDist <= minimumDist)
                            crossing.AddTrain(train);

                        // Player train travelling in forward direction above 11.1mph will activate the crossing.  
                        else if ((train is AITrain || train.TrainType == TrainType.Player || train.TrainType == TrainType.Remote) && speedMpS > 0 && speedMpS > minCrossingActivationSpeed && frontDist <= reqDist && (train.ReservedTrackLengthM <= 0 || frontDist < train.ReservedTrackLengthM) && rearDist <= minimumDist)
                            crossing.AddTrain(train);

                        else
                            crossing.RemoveTrain(train);
            }
        }

        public LevelCrossingItem SearchNearLevelCrossing(Train train, float reqDist, bool trainForwards, out float frontDist)
        {
            if (null == train)
                throw new ArgumentNullException(nameof(train));

            LevelCrossingItem roadItem = LevelCrossingItem.None;
            frontDist = -1;
            Traveller traveller = trainForwards ? train.FrontTDBTraveller :
                new Traveller(train.RearTDBTraveller, Traveller.TravellerDirection.Backward);
            foreach (LevelCrossingItem crossing in TrackCrossingItems.Values.Where(ci => ci.CrossingGroup != null))
                if (crossing.Trains.Contains(train))
                {
                    frontDist = crossing.DistanceTo(traveller, reqDist);
                    if (frontDist > 0 && frontDist <= reqDist)
                        if (RoadToTrackCrossingItems.ContainsValue(crossing))
                        {
                            roadItem = RoadToTrackCrossingItems.FirstOrDefault(x => x.Value == crossing).Key;
                            return roadItem;
                        }
                }
            return roadItem;
        }
    }

    public class LevelCrossingItem
    {
        private readonly TrackNode TrackNode;

        // THREAD SAFETY:
        //   All accesses must be done in local variables. No modifications to the objects are allowed except by
        //   assignment of a new instance (possibly cloned and then modified).
        internal List<Train> Trains = new List<Train>();
        internal List<Train> StaticConsists = new List<Train>();
        public ref readonly WorldLocation Location => ref trackItem.Location;
        public LevelCrossing CrossingGroup { get; internal set; }
        public uint TrackIndex => TrackNode.Index;
        private readonly TrackItem trackItem;

        public static LevelCrossingItem None { get; } = new LevelCrossingItem();


        public LevelCrossingItem(TrackNode trackNode, TrackItem trItem)
        {
            TrackNode = trackNode;
            trackItem = trItem;
        }

        public LevelCrossingItem()
        {

        }

        public void AddTrain(Train train)
        {
            if (null == train)
                throw new ArgumentNullException(nameof(train));

            if (train.TrainType == TrainType.Static)
            {
                List<Train> staticConsists = StaticConsists;
                if (!staticConsists.Contains(train))
                {
                    StaticConsists = new List<Train>(staticConsists)
                    {
                        train
                    };
                }
            }
            else
            {
                List<Train> trains = Trains;
                if (!trains.Contains(train))
                {
                    Trains = new List<Train>(trains)
                    {
                        train
                    };
                }
            }
        }

        public void RemoveTrain(Train train)
        {
            List<Train> trains = Trains;
            List<Train> staticConsists = StaticConsists;
            if (staticConsists.Count > 0)
                if (staticConsists.Contains(train))
                {
                    List<Train> newStaticConsists = new List<Train>(staticConsists);
                    newStaticConsists.Remove(train);
                    StaticConsists = newStaticConsists;
                }
                // Secondary option to remove Static entry from list in case the above does not work.
                // Since the above process would not be able to remove the static consist from the list when the locomotive attaches to the consist.
                // The process below will be able to do it. 
                else
                {
                    List<Train> newStaticConsists = new List<Train>(staticConsists);
                    for (int i = 0; i < newStaticConsists.Count; i++)
                        if (newStaticConsists[i].TrainType == TrainType.Static)
                            newStaticConsists.RemoveAt(i);
                    StaticConsists = newStaticConsists;
                }
            else if (trains.Count > 0)
                if (trains.Contains(train))
                {
                    List<Train> newTrains = new List<Train>(trains);
                    newTrains.Remove(train);
                    Trains = newTrains;
                }
        }

        public float DistanceTo(Traveller traveller)
        {
            return DistanceTo(traveller, float.MaxValue);
        }

        public float DistanceTo(Traveller traveller, float maxDistance)
        {
            return traveller?.DistanceTo(TrackNode, Location, maxDistance) ?? throw new ArgumentNullException(nameof(traveller));
        }
    }

    public class LevelCrossing
    {
        private readonly List<LevelCrossingItem> items;
        internal float WarningTime { get; }
        internal float MinimumDistance { get; }

        public LevelCrossing(IEnumerable<LevelCrossingItem> items, float warningTime, float minimumDistance)
        {
            this.items = new List<LevelCrossingItem>(items);
            WarningTime = warningTime;
            MinimumDistance = minimumDistance;
            foreach (LevelCrossingItem item in items ?? Enumerable.Empty<LevelCrossingItem>())
                item.CrossingGroup = this;
        }

        public bool HasTrain => (items.Any(i => i.Trains.Count > 0) || items.Any(i => i.StaticConsists.Count > 0));
    }
}
