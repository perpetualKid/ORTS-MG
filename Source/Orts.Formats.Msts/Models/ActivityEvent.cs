using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Orts.Common;
using Orts.Common.Position;
using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Models
{
    /// <summary>
    /// Parses Event objects and saves them in EventList.
    /// </summary>
    public class ActivityEvents: List<ActivityEvent>
    {
        public ActivityEvents(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("eventcategorylocation", ()=>{ Add(new LocationActivityEvent(stf)); }),
                new STFReader.TokenProcessor("eventcategoryaction", ()=>{ Add(new ActionActivityEvent(stf)); }),
                new STFReader.TokenProcessor("eventcategorytime", ()=>{ Add(new TimeActivityEvent(stf)); }),
            });
        }

        public void UpdateORActivtyData(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("eventcategorylocation", ()=>{ TryModify(0, stf); }),
                new STFReader.TokenProcessor("eventcategoryaction", ()=>{ TryModify(1, stf); }),
                new STFReader.TokenProcessor("eventcategorytime", ()=>{ TryModify(2, stf); }),
            });
        }

        private void TryModify(int category, STFReader stf)
        {
            ActivityEvent origEvent;
            bool wrongEventID = false;
            int modifiedID = -1;
            try
            {
                stf.MustMatchBlockStart();
                stf.MustMatch("id");
                stf.MustMatchBlockStart();
                modifiedID = stf.ReadInt(null);
                stf.MustMatchBlockEnd();
                origEvent = Find(x => x.ID == modifiedID);
                if (origEvent == null)
                {
                    wrongEventID = true;
                    Trace.TraceWarning("Skipped event {0} not present in base activity file", modifiedID);
                    stf.SkipRestOfBlock();
                }
                else
                {
                    wrongEventID = !TestMatch(category, origEvent);
                    if (!wrongEventID)
                    {
                        origEvent.Update(stf);
                    }
                    else
                    {
                        Trace.TraceWarning("Skipped event {0} of event category not matching with base activity file", modifiedID);
                        stf.SkipRestOfBlock();
                    }
                }
            }
            catch (Exception error)
            {
                Trace.WriteLine(new FileLoadException("Error in additional activity file", error));
            }
        }

        private bool TestMatch(int category, ActivityEvent origEvent)
        {
            return 
                ((category == 0 && origEvent is LocationActivityEvent) ||
                (category == 1 && origEvent is ActionActivityEvent) ||
                (category == 2 && origEvent is TimeActivityEvent));
        }
    }

    /// <summary>
    /// The 3 types of event are inherited from the abstract Event class.
    /// </summary>
    public abstract class ActivityEvent
    {
        public int ID { get; protected set; }
        public string Name { get; protected set; }
        public int ActivationLevel { get; set; }
        public Outcomes Outcomes { get; protected set; }
        public string TextToDisplayOnCompletionIfTriggered { get; protected set; } = "";
        public string TextToDisplayOnCompletionIfNotTriggered { get; protected set; } = "";
        public bool Reversible { get; protected set; }
        public int OrtsContinue { get; protected set; } = -1;
        public string SoundFile { get; protected set; }
        public OrtsActivitySoundFileType SoundFileType { get; protected set; }
        public OrtsWeatherChange WeatherChange { get; protected set; }
        public string TrainService { get; protected set; } = "";
        public int TrainStartingTime { get; protected set; } = -1;

        public virtual void Update(STFReader stf)
        { }

        protected void OrtsActivitySoundProcessor(STFReader stf)
        {
            stf.MustMatchBlockStart();
            string soundFile = stf.ReadString();
            SoundFile = FolderStructure.RouteFromActivity(stf.FileName).SoundFile(soundFile);
            if (!EnumExtension.GetValue(stf.ReadString(), out OrtsActivitySoundFileType soundFileType))
            {
                stf.StepBackOneItem();
                STFException.TraceInformation(stf, "Skipped unknown activity sound file type " + stf.ReadString());
                SoundFileType = OrtsActivitySoundFileType.None;
            }
            else
                SoundFileType = soundFileType;
            stf.MustMatchBlockEnd();
        }
    }

    public class LocationActivityEvent : ActivityEvent
    {
        private WorldLocation location;
        public bool TriggerOnStop { get; private set; } // Value assumed if property not found.
        public ref readonly WorldLocation Location => ref location;
        public float RadiusM { get; private set; }

        public LocationActivityEvent(STFReader stf)
        {
            stf.MustMatchBlockStart();
            Update(stf);
        }

        public override void Update(STFReader stf)
        {
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("eventtypelocation", ()=>{ stf.MustMatchBlockStart(); stf.MustMatchBlockEnd(); }),
                new STFReader.TokenProcessor("id", ()=>{ ID = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("ortstriggeringtrain", ()=>{ ParseTrain(stf); }),
                new STFReader.TokenProcessor("activation_level", ()=>{ ActivationLevel = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("outcomes", ()=>
                {
                    if (Outcomes == null)
                        Outcomes = new Outcomes(stf);
                    else
                        Outcomes.Update(stf); }),
                new STFReader.TokenProcessor("name", ()=>{ Name = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("texttodisplayoncompletioniftriggered", ()=>{ TextToDisplayOnCompletionIfTriggered = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("texttodisplayoncompletionifnottriggered", ()=>{ TextToDisplayOnCompletionIfNotTriggered = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("triggeronstop", ()=>{ TriggerOnStop = stf.ReadBoolBlock(true); }),
                new STFReader.TokenProcessor("location", ()=>{
                    stf.MustMatchBlockStart();
                    location = new WorldLocation(stf.ReadInt(null), stf.ReadInt(null),
                        stf.ReadFloat(STFReader.Units.None, null), 0f, stf.ReadFloat(STFReader.Units.None, null));
                    RadiusM = stf.ReadFloat(STFReader.Units.Distance, null);
                    stf.MustMatchBlockEnd();
                }),
                new STFReader.TokenProcessor("ortscontinue", ()=>{ OrtsContinue = stf.ReadIntBlock(0); }),
                new STFReader.TokenProcessor("ortsactsoundfile", ()=> OrtsActivitySoundProcessor(stf)),
                new STFReader.TokenProcessor("ortsweatherchange", ()=>{ WeatherChange = new OrtsWeatherChange(stf);}),
            });
        }

        private void ParseTrain(STFReader stf)
        {
            stf.MustMatchBlockStart();
            TrainService = stf.ReadString();
            TrainStartingTime = stf.ReadInt(-1);
            stf.SkipRestOfBlock();
        }
    }

    /// <summary>
    /// Parses all types of action events.
    /// Save type of action event in Type. MSTS syntax isn't fully hierarchical, so using inheritance here instead of Type would be awkward. 
    /// </summary>
    public class ActionActivityEvent : ActivityEvent
    {
        public EventType Type { get; private set; }
        public WorkOrderWagons WorkOrderWagons { get; private set; }
        public uint? SidingId { get; private set; }  // May be specified inside the Wagon_List instead. Nullable as can't use -1 to indicate not set.
        public float SpeedMpS { get; private set; }

        public ActionActivityEvent(STFReader stf)
        {
            stf.MustMatchBlockStart();
            Update(stf);
        }

        public override void Update(STFReader stf)
        {
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("eventtypeallstops", ()=>{ stf.MustMatchBlockStart(); stf.MustMatchBlockEnd(); Type = EventType.AllStops; }),
                new STFReader.TokenProcessor("eventtypeassembletrain", ()=>{ stf.MustMatchBlockStart(); stf.MustMatchBlockEnd(); Type = EventType.AssembleTrain; }),
                new STFReader.TokenProcessor("eventtypeassembletrainatlocation", ()=>{ stf.MustMatchBlockStart(); stf.MustMatchBlockEnd(); Type = EventType.AssembleTrainAtLocation; }),
                new STFReader.TokenProcessor("eventtypedropoffwagonsatlocation", ()=>{ stf.MustMatchBlockStart(); stf.MustMatchBlockEnd(); Type = EventType.DropOffWagonsAtLocation; }),
                new STFReader.TokenProcessor("eventtypepickuppassengers", ()=>{ stf.MustMatchBlockStart(); stf.MustMatchBlockEnd(); Type = EventType.PickUpPassengers; }),
                new STFReader.TokenProcessor("eventtypepickupwagons", ()=>{ stf.MustMatchBlockStart(); stf.MustMatchBlockEnd(); Type = EventType.PickUpWagons; }),
                new STFReader.TokenProcessor("eventtypereachspeed", ()=>{ stf.MustMatchBlockStart(); stf.MustMatchBlockEnd(); Type = EventType.ReachSpeed; }),
                new STFReader.TokenProcessor("id", ()=>{ ID = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("activation_level", ()=>{ ActivationLevel = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("outcomes", ()=>
                {
                    if (Outcomes == null)
                        Outcomes = new Outcomes(stf);
                    else
                        Outcomes.Update(stf); }),
                new STFReader.TokenProcessor("texttodisplayoncompletioniftriggered", ()=>{ TextToDisplayOnCompletionIfTriggered = stf.ReadStringBlock(""); }),
                new STFReader.TokenProcessor("texttodisplayoncompletionifnotrriggered", ()=>{ TextToDisplayOnCompletionIfNotTriggered = stf.ReadStringBlock(""); }),
                new STFReader.TokenProcessor("name", ()=>{ Name = stf.ReadStringBlock(""); }),
                new STFReader.TokenProcessor("wagon_list", ()=>{ WorkOrderWagons = new WorkOrderWagons(stf, Type); }),
                new STFReader.TokenProcessor("sidingitem", ()=>{ SidingId = (uint)stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("speed", ()=>{ SpeedMpS = stf.ReadFloatBlock(STFReader.Units.Speed, null); }),
                new STFReader.TokenProcessor("reversable_event", ()=>{ stf.MustMatchBlockStart(); stf.MustMatchBlockEnd(); Reversible = true; }),
                // Also support the correct spelling !
                new STFReader.TokenProcessor("reversible_event", ()=>{ stf.MustMatchBlockStart(); stf.MustMatchBlockEnd(); Reversible = true; }),
                new STFReader.TokenProcessor("ortscontinue", ()=>{ OrtsContinue = stf.ReadIntBlock(0); }),
                new STFReader.TokenProcessor("ortsactsoundfile", ()=> OrtsActivitySoundProcessor(stf)),
            });
        }
    }

    public class TimeActivityEvent : ActivityEvent
    {  // E.g. Hisatsu route and Short Passenger Run shrtpass.act
        public int Time { get; private set; }

        public TimeActivityEvent(STFReader stf)
        {
            stf.MustMatchBlockStart();
            Update(stf);
        }

        public override void Update(STFReader stf)
        {
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("id", ()=>{ ID = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("activation_level", ()=>{ ActivationLevel = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("outcomes", ()=>
                {
                    if (Outcomes == null)
                        Outcomes = new Outcomes(stf);
                    else
                        Outcomes.Update(stf); }),
                new STFReader.TokenProcessor("texttodisplayoncompletioniftriggered", ()=>{ TextToDisplayOnCompletionIfTriggered = stf.ReadStringBlock(""); }),
                new STFReader.TokenProcessor("texttodisplayoncompletionifnotrriggered", ()=>{ TextToDisplayOnCompletionIfNotTriggered = stf.ReadStringBlock(""); }),
                new STFReader.TokenProcessor("name", ()=>{ Name = stf.ReadStringBlock(""); }),
                new STFReader.TokenProcessor("time", ()=>{ Time = (int)stf.ReadFloatBlock(STFReader.Units.Time, null); }),
                new STFReader.TokenProcessor("ortscontinue", ()=>{ OrtsContinue = stf.ReadIntBlock(0); }),
                new STFReader.TokenProcessor("ortsactsoundfile", ()=> OrtsActivitySoundProcessor(stf)),
                new STFReader.TokenProcessor("ortsweatherchange", ()=>{ WeatherChange = new OrtsWeatherChange(stf);}),
            });
        }
    }

    public class OrtsWeatherChange
    {
        public float Overcast {get; private set;}
        public int OvercastTransitionTime { get; private set; }
        public float Fog { get; private set; }
        public int FogTransitionTime { get; private set; }
        public float PrecipitationIntensity { get; private set; }
        public int PrecipitationIntensityTransitionTime { get; private set; }
        public float PrecipitationLiquidity { get; private set; }
        public int PrecipitationLiquidityTransitionTime { get; private set; }

        public OrtsWeatherChange(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("ortsovercast", ()=>
                {
                    stf.MustMatchBlockStart();
                    Overcast = stf.ReadFloat(STFReader.Units.None, -1);
                    OvercastTransitionTime = stf.ReadInt(-1);
                    stf.MustMatchBlockEnd();
                }),
                new STFReader.TokenProcessor("ortsfog", ()=>
                {
                    stf.MustMatchBlockStart();
                    Fog = stf.ReadFloat(STFReader.Units.None, -1);
                    FogTransitionTime = stf.ReadInt(-1);
                    stf.MustMatchBlockEnd();
                }),
                new STFReader.TokenProcessor("ortsprecipitationintensity", ()=>
                {
                    stf.MustMatchBlockStart();
                    PrecipitationIntensity = stf.ReadFloat(STFReader.Units.None, -1);
                    PrecipitationIntensityTransitionTime = stf.ReadInt(-1);
                    stf.MustMatchBlockEnd();
                }),
                               new STFReader.TokenProcessor("ortsprecipitationliquidity", ()=>
                {
                    stf.MustMatchBlockStart();
                    PrecipitationLiquidity = stf.ReadFloat(STFReader.Units.None, -1);
                    PrecipitationLiquidityTransitionTime = stf.ReadInt(-1);
                    stf.MustMatchBlockEnd();
                })
            });
        }
    }

    public class Outcomes
    {
        public bool ActivitySuccess { get; private set; }
        public string ActivityFail { get; private set; }
        // MSTS Activity Editor limits model to 4 outcomes of any type. We use lists so there is no restriction.
        public List<int> ActivateList { get; } = new List<int>();
        public List<int> RestoreActivityLevels { get; } = new List<int>();
        public List<int> DecrementActivityLevels { get; } = new List<int>();
        public List<int> IncrementActivityLevels { get; } = new List<int>();
        public string DisplayMessage { get; private set; }
        //       public string WaitingTrainToRestart;
        public RestartWaitingTrain RestartWaitingTrain { get; private set; }
        public OrtsWeatherChange WeatherChange { get; private set; }
        public ActivitySound ActivitySound { get; private set; }

        public Outcomes(STFReader stf)
        {
            Update(stf);
        }

        public void Update(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("activitysuccess", ()=>{ stf.MustMatchBlockStart(); stf.MustMatchBlockEnd(); ActivitySuccess = true; }),
                new STFReader.TokenProcessor("activityfail", ()=>{ ActivityFail = stf.ReadStringBlock(""); }),
                new STFReader.TokenProcessor("activateevent", ()=>{ ActivateList.Add(stf.ReadIntBlock(null)); }),
                new STFReader.TokenProcessor("restoreactlevel", ()=>{ RestoreActivityLevels.Add(stf.ReadIntBlock(null)); }),
                new STFReader.TokenProcessor("decactlevel", ()=>{ DecrementActivityLevels.Add(stf.ReadIntBlock(null)); }),
                new STFReader.TokenProcessor("incactlevel", ()=>{ IncrementActivityLevels.Add(stf.ReadIntBlock(null)); }),
                new STFReader.TokenProcessor("displaymessage", ()=>{
                    DisplayMessage = stf.ReadStringBlock(""); }),
                new STFReader.TokenProcessor("ortsrestartwaitingtrain", ()=>{ RestartWaitingTrain = new RestartWaitingTrain(stf); }),
                new STFReader.TokenProcessor("ortsweatherchange", ()=>{ WeatherChange = new OrtsWeatherChange(stf);}),
                new STFReader.TokenProcessor("ortsactivitysound", ()=>{ ActivitySound = new ActivitySound(stf);}),
            });
        }
    }

}
