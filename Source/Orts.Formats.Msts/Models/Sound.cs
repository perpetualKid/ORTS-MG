using System.Collections.Generic;
using System.IO;

using Microsoft.Xna.Framework;

using Orts.Common;
using Orts.Common.Position;
using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Models
{
    public class TrackItemSound
    {
        public List<WorldSoundSource> SoundSources { get; private set; } = new List<WorldSoundSource>();
        public List<WorldSoundRegion> SoundRegions { get; private set; } = new List<WorldSoundRegion>();

        public TrackItemSound(STFReader stf, TrackItem[] trItems)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("soundsource", ()=>{ SoundSources.Add(new WorldSoundSource(stf)); }),
                new STFReader.TokenProcessor("soundregion", ()=>{ SoundRegions.Add(new WorldSoundRegion(stf, trItems)); }),
            });
        }
    }

    public class WorldSoundSource
    {
        private Vector3 position;

        public float X => position.X;
        public float Y => position.Y;
        public float Z => position.Z;
        public ref readonly Vector3 Position => ref position;
        public string FileName { get; private set; }

        public WorldSoundSource(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("filename", ()=>{ FileName = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("position", ()=>{
                    stf.MustMatchBlockStart();
                    stf.ReadVector3Block(STFReader.Units.None, ref position);
                    stf.SkipRestOfBlock();
                }),
            });
        }
    }

    public class WorldSoundRegion
    {
        public int TrackType { get; private set; } = -1;
        public float RotY { get; private set; }
        public List<int> TrackNodes { get; private set; }

        public WorldSoundRegion(STFReader stf, TrackItem[] trItems)
        {
            TrackNodes = new List<int>();
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("soundregiontracktype", ()=>{ TrackType = stf.ReadIntBlock(-1); }),
                new STFReader.TokenProcessor("soundregionroty", ()=>{ RotY = stf.ReadFloatBlock(STFReader.Units.None, float.MaxValue); }),
                new STFReader.TokenProcessor("tritemid", ()=>{
                    stf.MustMatchBlockStart();
                    stf.ReadInt(0);//dummy read
                    var trItemId = stf.ReadInt(-1);
                    if (trItemId != -1) {
                        if (trItemId >= trItems.Length) {
                            STFException.TraceWarning(stf, $"Ignored invalid TrItemId {trItemId}");
                        } else {
                            TrackNodes.Add(trItemId);
                        }
                    }
                    stf.SkipRestOfBlock();
                }),
            });
        }
    }

    public class ActivitySound
    {
        private WorldLocation location;

        public string SoundFile { get; private set; }
        public OrtsActivitySoundFileType SoundFileType { get; private set; }
        public ref readonly WorldLocation Location => ref location;
        public ActivitySound(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("ortsactsoundfile", ()=>
                {
                    stf.MustMatchBlockStart();
                    string soundFile = stf.ReadString();
                    SoundFile = Path.Combine(FolderStructure.RouteFromActivity(stf.FileName).SoundFile(soundFile));
                    if (!EnumExtension.GetValue(stf.ReadString(), out OrtsActivitySoundFileType soundFileType))
                    {
                        stf.StepBackOneItem();
                        STFException.TraceInformation(stf, "Skipped unknown activity sound file type " + stf.ReadString());
                        SoundFileType = OrtsActivitySoundFileType.None;
                    }
                    else
                        SoundFileType = soundFileType;
                    stf.MustMatchBlockEnd();
                    }),
                new STFReader.TokenProcessor("ortssoundlocation", ()=>{
                    stf.MustMatchBlockStart();
                    location = new WorldLocation(stf.ReadInt(null), stf.ReadInt(null), 
                        stf.ReadFloat(STFReader.Units.None, null), stf.ReadFloat(STFReader.Units.None, null), stf.ReadFloat(STFReader.Units.None, null));
                    stf.MustMatchBlockEnd();
                }),
            });
        }
    }

    public class SoundActivationCondition
    {
        public bool ExternalCam { get; private set; }
        public bool CabCam { get; private set; }
        public bool PassengerCam { get; private set; }
        public float Distance { get; private set; } = 1000;  // by default we are 'in range' to hear this
        public int TrackType { get; private set; } = -1;
        public ActivationType ActivationType { get; private set; }

        public SoundActivationCondition(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("externalcam", ()=>{ ExternalCam = stf.ReadBoolBlock(true); }),
                new STFReader.TokenProcessor("cabcam", ()=>{ CabCam = stf.ReadBoolBlock(true); }),
                new STFReader.TokenProcessor("passengercam", ()=>{ PassengerCam = stf.ReadBoolBlock(true); }),
                new STFReader.TokenProcessor("distance", ()=>{ Distance = stf.ReadFloatBlock(STFReader.Units.Distance, Distance); }),
                new STFReader.TokenProcessor("tracktype", ()=>{ TrackType = stf.ReadIntBlock(null); }),
            });
        }

        // for precompiled sound sources for activity sound
        public SoundActivationCondition(OrtsActivitySoundFileType soundFileType, ActivationType activationType)
        {
            switch (soundFileType)
            {
                case OrtsActivitySoundFileType.Everywhere:
                    CabCam = activationType == ActivationType.Activate;
                    ExternalCam = activationType == ActivationType.Activate;
                    PassengerCam = activationType == ActivationType.Activate;
                    break;
                default:
                case OrtsActivitySoundFileType.Cab:
                    CabCam = activationType == ActivationType.Activate;
                    ExternalCam = activationType == ActivationType.Deactivate;
                    PassengerCam = activationType == ActivationType.Deactivate; 
                    break;
                case OrtsActivitySoundFileType.Pass:
                    CabCam = activationType == ActivationType.Deactivate;
                    ExternalCam = activationType == ActivationType.Deactivate;
                    PassengerCam = activationType == ActivationType.Activate;
                    break;
                case OrtsActivitySoundFileType.Ground:
                    CabCam = activationType == ActivationType.Activate;
                    ExternalCam = activationType == ActivationType.Activate;
                    PassengerCam = activationType == ActivationType.Activate;
                    break;
                case OrtsActivitySoundFileType.Location:
                    CabCam = activationType == ActivationType.Activate;
                    ExternalCam = activationType == ActivationType.Activate;
                    PassengerCam = activationType == ActivationType.Activate;
                    break;
            }
        }
    }

    public class ScalabiltyGroup
    {
        public int DetailLevel { get; private set; }
        public SmsStreams Streams { get; private set; }
        public float Volume { get; private set; } = 1.0f;
        public bool Stereo { get; private set; }
        public bool Ignore3D { get; private set; }
        public SoundActivationCondition Activation { get; private set; }
        public SoundActivationCondition Deactivation { get; private set; }

        public ScalabiltyGroup(STFReader stf)
        {
            stf.MustMatchBlockStart();
            DetailLevel = stf.ReadInt(null);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("activation", ()=>{ Activation = new SoundActivationCondition(stf); }),
                new STFReader.TokenProcessor("deactivation", ()=>{ Deactivation = new SoundActivationCondition(stf); }),
                new STFReader.TokenProcessor("streams", ()=>{ Streams = new SmsStreams(stf); }),
                new STFReader.TokenProcessor("volume", ()=>{ Volume = stf.ReadFloatBlock(STFReader.Units.None, null); }),
                new STFReader.TokenProcessor("stereo", ()=>{ Stereo = stf.ReadBoolBlock(true); }),
                new STFReader.TokenProcessor("ignore3d", ()=>{ Ignore3D = stf.ReadBoolBlock(true); }),
            });
        }
    } // class ScalabiltyGroup

    public class SmsStreams : List<SmsStream>
    {
        public SmsStreams(STFReader stf)
        {
            stf.MustMatchBlockStart();
            var count = stf.ReadInt(null);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("stream", ()=>{
                    if (--count < 0)
                        STFException.TraceWarning(stf, "Skipped extra Stream");
                    else
                        Add(new SmsStream(stf));
                }),
            });
            if (count > 0)
                STFException.TraceWarning(stf, count + " missing Stream(s)");
        }
    }

    public class SmsStream
    {
        public int Priority { get; private set; }
        public Triggers Triggers { get; private set; }
        public float Volume { get; private set; } = 1.0f;
        public List<Curve> VolumeCurves { get; } = new List<Curve>();
        public Curve FrequencyCurve { get; private set; }

        public SmsStream(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("priority", ()=>{ Priority = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("triggers", ()=>{ Triggers = new Triggers(stf); }),
                new STFReader.TokenProcessor("volumecurve", ()=>{ VolumeCurves.Add(new Curve(stf)); }),
                new STFReader.TokenProcessor("frequencycurve", ()=>{ FrequencyCurve = new Curve(stf); }),
                new STFReader.TokenProcessor("volume", ()=>{ Volume = stf.ReadFloatBlock(STFReader.Units.None, Volume); }),
            });
            //if (Volume > 1)  Volume /= 100f;
        }
    }

    public readonly struct CurvePoint
    {
        public readonly float X, Y;

        public CurvePoint(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    public class Curve
    {
        public enum ControlMode
        {
            None,
            Distance,
            Speed,
            Variable1,
            Variable2,
            Variable3,
            BrakeCylinder,
            CurveForce,
        };

        public ControlMode Mode { get; private set; } = ControlMode.None;

        public float Granularity { get; private set; } = 1.0f;

        public CurvePoint[] CurvePoints { get; private set; }

        public Curve(STFReader stf)
        {
            stf.MustMatchBlockStart();
            var type = stf.ReadString();
            switch (type.ToLower())
            {
                case "distancecontrolled": Mode = ControlMode.Distance; break;
                case "speedcontrolled": Mode = ControlMode.Speed; break;
                case "variable1controlled": Mode = ControlMode.Variable1; break;
                case "variable2controlled": Mode = ControlMode.Variable2; break;
                case "variable3controlled": Mode = ControlMode.Variable3; break;
                case "brakecylcontrolled": Mode = ControlMode.BrakeCylinder; break;
                case "curveforcecontrolled": Mode = ControlMode.CurveForce; break;
                default: STFException.TraceWarning(stf, "Crash expected: Skipped unknown VolumeCurve/Frequencycurve type " + type); stf.SkipRestOfBlock(); return;
            }
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("granularity", ()=>{ Granularity = stf.ReadFloatBlock(STFReader.Units.None, null); }),
                new STFReader.TokenProcessor("curvepoints", ()=>{
                    stf.MustMatchBlockStart();
                    int count = stf.ReadInt(null);
                    CurvePoints = new CurvePoint[count];
                    for (int i = 0; i < count; ++i)
                    {
                        float x = stf.ReadFloat(STFReader.Units.None, null);
                        if (Mode == ControlMode.Distance)
                        {
                            if (x >= 0)
                                x *= x;
                            else
                                x *= -x;
                        }
                        float y = stf.ReadFloat(STFReader.Units.None, null);
                        CurvePoints[i] = new CurvePoint(x, y);
                    }
                    stf.SkipRestOfBlock();
                }),
            });
        }
    }

    public class Triggers : List<Trigger>
    {
        public Triggers(STFReader stf)
        {
            stf.MustMatchBlockStart();
            int count = stf.ReadInt(null);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("dist_travelled_trigger", ()=>{ Add(new DistanceTravelledTrigger(stf)); }),
                new STFReader.TokenProcessor("discrete_trigger", ()=>{ Add(new DiscreteTrigger(stf)); }),
                new STFReader.TokenProcessor("random_trigger", ()=>{ Add(new RandomTrigger(stf)); }),
                new STFReader.TokenProcessor("variable_trigger", ()=>{ Add(new VariableTrigger(stf)); }),
                new STFReader.TokenProcessor("initial_trigger", ()=>{ Add(new InitialTrigger(stf)); }),
            });
            foreach (Trigger trigger in this)
                if (trigger.SoundCommand == null)
                    STFException.TraceWarning(stf, "Trigger lacks a sound command");
        }
    }

    public abstract class Trigger
    {
        public SoundCommand SoundCommand { get; private set; }

        private int playcommandCount;

        protected void ParsePlayCommand(STFReader stf, string token)
        {
            switch (token)
            {
                case "playoneshot":
                case "startloop":
                case "releaselooprelease":
                case "startlooprelease":
                case "releaseloopreleasewithjump":
                case "disabletrigger":
                case "enabletrigger":
                case "setstreamvolume":
                    ++playcommandCount;
                    if (playcommandCount > 1)
                        STFException.TraceWarning(stf, "Replaced play command");
                    break;
                default:
                    break;
            }

            switch (token)
            {
                case "playoneshot": SoundCommand = new SoundPlayCommand(stf, SoundPlayCommand.SoundCommandType.PlayOneShot); break;
                case "startloop": SoundCommand = new SoundPlayCommand(stf, SoundPlayCommand.SoundCommandType.StartLoop); break;
                case "releaselooprelease": SoundCommand = new LoopRelease(stf, LoopRelease.ReleaseType.Release); break;
                case "startlooprelease": SoundCommand = new SoundPlayCommand(stf, SoundPlayCommand.SoundCommandType.StartLoopRelease); break;
                case "releaseloopreleasewithjump": SoundCommand = new LoopRelease(stf, LoopRelease.ReleaseType.ReleaseWithJump); break;
                case "disabletrigger": SoundCommand = new TriggerCommand(stf, TriggerCommand.TriggerType.Disable); break;
                case "enabletrigger": SoundCommand = new TriggerCommand(stf, TriggerCommand.TriggerType.Enable); break;
                case "setstreamvolume": SoundCommand = new StreamVolumeCommand(stf); break;
                case "(": stf.SkipRestOfBlock(); break;
            }
        }
    }

    public class InitialTrigger : Trigger
    {

        public InitialTrigger(STFReader stf)
        {
            stf.MustMatchBlockStart();
            while (!stf.EndOfBlock())
                ParsePlayCommand(stf, stf.ReadString().ToLower());
        }
    }

    public class DiscreteTrigger : Trigger
    {

        public int TriggerId { get; private set; }

        public DiscreteTrigger(STFReader stf)
        {
            stf.MustMatchBlockStart();
            TriggerId = stf.ReadInt(null);
            while (!stf.EndOfBlock())
                ParsePlayCommand(stf, stf.ReadString().ToLower());
        }
    }

    public class VariableTrigger : Trigger
    {
        public enum TriggerEvent
        {
            SpeedIncrease,
            SpeedDecrease,
            DistanceIncrease,
            DistanceDecrease,
            Variable1Increase,
            Variable1Decrease,
            Variable2Increase,
            Variable2Decrease,
            Variable3Increase,
            Variable3Decrease,
            BrakeCylinderIncrease,
            BrakeCylinderDecrease,
            CurveForceIncrease,
            CurveForceDecrease,
        };

        public TriggerEvent Event { get; private set; }
        public float Threshold { get; private set; }

        public VariableTrigger(STFReader stf)
        {
            stf.MustMatchBlockStart();

            string eventString = stf.ReadString();
            Threshold = stf.ReadFloat(STFReader.Units.None, null);

            switch (eventString.ToLower())
            {
                case "speed_inc_past": Event = TriggerEvent.SpeedIncrease; break;
                case "speed_dec_past": Event = TriggerEvent.SpeedDecrease; break;
                case "distance_inc_past":
                    {
                        Event = TriggerEvent.DistanceIncrease;
                        Threshold *= Threshold;
                        break;
                    }
                case "distance_dec_past":
                    {
                        Event = TriggerEvent.DistanceDecrease;
                        Threshold *= Threshold;
                        break;
                    }
                case "variable1_inc_past": Event = TriggerEvent.Variable1Increase; break;
                case "variable1_dec_past": Event = TriggerEvent.Variable1Decrease; break;
                case "variable2_inc_past": Event = TriggerEvent.Variable2Increase; break;
                case "variable2_dec_past": Event = TriggerEvent.Variable2Decrease; break;
                case "variable3_inc_past": Event = TriggerEvent.Variable3Increase; break;
                case "variable3_dec_past": Event = TriggerEvent.Variable3Decrease; break;
                case "brakecyl_inc_past": Event = TriggerEvent.BrakeCylinderIncrease; break;
                case "brakecyl_dec_past": Event = TriggerEvent.BrakeCylinderDecrease; break;
                case "curveforce_inc_past": Event = TriggerEvent.CurveForceIncrease; break;
                case "curveforce_dec_past": Event = TriggerEvent.CurveForceDecrease; break;
            }

            while (!stf.EndOfBlock())
                ParsePlayCommand(stf, stf.ReadString().ToLower());
        }
    }

    public class DistanceTravelledTrigger : Trigger
    {
        public float MinimumDistance { get; private set; } = 80;
        public float MaximumDistance { get; private set; } = 100;
        public float MinimumVolume { get; private set; } = 0.9f;
        public float MaximumVolume { get; private set; } = 1.0f;

        public DistanceTravelledTrigger(STFReader stf)
        {
            stf.MustMatchBlockStart();
            while (!stf.EndOfBlock())
            {
                string token = stf.ReadString().ToLower();
                switch (token)
                {
                    case "dist_min_max": stf.MustMatchBlockStart(); MinimumDistance = stf.ReadFloat(STFReader.Units.Distance, null); MaximumDistance = stf.ReadFloat(STFReader.Units.Distance, null); stf.SkipRestOfBlock(); break;
                    case "volume_min_max": stf.MustMatchBlockStart(); MinimumVolume = stf.ReadFloat(STFReader.Units.None, null); MaximumVolume = stf.ReadFloat(STFReader.Units.None, null); stf.SkipRestOfBlock(); break;
                    default: ParsePlayCommand(stf, token); break;
                }
            }
        }
    }

    public class RandomTrigger : Trigger
    {
        public float MinimumDelay { get; private set; } = 80;
        public float MaximumDelay { get; private set; } = 100;
        public float MinimumVolume { get; private set; } = 0.9f;
        public float MaximumVolume { get; private set; } = 1.0f;

        public RandomTrigger(STFReader f)
        {
            f.MustMatchBlockStart();
            while (!f.EndOfBlock())
            {
                string token = f.ReadString().ToLower();
                switch (token)
                {
                    case "delay_min_max": f.MustMatchBlockStart(); MinimumDelay = f.ReadFloat(STFReader.Units.None, null); MaximumDelay = f.ReadFloat(STFReader.Units.None, null); f.SkipRestOfBlock(); break;
                    case "volume_min_max": f.MustMatchBlockStart(); MinimumVolume = f.ReadFloat(STFReader.Units.None, null); MaximumVolume = f.ReadFloat(STFReader.Units.None, null); f.SkipRestOfBlock(); break;
                    default: ParsePlayCommand(f, token); break;
                }
            }
        }
    }

    public abstract class SoundCommand
    {
    }

    public class StreamVolumeCommand : SoundCommand
    {
        public float Volume { get; private set; }

        public StreamVolumeCommand(STFReader stf)
        {
            stf.MustMatchBlockStart();
            Volume = stf.ReadFloat(STFReader.Units.None, null);
            stf.SkipRestOfBlock();
        }
    }

    public class TriggerCommand : SoundCommand
    {
        public enum TriggerType
        {
            Enable,
            Disable,
        }

        public int TriggerId { get; private set; }

        public TriggerType Trigger { get; private set; }

        public TriggerCommand(STFReader stf, TriggerType trigger)
        {
            Trigger = trigger;
            stf.MustMatchBlockStart();
            TriggerId = stf.ReadInt(null);
            stf.SkipRestOfBlock();
        }
    }

    public class LoopRelease : SoundCommand
    {
        public ReleaseType ReleaseMode { get; private set; }

        public enum ReleaseType
        {
            Release,
            ReleaseWithJump,
        }

        public LoopRelease(STFReader stf, ReleaseType mode)
        {
            ReleaseMode = mode;
            stf.MustMatchBlockStart();
            stf.SkipRestOfBlock();
        }
    }

    public class SoundPlayCommand : SoundCommand
    {
        public enum Selection
        {
            Random,
            Sequential,
        };

        public enum SoundCommandType
        {
            PlayOneShot,
            StartLoop,
            StartLoopRelease,
        }

        public string[] Files { get; protected set; }
        public Selection SelectionMethod { get; protected set; } = Selection.Sequential;

        public SoundCommandType CommandType { get; private set; }

        public SoundPlayCommand(STFReader stf, SoundCommandType commandType)
        {
            CommandType = commandType;
            stf.MustMatchBlockStart();
            int count = stf.ReadInt(null);
            Files = new string[count];
            int fileIndex = 0;
            while (!stf.EndOfBlock())
                switch (stf.ReadString().ToLower())
                {
                    case "file":
                        if (fileIndex < count)
                        {
                            stf.MustMatchBlockStart();
                            Files[fileIndex++] = stf.ReadString();
                            stf.ReadInt(null);
                            stf.SkipRestOfBlock();
                        }
                        else  // MSTS skips extra files
                        {
                            STFException.TraceWarning(stf, "Skipped extra File");
                            stf.SkipBlock();
                        }
                        break;
                    case "selectionmethod":
                        stf.MustMatchBlockStart();
                        string s = stf.ReadString();
                        switch (s.ToLower())
                        {
                            case "randomselection": SelectionMethod = Selection.Random; break;
                            case "sequentialselection": SelectionMethod = Selection.Sequential; break;
                            default: STFException.TraceWarning(stf, "Skipped unknown selection method " + s); break;
                        }
                        stf.SkipRestOfBlock();
                        break;
                    case "(": stf.SkipRestOfBlock(); break;
                }
        }
    }

}
