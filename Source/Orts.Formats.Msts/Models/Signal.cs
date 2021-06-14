using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Xna.Framework;

using Orts.Common;
using Orts.Common.Calc;
using Orts.Common.Xna;
using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Models
{

    #region LightTexture
    /// <summary>
    /// Defines a single light texture, used as background to draw lit lights onto signals
    /// </summary>
    public class LightTexture
    {
        private readonly Matrix2x2 uv;

        /// <summary>Name of the light texture</summary>
        public string Name { get; private set; }
        /// <summary>Filename of the texture</summary>
        public string TextureFile { get; private set; }
        /// <summary>coordinates within texture (0.0 to 1.0) U-horizontally left to right, V-Vertically top to bottom</summary>
        public ref readonly Matrix2x2 TextureCoordinates => ref uv;

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        internal LightTexture(STFReader stf)
        {
            stf.MustMatchBlockStart();
            Name = stf.ReadString();
            TextureFile = stf.ReadString();
            uv = new Matrix2x2(stf.ReadFloat(null), stf.ReadFloat(null), stf.ReadFloat(null), stf.ReadFloat(null));
            stf.SkipRestOfBlock();
        }
    }
    #endregion

    #region LightTableEntry
    /// <summary>
    /// Describes how to draw a light in its illuminated state
    /// </summary>
    public class LightTableEntry
    {
        /// <summary>Name of the light</summary>
        public string Name { get; private set; }

        public Color Color { get; private set; }

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        internal LightTableEntry(STFReader stf)
        {
            stf.MustMatchBlockStart();
            Name = stf.ReadString();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("colour", ()=>{
                    stf.MustMatchBlockStart();
                    int alpha = stf.ReadInt(null);
                    Color = new Color(stf.ReadInt(null), stf.ReadInt(null), stf.ReadInt(null), alpha);
                    stf.SkipRestOfBlock();
                }),
            });
        }
    }
    #endregion

    #region OR Signal Types
    //using a Singleton instance for OR Signal Types read from SigCfg.dat
    //these are really an extension to the static MSTS signal types/functions
    //Static/singleton is used to avoid overly passing arround the values, while needed in very few distinct places only
    public class OrSignalTypes
    {
        private static OrSignalTypes instance;

        /// <summary>List of OR defined function types</summary>
        public List<string> FunctionTypes { get; } = new List<string>();
        /// <summary>List of OR defined subtypes for Norman signals</summary>
        public List<string> NormalSubTypes { get; } = new List<string>();

        public static OrSignalTypes Instance
        {
            get
            {
                if (null == instance)
                    instance = new OrSignalTypes();
                return instance;
            }
        }

        public static void Reset()
        {
            instance = null;
        }
    }
    #endregion

    #region SignalType
    /// <summary>
    /// Signal Type which defines the attributes of a type or category of signal-heads
    /// </summary>
    public class SignalType
    {
        /// <summary>
        /// Describe the function of a particular signal head.
        /// Only SIGFN_NORMAL signal heads will require a train to take action (e.g. to stop).  
        /// The other values act only as categories for signal types to belong to.
        /// Within MSTS and scripts known as SIGFN_ values.  
        /// </summary>

        /// <summary></summary>
        public string Name { get; private set; }
        /// allocated script
        public string Script { get; private set; } = string.Empty;
        /// <summary>MSTS Function type (normal, speed, ...) of this signal type </summary>
        public SignalFunction FunctionType { get; private set; }
        /// <summary>OR Function type (additional function types may be set using OR_FUNCTIONTYPES).</summary>
        public int OrtsFunctionTypeIndex { get; private set; }
        /// <summary>OR Additional subtype for Normal signals</summary>
        public int OrtsNormalSubTypeIndex { get; private set; }
        /// <summary>Unknown, used at least in Marias Pass route</summary>
        public bool Abs { get; private set; }
        /// <summary>This signal type is not suitable for placement on a gantry</summary>
        public bool NoGantry { get; private set; }
        /// <summary>This is a semaphore signal</summary>
        public bool Semaphore { get; private set; }
        /// <summary>On duration for flashing light. (In seconds.)</summary>
        public float FlashTimeOn { get; private set; }
        /// <summary>Off duration for flashing light. (In seconds.)</summary>
        public float FlashTimeOff { get; private set; }
        /// <summary>Transition time between lit and unlit states. (In seconds.)</summary>
        public float TransitionTime { get; private set; }
        /// <summary>The name of the texture to use for the lights</summary>
        public string LightTextureName { get; private set; }
        /// <summary></summary>
        public IList<SignalLight> Lights { get; private set; }
        /// <summary>Name-indexed draw states</summary>
        public Dictionary<string, SignalDrawState> DrawStates { get; private set; }
        /// <summary>List of aspects this signal type can have</summary>
        public IList<SignalAspect> Aspects { get; private set; }
        /// <summary>Number of blocks ahead which need to be cleared in order to maintain a 'clear' indication
        /// in front of a train. MSTS calculation</summary>
        public int ClearAheadNumberMsts { get; private set; }
        /// <summary>Number of blocks ahead which need to be cleared in order to maintain a 'clear' indication
        /// in front of a train. ORTS calculation</summary>
        public int ClearAheadNumberOrts { get; private set; }
        /// <summary>Number of seconds to spend animating a semaphore signal.</summary>
        public float SemaphoreInfo { get; private set; }
        public ApproachControlLimits ApproachControlDetails { get; private set; }

        /// <summary> Glow value for daytime (optional).</summary>
        public float? DayGlow { get; private set; }
        /// <summary> Glow value for nighttime (optional).</summary>
        public float? NightGlow { get; private set; }
        /// <summary> Lights switched off or on during daytime (default : on) (optional).</summary>
        public bool DayLight { get; private set; } = true;

        /// <summary>
        /// Common initialization part for constructors
        /// </summary>
        private SignalType()
        {
            SemaphoreInfo = 1; // Default animation time for semaphore signals (1 second).
            LightTextureName = string.Empty;
            FlashTimeOn = 1.0f;
            FlashTimeOff = 1.0f;
            TransitionTime = 0.2f;
        }

        /// <summary>
        /// Constructor for dummy entries
        /// </summary>
        public SignalType(SignalFunction function, SignalAspectState aspect)
            : this()
        {
            FunctionType = function;
            Name = "UNDEFINED";
            Semaphore = false;
            DrawStates = new Dictionary<string, SignalDrawState>(StringComparer.OrdinalIgnoreCase)
            {
                { "CLEAR", new SignalDrawState("CLEAR", 1) }
            };
            Aspects = new List<SignalAspect>
            {
                new SignalAspect(aspect, "CLEAR")
            };
        }

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        /// <param name="orMode">Process SignalType for ORTS mode (always set NumClearAhead_ORTS only)</param>
        internal SignalType(STFReader stf, bool orMode)
            : this()
        {
            stf.MustMatchBlockStart();
            Name = stf.ReadString();
            int numClearAhead = -2;
            int numdefs = 0;
            string ortsFunctionType = string.Empty;
            string ortsNormalSubType = string.Empty;

            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("ortsscript", ()=>{ Script = stf.ReadStringBlock(""); }),
                new STFReader.TokenProcessor("signalfntype", ()=>{
                    if (orMode)
                        ortsFunctionType = ReadOrtsFunctionType(stf);
                    else
                        FunctionType = ReadFunctionType(stf);
                }),
                new STFReader.TokenProcessor("signallighttex", ()=>{ LightTextureName = stf.ReadStringBlock(""); }),
                new STFReader.TokenProcessor("signallights", ()=>{ Lights = ReadLights(stf); }),
                new STFReader.TokenProcessor("signaldrawstates", ()=>{ DrawStates = ReadDrawStates(stf); }),
                new STFReader.TokenProcessor("signalaspects", ()=>{ Aspects = ReadAspects(stf); }),
                new STFReader.TokenProcessor("approachcontrolsettings", ()=>{ ApproachControlDetails = ReadApproachControlDetails(stf); }),
                new STFReader.TokenProcessor("signalnumclearahead", ()=>{ numClearAhead = numClearAhead >= -1 ? numClearAhead : stf.ReadIntBlock(null); numdefs++;}),
                new STFReader.TokenProcessor("semaphoreinfo", ()=>{ SemaphoreInfo = stf.ReadFloatBlock(STFReader.Units.None, null); }),
                new STFReader.TokenProcessor("ortsdayglow", ()=>{ DayGlow = stf.ReadFloatBlock(STFReader.Units.None, null); }),
                new STFReader.TokenProcessor("ortsnightglow", ()=>{ NightGlow = stf.ReadFloatBlock(STFReader.Units.None, null); }),
                new STFReader.TokenProcessor("ortsdaylight", ()=>{ DayLight = stf.ReadBoolBlock(true); }),
                new STFReader.TokenProcessor("ortsnormalsubtype", ()=>{ ortsNormalSubType = ReadOrtsNormalSubType(stf); }),
                new STFReader.TokenProcessor("ortsonofftime", ()=>{ TransitionTime = stf.ReadFloatBlock(STFReader.Units.None, null); }),
                new STFReader.TokenProcessor("sigflashduration", ()=>{
                    stf.MustMatchBlockStart();
                    FlashTimeOn = stf.ReadFloat(STFReader.Units.None, null);
                    FlashTimeOff = stf.ReadFloat(STFReader.Units.None, null);
                    stf.SkipRestOfBlock();
                }),
                new STFReader.TokenProcessor("signalflags", ()=>{
                    stf.MustMatchBlockStart();
                    while (!stf.EndOfBlock())
                        switch (stf.ReadString().ToUpperInvariant())
                        {
                            case "ABS": Abs = true; break;
                            case "NO_GANTRY": NoGantry = true; break;
                            case "SEMAPHORE": Semaphore = true; break;
                            default: stf.StepBackOneItem(); STFException.TraceInformation(stf, "Skipped unknown SignalType flag " + stf.ReadString()); break;
                        }
                }),
            });

            if (orMode)
            {
                // set related MSTS function type
                OrtsFunctionTypeIndex = OrSignalTypes.Instance.FunctionTypes.FindIndex(i => StringComparer.OrdinalIgnoreCase.Equals(i, ortsFunctionType));
                if (!EnumExtension.GetValue(ortsFunctionType, out SignalFunction functionType))
                    FunctionType = SignalFunction.Info;
                else
                    FunctionType = functionType;

                // set index for Normal Subtype
                OrtsNormalSubTypeIndex = OrSignalTypes.Instance.NormalSubTypes.FindIndex(i => StringComparer.OrdinalIgnoreCase.Equals(i, ortsNormalSubType));

                // set SNCA
                ClearAheadNumberMsts = -2;
                ClearAheadNumberOrts = numClearAhead;
            }
            else
            {
                // set defaulted OR function type
                OrtsFunctionTypeIndex = (int)FunctionType;

                // set SNCA
#pragma warning disable CA1508 // Avoid dead conditional code
                ClearAheadNumberMsts = numdefs == 1 ? numClearAhead : -2;
                ClearAheadNumberOrts = numdefs == 2 ? numClearAhead : -2;
#pragma warning restore CA1508 // Avoid dead conditional code
            }
        }

        private static SignalFunction ReadFunctionType(STFReader stf)
        {
            string signalType = stf.ReadStringBlock(null);
            if (!EnumExtension.GetValue(signalType, out SignalFunction result))
            {
                STFException.TraceInformation(stf, $"Skipped unknown SignalFnType {signalType}");
                return SignalFunction.Info;
            }
            return result;
        }

        private static string ReadOrtsFunctionType(STFReader stf)
        {
            string type = stf.ReadStringBlock(null);
            if (OrSignalTypes.Instance.FunctionTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
            {
                return type;
            }
            else
            {
                STFException.TraceInformation(stf, "Skipped unknown ORTSSignalFnType " + type);
                return SignalFunction.Info.ToString();
            }
        }

        private static string ReadOrtsNormalSubType(STFReader stf)
        {
            string type = stf.ReadStringBlock(null);
            if (OrSignalTypes.Instance.NormalSubTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
            {
                return (type);
            }
            else
            {
                STFException.TraceInformation(stf, "Skipped unknown ORTSNormalSubtype " + type);
                return (string.Empty);
            }
        }

        private static List<SignalLight> ReadLights(STFReader stf)
        {
            stf.MustMatchBlockStart();
            int count = stf.ReadInt(null);
            List<SignalLight> lights = new List<SignalLight>(count);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("signallight", ()=>{
                    if (lights.Count >= lights.Capacity)
                        STFException.TraceWarning(stf, "Skipped extra SignalLight");
                    else
                        lights.Add(new SignalLight(stf));
                }),
            });
            lights.Sort(SignalLight.Comparer);
            for (int i = 0; i < lights.Count; i++)
                if (lights[i].Index != i)
                    STFException.TraceWarning(stf, $"Invalid SignalLight index; expected {i}, got {lights[i].Index}");
            return lights;
        }

        private static Dictionary<string, SignalDrawState> ReadDrawStates(STFReader stf)
        {
            stf.MustMatchBlockStart();
            int count = stf.ReadInt(null);
            Dictionary<string, SignalDrawState> drawStates = new Dictionary<string, SignalDrawState>(count, StringComparer.OrdinalIgnoreCase);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("signaldrawstate", ()=>{
                    if (drawStates.Count >= count)
                        STFException.TraceWarning(stf, "Skipped extra SignalDrawState");
                    else
                    {
                        SignalDrawState drawState = new SignalDrawState(stf);
                        if (drawStates.ContainsKey(drawState.Name))
                        {
                            string newState = $"DST{drawStates.Count}";
                            drawStates.Add(newState, drawState);
                            STFException.TraceInformation(stf, $"Duplicate SignalDrawState name \'{drawState.Name}\', using name \'{newState}\' instead");
                        }
                        else
                        {
                            drawStates.Add(drawState.Name, drawState);
                        }
                    }
                }),
            });
            if (drawStates.Count < count)
                STFException.TraceWarning(stf, $"{count - drawStates.Count} missing SignalDrawState(s)");
            return drawStates;
        }

        private static List<SignalAspect> ReadAspects(STFReader stf)
        {
            stf.MustMatchBlockStart();
            int count = stf.ReadInt(null);
            List<SignalAspect> aspects = new List<SignalAspect>(count);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("signalaspect", ()=>{
                    if (aspects.Count >= aspects.Capacity)
                        STFException.TraceWarning(stf, "Skipped extra SignalAspect");
                    else
                    {
                        SignalAspect aspect = new SignalAspect(stf);
                        if (aspects.Any(sa => sa.Aspect == aspect.Aspect))
                            STFException.TraceWarning(stf, "Skipped duplicate SignalAspect " + aspect.Aspect);
                        else
                            aspects.Add(aspect);
                    }
                }),
            });
            return aspects;
        }

        private static ApproachControlLimits ReadApproachControlDetails(STFReader stf)
        {
            stf.MustMatchBlockStart();
            return new ApproachControlLimits(stf);
        }

        /// <summary>
        /// This method returns the default draw state for the specified aspect or -1 if none.
        /// </summary>
        public int GetDefaultDrawState(SignalAspectState state)
        {
            for (int i = 0; i < Aspects.Count; i++)
            {
                if (state == Aspects[i].Aspect)
                {
                    return DrawStates[Aspects[i].DrawStateName].Index;
                }
            }
            return -1;
        }

        /// <summary>
        /// This method returns the next least restrictive aspect from the one specified.
        /// </summary>
        public SignalAspectState GetNextLeastRestrictiveState(SignalAspectState state)
        {
            SignalAspectState targetState = SignalAspectState.Unknown;
            SignalAspectState leastState = SignalAspectState.Stop;

            for (int i = 0; i < Aspects.Count; i++)
            {
                if (Aspects[i].Aspect > leastState)
                    leastState = Aspects[i].Aspect;
                if (Aspects[i].Aspect > state && Aspects[i].Aspect < targetState)
                    targetState = Aspects[i].Aspect;
            }
            return (targetState == SignalAspectState.Unknown) ? leastState : targetState;
        }

        /// <summary>
        /// This method returns the most restrictive aspect for this signal type.
        /// </summary>
        public SignalAspectState GetMostRestrictiveAspect()
        {
            SignalAspectState targetAspect = SignalAspectState.Unknown;
            for (int i = 0; i < Aspects.Count; i++)
            {
                if (Aspects[i].Aspect < targetAspect) targetAspect = Aspects[i].Aspect;
            }
            return (targetAspect == SignalAspectState.Unknown) ? SignalAspectState.Stop : targetAspect;
        }

        /// <summary>
        /// This method returns the least restrictive aspect for this signal type.
        /// [Rob Roeterdink] added for basic signals without script
        /// </summary>
        public SignalAspectState GetLeastRestrictiveAspect()
        {
            SignalAspectState targetAspect = SignalAspectState.Stop;
            for (int i = 0; i < Aspects.Count; i++)
            {
                if (Aspects[i].Aspect > targetAspect)
                    targetAspect = Aspects[i].Aspect;
            }
            return (targetAspect > SignalAspectState.Clear_2) ? SignalAspectState.Clear_2 : targetAspect;
        }

        /// <summary>
        /// This method returns the lowest speed limit linked to the aspect
        /// </summary>
        public float GetSpeedLimit(SignalAspectState aspect)
        {
            return Aspects.First((a) => a.Aspect == aspect)?.SpeedLimit ?? -1;
        }

    }
    #endregion

    #region SignalLight
    /// <summary>
    /// Describes the a light on a signal, so the location and size of a signal light,
    /// as well as a reference to a light from the lights table
    /// </summary>
    public class SignalLight
    {
        private Vector3 position;
        /// <summary>Index in the list of signal lights</summary>
        public int Index { get; private set; }
        /// <summary>Name of the reference light from the lights table</summary>
        public string Name { get; private set; }
        public ref readonly Vector3 Position => ref position;
        /// <summary>Radius of the light</summary>
        public float Radius { get; private set; }
        /// <summary>is the SIGLIGHT flag SEMAPHORE_CHANGE set?</summary>
        public bool SemaphoreChange { get; private set; }

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        internal SignalLight(STFReader stf)
        {
            stf.MustMatchBlockStart();
            Index = stf.ReadInt(null);
            Name = stf.ReadString();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("radius", ()=>{ Radius = stf.ReadFloatBlock(STFReader.Units.None, null); }),
                new STFReader.TokenProcessor("position", ()=>{
                    stf.MustMatchBlockStart();
                    position = new Vector3(stf.ReadFloat(null), stf.ReadFloat(null), stf.ReadFloat(null));
                    stf.SkipRestOfBlock();
                }),
                new STFReader.TokenProcessor("signalflags", ()=>{
                    stf.MustMatchBlockStart();
                    while (!stf.EndOfBlock())
                        switch (stf.ReadString().ToUpperInvariant())
                        {
                            case "SEMAPHORE_CHANGE":
                                SemaphoreChange = true;
                                break;
                            default:
                                stf.StepBackOneItem();
                                STFException.TraceInformation(stf, "Skipped unknown SignalLight flag " + stf.ReadString());
                                break;
                        }
                }),
            });
        }

        /// <summary>
        /// Comparator function for ordering signal lights
        /// </summary>
        /// <param name="lightA">first light to compare</param>
        /// <param name="lightB">second light to compare</param>
        /// <returns>integer describing whether first light needs to be sorted before second light (so less than 0, 0, or larger than 0)</returns>
        public static int Comparer(SignalLight lightA, SignalLight lightB)
        {
            return lightA?.Index - lightB?.Index ?? 0;
        }
    }
    #endregion

    #region SignalDrawState
    /// <summary>
    /// Describes a draw state: a single combination of lights and semaphore arm positions that go together.
    /// </summary>
    public class SignalDrawState
    {
        /// <summary>Index in the list of draw states</summary>
        public int Index { get; private set; }
        /// <summary>Name identifying the draw state</summary>
        public string Name { get; private set; }
        /// <summary>The lights to draw in this state</summary>
        public IList<SignalDrawLight> DrawLights { get; private set; }
        /// <summary>The position of the semaphore for this draw state (as a keyframe)</summary>
        public float SemaphorePosition { get; private set; }

        /// <summary>
        /// constructor for dummy entries
        /// </summary>
        /// <param name="name">Requested name</param>
        /// <param name="index">Requested index</param>
        public SignalDrawState(string name, int index)
        {
            Index = index;
            Name = name;
        }

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        internal SignalDrawState(STFReader stf)
        {
            stf.MustMatchBlockStart();
            Index = stf.ReadInt(null);
            Name = stf.ReadString();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("drawlights", ()=>{ DrawLights = ReadDrawLights(stf); }),
                new STFReader.TokenProcessor("semaphorepos", ()=>{ SemaphorePosition = stf.ReadFloatBlock(STFReader.Units.None, 0); }),
            });
        }

        private static List<SignalDrawLight> ReadDrawLights(STFReader stf)
        {
            stf.MustMatchBlockStart();
            int count = stf.ReadInt(null);
            List<SignalDrawLight> drawLights = new List<SignalDrawLight>(count);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("drawlight", ()=>{
                    if (drawLights.Count >= drawLights.Capacity)
                        STFException.TraceWarning(stf, "Skipped extra DrawLight");
                    else
                        drawLights.Add(new SignalDrawLight(stf));
                }),
            });
            return drawLights;
        }

        /// <summary>
        /// Comparator function for ordering signal draw states
        /// </summary>
        /// <param name="drawStateA">first draw state to compare</param>
        /// <param name="drawStateB">second draw state to compare</param>
        /// <returns>integer describing whether first draw state needs to be sorted before second state (so less than 0, 0, or larger than 0)</returns>
        public static int Comparer(SignalDrawState drawStateA, SignalDrawState drawStateB)
        {
            return drawStateA?.Index - drawStateB?.Index ?? 0;
        }
    }
    #endregion

    #region SignalDrawLight
    /// <summary>
    /// Describes a single light to be drawn as part of a draw state
    /// </summary>
    public class SignalDrawLight
    {
        /// <summary>Index in the list of draw lights</summary>
        public int Index { get; private set; }
        /// <summary>Is the light flashing or not</summary>
        public bool Flashing { get; private set; }

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        internal SignalDrawLight(STFReader stf)
        {
            stf.MustMatchBlockStart();
            Index = stf.ReadInt(null);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("signalflags", ()=>{
                    stf.MustMatchBlockStart();
                    while (!stf.EndOfBlock())
                        switch (stf.ReadString().ToUpperInvariant())
                        {
                            case "FLASHING":
                                Flashing = true;
                                break;
                            default:
                                stf.StepBackOneItem();
                                STFException.TraceInformation(stf, "Skipped unknown DrawLight flag " + stf.ReadString());
                                break;
                        }
                }),
            });
        }
    }
    #endregion

    #region SignalAspect
    /// <summary>
    /// Describes an signal aspect, a combination of a signal indication state and what it means to be in that state.
    /// </summary>
    public class SignalAspect
    {
        /// <summary>The signal aspect or rather signal indication state itself</summary>
        public SignalAspectState Aspect { get; private set; }
        /// <summary>The name of the Draw State for this signal aspect</summary>
        public string DrawStateName { get; private set; }
        /// <summary>Speed limit (meters per second) for this aspect. -1 if track speed is to be used</summary>
        public float SpeedLimit { get; private set; }
        /// <summary>Set to true if SignalFlags ASAP option specified, meaning train needs to go to speed As Soon As Possible</summary>
        public bool Asap { get; private set; }
        /// <summary>Set to true if SignalFlags RESET option specified (ORTS only)</summary>
        public bool Reset { get; private set; }
        /// <summary>Set to true if no speed reduction is required for RESTRICTED or STOP_AND_PROCEED aspects (ORTS only) </summary>
        public bool NoSpeedReduction { get; private set; }

        /// <summary>
        /// constructor for dummy entries
        /// </summary>
        /// <param name="reqAspect">Requested aspect</param>
        /// <param name="reqName">Requested drawstate name</param>
        public SignalAspect(SignalAspectState aspect, string name)
        {
            Aspect = aspect;
            DrawStateName = name;
            SpeedLimit = -1;
        }

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        internal SignalAspect(STFReader stf)
        {
            SpeedLimit = -1;
            stf.MustMatchBlockStart();
            string aspectName = stf.ReadString();
            if (!EnumExtension.GetValue(aspectName, out SignalAspectState aspect))
            {
                STFException.TraceInformation(stf, "Skipped unknown signal aspect " + aspectName);
                Aspect = SignalAspectState.Unknown;
            }
            else
                Aspect = aspect;
            DrawStateName = stf.ReadString();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("speedmph", ()=>{ SpeedLimit = (float)Speed.MeterPerSecond.FromMpH(stf.ReadFloatBlock(STFReader.Units.None, 0)); }),
                new STFReader.TokenProcessor("speedkph", ()=>{ SpeedLimit = (float)Speed.MeterPerSecond.FromKpH(stf.ReadFloatBlock(STFReader.Units.None, 0)); }),
                new STFReader.TokenProcessor("signalflags", ()=>{
                    stf.MustMatchBlockStart();
                    while (!stf.EndOfBlock())
                        switch (stf.ReadString().ToUpperInvariant())
                        {
                            case "ASAP":
                                Asap = true;
                                break;
                            case "OR_SPEEDRESET":
                                Reset = true;
                                break;
                            case "OR_NOSPEEDREDUCTION":
                                NoSpeedReduction = true;
                                break;
                            default:
                                stf.StepBackOneItem();
                                STFException.TraceInformation(stf, "Skipped unknown DrawLight flag " + stf.ReadString());
                                break;
                        }
                }),
            });
        }
    }
    #endregion

    #region SignalShape
    /// <summary>
    /// Describes a signal object shape and the set of signal heads and other sub-objects that are present on this.
    /// </summary>

    public class ApproachControlLimits
    {
        public float? ApproachControlPositionM { get; private set; }
        public float? ApproachControlSpeedMpS { get; private set; }

        internal ApproachControlLimits(STFReader stf)
        {
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("positionmiles", ()=>{ ApproachControlPositionM = (float)Size.Length.FromMi(stf.ReadFloatBlock(STFReader.Units.None, 0)); }),
                new STFReader.TokenProcessor("positionkm", ()=>{ ApproachControlPositionM = (stf.ReadFloatBlock(STFReader.Units.None, 0) * 1000); }),
                new STFReader.TokenProcessor("positionm", ()=>{ ApproachControlPositionM = stf.ReadFloatBlock(STFReader.Units.None, 0); }),
                new STFReader.TokenProcessor("positionyd", ()=>{ ApproachControlPositionM = (float)Size.Length.FromYd(stf.ReadFloatBlock(STFReader.Units.None, 0)); }),
                new STFReader.TokenProcessor("speedmph", ()=>{ ApproachControlSpeedMpS = (float)Speed.MeterPerSecond.FromMpH(stf.ReadFloatBlock(STFReader.Units.None, 0)); }),
                new STFReader.TokenProcessor("speedkph", ()=>{ ApproachControlSpeedMpS = (float)Speed.MeterPerSecond.FromKpH(stf.ReadFloatBlock(STFReader.Units.None, 0)); }),
                });
        }
    }

    public class SignalShape
    {
        /// <summary>Name (without path) of the file that contains the shape itself</summary>
        public string ShapeFileName { get; private set; }
        /// <summary>Description of the signal shape</summary>
        public string Description { get; private set; }
        /// <summary>List of sub-objects that are belong to this shape</summary>
        public IList<SignalSubObject> SignalSubObjs { get; private set; }

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        internal SignalShape(STFReader stf)
        {
            stf.MustMatchBlockStart();
            ShapeFileName = Path.GetFileName(stf.ReadString());
            Description = stf.ReadString();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("signalsubobjs", ()=>{ SignalSubObjs = ReadSignalSubObjects(stf); }),
            });
        }

        private static List<SignalSubObject> ReadSignalSubObjects(STFReader stf)
        {
            stf.MustMatchBlockStart();
            int count = stf.ReadInt(null);
            List<SignalSubObject> signalSubObjects = new List<SignalSubObject>(count);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("signalsubobj", ()=>{
                    if (signalSubObjects.Count >= count)
                        STFException.TraceWarning(stf, "Skipped extra SignalSubObj");
                    else
                    {
                        SignalSubObject signalSubObject = new SignalSubObject(stf);
                        if (signalSubObject.Index != signalSubObjects.Count)
                            STFException.TraceWarning(stf, $"Invalid SignalSubObj index; expected {signalSubObjects.Count}, got {signalSubObject.Index}");
                        signalSubObjects.Add(signalSubObject);
                    }
                }),
            });
            if (signalSubObjects.Count < count)
                STFException.TraceWarning(stf, $"{(count - signalSubObjects.Count)} missing SignalSubObj(s)");
            return signalSubObjects;
        }

        /// <summary>
        /// Describes a sub-object belonging to a signal shape
        /// </summary>
        public class SignalSubObject
        {
            /// <summary></summary>
            public int Index { get; private set; }
            /// <summary>Name of the group within the signal shape which defines this head</summary>
            public string MatrixName { get; private set; }
            /// <summary></summary>
            public string Description { get; private set; }
            /// <summary>Index of the signal sub type (decor, signal_head, ...). -1 if not specified</summary>
            public SignalSubType SignalSubType { get; private set; }
            /// <summary>Signal Type of the this sub-object</summary>
            public string SignalSubSignalType { get; private set; }
            /// <summary>The sub-object is optional on this signal shape</summary>
            public bool Optional { get; private set; }
            /// <summary>The sub-object will be enabled by default (when manually placed)</summary>
            public bool Default { get; private set; }
            /// <summary>The sub-object is facing backwards w.r.t. rest of object</summary>
            public bool BackFacing { get; private set; }
            /// <summary>Signal should always have a junction link</summary>
            public bool JunctionLink { get; private set; }

            // SigSubJnLinkIf is not supported 

            /// <summary>
            /// Default constructor used during file parsing.
            /// </summary>
            /// <param name="stf">The STFreader containing the file stream</param>
            internal SignalSubObject(STFReader stf)
            {
                SignalSubType = SignalSubType.None;
                stf.MustMatchBlockStart();
                Index = stf.ReadInt(null);
                MatrixName = stf.ReadString().ToUpperInvariant();
                Description = stf.ReadString();
                stf.ParseBlock(new STFReader.TokenProcessor[] {
                    new STFReader.TokenProcessor("sigsubtype", ()=>{
                        if (EnumExtension.GetValue(stf.ReadStringBlock(null), out SignalSubType subType))
                            SignalSubType = subType;
                    }),
                    new STFReader.TokenProcessor("sigsubstype", ()=>{ SignalSubSignalType = stf.ReadStringBlock(null); }),
                    new STFReader.TokenProcessor("signalflags", ()=>{
                        stf.MustMatchBlockStart();
                        while (!stf.EndOfBlock())
                            switch (stf.ReadString().ToUpperInvariant())
                            {
                                case "OPTIONAL":
                                    Optional = true;
                                    break;
                                case "DEFAULT":
                                    Default = true; break;
                                case "BACK_FACING":
                                    BackFacing = true;
                                    break;
                                case "JN_LINK":
                                    JunctionLink = true;
                                    break;
                                default:
                                    stf.StepBackOneItem();
                                    STFException.TraceInformation(stf, "Skipped unknown SignalSubObj flag " + stf.ReadString());
                                    break;
                            }
                    }),
                });
            }
        }
    }
    #endregion
}
