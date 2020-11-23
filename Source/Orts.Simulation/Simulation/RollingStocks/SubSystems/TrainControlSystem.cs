﻿// COPYRIGHT 2013, 2014 by the Open Rails project.
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

// Define this to log the wheel configurations on cars as they are loaded.
//#define DEBUG_WHEELS

using Orts.Simulation.Signalling;
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

using Orts.Common;
using Orts.Common.Calc;
using Orts.Formats.Msts;
using Orts.Formats.Msts.Parsers;
using Orts.Scripting.Api;
using Orts.Simulation.Physics;
using Orts.Formats.Msts.Models;
using Orts.Simulation.Track;

namespace Orts.Simulation.RollingStocks.SubSystems
{
    public class ScriptedTrainControlSystem
    {
        public class MonitoringDevice
        {
            public float MonitorTimeS = 66; // Time from alerter reset to applying emergency brake
            public float AlarmTimeS = 60; // Time from alerter reset to audible and visible alarm
            public float PenaltyTimeS;
            public float CriticalLevelMpS;
            public float ResetLevelMpS;
            public bool AppliesFullBrake = true;
            public bool AppliesEmergencyBrake;
            public bool EmergencyCutsPower;
            public bool EmergencyShutsDownEngine;
            public float AlarmTimeBeforeOverspeedS = 5;         // OverspeedMonitor only
            public float TriggerOnOverspeedMpS;                 // OverspeedMonitor only
            public bool TriggerOnTrackOverspeed;                // OverspeedMonitor only
            public float TriggerOnTrackOverspeedMarginMpS = 4;  // OverspeedMonitor only
            public bool ResetOnDirectionNeutral = false;
            public bool ResetOnZeroSpeed = true;
            public bool ResetOnResetButton;                     // OverspeedMonitor only

            public MonitoringDevice(STFReader stf)
            {
                stf.MustMatch("(");
                stf.ParseBlock(new STFReader.TokenProcessor[] {
                    new STFReader.TokenProcessor("monitoringdevicemonitortimelimit", () => { MonitorTimeS = stf.ReadFloatBlock(STFReader.Units.Time, MonitorTimeS); }),
                    new STFReader.TokenProcessor("monitoringdevicealarmtimelimit", () => { AlarmTimeS = stf.ReadFloatBlock(STFReader.Units.Time, AlarmTimeS); }),
                    new STFReader.TokenProcessor("monitoringdevicepenaltytimelimit", () => { PenaltyTimeS = stf.ReadFloatBlock(STFReader.Units.Time, PenaltyTimeS); }),
                    new STFReader.TokenProcessor("monitoringdevicecriticallevel", () => { CriticalLevelMpS = stf.ReadFloatBlock(STFReader.Units.Speed, CriticalLevelMpS); }),
                    new STFReader.TokenProcessor("monitoringdeviceresetlevel", () => { ResetLevelMpS = stf.ReadFloatBlock(STFReader.Units.Speed, ResetLevelMpS); }),
                    new STFReader.TokenProcessor("monitoringdeviceappliesfullbrake", () => { AppliesFullBrake = stf.ReadBoolBlock(AppliesFullBrake); }),
                    new STFReader.TokenProcessor("monitoringdeviceappliesemergencybrake", () => { AppliesEmergencyBrake = stf.ReadBoolBlock(AppliesEmergencyBrake); }),
                    new STFReader.TokenProcessor("monitoringdeviceappliescutspower", () => { EmergencyCutsPower = stf.ReadBoolBlock(EmergencyCutsPower); }),
                    new STFReader.TokenProcessor("monitoringdeviceappliesshutsdownengine", () => { EmergencyShutsDownEngine = stf.ReadBoolBlock(EmergencyShutsDownEngine); }),
                    new STFReader.TokenProcessor("monitoringdevicealarmtimebeforeoverspeed", () => { AlarmTimeBeforeOverspeedS = stf.ReadFloatBlock(STFReader.Units.Time, AlarmTimeBeforeOverspeedS); }),
                    new STFReader.TokenProcessor("monitoringdevicetriggeronoverspeed", () => { TriggerOnOverspeedMpS = stf.ReadFloatBlock(STFReader.Units.Speed, TriggerOnOverspeedMpS); }),
                    new STFReader.TokenProcessor("monitoringdevicetriggerontrackoverspeed", () => { TriggerOnTrackOverspeed = stf.ReadBoolBlock(TriggerOnTrackOverspeed); }),
                    new STFReader.TokenProcessor("monitoringdevicetriggerontrackoverspeedmargin", () => { TriggerOnTrackOverspeedMarginMpS = stf.ReadFloatBlock(STFReader.Units.Speed, TriggerOnTrackOverspeedMarginMpS); }),
                    new STFReader.TokenProcessor("monitoringdeviceresetondirectionneutral", () => { ResetOnDirectionNeutral = stf.ReadBoolBlock(ResetOnDirectionNeutral); }),
                    new STFReader.TokenProcessor("monitoringdeviceresetonresetbutton", () => { ResetOnResetButton = stf.ReadBoolBlock(ResetOnResetButton); }),
                    new STFReader.TokenProcessor("monitoringdeviceresetonzerospeed", () => { ResetOnZeroSpeed = stf.ReadBoolBlock(ResetOnZeroSpeed); }),
                });
            }

            public MonitoringDevice() { }

            public MonitoringDevice(MonitoringDevice other)
            {
                MonitorTimeS = other.MonitorTimeS;
                AlarmTimeS = other.AlarmTimeS;
                PenaltyTimeS = other.PenaltyTimeS;
                CriticalLevelMpS = other.CriticalLevelMpS;
                ResetLevelMpS = other.ResetLevelMpS;
                AppliesFullBrake = other.AppliesFullBrake;
                AppliesEmergencyBrake = other.AppliesEmergencyBrake;
                EmergencyCutsPower = other.EmergencyCutsPower;
                EmergencyShutsDownEngine = other.EmergencyShutsDownEngine;
                AlarmTimeBeforeOverspeedS = other.AlarmTimeBeforeOverspeedS;
                TriggerOnOverspeedMpS = other.TriggerOnOverspeedMpS;
                TriggerOnTrackOverspeed = other.TriggerOnTrackOverspeed;
                TriggerOnTrackOverspeedMarginMpS = other.TriggerOnTrackOverspeedMarginMpS;
                ResetOnDirectionNeutral = other.ResetOnDirectionNeutral;
                ResetOnZeroSpeed = other.ResetOnZeroSpeed;
                ResetOnResetButton = other.ResetOnResetButton;
            }
        }

        // Constants
        private const int TCSCabviewControlCount = 48;
        private const int TCSCommandCount = 48;

        // Properties
        public bool VigilanceAlarm { get; set; }
        public bool VigilanceEmergency { get; set; }
        public bool OverspeedWarning { get; set; }
        public bool PenaltyApplication { get; set; }
        public float CurrentSpeedLimitMpS { get; set; }
        public float NextSpeedLimitMpS { get; set; }
        public float InterventionSpeedLimitMpS { get; set; }
        public TrackMonitorSignalAspect CabSignalAspect { get; set; }
        public MonitoringStatus MonitoringStatus { get; set; }

        public bool Activated = false;

        TrainInfo TrainInfo = new TrainInfo();

        readonly MSTSLocomotive Locomotive;
        readonly Simulator Simulator;

        List<float> SignalSpeedLimits = new List<float>();
        List<TrackMonitorSignalAspect> SignalAspects = new List<TrackMonitorSignalAspect>();
        List<float> SignalDistances = new List<float>();
        List<float> PostSpeedLimits = new List<float>();
        List<float> PostDistances = new List<float>();
        TrackMonitorSignalAspect SignalAspect;
        float SignalDistance;
        string MainHeadSignalTypeName;

        MonitoringDevice VigilanceMonitor;
        MonitoringDevice OverspeedMonitor;
        MonitoringDevice EmergencyStopMonitor;
        MonitoringDevice AWSMonitor;

        public bool AlerterButtonPressed { get; private set; }
        public bool PowerAuthorization { get; private set; }
        public bool CircuitBreakerClosingOrder { get; private set;  }
        public bool CircuitBreakerOpeningOrder { get; private set; }
        public bool TractionAuthorization { get; private set; }
        public bool FullDynamicBrakingOrder { get; private set; }

        public float[] CabDisplayControls = new float[TCSCabviewControlCount];

        // generic TCS commands
        public bool[] TCSCommandButtonDown = new bool[TCSCommandCount];
        public bool[] TCSCommandSwitchOn = new bool[TCSCommandCount];
        // List of customized control strings;
        public string[] CustomizedCabviewControlNames = new string[TCSCabviewControlCount];
        // TODO : Delete this when SetCustomizedTCSControlString is deleted
        protected int NextCabviewControlNameToEdit = 0;

        string ScriptName;
        string SoundFileName;
        string ParametersFileName;
        TrainControlSystem Script;

        public Dictionary<TrainControlSystem, string> Sounds = new Dictionary<TrainControlSystem, string>();

        const float GravityMpS2 = 9.80665f;

        public ScriptedTrainControlSystem() { }

        public ScriptedTrainControlSystem(MSTSLocomotive locomotive)
        {
            Locomotive = locomotive;
            Simulator = Locomotive.Simulator;

            PowerAuthorization = true;
            CircuitBreakerClosingOrder = false;
            CircuitBreakerOpeningOrder = false;
            TractionAuthorization = true;
            FullDynamicBrakingOrder = false;
        }

        public void Parse(string lowercasetoken, STFReader stf)
        {
            switch (lowercasetoken)
            {
                case "engine(vigilancemonitor": VigilanceMonitor = new MonitoringDevice(stf); break;
                case "engine(overspeedmonitor": OverspeedMonitor = new MonitoringDevice(stf); break;
                case "engine(emergencystopmonitor": EmergencyStopMonitor = new MonitoringDevice(stf); break;
                case "engine(awsmonitor": AWSMonitor = new MonitoringDevice(stf); break;
                case "engine(ortstraincontrolsystem": ScriptName = stf.ReadStringBlock(null); break;
                case "engine(ortstraincontrolsystemsound": SoundFileName = stf.ReadStringBlock(null); break;
                case "engine(ortstraincontrolsystemparameters": ParametersFileName = stf.ReadStringBlock(null); break;
            }
        }

        public void Copy(ScriptedTrainControlSystem other)
        {
            ScriptName = other.ScriptName;
            SoundFileName = other.SoundFileName;
            ParametersFileName = other.ParametersFileName;
            if (other.VigilanceMonitor != null) VigilanceMonitor = new MonitoringDevice(other.VigilanceMonitor);
            if (other.OverspeedMonitor != null) OverspeedMonitor = new MonitoringDevice(other.OverspeedMonitor);
            if (other.EmergencyStopMonitor != null) EmergencyStopMonitor = new MonitoringDevice(other.EmergencyStopMonitor);
            if (other.AWSMonitor != null) AWSMonitor = new MonitoringDevice(other.AWSMonitor);
        }

        //Debrief Eval
        public static int DbfevalFullBrakeAbove16kmh = 0;
        public bool ldbfevalfullbrakeabove16kmh = false;

        public void Initialize()
        {
            if (!Activated)
            {
                if (!Simulator.Settings.DisableTCSScripts && ScriptName != null && ScriptName != "MSTS" && ScriptName != "")
                {
                Script = Simulator.ScriptManager.Load(Path.Combine(Path.GetDirectoryName(Locomotive.WagFilePath), "Script"), ScriptName) as TrainControlSystem;
                }

                if (ParametersFileName != null)
                {
                    ParametersFileName = Path.Combine(Path.Combine(Path.GetDirectoryName(Locomotive.WagFilePath), "Script"), ParametersFileName);
                }

                if (Script == null)
                {
                    Script = new MSTSTrainControlSystem();
                    ((MSTSTrainControlSystem)Script).VigilanceMonitor = VigilanceMonitor;
                    ((MSTSTrainControlSystem)Script).OverspeedMonitor = OverspeedMonitor;
                    ((MSTSTrainControlSystem)Script).EmergencyStopMonitor = EmergencyStopMonitor;
                    ((MSTSTrainControlSystem)Script).AWSMonitor = AWSMonitor;
                    ((MSTSTrainControlSystem)Script).EmergencyCausesThrottleDown = Locomotive.EmergencyCausesThrottleDown;
                    ((MSTSTrainControlSystem)Script).EmergencyEngagesHorn = Locomotive.EmergencyEngagesHorn;
                }

                if (SoundFileName != null)
                {
                    var soundPathArray = new[] {
                    Path.Combine(Path.GetDirectoryName(Locomotive.WagFilePath), "SOUND"),
                    Path.Combine(Simulator.BasePath, "SOUND"),
                };
                var soundPath = FolderStructure.FindFileFromFolders(soundPathArray, SoundFileName);
                    if (File.Exists(soundPath))
                        Sounds.Add(Script, soundPath);
                }

                // AbstractScriptClass
                Script.ClockTime = () => (float)Simulator.ClockTime;
                Script.GameTime = () => (float)Simulator.GameTime;
                Script.DistanceM = () => Locomotive.DistanceM;
                Script.Confirm = Locomotive.Simulator.Confirmer.Confirm;
                Script.Message = Locomotive.Simulator.Confirmer.Message;
                Script.SignalEvent = Locomotive.SignalEvent;
                Script.SignalEventToTrain = (evt) =>
                {
                    if (Locomotive.Train != null)
                    {
                        Locomotive.Train.SignalEvent(evt);
                    }
                };

                // TrainControlSystem getters
                Script.IsTrainControlEnabled = () => Locomotive == Locomotive.Train.LeadLocomotive && Locomotive.Train.TrainType != TrainType.AiPlayerHosting;
                Script.IsAutopiloted = () => Locomotive == Simulator.PlayerLocomotive && Locomotive.Train.TrainType == TrainType.AiPlayerHosting;
                Script.IsAlerterEnabled = () =>
                {
                    return Simulator.Settings.Alerter
                        && !(Simulator.Settings.AlerterDisableExternal
                            && !Simulator.PlayerIsInCab
                        );
                };
                Script.IsSpeedControlEnabled = () => Simulator.Settings.SpeedControl;
                Script.AlerterSound = () => Locomotive.AlerterSnd;
                Script.TrainSpeedLimitMpS = () => TrainInfo.AllowedSpeed;
                Script.TrainMaxSpeedMpS = () => Locomotive.Train.TrainMaxSpeedMpS;
                Script.CurrentSignalSpeedLimitMpS = () => Locomotive.Train.allowedMaxSpeedSignalMpS;
                Script.NextSignalSpeedLimitMpS = (value) => NextSignalItem<float>(value, ref SignalSpeedLimits, TrainPathItemType.Signal);
            Script.NextSignalAspect = (value) => NextSignalItem<TrackMonitorSignalAspect>(value, ref SignalAspects, TrainPathItemType.Signal);
                Script.NextSignalDistanceM = (value) => NextSignalItem<float>(value, ref SignalDistances, TrainPathItemType.Signal);
                Script.NextNormalSignalDistanceHeadsAspect = () => NextNormalSignalDistanceHeadsAspect();
                Script.DoesNextNormalSignalHaveTwoAspects = () => DoesNextNormalSignalHaveTwoAspects();
                Script.NextDistanceSignalAspect = () =>
                NextGenericSignalItem<TrackMonitorSignalAspect>(ref SignalAspect, TrainPathItemType.Signal, "DISTANCE");
                Script.NextDistanceSignalDistanceM = () =>
                    NextGenericSignalItem<float>(ref SignalDistance, TrainPathItemType.Signal, "DISTANCE");
                Script.NextGenericSignalMainHeadSignalType = (string type) =>
                    NextGenericSignalItem<string>(ref MainHeadSignalTypeName, TrainPathItemType.Signal, type);
                Script.NextGenericSignalAspect = (string type) =>
                NextGenericSignalItem<TrackMonitorSignalAspect>(ref SignalAspect, TrainPathItemType.Signal, type);
                Script.NextGenericSignalDistanceM = (string type) =>
                    NextGenericSignalItem<float>(ref SignalDistance, TrainPathItemType.Signal, type);
                Script.DoesNextNormalSignalHaveRepeaterHead = () => DoesNextNormalSignalHaveRepeaterHead();
                Script.CurrentPostSpeedLimitMpS = () => Locomotive.Train.allowedMaxSpeedLimitMpS;
                Script.NextPostSpeedLimitMpS = (value) => NextSignalItem<float>(value, ref PostSpeedLimits, TrainPathItemType.Speedpost);
                Script.NextPostDistanceM = (value) => NextSignalItem<float>(value, ref PostDistances, TrainPathItemType.Speedpost);
                Script.TrainLengthM = () => Locomotive.Train != null ? Locomotive.Train.Length : 0f;
                Script.SpeedMpS = () => Math.Abs(Locomotive.SpeedMpS);
                Script.CurrentDirection = () => Locomotive.Direction;
                Script.IsDirectionForward = () => Locomotive.Direction == MidpointDirection.Forward;
                Script.IsDirectionNeutral = () => Locomotive.Direction == MidpointDirection.N;
                Script.IsDirectionReverse = () => Locomotive.Direction == MidpointDirection.Reverse;
                Script.IsBrakeEmergency = () => Locomotive.TrainBrakeController.EmergencyBraking;
                Script.IsBrakeFullService = () => Locomotive.TrainBrakeController.TCSFullServiceBraking;
                Script.PowerAuthorization = () => PowerAuthorization;
                Script.CircuitBreakerClosingOrder = () => CircuitBreakerClosingOrder;
                Script.CircuitBreakerOpeningOrder = () => CircuitBreakerOpeningOrder;
                Script.PantographState = (pantoID) => Locomotive.Pantographs[pantoID].State;
                Script.ArePantographsDown = () => Locomotive.Pantographs.State == PantographState.Down;
                Script.ThrottlePercent = () => Locomotive.ThrottleController.CurrentValue * 100;
                Script.TractionAuthorization = () => TractionAuthorization;
            Script.BrakePipePressureBar = () => Locomotive.BrakeSystem != null ? (float)Pressure.Atmospheric.FromPSI(Locomotive.BrakeSystem.BrakeLine1PressurePSI) : float.MaxValue;
            Script.LocomotiveBrakeCylinderPressureBar = () => Locomotive.BrakeSystem != null ? (float)Pressure.Atmospheric.FromPSI(Locomotive.BrakeSystem.GetCylPressurePSI()) : float.MaxValue;
                Script.DoesBrakeCutPower = () => Locomotive.DoesBrakeCutPower;
            Script.BrakeCutsPowerAtBrakeCylinderPressureBar = () => (float)Pressure.Atmospheric.FromPSI(Locomotive.BrakeCutsPowerAtBrakeCylinderPressurePSI);
                Script.CurrentElevationPercent = () => Locomotive.CurrentElevationPercent;
            Script.LineSpeedMpS = () => (float)Simulator.TRK.Route.SpeedLimit;
                Script.DoesStartFromTerminalStation = () => DoesStartFromTerminalStation();
                Script.IsColdStart = () => Locomotive.Train.ColdStart;
                Script.GetTrackNodeOffset = () => Locomotive.Train.FrontTDBTraveller.TrackNodeLength - Locomotive.Train.FrontTDBTraveller.TrackNodeOffset;
                Script.NextDivergingSwitchDistanceM = (value) => NextDivergingSwitchItem<float>(value, ref SignalDistance, TrainPathItemType.FacingSwitch);
                Script.NextTrailingDivergingSwitchDistanceM = (value) => Locomotive.Train.NextTrailingDivergingSwitchDistanceM(value);
            Script.GetControlMode = () => (TrainControlMode)(int)Locomotive.Train.ControlMode;
                Script.NextStationName = () => Locomotive.Train.StationStops != null && Locomotive.Train.StationStops.Count > 0 ? Locomotive.Train.StationStops[0].PlatformItem.Name : "";
                Script.NextStationDistanceM = () => Locomotive.Train.StationStops != null && Locomotive.Train.StationStops.Count > 0 ? Locomotive.Train.StationStops[0].DistanceToTrainM : float.MaxValue;
                Script.Locomotive = () => Locomotive;

                // TrainControlSystem functions
                Script.SpeedCurve = (arg1, arg2, arg3, arg4, arg5) => SpeedCurve(arg1, arg2, arg3, arg4, arg5);
                Script.DistanceCurve = (arg1, arg2, arg3, arg4, arg5) => DistanceCurve(arg1, arg2, arg3, arg4, arg5);
                Script.Deceleration = (arg1, arg2, arg3) => Deceleration(arg1, arg2, arg3);

                // TrainControlSystem setters
                Script.SetFullBrake = (value) =>
                {
                    if (Locomotive.TrainBrakeController.TCSFullServiceBraking != value)
                    {
                        Locomotive.TrainBrakeController.TCSFullServiceBraking = value;

                    //Debrief Eval
                    if (value && Locomotive.IsPlayerTrain && !ldbfevalfullbrakeabove16kmh && Math.Abs(Locomotive.SpeedMpS) > 4.44444)
                        {
                            var train = Simulator.PlayerLocomotive.Train;//Debrief Eval
                        DbfevalFullBrakeAbove16kmh++;
                            ldbfevalfullbrakeabove16kmh = true;
                            train.DbfEvalValueChanged = true;//Debrief eval
                    }
                        if (!value)
                            ldbfevalfullbrakeabove16kmh = false;
                    }
                };
                Script.SetEmergencyBrake = (value) =>
                {
                    if (Locomotive.TrainBrakeController.TCSEmergencyBraking != value)
                        Locomotive.TrainBrakeController.TCSEmergencyBraking = value;
                };
                Script.SetFullDynamicBrake = (value) => FullDynamicBrakingOrder = value;
                Script.SetThrottleController = (value) => Locomotive.ThrottleController.SetValue(value);
                Script.SetDynamicBrakeController = (value) =>
                {
                if (Locomotive.DynamicBrakeController == null) return;
                Locomotive.DynamicBrakeChangeActiveState(value > 0);
                Locomotive.DynamicBrakeController.SetValue(value);
                };
                Script.SetPantographsDown = () =>
                {
                    if (Locomotive.Pantographs.State == PantographState.Up)
                    {
                        Locomotive.Train.SignalEvent(PowerSupplyEvent.LowerPantograph);
                    }
                };
                Script.SetPantograph = (pantoCommand, pantoID) => Locomotive.Train.SignalEvent(pantoCommand, pantoID);
                Script.SetPowerAuthorization = (value) => PowerAuthorization = value;
                Script.SetCircuitBreakerClosingOrder = (value) => CircuitBreakerClosingOrder = value;
                Script.SetCircuitBreakerOpeningOrder = (value) => CircuitBreakerOpeningOrder = value;
                Script.SetTractionAuthorization = (value) => TractionAuthorization = value;
            Script.SetVigilanceAlarm = (value) => Locomotive.SignalEvent(value ? TrainEvent.VigilanceAlarmOn : TrainEvent.VigilanceAlarmOff);
                Script.SetHorn = (value) => Locomotive.TCSHorn = value;
            Script.TriggerSoundAlert1 = () => this.SignalEvent(TrainEvent.TrainControlSystemAlert1, Script);
            Script.TriggerSoundAlert2 = () => this.SignalEvent(TrainEvent.TrainControlSystemAlert2, Script);
            Script.TriggerSoundInfo1 = () => this.SignalEvent(TrainEvent.TrainControlSystemInfo1, Script);
            Script.TriggerSoundInfo2 = () => this.SignalEvent(TrainEvent.TrainControlSystemInfo2, Script);
            Script.TriggerSoundPenalty1 = () => this.SignalEvent(TrainEvent.TrainControlSystemPenalty1, Script);
            Script.TriggerSoundPenalty2 = () => this.SignalEvent(TrainEvent.TrainControlSystemPenalty2, Script);
            Script.TriggerSoundWarning1 = () => this.SignalEvent(TrainEvent.TrainControlSystemWarning1, Script);
            Script.TriggerSoundWarning2 = () => this.SignalEvent(TrainEvent.TrainControlSystemWarning2, Script);
            Script.TriggerSoundSystemActivate = () => this.SignalEvent(TrainEvent.TrainControlSystemActivate, Script);
            Script.TriggerSoundSystemDeactivate = () => this.SignalEvent(TrainEvent.TrainControlSystemDeactivate, Script);
                Script.SetVigilanceAlarmDisplay = (value) => this.VigilanceAlarm = value;
                Script.SetVigilanceEmergencyDisplay = (value) => this.VigilanceEmergency = value;
                Script.SetOverspeedWarningDisplay = (value) => this.OverspeedWarning = value;
                Script.SetPenaltyApplicationDisplay = (value) => this.PenaltyApplication = value;
                Script.SetMonitoringStatus = (value) => this.MonitoringStatus = value;
                Script.SetCurrentSpeedLimitMpS = (value) => this.CurrentSpeedLimitMpS = value;
                Script.SetNextSpeedLimitMpS = (value) => this.NextSpeedLimitMpS = value;
                Script.SetInterventionSpeedLimitMpS = (value) => this.InterventionSpeedLimitMpS = value;
                Script.SetNextSignalAspect = (value) => this.CabSignalAspect = (TrackMonitorSignalAspect)value;
                Script.SetCabDisplayControl = (arg1, arg2) => CabDisplayControls[arg1] = arg2;
                Script.SetCustomizedTCSControlString = (value) =>
                {
                    if (NextCabviewControlNameToEdit == 0)
                    {
                        Trace.TraceWarning("SetCustomizedTCSControlString is deprecated. Please use SetCustomizedCabviewControlName.");
                    }

                    if (NextCabviewControlNameToEdit < TCSCabviewControlCount)
                    {
                        CustomizedCabviewControlNames[NextCabviewControlNameToEdit] = value;
                    }

                    NextCabviewControlNameToEdit++;
                };
                Script.SetCustomizedCabviewControlName = (id, value) =>
                {
                    if (id >= 0 && id < TCSCabviewControlCount)
                    {
                        CustomizedCabviewControlNames[id] = value;
                    }
                };
                Script.RequestToggleManualMode = () => Locomotive.Train.RequestToggleManualMode();

                // TrainControlSystem INI configuration file
                Script.GetBoolParameter = (arg1, arg2, arg3) => LoadParameter<bool>(arg1, arg2, arg3);
                Script.GetIntParameter = (arg1, arg2, arg3) => LoadParameter<int>(arg1, arg2, arg3);
                Script.GetFloatParameter = (arg1, arg2, arg3) => LoadParameter<float>(arg1, arg2, arg3);
                Script.GetStringParameter = (arg1, arg2, arg3) => LoadParameter<string>(arg1, arg2, arg3);

                Script.Initialize();
                Activated = true;
            }
        }

        public void InitializeMoving()
        {
            Script?.InitializeMoving();
        }

        T NextSignalItem<T>(int forsight, ref List<T> list, TrainPathItemType type)
        {
            if (forsight < 0) forsight = 0;
            if (forsight >= list.Count) SearchTrainInfo(forsight, type);
            // <CSComment> Doesn't : list.Count - 1 lead to wrong returns if forsight > 0? </CSComment>
            return list[forsight < list.Count ? forsight : list.Count - 1];
        }

        void SearchTrainInfo(float forsight, TrainPathItemType searchFor)
        {
            if (SignalSpeedLimits.Count == 0)
                TrainInfo = Locomotive.Train.GetTrainInfo();

            var signalsFound = 0;
            var postsFound = 0;

            foreach (var foundItem in Locomotive.Train.MUDirection == MidpointDirection.Reverse ? TrainInfo.ObjectInfoBackward : TrainInfo.ObjectInfoForward)
            {
                switch (foundItem.ItemType)
                {
                    case TrainPathItemType.Signal:
                        signalsFound++;
                        if (signalsFound > SignalSpeedLimits.Count)
                        {
                            SignalSpeedLimits.Add(foundItem.AllowedSpeedMpS);
                            SignalAspects.Add((TrackMonitorSignalAspect)foundItem.SignalState);
                            SignalDistances.Add(foundItem.DistanceToTrainM);
                        }
                        break;

                    case TrainPathItemType.Speedpost:
                        postsFound++;
                        if (postsFound > PostSpeedLimits.Count)
                        {
                            PostSpeedLimits.Add(foundItem.AllowedSpeedMpS);
                            PostDistances.Add(foundItem.DistanceToTrainM);
                        }
                        break;

                    case TrainPathItemType.Authority:
                        signalsFound++;
                        if (signalsFound > SignalSpeedLimits.Count)
                        {
                            SignalSpeedLimits.Add(0f);
                            SignalAspects.Add(TrackMonitorSignalAspect.Stop);
                            SignalDistances.Add(foundItem.DistanceToTrainM);
                        }
                        break;
                }

                if (searchFor == TrainPathItemType.Signal && signalsFound > forsight ||
                    searchFor == TrainPathItemType.Speedpost && postsFound > forsight)
                {
                    break;
                }
            }

            if (searchFor == TrainPathItemType.Signal && signalsFound == 0)
            {
                SignalSpeedLimits.Add(-1);
                SignalAspects.Add(TrackMonitorSignalAspect.None);
                SignalDistances.Add(float.MaxValue);
            }
            if (searchFor == TrainPathItemType.Speedpost && postsFound == 0)
            {
                PostSpeedLimits.Add(-1);
                PostDistances.Add(float.MaxValue);
            }
        }

        private TrackMonitorSignalAspect NextNormalSignalDistanceHeadsAspect()
        {
            var signal = Locomotive.Train.NextSignalObject[Locomotive.Train.MUDirection == MidpointDirection.Reverse ? 1 : 0];
            if (signal != null)
            {
                foreach (var signalHead in signal.SignalHeads)
                {
                    if (signalHead.SignalType.FunctionType == SignalFunction.Distance)
                    {
                        return SignalEnvironment.TranslateToTCSAspect(signal.SignalLR(SignalFunction.Distance));
                    }
                }
            }
            return TrackMonitorSignalAspect.None; ;
        }

        private bool DoesNextNormalSignalHaveTwoAspects()
            // ...and the two aspects of each head are STOP and ( CLEAR_2 or CLEAR_1 or RESTRICTING)
        {
            var signal = Locomotive.Train.NextSignalObject[Locomotive.Train.MUDirection == MidpointDirection.Reverse ? 1 : 0];
            if (signal != null)
            {
                if (signal.SignalHeads[0].SignalType.Aspects.Count > 2) return false;
                else
                {
                    foreach (var signalHead in signal.SignalHeads)
                    {
                        if (signalHead.SignalType.FunctionType != SignalFunction.Distance &&
                            signalHead.SignalType.Aspects.Count == 2 &&
                            (int)(signalHead.SignalType.Aspects[0].Aspect) == 0 &&
                                ((int)(signalHead.SignalType.Aspects[1].Aspect) == 7 ||
                                (int)(signalHead.SignalType.Aspects[1].Aspect) == 6 ||
                                (int)(signalHead.SignalType.Aspects[1].Aspect) == 2)) continue;
                        else return false;
                    }
                    return true;
                }
            }
            return true;
        }

        T NextGenericSignalItem<T>(ref T retval, TrainPathItemType type, string signalTypeName)
        {
            SignalAspect = TrackMonitorSignalAspect.None;
            SignalDistance = float.MaxValue;
            MainHeadSignalTypeName = "";

            int fn_type = OrSignalTypes.Instance.FunctionTypes.IndexOf(signalTypeName);
            if (fn_type <= 0) // Invalid value or NORMAL signal
                return retval;

            Direction direction = Locomotive.Train.MUDirection == MidpointDirection.Reverse ? Direction.Backward : Direction.Forward;

            if (Locomotive.Train.ValidRoute[(int)direction] == null || direction == Direction.Backward && Locomotive.Train.PresentPosition[direction].TrackCircuitSectionIndex < 0)
                return retval;

            int index = direction == 0 ? Locomotive.Train.PresentPosition[direction].RouteListIndex : 
                Locomotive.Train.ValidRoute[(int)direction].GetRouteIndex(Locomotive.Train.PresentPosition[direction].TrackCircuitSectionIndex, 0);
            if (index < 0)
                return retval;

            float lengthOffset = (direction == Direction.Backward) ? (-Locomotive.Train.PresentPosition[Direction.Backward].Offset +
                     TrackCircuitSection.TrackCircuitList[Locomotive.Train.PresentPosition[Direction.Backward].TrackCircuitSectionIndex].Length) : Locomotive.Train.PresentPosition[Direction.Forward].Offset;
            float totalLength = 0;
            var routePath = Locomotive.Train.ValidRoute[(int)direction];
            while (index < routePath.Count && totalLength < 400) // Check only first 400m
            {
                var thisElement = routePath[index];
                TrackCircuitSection thisSection = thisElement.TrackCircuitSection;
                TrackCircuitSignalList thisSignalList = thisSection.CircuitItems.TrackCircuitSignals[thisElement.Direction][fn_type];
                foreach (TrackCircuitSignalItem thisSignal in thisSignalList)
                {
                    if (thisSignal.SignalLocation > lengthOffset)
                    {
                        SignalAspect = SignalEnvironment.TranslateToTCSAspect(thisSignal.Signal.SignalLRLimited(fn_type));
                        SignalDistance = thisSignal.SignalLocation - lengthOffset + totalLength;
                        MainHeadSignalTypeName = thisSignal.Signal.SignalHeads[0].SignalType?.Name ?? string.Empty;
                        return retval;
                    }
                }
                totalLength += (thisSection.Length - lengthOffset);
                lengthOffset = 0;

                int setSection = thisSection.ActivePins[thisElement.OutPin[Location.NearEnd], (Location)thisElement.OutPin[Location.FarEnd]].Link;
                index++;
                if (setSection < 0)
                    return retval;
            }

            return retval;
        }

        private bool DoesNextNormalSignalHaveRepeaterHead()
        {
            var signal = Locomotive.Train.NextSignalObject[Locomotive.Train.MUDirection == MidpointDirection.Reverse ? 1 : 0];
            if (signal != null)
            {
                foreach (var signalHead in signal.SignalHeads)
                {
                    if (signalHead.SignalType.FunctionType == SignalFunction.Repeater) return true;
                }
                return false;
            }
            return false;
        }

        private bool DoesStartFromTerminalStation()
        {
            var tempTraveller = new Traveller(Locomotive.Train.RearTDBTraveller);
            tempTraveller.ReverseDirection();
            return tempTraveller.NextTrackNode() && tempTraveller.IsEnd;
        }


        public void SignalEvent(TrainEvent evt, TrainControlSystem script)
        {
            try
            { 
                foreach (var eventHandler in Locomotive.EventHandlers)
                    eventHandler.HandleEvent(evt, script);
            }
            catch (Exception error)
            {
                Trace.TraceInformation("Sound event skipped due to thread safety problem" + error.Message);
            }
        }

        T NextDivergingSwitchItem<T>(float maxDistanceM, ref T retval, TrainPathItemType type)
        {
            var LocalTrainInfo = Locomotive.Train.GetTrainInfo();
            SignalDistance = float.MaxValue;
            foreach (var foundItem in Locomotive.Train.MUDirection == MidpointDirection.Reverse ? TrainInfo.ObjectInfoBackward : TrainInfo.ObjectInfoForward)
            {
                if (foundItem.DistanceToTrainM > maxDistanceM)
                {
                    return retval;
                }
                else if (foundItem.ItemType == type)
                {
                    SignalDistance = foundItem.DistanceToTrainM;
                    return retval;
                }
            }
            return retval;
        }

        private static float SpeedCurve(float targetDistanceM, float targetSpeedMpS, float slope, float delayS, float decelerationMpS2)
        {
            if (targetSpeedMpS < 0)
                targetSpeedMpS = 0;

            decelerationMpS2 -= GravityMpS2 * slope;

            float squareSpeedComponent = targetSpeedMpS * targetSpeedMpS
                + (delayS * delayS) * decelerationMpS2 * decelerationMpS2
                + 2f * targetDistanceM * decelerationMpS2;

            float speedComponent = delayS * decelerationMpS2;

            return (float)Math.Sqrt(squareSpeedComponent) - speedComponent;
        }

        private static float DistanceCurve(float currentSpeedMpS, float targetSpeedMpS, float slope, float delayS, float decelerationMpS2)
        {
            if (targetSpeedMpS < 0)
                targetSpeedMpS = 0;

            float brakingDistanceM = (currentSpeedMpS * currentSpeedMpS - targetSpeedMpS * targetSpeedMpS)
                / (2 * (decelerationMpS2 - GravityMpS2 * slope));

            float delayDistanceM = delayS * currentSpeedMpS;

            return brakingDistanceM + delayDistanceM;
        }

        private static float Deceleration(float currentSpeedMpS, float targetSpeedMpS, float distanceM)
        {
            return (currentSpeedMpS - targetSpeedMpS) * (currentSpeedMpS + targetSpeedMpS) / (2 * distanceM);
        }

        public void Update()
        {
            switch (Locomotive.Train.TrainType)
            {
                case TrainType.Static:
                case TrainType.Ai:
                case TrainType.AiNotStarted:
                case TrainType.AiAutoGenerated:
                case TrainType.Remote:
                case TrainType.AiIncorporated:
                    DisableRestrictions();
                    break;

                default:
                    if (Script == null)
                    {
                        DisableRestrictions();
                    }
                    else
                    {
                        ClearParams();
                        Script.Update();
                    }
                    break;
            }
        }

        public void DisableRestrictions()
        {
            PowerAuthorization = true;
            if (Locomotive.TrainBrakeController != null)
            {
                Locomotive.TrainBrakeController.TCSFullServiceBraking = false;
                Locomotive.TrainBrakeController.TCSEmergencyBraking = false;
            }
        }

        public void ClearParams()
        {
            SignalSpeedLimits.Clear();
            SignalAspects.Clear();
            SignalDistances.Clear();
            PostSpeedLimits.Clear();
            PostDistances.Clear();
        }

        public void AlerterPressed(bool pressed)
        {
            AlerterButtonPressed = pressed;
            HandleEvent(pressed ? TCSEvent.AlerterPressed : TCSEvent.AlerterReleased);
        }

        public void AlerterReset()
        {
            HandleEvent(TCSEvent.AlerterReset);
        }

        public void SetEmergency(bool emergency)
        {
            if (Script != null)
                Script.SetEmergency(emergency);
            else
                Locomotive.TrainBrakeController.TCSEmergencyBraking = emergency;
        }

        public void HandleEvent(TCSEvent evt)
        {
            HandleEvent(evt, String.Empty);
        }

        public void HandleEvent(TCSEvent evt, string message)
        {
            if (Script != null)
                Script.HandleEvent(evt, message);
        }

        public void HandleEvent(TCSEvent evt, int eventIndex)
        {
            if (Script != null)
            {
                var message = eventIndex.ToString();
                Script.HandleEvent(evt, message);
            }
        }

        private T LoadParameter<T>(string sectionName, string keyName, T defaultValue)
        {
            var buffer = new String('\0', 256);

            var length = Orts.Common.Native.NativeMethods.GetPrivateProfileString(sectionName, keyName, null, buffer, buffer.Length, ParametersFileName);
            if (length > 0)
            {
                buffer.Trim();
                return (T)Convert.ChangeType(buffer, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
            }
            else
                return defaultValue;
        }


        // Converts the generic string (e.g. ORTS_TCS5) shown when browsing with the mouse on a TCS control
        // to a customized string defined in the script
        public string GetDisplayString(string originalString)
        {
            if (originalString.Length < 9) return originalString;
            if (originalString.Substring(0, 8) != "ORTS_TCS") return originalString;
            var commandIndex = Convert.ToInt32(originalString.Substring(8));
            return commandIndex > 0 && commandIndex <= TCSCabviewControlCount && CustomizedCabviewControlNames[commandIndex - 1] != ""
                ? CustomizedCabviewControlNames[commandIndex - 1]
                : originalString;
        }

        public void Save(BinaryWriter outf)
        {
            outf.Write(ScriptName ?? "");
            if (ScriptName != "")
                Script.Save(outf);
        }

        public void Restore(BinaryReader inf)
        {
            ScriptName = inf.ReadString();
            if (ScriptName != "")
            {
                Initialize();
                Script.Restore(inf);
            }
        }

    }

    public class MSTSTrainControlSystem : TrainControlSystem
    {
        public enum MonitorState
        {
            Disabled,
            StandBy,
            Alarm,
            Emergency
        };

        public bool ResetButtonPressed { get; private set; }

        public bool VigilanceSystemEnabled
        {
            get
            {
                bool enabled = true;

                enabled &= IsAlerterEnabled();

                if (VigilanceMonitor != null)
                {
                    if (VigilanceMonitor.ResetOnDirectionNeutral)
                    {
                        enabled &= CurrentDirection() != MidpointDirection.N;
                    }

                    if (VigilanceMonitor.ResetOnZeroSpeed)
                    {
                        enabled &= SpeedMpS() >= 0.1f;
                    }
                }

                return enabled;
            }
        }

        public bool VigilanceReset
        {
            get
            {
                bool vigilanceReset = true;

                if (VigilanceMonitor != null)
                {
                    if (VigilanceMonitor.ResetOnDirectionNeutral)
                    {
                        vigilanceReset &= CurrentDirection() == MidpointDirection.N;
                    }

                    if (VigilanceMonitor.ResetOnZeroSpeed)
                    {
                        vigilanceReset &= SpeedMpS() < 0.1f;
                    }

                    if (VigilanceMonitor.ResetOnResetButton)
                    {
                        vigilanceReset &= ResetButtonPressed;
                    }
                }

                return vigilanceReset;
            }
        }

        public bool SpeedControlSystemEnabled
        {
            get
            {
                bool enabled = true;

                enabled &= IsSpeedControlEnabled();

                return enabled;
            }
        }

        public bool Overspeed
        {
            get
            {
                bool overspeed = false;

                if (OverspeedMonitor != null)
                {
                    if (OverspeedMonitor.TriggerOnOverspeedMpS > 0)
                    {
                        overspeed |= SpeedMpS() > OverspeedMonitor.TriggerOnOverspeedMpS;
                    }

                    if (OverspeedMonitor.CriticalLevelMpS > 0)
                    {
                        overspeed |= SpeedMpS() > OverspeedMonitor.CriticalLevelMpS;
                    }

                    if (OverspeedMonitor.TriggerOnTrackOverspeed)
                    {
                        overspeed |= SpeedMpS() > CurrentSpeedLimitMpS + OverspeedMonitor.TriggerOnTrackOverspeedMarginMpS;
                    }
                }

                return overspeed;
            }
        }

        public bool OverspeedReset
        {
            get
            {
                bool overspeedReset = true;

                if (OverspeedMonitor != null)
                {
                    if (OverspeedMonitor.ResetOnDirectionNeutral)
                    {
                        overspeedReset &= CurrentDirection() == MidpointDirection.N;
                    }

                    if (OverspeedMonitor.ResetOnZeroSpeed)
                    {
                        overspeedReset &= SpeedMpS() < 0.1f;
                    }

                    if (OverspeedMonitor.ResetOnResetButton)
                    {
                        overspeedReset &= ResetButtonPressed;
                    }
                }

                return overspeedReset;
            }
        }

        Timer VigilanceAlarmTimer;
        Timer VigilanceEmergencyTimer;
        Timer VigilancePenaltyTimer;
        Timer OverspeedEmergencyTimer;
        Timer OverspeedPenaltyTimer;

        MonitorState VigilanceMonitorState;
        MonitorState OverspeedMonitorState;
        bool ExternalEmergency;

        float VigilanceAlarmTimeoutS;
        float CurrentSpeedLimitMpS;
        float NextSpeedLimitMpS;
       
        MonitoringStatus Status;

        public ScriptedTrainControlSystem.MonitoringDevice VigilanceMonitor;
        public ScriptedTrainControlSystem.MonitoringDevice OverspeedMonitor;
        public ScriptedTrainControlSystem.MonitoringDevice EmergencyStopMonitor;
        public ScriptedTrainControlSystem.MonitoringDevice AWSMonitor;
        public bool EmergencyCausesThrottleDown;
        public bool EmergencyEngagesHorn;

        public MSTSTrainControlSystem() { }

        public override void Initialize()
        {
            VigilanceAlarmTimer = new Timer(this);
            VigilanceEmergencyTimer = new Timer(this);
            VigilancePenaltyTimer = new Timer(this);
            OverspeedEmergencyTimer = new Timer(this);
            OverspeedPenaltyTimer = new Timer(this);

            if (VigilanceMonitor != null)
            {
                if (VigilanceMonitor.MonitorTimeS > VigilanceMonitor.AlarmTimeS)
                    VigilanceAlarmTimeoutS = VigilanceMonitor.MonitorTimeS - VigilanceMonitor.AlarmTimeS;
                VigilanceAlarmTimer.Setup(VigilanceMonitor.AlarmTimeS);
                VigilanceEmergencyTimer.Setup(VigilanceAlarmTimeoutS);
                VigilancePenaltyTimer.Setup(VigilanceMonitor.PenaltyTimeS);
                VigilanceAlarmTimer.Start();
            }
            if (OverspeedMonitor != null)
            {
                OverspeedEmergencyTimer.Setup(Math.Max(OverspeedMonitor.AlarmTimeS, OverspeedMonitor.AlarmTimeBeforeOverspeedS));
                OverspeedPenaltyTimer.Setup(OverspeedMonitor.PenaltyTimeS);
            }

            Activated = true;
        }

        public override void Update()
        {
            UpdateInputs();

            if (IsTrainControlEnabled())
            {
                if (VigilanceMonitor != null)
                    UpdateVigilance();
                if (OverspeedMonitor != null)
                    UpdateSpeedControl();

                bool EmergencyBrake = false;
                bool FullBrake = false;
                bool PowerCut = false;

                if (VigilanceMonitor != null)
                {
                    if (VigilanceMonitor.AppliesEmergencyBrake)
                        EmergencyBrake |= (VigilanceMonitorState == MonitorState.Emergency);
                    else if (VigilanceMonitor.AppliesFullBrake)
                        FullBrake |= (VigilanceMonitorState == MonitorState.Emergency);

                    if (VigilanceMonitor.EmergencyCutsPower)
                        PowerCut |= (VigilanceMonitorState == MonitorState.Emergency);
                }

                if (OverspeedMonitor != null)
                {
                    if (OverspeedMonitor.AppliesEmergencyBrake)
                        EmergencyBrake |= (OverspeedMonitorState == MonitorState.Emergency);
                    else if (OverspeedMonitor.AppliesFullBrake)
                        FullBrake |= (OverspeedMonitorState == MonitorState.Emergency);

                    if (OverspeedMonitor.EmergencyCutsPower)
                        PowerCut |= (OverspeedMonitorState == MonitorState.Emergency);
                }

                if (EmergencyStopMonitor != null)
                {
                    if (EmergencyStopMonitor.AppliesEmergencyBrake)
                        EmergencyBrake |= ExternalEmergency;
                    else if (EmergencyStopMonitor.AppliesFullBrake)
                        FullBrake |= ExternalEmergency;

                    if (EmergencyStopMonitor.EmergencyCutsPower)
                        PowerCut |= ExternalEmergency;
                }

                SetTractionAuthorization(!DoesBrakeCutPower() || LocomotiveBrakeCylinderPressureBar() < BrakeCutsPowerAtBrakeCylinderPressureBar());

                SetEmergencyBrake(EmergencyBrake);
                SetFullBrake(FullBrake);
                SetPowerAuthorization(!PowerCut);

                if (EmergencyCausesThrottleDown && (IsBrakeEmergency() || IsBrakeFullService()))
                    SetThrottleController(0f);

                if (EmergencyEngagesHorn)
                    SetHorn(IsBrakeEmergency() || IsBrakeFullService());

                SetPenaltyApplicationDisplay(IsBrakeEmergency() || IsBrakeFullService());

                // Update monitoring status
                if (SpeedMpS() > CurrentSpeedLimitMpS)
                {
                    if (OverspeedMonitor != null && (OverspeedMonitor.AppliesEmergencyBrake || OverspeedMonitor.AppliesFullBrake))
                        Status = MonitoringStatus.Intervention;
                    else
                        Status = MonitoringStatus.Warning;
                }
                else if (NextSpeedLimitMpS < CurrentSpeedLimitMpS && SpeedMpS() > NextSpeedLimitMpS)
                {
                    if (Deceleration(SpeedMpS(), NextSpeedLimitMpS, NextSignalDistanceM(0)) > 0.7f)
                        Status = MonitoringStatus.Overspeed;
                    else
                        Status = MonitoringStatus.Indication;
                }
                else
                    Status = MonitoringStatus.Normal;
                SetMonitoringStatus(Status);
            }
        }

        public void UpdateInputs()
        {
            SetNextSignalAspect(NextSignalAspect(0));

            CurrentSpeedLimitMpS = CurrentSignalSpeedLimitMpS();
            if (CurrentSpeedLimitMpS < 0 || CurrentSpeedLimitMpS > TrainSpeedLimitMpS())
                CurrentSpeedLimitMpS = TrainSpeedLimitMpS();

            // TODO: NextSignalSpeedLimitMpS(0) should return 0 if the signal is at stop; cause seems to be updateSpeedInfo() within Train.cs
            NextSpeedLimitMpS = NextSignalAspect(0) != TrackMonitorSignalAspect.Stop ? (NextSignalSpeedLimitMpS(0) > 0 && NextSignalSpeedLimitMpS(0) < TrainSpeedLimitMpS() ? NextSignalSpeedLimitMpS(0) : TrainSpeedLimitMpS() ) : 0;

            SetCurrentSpeedLimitMpS(CurrentSpeedLimitMpS);
            SetNextSpeedLimitMpS(NextSpeedLimitMpS);
        }

        public override void HandleEvent(TCSEvent evt, string message)
        {
            switch(evt)
            {
                case TCSEvent.AlerterPressed:
                case TCSEvent.AlerterReleased:
                case TCSEvent.AlerterReset:
                    if (Activated)
                    {
                        switch (VigilanceMonitorState)
                        {
                            // case VigilanceState.Disabled: do nothing

                            case MonitorState.StandBy:
                                VigilanceAlarmTimer.Stop();
                                break;

                            case MonitorState.Alarm:
                                VigilanceEmergencyTimer.Stop();
                                VigilanceMonitorState = MonitorState.StandBy;
                                break;

                            // case VigilanceState.Emergency: do nothing
                        }
                    }
                    break;
            }

            switch (evt)
            {
                case TCSEvent.AlerterPressed:
                    ResetButtonPressed = true;
                    break;

                case TCSEvent.AlerterReleased:
                    ResetButtonPressed = false;
                    break;
            }
        }

        public override void SetEmergency(bool emergency)
        {
            ExternalEmergency = emergency;
        }

        void UpdateVigilance()
        {
            switch (VigilanceMonitorState)
            {
                case MonitorState.Disabled:
                    if (VigilanceSystemEnabled)
                    {
                        VigilanceMonitorState = MonitorState.StandBy;
                    }
                    break;

                case MonitorState.StandBy:
                    if (!VigilanceSystemEnabled)
                    {
                        VigilanceAlarmTimer.Stop();
                        VigilanceMonitorState = MonitorState.Disabled;
                    }
                    else
                    {
                        if (!VigilanceAlarmTimer.Started)
                        {
                            VigilanceAlarmTimer.Start();
                        }

                        if (VigilanceAlarmTimer.Triggered)
                        {
                            VigilanceAlarmTimer.Stop();
                            VigilanceMonitorState = MonitorState.Alarm;
                        }
                    }
                    break;

                case MonitorState.Alarm:
                    if (!VigilanceSystemEnabled)
                    {
                        VigilanceEmergencyTimer.Stop();
                        VigilanceMonitorState = MonitorState.Disabled;
                    }
                    else
                    {
                        if (!VigilanceEmergencyTimer.Started)
                        {
                            VigilanceEmergencyTimer.Start();
                        }

                        if (VigilanceEmergencyTimer.Triggered)
                        {
                            VigilanceEmergencyTimer.Stop();
                            VigilanceMonitorState = MonitorState.Emergency;
                        }
                    }
                    break;

                case MonitorState.Emergency:
                    if (!VigilancePenaltyTimer.Started)
                    {
                        VigilancePenaltyTimer.Start();
                    }

                    if (VigilancePenaltyTimer.Triggered && VigilanceReset)
                    {
                        VigilanceEmergencyTimer.Stop();
                        VigilanceMonitorState = (VigilanceSystemEnabled ? MonitorState.StandBy : MonitorState.Disabled);
                    }
                    break;
            }

            if (VigilanceMonitorState >= MonitorState.Alarm)
            {
                if (!AlerterSound())
                {
                    SetVigilanceAlarm(true);
                }
            }
            else
            {
                if (AlerterSound())
                {
                    SetVigilanceAlarm(false);
                }
            }

            SetVigilanceAlarmDisplay(VigilanceMonitorState == MonitorState.Alarm);
            SetVigilanceEmergencyDisplay(VigilanceMonitorState == MonitorState.Emergency);
        }

        void UpdateSpeedControl()
        {
            var interventionSpeedMpS = CurrentSpeedLimitMpS + Speed.MeterPerSecond.FromKpH(5.0f); // Default margin : 5 km/h
            
            if (OverspeedMonitor.TriggerOnTrackOverspeed)
            {
                interventionSpeedMpS = CurrentSpeedLimitMpS + OverspeedMonitor.TriggerOnTrackOverspeedMarginMpS;
            }
            
            SetInterventionSpeedLimitMpS((float)interventionSpeedMpS);

            switch (OverspeedMonitorState)
            {
                case MonitorState.Disabled:
                    if (SpeedControlSystemEnabled)
                    {
                        OverspeedMonitorState = MonitorState.StandBy;
                    }
                    break;

                case MonitorState.StandBy:
                    if (!SpeedControlSystemEnabled)
                    {
                        OverspeedMonitorState = MonitorState.Disabled;
                    }
                    else
                    {
                        if (Overspeed)
                        {
                            OverspeedMonitorState = MonitorState.Alarm;
                        }
                    }
                    break;

                case MonitorState.Alarm:
                    if (!SpeedControlSystemEnabled)
                    {
                        OverspeedMonitorState = MonitorState.Disabled;
                    }
                    else
                    {
                        if (!OverspeedEmergencyTimer.Started)
                        {
                            OverspeedEmergencyTimer.Start();
                        }

                        if (!Overspeed)
                        {
                            OverspeedEmergencyTimer.Stop();
                            OverspeedMonitorState = MonitorState.StandBy;
                        }
                        else if (OverspeedEmergencyTimer.Triggered)
                        {
                            OverspeedEmergencyTimer.Stop();
                            OverspeedMonitorState = MonitorState.Emergency;
                        }
                    }
                    break;

                case MonitorState.Emergency:
                    if (!OverspeedPenaltyTimer.Started)
                    {
                        OverspeedPenaltyTimer.Start();
                    }

                    if (OverspeedPenaltyTimer.Triggered && OverspeedReset)
                    {
                        OverspeedPenaltyTimer.Stop();
                        OverspeedMonitorState = MonitorState.StandBy;
                    }
                    break;
            }

            SetOverspeedWarningDisplay(OverspeedMonitorState >= MonitorState.Alarm);
        }
    }
}
