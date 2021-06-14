using System;
using System.ComponentModel;

namespace Orts.Formats.Msts
{
#pragma warning disable CA1707 // Identifiers should not contain underscores
    #region Activity
    public enum EventType
    {
        AllStops = 0,
        AssembleTrain,
        AssembleTrainAtLocation,
        DropOffWagonsAtLocation,
        PickUpPassengers,
        PickUpWagons,
        ReachSpeed
    }
    #endregion

    #region Signalling
    /// <summary>
    /// Describe the various states a block (roughly a region between two signals) can be in.
    /// </summary>
    public enum SignalBlockState
    {
        /// <summary>Block ahead is clear and accesible</summary>
        Clear,
        /// <summary>Block ahead is occupied by one or more wagons/locos not moving in opposite direction</summary>
        Occupied,
        /// <summary>Block ahead is impassable due to the state of a switch or occupied by moving train or not accesible</summary>
        Jn_Obstructed,
    }

    /// <summary>
    /// Describe the various aspects (or signal indication states) that MSTS signals can have.
    /// Within MSTS known as SIGASP_ values.  
    /// Note: They are in order from most restrictive to least restrictive.
    /// </summary>
    public enum SignalAspectState
    {
        /// <summary>Stop (absolute)</summary>
        Stop,
        /// <summary>Stop and proceed</summary>
        Stop_And_Proceed,
        /// <summary>Restricting</summary>
        Restricting,
        /// <summary>Final caution before 'stop' or 'stop and proceed'</summary>
        Approach_1,
        /// <summary>Advanced caution</summary>
        Approach_2,
        /// <summary>Least restrictive advanced caution</summary>
        Approach_3,
        /// <summary>Clear to next signal</summary>
        Clear_1,
        /// <summary>Clear to next signal (least restrictive)</summary>
        Clear_2,
        /// <summary>Signal aspect is unknown (possibly not yet defined)</summary>
        Unknown,
    }

    /// <summary>
    /// List of allowed signal sub types, as defined by MSTS (SIGSUBT_ values)
    /// </summary>
    public enum SignalSubType
    {
        None = -1,
        Decor,
        Signal_Head,
        Dummy1,
        Dummy2,
        Number_Plate,
        Gradient_Plate,
        User1,
        User2,
        User3,
        User4,
    }

    /// <summary>
    /// Describe the function of a particular signal head.
    /// Only SIGFN_NORMAL signal heads will require a train to take action (e.g. to stop).  
    /// The other values act only as categories for signal types to belong to.
    /// Within MSTS known as SIGFN_ values.  
    /// </summary>
    public enum SignalFunction
    {
        /// <summary>Signal head showing primary indication</summary>
        Normal,
        /// <summary>Distance signal head</summary>
        Distance,
        /// <summary>Repeater signal head</summary>
        Repeater,
        /// <summary>Shunting signal head</summary>
        Shunting,
        /// <summary>Signal is informational only e.g. direction lights</summary>
        Info,
        /// <summary>Speedpost signal (not part of MSTS SIGFN_)</summary>
        Speed,
        /// <summary>Alerting function not part of MSTS SIGFN_)</summary>
        Alert,
        /// <summary>Unknown (or undefined) signal type</summary>
        Unknown, // needs to be last because some code depends this for looping. That should be changed of course.
    }
    #endregion

    #region Path
    // This relates to TrPathFlags, which is not always present in .pat file
    // Bit 0 - connected pdp-entry references a reversal-point (1/x1)
    // Bit 1 - waiting point (2/x2)
    // Bit 2 - intermediate point between switches (4/x4)
    // Bit 3 - 'other exit' is used (8/x8)
    // Bit 4 - 'optional Route' active (16/x10)
     [Flags]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
    public enum PathFlags
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
    {
        None = 0x0,
        ReversalPoint = 1 << 0,
        WaitPoint = 1 << 1,
        IntermediatePoint = 1 << 2,
        OtherExit = 1 << 3,
        OptionalRoute = 1 << 4,
        NotPlayerPath = 1 << 5,
    }

    #endregion

    #region AceFile
    [Flags]
    public enum SimisAceFormatOptions
    {
        None = 0,
        MipMaps = 0x01,
        RawData = 0x10,
    }

    public enum SimisAceChannelId
    {
        None = 0,
        Mask = 2,
        Red = 3,
        Green = 4,
        Blue = 5,
        Alpha = 6,
    }

    #endregion

    #region Light
    /// <summary>
    /// Specifies whether a wagon light is glow (simple light texture) or cone (projected light cone).
    /// </summary>
    public enum LightType
    {
        Glow,
        Cone,
    }

    /// <summary>
    /// Specifies in which headlight positions (off, dim, bright) the wagon light is illuminated.
    /// </summary>
    public enum LightHeadlightCondition
    {
        Ignore,
        Off,
        Dim,
        Bright,
        DimBright, // MSTSBin
        OffBright, // MSTSBin
        OffDim, // MSTSBin
        // TODO: DimBright?, // MSTSBin labels this the same as DimBright. Not sure what it means.
    }

    /// <summary>
    /// Specifies on which units of a consist (first, middle, last) the wagon light is illuminated.
    /// </summary>
    public enum LightUnitCondition
    {
        Ignore,
        Middle,
        First,
        Last,
        LastRev, // MSTSBin
        FirstRev, // MSTSBin
    }

    /// <summary>
    /// Specifies in which penalty states (no, yes) the wagon light is illuminated.
    /// </summary>
    public enum LightPenaltyCondition
    {
        Ignore,
        No,
        Yes,
    }

    /// <summary>
    /// Specifies on which types of trains (AI, player) the wagon light is illuminated.
    /// </summary>
    public enum LightControlCondition
    {
        Ignore,
        AI,
        Player,
    }

    /// <summary>
    /// Specifies in which in-service states (no, yes) the wagon light is illuminated.
    /// </summary>
    public enum LightServiceCondition
    {
        Ignore,
        No,
        Yes,
    }

    /// <summary>
    /// Specifies during which times of day (day, night) the wagon light is illuminated.
    /// </summary>
    public enum LightTimeOfDayCondition
    {
        Ignore,
        Day,
        Night,
    }

    /// <summary>
    /// Specifies in which weather conditions (clear, rain, snow) the wagon light is illuminated.
    /// </summary>
    public enum LightWeatherCondition
    {
        Ignore,
        Clear,
        Rain,
        Snow,
    }

    /// <summary>
    /// Specifies on which units of a consist by coupling (front, rear, both) the wagon light is illuminated.
    /// </summary>
    public enum LightCouplingCondition
    {
        Ignore,
        Front,
        Rear,
        Both,
    }
    #endregion

    #region Activity
    public enum OrtsActivitySoundFileType
    {
        None,
        Everywhere,
        Cab,
        Pass,
        Ground,
        Location
    }

    public enum ActivationType
    {
        Activate,
        Deactivate,
    }
    #endregion

    #region CabView
    public enum CabViewControlType
    {
        None,
        Speedometer,
        Main_Res,
        Eq_Res,
        Brake_Cyl,
        Brake_Pipe,
        Line_Voltage,
        AmMeter,
        AmMeter_Abs,
        Load_Meter,
        Throttle,
        Pantograph,
        Train_Brake,
        Friction_Brake,
        Engine_Brake,
        Brakeman_Brake,
        Dynamic_Brake,
        Dynamic_Brake_Display,
        Sanders,
        Wipers,
        Vacuum_Exhauster,
        Horn,
        Bell,
        Front_HLight,
        Direction,
        Aspect_Display,
        Throttle_Display,
        Cph_Display,
        Panto_Display,
        Direction_Display,
        Cp_Handle,
        Pantograph2,
        Clock,
        Sanding,
        Alerter_Display,
        Traction_Braking,
        Accelerometer,
        WheelSlip,
        Friction_Braking,
        Penalty_App,
        Emergency_Brake,
        Reset,
        Cab_Radio,
        OverSpeed,
        SpeedLim_Display,
        Fuel_Gauge,
        Whistle,
        Regulator,
        Cyl_Cocks,
        Blower,
        Steam_Inj1,
        Steam_Inj2,
        Orts_BlowDown_Valve,
        Dampers_Front,
        Dampers_Back,
        Steam_Heat,
        Water_Injector1,
        Water_Injector2,
        Small_Ejector,
        Steam_Pr,
        SteamChest_Pr,
        Tender_Water,
        Boiler_Water,
        Reverser_Plate,
        SteamHeat_Pressure,
        FireBox,
        Rpm,
        FireHole,
        CutOff,
        Vacuum_Reservoir_Pressure,
        Gears,
        Doors_Display,
        Speed_Projected,
        SpeedLimit,
        Pantographs_4,
        Pantographs_4C,
        Pantographs_5,
        Orts_Oil_Pressure,
        Orts_Diesel_Temperature,
        Orts_Cyl_Comp,
        Gears_Display,
        Dynamic_Brake_Force,
        Orts_Circuit_Breaker_Driver_Closing_Order,
        Orts_Circuit_Breaker_Driver_Opening_Order,
        Orts_Circuit_Breaker_Driver_Closing_Authorization,
        Orts_Circuit_Breaker_State,
        Orts_Circuit_Breaker_Closed,
        Orts_Circuit_Breaker_Open,
        Orts_Circuit_Breaker_Authorized,
        Orts_Circuit_Breaker_Open_And_Authorized,
        Orts_Player_Diesel_Engine,
        Orts_Helpers_Diesel_Engines,
        Orts_Player_Diesel_Engine_State,
        Orts_Player_Diesel_Engine_Starter,
        Orts_Player_Diesel_Engine_Stopper,
        Orts_CabLight,
        Orts_LeftDoor,
        Orts_RightDoor,
        Orts_Mirros,
        Orts_Pantograph3,
        Orts_Pantograph4,
        Orts_Water_Scoop,
        Orts_Large_Ejector,
        Orts_HourDial,
        Orts_MinuteDial,
        Orts_SecondDial,
        Orts_Signed_Traction_Braking,
        Orts_Signed_Traction_Total_Braking,
        Orts_Bailoff,
        Orts_QuickRelease,
        Orts_Overcharge,
        Orts_Battery,
        Orts_PowerKey,
        Orts_2DExternalWipers,

        // TCS Controls
        Orts_TCS1,
        Orts_TCS2,
        Orts_TCS3,
        Orts_TCS4,
        Orts_TCS5,
        Orts_TCS6,
        Orts_TCS7,
        Orts_TCS8,
        Orts_TCS9,
        Orts_TCS10,
        Orts_TCS11,
        Orts_TCS12,
        Orts_TCS13,
        Orts_TCS14,
        Orts_TCS15,
        Orts_TCS16,
        Orts_TCS17,
        Orts_TCS18,
        Orts_TCS19,
        Orts_TCS20,
        Orts_TCS21,
        Orts_TCS22,
        Orts_TCS23,
        Orts_TCS24,
        Orts_TCS25,
        Orts_TCS26,
        Orts_TCS27,
        Orts_TCS28,
        Orts_TCS29,
        Orts_TCS30,
        Orts_TCS31,
        Orts_TCS32,
        Orts_TCS33,
        Orts_TCS34,
        Orts_TCS35,
        Orts_TCS36,
        Orts_TCS37,
        Orts_TCS38,
        Orts_TCS39,
        Orts_TCS40,
        Orts_TCS41,
        Orts_TCS42,
        Orts_TCS43,
        Orts_TCS44,
        Orts_TCS45,
        Orts_TCS46,
        Orts_TCS47,
        Orts_TCS48,
        Orts_Etcs,

        // Further CabViewControlTypes must be added above this line, to avoid their malfunction in 3DCabs
        ExternalWipers,
        LeftDoor,
        RightDoor,
        Mirrors,
    }

    public enum CabViewControlStyle
    {
        None,
        Needle,
#pragma warning disable CA1720 // Identifier contains type name
        Pointer,
#pragma warning restore CA1720 // Identifier contains type name
        Solid,
        Liquid,
        Sprung,
        Not_Sprung,
        While_Pressed,
        Pressed,
        OnOff,
        Hour24,
        Hour12,
    }

    public enum CabViewControlUnit
    {
        None,
        Bar,
        Psi,
        KiloPascals,
        Kgs_Per_Square_Cm,
        Amps,
        Volts,
        KiloVolts,

        Km_Per_Hour,
        Miles_Per_Hour,
        MetresµSecµSec,
        Metres_Sec_Sec,
        KmµHourµHour,
        Km_Hour_Hour,
        KmµHourµSec,
        Km_Hour_Sec,
        MetresµSecµHour,
        Metres_Sec_Hour,
        Miles_Hour_Min,
        Miles_Hour_Hour,

        Newtons,
        Kilo_Newtons,
        Kilo_Lbs,
        Metres_Per_Sec,
        Litres,
        Gallons,
        Inches_Of_Mercury,
        Mili_Amps,
        Rpm,
        Lbs,
    }
    #endregion

    #region WorldFile
    // These relate to the general properties settable for scenery objects in RE
    [Flags]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
    public enum StaticFlag
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
    {
        RoundShadow = 0x00002000,
        RectangularShadow = 0x00004000,
        TreelineShadow = 0x00008000,
        DynamicShadow = 0x00010000,
        AnyShadow = 0x0001E000,
        Terrain = 0x00040000,
        Animate = 0x00080000,
        Global = 0x00200000,
    }

    [Flags]
    public enum PlatformData
    {
        PlatformLeft = 0x00000002,
        PlatformRight = 0x00000004,
    }

    /// <summary>
    /// Supply types for freight wagons and locos
    /// </summary>
    public enum PickupType
    {
        [Description("none")]               None = 0,
        [Description("freight-grain")]      FreightGrain = 1,
        [Description("freight-coal")]       FreightCoal = 2,
        [Description("freight-gravel")]     FreightGravel = 3,
        [Description("freight-sand")]       FreightSand = 4,
        [Description("water")]              FuelWater = 5,
        [Description("coal")]               FuelCoal = 6,
        [Description("diesel oil")]         FuelDiesel = 7,
        [Description("wood")]               FuelWood = 8,    // Think this is new to OR and not recognised by MSTS
        [Description("sand")]               FuelSand = 9,  // New to OR
        [Description("freight-general")]    FreightGeneral = 10, // New to OR
        [Description("freight-livestock")]  FreightLivestock = 11,  // New to OR
        [Description("freight-fuel")]       FreightFuel = 12,  // New to OR
        [Description("freight-milk")]       FreightMilk = 13,   // New to OR
        [Description("mail")]               SpecialMail = 14  // New to OR
    }
    #endregion

    #region TrackItem

    #endregion
#pragma warning restore CA1707 // Identifiers should not contain underscores
}
