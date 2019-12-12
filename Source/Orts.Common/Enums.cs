// COPYRIGHT 2009, 2011 by the Open Rails project.
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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Orts.Common
{
    #region Enum Extension
    public static class EnumExtension
    {
        private static class EnumCache<T> where T : Enum
        {
            internal static readonly IList<string> Names;
            internal static readonly IList<T> Values;
            internal static readonly Dictionary<T, string> ValueToDescriptionMap;
            internal static string EnumDescription;
            internal static IDictionary<string, T> NameValuePairs;

            static EnumCache()
            {
                Values = new ReadOnlyCollection<T>((T[])Enum.GetValues(typeof(T)));
                Names = new ReadOnlyCollection<string>(Enum.GetNames(typeof(T)));
                ValueToDescriptionMap = new Dictionary<T, string>();
                EnumDescription = typeof(T).GetCustomAttributes(typeof(DescriptionAttribute), false).
                    Cast<DescriptionAttribute>().
                    Select(x => x.Description).
                    FirstOrDefault();
                foreach (T value in Values)//(T[])Enum.GetValues(typeof(T)))
                {
                    ValueToDescriptionMap[value] = GetDescription(value);
                }

                NameValuePairs = Names.Zip(Values, (k, v) => new { k, v })
                              .ToDictionary(x => x.k, x => x.v, StringComparer.OrdinalIgnoreCase);
            }

            private static string GetDescription(T value)
            {
                FieldInfo field = typeof(T).GetField(value.ToString());
                return field.GetCustomAttributes(typeof(DescriptionAttribute), false)
                            .Cast<DescriptionAttribute>()
                            .Select(x => x.Description)
                            .FirstOrDefault();
            }
        }

        /// <summary>
        /// returns the Description attribute for the particular enum value
        /// </summary>
        public static string GetDescription<T>(this T item) where T : Enum
        {
            if (EnumCache<T>.ValueToDescriptionMap.TryGetValue(item, out string description))
            {
                return description;
            }
            throw new ArgumentOutOfRangeException("item");
        }

        /// <summary>
        /// returns the Description attribute for the enum type
        /// </summary>
        public static string EnumDescription<T>() where T : Enum
        {
            return EnumCache<T>.EnumDescription;
        }

        /// <summary>
        /// returns a static list of all names in this enum
        /// </summary>
        public static IList<string> GetNames<T>() where T : Enum
        {
            return EnumCache<T>.Names;
        }

        /// <summary>
        /// returns a static list of all values in this enum
        /// </summary>
        public static IList<T> GetValues<T>() where T : Enum
        {
            return EnumCache<T>.Values;
        }

        /// <summary>
        /// returns a number of elements in this enum
        /// </summary>
        public static int GetLength<T>() where T : Enum
        {
            return EnumCache<T>.Values.Count;
        }

        /// <summary>
        /// Similar as Enum.TryParse, but based on statically cached dictionary
        /// </summary>
        public static bool GetValue<T>(string name, out T result) where T : Enum
        {
            return EnumCache<T>.NameValuePairs.TryGetValue(name, out result);
        }

        /// <summary>
        /// allows to enumerate forward over enum values
        /// </summary>
        public static T Next<T>(this T item) where T : Enum
        {
            return EnumCache<T>.Values[(EnumCache<T>.Values.IndexOf(item) + 1) % EnumCache<T>.Values.Count];
        }

        /// <summary>
        /// allows to enumerate backward over enum values
        /// </summary>
        public static T Previous<T>(this T item) where T : Enum
        {
            return EnumCache<T>.Values[(EnumCache<T>.Values.IndexOf(item) - 1 + EnumCache<T>.Values.Count) % EnumCache<T>.Values.Count];
        }
    }
    #endregion

    [Description("Reverser")]
    public enum Direction
    {
        [Description("Reverse")] Reverse = -1,
        [Description("N")] N = 0,
        [Description("Forward")] Forward = 1,
    }

    [Description("Rotation")]
    public enum Rotation
    {
        CounterClockwise = -1,
        None = 0,
        Clockwise = 1,
    }

    public enum TrackMonitorSignalAspect
    {
        None,
        Clear_2,
        Clear_1,
        Approach_3,
        Approach_2,
        Approach_1,
        Restricted,
        StopAndProceed,
        Stop,
        Permission,
    }

    
    public enum ScreenMode
    {
        WindowedPresetResolution,
        FullscreenPresetResolution,
        FullscreenNativeResolution,
    }

    public enum CabSetting
    {
        Name,       // name of control
        Off,        // 2 or 3 state control/reset/initialise
        Neutral,    // 2 or 3 state control
        On,         // 2 or 3 state control/apply/change
        Decrease,   // continuous control
        Increase,   // continuous control
        Warn1,
        Warn2,
        Range1,     // sub-range
        Range2,
        Range3,
        Range4,
    }

    public enum ConfirmLevel
    {
        [Description("None")] None,
        [Description("Information")] Information,
        [Description("Warning")] Warning,
        [Description("Error")] Error,
        [Description("Message")] Message,
    };

    public enum CabControl
    {
        None,
        // Power
        Reverser,
        Throttle,
        Wheelslip,
        // Electric Power
        Power,
        Pantograph1,
        Pantograph2,
        Pantograph3,
        Pantograph4,
        CircuitBreakerClosingOrder,
        CircuitBreakerOpeningOrder,
        CircuitBreakerClosingAuthorization,
        // Diesel Power
        PlayerDiesel,
        HelperDiesel,
        DieselFuel,
        // Steam power
        SteamLocomotiveReverser,
        Regulator,
        Injector1,
        Injector2,
        Blower,
        SteamHeat,
        Damper,
        FireboxDoor,
        FiringRate,
        FiringIsManual,
        FireShovelfull,
        CylinderCocks,
        CylinderCompound,
        SmallEjector,
        TenderCoal,
        TenderWater,
        // General
        WaterScoop,
        // Braking
        TrainBrake,
        EngineBrake,
        DynamicBrake,
        EmergencyBrake,
        BailOff,
        InitializeBrakes,
        Handbrake,
        Retainers,
        BrakeHose,
        // Cab Devices
        Sander,
        Alerter,
        Horn,
        Whistle,
        Bell,
        Headlight,
        CabLight,
        Wipers,
        ChangeCab,
        Odometer,
        // Train Devices
        DoorsLeft,
        DoorsRight,
        Mirror,
        // Track Devices
        SwitchAhead,
        SwitchBehind,
        // Simulation
        SimulationSpeed,
        Uncouple,
        Activity,
        Replay,
        GearBox,
        SignalMode,
        // Freight Load
        FreightLoad,
        CabRadio,
    }

    public enum TrainEvent
    {
        None,
        BellOff,
        BellOn,
        BlowerChange,
        BrakesStuck,
        CabLightSwitchToggle,
        CabRadioOn,
        CabRadioOff,
        CircuitBreakerOpen,
        CircuitBreakerClosing,
        CircuitBreakerClosed,
        CircuitBreakerClosingOrderOff,
        CircuitBreakerClosingOrderOn,
        CircuitBreakerOpeningOrderOff,
        CircuitBreakerOpeningOrderOn,
        CircuitBreakerClosingAuthorizationOff,
        CircuitBreakerClosingAuthorizationOn,
        CompressorOff,
        CompressorOn,
        ControlError,
        Couple,
        CoupleB, // NOTE: Currently not used in Open Rails.
        CoupleC, // NOTE: Currently not used in Open Rails.
        CrossingClosing,
        CrossingOpening,
        CylinderCocksToggle,
        CylinderCompoundToggle,
        DamperChange,
        Derail1, // NOTE: Currently not used in Open Rails.
        Derail2, // NOTE: Currently not used in Open Rails.
        Derail3, // NOTE: Currently not used in Open Rails.
        DoorClose,
        DoorOpen,
        DynamicBrakeChange,
        DynamicBrakeIncrease, // NOTE: Currently not used in Open Rails.
        DynamicBrakeOff,
        EngineBrakeChange,
        EngineBrakePressureDecrease,
        EngineBrakePressureIncrease,
        EnginePowerOff,
        EnginePowerOn,
        FireboxDoorChange,
        FireboxDoorOpen,
        FireboxDoorClose,
        FuelTowerDown,
        FuelTowerTransferEnd,
        FuelTowerTransferStart,
        FuelTowerUp,
        GearDown,
        GearUp,
        HornOff,
        HornOn,
        LightSwitchToggle,
        MirrorClose,
        MirrorOpen,
        Pantograph1Down,
        PantographToggle,
        Pantograph1Up,
        Pantograph2Down,
        Pantograph2Up,
        Pantograph3Down,
        Pantograph3Up,
        Pantograph4Down,
        Pantograph4Up,
        PermissionDenied,
        PermissionGranted,
        PermissionToDepart,
        ReverserChange,
        ReverserToForwardBackward,
        ReverserToNeutral,
        SanderOff,
        SanderOn,
        SemaphoreArm,
        SmallEjectorChange,
        WaterInjector1Off,
        WaterInjector1On,
        WaterInjector2Off,
        WaterInjector2On,
        SteamHeatChange,
        SteamPulse1,
        SteamPulse2,
        SteamPulse3,
        SteamPulse4,
        SteamPulse5,
        SteamPulse6,
        SteamPulse7,
        SteamPulse8,
        SteamPulse9,
        SteamPulse10,
        SteamPulse11,
        SteamPulse12,
        SteamPulse13,
        SteamPulse14,
        SteamPulse15,
        SteamPulse16,
        SteamSafetyValveOff,
        SteamSafetyValveOn,
        ThrottleChange,
        TrainBrakeChange,
        TrainBrakePressureDecrease,
        TrainBrakePressureIncrease,
        TrainControlSystemActivate,
        TrainControlSystemAlert1,
        TrainControlSystemAlert2,
        TrainControlSystemDeactivate,
        TrainControlSystemInfo1,
        TrainControlSystemInfo2,
        TrainControlSystemPenalty1,
        TrainControlSystemPenalty2,
        TrainControlSystemWarning1,
        TrainControlSystemWarning2,
        MovingTableMovingEmpty,
        MovingTableMovingLoaded,
        MovingTableStopped,
        Uncouple,
        UncoupleB, // NOTE: Currently not used in Open Rails.
        UncoupleC, // NOTE: Currently not used in Open Rails.
        VacuumExhausterOn,
        VacuumExhausterOff,
        VigilanceAlarmOff,
        VigilanceAlarmOn,
        VigilanceAlarmReset,
        WaterScoopDown,
        WaterScoopUp,
        WiperOff,
        WiperOn,
        _HeadlightDim,
        _HeadlightOff,
        _HeadlightOn,
        _ResetWheelSlip,

        TrainBrakePressureStoppedChanging,
        EngineBrakePressureStoppedChanging,
        BrakePipePressureIncrease,
        BrakePipePressureDecrease,
        BrakePipePressureStoppedChanging,
        CylinderCocksOpen,
        CylinderCocksClose,
        SecondEnginePowerOff,
        SecondEnginePowerOn,

        HotBoxBearingOn,
        HotBoxBearingOff
    }
    public enum PowerSupplyEvent
    {
        RaisePantograph,
        LowerPantograph,
        CloseCircuitBreaker,
        OpenCircuitBreaker,
        CloseCircuitBreakerButtonPressed,
        CloseCircuitBreakerButtonReleased,
        OpenCircuitBreakerButtonPressed,
        OpenCircuitBreakerButtonReleased,
        GiveCircuitBreakerClosingAuthorization,
        RemoveCircuitBreakerClosingAuthorization,
        StartEngine,
        StopEngine,
        ClosePowerContactor,
        OpenPowerContactor,
        GivePowerContactorClosingAuthorization,
        RemovePowerContactorClosingAuthorization
    }

    [Description("PowerSupply")]
    public enum PowerSupplyState
    {
        [Description("Off")] PowerOff,
        [Description("On ongoing")] PowerOnOngoing,
        [Description("On")] PowerOn
    }

    [Description("Pantograph")]
    public enum PantographState
    {
        [Description("Down")] Down,
        [Description("Lowering")] Lowering,
        [Description("Raising")] Raising,
        [Description("Up")] Up
    }

    [Description("CircuitBreaker")]
    public enum CircuitBreakerState
    {
        [Description("Open")] Open,
        [Description("Closing")] Closing,
        [Description("Closed")] Closed
    }

    public enum BrakeControllerEvent
    {
        /// <summary>
        /// Starts the pressure increase (may have a target value)
        /// </summary>
        StartIncrease,
        /// <summary>
        /// Stops the pressure increase
        /// </summary>
        StopIncrease,
        /// <summary>
        /// Starts the pressure decrease (may have a target value)
        /// </summary>
        StartDecrease,
        /// <summary>
        /// Stops the pressure decrease
        /// </summary>
        StopDecrease,
        /// <summary>
        /// Sets the value of the brake controller using a RailDriver peripheral (must have a value)
        /// </summary>
        SetCurrentPercent,
        /// <summary>
        /// Sets the current value of the brake controller (must have a value)
        /// </summary>
        SetCurrentValue,
        /// <summary>
        /// Starts a full quick brake release.
        /// </summary>
        FullQuickRelease,
        /// <summary>
        /// Starts a pressure decrease to zero (may have a target value)
        /// </summary>
        StartDecreaseToZero
    }

    //TrainBrakesController
    [Description("Brake Controller")]
    public enum ControllerState
    {
        // MSTS values (DO NOT CHANGE THE ORDER !)
        [Description("")]
        Dummy,              // Dummy
        [Description("Release")]
        Release,            // ReleaseStart 
        [Description("Quick Release")]
        FullQuickRelease,   // FullQuickReleaseStart
        [Description("Running")]
        Running,            // RunningStart 
        [Description("Neutral")]
        Neutral,            // NeutralhandleOffStart
        [Description("Self Lap")]
        SelfLap,            // SelfLapStart 
        [Description("Lap")]
        Lap,                // HoldLapStart 
        [Description("Apply")]
        Apply,              // ApplyStart 
        [Description("EPApply")]
        EPApply,            // EPApplyStart 
        [Description("Service")]
        GSelfLap,           // GraduatedSelfLapLimitedStart
        [Description("Service")]
        GSelfLapH,          // GraduatedSelfLapLimitedHoldStart
        [Description("Suppression")]
        Suppression,        // SuppressionStart 
        [Description("Cont. Service")]
        ContServ,           // ContinuousServiceStart 
        [Description("Full Service")]
        FullServ,           // FullServiceStart 
        [Description("Emergency")]
        Emergency,          // EmergencyStart

        // Extra MSTS values
        [Description("Minimum Reduction")]
        MinimalReduction,  // MinimalReductionStart,
        [Description("Hold")]
        Hold,                   // HoldStart

        // OR values
        [Description("Overcharge")]
        Overcharge,         // Overcharge
        [Description("Emergency Braking Push Button")]
        EBPB,               // Emergency Braking Push Button
        [Description("TCS Emergency Braking")]
        TCSEmergency,       // TCS Emergency Braking
        [Description("TCS Full Service Braking")]
        TCSFullServ,        // TCS Full Service Braking
        [Description("Vac. Cont. Service")]
        VacContServ         // VacuumContinuousServiceStart
    }


    public enum TCSEvent
    {
        /// <summary>
        /// Reset request by pressing the alerter button.
        /// </summary>
        AlerterPressed,
        /// <summary>
        /// Alerter button was released.
        /// </summary>
        AlerterReleased,
        /// <summary>
        /// Internal reset request by touched systems other than the alerter button.
        /// </summary>
        AlerterReset,
        /// <summary>
        /// Internal reset request by the reverser.
        /// </summary>
        ReverserChanged,
        /// <summary>
        /// Internal reset request by the throttle controller.
        /// </summary>
        ThrottleChanged,
        /// <summary>
        /// Internal reset request by the gear box controller.
        /// </summary>
        GearBoxChanged,
        /// <summary>
        /// Internal reset request by the train brake controller.
        /// </summary>
        TrainBrakeChanged,
        /// <summary>
        /// Internal reset request by the engine brake controller.
        /// </summary>
        EngineBrakeChanged,
        /// <summary>
        /// Internal reset request by the dynamic brake controller.
        /// </summary>
        DynamicBrakeChanged,
        /// <summary>
        /// Internal reset request by the horn handle.
        /// </summary>
        HornActivated,
    }

    /// <summary>
    /// Controls what color the speed monitoring display uses.
    /// </summary>
    public enum MonitoringStatus
    {
        /// <summary>
        /// Grey color. No speed restriction is ahead.
        /// </summary>
        Normal,
        /// <summary>
        /// White color. Pre-indication, that the next signal is restricted. No manual intervention is needed yet.
        /// </summary>
        Indication,
        /// <summary>
        /// Yellow color. Next signal is restricted, driver should start decreasing speed.
        /// (Please note, it is not for indication of a "real" overspeed. In this state the locomotive still runs under the actual permitted speed.)
        /// </summary>
        Overspeed,
        /// <summary>
        /// Orange color. The locomotive is very close to next speed restriction, driver should start strong braking immediately.
        /// </summary>
        Warning,
        /// <summary>
        /// Red color. Train control system intervention speed. Computer has to apply full service or emergency brake to maintain speed restriction.
        /// </summary>
        Intervention,
    }


}
