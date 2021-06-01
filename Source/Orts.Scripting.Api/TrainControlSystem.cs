using System;
using System.IO;

using Orts.Common;
using Orts.Scripting.Api.Etcs;

namespace Orts.Scripting.Api
{
    public abstract class TrainControlSystem : TrainScriptBase
    {
        public bool Activated { get; set; }

        public readonly ETCSStatus ETCSStatus = new ETCSStatus();

        /// <summary>
        /// True if train control is switched on (the locomotive is the lead locomotive and the train is not autopiloted).
        /// </summary>
        public Func<bool> IsTrainControlEnabled;
        /// <summary>
        /// True if train is autopiloted
        /// </summary>
        public Func<bool> IsAutopiloted;
        /// <summary>
        /// True if vigilance monitor was switched on in game options.
        /// </summary>
        public Func<bool> IsAlerterEnabled;
        /// <summary>
        /// True if speed control was switched on in game options.
        /// </summary>
        public Func<bool> IsSpeedControlEnabled;
        /// <summary>
        /// True if alerter sound rings, otherwise false
        /// </summary>
        public Func<bool> AlerterSound;
        /// <summary>
        /// Max allowed speed for the train in that moment.
        /// </summary>
        public Func<float> TrainSpeedLimitMpS;
        /// <summary>
        /// Max allowed speed for the train basing on consist and route max speed.
        /// </summary>
        public Func<float> TrainMaxSpeedMpS;
        /// <summary>
        /// Max allowed speed determined by current signal.
        /// </summary>
        public Func<float> CurrentSignalSpeedLimitMpS;
        /// <summary>
        /// Max allowed speed determined by next signal.
        /// </summary>
        public Func<int, float> NextSignalSpeedLimitMpS;
        /// <summary>
        /// Aspect of the next signal.
        /// </summary>
        public Func<int, TrackMonitorSignalAspect> NextSignalAspect;
        /// <summary>
        /// Distance to next signal.
        /// </summary>
        public Func<int, float> NextSignalDistanceM;
        /// <summary>
        /// Aspect of the DISTANCE heads of next NORMAL signal.
        /// </summary>
        public Func<TrackMonitorSignalAspect> NextNormalSignalDistanceHeadsAspect;
        /// <summary>
        /// Next normal signal has only two aspects (STOP and CLEAR_2).
        /// </summary>
        public Func<bool> DoesNextNormalSignalHaveTwoAspects;
        /// <summary>
        /// Aspect of the next DISTANCE signal.
        /// </summary>
        public Func<TrackMonitorSignalAspect> NextDistanceSignalAspect;
        /// <summary>
        /// Distance to next DISTANCE signal.
        /// </summary>
        public Func<float> NextDistanceSignalDistanceM;
        /// <summary>
        /// Signal type of main head of hext generic signal. Not for NORMAL signals
        /// </summary>
        public Func<string, string> NextGenericSignalMainHeadSignalType;
        /// <summary>
        /// Aspect of the next generic signal. Not for NORMAL signals
        /// </summary>
        public Func<string, TrackMonitorSignalAspect> NextGenericSignalAspect;
        /// <summary>
        /// Distance to next generic signal. Not for NORMAL signals
        /// </summary>
        public Func<string, float> NextGenericSignalDistanceM;
        /// <summary>
        /// Features of next generic signal. 
        /// string: signal type (DISTANCE etc.)
        /// int: position of signal in the signal sequence along the train route, starting from train front; 0 for first signal;
        /// float: max testing distance
        /// </summary>
        public Func<string, int, float, SignalFeatures> NextGenericSignalFeatures;
        /// <summary>
        /// Next normal signal has a repeater head
        /// </summary>
        public Func<bool> DoesNextNormalSignalHaveRepeaterHead;
        /// <summary>
        /// Max allowed speed determined by current speedpost.
        /// </summary>
        public Func<float> CurrentPostSpeedLimitMpS;
        /// <summary>
        /// Max allowed speed determined by next speedpost.
        /// </summary>
        public Func<int, float> NextPostSpeedLimitMpS;
        /// <summary>
        /// Distance to next speedpost.
        /// </summary>
        public Func<int, float> NextPostDistanceM;
        /// <summary>
        /// Distance and length of next tunnels
        /// int: position of tunnel along the train route, starting from train front; 0 for first tunnel;
        /// If train is in tunnel, index 0 will contain the remaining length of the tunnel
        /// </summary>
        public Func<int, TunnelInfo> NextTunnel;
        /// <summary>
        /// Distance and value of next mileposts
        /// int: return nth milepost ahead; 0 for first milepost
        /// </summary>
        public Func<int, MilepostInfo> NextMilepost;
        /// <summary>
        /// Distance to end of authority.
        /// int: direction; 0: forwards; 1: backwards
        /// </summary>
        public Func<int, float> EOADistanceM;
        /// <summary>
        /// Train's length
        /// </summary>
        public Func<float> TrainLengthM;
        /// <summary>
        /// Locomotive direction.
        /// </summary>
        public Func<MidpointDirection> CurrentDirection;
        /// <summary>
        /// True if locomotive direction is forward.
        /// </summary>
        public Func<bool> IsDirectionForward;
        /// <summary>
        /// True if locomotive direction is neutral.
        /// </summary>
        public Func<bool> IsDirectionNeutral;
        /// <summary>
        /// True if locomotive direction is reverse.
        /// </summary>
        public Func<bool> IsDirectionReverse;
        /// <summary>
        /// Train direction.
        /// </summary>
        public Func<MidpointDirection> CurrentTrainMUDirection;
        /// <summary>
        /// True if locomotive is flipped.
        /// </summary>
        public Func<bool> IsFlipped;
        /// <summary>
        /// True if player is in rear cab.
        /// </summary>
        public Func<bool> IsRearCab;
        /// <summary>
        /// True if train brake controller is in emergency position, otherwise false.
        /// </summary>
        public Func<bool> IsBrakeEmergency;
        /// <summary>
        /// True if train brake controller is in full service position, otherwise false.
        /// </summary>
        public Func<bool> IsBrakeFullService;
        /// <summary>
        /// True if circuit breaker or power contactor closing authorization is true.
        /// </summary>
        public Func<bool> PowerAuthorization;
        /// <summary>
        /// True if circuit breaker or power contactor closing order is true.
        /// </summary>
        public Func<bool> CircuitBreakerClosingOrder;
        /// <summary>
        /// True if circuit breaker or power contactor opening order is true.
        /// </summary>
        public Func<bool> CircuitBreakerOpeningOrder;
        /// <summary>
        /// Returns the number of pantographs on the locomotive.
        /// </summary>
        public Func<int> PantographCount;
        /// <summary>
        /// Checks the state of any pantograph
        /// int: pantograph ID (1 for first pantograph)
        /// </summary>
        public Func<int, PantographState> GetPantographState;
        /// <summary>
        /// True if all pantographs are down.
        /// </summary>
        public Func<bool> ArePantographsDown;
        /// <summary>
        /// Returns throttle percent
        /// </summary>
        public Func<float> ThrottlePercent;
        /// <summary>
        /// Returns dynamic brake percent
        /// </summary>
        public Func<float> DynamicBrakePercent;
        /// <summary>
        /// True if traction is authorized.
        /// </summary>
        public Func<bool> TractionAuthorization;
        /// <summary>
        /// Train brake pipe pressure. Returns float.MaxValue if no data is available.
        /// </summary>
        public Func<float> BrakePipePressureBar;
        /// <summary>
        /// Locomotive brake cylinder pressure. Returns float.MaxValue if no data is available.
        /// </summary>
        public Func<float> LocomotiveBrakeCylinderPressureBar;
        /// <summary>
        /// True if power must be cut if the brake is applied.
        /// </summary>
        public Func<bool> DoesBrakeCutPower;
        /// <summary>
        /// Train brake pressure value which triggers the power cut-off.
        /// </summary>
        public Func<float> BrakeCutsPowerAtBrakeCylinderPressureBar;
        /// <summary>
        /// State of the train brake controller.
        /// </summary>
        public Func<ControllerState> TrainBrakeControllerState;
        /// <summary>
        /// Locomotive acceleration.
        /// </summary>
        public Func<float> AccelerationMpSS;
        /// <summary>
        /// Locomotive altitude.
        /// </summary>
        public Func<float> AltitudeM;
        /// <summary>
        /// Track gradient percent at the locomotive's location (positive = uphill).
        /// </summary>
        public Func<float> CurrentGradientPercent;
        /// <summary>
        /// Line speed taken from .trk file.
        /// </summary>
        public Func<float> LineSpeedMpS;
        /// <summary>
        /// True if starting from terminal station (no track behind the train).
        /// </summary>
        public Func<bool> DoesStartFromTerminalStation;
        /// <summary>
        /// True if game just started and train speed = 0.
        /// </summary>
        public Func<bool> IsColdStart;
        /// <summary>
        /// Get front traveller track node offset.
        /// </summary>
        public Func<float> GetTrackNodeOffset;
        /// <summary>
        /// Search next diverging switch distance
        /// </summary>
        public Func<float, float> NextDivergingSwitchDistanceM;
        /// <summary>
        /// Search next trailing diverging switch distance
        /// </summary>
        public Func<float, float> NextTrailingDivergingSwitchDistanceM;
        /// <summary>
        /// Get Control Mode of player train
        /// </summary>
        public Func<TrainControlMode> GetControlMode;
        /// <summary>
        /// Get name of next station if any, else empty string
        /// </summary>
        public Func<string> NextStationName;
        /// <summary>
        /// Get distance of next station if any, else max float value
        /// </summary>
        public Func<float> NextStationDistanceM;
        /// <summary>
        /// Get locomotive handle
        /// </summary>
        public Func<dynamic> Locomotive; //TODO 20200729 MSTSLocomotive not known here
        /// <summary>
        /// (float targetDistanceM, float targetSpeedMpS, float slope, float delayS, float decelerationMpS2)
        /// Returns a speed curve based speed limit, unit is m/s
        /// </summary>
        public Func<float, float, float, float, float, float> SpeedCurve;
        /// <summary>
        /// (float currentSpeedMpS, float targetSpeedMpS, float slope, float delayS, float decelerationMpS2)
        /// Returns a distance curve based safe braking distance, unit is m
        /// </summary>
        public Func<float, float, float, float, float, float> DistanceCurve;
        /// <summary>
        /// (float currentSpeedMpS, float targetSpeedMpS, float distanceM)
        /// Returns the deceleration needed to decrease the speed to the target speed at the target distance
        /// </summary>
        public Func<float, float, float, float> Deceleration;

        /// <summary>
        /// Set train brake controller to full service position.
        /// </summary>
        public Action<bool> SetFullBrake;
        /// <summary>
        /// Set emergency braking on or off.
        /// </summary>
        public Action<bool> SetEmergencyBrake;
        /// Set full dynamic braking on or off.
        /// </summary>
        public Action<bool> SetFullDynamicBrake;
        /// <summary>
        /// Set throttle controller to position in range [0-1].
        /// </summary>
        public Action<float> SetThrottleController;
        /// <summary>
        /// Set dynamic brake controller to position in range [0-1].
        /// </summary>
        public Action<float> SetDynamicBrakeController;
        /// <summary>
        /// Cut power by pull all pantographs down.
        /// </summary>
        public Action SetPantographsDown;
        /// <summary>
        /// Raise specified pantograph
        /// int: pantographID, from 1 to 4
        /// </summary>
        public Action<int> SetPantographUp;
        /// <summary>
        /// Lower specified pantograph
        /// int: pantographID, from 1 to 4
        /// </summary>
        public Action<int> SetPantographDown;
        /// <summary>
        /// Set the circuit breaker or power contactor closing authorization.
        /// </summary>
        public Action<bool> SetPowerAuthorization;
        /// <summary>
        /// Set the circuit breaker or power contactor closing order.
        /// </summary>
        public Action<bool> SetCircuitBreakerClosingOrder;
        /// <summary>
        /// Set the circuit breaker or power contactor opening order.
        /// </summary>
        public Action<bool> SetCircuitBreakerOpeningOrder;
        /// <summary>
        /// Set the traction authorization.
        /// </summary>
        public Action<bool> SetTractionAuthorization;
        /// <summary>
        /// Switch vigilance alarm sound on (true) or off (false).
        /// </summary>
        public Action<bool> SetVigilanceAlarm;
        /// <summary>
        /// Set horn on (true) or off (false).
        /// </summary>
        public Action<bool> SetHorn;
        /// <summary>
        /// Trigger Alert1 sound event
        /// </summary>
        public Action TriggerSoundAlert1;
        /// <summary>
        /// Trigger Alert2 sound event
        /// </summary>
        public Action TriggerSoundAlert2;
        /// <summary>
        /// Trigger Info1 sound event
        /// </summary>
        public Action TriggerSoundInfo1;
        /// <summary>
        /// Trigger Info2 sound event
        /// </summary>
        public Action TriggerSoundInfo2;
        /// <summary>
        /// Trigger Penalty1 sound event
        /// </summary>
        public Action TriggerSoundPenalty1;
        /// <summary>
        /// Trigger Penalty2 sound event
        /// </summary>
        public Action TriggerSoundPenalty2;
        /// <summary>
        /// Trigger Warning1 sound event
        /// </summary>
        public Action TriggerSoundWarning1;
        /// <summary>
        /// Trigger Warning2 sound event
        /// </summary>
        public Action TriggerSoundWarning2;
        /// <summary>
        /// Trigger Activate sound event
        /// </summary>
        public Action TriggerSoundSystemActivate;
        /// <summary>
        /// Trigger Deactivate sound event
        /// </summary>
        public Action TriggerSoundSystemDeactivate;
        /// <summary>
        /// Trigger generic sound event
        /// </summary>
        public Action<TrainEvent> TriggerGenericSound;
        /// <summary>
        /// Set ALERTER_DISPLAY cabcontrol display's alarm state on or off.
        /// </summary>
        public Action<bool> SetVigilanceAlarmDisplay;
        /// <summary>
        /// Set ALERTER_DISPLAY cabcontrol display's emergency state on or off.
        /// </summary>
        public Action<bool> SetVigilanceEmergencyDisplay;
        /// <summary>
        /// Set OVERSPEED cabcontrol display on or off.
        /// </summary>
        public Action<bool> SetOverspeedWarningDisplay;
        /// <summary>
        /// Set PENALTY_APP cabcontrol display on or off.
        /// </summary>
        public Action<bool> SetPenaltyApplicationDisplay;
        /// <summary>
        /// Monitoring status determines the colors speeds displayed with. (E.g. circular speed gauge).
        /// </summary>
        public Action<MonitoringStatus> SetMonitoringStatus;
        /// <summary>
        /// Set current speed limit of the train, as to be shown on SPEEDLIMIT cabcontrol.
        /// </summary>
        public Action<float> SetCurrentSpeedLimitMpS;
        /// <summary>
        /// Set speed limit of the next signal, as to be shown on SPEEDLIM_DISPLAY cabcontrol.
        /// </summary>
        public Action<float> SetNextSpeedLimitMpS;
        /// <summary>
        /// The speed at the train control system applies brake automatically.
        /// Determines needle color (orange/red) on circular speed gauge, when the locomotive
        /// already runs above the permitted speed limit. Otherwise is unused.
        /// </summary>
        public Action<float> SetInterventionSpeedLimitMpS;
        /// <summary>
        /// Will be whown on ASPECT_DISPLAY cabcontrol.
        /// </summary>
        public Action<TrackMonitorSignalAspect> SetNextSignalAspect;
        /// <summary>
        /// Sets the value for a cabview control.
        /// </summary>
        public Action<int, float> SetCabDisplayControl;
        /// <summary>
        /// Sets the name which is to be shown which putting the cursor above a cabview control.
        /// DEPRECATED
        /// </summary>
        public Action<string> SetCustomizedTCSControlString;
        /// <summary>
        /// Sets the name which is to be shown which putting the cursor above a cabview control.
        /// </summary>
        public Action<int, string> SetCustomizedCabviewControlName;
        /// <summary>
        /// Requests toggle to and from Manual Mode.
        /// </summary>
        public Action RequestToggleManualMode;
        /// <summary>
        /// Get bool parameter in the INI file.
        /// </summary>
        public Func<string, string, bool, bool> GetBoolParameter;
        /// <summary>
        /// Get int parameter in the INI file.
        /// </summary>
        public Func<string, string, int, int> GetIntParameter;
        /// <summary>
        /// Get int parameter in the INI file.
        /// </summary>
        public Func<string, string, float, float> GetFloatParameter;
        /// <summary>
        /// Get string parameter in the INI file.
        /// </summary>
        public Func<string, string, string, string> GetStringParameter;

        /// <summary>
        /// Called once at initialization time.
        /// </summary>
        public abstract void Initialize();
        /// <summary>
        /// Called once at initialization time if the train speed is greater than 0.
        /// Set at virtual to keep compatibility with scripts not providing this method.
        /// </summary>
        public virtual void InitializeMoving() { }
        /// <summary>
        /// Called regularly at every simulator update cycle.
        /// </summary>
        public abstract void Update();
        /// <summary>
        /// Called when an event happens (like the alerter button pressed)
        /// </summary>
        /// <param name="evt">The event happened</param>
        /// <param name="message">The message the event wants to communicate. May be empty.</param>
        public abstract void HandleEvent(TCSEvent evt, string message);
        /// <summary>
        /// Called by signalling code externally to stop the train in certain circumstances.
        /// </summary>
        public abstract void SetEmergency(bool emergency);
        /// <summary>
        /// Called when player has requested a game save. 
        /// Set at virtual to keep compatibility with scripts not providing this method.
        /// </summary>
        public virtual void Save(BinaryWriter outf) { }
        /// <summary>
        /// Called when player has requested a game restore. 
        /// Set at virtual to keep compatibility with scripts not providing this method.
        /// </summary>
        public virtual void Restore(BinaryReader inf) { }
    }

#pragma warning disable CA1815 // Override equals and operator equals on value types
    public readonly struct SignalFeatures
#pragma warning restore CA1815 // Override equals and operator equals on value types
    {
        private static readonly SignalFeatures none = new SignalFeatures(string.Empty, TrackMonitorSignalAspect.None, float.MaxValue, -1f, float.MinValue);
        public static ref readonly SignalFeatures None => ref none;

        public string MainHeadSignalTypeName { get; }
        public TrackMonitorSignalAspect Aspect { get; }
        public float Distance { get; }
        public float SpeedLimit { get; }
        public float Altitude { get; }
        public string TextAspect { get; }

        public SignalFeatures(string mainHeadSignalTypeName, TrackMonitorSignalAspect aspect, float distance, float speedLimit, float altitude, string textAspect = "")
        {
            MainHeadSignalTypeName = mainHeadSignalTypeName;
            Aspect = aspect;
            Distance = distance;
            SpeedLimit = speedLimit;
            Altitude = altitude;
            TextAspect = textAspect;
        }
    }

#pragma warning disable CA1815 // Override equals and operator equals on value types
    public readonly struct TunnelInfo
#pragma warning restore CA1815 // Override equals and operator equals on value types
    {
        /// <summary>
        /// Distance to tunnel (m)
        /// -1 if train is in tunnel
        /// </summary>
        public float Distance { get; }
        /// <summary>
        /// Tunnel length (m)
        /// If train is in tunnel, remaining distance to exit
        /// </summary>
        public float Length { get; }

        public TunnelInfo(float distance, float length)
        {
            Distance = distance;
            Length = length;
        }
    }

#pragma warning disable CA1815 // Override equals and operator equals on value types
    public readonly struct MilepostInfo
#pragma warning restore CA1815 // Override equals and operator equals on value types
    {
        /// <summary>
        /// Distance to milepost (m)
        /// </summary>
        public float Distance { get; }
        /// <summary>
        /// Value of the milepost
        /// </summary>
        public float Value { get; }

        public MilepostInfo(float distance, float value)
        {
            Distance = distance;
            Value = value;
        }
    }
}
