// COPYRIGHT 2010, 2011, 2012, 2013 by the Open Rails project.
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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Xna.Framework;
using Orts.Common;
using Orts.Common.Calc;
using Orts.Common.Logging;
using Orts.Common.Position;
using Orts.Common.Xna;
using Orts.Formats.Msts;
using Orts.Formats.Msts.Files;
using Orts.Formats.Msts.Models;
using Orts.Simulation.AIs;
using Orts.Simulation.Physics;
using Orts.Simulation.Signalling;

namespace Orts.Simulation
{

    public enum ActivityEventType
    {
        Timer,
        TrainStart,
        TrainStop,
        Couple,
        Uncouple
    }

    public class Activity
    {
        private Simulator Simulator;

        // Passenger tasks
        public DateTime StartTime;
        public List<ActivityTask> Tasks = new List<ActivityTask>();
        public ActivityTask Current;
        private double prevTrainSpeed = -1;

        // Freight events
        public List<EventWrapper> EventList = new List<EventWrapper>();
        public Boolean IsComplete;          // true once activity is completed.
        public Boolean IsSuccessful;        // status of completed activity
        public Nullable<int> StartTimeS;    // Clock time in seconds when activity was launched.
        public EventWrapper TriggeredEvent; // Indicates the currently triggered event whose data the ActivityWindow will pop up to display.

        // The ActivityWindow may be open when the simulation is saved with F2.
        // If so, we need to remember the event and the state of the window (is the activity resumed or still paused, so we can restore it.
        public bool IsActivityWindowOpen;       // Remembers the status of the ActivityWindow [closed|opened]
        public EventWrapper LastTriggeredEvent; // Remembers the TriggeredEvent after it has been cancelled.
        public bool IsActivityResumed;            // Remembers the status of the ActivityWindow [paused|resumed]
        public bool ReopenActivityWindow;       // Set on Restore() and tested by ActivityWindow
        // Note: The variables above belong to the Activity, not the ActivityWindow because they run on different threads.
        // The Simulator must not monitor variables in the Window thread, but it's OK for the Window thread to monitor the Simulator.

        // station stop logging flags - these are saved to resume correct logging after save
        private string StationStopLogFile;   // logfile name
        private bool StationStopLogActive;   // logging is active
        public EventWrapper triggeredEventWrapper;        // used for exchange with Sound.cs to trigger activity sounds;
        public bool NewMsgFromNewPlayer; // flag to indicate to ActivityWindow that there is a new message to be shown;
        public string MsgFromNewPlayer; // string to be displayed in ActivityWindow

        public List<TempSpeedPostItem> TempSpeedPostItems;

        public int RandomizabilityPerCent; // 0 -> hardly randomizable ; 100 -> well randomizable
        public bool WeatherChangesPresent; // tested in case of randomized activities to state wheter weather should be randomized

        private Activity(BinaryReader inf, Simulator simulator, List<EventWrapper> oldEventList, List<TempSpeedPostItem> tempSpeedPostItems)
        {
            TempSpeedPostItems = tempSpeedPostItems;
            Simulator = simulator;
            RestoreThis(inf, simulator, oldEventList);
        }

        public Activity(ActivityFile actFile, Simulator simulator)
        {
            Simulator = simulator;  // Save for future use.
            PlayerServices sd;
            sd = actFile.Activity.PlayerServices;
            if (sd != null)
            {
                if (sd.PlayerTraffics.Count > 0)
                {
                    PlatformItem Platform = null;
                    ActivityTask task = null;

                    foreach (var i in sd.PlayerTraffics)
                    {
                        if (i.PlatformStartID < Simulator.TrackDatabase.TrackDB.TrackItems.Length && i.PlatformStartID >= 0 &&
                            Simulator.TrackDatabase.TrackDB.TrackItems[i.PlatformStartID] is PlatformItem)
                            Platform = Simulator.TrackDatabase.TrackDB.TrackItems[i.PlatformStartID] as PlatformItem;
                        else
                        {
                            Trace.TraceWarning("PlatformStartID {0} is not present in TDB file", i.PlatformStartID);
                            continue;
                        }
                        if (Platform != null)
                        {
                            if (Simulator.TrackDatabase.TrackDB.TrackItems[Platform.LinkedPlatformItemId] is PlatformItem)
                            {
                                PlatformItem Platform2 = Simulator.TrackDatabase.TrackDB.TrackItems[Platform.LinkedPlatformItemId] as PlatformItem;
                                Tasks.Add(task = new ActivityTaskPassengerStopAt(simulator,
                                    task,
                                    new DateTime().AddSeconds(i.ArrivalTime),
                                    new DateTime().AddSeconds(i.DepartTime),
                                    Platform, Platform2));
                            }
                        }
                    }
                    Current = Tasks[0];
                }
            }

            // Compile list of freight events, if any, from the parsed ACT file.
            foreach (ActivityEvent i in actFile?.Activity?.Events ?? Enumerable.Empty<ActivityEvent>())
            {
                if (i is ActionActivityEvent)
                {
                    EventList.Add(new EventCategoryActionWrapper(i, Simulator));
                }
                if (i is LocationActivityEvent)
                {
                    EventList.Add(new EventCategoryLocationWrapper(i, Simulator));
                }
                if (i is TimeActivityEvent)
                {
                    EventList.Add(new EventCategoryTimeWrapper(i, Simulator));
                }
                EventWrapper eventAdded = EventList.Last();
                eventAdded.OriginalActivationLevel = i.ActivationLevel;
                if (i.WeatherChange != null || i.Outcomes.WeatherChange != null) WeatherChangesPresent = true;
            }

            StationStopLogActive = false;
            StationStopLogFile = null;
        }

        public ActivityTask Last
        {
            get
            {
                return Tasks.Count == 0 ? null : Tasks[Tasks.Count - 1];
            }
        }

        public bool IsFinished
        {
            get
            {
                return Tasks.Count == 0 ? false : Last.IsCompleted != null;
            }
        }

        public void Update()
        {
            // Update freight events
            // Set the clock first time through. Can't set in the Activity constructor as Simulator.ClockTime is still 0 then.
            if (!StartTimeS.HasValue)
            {
                StartTimeS = (int)Simulator.ClockTime;
                // Initialise passenger actual arrival time
                if (Current != null)
                {
                    if (Current is ActivityTaskPassengerStopAt)
                    {
                        ActivityTaskPassengerStopAt task = Current as ActivityTaskPassengerStopAt;
                    }
                }
            }
            if (this.IsComplete == false)
            {
                foreach (var i in EventList)
                {
                    // Once an event has fired, we don't respond to any more events until that has been acknowledged.
                    // so this line needs to be inside the EventList loop.
                    if (this.TriggeredEvent != null) { break; }

                    if (i != null && i.ParsedObject.ActivationLevel > 0)
                    {
                        if (i.TimesTriggered < 1 || i.ParsedObject.Reversible)
                        {
                            if (i.Triggered(this))
                            {
                                if (i.IsDisabled == false)
                                {
                                    i.TimesTriggered += 1;
                                    if (i.IsActivityEnded(this))
                                    {
                                        IsComplete = true;
                                    }
                                    this.TriggeredEvent = i;    // Note this for Viewer and ActivityWindow to use.
                                    // Do this after IsActivityEnded() so values are ready for ActivityWindow
                                    LastTriggeredEvent = TriggeredEvent;
                                }
                            }
                            else
                            {
                                if (i.ParsedObject.Reversible)
                                {
                                    // Reversible event is no longer triggered, so can re-enable it.
                                    i.IsDisabled = false;
                                }
                            }
                        }
                    }
                }
            }

            // Update passenger tasks
            if (Current == null) return;

            Current.NotifyEvent(ActivityEventType.Timer);
            if (Current.IsCompleted != null)    // Surely this doesn't test for: 
            //   Current.IsCompleted == false
            // More correct would be:
            //   if (Current.IsCompleted.HasValue && Current.IsCompleted == true)
            // (see http://stackoverflow.com/questions/56518/c-is-there-any-difference-between-bool-and-nullablebool)
            {
                Current = Current.NextTask;
            }
            if (Simulator.OriginalPlayerTrain.TrainType == TrainType.Player || Simulator.OriginalPlayerTrain.TrainType == TrainType.AiPlayerDriven)
            {
                if (Math.Abs(Simulator.OriginalPlayerTrain.SpeedMpS) < 0.2f)
                {
                    if (Math.Abs(prevTrainSpeed) >= 0.2f)
                    {
                        prevTrainSpeed = 0;
                        Current.NotifyEvent(ActivityEventType.TrainStop);
                        if (Current.IsCompleted != null)
                        {
                            Current = Current.NextTask;
                        }
                    }
                }
                else
                {
                    if (Math.Abs(prevTrainSpeed) < 0.2f && Math.Abs(Simulator.OriginalPlayerTrain.SpeedMpS) >= 0.2f)
                    {
                        prevTrainSpeed = Simulator.OriginalPlayerTrain.SpeedMpS;
                        Current.NotifyEvent(ActivityEventType.TrainStart);
                        if (Current.IsCompleted != null)
                        {
                            Current = Current.NextTask;
                        }
                    }
                }
            }
            else
            {
                if (Math.Abs(Simulator.OriginalPlayerTrain.SpeedMpS) <= Simulator.MaxStoppedMpS)
                {
                    if (prevTrainSpeed != 0)
                    {
                        prevTrainSpeed = 0;
                        Current.NotifyEvent(ActivityEventType.TrainStop);
                        if (Current.IsCompleted != null)
                        {
                            Current = Current.NextTask;
                        }
                    }
                }
                else
                {
                    if (prevTrainSpeed == 0 && Math.Abs(Simulator.OriginalPlayerTrain.SpeedMpS) > 0.2f)
                    {
                        prevTrainSpeed = Simulator.OriginalPlayerTrain.SpeedMpS;
                        Current.NotifyEvent(ActivityEventType.TrainStart);
                        if (Current.IsCompleted != null)
                        {
                            Current = Current.NextTask;
                        }
                    }
                }
            }
        }

        // <CJComment> Use of static methods is clumsy. </CJComment>
        public static void Save(BinaryWriter outf, Activity act)
        {
            Int32 noval = -1;
            if (act == null)
            {
                outf.Write(noval);
            }
            else
            {
                noval = 1;
                outf.Write(noval);
                act.Save(outf);
            }
        }

        // <CJComment> Re-creating the activity object seems bizarre but not ready to re-write it yet. </CJComment>
        public static Activity Restore(BinaryReader inf, Simulator simulator, Activity oldActivity)
        {
            Int32 rdval;
            rdval = inf.ReadInt32();
            if (rdval == -1)
            {
                return null;
            }
            else
            {
                // Retain the old EventList. It's full of static data so save and restore is a waste of effort
                Activity act = new Activity(inf, simulator, oldActivity.EventList, oldActivity.TempSpeedPostItems);
                return act;
            }
        }

        public void Save(BinaryWriter outf)
        {
            Int32 noval = -1;

            // Save passenger activity
            outf.Write((Int64)StartTime.Ticks);
            outf.Write((Int32)Tasks.Count);
            foreach (ActivityTask task in Tasks)
            {
                task.Save(outf);
            }
            if (Current == null) outf.Write(noval); else outf.Write((Int32)(Tasks.IndexOf(Current)));
            outf.Write(prevTrainSpeed);

            // Save freight activity
            outf.Write((bool)IsComplete);
            outf.Write((bool)IsSuccessful);
            outf.Write((Int32)StartTimeS);
            foreach (EventWrapper e in EventList)
            {
                e.Save(outf);
            }
            if (TriggeredEvent == null)
            {
                outf.Write(false);
            }
            else
            {
                outf.Write(true);
                outf.Write(EventList.IndexOf(TriggeredEvent));
            }
            outf.Write(IsActivityWindowOpen);
            if (LastTriggeredEvent == null)
            {
                outf.Write(false);
            }
            else
            {
                outf.Write(true);
                outf.Write(EventList.IndexOf(LastTriggeredEvent));
            }

            // Save info for ActivityWindow coming from new player train
            outf.Write(NewMsgFromNewPlayer);
            if (NewMsgFromNewPlayer) outf.Write(MsgFromNewPlayer);

            outf.Write(IsActivityResumed);

            // write log details

            outf.Write(StationStopLogActive);
            if (StationStopLogActive)
            {
                outf.Write(StationStopLogFile);
            }
        }

        public void RestoreThis(BinaryReader inf, Simulator simulator, List<EventWrapper> oldEventList)
        {
            Int32 rdval;

            // Restore passenger activity
            ActivityTask task;
            StartTime = new DateTime(inf.ReadInt64());
            rdval = inf.ReadInt32();
            for (int i = 0; i < rdval; i++)
            {
                task = GetTask(inf, simulator);
                task.Restore(inf);
                Tasks.Add(task);
            }
            rdval = inf.ReadInt32();
            Current = rdval == -1 ? null : Tasks[rdval];
            prevTrainSpeed = inf.ReadDouble();

            task = null;
            for (int i = 0; i < Tasks.Count; i++)
            {
                Tasks[i].PrevTask = task;
                if (task != null) task.NextTask = Tasks[i];
                task = Tasks[i];
            }

            // Restore freight activity
            IsComplete = inf.ReadBoolean();
            IsSuccessful = inf.ReadBoolean();
            StartTimeS = inf.ReadInt32();

            this.EventList = oldEventList;
            foreach (var e in EventList)
            {
                e.Restore(inf);
            }

            if (inf.ReadBoolean()) TriggeredEvent = EventList[inf.ReadInt32()];

            IsActivityWindowOpen = inf.ReadBoolean();
            if (inf.ReadBoolean()) LastTriggeredEvent = EventList[inf.ReadInt32()];

            // Restore info for ActivityWindow coming from new player train
            NewMsgFromNewPlayer = inf.ReadBoolean();
            if (NewMsgFromNewPlayer) MsgFromNewPlayer = inf.ReadString();

            IsActivityResumed = inf.ReadBoolean();
            ReopenActivityWindow = IsActivityWindowOpen;

            // restore logging info
            StationStopLogActive = inf.ReadBoolean();
            if (StationStopLogActive)
            {
                StationStopLogFile = inf.ReadString();

                foreach (ActivityTask stask in Tasks)
                {
                    if (stask.GetType() == typeof(ActivityTaskPassengerStopAt))
                    {
                        ActivityTaskPassengerStopAt stoptask = stask as ActivityTaskPassengerStopAt;
                        stoptask.LogStationLogFile = StationStopLogFile;
                        stoptask.LogStationStops = true;
                    }
                }
            }
            else
            {
                StationStopLogFile = null;
            }
        }

        private static ActivityTask GetTask(BinaryReader inf, Simulator simulator)
        {
            Int32 rdval;
            rdval = inf.ReadInt32();
            if (rdval == 1)
                return new ActivityTaskPassengerStopAt(simulator);
            else
                return null;
        }

        public void StartStationLogging(string stationLogFile)
        {
            StationStopLogFile = stationLogFile;
            StationStopLogActive = true;

            StringBuilder stringBuild = new StringBuilder();

            char separator = (char)Simulator.Settings.DataLoggerSeparator;

            stringBuild.Append("STATION");
            stringBuild.Append(separator);
            stringBuild.Append("BOOKED ARR");
            stringBuild.Append(separator);
            stringBuild.Append("BOOKED DEP");
            stringBuild.Append(separator);
            stringBuild.Append("ACTUAL ARR");
            stringBuild.Append(separator);
            stringBuild.Append("ACTUAL DEP");
            stringBuild.Append(separator);
            stringBuild.Append("DELAY");
            stringBuild.Append(separator);
            stringBuild.Append("STATE");
            stringBuild.Append('\n');
            File.AppendAllText(StationStopLogFile, stringBuild.ToString());

            foreach (ActivityTask task in Tasks)
            {
                if (task.GetType() == typeof(ActivityTaskPassengerStopAt))
                {
                    ActivityTaskPassengerStopAt stoptask = task as ActivityTaskPassengerStopAt;
                    stoptask.LogStationLogFile = StationStopLogFile;
                    stoptask.LogStationStops = true;
                }
            }
        }

        /// <summary>
        /// Add speedposts to the track database for each Temporary Speed Restriction zone
        /// </summary>
        /// <param name="routeFile"></param>
        /// <param name="tsectionDat">track sections containing the details of the various sections</param>
        /// <param name="trackDB">The track Database that needs to be updated</param>
        /// <param name="zones">List of speed restriction zones</param>
        public void AddRestrictZones(Route routeFile, TrackSectionsFile tsectionDat, TrackDB trackDB, RestrictedSpeedZones zones)
        {
            if (zones.Count < 1) return;

            TempSpeedPostItems = new List<TempSpeedPostItem>();

            TrackItem[] newSpeedPostItems = new TempSpeedPostItem[2];

            Traveller traveller;

            const float MaxDistanceOfWarningPost = 2000;

            for (int idxZone = 0; idxZone < zones.Count; idxZone++)
            {
                newSpeedPostItems[0] = new TempSpeedPostItem(routeFile,
                    zones[idxZone].StartPosition, true, WorldPosition.None, false);
                newSpeedPostItems[1] = new TempSpeedPostItem(routeFile,
                    zones[idxZone].EndPosition, false, WorldPosition.None, false);

                // Add the speedposts to the track database. This will set the TrItemId's of all speedposts
                trackDB.AddTrackItems(newSpeedPostItems);

                // And now update the various (vector) tracknodes (this needs the TrItemIds.
                var endOffset = AddItemIdToTrackNode(zones[idxZone].EndPosition,
                    tsectionDat, trackDB, newSpeedPostItems[1], out traveller);
                var startOffset = AddItemIdToTrackNode(zones[idxZone].StartPosition,
                    tsectionDat, trackDB, newSpeedPostItems[0], out traveller);
                float distanceOfWarningPost = 0;
                TrackNode trackNode = trackDB.TrackNodes[traveller.TrackNodeIndex];
                if (startOffset != null && endOffset != null && startOffset > endOffset)
                {
                    ((TempSpeedPostItem)newSpeedPostItems[0]).Flip();
                    ((TempSpeedPostItem)newSpeedPostItems[1]).Flip();
                    distanceOfWarningPost = (float)Math.Min(MaxDistanceOfWarningPost, traveller.TrackNodeLength - (double)startOffset);
                }
                else if (startOffset != null && endOffset != null && startOffset <= endOffset)
                    distanceOfWarningPost = (float)Math.Max(-MaxDistanceOfWarningPost, -(double)startOffset);
                traveller.Move(distanceOfWarningPost);
                var worldPosition3 = WorldPosition.None;
                var speedWarningPostItem = new TempSpeedPostItem(routeFile,
                    zones[idxZone].StartPosition, false, worldPosition3, true);
                SpeedPostPosition(speedWarningPostItem, ref traveller);
                if (startOffset != null && endOffset != null && startOffset > endOffset)
                {
                    speedWarningPostItem.Flip();
                }
                ((TempSpeedPostItem)newSpeedPostItems[0]).ComputeTablePosition();
                TempSpeedPostItems.Add((TempSpeedPostItem)newSpeedPostItems[0]);
                ((TempSpeedPostItem)newSpeedPostItems[1]).ComputeTablePosition();
                TempSpeedPostItems.Add((TempSpeedPostItem)newSpeedPostItems[1]);
                speedWarningPostItem.ComputeTablePosition();
                TempSpeedPostItems.Add(speedWarningPostItem);
            }
        }

        /// <summary>
        /// Add a reference to a new TrItemId to the correct trackNode (which needs to be determined from the position)
        /// </summary>
        /// <param name="location">Position of the new </param>
        /// <param name="tsectionDat">track sections containing the details of the various sections</param>
        /// <param name="trackDB">track database to be modified</param>
        /// <param name="newTrItemRef">The Id of the new TrItem to add to the tracknode</param>
        /// <param name="traveller">The computed traveller to the speedPost position</param>
        private static float? AddItemIdToTrackNode(in WorldLocation location, TrackSectionsFile tsectionDat, TrackDB trackDB, TrackItem newTrItem, out Traveller traveller)
        {
            float? offset = 0.0f;
            traveller = new Traveller(tsectionDat, trackDB.TrackNodes, location);
            TrackNode trackNode = trackDB.TrackNodes[traveller.TrackNodeIndex];//find the track node
            if (trackNode is TrackVectorNode trackVectorNode)
            {
                offset = traveller.TrackNodeOffset;
                SpeedPostPosition((TempSpeedPostItem)newTrItem, ref traveller);
                InsertTrItemRef(tsectionDat, trackDB, trackVectorNode, (int)newTrItem.TrackItemId, (float)offset);
            }
            return offset;
        }

        /// <summary>
        /// Determine position parameters of restricted speed Post
        /// </summary>
        /// <param name="restrSpeedPost">The Id of the new restricted speed post to position</param>
        /// <param name="traveller">The traveller to the speedPost position</param>
        /// 
        private static void SpeedPostPosition(TempSpeedPostItem restrSpeedPost, ref Traveller traveller)
        {
            restrSpeedPost.Update(traveller.Y, -traveller.RotY + (float)Math.PI / 2, new WorldPosition(traveller.TileX, traveller.TileZ, MatrixExtension.SetTranslation(Matrix.CreateFromYawPitchRoll(-traveller.RotY, 0, 0), traveller.X, traveller.Y, -traveller.Z)));
        }

        /// <summary>
        /// Insert a reference to a new TrItem to the already existing TrItemRefs basing on its offset within the track node.
        /// </summary>
        /// 
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", Justification = "Keeping identifier consistent to use in MSTS")]
        private static void InsertTrItemRef(TrackSectionsFile tsectionDat, TrackDB trackDB, TrackVectorNode thisVectorNode, int newTrItemId, float offset)
        {
            int index = 0;
            // insert the new TrItemRef accordingly to its offset
            for (int iTrItems = thisVectorNode.TrackItemIndices.Length - 1; iTrItems >= 0; iTrItems--)
            {
                int currTrItemID = thisVectorNode.TrackItemIndices[iTrItems];
                TrackItem currTrItem = trackDB.TrackItems[currTrItemID];
                Traveller traveller = new Traveller(tsectionDat, trackDB.TrackNodes, currTrItem.Location);
                if (offset >= traveller.TrackNodeOffset)
                {
                    index = iTrItems + 1;
                    break;
                }
            }
            thisVectorNode.InsertTrackItemIndex(newTrItemId, index);
        }

        public void AssociateEvents(Train train)
        {
            foreach (var eventWrapper in EventList)
            {
                if (eventWrapper is EventCategoryLocationWrapper && !string.IsNullOrEmpty(eventWrapper.ParsedObject.TrainService) &&
                    eventWrapper.ParsedObject.TrainService.Equals(train.Name, StringComparison.OrdinalIgnoreCase))
                {
                    if (eventWrapper.ParsedObject.TrainStartingTime == -1 || (train as AITrain).ServiceDefinition.Time == eventWrapper.ParsedObject.TrainStartingTime)
                    {
                        eventWrapper.Train = train;
                    }
                }
            }
        }
    }

    public class ActivityTask
    {
        public bool? IsCompleted { get; internal set; }
        public ActivityTask PrevTask { get; internal set; }
        public ActivityTask NextTask { get; internal set; }
        public DateTime CompletedAt { get; internal set; }
        public string DisplayMessage { get; internal set; }
        public Color DisplayColor { get; internal set; }

        public virtual void NotifyEvent(ActivityEventType EventType)
        {
        }

        public virtual void Save(BinaryWriter outf)
        {
            Int32 noval = -1;
            if (IsCompleted == null) outf.Write(noval); else outf.Write(IsCompleted.Value ? (Int32)1 : (Int32)0);
            outf.Write((Int64)CompletedAt.Ticks);
            outf.Write(DisplayMessage);
        }

        public virtual void Restore(BinaryReader inf)
        {
            Int64 rdval;
            rdval = inf.ReadInt32();
            IsCompleted = rdval == -1 ? (bool?)null : rdval == 0 ? false : true;
            CompletedAt = new DateTime(inf.ReadInt64());
            DisplayMessage = inf.ReadString();
        }
    }

    /// <summary>
    /// Helper class to calculate distances along the path
    /// </summary>
    public class TDBTravellerDistanceCalculatorHelper
    {
        /// <summary>Maximum size of a platform or station we use for searching forward and backward</summary>
        private const float maxPlatformOrStationSize = 10000f;

        // Result of calculation
        public enum DistanceResult
        {
            Valid,
            Behind,
            OffPath
        }

        // We use this traveller as the basis of the calculations.
        private Traveller refTraveller;
        private float Distance;

        public TDBTravellerDistanceCalculatorHelper(Traveller traveller)
        {
            refTraveller = traveller;
        }

        public DistanceResult CalculateToPoint(in WorldLocation location)
        {
            Traveller poiTraveller;
            poiTraveller = new Traveller(refTraveller);

            // Find distance once
            Distance = poiTraveller.DistanceTo(location, maxPlatformOrStationSize);

            // If valid
            if (Distance > 0)
            {
                return DistanceResult.Valid;
            }
            else
            {
                // Go to opposite direction
                poiTraveller = new Traveller(refTraveller, Traveller.TravellerDirection.Backward);

                Distance = poiTraveller.DistanceTo(location, maxPlatformOrStationSize);
                // If valid, it is behind us
                if (Distance > 0)
                {
                    return DistanceResult.Behind;
                }
            }

            // Otherwise off path
            return DistanceResult.OffPath;
        }
    }

    public class ActivityTaskPassengerStopAt : ActivityTask
    {
        private readonly Simulator Simulator;

        public DateTime SchArrive;
        public DateTime SchDepart;
        public DateTime? ActArrive;
        public DateTime? ActDepart;
        public PlatformItem PlatformEnd1;
        public PlatformItem PlatformEnd2;

        public double BoardingS;   // MSTS calls this the Load/Unload time. Cargo gets loaded, but passengers board the train.
        public double BoardingEndS;
        private int TimerChk;
        private bool arrived;
        private bool maydepart;
        public bool LogStationStops;
        public string LogStationLogFile;
        public float distanceToNextSignal = -1;
        public Train MyPlayerTrain; // Shortcut to player train

        public bool ldbfevaldepartbeforeboarding;//Debrief Eval
        public static List<string> DbfEvalDepartBeforeBoarding = new List<string>();//Debrief Eval

        public ActivityTaskPassengerStopAt(Simulator simulator, ActivityTask prev, DateTime Arrive, DateTime Depart,
                 PlatformItem Platformend1, PlatformItem Platformend2)
        {
            Simulator = simulator;
            SchArrive = Arrive;
            SchDepart = Depart;
            PlatformEnd1 = Platformend1;
            PlatformEnd2 = Platformend2;
            PrevTask = prev;
            if (prev != null)
                prev.NextTask = this;
            DisplayMessage = "";

            LogStationStops = false;
            LogStationLogFile = null;
        }

        internal ActivityTaskPassengerStopAt(Simulator simulator)
        {
            Simulator = simulator;
        }

        /// <summary>
        /// Determines if the train is at station.
        /// Tests for either the front or the rear of the train is within the platform.
        /// </summary>
        /// <returns></returns>
        public bool IsAtStation(Train myTrain)
        {
            if (myTrain.StationStops.Count == 0) return false;
            var thisStation = myTrain.StationStops[0];
            if (myTrain.StationStops[0].SubrouteIndex != myTrain.TCRoute.ActiveSubPath) return false;
            return myTrain.CheckStationPosition(thisStation.PlatformItem, thisStation.Direction, thisStation.TrackCircuitSectionIndex);
        }

        public bool IsMissedStation()
        {
            // Check if station is in present train path

            if (MyPlayerTrain.StationStops.Count == 0 ||
                MyPlayerTrain.TCRoute.ActiveSubPath != MyPlayerTrain.StationStops[0].SubrouteIndex || !(MyPlayerTrain.ControlMode == TrainControlMode.AutoNode || MyPlayerTrain.ControlMode == TrainControlMode.AutoSignal))
            {
                return (false);
            }

            return MyPlayerTrain.MissedPlatform(200.0f);
        }

        public override void NotifyEvent(ActivityEventType EventType)
        {

            MyPlayerTrain = Simulator.OriginalPlayerTrain;
            // The train is stopped.
            if (EventType == ActivityEventType.TrainStop)
            {
                if (MyPlayerTrain.TrainType != TrainType.AiPlayerHosting && IsAtStation(MyPlayerTrain)  ||
                    MyPlayerTrain.TrainType == TrainType.AiPlayerHosting && (MyPlayerTrain as AITrain).MovementState == AiMovementState.StationStop)
                {
                    if (Simulator.TimetableMode || MyPlayerTrain.StationStops.Count == 0)
                    {
                        // If yes, we arrived
                        if (ActArrive == null)
                        {
                            ActArrive = new DateTime().Add(TimeSpan.FromSeconds(Simulator.ClockTime));
                        }

                        arrived = true;

                        // Figure out the boarding time
                        // <CSComment> No midnight checks here? There are some in Train.CalculateDepartTime
                        double plannedBoardingS = (SchDepart - SchArrive).TotalSeconds;
                        double punctualBoardingS = (SchDepart - ActArrive).Value.TotalSeconds;
                        double expectedBoardingS = plannedBoardingS > 0 ? plannedBoardingS : PlatformEnd1.PlatformMinWaitingTime;
                        BoardingS = punctualBoardingS;                                     // default is leave on time
                        if (punctualBoardingS < expectedBoardingS)                         // if not enough time for boarding
                        {
                            if (plannedBoardingS > 0 && plannedBoardingS < PlatformEnd1.PlatformMinWaitingTime)
                            { // and tight schedule
                                BoardingS = plannedBoardingS;                              // leave late with no recovery of time
                            }
                            else
                            {                                                       // generous schedule
                                BoardingS = Math.Max(
                                    punctualBoardingS,                                     // leave on time
                                    PlatformEnd1.PlatformMinWaitingTime);                  // leave late with some recovery
                            }
                        }
                        // ActArrive is usually same as ClockTime
                        BoardingEndS = Simulator.ClockTime + BoardingS;
                        // But not if game starts after scheduled arrival. In which case actual arrival is assumed to be same as schedule arrival.
                        double sinceActArriveS = (new DateTime().Add(TimeSpan.FromSeconds(Simulator.ClockTime))
                                                - ActArrive).Value.TotalSeconds;
                        BoardingEndS -= sinceActArriveS;
                    }
                    else
                    {
                        // <CSComment> MSTS mode - player
                        if (Simulator.GameTime < 2)
                        {
                            // If the simulation starts with a scheduled arrive in the past, assume the train arrived on time.
                            if (SchArrive < new DateTime().Add(TimeSpan.FromSeconds(Simulator.ClockTime)))
                            {
                                ActArrive = SchArrive;
                            }
                        }
                        BoardingS = (double)MyPlayerTrain.StationStops[0].ComputeStationBoardingTime(Simulator.PlayerLocomotive.Train);
                        if (BoardingS > 0 || ((double)(SchDepart - SchArrive).TotalSeconds > 0 &&
                            MyPlayerTrain.PassengerCarsNumber == 1 && MyPlayerTrain.Cars.Count > 10))
                        {
                            // accepted station stop because either freight train or passenger train or fake passenger train with passenger car on platform or fake passenger train
                            // with Scheduled Depart > Scheduled Arrive
                            // ActArrive is usually same as ClockTime
                            BoardingEndS = Simulator.ClockTime + BoardingS;

                            if (ActArrive == null)
                            {
                                ActArrive = new DateTime().Add(TimeSpan.FromSeconds(Simulator.ClockTime));
                            }

                            arrived = true;
                            // But not if game starts after scheduled arrival. In which case actual arrival is assumed to be same as schedule arrival.
                            double sinceActArriveS = (new DateTime().Add(TimeSpan.FromSeconds(Simulator.ClockTime))
                                                    - ActArrive).Value.TotalSeconds;
                            BoardingEndS -= sinceActArriveS;
                                double SchDepartS = SchDepart.Subtract(new DateTime()).TotalSeconds;
                                BoardingEndS = Time.Compare.Latest((int)SchDepartS, (int)BoardingEndS);

                        }
                    }
                    if (MyPlayerTrain.NextSignalObject[0] != null)
                        distanceToNextSignal = MyPlayerTrain.NextSignalObject[0].DistanceTo(MyPlayerTrain.FrontTDBTraveller);

                }
            }
            else if (EventType == ActivityEventType.TrainStart)
            {
                // Train has started, we have things to do if we arrived before
                if (arrived)
                {
                    ActDepart = new DateTime().Add(TimeSpan.FromSeconds(Simulator.ClockTime));
                    CompletedAt = ActDepart.Value;
                    // Completeness depends on the elapsed waiting time
                    IsCompleted = maydepart;
                    if (MyPlayerTrain.TrainType != TrainType.AiPlayerHosting)
                        MyPlayerTrain.ClearStation(PlatformEnd1.LinkedPlatformItemId, PlatformEnd2.LinkedPlatformItemId, true);

                    if (LogStationStops)
                    {
                        StringBuilder stringBuild = new StringBuilder();
                        char separator = (char)Simulator.Settings.DataLoggerSeparator;
                        stringBuild.Append(PlatformEnd1.Station);
                        stringBuild.Append(separator);
                        stringBuild.Append(SchArrive.ToString("HH:mm:ss"));
                        stringBuild.Append(separator);
                        stringBuild.Append(SchDepart.ToString("HH:mm:ss"));
                        stringBuild.Append(separator);
                        stringBuild.Append(ActArrive.HasValue ? ActArrive.Value.ToString("HH:mm:ss") : "-");
                        stringBuild.Append(separator);
                        stringBuild.Append(ActDepart.HasValue ? ActDepart.Value.ToString("HH:mm:ss") : "-");

                        TimeSpan delay = ActDepart.HasValue ? (ActDepart - SchDepart).Value : TimeSpan.Zero;
                        stringBuild.Append(separator);
                        stringBuild.AppendFormat("{0}:{1}:{2}", delay.Hours.ToString("00"), delay.Minutes.ToString("00"), delay.Seconds.ToString("00"));
                        stringBuild.Append(separator);
                        stringBuild.Append(maydepart ? "Completed" : "NotCompleted");
                        stringBuild.Append('\n');
                        File.AppendAllText(LogStationLogFile, stringBuild.ToString());
                    }
                }
            }
            else if (EventType == ActivityEventType.Timer)
            {
                // Waiting at a station
                if (arrived)
                {
                    var remaining = (int)Math.Ceiling(BoardingEndS - Simulator.ClockTime);
                    if (remaining < 1) DisplayColor = Color.LightGreen;
                    else if (remaining < 11) DisplayColor = new Color(255, 255, 128);
                    else DisplayColor = Color.White;

                    if (remaining < 120 && (MyPlayerTrain.TrainType != TrainType.AiPlayerHosting))
                    {
                        MyPlayerTrain.ClearStation(PlatformEnd1.LinkedPlatformItemId, PlatformEnd2.LinkedPlatformItemId, false);
                    }

                    // Still have to wait
                    if (remaining > 0)
                    {
                        DisplayMessage = Simulator.Catalog.GetString("Passenger boarding completes in {0:D2}:{1:D2}",
                            remaining / 60, remaining % 60);

                        //Debrief Eval
                        if (Simulator.PlayerLocomotive.SpeedMpS > 0 && !ldbfevaldepartbeforeboarding)
                        {
                            var train = Simulator.PlayerLocomotive.Train;
                            ldbfevaldepartbeforeboarding = true;
                            DbfEvalDepartBeforeBoarding.Add(PlatformEnd1.Station);
                            train.DbfEvalValueChanged = true;
                        }
                    }
                    // May depart
                    else if (!maydepart)
                    {
                        // check if signal ahead is cleared - if not, do not allow depart
                        if (distanceToNextSignal >= 0 && distanceToNextSignal < 300 && MyPlayerTrain.NextSignalObject[0] != null &&
                            MyPlayerTrain.NextSignalObject[0].SignalLR(SignalFunction.Normal) == SignalAspectState.Stop
                            && MyPlayerTrain.NextSignalObject[0].OverridePermission != SignalPermission.Granted)
                        {
                            DisplayMessage = Simulator.Catalog.GetString("Passenger boarding completed. Waiting for signal ahead to clear.");
                        }
                        else
                        {
                            maydepart = true;
                            DisplayMessage = Simulator.Catalog.GetString("Passenger boarding completed. You may depart now.");
                            Simulator.SoundNotify = TrainEvent.PermissionToDepart;
                        }

                        ldbfevaldepartbeforeboarding = false;//reset flag. Debrief Eval

                        // if last task, show closure window
                        // also set times in logfile

                        if (NextTask == null)
                        {
                            if (LogStationStops)
                            {
                                StringBuilder stringBuild = new StringBuilder();
                                char separator = (char)Simulator.Settings.DataLoggerSeparator;
                                stringBuild.Append(PlatformEnd1.Station);
                                stringBuild.Append(separator);
                                stringBuild.Append(SchArrive.ToString("HH:mm:ss"));
                                stringBuild.Append(separator);
                                stringBuild.Append('-');
                                stringBuild.Append(separator);
                                stringBuild.Append(ActArrive.HasValue ? ActArrive.Value.ToString("HH:mm:ss") : "-");
                                stringBuild.Append(separator);
                                stringBuild.Append('-');
                                stringBuild.Append(separator);

                                TimeSpan delay = ActArrive.HasValue ? (ActArrive - SchArrive).Value : TimeSpan.Zero;
                                if (delay.CompareTo(TimeSpan.Zero) < 0)
                                {
                                    delay = TimeSpan.Zero - delay;
                                    stringBuild.AppendFormat("-{0}:{1}:{2}", delay.Hours.ToString("00"), delay.Minutes.ToString("00"), delay.Seconds.ToString("00"));
                                }
                                else
                                {
                                    stringBuild.AppendFormat("{0}:{1}:{2}", delay.Hours.ToString("00"), delay.Minutes.ToString("00"), delay.Seconds.ToString("00"));
                                }

                                stringBuild.Append(separator);
                                stringBuild.Append("Final stop");
                                stringBuild.Append('\n');
                                File.AppendAllText(LogStationLogFile, stringBuild.ToString());
                            }

                            IsCompleted = true;
                        }
                    }
                }
                else
                {
                    // Checking missed station
                    int tmp = (int)(Simulator.ClockTime % 10);
                    if (tmp != TimerChk)
                    {
                        if (IsMissedStation() && (MyPlayerTrain.TrainType != TrainType.AiPlayerHosting))
                        {
                            MyPlayerTrain.ClearStation(PlatformEnd1.LinkedPlatformItemId, PlatformEnd2.LinkedPlatformItemId, true);
                            IsCompleted = false;

                            if (LogStationStops)
                            {
                                StringBuilder stringBuild = new StringBuilder();
                                char separator = (char)Simulator.Settings.DataLoggerSeparator;
                                stringBuild.Append(PlatformEnd1.Station);
                                stringBuild.Append(separator);
                                stringBuild.Append(SchArrive.ToString("HH:mm:ss"));
                                stringBuild.Append(separator);
                                stringBuild.Append(SchDepart.ToString("HH:mm:ss"));
                                stringBuild.Append(separator);
                                stringBuild.Append('-');
                                stringBuild.Append(separator);
                                stringBuild.Append('-');
                                stringBuild.Append(separator);
                                stringBuild.Append('-');
                                stringBuild.Append(separator);
                                stringBuild.Append("Missed");
                                stringBuild.Append('\n');
                                File.AppendAllText(LogStationLogFile, stringBuild.ToString());
                            }
                        }
                    }
                }
            }
        }

        public override void Save(BinaryWriter outf)
        {
            Int64 noval = -1;
            outf.Write((Int32)1);

            base.Save(outf);

            outf.Write((Int64)SchArrive.Ticks);
            outf.Write((Int64)SchDepart.Ticks);
            if (ActArrive == null) outf.Write(noval); else outf.Write((Int64)ActArrive.Value.Ticks);
            if (ActDepart == null) outf.Write(noval); else outf.Write((Int64)ActDepart.Value.Ticks);
            outf.Write((Int32)PlatformEnd1.TrackItemId);
            outf.Write((Int32)PlatformEnd2.TrackItemId);
            outf.Write((double)BoardingEndS);
            outf.Write((double)BoardingS);
            outf.Write((Int32)TimerChk);
            outf.Write(arrived);
            outf.Write(maydepart);
            outf.Write(distanceToNextSignal);
        }

        public override void Restore(BinaryReader inf)
        {
            Int64 rdval;

            base.Restore(inf);

            SchArrive = new DateTime(inf.ReadInt64());
            SchDepart = new DateTime(inf.ReadInt64());
            rdval = inf.ReadInt64();
            ActArrive = rdval == -1 ? (DateTime?)null : new DateTime(rdval);
            rdval = inf.ReadInt64();
            ActDepart = rdval == -1 ? (DateTime?)null : new DateTime(rdval);
            PlatformEnd1 = Simulator.TrackDatabase.TrackDB.TrackItems[inf.ReadInt32()] as PlatformItem;
            PlatformEnd2 = Simulator.TrackDatabase.TrackDB.TrackItems[inf.ReadInt32()] as PlatformItem;
            BoardingEndS = inf.ReadDouble();
            BoardingS = inf.ReadDouble();
            TimerChk = inf.ReadInt32();
            arrived = inf.ReadBoolean();
            maydepart = inf.ReadBoolean();
            distanceToNextSignal = inf.ReadSingle();
        }
    }

    /// <summary>
    /// This class adds attributes around the event objects parsed from the ACT file.
    /// Note: Can't add attributes to the event objects directly as ACTFile.cs is not just used by 
    /// ActivityRunner.exe but also by Menu.exe and these executables lack most of the ORTS classes.
    /// </summary>
    public abstract class EventWrapper
    {
        public Orts.Formats.Msts.Models.ActivityEvent ParsedObject;     // Points to object parsed from file *.act
        public int OriginalActivationLevel; // Needed to reset .ActivationLevel
        public int TimesTriggered;          // Needed for evaluation after activity ends
        public Boolean IsDisabled;          // Used for a reversible event to prevent it firing again until after it has been reset.
        protected Simulator Simulator;
        public Train Train;              // Train involved in event; if null actual or original player train

        public EventWrapper(Orts.Formats.Msts.Models.ActivityEvent @event, Simulator simulator)
        {
            ParsedObject = @event;
            Simulator = simulator;
            Train = null;
        }

        public virtual void Save(BinaryWriter outf)
        {
            outf.Write(TimesTriggered);
            outf.Write(IsDisabled);
            outf.Write(ParsedObject.ActivationLevel);
        }

        public virtual void Restore(BinaryReader inf)
        {
            TimesTriggered = inf.ReadInt32();
            IsDisabled = inf.ReadBoolean();
            ParsedObject.ActivationLevel = inf.ReadInt32();
        }

        /// <summary>
        /// After an event is triggered, any message is displayed independently by ActivityWindow.
        /// </summary>
        /// <param name="activity"></param>
        /// <returns></returns>
        public virtual Boolean Triggered(Activity activity)
        {  // To be overloaded by subclasses
            return false;  // Compiler insists something is returned.
        }

        /// <summary>
        /// Acts on the outcomes and then sets ActivationLevel = 0 to prevent re-use.
        /// </summary>
        /// <param name="activity"></param>
        /// <returns>true if entire activity ends here whether it succeeded or failed</returns>
        public Boolean IsActivityEnded(Activity activity)
        {

            if (this.ParsedObject.Reversible)
            {
                // Stop this event being actioned
                this.IsDisabled = true;
            }
            else
            {
                // Stop this event being monitored
                this.ParsedObject.ActivationLevel = 0;
            }
            // No further action if this reversible event has been triggered before
            if (this.TimesTriggered > 1) { return false; }

            if (this.ParsedObject.Outcomes == null) { return false; }

            // Set Activation Level of each event in the Activate list to 1.
            // Uses lambda expression => for brevity.
            foreach (int eventId in ParsedObject.Outcomes.ActivateList)
            {
                foreach (var item in activity.EventList.Where(item => item.ParsedObject.ID == eventId))
                    item.ParsedObject.ActivationLevel = 1;
            }
            foreach (int eventId in ParsedObject.Outcomes.RestoreActivityLevels)
            {
                foreach (var item in activity.EventList.Where(item => item.ParsedObject.ID == eventId))
                    item.ParsedObject.ActivationLevel = item.OriginalActivationLevel;
            }
            foreach (int eventId in ParsedObject.Outcomes.DecrementActivityLevels)
            {
                foreach (var item in activity.EventList.Where(item => item.ParsedObject.ID == eventId))
                    item.ParsedObject.ActivationLevel += -1;
            }
            foreach (int eventId in ParsedObject.Outcomes.IncrementActivityLevels)
            {
                foreach (var item in activity.EventList.Where(item => item.ParsedObject.ID == eventId))
                {
                    item.ParsedObject.ActivationLevel += +1;
                }
            }

            // Activity sound management

            if (this.ParsedObject.SoundFile != null || (this.ParsedObject.Outcomes != null && this.ParsedObject.Outcomes.ActivitySound != null))
            {
                if (activity.triggeredEventWrapper == null) activity.triggeredEventWrapper = this;
            }

            if (this.ParsedObject.WeatherChange != null || (this.ParsedObject.Outcomes != null && this.ParsedObject.Outcomes.WeatherChange != null))
            {
                if (activity.triggeredEventWrapper == null) activity.triggeredEventWrapper = this;
            }

            if (this.ParsedObject.Outcomes.ActivityFail != null)
            {
                activity.IsSuccessful = false;
                return true;
            }
            if (this.ParsedObject.Outcomes.ActivitySuccess == true)
            {
                activity.IsSuccessful = true;
                return true;
            }
            if (!string.IsNullOrEmpty(ParsedObject.Outcomes.RestartWaitingTrain?.WaitingTrainToRestart))
            {
                var restartWaitingTrain = ParsedObject.Outcomes.RestartWaitingTrain;
                Simulator.RestartWaitingTrain(restartWaitingTrain);
            }
            return false;
        }
     
      
    }

    public class EventCategoryActionWrapper : EventWrapper
    {
        private SidingItem SidingEnd1;
        private SidingItem SidingEnd2;
        private List<string> ChangeWagonIdList;   // Wagons to be assembled, picked up or dropped off.

        public EventCategoryActionWrapper(Orts.Formats.Msts.Models.ActivityEvent @event, Simulator simulator)
            : base(@event, simulator)
        {
            var e = this.ParsedObject as ActionActivityEvent;
            if (e.SidingId != null)
            {
                var i = e.SidingId.Value;
                try
                {
                    SidingEnd1 = Simulator.TrackDatabase.TrackDB.TrackItems[i] as SidingItem;
                    i = SidingEnd1.LinkedSidingId;
                    SidingEnd2 = Simulator.TrackDatabase.TrackDB.TrackItems[i] as SidingItem;
                }
                catch (IndexOutOfRangeException)
                {
                    Trace.TraceWarning("Siding {0} is not in track database.", i);
                }
                catch (NullReferenceException)
                {
                    Trace.TraceWarning("Item {0} in track database is not a siding.", i);
                }
            }
        }

        public override Boolean Triggered(Activity activity)
        {
            Train OriginalPlayerTrain = Simulator.OriginalPlayerTrain;
            var e = this.ParsedObject as ActionActivityEvent;
            if (e.WorkOrderWagons != null)
            {                     // only if event involves wagons
                if (ChangeWagonIdList == null)
                {           // populate the list only once - the first time that ActivationLevel > 0 and so this method is called.
                    ChangeWagonIdList = new List<string>();
                    foreach (var item in e.WorkOrderWagons)
                    {
                        ChangeWagonIdList.Add($"{((int)item.UiD & 0xFFFF0000) >> 16} - {(int)item.UiD & 0x0000FFFF}"); // form the .CarID
                    }
                }
            }
            var triggered = false;
            Train consistTrain;
            switch (e.Type)
            {
                case EventType.AllStops:
                    triggered = activity.Tasks.Count > 0 && activity.Last.IsCompleted != null;
                    break;
                case EventType.AssembleTrain:
                    consistTrain = matchesConsist(ChangeWagonIdList);
                    if (consistTrain != null)
                    {
                        triggered = true;
                    }
                    break;
                case EventType.AssembleTrainAtLocation:
                    if (atSiding(OriginalPlayerTrain.FrontTDBTraveller, OriginalPlayerTrain.RearTDBTraveller, this.SidingEnd1, this.SidingEnd2))
                    {
                        consistTrain = matchesConsist(ChangeWagonIdList);
                        triggered = consistTrain != null;
                    }
                    break;
                case EventType.DropOffWagonsAtLocation:
                    // Dropping off of wagons should only count once disconnected from player train.
                    // A better name than DropOffWagonsAtLocation would be ArriveAtSidingWithWagons.
                    // To recognize the dropping off of the cars before the event is activated, this method is used.
                    if (atSiding(OriginalPlayerTrain.FrontTDBTraveller, OriginalPlayerTrain.RearTDBTraveller, this.SidingEnd1, this.SidingEnd2))
                    {
                        consistTrain = matchesConsistNoOrder(ChangeWagonIdList);
                        triggered = consistTrain != null;
                    }
                    break;
                case EventType.PickUpPassengers:
                    break;
                case EventType.PickUpWagons: // PickUpWagons is independent of location or siding
                    triggered = includesWagons(OriginalPlayerTrain, ChangeWagonIdList);
                    break;
                case EventType.ReachSpeed:
                    triggered = (Math.Abs(Simulator.PlayerLocomotive.SpeedMpS) >= e.SpeedMpS);
                    break;
            }
            return triggered;
        }
        /// <summary>
        /// Finds the train that contains exactly the wagons (and maybe loco) in the list in the correct sequence.
        /// </summary>
        /// <param name="wagonIdList"></param>
        /// <returns>train or null</returns>
        private Train matchesConsist(List<string> wagonIdList)
        {
            foreach (var trainItem in Simulator.Trains)
            {
                if (trainItem.Cars.Count == wagonIdList.Count)
                {
                    // Compare two lists to make sure wagons are in expected sequence.
                    bool listsMatch = true;
                    //both lists with the same order
                    for (int i = 0; i < trainItem.Cars.Count; i++)
                    {
                        if (trainItem.Cars.ElementAt(i).CarID != wagonIdList.ElementAt(i)) { listsMatch = false; break; }
                    }
                    if (!listsMatch)
                    {//different order list
                        listsMatch = true;
                        for (int i = trainItem.Cars.Count; i > 0; i--)
                        {
                            if (trainItem.Cars.ElementAt(i - 1).CarID != wagonIdList.ElementAt(trainItem.Cars.Count - i)) { listsMatch = false; break; }
                        }
                    }
                    if (listsMatch) return trainItem;
                }
            }
            return null;
        }
        /// <summary>
        /// Finds the train that contains exactly the wagons (and maybe loco) in the list. Exact order is not required.
        /// </summary>
        /// <param name="wagonIdList"></param>
        /// <returns>train or null</returns>
        private Train matchesConsistNoOrder(List<string> wagonIdList)
        {
            foreach (var trainItem in Simulator.Trains)
            {
                int nCars = 0;//all cars other than WagonIdList.
                int nWagonListCars = 0;//individual wagon drop.
                foreach (var item in trainItem.Cars)
                {
                    if (!wagonIdList.Contains(item.CarID)) nCars++;
                    if (wagonIdList.Contains(item.CarID)) nWagonListCars++;
                }
                // Compare two lists to make sure wagons are present.
                bool listsMatch = true;
                //support individual wagonIdList drop
                if (trainItem.Cars.Count - nCars == (wagonIdList.Count == nWagonListCars ? wagonIdList.Count : nWagonListCars))
                {
                    if (excludesWagons(trainItem, wagonIdList)) listsMatch = false;//all wagons dropped
                    
                    if (listsMatch) return trainItem;
                    
                }
               
            }
            return null;
        }

        /// <summary>
        /// Like MSTS, do not check for unlisted wagons as the wagon list may be shortened for convenience to contain
        /// only the first and last wagon or even just the first wagon.
        /// </summary>
        /// <param name="train"></param>
        /// <param name="wagonIdList"></param>
        /// <returns>True if all listed wagons are part of the given train.</returns>
        private static bool includesWagons(Train train, List<string> wagonIdList)
        {
            foreach (var item in wagonIdList)
            {
                if (train.Cars.Find(car => car.CarID == item) == null) return false;
            }
            // train speed < 1
            return (Math.Abs(train.SpeedMpS) <= 1 ? true : false);
        }

        /// <summary>
        /// Like MSTS, do not check for unlisted wagons as the wagon list may be shortened for convenience to contain
        /// only the first and last wagon or even just the first wagon.
        /// </summary>
        /// <param name="train"></param>
        /// <param name="wagonIdList"></param>
        /// <returns>True if all listed wagons are not part of the given train.</returns>
        private static bool excludesWagons(Train train, List<string> wagonIdList)
        {
            // The Cars list is a global list that includes STATIC cars.  We need to make sure that the active train/car is processed only.
            if (train.TrainType == TrainType.Static)
                return true;

            bool lNotFound = false;
            foreach (var item in wagonIdList)
            {
                //take in count each item in wagonIdList 
                if (train.Cars.Find(car => car.CarID == item) == null)
                {
                    lNotFound = true; //wagon not part of the train
                }
                else
                {
                    lNotFound = false; break;//wagon still part of the train
                }
            }
            return lNotFound;
        }

        /// <summary>
        /// Like platforms, checking that one end of the train is within the siding.
        /// </summary>
        /// <param name="frontPosition"></param>
        /// <param name="rearPosition"></param>
        /// <param name="sidingEnd1"></param>
        /// <param name="sidingEnd2"></param>
        /// <returns>true if both ends of train within siding</returns>
        private static bool atSiding(Traveller frontPosition, Traveller rearPosition, SidingItem sidingEnd1, SidingItem sidingEnd2)
        {
            if (sidingEnd1 == null || sidingEnd2 == null)
            {
                return true;
            }

            TDBTravellerDistanceCalculatorHelper helper;
            TDBTravellerDistanceCalculatorHelper.DistanceResult distanceEnd1;
            TDBTravellerDistanceCalculatorHelper.DistanceResult distanceEnd2;

            // Front calcs
            helper = new TDBTravellerDistanceCalculatorHelper(frontPosition);

            distanceEnd1 = helper.CalculateToPoint(sidingEnd1.Location);
            distanceEnd2 = helper.CalculateToPoint(sidingEnd2.Location);

            // If front between the ends of the siding
            if (((distanceEnd1 == TDBTravellerDistanceCalculatorHelper.DistanceResult.Behind
                && distanceEnd2 == TDBTravellerDistanceCalculatorHelper.DistanceResult.Valid)
                || (distanceEnd1 == TDBTravellerDistanceCalculatorHelper.DistanceResult.Valid
                && distanceEnd2 == TDBTravellerDistanceCalculatorHelper.DistanceResult.Behind)))
            {
                return true;
            }

            // Rear calcs
            helper = new TDBTravellerDistanceCalculatorHelper(rearPosition);

            distanceEnd1 = helper.CalculateToPoint(sidingEnd1.Location);
            distanceEnd2 = helper.CalculateToPoint(sidingEnd2.Location);

            // If rear between the ends of the siding
            if (((distanceEnd1 == TDBTravellerDistanceCalculatorHelper.DistanceResult.Behind
                && distanceEnd2 == TDBTravellerDistanceCalculatorHelper.DistanceResult.Valid)
                || (distanceEnd1 == TDBTravellerDistanceCalculatorHelper.DistanceResult.Valid
                && distanceEnd2 == TDBTravellerDistanceCalculatorHelper.DistanceResult.Behind)))
            {
                return true;
            }

            return false;
        }
    }

    public class EventCategoryLocationWrapper : EventWrapper
    {
        public EventCategoryLocationWrapper(Orts.Formats.Msts.Models.ActivityEvent @event, Simulator simulator)
            : base(@event, simulator)
        {
        }

        public override Boolean Triggered(Activity activity)
        {
            var triggered = false;
            var e = this.ParsedObject as Orts.Formats.Msts.Models.LocationActivityEvent;
            var train = Simulator.PlayerLocomotive.Train;
            if (!string.IsNullOrEmpty(ParsedObject.TrainService) && Train != null)
            {
                if (Train.FrontTDBTraveller == null) return triggered;
                train = Train;
            }
            Train = train;
            if (e.TriggerOnStop)
            {
                // Is train still moving?
                if (Math.Abs(train.SpeedMpS) > 0.032f)
                {
                    return triggered;
                }
            }
            var trainFrontPosition = new Traveller(train.NextRouteReady && train.TCRoute.ActiveSubPath > 0 && train.TCRoute.ReversalInfo[train.TCRoute.ActiveSubPath - 1].Valid ?
                train.RearTDBTraveller : train.FrontTDBTraveller); // just after reversal the old train front position must be considered
            var distance = trainFrontPosition.DistanceTo(e.Location, e.RadiusM);
            if (distance == -1)
            {
                trainFrontPosition.ReverseDirection();
                distance = trainFrontPosition.DistanceTo(e.Location, e.RadiusM);
                if (distance == -1)
                    return triggered;
            }
            if (distance < e.RadiusM) { triggered = true; }
            return triggered;
        }
    }

    public class EventCategoryTimeWrapper : EventWrapper
    {

        public EventCategoryTimeWrapper(Orts.Formats.Msts.Models.ActivityEvent @event, Simulator simulator)
            : base(@event, simulator)
        {
        }

        public override Boolean Triggered(Activity activity)
        {
            var e = this.ParsedObject as Orts.Formats.Msts.Models.TimeActivityEvent;
            if (e == null) return false;
            Train = Simulator.PlayerLocomotive.Train;
            var triggered = (e.Time <= (int)Simulator.ClockTime - activity.StartTimeS);
            return triggered;
        }
    }
}
