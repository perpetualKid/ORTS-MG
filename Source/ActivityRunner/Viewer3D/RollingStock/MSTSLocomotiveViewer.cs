// COPYRIGHT 2009, 2010, 2011, 2012, 2013, 2014, 2015 by the Open Rails project.
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
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Orts.ActivityRunner.Viewer3D.Common;
using Orts.ActivityRunner.Viewer3D.Popups;
using Orts.ActivityRunner.Viewer3D.RollingStock.SubSystems.Etcs;
using Orts.ActivityRunner.Viewer3D.Shapes;
using Orts.Common;
using Orts.Common.Calc;
using Orts.Common.Input;
using Orts.Common.Position;
using Orts.Common.Xna;
using Orts.Formats.Msts;
using Orts.Formats.Msts.Files;
using Orts.Formats.Msts.Models;
using Orts.Simulation;
using Orts.Simulation.Commanding;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems.Controllers;

namespace Orts.ActivityRunner.Viewer3D.RollingStock
{
    public class MSTSLocomotiveViewer : MSTSWagonViewer
    {
        private MSTSLocomotive Locomotive;

        protected MSTSLocomotive MSTSLocomotive { get { return (MSTSLocomotive)Car; } }

        public bool HasCabRenderer { get; private set; }
        public bool Has3DCabRenderer { get; private set; }
        public CabRenderer CabRenderer { get; private set; }
        public ThreeDimentionCabViewer CabViewer3D { get; private set; }
        public CabRenderer CabRenderer3D { get; internal set; } //allow user to have different setting of .cvf file under CABVIEW3D

        public static int DbfEvalEBPBstopped = 0;//Debrief eval
        public static int DbfEvalEBPBmoving = 0;//Debrief eval
        public bool lemergencybuttonpressed = false;

        public MSTSLocomotiveViewer(Viewer viewer, MSTSLocomotive car)
            : base(viewer, car)
        {
            Locomotive = car;

            string wagonFolderSlash = Path.GetDirectoryName(Locomotive.WagFilePath) + "\\";
            if (Locomotive.CabSoundFileName != null) LoadCarSound(wagonFolderSlash, Locomotive.CabSoundFileName);

            //Viewer.SoundProcess.AddSoundSource(this, new TrackSoundSource(MSTSWagon, Viewer));

            if (Locomotive.TrainControlSystem != null && Locomotive.TrainControlSystem.Sounds.Count > 0)
                foreach (var script in Locomotive.TrainControlSystem.Sounds.Keys)
                {
                    try
                    {
                        Viewer.SoundProcess.AddSoundSources(script, new List<SoundSourceBase>() {
                            new SoundSource(Viewer, Locomotive, Locomotive.TrainControlSystem.Sounds[script])});
                    }
                    catch (Exception error)
                    {
                        Trace.TraceInformation("File " + Locomotive.TrainControlSystem.Sounds[script] + " in script of locomotive of train " + Locomotive.Train.Name + " : " + error.Message);
                    }
                }
        }

        protected virtual void StartGearBoxIncrease()
        {
            if (Locomotive.GearBoxController != null)
                Locomotive.StartGearBoxIncrease();
        }

        protected virtual void StopGearBoxIncrease()
        {
            if (Locomotive.GearBoxController != null)
                Locomotive.StopGearBoxIncrease();
        }

        protected virtual void StartGearBoxDecrease()
        {
            if (Locomotive.GearBoxController != null)
                Locomotive.StartGearBoxDecrease();
        }

        protected virtual void StopGearBoxDecrease()
        {
            if (Locomotive.GearBoxController != null)
                Locomotive.StopGearBoxDecrease();
        }

        protected virtual void ReverserControlForwards()
        {
            if (Locomotive.Direction != MidpointDirection.Forward
            && (Locomotive.ThrottlePercent >= 1
            || Math.Abs(Locomotive.SpeedMpS) > 1))
            {
                Viewer.Simulator.Confirmer.Warning(CabControl.Reverser, CabSetting.Warn1);
                return;
            }
            new ReverserCommand(Viewer.Log, true);    // No harm in trying to engage Forward when already engaged.
        }

        protected virtual void ReverserControlBackwards()
        {
            if (Locomotive.Direction != MidpointDirection.Reverse
            && (Locomotive.ThrottlePercent >= 1
            || Math.Abs(Locomotive.SpeedMpS) > 1))
            {
                Viewer.Simulator.Confirmer.Warning(CabControl.Reverser, CabSetting.Warn1);
                return;
            }
            new ReverserCommand(Viewer.Log, false);    // No harm in trying to engage Reverse when already engaged.
        }

        /// <summary>
        /// A keyboard or mouse click has occurred. Read the UserInput
        /// structure to determine what was pressed.
        /// </summary>
        public override void HandleUserInput(in ElapsedTime elapsedTime)
        {
            //Debrief eval
            if (!lemergencybuttonpressed && Locomotive.EmergencyButtonPressed && Locomotive.IsPlayerTrain)
            {
                var train = Program.Viewer.PlayerLocomotive.Train;
                if (Math.Abs(Locomotive.SpeedMpS) == 0)
                    DbfEvalEBPBstopped++;
                if (Math.Abs(Locomotive.SpeedMpS) > 0)
                    DbfEvalEBPBmoving++;
                lemergencybuttonpressed = true;
                train.DbfEvalValueChanged = true;//Debrief eval
            }
            //Debrief eval
            if (lemergencybuttonpressed && !Locomotive.EmergencyButtonPressed) lemergencybuttonpressed = false;
        }

        public override void RegisterUserCommandHandling()
        {
            Viewer.UserCommandController.AddEvent(UserCommand.CameraToggleShowCab, KeyEventType.KeyPressed, ShowCabCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.DebugResetWheelSlip, KeyEventType.KeyPressed, DebugResetWheelSlipCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.DebugToggleAdvancedAdhesion, KeyEventType.KeyPressed, DebugToggleAdvancedAdhesionCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlReverserForward, KeyEventType.KeyPressed, ReverserControlForwards, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlReverserBackward, KeyEventType.KeyPressed, ReverserControlBackwards, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlThrottleIncrease, KeyEventType.KeyPressed, Locomotive.StartThrottleIncrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlThrottleDecrease, KeyEventType.KeyPressed, Locomotive.StartThrottleDecrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlThrottleIncrease, KeyEventType.KeyReleased, Locomotive.StopThrottleIncrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlThrottleDecrease, KeyEventType.KeyReleased, Locomotive.StopThrottleDecrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlThrottleZero, KeyEventType.KeyPressed, Locomotive.ThrottleToZero, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlTrainBrakeIncrease, KeyEventType.KeyPressed, Locomotive.StartTrainBrakeIncrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlTrainBrakeDecrease, KeyEventType.KeyPressed, Locomotive.StartTrainBrakeDecrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlTrainBrakeIncrease, KeyEventType.KeyReleased, Locomotive.StopTrainBrakeIncrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlTrainBrakeDecrease, KeyEventType.KeyReleased, Locomotive.StopTrainBrakeDecrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlTrainBrakeZero, KeyEventType.KeyPressed, Locomotive.StartTrainBrakeZero, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlEngineBrakeIncrease, KeyEventType.KeyPressed, Locomotive.StartEngineBrakeIncrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlEngineBrakeDecrease, KeyEventType.KeyPressed, Locomotive.StartEngineBrakeDecrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlEngineBrakeIncrease, KeyEventType.KeyReleased, Locomotive.StopEngineBrakeIncrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlEngineBrakeDecrease, KeyEventType.KeyReleased, Locomotive.StopEngineBrakeDecrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlBrakemanBrakeIncrease, KeyEventType.KeyPressed, Locomotive.StartBrakemanBrakeIncrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlBrakemanBrakeDecrease, KeyEventType.KeyPressed, Locomotive.StartBrakemanBrakeDecrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlBrakemanBrakeIncrease, KeyEventType.KeyReleased, Locomotive.StopBrakemanBrakeIncrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlBrakemanBrakeDecrease, KeyEventType.KeyReleased, Locomotive.StopBrakemanBrakeDecrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlDynamicBrakeIncrease, KeyEventType.KeyPressed, Locomotive.StartDynamicBrakeIncrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlDynamicBrakeDecrease, KeyEventType.KeyPressed, Locomotive.StartDynamicBrakeDecrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlDynamicBrakeIncrease, KeyEventType.KeyReleased, Locomotive.StopDynamicBrakeIncrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlDynamicBrakeDecrease, KeyEventType.KeyReleased, Locomotive.StopDynamicBrakeDecrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlGearUp, KeyEventType.KeyPressed, StartGearBoxIncrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlGearDown, KeyEventType.KeyPressed, StartGearBoxDecrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlGearUp, KeyEventType.KeyReleased, StopGearBoxIncrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlGearDown, KeyEventType.KeyReleased, StopGearBoxDecrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlSteamHeatIncrease, KeyEventType.KeyPressed, Locomotive.StartSteamHeatIncrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlSteamHeatDecrease, KeyEventType.KeyPressed, Locomotive.StartSteamHeatDecrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlSteamHeatIncrease, KeyEventType.KeyReleased, Locomotive.StopSteamHeatIncrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlSteamHeatDecrease, KeyEventType.KeyReleased, Locomotive.StopSteamHeatDecrease, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlBailOff, KeyEventType.KeyPressed, BailOffOnCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlBailOff, KeyEventType.KeyReleased, BailOffOffCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlInitializeBrakes, KeyEventType.KeyPressed, InitializeBrakesCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlHandbrakeNone, KeyEventType.KeyPressed, HandbrakeNoneCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlHandbrakeFull, KeyEventType.KeyPressed, HandbrakeFullCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlRetainersOff, KeyEventType.KeyPressed, RetainersOffCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlRetainersOn, KeyEventType.KeyPressed, RetainersOnCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlBrakeHoseConnect, KeyEventType.KeyPressed, BrakeHoseConnectCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlBrakeHoseDisconnect, KeyEventType.KeyPressed, BrakeHoseDisconnectCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlEmergencyPushButton, KeyEventType.KeyPressed, EmergencyPushButtonCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlSander, KeyEventType.KeyPressed, SanderOnCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlSander, KeyEventType.KeyReleased, SanderOffCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlSanderToggle, KeyEventType.KeyPressed, SanderToogleCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlWiper, KeyEventType.KeyPressed, WiperCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlHorn, KeyEventType.KeyPressed, HornOnCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlHorn, KeyEventType.KeyReleased, HornOffCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlBell, KeyEventType.KeyPressed, BellOnCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlBell, KeyEventType.KeyReleased, BellOffCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlBellToggle, KeyEventType.KeyPressed, BellToggleCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlAlerter, KeyEventType.KeyPressed, AlerterOnCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlAlerter, KeyEventType.KeyReleased, AlerterOffCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlHeadlightIncrease, KeyEventType.KeyPressed, HeadlightIncreaseCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlHeadlightDecrease, KeyEventType.KeyPressed, HeadlightDecreaseCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlLight, KeyEventType.KeyPressed, ToggleCabLightCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlRefill, KeyEventType.KeyPressed, AttemptToRefillOrUnload, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlRefill, KeyEventType.KeyReleased, StopRefillingOrUnloading, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlImmediateRefill, KeyEventType.KeyPressed, ImmediateRefill, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlImmediateRefill, KeyEventType.KeyReleased, StopImmediateRefilling, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlWaterScoop, KeyEventType.KeyPressed, ToggleWaterScoopCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlOdoMeterShowHide, KeyEventType.KeyPressed, ResetOdometerCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlOdoMeterReset, KeyEventType.KeyPressed, ResetOdometerCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlOdoMeterDirection, KeyEventType.KeyPressed, ToggleOdometerDirectionCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlCabRadio, KeyEventType.KeyPressed, CabRadioCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlDieselHelper, KeyEventType.KeyPressed, ToggleHelpersEngineCommand, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlGeneric1, KeyEventType.KeyPressed, GenericCommand1On, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlGeneric1, KeyEventType.KeyReleased, GenericCommand1Off, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlGeneric2, KeyEventType.KeyPressed, GenericCommand2On, true);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlGeneric2, KeyEventType.KeyReleased, GenericCommand2Off, true);
            Viewer.UserCommandController.AddEvent(AnalogUserCommand.Light, LightSwitchCommand);
            Viewer.UserCommandController.AddEvent(AnalogUserCommand.Wiper, WiperSwitchCommand);
            Viewer.UserCommandController.AddEvent(AnalogUserCommand.Direction, DirectionHandleCommand);
            Viewer.UserCommandController.AddEvent(AnalogUserCommand.Throttle, ThrottleHandleCommand);
            Viewer.UserCommandController.AddEvent(AnalogUserCommand.DynamicBrake, DynamicBrakeHandleCommand);
            Viewer.UserCommandController.AddEvent(AnalogUserCommand.TrainBrake, TrainBrakeHandleCommand);
            Viewer.UserCommandController.AddEvent(AnalogUserCommand.EngineBrake, EngineBrakeHandleCommand);
            Viewer.UserCommandController.AddEvent(AnalogUserCommand.BailOff, BailOffHandleCommand);
            Viewer.UserCommandController.AddEvent(AnalogUserCommand.Emergency, EmergencyHandleCommand);
            Viewer.UserCommandController.AddEvent(AnalogUserCommand.CabActivity, AlerterResetCommand);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlBattery, KeyEventType.KeyPressed, ToggleBatteryCommand);
            Viewer.UserCommandController.AddEvent(UserCommand.ControlPowerKey, KeyEventType.KeyPressed, TogglePowerKeyCommand);

            base.RegisterUserCommandHandling();
        }

        public override void UnregisterUserCommandHandling()
        {
            Viewer.UserCommandController.RemoveEvent(UserCommand.CameraToggleShowCab, KeyEventType.KeyPressed, ShowCabCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.DebugResetWheelSlip, KeyEventType.KeyPressed, DebugResetWheelSlipCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.DebugToggleAdvancedAdhesion, KeyEventType.KeyPressed, DebugToggleAdvancedAdhesionCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlReverserForward, KeyEventType.KeyPressed, ReverserControlForwards);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlReverserBackward, KeyEventType.KeyPressed, ReverserControlBackwards);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlThrottleIncrease, KeyEventType.KeyPressed, Locomotive.StartThrottleIncrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlThrottleDecrease, KeyEventType.KeyPressed, Locomotive.StartThrottleDecrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlThrottleIncrease, KeyEventType.KeyReleased, Locomotive.StopThrottleIncrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlThrottleDecrease, KeyEventType.KeyReleased, Locomotive.StopThrottleDecrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlThrottleZero, KeyEventType.KeyPressed, Locomotive.ThrottleToZero);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlTrainBrakeIncrease, KeyEventType.KeyPressed, Locomotive.StartTrainBrakeIncrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlTrainBrakeDecrease, KeyEventType.KeyPressed, Locomotive.StartTrainBrakeDecrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlTrainBrakeIncrease, KeyEventType.KeyReleased, Locomotive.StopTrainBrakeIncrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlTrainBrakeDecrease, KeyEventType.KeyReleased, Locomotive.StopTrainBrakeDecrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlTrainBrakeZero, KeyEventType.KeyPressed, Locomotive.StartTrainBrakeZero);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlEngineBrakeIncrease, KeyEventType.KeyPressed, Locomotive.StartEngineBrakeIncrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlEngineBrakeDecrease, KeyEventType.KeyPressed, Locomotive.StartEngineBrakeDecrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlEngineBrakeIncrease, KeyEventType.KeyReleased, Locomotive.StopEngineBrakeIncrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlEngineBrakeDecrease, KeyEventType.KeyReleased, Locomotive.StopEngineBrakeDecrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlBrakemanBrakeIncrease, KeyEventType.KeyPressed, Locomotive.StartBrakemanBrakeIncrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlBrakemanBrakeDecrease, KeyEventType.KeyPressed, Locomotive.StartBrakemanBrakeDecrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlBrakemanBrakeIncrease, KeyEventType.KeyReleased, Locomotive.StopBrakemanBrakeIncrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlBrakemanBrakeDecrease, KeyEventType.KeyReleased, Locomotive.StopBrakemanBrakeDecrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlDynamicBrakeIncrease, KeyEventType.KeyPressed, Locomotive.StartDynamicBrakeIncrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlDynamicBrakeDecrease, KeyEventType.KeyPressed, Locomotive.StartDynamicBrakeDecrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlDynamicBrakeIncrease, KeyEventType.KeyReleased, Locomotive.StopDynamicBrakeIncrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlDynamicBrakeDecrease, KeyEventType.KeyReleased, Locomotive.StopDynamicBrakeDecrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlGearUp, KeyEventType.KeyPressed, StartGearBoxIncrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlGearDown, KeyEventType.KeyPressed, StartGearBoxDecrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlGearUp, KeyEventType.KeyReleased, StopGearBoxIncrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlGearDown, KeyEventType.KeyReleased, StopGearBoxDecrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlSteamHeatIncrease, KeyEventType.KeyPressed, Locomotive.StartSteamHeatIncrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlSteamHeatDecrease, KeyEventType.KeyPressed, Locomotive.StartSteamHeatDecrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlSteamHeatIncrease, KeyEventType.KeyReleased, Locomotive.StopSteamHeatIncrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlSteamHeatDecrease, KeyEventType.KeyReleased, Locomotive.StopSteamHeatDecrease);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlBailOff, KeyEventType.KeyPressed, BailOffOnCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlBailOff, KeyEventType.KeyReleased, BailOffOffCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlInitializeBrakes, KeyEventType.KeyPressed, InitializeBrakesCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlHandbrakeNone, KeyEventType.KeyPressed, HandbrakeNoneCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlHandbrakeFull, KeyEventType.KeyPressed, HandbrakeFullCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlRetainersOff, KeyEventType.KeyPressed, RetainersOffCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlRetainersOn, KeyEventType.KeyPressed, RetainersOnCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlBrakeHoseConnect, KeyEventType.KeyPressed, BrakeHoseConnectCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlBrakeHoseDisconnect, KeyEventType.KeyPressed, BrakeHoseDisconnectCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlEmergencyPushButton, KeyEventType.KeyPressed, EmergencyPushButtonCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlSander, KeyEventType.KeyPressed, SanderOnCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlSander, KeyEventType.KeyReleased, SanderOffCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlSanderToggle, KeyEventType.KeyPressed, SanderToogleCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlWiper, KeyEventType.KeyPressed, WiperCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlHorn, KeyEventType.KeyPressed, HornOnCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlHorn, KeyEventType.KeyReleased, HornOffCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlBell, KeyEventType.KeyPressed, BellOnCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlBell, KeyEventType.KeyReleased, BellOffCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlBellToggle, KeyEventType.KeyPressed, BellToggleCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlAlerter, KeyEventType.KeyPressed, AlerterOnCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlAlerter, KeyEventType.KeyReleased, AlerterOffCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlHeadlightIncrease, KeyEventType.KeyPressed, HeadlightIncreaseCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlHeadlightDecrease, KeyEventType.KeyPressed, HeadlightDecreaseCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlLight, KeyEventType.KeyPressed, ToggleCabLightCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlRefill, KeyEventType.KeyPressed, AttemptToRefillOrUnload);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlRefill, KeyEventType.KeyReleased, StopRefillingOrUnloading);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlImmediateRefill, KeyEventType.KeyPressed, ImmediateRefill);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlImmediateRefill, KeyEventType.KeyReleased, StopImmediateRefilling);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlWaterScoop, KeyEventType.KeyPressed, ToggleWaterScoopCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlOdoMeterShowHide, KeyEventType.KeyPressed, ResetOdometerCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlOdoMeterReset, KeyEventType.KeyPressed, ResetOdometerCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlOdoMeterDirection, KeyEventType.KeyPressed, ToggleOdometerDirectionCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlCabRadio, KeyEventType.KeyPressed, CabRadioCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlDieselHelper, KeyEventType.KeyPressed, ToggleHelpersEngineCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlGeneric1, KeyEventType.KeyPressed, GenericCommand1On);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlGeneric1, KeyEventType.KeyReleased, GenericCommand1Off);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlGeneric2, KeyEventType.KeyPressed, GenericCommand2On);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlGeneric2, KeyEventType.KeyReleased, GenericCommand2Off);
            Viewer.UserCommandController.RemoveEvent(AnalogUserCommand.Light, LightSwitchCommand);
            Viewer.UserCommandController.RemoveEvent(AnalogUserCommand.Wiper, WiperSwitchCommand);
            Viewer.UserCommandController.RemoveEvent(AnalogUserCommand.Direction, DirectionHandleCommand);
            Viewer.UserCommandController.RemoveEvent(AnalogUserCommand.Throttle, ThrottleHandleCommand);
            Viewer.UserCommandController.RemoveEvent(AnalogUserCommand.DynamicBrake, DynamicBrakeHandleCommand);
            Viewer.UserCommandController.RemoveEvent(AnalogUserCommand.TrainBrake, TrainBrakeHandleCommand);
            Viewer.UserCommandController.RemoveEvent(AnalogUserCommand.EngineBrake, EngineBrakeHandleCommand);
            Viewer.UserCommandController.RemoveEvent(AnalogUserCommand.BailOff, BailOffHandleCommand);
            Viewer.UserCommandController.RemoveEvent(AnalogUserCommand.Emergency, EmergencyHandleCommand);
            Viewer.UserCommandController.AddEvent(AnalogUserCommand.CabActivity, AlerterResetCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlBattery, KeyEventType.KeyPressed, ToggleBatteryCommand);
            Viewer.UserCommandController.RemoveEvent(UserCommand.ControlPowerKey, KeyEventType.KeyPressed, TogglePowerKeyCommand);

            base.UnregisterUserCommandHandling();
        }

        #region private event handlers
        // To be able to attach and detach from UserCommandController we need some actual instance
        // therefore can't use anonymous lambda
#pragma warning disable IDE0022 // Use block body for methods
        private void ShowCabCommand() => Locomotive.ShowCab = !Locomotive.ShowCab;
        private void DebugResetWheelSlipCommand() => Locomotive.Train.SignalEvent(TrainEvent.ResetWheelSlip);
        private void DebugToggleAdvancedAdhesionCommand()
        {
            Locomotive.Train.SignalEvent(TrainEvent.ResetWheelSlip);
            Locomotive.Simulator.UseAdvancedAdhesion = !Locomotive.Simulator.UseAdvancedAdhesion;
        }
        private void BailOffOnCommand() => _ = new BailOffCommand(Viewer.Log, true);
        private void BailOffOffCommand() => _ = new BailOffCommand(Viewer.Log, false);
        private void InitializeBrakesCommand() => _ = new InitializeBrakesCommand(Viewer.Log);
        private void HandbrakeNoneCommand() => _ = new HandbrakeCommand(Viewer.Log, false);
        private void HandbrakeFullCommand() => _ = new HandbrakeCommand(Viewer.Log, true);
        private void RetainersOffCommand() => _ = new RetainersCommand(Viewer.Log, false);
        private void RetainersOnCommand() => _ = new RetainersCommand(Viewer.Log, true);
        private void BrakeHoseConnectCommand() => _ = new BrakeHoseConnectCommand(Viewer.Log, true);
        private void BrakeHoseDisconnectCommand() => _ = new BrakeHoseConnectCommand(Viewer.Log, false);
        private void EmergencyPushButtonCommand() => _ = new EmergencyPushButtonCommand(Viewer.Log);
        private void SanderOnCommand() => _ = new SanderCommand(Viewer.Log, true);
        private void SanderOffCommand() => _ = new SanderCommand(Viewer.Log, false);
        private void SanderToogleCommand() => _ = new SanderCommand(Viewer.Log, !Locomotive.Sander);
        private void WiperCommand() => _ = new WipersCommand(Viewer.Log, !Locomotive.Wiper);
        private void HornOnCommand() => _ = new HornCommand(Viewer.Log, true);
        private void HornOffCommand() => _ = new HornCommand(Viewer.Log, false);
        private void BellOnCommand() => _ = new BellCommand(Viewer.Log, true);
        private void BellOffCommand() => _ = new BellCommand(Viewer.Log, false);
        private void BellToggleCommand() => _ = new BellCommand(Viewer.Log, !Locomotive.Bell);
        private void AlerterOnCommand() => _ = new AlerterCommand(Viewer.Log, true);
        private void AlerterOffCommand() => _ = new AlerterCommand(Viewer.Log, false);
        private void HeadlightIncreaseCommand() => _ = new HeadlightCommand(Viewer.Log, true);
        private void HeadlightDecreaseCommand() => _ = new HeadlightCommand(Viewer.Log, false);
        private void ToggleCabLightCommand() => _ = new ToggleCabLightCommand(Viewer.Log);
        private void ToggleWaterScoopCommand() => _ = new ToggleWaterScoopCommand(Viewer.Log);
        private void ToggleOdometerCommand() => _ = new ToggleOdometerCommand(Viewer.Log);
        private void ResetOdometerCommand() => _ = new ResetOdometerCommand(Viewer.Log);
        private void ToggleOdometerDirectionCommand() => _ = new ToggleOdometerDirectionCommand(Viewer.Log);
        private void CabRadioCommand() => _ = new CabRadioCommand(Viewer.Log, !Locomotive.CabRadioOn);
        private void ToggleHelpersEngineCommand() => _ = new ToggleHelpersEngineCommand(Viewer.Log);
        private void GenericCommand1On()
        {
            _ = new TCSButtonCommand(Viewer.Log, true, 0);
            _ = new TCSSwitchCommand(Viewer.Log, !Locomotive.TrainControlSystem.TCSCommandSwitchOn[0], 0);
        }
        private void GenericCommand1Off()
        {
            _ = new TCSButtonCommand(Viewer.Log, false, 0);
        }
        private void GenericCommand2On()
        {
            _ = new TCSButtonCommand(Viewer.Log, true, 1);
            _ = new TCSSwitchCommand(Viewer.Log, !Locomotive.TrainControlSystem.TCSCommandSwitchOn[1], 1);
        }
        private void GenericCommand2Off()
        {
            _ = new TCSButtonCommand(Viewer.Log, false, 1);
        }

        private void LightSwitchCommand(UserCommandArgs commandArgs, GameTime gameTime)
        {
            if (commandArgs is UserCommandArgs<int> switchCommandArgs)
            {
                // changing Headlight more than one step at a time doesn't work for some reason
                if (Locomotive.Headlight < switchCommandArgs.Value - 1)
                {
                    Locomotive.Headlight++;
                    Locomotive.SignalEvent(TrainEvent.LightSwitchToggle);
                }
                if (Locomotive.Headlight > switchCommandArgs.Value - 1)
                {
                    Locomotive.Headlight--;
                    Locomotive.SignalEvent(TrainEvent.LightSwitchToggle);
                }
            }
        }

        private void WiperSwitchCommand(UserCommandArgs commandArgs, GameTime gameTime)
        {
            if (commandArgs is UserCommandArgs<int> switchCommandArgs)
            {
                if (switchCommandArgs.Value == 1 && Locomotive.Wiper)
                    Locomotive.SignalEvent(TrainEvent.WiperOff);
                else if (switchCommandArgs.Value != 1 && !Locomotive.Wiper)
                    Locomotive.SignalEvent(TrainEvent.WiperOn);
            }
        }

        private protected virtual void DirectionHandleCommand(UserCommandArgs commandArgs, GameTime gameTime)
        {
            if (commandArgs is UserCommandArgs<float> handleCommandArgs)
            {
                if (handleCommandArgs.Value > 50)
                    Locomotive.SetDirection(MidpointDirection.Forward);
                else if (handleCommandArgs.Value < -50)
                    Locomotive.SetDirection(MidpointDirection.Reverse);
                else
                    Locomotive.SetDirection(MidpointDirection.N);
            }
        }

        private void ThrottleHandleCommand(UserCommandArgs commandArgs, GameTime gameTime)
        {
            if (commandArgs is UserCommandArgs<float> handleCommandArgs)
            {
                Locomotive.SetThrottlePercentWithSound(handleCommandArgs.Value);
            }
        }

        private void DynamicBrakeHandleCommand(UserCommandArgs commandArgs, GameTime gameTime)
        {
            if (commandArgs is UserCommandArgs<float> handleCommandArgs)
            {
                if (Locomotive.CombinedControlType != MSTSLocomotive.CombinedControl.ThrottleAir)
                    Locomotive.SetDynamicBrakePercentWithSound(handleCommandArgs.Value);
            }
        }

        private void TrainBrakeHandleCommand(UserCommandArgs commandArgs, GameTime gameTime)
        {
            if (commandArgs is UserCommandArgs<float> handleCommandArgs)
            {
                Locomotive.SetTrainBrakePercent(handleCommandArgs.Value);
            }
        }

        private void EngineBrakeHandleCommand(UserCommandArgs commandArgs, GameTime gameTime)
        {
            if (commandArgs is UserCommandArgs<float> handleCommandArgs)
            {
                Locomotive.SetEngineBrakePercent(handleCommandArgs.Value);
            }
        }

        private void BailOffHandleCommand(UserCommandArgs commandArgs, GameTime gameTime)
        {
            if (commandArgs is UserCommandArgs<bool> handleCommandArgs)
            {
                Locomotive.SetBailOff(handleCommandArgs.Value);
            }
        }

        private void EmergencyHandleCommand(UserCommandArgs commandArgs, GameTime gameTime)
        {
            if (commandArgs is UserCommandArgs<bool> handleCommandArgs)
            {
                Locomotive.SetEmergency(handleCommandArgs.Value);
            }
        }

        private void AlerterResetCommand(UserCommandArgs commandArgs, GameTime gameTime)
        {
            Locomotive.AlerterReset();
        }

        private void ToggleBatteryCommand() => _ = new ToggleBatteryCommand(Viewer.Log);
        private void TogglePowerKeyCommand() => _ = new TogglePowerKeyCommand(Viewer.Log);

#pragma warning restore IDE0022 // Use block body for methods
        #endregion

        /// <summary>
        /// We are about to display a video frame.  Calculate positions for 
        /// animated objects, and add their primitives to the RenderFrame list.
        /// </summary>
        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            if (Viewer.Camera.AttachedCar == this.MSTSWagon && Viewer.Camera.Style == Camera.Styles.ThreeDimCab)
            {
                if (CabViewer3D != null)
                    CabViewer3D.PrepareFrame(frame, elapsedTime);
            }

            // Wipers and bell animation
            Wipers.UpdateLoop(Locomotive.Wiper, elapsedTime);
            Bell.UpdateLoop(Locomotive.Bell, elapsedTime, TrainCarShape.SharedShape.BellAnimationFPS);

            // Draw 2D CAB View - by GeorgeS
            if (Viewer.Camera.AttachedCar == this.MSTSWagon &&
                Viewer.Camera.Style == Camera.Styles.Cab)
            {
                if (CabRenderer != null)
                    CabRenderer.PrepareFrame(frame, elapsedTime);
            }

            base.PrepareFrame(frame, elapsedTime);
        }

        internal override void LoadForPlayer()
        {
            if (!HasCabRenderer)
            {
                if (Locomotive.CabViewList.Count > 0)
                {
                    if (Locomotive.CabViewList[(int)CabViewType.Front].CVFFile != null && Locomotive.CabViewList[(int)CabViewType.Front].CVFFile.Views2D.Count > 0)
                        CabRenderer = new CabRenderer(Viewer, Locomotive);
                    HasCabRenderer = true;
                }
            }
            if (!Has3DCabRenderer)
            {
                if (Locomotive.CabViewpoints != null)
                {
                    ThreeDimentionCabViewer tmp3DViewer = null;
                    try
                    {
                        tmp3DViewer = new ThreeDimentionCabViewer(Viewer, this.Locomotive, this); //this constructor may throw an error
                        CabViewer3D = tmp3DViewer; //if not catching an error, we will assign it
                        Has3DCabRenderer = true;
                    }
                    catch (Exception error)
                    {
                        Trace.WriteLine(new Exception("Could not load 3D cab.", error));
                    }
                }
            }
        }

        internal override void Mark()
        {
            foreach (var pdl in ParticleDrawers.Values)
                foreach (var pd in pdl)
                    pd.Mark();
            if (CabRenderer != null)
                CabRenderer.Mark();
            base.Mark();
        }

        /// <summary>
        /// Release sounds of TCS if any, but not for player locomotive
        /// </summary>
        public override void Unload()
        {
            if (Locomotive.TrainControlSystem != null && Locomotive.TrainControlSystem.Sounds.Count > 0)
                foreach (var script in Locomotive.TrainControlSystem.Sounds.Keys)
                {
                    Viewer.SoundProcess.RemoveSoundSources(script);
                }
            base.Unload();
        }

        /// <summary>
        /// Finds the pickup point which is closest to the loco or tender that uses coal, water or diesel oil.
        /// Uses that pickup to refill the loco or tender.
        /// Not implemented yet:
        /// 1. allowing for the position of the intake on the wagon/loco.
        /// 2. allowing for the rate at with the pickup can supply.
        /// 3. refilling any but the first loco in the player's train.
        /// 4. refilling AI trains.
        /// 5. animation is in place, but the animated object should be able to swing into place first, then the refueling process begins.
        /// 6. currently ignores locos and tenders without intake points.
        /// The note below may not be accurate since I released a fix that allows the setting of both coal and water level for the tender to be set at start of activity(EK).
        /// Note that the activity class currently parses the initial level of diesel oil, coal and water
        /// but does not use it yet.
        /// Note: With the introduction of the  animated object, I implemented the RefillProcess class as a starter to allow outside classes to use, but
        /// to solve #5 above, its probably best that the processes below be combined in a common class so that both Shapes.cs and FuelPickup.cs can properly keep up with events(EK).
        /// </summary>
        #region Refill loco or tender from pickup points

        private WagonAndMatchingPickup MatchedWagonAndPickup;


        /// <summary>
        /// Holds data for an intake point on a wagon (e.g. tender) or loco and a pickup point which can supply that intake. 
        /// </summary>
        public class WagonAndMatchingPickup
        {
            public PickupObject Pickup;
            public MSTSWagon Wagon;
            public MSTSLocomotive SteamLocomotiveWithTender;
            public IntakePoint IntakePoint;

        }

        /// <summary>
        /// Scans the train's cars for intake points and the world files for pickup refilling points of the same type.
        /// (e.g. "fuelwater").
        /// TODO: Allow for position of intake point within the car. Currently all intake points are assumed to be at the back of the car.
        /// </summary>
        /// <param name="train"></param>
        /// <returns>a combination of intake point and pickup that are closest</returns>
        // <CJComment> Might be better in the MSTSLocomotive class, but can't see the World objects from there. </CJComment>
        private WagonAndMatchingPickup GetMatchingPickup(Train train)
        {
            var worldFiles = Viewer.World.Scenery.WorldFiles;
            var shortestD2 = float.MaxValue;
            WagonAndMatchingPickup nearestPickup = null;
            float distanceFromFrontOfTrainM = 0f;
            int index = 0;
            foreach (var car in train.Cars)
            {
                if (car is MSTSWagon)
                {
                    MSTSWagon wagon = (MSTSWagon)car;
                    foreach (var intake in wagon.IntakePointList)
                    {
                        // TODO Use the value calculated below
                        //if (intake.DistanceFromFrontOfTrainM == null)
                        //{
                        //    intake.DistanceFromFrontOfTrainM = distanceFromFrontOfTrainM + (wagon.LengthM / 2) - intake.OffsetM;
                        //}
                        foreach (var worldFile in worldFiles)
                        {
                            foreach (var pickup in worldFile.PickupList)
                            {
                                if ((wagon.FreightAnimations != null && (wagon.FreightAnimations.FreightType == pickup.PickupType ||
                                    wagon.FreightAnimations.FreightType == PickupType.None) &&
                                    intake.Type == pickup.PickupType)
                                 || (intake.Type == pickup.PickupType && intake.Type > PickupType.FreightSand && (wagon.WagonType == TrainCar.WagonTypes.Tender || wagon is MSTSLocomotive)))
                                {
                                    VectorExtension.Transform(new Vector3(0, 0, -intake.OffsetM), car.WorldPosition.XNAMatrix, out Vector3 intakePosition);

                                    var intakeLocation = new WorldLocation(
                                        car.WorldPosition.TileX, car.WorldPosition.TileZ,
                                        intakePosition.X, intakePosition.Y, -intakePosition.Z);

                                    var d2 = (float)WorldLocation.GetDistanceSquared(intakeLocation, pickup.WorldPosition.WorldLocation);
                                    if (d2 < shortestD2)
                                    {
                                        shortestD2 = d2;
                                        nearestPickup = new WagonAndMatchingPickup();
                                        nearestPickup.Pickup = pickup;
                                        nearestPickup.Wagon = wagon;
                                        if (wagon.WagonType == TrainCar.WagonTypes.Tender)
                                        {
                                            // Normal arrangement would be steam locomotive followed by the tender car.
                                            if (index > 0 && train.Cars[index - 1] is MSTSSteamLocomotive && !wagon.Flipped && !train.Cars[index - 1].Flipped)
                                                nearestPickup.SteamLocomotiveWithTender = train.Cars[index - 1] as MSTSLocomotive;
                                            // but after reversal point or turntable reversal order of cars is reversed too!
                                            else if (index < train.Cars.Count - 1 && train.Cars[index + 1] is MSTSSteamLocomotive && wagon.Flipped && train.Cars[index + 1].Flipped)
                                                nearestPickup.SteamLocomotiveWithTender = train.Cars[index + 1] as MSTSLocomotive;
                                            else if (index > 0 && train.Cars[index - 1] is MSTSSteamLocomotive)
                                                nearestPickup.SteamLocomotiveWithTender = train.Cars[index - 1] as MSTSLocomotive;
                                            else if (index < train.Cars.Count - 1 && train.Cars[index + 1] is MSTSSteamLocomotive)
                                                nearestPickup.SteamLocomotiveWithTender = train.Cars[index + 1] as MSTSLocomotive;
                                        }
                                        nearestPickup.IntakePoint = intake;
                                    }
                                }
                            }
                        }
                    }
                    distanceFromFrontOfTrainM += wagon.CarLengthM;
                }
                index++;
            }
            return nearestPickup;
        }

        /// <summary>
        /// Returns 
        /// TODO Allow for position of intake point within the car. Currently all intake points are assumed to be at the back of the car.
        /// </summary>
        /// <param name="match"></param>
        /// <returns></returns>
        private float GetDistanceToM(WagonAndMatchingPickup match)
        {
            VectorExtension.Transform(new Vector3(0, 0, -match.IntakePoint.OffsetM), match.Wagon.WorldPosition.XNAMatrix, out Vector3 intakePosition);

            var intakeLocation = new WorldLocation(
                match.Wagon.WorldPosition.TileX, match.Wagon.WorldPosition.TileZ,
                intakePosition.X, intakePosition.Y, -intakePosition.Z);

            return (float)Math.Sqrt(WorldLocation.GetDistanceSquared(intakeLocation, match.Pickup.WorldPosition.WorldLocation));
        }

        /// <summary>
        // This process is tied to the Shift T key combination
        // The purpose of is to perform immediate refueling without having to pull up alongside the fueling station.
        /// </summary>
        public void ImmediateRefill()
        {
            var loco = this.Locomotive;

            if (loco == null)
                return;

            foreach (var car in loco.Train.Cars)
            {
                // There is no need to check for the tender.  The MSTSSteamLocomotive is the primary key in the refueling process when using immediate refueling.
                // Electric locomotives may have steam heat boilers fitted, and they can refill these
                if (car is MSTSDieselLocomotive || car is MSTSSteamLocomotive || (car is MSTSElectricLocomotive && loco.IsSteamHeatFitted))
                {
                    Viewer.Simulator.Confirmer.Message(ConfirmLevel.None, Viewer.Catalog.GetString("Refill: Immediate refill process selected, refilling immediately."));
                    (car as MSTSLocomotive).RefillImmediately();
                }
            }
        }

        /// <summary>
        /// Prompts if cannot refill yet, else starts continuous refilling.
        /// Tries to find the nearest supply (pickup point) which can refill the locos and tenders in the train.  
        /// </summary>
        public void AttemptToRefillOrUnload()
        {
            MatchedWagonAndPickup = null;   // Ensures that releasing the T key doesn't do anything unless there is something to do.

            var loco = this.Locomotive;

            var match = GetMatchingPickup(loco.Train);
            if (match == null && !(loco is MSTSElectricLocomotive && loco.IsSteamHeatFitted))
                return;
            if (match == null)
            {
                Viewer.Simulator.Confirmer.Message(ConfirmLevel.None, Viewer.Catalog.GetString("Refill: Electric loco and no pickup. Command rejected"));
                return;
            }

            float distanceToPickupM = GetDistanceToM(match) - 2.5f; // Deduct an extra 2.5 so that the tedious placement is less of an issue.
            if (distanceToPickupM > match.IntakePoint.WidthM / 2)
            {
                Viewer.Simulator.Confirmer.Message(ConfirmLevel.None, Viewer.Catalog.GetString("Refill: Distance to {0} supply is {1}.",
                    match.Pickup.PickupType.GetLocalizedDescription(), Viewer.Catalog.GetPluralString("{0} meter", "{0} meters", (long)(distanceToPickupM + 1f))));
                return;
            }
            if (distanceToPickupM <= match.IntakePoint.WidthM / 2)
                MSTSWagon.RefillProcess.ActivePickupObjectUID = (int)match.Pickup.UiD;
            if (loco.SpeedMpS != 0 && match.Pickup.SpeedRange.UpperLimit == 0f)
            {
                Viewer.Simulator.Confirmer.Message(ConfirmLevel.None, Viewer.Catalog.GetString("Refill: Loco must be stationary to refill {0}.",
                    match.Pickup.PickupType.GetLocalizedDescription()));
                return;
            }
            if (loco.SpeedMpS < match.Pickup.SpeedRange.LowerLimit)
            {
                Viewer.Simulator.Confirmer.Message(ConfirmLevel.None, Viewer.Catalog.GetString("Refill: Loco speed must exceed {0}.",
                    FormatStrings.FormatSpeedLimit(match.Pickup.SpeedRange.LowerLimit, Viewer.MilepostUnitsMetric)));
                return;
            }
            if (loco.SpeedMpS > match.Pickup.SpeedRange.UpperLimit)
            {
                var speedLimitMpH = Speed.MeterPerSecond.ToMpH(match.Pickup.SpeedRange.UpperLimit);
                Viewer.Simulator.Confirmer.Message(ConfirmLevel.None, Viewer.Catalog.GetString("Refill: Loco speed must not exceed {0}.",
                    FormatStrings.FormatSpeedLimit(match.Pickup.SpeedRange.UpperLimit, Viewer.MilepostUnitsMetric)));
                return;
            }
            if (match.Wagon is MSTSDieselLocomotive || match.Wagon is MSTSSteamLocomotive || match.Wagon is MSTSElectricLocomotive || (match.Wagon.WagonType == TrainCar.WagonTypes.Tender && match.SteamLocomotiveWithTender != null))
            {
                // Note: The tender contains the intake information, but the steam locomotive includes the controller information that is needed for the refueling process.

                float fraction = 0;

                // classical MSTS Freightanim, handled as usual
                if (match.SteamLocomotiveWithTender != null)
                    fraction = match.SteamLocomotiveWithTender.GetFilledFraction(match.Pickup.PickupType);
                else
                    fraction = match.Wagon.GetFilledFraction(match.Pickup.PickupType);

                if (fraction > 0.99)
                {
                    Viewer.Simulator.Confirmer.Message(ConfirmLevel.None, Viewer.Catalog.GetString("Refill: {0} supply now replenished.",
                        match.Pickup.PickupType.GetLocalizedDescription()));
                    return;
                }
                else
                {
                    MSTSWagon.RefillProcess.OkToRefill = true;
                    if (match.SteamLocomotiveWithTender != null)
                        StartRefilling(match.Pickup, fraction, match.SteamLocomotiveWithTender);
                    else
                        StartRefilling(match.Pickup, fraction, match.Wagon);

                    MatchedWagonAndPickup = match;  // Save away for HandleUserInput() to use when key is released.
                }
            }
            else if (match.Wagon.FreightAnimations != null)
            {
                // freight wagon animation
                var fraction = match.Wagon.GetFilledFraction(match.Pickup.PickupType);
                if (fraction > 0.99 && match.Pickup.Capacity.FeedRateKGpS >= 0)
                {
                    Viewer.Simulator.Confirmer.Message(ConfirmLevel.None, Viewer.Catalog.GetString("Refill: {0} supply now replenished.",
                        match.Pickup.PickupType.GetLocalizedDescription()));
                    return;
                }
                else if (fraction < 0.01 && match.Pickup.Capacity.FeedRateKGpS < 0)
                {
                    Viewer.Simulator.Confirmer.Message(ConfirmLevel.None, Viewer.Catalog.GetString("Unload: {0} fuel or freight now unloaded.",
                        match.Pickup.PickupType.GetLocalizedDescription()));
                    return;
                }
                else
                {
                    MSTSWagon.RefillProcess.OkToRefill = true;
                    MSTSWagon.RefillProcess.Unload = match.Pickup.Capacity.FeedRateKGpS < 0;
                    match.Wagon.StartRefillingOrUnloading(match.Pickup, match.IntakePoint, fraction, MSTSWagon.RefillProcess.Unload);
                    MatchedWagonAndPickup = match;  // Save away for HandleUserInput() to use when key is released.
                }
            }
        }

        /// <summary>
        /// Called by RefillCommand during replay.
        /// </summary>
        public void RefillChangeTo(float? target)
        {
            MSTSNotchController controller = new MSTSNotchController();
            var loco = this.Locomotive;

            var matchedWagonAndPickup = GetMatchingPickup(loco.Train);   // Save away for RefillCommand to use.
            if (matchedWagonAndPickup != null)
            {
                if (matchedWagonAndPickup.SteamLocomotiveWithTender != null)
                    controller = matchedWagonAndPickup.SteamLocomotiveWithTender.GetRefillController(matchedWagonAndPickup.Pickup.PickupType);
                else
                    controller = (matchedWagonAndPickup.Wagon as MSTSLocomotive).GetRefillController(matchedWagonAndPickup.Pickup.PickupType);
                controller.StartIncrease(target);
            }
        }

        /// <summary>
        /// Starts a continuous increase in controlled value. This method also receives TrainCar car to process individual locomotives for refueling.
        /// </summary>
        /// <param name="type">Pickup point</param>
        public void StartRefilling(PickupObject matchPickup, float fraction, TrainCar car)
        {
            var controller = (car as MSTSLocomotive).GetRefillController(matchPickup.PickupType);

            if (controller == null)
            {
                Viewer.Simulator.Confirmer.Message(ConfirmLevel.Error, Viewer.Catalog.GetString("Incompatible pickup type"));
                return;
            }
            (car as MSTSLocomotive).SetStepSize(matchPickup);
            controller.SetValue(fraction);
            controller.CommandStartTime = Viewer.Simulator.ClockTime;  // for Replay to use 
            Viewer.Simulator.Confirmer.Message(ConfirmLevel.Information, Viewer.Catalog.GetString("Starting refill"));
            controller.StartIncrease(controller.MaximumValue);
        }

        /// <summary>
        /// Starts a continuous increase in controlled value.
        /// </summary>
        /// <param name="type">Pickup point</param>
        public void StartRefilling(PickupType type, float fraction)
        {
            var controller = Locomotive.GetRefillController(type);
            if (controller == null)
            {
                Viewer.Simulator.Confirmer.Message(ConfirmLevel.Error, Viewer.Catalog.GetString("Incompatible pickup type"));
                return;
            }
            controller.SetValue(fraction);
            controller.CommandStartTime = Viewer.Simulator.ClockTime;  // for Replay to use 
            Viewer.Simulator.Confirmer.Message(ConfirmLevel.Information, Viewer.Catalog.GetString("Starting refill"));
            controller.StartIncrease(controller.MaximumValue);
        }

        /// <summary>
        // Immediate refueling process is different from the process of refueling individual locomotives.
        /// </summary> 
        public void StopImmediateRefilling()
        {
            new ImmediateRefillCommand(Viewer.Log);  // for Replay to use
        }

        /// <summary>
        /// Ends a continuous increase in controlled value.
        /// </summary>
        public void StopRefillingOrUnloading()
        {
            if (MatchedWagonAndPickup == null)
                return;
            MSTSWagon.RefillProcess.OkToRefill = false;
            MSTSWagon.RefillProcess.ActivePickupObjectUID = 0;
            var match = MatchedWagonAndPickup;
            var controller = new MSTSNotchController();
            if (match.Wagon is MSTSElectricLocomotive || match.Wagon is MSTSDieselLocomotive || match.Wagon is MSTSSteamLocomotive || (match.Wagon.WagonType == TrainCar.WagonTypes.Tender && match.SteamLocomotiveWithTender != null))
            {
                if (match.SteamLocomotiveWithTender != null)
                    controller = match.SteamLocomotiveWithTender.GetRefillController(MatchedWagonAndPickup.Pickup.PickupType);
                else
                    controller = (match.Wagon as MSTSLocomotive).GetRefillController(MatchedWagonAndPickup.Pickup.PickupType);
            }
            else
            {
                controller = match.Wagon.WeightLoadController;
                match.Wagon.UnloadingPartsOpen = false;
            }

            new RefillCommand(Viewer.Log, controller.CurrentValue, controller.CommandStartTime);  // for Replay to use
            if (controller.UpdateValue >= 0)
                controller.StopIncrease();
            else controller.StopDecrease();
        }
        #endregion
    } // Class LocomotiveViewer

    // By GeorgeS
    /// <summary>
    /// Manages all CAB View textures - light conditions and texture parts
    /// </summary>
    public static class CABTextureManager
    {
        private static Dictionary<string, Texture2D> DayTextures = new Dictionary<string, Texture2D>();
        private static Dictionary<string, Texture2D> NightTextures = new Dictionary<string, Texture2D>();
        private static Dictionary<string, Texture2D> LightTextures = new Dictionary<string, Texture2D>();
        private static Dictionary<string, Texture2D[]> PDayTextures = new Dictionary<string, Texture2D[]>();
        private static Dictionary<string, Texture2D[]> PNightTextures = new Dictionary<string, Texture2D[]>();
        private static Dictionary<string, Texture2D[]> PLightTextures = new Dictionary<string, Texture2D[]>();

        /// <summary>
        /// Loads a texture, day night and cablight
        /// </summary>
        /// <param name="viewer">Viver3D</param>
        /// <param name="FileName">Name of the Texture</param>
        public static bool LoadTextures(Viewer viewer, string FileName)
        {
            if (string.IsNullOrEmpty(FileName))
                return false;

            if (DayTextures.Keys.Contains(FileName))
                return false;

            DayTextures.Add(FileName, viewer.TextureManager.Get(FileName, true));

            var nightpath = Path.Combine(Path.Combine(Path.GetDirectoryName(FileName), "night"), Path.GetFileName(FileName));
            NightTextures.Add(FileName, viewer.TextureManager.Get(nightpath));

            var lightdirectory = Path.Combine(Path.GetDirectoryName(FileName), "cablight");
            var lightpath = Path.Combine(lightdirectory, Path.GetFileName(FileName));
            var lightTexture = viewer.TextureManager.Get(lightpath);
            LightTextures.Add(FileName, lightTexture);
            return Directory.Exists(lightdirectory);
        }

        private static Texture2D[] Disassemble(GraphicsDevice graphicsDevice, Texture2D texture, int frameCount, Point frameGrid, string fileName)
        {
            if (frameGrid.X < 1 || frameGrid.Y < 1 || frameCount < 1)
            {
                Trace.TraceWarning("Cab control has invalid frame data {1}*{2}={3} (no frames will be shown) for {0}", fileName, frameGrid.X, frameGrid.Y, frameCount);
                return new Texture2D[0];
            }

            var frameSize = new Point(texture.Width / frameGrid.X, texture.Height / frameGrid.Y);
            var frames = new Texture2D[frameCount];
            var frameIndex = 0;

            if (frameCount > frameGrid.X * frameGrid.Y)
                Trace.TraceWarning("Cab control frame count {1} is larger than the number of frames {2}*{3}={4} (some frames will be blank) for {0}", fileName, frameCount, frameGrid.X, frameGrid.Y, frameGrid.X * frameGrid.Y);

            if (texture.Format != SurfaceFormat.Color && texture.Format != SurfaceFormat.Dxt1)
            {
                Trace.TraceWarning("Cab control texture {0} has unsupported format {1}; only Color and Dxt1 are supported.", fileName, texture.Format);
            }
            else
            {
                var copySize = new Point(frameSize.X, frameSize.Y);
                Point controlSize;
                if (texture.Format == SurfaceFormat.Dxt1)
                {
                    controlSize.X = (int)Math.Ceiling((float)copySize.X / 4) * 4;
                    controlSize.Y = (int)Math.Ceiling((float)copySize.Y / 4) * 4;
                    var buffer = new byte[(int)Math.Ceiling((float)copySize.X / 4) * 4 * (int)Math.Ceiling((float)copySize.Y / 4) * 4 / 2];
                    frameIndex = DisassembleFrames(graphicsDevice, texture, frameCount, frameGrid, frames, frameSize, copySize, controlSize, buffer);
                }
                else
                {
                    var buffer = new Color[copySize.X * copySize.Y];
                    frameIndex = DisassembleFrames(graphicsDevice, texture, frameCount, frameGrid, frames, frameSize, copySize, copySize, buffer);
                }
            }

            while (frameIndex < frameCount)
                frames[frameIndex++] = SharedMaterialManager.MissingTexture;

            return frames;
        }

        private static int DisassembleFrames<T>(GraphicsDevice graphicsDevice, Texture2D texture, int frameCount, Point frameGrid, Texture2D[] frames, Point frameSize, Point copySize, Point controlSize, T[] buffer) where T : struct
        {
            //Trace.TraceInformation("Disassembling {0} {1} frames in {2}x{3}; control {4}x{5}, frame {6}x{7}, copy {8}x{9}.", texture.Format, frameCount, frameGrid.X, frameGrid.Y, controlSize.X, controlSize.Y, frameSize.X, frameSize.Y, copySize.X, copySize.Y);
            var frameIndex = 0;
            for (var y = 0; y < frameGrid.Y; y++)
            {
                for (var x = 0; x < frameGrid.X; x++)
                {
                    if (frameIndex < frameCount)
                    {
                        texture.GetData(0, new Rectangle(x * frameSize.X, y * frameSize.Y, copySize.X, copySize.Y), buffer, 0, buffer.Length);
                        var frame = frames[frameIndex++] = new Texture2D(graphicsDevice, controlSize.X, controlSize.Y, false, texture.Format);
                        frame.SetData(0, new Rectangle(0, 0, copySize.X, copySize.Y), buffer, 0, buffer.Length);
                    }
                }
            }
            return frameIndex;
        }

        /// <summary>
        /// Disassembles all compound textures into parts
        /// </summary>
        /// <param name="graphicsDevice">The GraphicsDevice</param>
        /// <param name="fileName">Name of the Texture to be disassembled</param>
        /// <param name="width">Width of the Cab View Control</param>
        /// <param name="height">Height of the Cab View Control</param>
        /// <param name="frameCount">Number of frames</param>
        /// <param name="framesX">Number of frames in the X dimension</param>
        /// <param name="framesY">Number of frames in the Y direction</param>
        public static void DisassembleTexture(GraphicsDevice graphicsDevice, string fileName, int width, int height, int frameCount, int framesX, int framesY)
        {
            var frameGrid = new Point(framesX, framesY);

            PDayTextures[fileName] = null;
            if (DayTextures.ContainsKey(fileName))
            {
                var texture = DayTextures[fileName];
                if (texture != SharedMaterialManager.MissingTexture)
                {
                    PDayTextures[fileName] = Disassemble(graphicsDevice, texture, frameCount, frameGrid, fileName + ":day");
                }
            }

            PNightTextures[fileName] = null;
            if (NightTextures.ContainsKey(fileName))
            {
                var texture = NightTextures[fileName];
                if (texture != SharedMaterialManager.MissingTexture)
                {
                    PNightTextures[fileName] = Disassemble(graphicsDevice, texture, frameCount, frameGrid, fileName + ":night");
                }
            }

            PLightTextures[fileName] = null;
            if (LightTextures.ContainsKey(fileName))
            {
                var texture = LightTextures[fileName];
                if (texture != SharedMaterialManager.MissingTexture)
                {
                    PLightTextures[fileName] = Disassemble(graphicsDevice, texture, frameCount, frameGrid, fileName + ":light");
                }
            }
        }

        /// <summary>
        /// Gets a Texture from the given array
        /// </summary>
        /// <param name="arr">Texture array</param>
        /// <param name="indx">Index</param>
        /// <param name="FileName">Name of the file to report</param>
        /// <returns>The given Texture</returns>
        private static Texture2D SafeGetAt(Texture2D[] arr, int indx, string FileName)
        {
            if (arr == null)
            {
                Trace.TraceWarning("Passed null Texture[] for accessing {0}", FileName);
                return SharedMaterialManager.MissingTexture;
            }

            if (arr.Length < 1)
            {
                Trace.TraceWarning("Disassembled texture invalid for {0}", FileName);
                return SharedMaterialManager.MissingTexture;
            }

            indx = (int)MathHelper.Clamp(indx, 0, arr.Length - 1);

            try
            {
                return arr[indx];
            }
            catch (IndexOutOfRangeException)
            {
                Trace.TraceWarning("Index {1} out of range for array length {2} while accessing texture for {0}", FileName, indx, arr.Length);
                return SharedMaterialManager.MissingTexture;
            }
        }

        /// <summary>
        /// Returns the compound part of a Texture previously disassembled
        /// </summary>
        /// <param name="FileName">Name of the disassembled Texture</param>
        /// <param name="indx">Index of the part</param>
        /// <param name="isDark">Is dark out there?</param>
        /// <param name="isLight">Is Cab Light on?</param>
        /// <param name="isNightTexture"></param>
        /// <returns>The Texture represented by its index</returns>
        public static Texture2D GetTextureByIndexes(string FileName, int indx, bool isDark, bool isLight, out bool isNightTexture, bool hasCabLightDirectory)
        {
            Texture2D retval = SharedMaterialManager.MissingTexture;
            Texture2D[] tmp = null;

            isNightTexture = false;

            if (string.IsNullOrEmpty(FileName) || !PDayTextures.Keys.Contains(FileName))
                return SharedMaterialManager.MissingTexture;

            if (isLight)
            {
                // Light on: use light texture when dark, if available; else select accordingly presence of CabLight directory.
                if (isDark)
                {
                    tmp = PLightTextures[FileName];
                    if (tmp == null)
                    {
                        if (hasCabLightDirectory)
                            tmp = PNightTextures[FileName];
                        else
                            tmp = PDayTextures[FileName];
                    }
                }
                // Both light and day textures should be used as-is in this situation.
                isNightTexture = true;
            }
            else if (isDark)
            {
                // Darkness: use night texture, if available.
                tmp = PNightTextures[FileName];
                // Only use night texture as-is in this situation.
                isNightTexture = tmp != null;
            }

            // No light or dark texture selected/available? Use day texture instead.
            if (tmp == null)
                tmp = PDayTextures[FileName];

            if (tmp != null)
                retval = SafeGetAt(tmp, indx, FileName);

            return retval;
        }

        /// <summary>
        /// Returns a Texture by its name
        /// </summary>
        /// <param name="FileName">Name of the Texture</param>
        /// <param name="isDark">Is dark out there?</param>
        /// <param name="isLight">Is Cab Light on?</param>
        /// <param name="isNightTexture"></param>
        /// <returns>The Texture</returns>
        public static Texture2D GetTexture(string FileName, bool isDark, bool isLight, out bool isNightTexture, bool hasCabLightDirectory)
        {
            Texture2D retval = SharedMaterialManager.MissingTexture;
            isNightTexture = false;

            if (string.IsNullOrEmpty(FileName) || !DayTextures.Keys.Contains(FileName))
                return retval;

            if (isLight)
            {
                // Light on: use light texture when dark, if available; else check if CabLightDirectory available to decide.
                if (isDark)
                {
                    retval = LightTextures[FileName];
                    if (retval == SharedMaterialManager.MissingTexture)
                        retval = hasCabLightDirectory ? NightTextures[FileName] : DayTextures[FileName];
                }

                // Both light and day textures should be used as-is in this situation.
                isNightTexture = true;
            }
            else if (isDark)
            {
                // Darkness: use night texture, if available.
                retval = NightTextures[FileName];
                // Only use night texture as-is in this situation.
                isNightTexture = retval != SharedMaterialManager.MissingTexture;
            }

            // No light or dark texture selected/available? Use day texture instead.
            if (retval == SharedMaterialManager.MissingTexture)
                retval = DayTextures[FileName];

            return retval;
        }

        public static void Mark(Viewer viewer)
        {
            foreach (var texture in DayTextures.Values)
                viewer.TextureManager.Mark(texture);
            foreach (var texture in NightTextures.Values)
                viewer.TextureManager.Mark(texture);
            foreach (var texture in LightTextures.Values)
                viewer.TextureManager.Mark(texture);
            foreach (var textureList in PDayTextures.Values)
                if (textureList != null)
                    foreach (var texture in textureList)
                        viewer.TextureManager.Mark(texture);
            foreach (var textureList in PNightTextures.Values)
                if (textureList != null)
                    foreach (var texture in textureList)
                        viewer.TextureManager.Mark(texture);
            foreach (var textureList in PLightTextures.Values)
                if (textureList != null)
                    foreach (var texture in textureList)
                        viewer.TextureManager.Mark(texture);
        }
    }

    public class CabRenderer : RenderPrimitive
    {
        private CabSpriteBatchMaterial _SpriteShader2DCabView;
        private Matrix _Scale = Matrix.Identity;
        private Texture2D _CabTexture;
        private readonly Texture2D letterboxTexture;
        private CabShader _Shader;  // Shaders must have unique Keys - below
        private int ShaderKey = 1;  // Shader Key must refer to only o

        private Point _PrevScreenSize;

        private List<List<CabViewControlRenderer>> CabViewControlRenderersList = new List<List<CabViewControlRenderer>>();
        private Viewer _Viewer;
        private MSTSLocomotive _Locomotive;
        private int _Location;
        private bool _isNightTexture;
        private bool HasCabLightDirectory = false;
        public Dictionary<int, CabViewControlRenderer> ControlMap;

        public CabRenderer(Viewer viewer, MSTSLocomotive car)
        {
            //Sequence = RenderPrimitiveSequence.CabView;
            _Viewer = viewer;
            _Locomotive = car;
            // _Viewer.DisplaySize intercepted to adjust cab view height
            Point DisplaySize = _Viewer.DisplaySize;
            DisplaySize.Y = _Viewer.CabHeightPixels;

            _PrevScreenSize = DisplaySize;

            letterboxTexture = new Texture2D(viewer.RenderProcess.GraphicsDevice, 1, 1);
            letterboxTexture.SetData(new Color[] { Color.Black });

            // Use same shader for both front-facing and rear-facing cabs.
            if (_Locomotive.CabViewList[(int)CabViewType.Front].ExtendedCVF != null)
            {
                _Shader = new CabShader(viewer.RenderProcess.GraphicsDevice,
                    ExtendedCVF.TranslatedPosition(_Locomotive.CabViewList[(int)CabViewType.Front].ExtendedCVF.Light1Position, DisplaySize),
                    ExtendedCVF.TranslatedPosition(_Locomotive.CabViewList[(int)CabViewType.Front].ExtendedCVF.Light2Position, DisplaySize),
                    ExtendedCVF.TranslatedColor(_Locomotive.CabViewList[(int)CabViewType.Front].ExtendedCVF.Light1Color),
                    ExtendedCVF.TranslatedColor(_Locomotive.CabViewList[(int)CabViewType.Front].ExtendedCVF.Light2Color));
            }

            _SpriteShader2DCabView = (CabSpriteBatchMaterial)viewer.MaterialManager.Load("CabSpriteBatch", null, 0, 0, _Shader);

            #region Create Control renderers
            ControlMap = new Dictionary<int, CabViewControlRenderer>();
            int[] count = new int[256];//enough to hold all types, count the occurence of each type
            var i = 0;
            bool firstOne = true;
            foreach (var cabView in car.CabViewList)
            {
                if (cabView.CVFFile != null)
                {
                    // Loading ACE files, skip displaying ERROR messages
                    foreach (var cabfile in cabView.CVFFile.Views2D)
                    {
                        HasCabLightDirectory = CABTextureManager.LoadTextures(viewer, cabfile);
                    }

                    if (firstOne)
                    {
                        _Viewer.AdjustCabHeight(_Viewer.DisplaySize.X, _Viewer.DisplaySize.Y);

                        _Viewer.CabCamera.ScreenChanged();
                        DisplaySize.Y = _Viewer.CabHeightPixels;
                        // Use same shader for both front-facing and rear-facing cabs.
                        if (_Locomotive.CabViewList[(int)CabViewType.Front].ExtendedCVF != null)
                        {
                            _Shader = new CabShader(viewer.RenderProcess.GraphicsDevice,
                            ExtendedCVF.TranslatedPosition(_Locomotive.CabViewList[(int)CabViewType.Front].ExtendedCVF.Light1Position, DisplaySize),
                            ExtendedCVF.TranslatedPosition(_Locomotive.CabViewList[(int)CabViewType.Front].ExtendedCVF.Light2Position, DisplaySize),
                            ExtendedCVF.TranslatedColor(_Locomotive.CabViewList[(int)CabViewType.Front].ExtendedCVF.Light1Color),
                            ExtendedCVF.TranslatedColor(_Locomotive.CabViewList[(int)CabViewType.Front].ExtendedCVF.Light2Color));
                        }
                        _SpriteShader2DCabView = (CabSpriteBatchMaterial)viewer.MaterialManager.Load("CabSpriteBatch", null, 0, 0, _Shader);
                        firstOne = false;
                    }

                    if (cabView.CVFFile.CabViewControls == null)
                        continue;

                    var controlSortIndex = 1;  // Controls are drawn atop the cabview and in order they appear in the CVF file.
                                               // This allows the segments of moving-scale meters to be hidden by covers (e.g. TGV-A)
                    CabViewControlRenderersList.Add(new List<CabViewControlRenderer>());
                    foreach (CabViewControl cvc in cabView.CVFFile.CabViewControls)
                    {
                        controlSortIndex++;
                        int key = 1000 * (int)cvc.ControlType + count[(int)cvc.ControlType];
                        CabViewDialControl dial = cvc as CabViewDialControl;
                        if (dial != null)
                        {
                            CabViewDialRenderer cvcr = new CabViewDialRenderer(viewer, car, dial, _Shader);
                            cvcr.SortIndex = controlSortIndex;
                            CabViewControlRenderersList[i].Add(cvcr);
                            if (!ControlMap.ContainsKey(key)) ControlMap.Add(key, cvcr);
                            count[(int)cvc.ControlType]++;
                            continue;
                        }
                        CabViewFireboxControl firebox = cvc as CabViewFireboxControl;
                        if (firebox != null)
                        {
                            CabViewGaugeRenderer cvgrFire = new CabViewGaugeRenderer(viewer, car, firebox, _Shader);
                            cvgrFire.SortIndex = controlSortIndex++;
                            CabViewControlRenderersList[i].Add(cvgrFire);
                            // don't "continue", because this cvc has to be also recognized as CVCGauge
                        }
                        CabViewGaugeControl gauge = cvc as CabViewGaugeControl;
                        if (gauge != null)
                        {
                            CabViewGaugeRenderer cvgr = new CabViewGaugeRenderer(viewer, car, gauge, _Shader);
                            cvgr.SortIndex = controlSortIndex;
                            CabViewControlRenderersList[i].Add(cvgr);
                            if (!ControlMap.ContainsKey(key)) ControlMap.Add(key, cvgr);
                            count[(int)cvc.ControlType]++;
                            continue;
                        }
                        CabViewSignalControl asp = cvc as CabViewSignalControl;
                        if (asp != null)
                        {
                            CabViewDiscreteRenderer aspr = new CabViewDiscreteRenderer(viewer, car, asp, _Shader);
                            aspr.SortIndex = controlSortIndex;
                            CabViewControlRenderersList[i].Add(aspr);
                            if (!ControlMap.ContainsKey(key)) ControlMap.Add(key, aspr);
                            count[(int)cvc.ControlType]++;
                            continue;
                        }
                        CabViewMultiStateDisplayControl multi = cvc as CabViewMultiStateDisplayControl;
                        if (multi != null)
                        {
                            CabViewDiscreteRenderer mspr = new CabViewDiscreteRenderer(viewer, car, multi, _Shader);
                            mspr.SortIndex = controlSortIndex;
                            CabViewControlRenderersList[i].Add(mspr);
                            if (!ControlMap.ContainsKey(key)) ControlMap.Add(key, mspr);
                            count[(int)cvc.ControlType]++;
                            continue;
                        }
                        CabViewDiscreteControl disc = cvc as CabViewDiscreteControl;
                        if (disc != null)
                        {
                            CabViewDiscreteRenderer cvdr = new CabViewDiscreteRenderer(viewer, car, disc, _Shader);
                            cvdr.SortIndex = controlSortIndex;
                            CabViewControlRenderersList[i].Add(cvdr);
                            if (!ControlMap.ContainsKey(key)) ControlMap.Add(key, cvdr);
                            count[(int)cvc.ControlType]++;
                            continue;
                        }
                        CabViewDigitalControl digital = cvc as CabViewDigitalControl;
                        if (digital != null)
                        {
                            CabViewDigitalRenderer cvdr;
                            if (viewer.Settings.CircularSpeedGauge && digital.ControlStyle == CabViewControlStyle.Needle)
                                cvdr = new CircularSpeedGaugeRenderer(viewer, car, digital, _Shader);
                            else
                                cvdr = new CabViewDigitalRenderer(viewer, car, digital, _Shader);
                            cvdr.SortIndex = controlSortIndex;
                            CabViewControlRenderersList[i].Add(cvdr);
                            if (!ControlMap.ContainsKey(key)) ControlMap.Add(key, cvdr);
                            count[(int)cvc.ControlType]++;
                            continue;
                        }
                        CabViewScreenControl screen = cvc as CabViewScreenControl;
                        if (screen != null)
                        {
                            if (screen.ControlType == CabViewControlType.Orts_Etcs)
                            {
                                var cvr = new DriverMachineInterfaceRenderer(viewer, car, screen, _Shader);
                                cvr.SortIndex = controlSortIndex;
                                CabViewControlRenderersList[i].Add(cvr);
                                if (!ControlMap.ContainsKey(key)) ControlMap.Add(key, cvr);
                                count[(int)cvc.ControlType]++;
                                continue;
                            }
                        }
                    }
                }
                i++;
            }
            #endregion

        }

        public CabRenderer(Viewer viewer, MSTSLocomotive car, CabViewFile CVFFile) //used by 3D cab as a refrence, thus many can be eliminated
        {
            _Viewer = viewer;
            _Locomotive = car;


            #region Create Control renderers
            ControlMap = new Dictionary<int, CabViewControlRenderer>();
            int[] count = new int[256];//enough to hold all types, count the occurence of each type
            var i = 0;

            var controlSortIndex = 1;  // Controls are drawn atop the cabview and in order they appear in the CVF file.
                                       // This allows the segments of moving-scale meters to be hidden by covers (e.g. TGV-A)
            CabViewControlRenderersList.Add(new List<CabViewControlRenderer>());
            foreach (CabViewControl cvc in CVFFile.CabViewControls)
            {
                controlSortIndex++;
                int key = 1000 * (int)cvc.ControlType + count[(int)cvc.ControlType];
                CabViewDialControl dial = cvc as CabViewDialControl;
                if (dial != null)
                {
                    CabViewDialRenderer cvcr = new CabViewDialRenderer(viewer, car, dial, _Shader);
                    cvcr.SortIndex = controlSortIndex;
                    CabViewControlRenderersList[i].Add(cvcr);
                    if (!ControlMap.ContainsKey(key)) ControlMap.Add(key, cvcr);
                    count[(int)cvc.ControlType]++;
                    continue;
                }
                CabViewFireboxControl firebox = cvc as CabViewFireboxControl;
                if (firebox != null)
                {
                    CabViewGaugeRenderer cvgrFire = new CabViewGaugeRenderer(viewer, car, firebox, _Shader);
                    cvgrFire.SortIndex = controlSortIndex++;
                    CabViewControlRenderersList[i].Add(cvgrFire);
                    // don't "continue", because this cvc has to be also recognized as CVCGauge
                }
                CabViewGaugeControl gauge = cvc as CabViewGaugeControl;
                if (gauge != null)
                {
                    CabViewGaugeRenderer cvgr = new CabViewGaugeRenderer(viewer, car, gauge, _Shader);
                    cvgr.SortIndex = controlSortIndex;
                    CabViewControlRenderersList[i].Add(cvgr);
                    if (!ControlMap.ContainsKey(key)) ControlMap.Add(key, cvgr);
                    count[(int)cvc.ControlType]++;
                    continue;
                }
                CabViewSignalControl asp = cvc as CabViewSignalControl;
                if (asp != null)
                {
                    CabViewDiscreteRenderer aspr = new CabViewDiscreteRenderer(viewer, car, asp, _Shader);
                    aspr.SortIndex = controlSortIndex;
                    CabViewControlRenderersList[i].Add(aspr);
                    if (!ControlMap.ContainsKey(key)) ControlMap.Add(key, aspr);
                    count[(int)cvc.ControlType]++;
                    continue;
                }
                CabViewMultiStateDisplayControl multi = cvc as CabViewMultiStateDisplayControl;
                if (multi != null)
                {
                    CabViewDiscreteRenderer mspr = new CabViewDiscreteRenderer(viewer, car, multi, _Shader);
                    mspr.SortIndex = controlSortIndex;
                    CabViewControlRenderersList[i].Add(mspr);
                    if (!ControlMap.ContainsKey(key)) ControlMap.Add(key, mspr);
                    count[(int)cvc.ControlType]++;
                    continue;
                }
                CabViewDiscreteControl disc = cvc as CabViewDiscreteControl;
                if (disc != null)
                {
                    CabViewDiscreteRenderer cvdr = new CabViewDiscreteRenderer(viewer, car, disc, _Shader);
                    cvdr.SortIndex = controlSortIndex;
                    CabViewControlRenderersList[i].Add(cvdr);
                    if (!ControlMap.ContainsKey(key)) ControlMap.Add(key, cvdr);
                    count[(int)cvc.ControlType]++;
                    continue;
                }
                CabViewDigitalControl digital = cvc as CabViewDigitalControl;
                if (digital != null)
                {
                    CabViewDigitalRenderer cvdr;
                    if (viewer.Settings.CircularSpeedGauge && digital.ControlStyle == CabViewControlStyle.Needle)
                        cvdr = new CircularSpeedGaugeRenderer(viewer, car, digital, _Shader);
                    else
                        cvdr = new CabViewDigitalRenderer(viewer, car, digital, _Shader);
                    cvdr.SortIndex = controlSortIndex;
                    CabViewControlRenderersList[i].Add(cvdr);
                    if (!ControlMap.ContainsKey(key)) ControlMap.Add(key, cvdr);
                    count[(int)cvc.ControlType]++;
                    continue;
                }
                CabViewScreenControl screen = cvc as CabViewScreenControl;
                if (screen != null)
                {
                    if (screen.ControlType == CabViewControlType.Orts_Etcs)
                    {
                        var cvr = new DriverMachineInterfaceRenderer(viewer, car, screen, _Shader);
                        cvr.SortIndex = controlSortIndex;
                        CabViewControlRenderersList[i].Add(cvr);
                        if (!ControlMap.ContainsKey(key)) ControlMap.Add(key, cvr);
                        count[(int)cvc.ControlType]++;
                        continue;
                    }
                }
            }
            #endregion
        }

        public void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            if (!_Locomotive.ShowCab)
                return;

            bool Dark = _Viewer.MaterialManager.sunDirection.Y <= -0.085f || _Viewer.Camera.IsUnderground;
            bool CabLight = _Locomotive.CabLightOn;

            CabCamera cbc = _Viewer.Camera as CabCamera;
            if (cbc != null)
            {
                _Location = cbc.SideLocation;
            }
            else
            {
                _Location = 0;
            }

            var i = (_Locomotive.UsingRearCab) ? 1 : 0;
            _CabTexture = CABTextureManager.GetTexture(_Locomotive.CabViewList[i].CVFFile.Views2D[_Location], Dark, CabLight, out _isNightTexture, HasCabLightDirectory);
            if (_CabTexture == SharedMaterialManager.MissingTexture)
                return;

            if (_PrevScreenSize != _Viewer.DisplaySize && _Shader != null)
            {
                _PrevScreenSize = _Viewer.DisplaySize;
                _Shader.SetLightPositions(
                    ExtendedCVF.TranslatedPosition(_Locomotive.CabViewList[i].ExtendedCVF.Light1Position, _Viewer.DisplaySize),
                    ExtendedCVF.TranslatedPosition(_Locomotive.CabViewList[i].ExtendedCVF.Light2Position, _Viewer.DisplaySize));
            }

            frame.AddPrimitive(_SpriteShader2DCabView, this, RenderPrimitiveGroup.Cab, ref _Scale);
            //frame.AddPrimitive(Materials.SpriteBatchMaterial, this, RenderPrimitiveGroup.Cab, ref _Scale);

            if (_Location == 0)
                foreach (var cvcr in CabViewControlRenderersList[i])
                    cvcr.PrepareFrame(frame, elapsedTime);
        }

        public override void Draw()
        {
            var cabScale = new Vector2((float)_Viewer.CabWidthPixels / _CabTexture.Width, (float)_Viewer.CabHeightPixels / _CabTexture.Height);
            // Cab view vertical position adjusted to allow for clip or stretch.
            var cabPos = new Vector2(_Viewer.CabXOffsetPixels / cabScale.X, -_Viewer.CabYOffsetPixels / cabScale.Y);
            var cabSize = new Vector2((_Viewer.CabWidthPixels - _Viewer.CabExceedsDisplayHorizontally) / cabScale.X, (_Viewer.CabHeightPixels - _Viewer.CabExceedsDisplay) / cabScale.Y);
            int round(float x)
            {
                return (int)Math.Round(x);
            }
            var cabRect = new Rectangle(round(cabPos.X), round(cabPos.Y), round(cabSize.X), round(cabSize.Y));

            if (_Shader != null)
            {
                // TODO: Readd ability to control night time lighting.
                float overcast = _Viewer.Settings.UseMSTSEnv ? _Viewer.World.MSTSSky.mstsskyovercastFactor : _Viewer.Simulator.Weather.OvercastFactor;
                _Shader.SetData(_Viewer.MaterialManager.sunDirection, _isNightTexture, false, overcast);
                _Shader.SetTextureData(cabRect.Left, cabRect.Top, cabRect.Width, cabRect.Height);
            }

            if (_CabTexture == null)
                return;

            var drawOrigin = new Vector2(_CabTexture.Width / 2, _CabTexture.Height / 2);
            var drawPos = new Vector2(_Viewer.CabWidthPixels / 2, _Viewer.CabHeightPixels / 2);
            // Cab view position adjusted to allow for letterboxing.
            drawPos.X += _Viewer.CabXLetterboxPixels;
            drawPos.Y += _Viewer.CabYLetterboxPixels;

            _SpriteShader2DCabView.SpriteBatch.Draw(_CabTexture, drawPos, cabRect, Color.White, 0f, drawOrigin, cabScale, SpriteEffects.None, 0f);

            // Draw letterboxing.
            void drawLetterbox(int x, int y, int w, int h)
            {
                _SpriteShader2DCabView.SpriteBatch.Draw(letterboxTexture, new Rectangle(x, y, w, h), Color.White);
            }
            if (_Viewer.CabXLetterboxPixels > 0)
            {
                drawLetterbox(0, 0, _Viewer.CabXLetterboxPixels, _Viewer.DisplaySize.Y);
                drawLetterbox(_Viewer.CabXLetterboxPixels + _Viewer.CabWidthPixels, 0, _Viewer.DisplaySize.X - _Viewer.CabWidthPixels - _Viewer.CabXLetterboxPixels, _Viewer.DisplaySize.Y);
            }
            if (_Viewer.CabYLetterboxPixels > 0)
            {
                drawLetterbox(0, 0, _Viewer.DisplaySize.X, _Viewer.CabYLetterboxPixels);
                drawLetterbox(0, _Viewer.CabYLetterboxPixels + _Viewer.CabHeightPixels, _Viewer.DisplaySize.X, _Viewer.DisplaySize.Y - _Viewer.CabHeightPixels - _Viewer.CabYLetterboxPixels);
            }
        }

        internal void Mark()
        {
            _Viewer.TextureManager.Mark(_CabTexture);

            var i = (_Locomotive.UsingRearCab) ? 1 : 0;
            foreach (var cvcr in CabViewControlRenderersList[i])
                cvcr.Mark();
        }
    }

    /// <summary>
    /// Base class for rendering Cab Controls
    /// </summary>
    public abstract class CabViewControlRenderer : RenderPrimitive
    {
        protected readonly Viewer Viewer;
        protected readonly MSTSLocomotive Locomotive;
        internal protected readonly CabViewControl Control;
        protected readonly CabShader Shader;
        protected readonly int ShaderKey = 1;
        protected readonly CabSpriteBatchMaterial ControlView;

        protected Vector2 Position;
        protected Texture2D Texture;
        protected bool IsNightTexture;
        protected bool HasCabLightDirectory;
        private Matrix Matrix = Matrix.Identity;

        public CabViewControlRenderer(Viewer viewer, MSTSLocomotive locomotive, CabViewControl control, CabShader shader)
        {
            Viewer = viewer;
            Locomotive = locomotive;
            Control = control;
            Shader = shader;

            ControlView = (CabSpriteBatchMaterial)viewer.MaterialManager.Load("CabSpriteBatch", null, 0, 0, Shader);

            HasCabLightDirectory = CABTextureManager.LoadTextures(Viewer, Control.AceFile);
        }

        public CabViewControlType GetControlType()
        {
            return Control.ControlType;
        }
        /// <summary>
        /// Gets the requested Locomotive data and returns it as a fraction (from 0 to 1) of the range between Min and Max values.
        /// </summary>
        /// <returns>Data value as fraction (from 0 to 1) of the range between Min and Max values</returns>
        public float GetRangeFraction()
        {
            var data = Locomotive.GetDataOf(Control);
            if (data < Control.ScaleRangeMin)
                return 0;
            if (data > Control.ScaleRangeMax)
                return 1;

            if (Control.ScaleRangeMax == Control.ScaleRangeMin)
                return 0;

            return (float)((data - Control.ScaleRangeMin) / (Control.ScaleRangeMax - Control.ScaleRangeMin));
        }

        public CabViewControlStyle GetStyle()
        {
            return Control.ControlStyle;
        }

        public virtual void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            frame.AddPrimitive(ControlView, this, RenderPrimitiveGroup.Cab, ref Matrix);
        }

        internal void Mark()
        {
            Viewer.TextureManager.Mark(Texture);
        }
    }

    /// <summary>
    /// Interface for mouse controllable CabViewControls
    /// </summary>
    public interface ICabViewMouseControlRenderer
    {
        bool IsMouseWithin(Point mousePoint);
        void HandleUserInput(GenericButtonEventType buttonEventType, Point position, Vector2 delta);
        string GetControlName(Point mousePoint);

    }

    /// <summary>
    /// Dial Cab Control Renderer
    /// Problems with aspect ratio
    /// </summary>
    public class CabViewDialRenderer : CabViewControlRenderer
    {
        private readonly CabViewDialControl ControlDial;

        /// <summary>
        /// Rotation center point, in unscaled texture coordinates
        /// </summary>
        private readonly Vector2 Origin;

        /// <summary>
        /// Scale factor. Only downscaling is allowed by MSTS, so the value is in 0-1 range
        /// </summary>
        private readonly float Scale = 1;

        /// <summary>
        /// 0° is 12 o'clock, 90° is 3 o'clock
        /// </summary>
        private float Rotation;
        private float ScaleToScreen = 1;

        public CabViewDialRenderer(Viewer viewer, MSTSLocomotive locomotive, CabViewDialControl control, CabShader shader)
            : base(viewer, locomotive, control, shader)
        {
            ControlDial = control;

            Texture = CABTextureManager.GetTexture(Control.AceFile, false, false, out IsNightTexture, HasCabLightDirectory);
            if (ControlDial.Bounds.Height < Texture.Height)
                Scale = ((float)ControlDial.Bounds.Height / Texture.Height);
            Origin = new Vector2((float)Texture.Width / 2, ControlDial.Center / Scale);
        }

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            var dark = Viewer.MaterialManager.sunDirection.Y <= -0.085f || Viewer.Camera.IsUnderground;

            Texture = CABTextureManager.GetTexture(Control.AceFile, dark, Locomotive.CabLightOn, out IsNightTexture, HasCabLightDirectory);
            if (Texture == SharedMaterialManager.MissingTexture)
                return;

            base.PrepareFrame(frame, elapsedTime);

            // Cab view height and vertical position adjusted to allow for clip or stretch.
            // Cab view position adjusted to allow for letterboxing.
            Position.X = (float)Viewer.CabWidthPixels / 640 * ((float)Control.Bounds.X + Origin.X * Scale) - Viewer.CabXOffsetPixels + Viewer.CabXLetterboxPixels;
            Position.Y = (float)Viewer.CabHeightPixels / 480 * ((float)Control.Bounds.Y + Origin.Y * Scale) + Viewer.CabYOffsetPixels + Viewer.CabYLetterboxPixels;
            ScaleToScreen = (float)Viewer.CabWidthPixels / 640 * Scale;

            var rangeFraction = GetRangeFraction();
            var direction = ControlDial.Direction == 0 ? 1 : -1;
            var rangeDegrees = (int)ControlDial.Direction * (ControlDial.EndAngle - ControlDial.StartAngle);
            while (rangeDegrees <= 0)
                rangeDegrees += 360;
            Rotation = MathHelper.WrapAngle(MathHelper.ToRadians(ControlDial.StartAngle + (int)ControlDial.Direction * rangeDegrees * rangeFraction));
        }

        public override void Draw()
        {
            if (Shader != null)
            {
                Shader.SetTextureData(Position.X, Position.Y, Texture.Width * ScaleToScreen, Texture.Height * ScaleToScreen);
            }
            ControlView.SpriteBatch.Draw(Texture, Position, null, Color.White, Rotation, Origin, ScaleToScreen, SpriteEffects.None, 0);
        }
    }

    /// <summary>
    /// Gauge type renderer
    /// Supports pointer, liquid, solid
    /// Supports Orientation and Direction
    /// </summary>
    public class CabViewGaugeRenderer : CabViewControlRenderer
    {
        private readonly CabViewGaugeControl Gauge;
        private readonly Rectangle SourceRectangle;
        private Rectangle DestinationRectangle = new Rectangle();

        //      bool LoadMeterPositive = true;
        private Color DrawColor;
        private float DrawRotation;
        private Double Num;
        private bool IsFire;

        public CabViewGaugeRenderer(Viewer viewer, MSTSLocomotive locomotive, CabViewGaugeControl control, CabShader shader)
            : base(viewer, locomotive, control, shader)
        {
            Gauge = control;
            if ((Control.ControlType == CabViewControlType.Reverser_Plate) || (Gauge.ControlStyle == CabViewControlStyle.Pointer))
            {
                DrawColor = Color.White;
                Texture = CABTextureManager.GetTexture(Control.AceFile, false, Locomotive.CabLightOn, out IsNightTexture, HasCabLightDirectory);
                SourceRectangle.Width = (int)Texture.Width;
                SourceRectangle.Height = (int)Texture.Height;
            }
            else
            {
                DrawColor = Gauge.PositiveColors[0];
                SourceRectangle = Gauge.Area;
            }
        }

        public CabViewGaugeRenderer(Viewer viewer, MSTSLocomotive locomotive, CabViewFireboxControl control, CabShader shader)
            : base(viewer, locomotive, control, shader)
        {
            Gauge = control;
            HasCabLightDirectory = CABTextureManager.LoadTextures(Viewer, control.FireBoxAceFile);
            Texture = CABTextureManager.GetTexture(control.FireBoxAceFile, false, Locomotive.CabLightOn, out IsNightTexture, HasCabLightDirectory);
            DrawColor = Color.White;
            SourceRectangle.Width = (int)Texture.Width;
            SourceRectangle.Height = (int)Texture.Height;
            IsFire = true;
        }

        public Color GetColor(out bool positive)
        {
            if (Locomotive.GetDataOf(Control) < 0)
            {
                positive = false;
                return Gauge.NegativeColors[0];
            }
            else
            {
                positive = true;
                return Gauge.PositiveColors[0];
            }
        }

        public CabViewGaugeControl GetGauge() { return Gauge; }

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            if (!(Gauge is CabViewFireboxControl))
            {
                var dark = Viewer.MaterialManager.sunDirection.Y <= -0.085f || Viewer.Camera.IsUnderground;
                Texture = CABTextureManager.GetTexture(Control.AceFile, dark, Locomotive.CabLightOn, out IsNightTexture, HasCabLightDirectory);
            }
            if (Texture == SharedMaterialManager.MissingTexture)
                return;

            base.PrepareFrame(frame, elapsedTime);

            // Cab view height adjusted to allow for clip or stretch.
            var xratio = (float)Viewer.CabWidthPixels / 640;
            var yratio = (float)Viewer.CabHeightPixels / 480;

            float percent, xpos, ypos, zeropos;

            percent = IsFire ? 1f : GetRangeFraction();
            //           LoadMeterPositive = percent + Gauge.MinValue / (Gauge.MaxValue - Gauge.MinValue) >= 0;
            Num = Locomotive.GetDataOf(Control);

            if (Gauge.Orientation == 0)  // gauge horizontal
            {
                ypos = Gauge.Bounds.Height;
                zeropos = (float)(Gauge.Bounds.Width * -Control.ScaleRangeMin / (Control.ScaleRangeMax - Control.ScaleRangeMin));
                xpos = Gauge.Bounds.Width * percent;
            }
            else  // gauge vertical
            {
                xpos = Gauge.Bounds.Width;
                zeropos = (float)(Gauge.Bounds.Height * -Control.ScaleRangeMin / (Control.ScaleRangeMax - Control.ScaleRangeMin));
                ypos = Gauge.Bounds.Height * percent;
            }

            int destX, destY, destW, destH;
            if (Gauge.ControlStyle == CabViewControlStyle.Solid || Gauge.ControlStyle == CabViewControlStyle.Liquid)
            {
                if (Control.ScaleRangeMin < 0)
                {
                    if (Gauge.Orientation == 0)
                    {
                        destX = (int)(xratio * (Control.Bounds.X)) + (int)(xratio * (zeropos < xpos ? zeropos : xpos));
                        destY = (int)(yratio * Control.Bounds.Y);
                        destY = (int)(yratio * (Control.Bounds.Y) - (int)(yratio * (Gauge.Direction == 0 && zeropos > xpos ? (zeropos - xpos) * Math.Sin(DrawRotation) : 0)));
                        destW = ((int)(xratio * xpos) - (int)(xratio * zeropos)) * (xpos >= zeropos ? 1 : -1);
                        destH = (int)(yratio * ypos);
                    }
                    else
                    {
                        destX = (int)(xratio * Control.Bounds.X) +(int)(xratio * (Gauge.Direction == 0 && ypos > zeropos ? (ypos - zeropos) * Math.Sin(DrawRotation) : 0));
                        if (Gauge.Direction != 1 && !IsFire)
                            destY = (int)(yratio * (Control.Bounds.Y + zeropos)) + (ypos > zeropos ? (int)(yratio * (zeropos - ypos)) : 0);
                        else
                            destY = (int)(yratio * (Control.Bounds.Y + (zeropos < ypos ? zeropos : ypos)));
                        destW = (int)(xratio * xpos);
                        destH = ((int)(yratio * (ypos - zeropos))) * (ypos > zeropos ? 1 : -1);
                    }
                }
                else
                {
                    var topY = Control.Bounds.Y;  // top of visible column. +ve Y is downwards
                    if (Gauge.Direction != 0)  // column grows from bottom or from right
                    {
                        if (Gauge.Orientation != 0)
                        {
                            topY += (int)(Gauge.Bounds.Height * (1 - percent));
                            destX = (int)(xratio * (Control.Bounds.X + Gauge.Bounds.Width - xpos + ypos * Math.Sin(DrawRotation)));
                        }
                        else
                        {
                            topY -= (int)(xpos * Math.Sin(DrawRotation));
                            destX = (int)(xratio * (Control.Bounds.X + Gauge.Bounds.Width - xpos));
                        }
                    }
                    else
                    {
                        destX = (int)(xratio * Control.Bounds.X);
                    }
                    destY = (int)(yratio * topY);
                    destW = (int)(xratio * xpos);
                    destH = (int)(yratio * ypos);
                }
            }
            else // pointer gauge using texture
            {
                var topY = Control.Bounds.Y;  // top of visible column. +ve Y is downwards
                // even if there is a rotation, we leave the X position unaltered (for small angles Cos(alpha) = 1)
                if (Gauge.Orientation == 0) // gauge horizontal
                {

                    if (Gauge.Direction != 0)  // column grows from right
                    {
                        destX = (int)(xratio * (Control.Bounds.X + Gauge.Area.Width - 0.5 * Gauge.Area.Width - xpos));
                        topY -= (int)(xpos * Math.Sin(DrawRotation));
                    }
                    else
                    {
                        destX = (int)(xratio * (Control.Bounds.X - 0.5 * Gauge.Area.Width + xpos));
                        topY += (int)(xpos * Math.Sin(DrawRotation));
                    }
                }
                else // gauge vertical
                {
                    // even if there is a rotation, we leave the Y position unaltered (for small angles Cos(alpha) = 1)
                    topY += (int)(ypos - 0.5 * Gauge.Area.Height);
                    if (Gauge.Direction == 0)
                        destX = (int)(xratio * (Control.Bounds.X - ypos * Math.Sin(DrawRotation)));
                    else  // column grows from bottom
                    {
                        topY += (int)(Gauge.Area.Height - 2 * ypos);
                        destX = (int)(xratio * (Control.Bounds.X + ypos * Math.Sin(DrawRotation)));
                    }
                }
                destY = (int)(yratio * topY);
                destW = (int)(xratio * Gauge.Area.Width);
                destH = (int)(yratio * Gauge.Area.Height);

                // Adjust coal texture height, because it mustn't show up at the bottom of door (see Scotsman)
                // TODO: cut the texture at the bottom instead of stretching
                if (Gauge is CabViewFireboxControl)
                    destH = Math.Min(destH, (int)(yratio * (Control.Bounds.Y + 0.5 * Gauge.Area.Height)) - destY);
            }
            if (Control.ScaleRangeMin < 0 && Control.ControlType != CabViewControlType.Reverser_Plate && Gauge.ControlStyle != CabViewControlStyle.Pointer)
            {
                if (Num < 0 && Gauge.NegativeColors[0].A != 0)
                {
                    if ((Gauge.NegativeColors.Length >= 2) && (Num < Gauge.NegativeTrigger))
                        DrawColor = Gauge.NegativeColors[1];
                    else
                        DrawColor = Gauge.NegativeColors[0];
                }
                else
                {
                    if ((Gauge.PositiveColors.Length >= 2) && (Num > Gauge.PositiveTrigger))
                        DrawColor = Gauge.PositiveColors[1];
                    else
                        DrawColor = Gauge.PositiveColors[0];
                }
            }

            // Cab view vertical position adjusted to allow for clip or stretch.
            destX -= Viewer.CabXOffsetPixels;
            destY += Viewer.CabYOffsetPixels;

            // Cab view position adjusted to allow for letterboxing.
            destX += Viewer.CabXLetterboxPixels;
            destY += Viewer.CabYLetterboxPixels;

            DestinationRectangle.X = destX;
            DestinationRectangle.Y = destY;
            DestinationRectangle.Width = destW;
            DestinationRectangle.Height = destH;
            DrawRotation = Gauge.Rotation;
        }

        public override void Draw()
        {
            if (Shader != null)
            {
                Shader.SetTextureData(DestinationRectangle.Left, DestinationRectangle.Top, DestinationRectangle.Width, DestinationRectangle.Height);
            }
            ControlView.SpriteBatch.Draw(Texture, DestinationRectangle, SourceRectangle, DrawColor, DrawRotation, Vector2.Zero, SpriteEffects.None, 0);
        }
    }

    /// <summary>
    /// Discrete renderer for Lever, Twostate, Tristate, Multistate, Signal
    /// </summary>
    public class CabViewDiscreteRenderer : CabViewControlRenderer, ICabViewMouseControlRenderer
    {
        private readonly CabViewFramedControl ControlDiscrete;
        private readonly Rectangle SourceRectangle;
        private Rectangle DestinationRectangle = new Rectangle();
        public readonly float CVCFlashTimeOn = 0.75f;
        public readonly float CVCFlashTimeTotal = 1.5f;
        private float CumulativeTime;

        /// <summary>
        /// Accumulated mouse movement. Used for controls with no assigned notch controllers, e.g. headlight and reverser.
        /// </summary>
        private float IntermediateValue;

        public CabViewDiscreteRenderer(Viewer viewer, MSTSLocomotive locomotive, CabViewFramedControl control, CabShader shader)
            : base(viewer, locomotive, control, shader)
        {
            ControlDiscrete = control;
            CABTextureManager.DisassembleTexture(viewer.RenderProcess.GraphicsDevice, Control.AceFile, Control.Bounds.Width, Control.Bounds.Height, ControlDiscrete.FramesCount, ControlDiscrete.FramesX, ControlDiscrete.FramesY);
            Texture = CABTextureManager.GetTextureByIndexes(Control.AceFile, 0, false, false, out IsNightTexture, HasCabLightDirectory);
            SourceRectangle = new Rectangle(0, 0, Texture.Width, Texture.Height);
        }

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            var index = GetDrawIndex();

            var mS = Control as CabViewMultiStateDisplayControl;
            if (mS != null)
            {
                CumulativeTime += (float)elapsedTime.ClockSeconds;
                while (CumulativeTime > CVCFlashTimeTotal)
                    CumulativeTime -= CVCFlashTimeTotal;
                if ((mS.Styles.Count > index) && (mS.Styles[index] == 1) && (CumulativeTime > CVCFlashTimeOn))
                    return;
            }
            var dark = Viewer.MaterialManager.sunDirection.Y <= -0.085f || Viewer.Camera.IsUnderground;

            Texture = CABTextureManager.GetTextureByIndexes(Control.AceFile, index, dark, Locomotive.CabLightOn, out IsNightTexture, HasCabLightDirectory);
            if (Texture == SharedMaterialManager.MissingTexture)
                return;

            base.PrepareFrame(frame, elapsedTime);

            // Cab view height and vertical position adjusted to allow for clip or stretch.
            var xratio = (float)Viewer.CabWidthPixels / 640;
            var yratio = (float)Viewer.CabHeightPixels / 480;
            // Cab view position adjusted to allow for letterboxing.
            DestinationRectangle.X = (int)(xratio * Control.Bounds.X * 1.0001) - Viewer.CabXOffsetPixels + Viewer.CabXLetterboxPixels;
            DestinationRectangle.Y = (int)(yratio * Control.Bounds.Y * 1.0001) + Viewer.CabYOffsetPixels + Viewer.CabYLetterboxPixels;
            DestinationRectangle.Width = (int)(xratio * Math.Min(Control.Bounds.Width, Texture.Width));  // Allow only downscaling of the texture, and not upscaling
            DestinationRectangle.Height = (int)(yratio * Math.Min(Control.Bounds.Height, Texture.Height));  // Allow only downscaling of the texture, and not upscaling
        }

        public override void Draw()
        {
            if (Shader != null)
            {
                Shader.SetTextureData(DestinationRectangle.Left, DestinationRectangle.Top, DestinationRectangle.Width, DestinationRectangle.Height);
            }
            ControlView.SpriteBatch.Draw(Texture, DestinationRectangle, SourceRectangle, Color.White);
        }

        /// <summary>
        /// Determines the index of the Texture to be drawn
        /// </summary>
        /// <returns>index of the Texture</returns>
        public int GetDrawIndex()
        {
            var data = Locomotive.GetDataOf(Control);

            var index = 0;
            switch (ControlDiscrete.ControlType)
            {
                case CabViewControlType.Engine_Brake:
                case CabViewControlType.Brakeman_Brake:
                case CabViewControlType.Train_Brake:
                case CabViewControlType.Regulator:
                case CabViewControlType.CutOff:
                case CabViewControlType.Blower:
                case CabViewControlType.Dampers_Front:
                case CabViewControlType.Steam_Heat:
                case CabViewControlType.Orts_Water_Scoop:
                case CabViewControlType.Water_Injector1:
                case CabViewControlType.Water_Injector2:
                case CabViewControlType.Small_Ejector:
                case CabViewControlType.Orts_Large_Ejector:
                case CabViewControlType.FireHole:
                    index = PercentToIndex(data);
                    break;
                case CabViewControlType.Throttle:
                case CabViewControlType.Throttle_Display:
                    index = PercentToIndex(data);
                    break;
                case CabViewControlType.Friction_Braking:
                    index = data > 0.001 ? 1 : 0;
                    break;
                case CabViewControlType.Dynamic_Brake:
                case CabViewControlType.Dynamic_Brake_Display:
                    var dynBrakePercent = Locomotive.Train.TrainType == TrainType.AiPlayerHosting ?
                        Locomotive.DynamicBrakePercent : Locomotive.LocalDynamicBrakePercent;
                    if (Locomotive.DynamicBrakeController != null)
                    {
                        if (dynBrakePercent == -1)
                            break;
                        if (!Locomotive.HasSmoothStruc)
                        {
                            index = Locomotive.DynamicBrakeController != null ? Locomotive.DynamicBrakeController.CurrentNotch : 0;
                        }
                        else
                            index = PercentToIndex(dynBrakePercent);
                    }
                    else
                    {
                        index = PercentToIndex(dynBrakePercent);
                    }
                    break;
                case CabViewControlType.Cph_Display:
                    if (Locomotive.CombinedControlType == MSTSLocomotive.CombinedControl.ThrottleDynamic && Locomotive.DynamicBrakePercent >= 0)
                        // TODO <CSComment> This is a sort of hack to allow MSTS-compliant operation of Dynamic brake indications in the standard USA case with 8 steps (e.g. Dash9)
                        // This hack returns to code of previous OR versions (e.g. release 1.0).
                        // The clean solution for MSTS compliance would be not to increment the percentage of the dynamic brake at first dynamic brake key pression, so that
                        // subsequent steps become of 12.5% as in MSTS instead of 11.11% as in OR. This requires changes in the physics logic </CSComment>
                        index = (int)((ControlDiscrete.FramesCount) * Locomotive.GetCombinedHandleValue(false));
                    else
                        index = PercentToIndex(Locomotive.GetCombinedHandleValue(false));
                    break;
                case CabViewControlType.Cp_Handle:
                    if (Locomotive.CombinedControlType == MSTSLocomotive.CombinedControl.ThrottleDynamic && Locomotive.DynamicBrakePercent >= 0
                        || Locomotive.CombinedControlType == MSTSLocomotive.CombinedControl.ThrottleAir && Locomotive.TrainBrakeController.CurrentValue > 0)
                        index = PercentToIndex(Locomotive.GetCombinedHandleValue(false));
                    else
                        index = PercentToIndex(Locomotive.GetCombinedHandleValue(false));
                    break;
                case CabViewControlType.Alerter_Display:
                case CabViewControlType.Reset:
                case CabViewControlType.Wipers:
                case CabViewControlType.ExternalWipers:
                case CabViewControlType.LeftDoor:
                case CabViewControlType.RightDoor:
                case CabViewControlType.Mirrors:
                case CabViewControlType.Horn:
                case CabViewControlType.Vacuum_Exhauster:
                case CabViewControlType.Whistle:
                case CabViewControlType.Bell:
                case CabViewControlType.Sanders:
                case CabViewControlType.Sanding:
                case CabViewControlType.WheelSlip:
                case CabViewControlType.Front_HLight:
                case CabViewControlType.Pantograph:
                case CabViewControlType.Pantograph2:
                case CabViewControlType.Orts_Pantograph3:
                case CabViewControlType.Orts_Pantograph4:
                case CabViewControlType.Pantographs_4:
                case CabViewControlType.Pantographs_4C:
                case CabViewControlType.Pantographs_5:
                case CabViewControlType.Panto_Display:
                case CabViewControlType.Orts_Circuit_Breaker_Driver_Closing_Order:
                case CabViewControlType.Orts_Circuit_Breaker_Driver_Opening_Order:
                case CabViewControlType.Orts_Circuit_Breaker_Driver_Closing_Authorization:
                case CabViewControlType.Orts_Circuit_Breaker_State:
                case CabViewControlType.Orts_Circuit_Breaker_Closed:
                case CabViewControlType.Orts_Circuit_Breaker_Open:
                case CabViewControlType.Orts_Circuit_Breaker_Authorized:
                case CabViewControlType.Orts_Circuit_Breaker_Open_And_Authorized:
                case CabViewControlType.Direction:
                case CabViewControlType.Direction_Display:
                case CabViewControlType.Aspect_Display:
                case CabViewControlType.Gears:
                case CabViewControlType.OverSpeed:
                case CabViewControlType.Penalty_App:
                case CabViewControlType.Emergency_Brake:
                case CabViewControlType.Doors_Display:
                case CabViewControlType.Cyl_Cocks:
                case CabViewControlType.Orts_BlowDown_Valve:
                case CabViewControlType.Orts_Cyl_Comp:
                case CabViewControlType.Steam_Inj1:
                case CabViewControlType.Steam_Inj2:
                case CabViewControlType.Gears_Display:
                case CabViewControlType.Cab_Radio:
                case CabViewControlType.Orts_Player_Diesel_Engine:
                case CabViewControlType.Orts_Helpers_Diesel_Engines:
                case CabViewControlType.Orts_Player_Diesel_Engine_State:
                case CabViewControlType.Orts_Player_Diesel_Engine_Starter:
                case CabViewControlType.Orts_Player_Diesel_Engine_Stopper:
                case CabViewControlType.Orts_CabLight:
                case CabViewControlType.Orts_LeftDoor:
                case CabViewControlType.Orts_RightDoor:
                case CabViewControlType.Orts_Mirros:
                case CabViewControlType.Orts_Battery:
                case CabViewControlType.Orts_PowerKey:
                    index = (int)data;
                    break;

                // Train Control System controls
                case CabViewControlType.Orts_TCS1:
                case CabViewControlType.Orts_TCS2:
                case CabViewControlType.Orts_TCS3:
                case CabViewControlType.Orts_TCS4:
                case CabViewControlType.Orts_TCS5:
                case CabViewControlType.Orts_TCS6:
                case CabViewControlType.Orts_TCS7:
                case CabViewControlType.Orts_TCS8:
                case CabViewControlType.Orts_TCS9:
                case CabViewControlType.Orts_TCS10:
                case CabViewControlType.Orts_TCS11:
                case CabViewControlType.Orts_TCS12:
                case CabViewControlType.Orts_TCS13:
                case CabViewControlType.Orts_TCS14:
                case CabViewControlType.Orts_TCS15:
                case CabViewControlType.Orts_TCS16:
                case CabViewControlType.Orts_TCS17:
                case CabViewControlType.Orts_TCS18:
                case CabViewControlType.Orts_TCS19:
                case CabViewControlType.Orts_TCS20:
                case CabViewControlType.Orts_TCS21:
                case CabViewControlType.Orts_TCS22:
                case CabViewControlType.Orts_TCS23:
                case CabViewControlType.Orts_TCS24:
                case CabViewControlType.Orts_TCS25:
                case CabViewControlType.Orts_TCS26:
                case CabViewControlType.Orts_TCS27:
                case CabViewControlType.Orts_TCS28:
                case CabViewControlType.Orts_TCS29:
                case CabViewControlType.Orts_TCS30:
                case CabViewControlType.Orts_TCS31:
                case CabViewControlType.Orts_TCS32:
                case CabViewControlType.Orts_TCS33:
                case CabViewControlType.Orts_TCS34:
                case CabViewControlType.Orts_TCS35:
                case CabViewControlType.Orts_TCS36:
                case CabViewControlType.Orts_TCS37:
                case CabViewControlType.Orts_TCS38:
                case CabViewControlType.Orts_TCS39:
                case CabViewControlType.Orts_TCS40:
                case CabViewControlType.Orts_TCS41:
                case CabViewControlType.Orts_TCS42:
                case CabViewControlType.Orts_TCS43:
                case CabViewControlType.Orts_TCS44:
                case CabViewControlType.Orts_TCS45:
                case CabViewControlType.Orts_TCS46:
                case CabViewControlType.Orts_TCS47:
                case CabViewControlType.Orts_TCS48:
                    index = (int)data;
                    break;
            }
            // If it is a control with NumPositions and NumValues, the index becomes the reference to the Positions entry, which in turn is the frame index within the .ace file
            if (ControlDiscrete is CabViewDiscreteControl && !(ControlDiscrete is CabViewSignalControl) && (ControlDiscrete as CabViewDiscreteControl).Positions.Count > index &&
                (ControlDiscrete as CabViewDiscreteControl).Positions.Count == ControlDiscrete.Values.Count && index >= 0)
                index = (ControlDiscrete as CabViewDiscreteControl).Positions[index];

            if (index >= ControlDiscrete.FramesCount) index = ControlDiscrete.FramesCount - 1;
            if (index < 0) index = 0;
            return index;
        }

        public bool IsMouseWithin(Point mousePoint)
        {
            return ControlDiscrete.MouseControl & DestinationRectangle.Contains(mousePoint.X, mousePoint.Y);
        }

        private float UpdateCommandValue(float value, GenericButtonEventType buttonEventType, Vector2 delta)
        {
            switch (ControlDiscrete.ControlStyle)
            {
                case CabViewControlStyle.OnOff:
                    return buttonEventType == GenericButtonEventType.Pressed ? 1 - value : value;
                case CabViewControlStyle.While_Pressed:
                case CabViewControlStyle.Pressed:
                    return buttonEventType == GenericButtonEventType.Pressed ? 1 : 0;
                case CabViewControlStyle.None:
                    IntermediateValue %= 0.5f;
                    IntermediateValue += (ControlDiscrete.Orientation > 0 ? delta.Y / Control.Bounds.Height : delta.X / Control.Bounds.Width) * (ControlDiscrete.Direction > 0 ? -1 : 1);
                    return IntermediateValue > 0.5f ? 1 : IntermediateValue < -0.5f ? -1 : 0;
                default:
                    return value + (ControlDiscrete.Orientation > 0 ? delta.Y / Control.Bounds.Height : delta.X / Control.Bounds.Width) * (ControlDiscrete.Direction > 0 ? -1 : 1);
            }
        }

        public string GetControlName(Point mousePoint)
        {
            return Locomotive.TrainControlSystem.GetDisplayString(GetControlType().ToString());
        }

        /// <summary>
        /// Handles cabview mouse events, and changes the corresponding locomotive control values.
        /// </summary>
        public void HandleUserInput(GenericButtonEventType buttonEventType, Point position, Vector2 delta)
        {
            switch (Control.ControlType)
            {
                case CabViewControlType.Regulator:
                case CabViewControlType.Throttle: Locomotive.SetThrottleValue(UpdateCommandValue(Locomotive.ThrottleController.IntermediateValue, buttonEventType, delta)); break;
                case CabViewControlType.Engine_Brake: Locomotive.SetEngineBrakeValue(UpdateCommandValue(Locomotive.EngineBrakeController.IntermediateValue, buttonEventType, delta)); break;
                case CabViewControlType.Brakeman_Brake: Locomotive.SetBrakemanBrakeValue(UpdateCommandValue(Locomotive.BrakemanBrakeController.IntermediateValue, buttonEventType, delta)); break;
                case CabViewControlType.Train_Brake: Locomotive.SetTrainBrakeValue(UpdateCommandValue(Locomotive.TrainBrakeController.IntermediateValue, buttonEventType, delta)); break;
                case CabViewControlType.Dynamic_Brake: Locomotive.SetDynamicBrakeValue(UpdateCommandValue(Locomotive.DynamicBrakeController.IntermediateValue, buttonEventType, delta)); break;
                case CabViewControlType.Gears: Locomotive.SetGearBoxValue(UpdateCommandValue(Locomotive.GearBoxController.IntermediateValue, buttonEventType, delta)); break;
                case CabViewControlType.Direction:
                    float dir = UpdateCommandValue(0, buttonEventType, delta);
                    if (dir != 0)
                        _ = new ReverserCommand(Viewer.Log, dir > 0);
                    break;
                case CabViewControlType.Front_HLight:
                    float hl = UpdateCommandValue(0, buttonEventType, delta);
                    if (hl != 0)
                        _ = new HeadlightCommand(Viewer.Log, hl > 0);
                    break;
                case CabViewControlType.Whistle:
                case CabViewControlType.Horn: _ = new HornCommand(Viewer.Log, UpdateCommandValue(Locomotive.Horn ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Vacuum_Exhauster: _ = new VacuumExhausterCommand(Viewer.Log, UpdateCommandValue(Locomotive.VacuumExhausterPressed ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Bell: _ = new BellCommand(Viewer.Log, UpdateCommandValue(Locomotive.Bell ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Sanders:
                case CabViewControlType.Sanding: _ = new SanderCommand(Viewer.Log, UpdateCommandValue(Locomotive.Sander ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Pantograph: _ = new PantographCommand(Viewer.Log, 1, UpdateCommandValue(Locomotive.Pantographs[1].CommandUp ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Pantograph2: _ = new PantographCommand(Viewer.Log, 2, UpdateCommandValue(Locomotive.Pantographs[2].CommandUp ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Orts_Pantograph3: _ = new PantographCommand(Viewer.Log, 3, UpdateCommandValue(Locomotive.Pantographs[3].CommandUp ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Orts_Pantograph4: _ = new PantographCommand(Viewer.Log, 4, UpdateCommandValue(Locomotive.Pantographs[4].CommandUp ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Pantographs_4C:
                case CabViewControlType.Pantographs_4:
                    float pantos = UpdateCommandValue(0, buttonEventType, delta);
                    if (pantos != 0)
                    {
                        if (Locomotive.Pantographs[1].State == PantographState.Down && Locomotive.Pantographs[2].State == PantographState.Down) //TODO 20210412 investigate why compiler is showing CA1508 warning the condition is always true
                        {
                            if (pantos > 0)
                                _ = new PantographCommand(Viewer.Log, 1, true);
                            else if (Control.ControlType == CabViewControlType.Pantographs_4C)
                                _ = new PantographCommand(Viewer.Log, 2, true);
                        }
                        else if (Locomotive.Pantographs[1].State == PantographState.Up && Locomotive.Pantographs[2].State == PantographState.Down)
                        {
                            if (pantos > 0)
                                _ = new PantographCommand(Viewer.Log, 2, true);
                            else
                                _ = new PantographCommand(Viewer.Log, 1, false);
                        }
                        else if (Locomotive.Pantographs[1].State == PantographState.Up && Locomotive.Pantographs[2].State == PantographState.Up)
                        {
                            if (pantos > 0)
                                _ = new PantographCommand(Viewer.Log, 1, false);
                            else
                                _ = new PantographCommand(Viewer.Log, 2, false);
                        }
                        else if (Locomotive.Pantographs[1].State == PantographState.Down && Locomotive.Pantographs[2].State == PantographState.Up)
                        {
                            if (pantos < 0)
                                _ = new PantographCommand(Viewer.Log, 1, true);
                            else if (Control.ControlType == CabViewControlType.Pantographs_4C)
                                _ = new PantographCommand(Viewer.Log, 2, false);
                        }
                    }
                    break;
                case CabViewControlType.Steam_Heat: Locomotive.SetSteamHeatValue(UpdateCommandValue(Locomotive.SteamHeatController.IntermediateValue, buttonEventType, delta)); break;
                case CabViewControlType.Orts_Water_Scoop: if (((Locomotive as MSTSSteamLocomotive).WaterScoopDown ? 1 : 0) != UpdateCommandValue(Locomotive.WaterScoopDown ? 1 : 0, buttonEventType, delta)) _ = new ToggleWaterScoopCommand(Viewer.Log); break;
                case CabViewControlType.Orts_Circuit_Breaker_Driver_Closing_Order:
                    _ = new CircuitBreakerClosingOrderCommand(Viewer.Log, UpdateCommandValue((Locomotive as MSTSElectricLocomotive).PowerSupply.CircuitBreaker.DriverClosingOrder ? 1 : 0, buttonEventType, delta) > 0);
                    _ = new CircuitBreakerClosingOrderButtonCommand(Viewer.Log, UpdateCommandValue(buttonEventType == GenericButtonEventType.Pressed ? 1 : 0, buttonEventType, delta) > 0);
                    break;
                case CabViewControlType.Orts_Circuit_Breaker_Driver_Opening_Order: _ = new CircuitBreakerOpeningOrderButtonCommand(Viewer.Log, UpdateCommandValue(buttonEventType == GenericButtonEventType.Pressed ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Orts_Circuit_Breaker_Driver_Closing_Authorization: _ = new CircuitBreakerClosingAuthorizationCommand(Viewer.Log, UpdateCommandValue((Locomotive as MSTSElectricLocomotive).PowerSupply.CircuitBreaker.DriverClosingAuthorization ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Emergency_Brake: if ((Locomotive.EmergencyButtonPressed ? 1 : 0) != UpdateCommandValue(Locomotive.EmergencyButtonPressed ? 1 : 0, buttonEventType, delta)) _ = new EmergencyPushButtonCommand(Viewer.Log); break;
                case CabViewControlType.Orts_Bailoff: _ = new BailOffCommand(Viewer.Log, UpdateCommandValue(Locomotive.BailOff ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Orts_QuickRelease: _ = new QuickReleaseCommand(Viewer.Log, UpdateCommandValue(Locomotive.TrainBrakeController.QuickReleaseButtonPressed ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Orts_Overcharge: _ = new BrakeOverchargeCommand(Viewer.Log, UpdateCommandValue(Locomotive.TrainBrakeController.OverchargeButtonPressed ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Reset: _ = new AlerterCommand(Viewer.Log, UpdateCommandValue(Locomotive.TrainControlSystem.AlerterButtonPressed ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Cp_Handle: Locomotive.SetCombinedHandleValue(UpdateCommandValue(Locomotive.GetCombinedHandleValue(true), buttonEventType, delta)); break;
                // Steam locomotives only:
                case CabViewControlType.CutOff: (Locomotive as MSTSSteamLocomotive).SetCutoffValue(UpdateCommandValue((Locomotive as MSTSSteamLocomotive).CutoffController.IntermediateValue, buttonEventType, delta)); break;
                case CabViewControlType.Blower: (Locomotive as MSTSSteamLocomotive).SetBlowerValue(UpdateCommandValue((Locomotive as MSTSSteamLocomotive).BlowerController.IntermediateValue, buttonEventType, delta)); break;
                case CabViewControlType.Dampers_Front: (Locomotive as MSTSSteamLocomotive).SetDamperValue(UpdateCommandValue((Locomotive as MSTSSteamLocomotive).DamperController.IntermediateValue, buttonEventType, delta)); break;
                case CabViewControlType.FireHole: (Locomotive as MSTSSteamLocomotive).SetFireboxDoorValue(UpdateCommandValue((Locomotive as MSTSSteamLocomotive).FireboxDoorController.IntermediateValue, buttonEventType, delta)); break;
                case CabViewControlType.Water_Injector1: (Locomotive as MSTSSteamLocomotive).SetInjector1Value(UpdateCommandValue((Locomotive as MSTSSteamLocomotive).Injector1Controller.IntermediateValue, buttonEventType, delta)); break;
                case CabViewControlType.Water_Injector2: (Locomotive as MSTSSteamLocomotive).SetInjector2Value(UpdateCommandValue((Locomotive as MSTSSteamLocomotive).Injector2Controller.IntermediateValue, buttonEventType, delta)); break;
                case CabViewControlType.Cyl_Cocks: if (((Locomotive as MSTSSteamLocomotive).CylinderCocksAreOpen ? 1 : 0) != UpdateCommandValue((Locomotive as MSTSSteamLocomotive).CylinderCocksAreOpen ? 1 : 0, buttonEventType, delta)) _ = new ToggleCylinderCocksCommand(Viewer.Log); break;
                case CabViewControlType.Orts_BlowDown_Valve: if (((Locomotive as MSTSSteamLocomotive).BlowdownValveOpen ? 1 : 0) != UpdateCommandValue((Locomotive as MSTSSteamLocomotive).BlowdownValveOpen ? 1 : 0, buttonEventType, delta)) _ = new ToggleBlowdownValveCommand(Viewer.Log); break;
                case CabViewControlType.Orts_Cyl_Comp: if (((Locomotive as MSTSSteamLocomotive).CylinderCompoundOn ? 1 : 0) != UpdateCommandValue((Locomotive as MSTSSteamLocomotive).CylinderCompoundOn ? 1 : 0, buttonEventType, delta)) _ = new ToggleCylinderCompoundCommand(Viewer.Log); break;
                case CabViewControlType.Steam_Inj1: if (((Locomotive as MSTSSteamLocomotive).Injector1IsOn ? 1 : 0) != UpdateCommandValue((Locomotive as MSTSSteamLocomotive).Injector1IsOn ? 1 : 0, buttonEventType, delta)) _ = new ToggleInjectorCommand(Viewer.Log, 1); break;
                case CabViewControlType.Steam_Inj2: if (((Locomotive as MSTSSteamLocomotive).Injector2IsOn ? 1 : 0) != UpdateCommandValue((Locomotive as MSTSSteamLocomotive).Injector2IsOn ? 1 : 0, buttonEventType, delta)) _ = new ToggleInjectorCommand(Viewer.Log, 2); break;
                case CabViewControlType.Small_Ejector: (Locomotive as MSTSSteamLocomotive).SetSmallEjectorValue(UpdateCommandValue((Locomotive as MSTSSteamLocomotive).SmallEjectorController.IntermediateValue, buttonEventType, delta)); break;
                case CabViewControlType.Orts_Large_Ejector: (Locomotive as MSTSSteamLocomotive).SetLargeEjectorValue(UpdateCommandValue((Locomotive as MSTSSteamLocomotive).LargeEjectorController.IntermediateValue, buttonEventType, delta)); break;
                //
                case CabViewControlType.Cab_Radio: _ = new CabRadioCommand(Viewer.Log, UpdateCommandValue(Locomotive.CabRadioOn ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Wipers: _ = new WipersCommand(Viewer.Log, UpdateCommandValue(Locomotive.Wiper ? 1 : 0, buttonEventType, delta) > 0); break;
                case CabViewControlType.Orts_Player_Diesel_Engine:
                    MSTSDieselLocomotive dieselLoco = Locomotive as MSTSDieselLocomotive;
                    if ((dieselLoco.DieselEngines[0].EngineStatus == Simulation.RollingStocks.SubSystems.PowerSupplies.DieselEngine.Status.Running ||
                                dieselLoco.DieselEngines[0].EngineStatus == Simulation.RollingStocks.SubSystems.PowerSupplies.DieselEngine.Status.Stopped) &&
                                UpdateCommandValue(1, buttonEventType, delta) == 0)
                        _ = new TogglePlayerEngineCommand(Viewer.Log); break;
                case CabViewControlType.Orts_Helpers_Diesel_Engines:
                    foreach (TrainCar car in Locomotive.Train.Cars)
                    {
                        dieselLoco = car as MSTSDieselLocomotive;
                        if (dieselLoco != null && dieselLoco.AcceptMUSignals)
                        {
                            if (car == Viewer.Simulator.PlayerLocomotive && dieselLoco.DieselEngines.Count > 1)
                            {
                                if ((dieselLoco.DieselEngines[1].EngineStatus == Simulation.RollingStocks.SubSystems.PowerSupplies.DieselEngine.Status.Running ||
                                            dieselLoco.DieselEngines[1].EngineStatus == Simulation.RollingStocks.SubSystems.PowerSupplies.DieselEngine.Status.Stopped) &&
                                            UpdateCommandValue(1, buttonEventType, delta) == 0)
                                    _ = new ToggleHelpersEngineCommand(Viewer.Log);
                                break;
                            }
                            else if (car != Viewer.Simulator.PlayerLocomotive)
                            {
                                if ((dieselLoco.DieselEngines[0].EngineStatus == Simulation.RollingStocks.SubSystems.PowerSupplies.DieselEngine.Status.Running ||
                                            dieselLoco.DieselEngines[0].EngineStatus == Simulation.RollingStocks.SubSystems.PowerSupplies.DieselEngine.Status.Stopped) &&
                                            UpdateCommandValue(1, buttonEventType, delta) == 0)
                                    _ = new ToggleHelpersEngineCommand(Viewer.Log);
                                break;
                            }
                        }
                    }
                    break;
                case CabViewControlType.Orts_Player_Diesel_Engine_Starter:
                    dieselLoco = Locomotive as MSTSDieselLocomotive;
                    if (dieselLoco.DieselEngines[0].EngineStatus == Simulation.RollingStocks.SubSystems.PowerSupplies.DieselEngine.Status.Stopped &&
                                UpdateCommandValue(1, buttonEventType, delta) == 0)
                        _ = new TogglePlayerEngineCommand(Viewer.Log); break;
                case CabViewControlType.Orts_Player_Diesel_Engine_Stopper:
                    dieselLoco = Locomotive as MSTSDieselLocomotive;
                    if (dieselLoco.DieselEngines[0].EngineStatus == Simulation.RollingStocks.SubSystems.PowerSupplies.DieselEngine.Status.Running &&
                                UpdateCommandValue(1, buttonEventType, delta) == 0)
                        _ = new TogglePlayerEngineCommand(Viewer.Log); break;
                case CabViewControlType.Orts_CabLight:
                    if ((Locomotive.CabLightOn ? 1 : 0) != UpdateCommandValue(Locomotive.CabLightOn ? 1 : 0, buttonEventType, delta)) _ = new ToggleCabLightCommand(Viewer.Log); break;
                case CabViewControlType.Orts_LeftDoor:
                    if ((Locomotive.GetCabFlipped() ? (Locomotive.DoorRightOpen ? 1 : 0) : Locomotive.DoorLeftOpen ? 1 : 0)
                        != UpdateCommandValue(Locomotive.GetCabFlipped() ? (Locomotive.DoorRightOpen ? 1 : 0) : Locomotive.DoorLeftOpen ? 1 : 0, buttonEventType, delta)) _ = new ToggleDoorsLeftCommand(Viewer.Log); break;
                case CabViewControlType.Orts_RightDoor:
                    if ((Locomotive.GetCabFlipped() ? (Locomotive.DoorLeftOpen ? 1 : 0) : Locomotive.DoorRightOpen ? 1 : 0)
                         != UpdateCommandValue(Locomotive.GetCabFlipped() ? (Locomotive.DoorLeftOpen ? 1 : 0) : Locomotive.DoorRightOpen ? 1 : 0, buttonEventType, delta)) _ = new ToggleDoorsRightCommand(Viewer.Log); break;
                case CabViewControlType.Orts_Mirros:
                    if ((Locomotive.MirrorOpen ? 1 : 0) != UpdateCommandValue(Locomotive.MirrorOpen ? 1 : 0, buttonEventType, delta)) _ = new ToggleMirrorsCommand(Viewer.Log); break;
                case CabViewControlType.Orts_Battery:
                    if ((Locomotive.Battery ? 1 : 0) != UpdateCommandValue(Locomotive.Battery ? 1 : 0, buttonEventType, delta)) _ = new ToggleBatteryCommand(Viewer.Log); break;
                case CabViewControlType.Orts_PowerKey:
                    if ((Locomotive.PowerKey ? 1 : 0) != UpdateCommandValue(Locomotive.PowerKey ? 1 : 0, buttonEventType, delta)) _ = new TogglePowerKeyCommand(Viewer.Log); break;
                // Train Control System controls
                case CabViewControlType.Orts_TCS1:
                case CabViewControlType.Orts_TCS2:
                case CabViewControlType.Orts_TCS3:
                case CabViewControlType.Orts_TCS4:
                case CabViewControlType.Orts_TCS5:
                case CabViewControlType.Orts_TCS6:
                case CabViewControlType.Orts_TCS7:
                case CabViewControlType.Orts_TCS8:
                case CabViewControlType.Orts_TCS9:
                case CabViewControlType.Orts_TCS10:
                case CabViewControlType.Orts_TCS11:
                case CabViewControlType.Orts_TCS12:
                case CabViewControlType.Orts_TCS13:
                case CabViewControlType.Orts_TCS14:
                case CabViewControlType.Orts_TCS15:
                case CabViewControlType.Orts_TCS16:
                case CabViewControlType.Orts_TCS17:
                case CabViewControlType.Orts_TCS18:
                case CabViewControlType.Orts_TCS19:
                case CabViewControlType.Orts_TCS20:
                case CabViewControlType.Orts_TCS21:
                case CabViewControlType.Orts_TCS22:
                case CabViewControlType.Orts_TCS23:
                case CabViewControlType.Orts_TCS24:
                case CabViewControlType.Orts_TCS25:
                case CabViewControlType.Orts_TCS26:
                case CabViewControlType.Orts_TCS27:
                case CabViewControlType.Orts_TCS28:
                case CabViewControlType.Orts_TCS29:
                case CabViewControlType.Orts_TCS30:
                case CabViewControlType.Orts_TCS31:
                case CabViewControlType.Orts_TCS32:
                case CabViewControlType.Orts_TCS33:
                case CabViewControlType.Orts_TCS34:
                case CabViewControlType.Orts_TCS35:
                case CabViewControlType.Orts_TCS36:
                case CabViewControlType.Orts_TCS37:
                case CabViewControlType.Orts_TCS38:
                case CabViewControlType.Orts_TCS39:
                case CabViewControlType.Orts_TCS40:
                case CabViewControlType.Orts_TCS41:
                case CabViewControlType.Orts_TCS42:
                case CabViewControlType.Orts_TCS43:
                case CabViewControlType.Orts_TCS44:
                case CabViewControlType.Orts_TCS45:
                case CabViewControlType.Orts_TCS46:
                case CabViewControlType.Orts_TCS47:
                case CabViewControlType.Orts_TCS48:
                    int commandIndex = (int)Control.ControlType - (int)CabViewControlType.Orts_TCS1;
                    if (UpdateCommandValue(1, buttonEventType, delta) > 0 ^ Locomotive.TrainControlSystem.TCSCommandButtonDown[commandIndex])
                        _ = new TCSButtonCommand(Viewer.Log, !Locomotive.TrainControlSystem.TCSCommandButtonDown[commandIndex], commandIndex);
                    _ = new TCSSwitchCommand(Viewer.Log, UpdateCommandValue(Locomotive.TrainControlSystem.TCSCommandSwitchOn[commandIndex] ? 1 : 0, buttonEventType, delta) > 0, commandIndex);
                    break;
            }
        }

        /// <summary>
        /// Translates a percent value to a display index
        /// </summary>
        /// <param name="percent">Percent to be translated</param>
        /// <returns>The calculated display index by the Control's Values</returns>
        private int PercentToIndex(float percent)
        {
            var index = 0;

            if (percent > 1)
                percent /= 100f;

            if (ControlDiscrete.ScaleRangeMin != ControlDiscrete.ScaleRangeMax && !(ControlDiscrete.ScaleRangeMin == 0 && ControlDiscrete.ScaleRangeMax == 0))
                percent = MathHelper.Clamp(percent, (float)ControlDiscrete.ScaleRangeMin, (float)ControlDiscrete.ScaleRangeMax);

            if (ControlDiscrete.Values.Count > 1)
            {
                try
                {
                    var val = ControlDiscrete.Values[0] <= ControlDiscrete.Values[ControlDiscrete.Values.Count - 1] ?
                        ControlDiscrete.Values.Where(v => (float)v <= percent + 0.00001).Last() : ControlDiscrete.Values.Where(v => (float)v <= percent + 0.00001).First();
                    index = ControlDiscrete.Values.IndexOf(val);
                }
                catch
                {
                    var val = ControlDiscrete.Values.Min();
                    index = ControlDiscrete.Values.IndexOf(val);
                }
            }
            else if (ControlDiscrete.ScaleRangeMax != ControlDiscrete.ScaleRangeMin)
            {
                index = (int)(percent / (ControlDiscrete.ScaleRangeMax - ControlDiscrete.ScaleRangeMin) * ControlDiscrete.FramesCount);
            }

            return index;
        }
    }

    /// <summary>
    /// Digital Cab Control renderer
    /// Uses fonts instead of graphic
    /// </summary>
    public class CabViewDigitalRenderer : CabViewControlRenderer
    {
        private readonly LabelAlignment Alignment;
        private string Format = "{0}";
        private readonly string Format1 = "{0}";
        private readonly string Format2 = "{0}";
        private float Num;
        private WindowTextFont DrawFont;
        protected Rectangle DrawPosition;
        private string DrawText;
        private Color DrawColor;
        private float DrawRotation;

        public CabViewDigitalRenderer(Viewer viewer, MSTSLocomotive car, CabViewDigitalControl digital, CabShader shader)
            : base(viewer, car, digital, shader)
        {
            Position.X = Control.Bounds.X;
            Position.Y = Control.Bounds.Y;

            // Clock defaults to centered.
            if (Control.ControlType == CabViewControlType.Clock)
                Alignment = LabelAlignment.Center;
            Alignment = digital.Justification == 1 ? LabelAlignment.Center : digital.Justification == 2 ? LabelAlignment.Left : digital.Justification == 3 ? LabelAlignment.Right : Alignment;

            Format1 = "{0:0" + new String('0', digital.LeadingZeros) + (digital.Accuracy > 0 ? "." + new String('0', (int)digital.Accuracy) : "") + "}";
            Format2 = "{0:0" + new String('0', digital.LeadingZeros) + (digital.AccuracySwitch > 0 ? "." + new String('0', (int)(digital.Accuracy + 1)) : "") + "}";
        }

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            var digital = Control as CabViewDigitalControl;

            Num = Locomotive.GetDataOf(Control);
            if (digital.ScaleRangeMin < digital.ScaleRangeMax)
                Num = MathHelper.Clamp(Num, digital.ScaleRangeMin, digital.ScaleRangeMax);
            if (Math.Abs(Num) < digital.AccuracySwitch)
                Format = Format2;
            else
                Format = Format1;
            DrawFont = Viewer.WindowManager.TextManager.GetExact(digital.FontFamily, Viewer.CabHeightPixels * digital.FontSize / 480, digital.FontStyle == 0 ? System.Drawing.FontStyle.Regular : System.Drawing.FontStyle.Bold);
            var xScale = Viewer.CabWidthPixels / 640f;
            var yScale = Viewer.CabHeightPixels / 480f;
            // Cab view position adjusted to allow for letterboxing.
            DrawPosition.X = (int)(Position.X * xScale) + (Viewer.CabExceedsDisplayHorizontally > 0 ? DrawFont.Height / 4 : 0) - Viewer.CabXOffsetPixels + Viewer.CabXLetterboxPixels;
            DrawPosition.Y = (int)((Position.Y + Control.Bounds.Height / 2) * yScale) - DrawFont.Height / 2 + Viewer.CabYOffsetPixels + Viewer.CabYLetterboxPixels;
            DrawPosition.Width = (int)(Control.Bounds.Width * xScale);
            DrawPosition.Height = (int)(Control.Bounds.Height * yScale);
            DrawRotation = digital.Rotation;

            if (Control.ControlType == CabViewControlType.Clock)
            {
                // Clock is drawn specially.
                var clockSeconds = Locomotive.Simulator.ClockTime;
                var hour = (int)(clockSeconds / 3600) % 24;
                var minute = (int)(clockSeconds / 60) % 60;
                var seconds = (int)clockSeconds % 60;

                if (hour < 0)
                    hour += 24;
                if (minute < 0)
                    minute += 60;
                if (seconds < 0)
                    seconds += 60;

                if (digital.ControlStyle == CabViewControlStyle.Hour12)
                {
                    hour %= 12;
                    if (hour == 0)
                        hour = 12;
                }
                DrawText = digital.Accuracy > 0 ? $"{hour:D2}:{minute:D2}:{seconds:D2}" : $"{hour:D2}:{minute:D2}";
                DrawColor = digital.PositiveColors[0];
            }
            else if (digital.PreviousValue != 0 && digital.PreviousValue > Num && digital.DecreaseColor.A != 0)
            {
                DrawText = string.Format(CultureInfo.CurrentCulture, Format, Math.Abs(Num));
                DrawColor = new Color(digital.DecreaseColor.R, digital.DecreaseColor.G, digital.DecreaseColor.B, digital.DecreaseColor.A);
            }
            else if (Num < 0 && digital.NegativeColors[0].A != 0)
            {
                DrawText = string.Format(CultureInfo.CurrentCulture, Format, Math.Abs(Num));
                if ((digital.NegativeColors.Length >= 2) && (Num < digital.NegativeTrigger))
                    DrawColor = digital.NegativeColors[1];
                else
                    DrawColor = digital.NegativeColors[0];
            }
            else if (digital.PositiveColors[0].A != 0)
            {
                DrawText = string.Format(CultureInfo.CurrentCulture, Format, Num);
                if ((digital.PositiveColors.Length >= 2) && (Num > digital.PositiveTrigger))
                    DrawColor = digital.PositiveColors[1];
                else
                    DrawColor = digital.PositiveColors[0];
            }
            else
            {
                DrawText = string.Format(CultureInfo.CurrentCulture, Format, Num);
                DrawColor = Color.White;
            }
            //          <CSComment> Now speedometer is handled like the other digitals

            base.PrepareFrame(frame, elapsedTime);
        }

        public override void Draw()
        {
            DrawFont.Draw(ControlView.SpriteBatch, DrawPosition, Point.Zero, DrawRotation, DrawText, Alignment, DrawColor, Color.Black);
        }

        public string GetDigits(out Color DrawColor)
        {
            try
            {
                var digital = Control as CabViewDigitalControl;
                string displayedText = "";
                Num = Locomotive.GetDataOf(Control);
                if (Math.Abs(Num) < digital.AccuracySwitch)
                    Format = Format2;
                else
                    Format = Format1;

                if (Control.ControlType == CabViewControlType.Clock)
                {
                    // Clock is drawn specially.
                    var clockSeconds = Locomotive.Simulator.ClockTime;
                    var hour = (int)(clockSeconds / 3600) % 24;
                    var minute = (int)(clockSeconds / 60) % 60;
                    var seconds = (int)clockSeconds % 60;

                    if (hour < 0)
                        hour += 24;
                    if (minute < 0)
                        minute += 60;
                    if (seconds < 0)
                        seconds += 60;

                    if (digital.ControlStyle == CabViewControlStyle.Hour12)
                    {
                        hour %= 12;
                        if (hour == 0)
                            hour = 12;
                    }
                    displayedText = digital.Accuracy > 0 ? $"{hour:D2}:{minute:D2}:{seconds:D2}" : $"{hour:D2}:{minute:D2}";
                    DrawColor = digital.PositiveColors[0];
                }
                else if (digital.PreviousValue != 0 && digital.PreviousValue > Num && digital.DecreaseColor.A != 0)
                {
                    displayedText = string.Format(CultureInfo.CurrentCulture, Format, Math.Abs(Num));
                    DrawColor = new Color(digital.DecreaseColor.R, digital.DecreaseColor.G, digital.DecreaseColor.B, digital.DecreaseColor.A);
                }
                else if (Num < 0 && digital.NegativeColors[0].A != 0)
                {
                    displayedText = string.Format(CultureInfo.CurrentCulture, Format, Math.Abs(Num));
                    if ((digital.NegativeColors.Length >= 2) && (Num < digital.NegativeTrigger))
                        DrawColor = digital.NegativeColors[1];
                    else
                        DrawColor = digital.NegativeColors[0];
                }
                else if (digital.PositiveColors[0].A != 0)
                {
                    displayedText = string.Format(CultureInfo.CurrentCulture, Format, Num);
                    if ((digital.PositiveColors.Length >= 2) && (Num > digital.PositiveTrigger))
                        DrawColor = digital.PositiveColors[1];
                    else DrawColor = digital.PositiveColors[0];
                }
                else
                {
                    displayedText = string.Format(CultureInfo.CurrentCulture, Format, Num);
                    DrawColor = Color.White;
                }
                // <CSComment> Speedometer is now managed like the other digitals

                return displayedText;
            }
            catch (Exception)
            {
                DrawColor = Color.Blue;
            }

            return "";
        }

        public string Get3DDigits(out bool Alert) //used in 3D cab, with AM/PM added, and determine if we want to use alert color
        {
            Alert = false;
            try
            {
                var digital = Control as CabViewDigitalControl;
                string displayedText = "";
                Num = Locomotive.GetDataOf(Control);
                if (Math.Abs(Num) < digital.AccuracySwitch)
                    Format = Format2;
                else
                    Format = Format1;

                if (Control.ControlType == CabViewControlType.Clock)
                {
                    // Clock is drawn specially.
                    var clockSeconds = Locomotive.Simulator.ClockTime;
                    var hour = (int)(clockSeconds / 3600) % 24;
                    var minute = (int)(clockSeconds / 60) % 60;
                    var seconds = (int)clockSeconds % 60;

                    if (hour < 0)
                        hour += 24;
                    if (minute < 0)
                        minute += 60;
                    if (seconds < 0)
                        seconds += 60;

                    if (digital.ControlStyle == CabViewControlStyle.Hour12)
                    {
                        if (hour < 12) displayedText = "a";
                        else displayedText = "p";
                        hour %= 12;
                        if (hour == 0)
                            hour = 12;
                    }
                    displayedText = (digital.Accuracy > 0 ? $"{hour:D2}:{minute:D2}:{seconds:D2}" : $"{hour:D2}:{minute:D2}") + displayedText;
                }
                else if (digital.PreviousValue != 0 && digital.PreviousValue > Num && digital.DecreaseColor.A != 0)
                {
                    displayedText = string.Format(CultureInfo.CurrentCulture, Format, Math.Abs(Num));
                }
                else if (Num < 0 && digital.NegativeColors[0].A != 0)
                {
                    displayedText = string.Format(CultureInfo.CurrentCulture, Format, Math.Abs(Num));
                    if ((digital.NegativeColors.Length >= 2) && (Num < digital.NegativeTrigger))
                        Alert = true;
                }
                else if (digital.PositiveColors[0].A != 0)
                {
                    displayedText = string.Format(CultureInfo.CurrentCulture, Format, Num);
                    if ((digital.PositiveColors.Length >= 2) && (Num > digital.PositiveTrigger))
                        Alert = true;
                }
                else
                {
                    displayedText = string.Format(CultureInfo.CurrentCulture, Format, Num);
                }
                // <CSComment> Speedometer is now managed like the other digitals

                return displayedText;
            }
            catch (Exception)
            {
                DrawColor = Color.Blue;
            }

            return "";
        }

    }

    /// <summary>
    /// ThreeDimentionCabViewer
    /// </summary>
    public class ThreeDimentionCabViewer : TrainCarViewer
    {
        private MSTSLocomotive Locomotive;

        public PoseableShape TrainCarShape = null;
        public Dictionary<int, AnimatedPartMultiState> AnimateParts = null;
        private Dictionary<int, ThreeDimCabGaugeNative> Gauges = null;
        private Dictionary<int, AnimatedPart> OnDemandAnimateParts = null; //like external wipers, and other parts that will be switched on by mouse in the future
                                                                          //Dictionary<int, DigitalDisplay> DigitParts = null;

        private Dictionary<int, ThreeDimCabDigit> DigitParts3D = null;
        private AnimatedPart ExternalWipers = null; // setting to zero to prevent a warning. Probably this will be used later. TODO
        protected MSTSLocomotive MSTSLocomotive { get { return (MSTSLocomotive)Car; } }

        private MSTSLocomotiveViewer LocoViewer;
        private SpriteBatchMaterial _Sprite2DCabView;
        public ThreeDimentionCabViewer(Viewer viewer, MSTSLocomotive car, MSTSLocomotiveViewer locoViewer)
            : base(viewer, car)
        {
            Locomotive = car;
            _Sprite2DCabView = (SpriteBatchMaterial)viewer.MaterialManager.Load("SpriteBatch");
            LocoViewer = locoViewer;
            if (car.CabView3D != null)
            {
                var shapePath = car.CabView3D.ShapeFilePath;
                TrainCarShape = new PoseableShape(shapePath + '\0' + Path.GetDirectoryName(shapePath), car, ShapeFlags.ShadowCaster | ShapeFlags.Interior);
                locoViewer.CabRenderer3D = new CabRenderer(viewer, car, car.CabView3D.CVFFile);
            }
            else locoViewer.CabRenderer3D = locoViewer.CabRenderer;

            AnimateParts = new Dictionary<int, AnimatedPartMultiState>();
            //DigitParts = new Dictionary<int, DigitalDisplay>();
            DigitParts3D = new Dictionary<int, ThreeDimCabDigit>();
            Gauges = new Dictionary<int, ThreeDimCabGaugeNative>();
            OnDemandAnimateParts = new Dictionary<int, AnimatedPart>();
            // Find the animated parts
            if (TrainCarShape != null && TrainCarShape.SharedShape.Animations != null)
            {
                string matrixName = ""; string typeName = ""; AnimatedPartMultiState tmpPart = null;
                for (int iMatrix = 0; iMatrix < TrainCarShape.SharedShape.MatrixNames.Count; ++iMatrix)
                {
                    matrixName = TrainCarShape.SharedShape.MatrixNames[iMatrix].ToUpper();
                    //Name convention
                    //TYPE:Order:Parameter-PartN
                    //e.g. ASPECT_SIGNAL:0:0-1: first ASPECT_SIGNAL, parameter is 0, this component is part 1 of this cab control
                    //     ASPECT_SIGNAL:0:0-2: first ASPECT_SIGNAL, parameter is 0, this component is part 2 of this cab control
                    //     ASPECT_SIGNAL:1:0  second ASPECT_SIGNAL, parameter is 0, this component is the only one for this cab control
                    typeName = matrixName.Split('-')[0]; //a part may have several sub-parts, like ASPECT_SIGNAL:0:0-1, ASPECT_SIGNAL:0:0-2
                    tmpPart = null;
                    int order = 0, key;
                    string parameter1 = "0", parameter2 = "";
                    CabViewControlRenderer style = null;
                    //ASPECT_SIGNAL:0:0
                    var tmp = typeName.Split(':');
                    if (tmp.Length > 1 && int.TryParse(tmp[1].Trim(), out order))
                    {
                        if (tmp.Length > 2)
                        {
                            parameter1 = tmp[2].Trim();
                            if (tmp.Length == 4) //we can get max two parameters per part
                                parameter2 = tmp[3].Trim();
                        }
                    }

                    if (EnumExtension.GetValue(tmp[0].Trim(), out CabViewControlType type))
                    {
                        key = 1000 * (int)type + order;
                        switch (type)
                        {
                            case CabViewControlType.ExternalWipers:
                            case CabViewControlType.Mirrors:
                            case CabViewControlType.LeftDoor:
                            case CabViewControlType.RightDoor:
                                break;
                            default:
                                //cvf file has no external wipers, left door, right door and mirrors key word
                                if (!locoViewer.CabRenderer3D.ControlMap.TryGetValue(key, out style))
                                {
                                    var cvfBasePath = Path.Combine(Path.GetDirectoryName(Locomotive.WagFilePath), "CABVIEW");
                                    var cvfFilePath = Path.Combine(cvfBasePath, Locomotive.CVFFileName);
                                    Trace.TraceWarning($"Cabview control {tmp[0].Trim()} has not been defined in CVF file {cvfFilePath}");
                                }
                                break;
                        }
                    }

                    key = 1000 * (int)type + order;
                    if (style != null && style is CabViewDigitalRenderer)//digits?
                    {
                        //DigitParts.Add(key, new DigitalDisplay(viewer, TrainCarShape, iMatrix, parameter, locoViewer.ThreeDimentionCabRenderer.ControlMap[key]));
                        DigitParts3D.Add(key, new ThreeDimCabDigit(viewer, iMatrix, parameter1, parameter2, this.TrainCarShape, locoViewer.CabRenderer3D.ControlMap[key]));
                    }
                    else if (style != null && style is CabViewGaugeRenderer)
                    {
                        var CVFR = (CabViewGaugeRenderer)style;

                        if (CVFR.GetGauge().ControlStyle != CabViewControlStyle.Pointer) //pointer will be animated, others will be drawn dynamicaly
                        {
                            Gauges.Add(key, new ThreeDimCabGaugeNative(viewer, iMatrix, parameter1, parameter2, this.TrainCarShape, locoViewer.CabRenderer3D.ControlMap[key]));
                        }
                        else
                        {//for pointer animation
                         //if there is a part already, will insert this into it, otherwise, create a new
                            if (!AnimateParts.ContainsKey(key))
                            {
                                tmpPart = new AnimatedPartMultiState(TrainCarShape, type, key);
                                AnimateParts.Add(key, tmpPart);
                            }
                            else tmpPart = AnimateParts[key];
                            tmpPart.AddMatrix(iMatrix); //tmpPart.SetPosition(false);
                        }
                    }
                    else
                    {
                        //if there is a part already, will insert this into it, otherwise, create a new
                        if (!AnimateParts.ContainsKey(key))
                        {
                            tmpPart = new AnimatedPartMultiState(TrainCarShape, type, key);
                            AnimateParts.Add(key, tmpPart);
                        }
                        else tmpPart = AnimateParts[key];
                        tmpPart.AddMatrix(iMatrix); //tmpPart.SetPosition(false);
                    }
                }
            }
        }
        public override void InitializeUserInputCommands() { }

        /// <summary>
        /// We are about to display a video frame.  Calculate positions for 
        /// animated objects, and add their primitives to the RenderFrame list.
        /// </summary>
        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            float elapsedClockSeconds = (float)elapsedTime.ClockSeconds;

            foreach (var p in AnimateParts)
            {
                if (p.Value.Type >= CabViewControlType.ExternalWipers) //for wipers, doors and mirrors
                {
                    switch (p.Value.Type)
                    {
                        case CabViewControlType.ExternalWipers:
                            p.Value.UpdateLoop(Locomotive.Wiper, elapsedTime);
                            break;
                        case CabViewControlType.LeftDoor:
                            p.Value.UpdateState(Locomotive.DoorLeftOpen, elapsedTime);
                            break;
                        case CabViewControlType.RightDoor:
                            p.Value.UpdateState(Locomotive.DoorRightOpen, elapsedTime);
                            break;
                        case CabViewControlType.Mirrors:
                            p.Value.UpdateState(Locomotive.MirrorOpen, elapsedTime);
                            break;
                        default:
                            break;
                    }
                }
                else p.Value.Update(this.LocoViewer, elapsedTime); //for all other intruments with animations
            }
            foreach (var p in DigitParts3D)
            {
                p.Value.PrepareFrame(frame, elapsedTime);
            }
            foreach (var p in Gauges)
            {
                p.Value.PrepareFrame(frame, elapsedTime);
            }

            if (ExternalWipers != null) ExternalWipers.UpdateLoop(Locomotive.Wiper, elapsedTime);
            /*
            foreach (var p in DigitParts)
            {
                p.Value.PrepareFrame(frame, elapsedTime);
            }*/ //removed with 3D digits

            if (TrainCarShape != null)
                TrainCarShape.PrepareFrame(frame, elapsedTime);
        }

        internal override void Mark()
        {
            // TODO: This is likely wrong; we should mark textures, shapes and other graphical resources here.
        }

        public override void HandleUserInput(in ElapsedTime elapsedTime)
        {
        }

        public override void RegisterUserCommandHandling()
        {
        }

        public override void UnregisterUserCommandHandling()
        {
        }

    } // Class ThreeDimentionCabViewer

    public class ThreeDimCabDigit
    {
        private const int MaxDigits = 6;
        private PoseableShape TrainCarShape;
        private VertexPositionNormalTexture[] VertexList;
        private int NumVertices;
        private int NumIndices;
        public short[] TriangleListIndices;// Array of indices to vertices for triangles
        private Matrix XNAMatrix;
        private Viewer Viewer;
        private MutableShapePrimitive shapePrimitive;
        private CabViewDigitalRenderer CVFR;
        private Material Material;
        private Material AlertMaterial;
        private float Size;
        private string AceFile;
        public ThreeDimCabDigit(Viewer viewer, int iMatrix, string size, string aceFile, PoseableShape trainCarShape, CabViewControlRenderer c)
        {

            Size = int.Parse(size) * 0.001f;//input size is in mm
            if (aceFile != "")
            {
                AceFile = aceFile.ToUpper();
                if (!AceFile.EndsWith(".ACE")) AceFile = AceFile + ".ACE"; //need to add ace into it
            }
            else { AceFile = ""; }

            CVFR = (CabViewDigitalRenderer)c;
            Viewer = viewer;
            TrainCarShape = trainCarShape;
            XNAMatrix = TrainCarShape.SharedShape.Matrices[iMatrix];
            var maxVertex = 32;// every face has max 5 digits, each has 2 triangles
                               //Material = viewer.MaterialManager.Load("Scenery", Helpers.GetRouteTextureFile(viewer.Simulator, Helpers.TextureFlags.None, texture), (int)(SceneryMaterialOptions.None | SceneryMaterialOptions.AlphaBlendingBlend), 0);
            Material = FindMaterial(false);//determine normal material
                                           // Create and populate a new ShapePrimitive
            NumVertices = NumIndices = 0;

            VertexList = new VertexPositionNormalTexture[maxVertex];
            TriangleListIndices = new short[maxVertex / 2 * 3]; // as is NumIndices

            //start position is the center of the text
            var start = new Vector3(0, 0, 0);
            var rotation = 0;

            //find the left-most of text
            Vector3 offset;

            offset.X = 0;

            offset.Y = -Size;

            var speed = new string('0', MaxDigits);
            foreach (char ch in speed)
            {
                var tX = GetTextureCoordX(ch);
                var tY = GetTextureCoordY(ch);
                var rot = Matrix.CreateRotationY(-rotation);

                //the left-bottom vertex
                Vector3 v = new Vector3(offset.X, offset.Y, 0.01f);
                v = Vector3.Transform(v, rot);
                v += start; Vertex v1 = new Vertex(v.X, v.Y, v.Z, 0, 0, -1, tX, tY);

                //the right-bottom vertex
                v.X = offset.X + Size; v.Y = offset.Y; v.Z = 0.01f;
                v = Vector3.Transform(v, rot);
                v += start; Vertex v2 = new Vertex(v.X, v.Y, v.Z, 0, 0, -1, tX + 0.25f, tY);

                //the right-top vertex
                v.X = offset.X + Size; v.Y = offset.Y + Size; v.Z = 0.01f;
                v = Vector3.Transform(v, rot);
                v += start; Vertex v3 = new Vertex(v.X, v.Y, v.Z, 0, 0, -1, tX + 0.25f, tY - 0.25f);

                //the left-top vertex
                v.X = offset.X; v.Y = offset.Y + Size; v.Z = 0.01f;
                v = Vector3.Transform(v, rot);
                v += start; Vertex v4 = new Vertex(v.X, v.Y, v.Z, 0, 0, -1, tX, tY - 0.25f);

                //create first triangle
                TriangleListIndices[NumIndices++] = (short)NumVertices;
                TriangleListIndices[NumIndices++] = (short)(NumVertices + 2);
                TriangleListIndices[NumIndices++] = (short)(NumVertices + 1);
                // Second triangle:
                TriangleListIndices[NumIndices++] = (short)NumVertices;
                TriangleListIndices[NumIndices++] = (short)(NumVertices + 3);
                TriangleListIndices[NumIndices++] = (short)(NumVertices + 2);

                //create vertex
                VertexList[NumVertices].Position = v1.Position; VertexList[NumVertices].Normal = v1.Normal; VertexList[NumVertices].TextureCoordinate = v1.TexCoord;
                VertexList[NumVertices + 1].Position = v2.Position; VertexList[NumVertices + 1].Normal = v2.Normal; VertexList[NumVertices + 1].TextureCoordinate = v2.TexCoord;
                VertexList[NumVertices + 2].Position = v3.Position; VertexList[NumVertices + 2].Normal = v3.Normal; VertexList[NumVertices + 2].TextureCoordinate = v3.TexCoord;
                VertexList[NumVertices + 3].Position = v4.Position; VertexList[NumVertices + 3].Normal = v4.Normal; VertexList[NumVertices + 3].TextureCoordinate = v4.TexCoord;
                NumVertices += 4;
                offset.X += Size * 0.8f; offset.Y += 0; //move to next digit
            }

            //create the shape primitive
            shapePrimitive = new MutableShapePrimitive(viewer.RenderProcess.GraphicsDevice, Material, NumVertices, NumIndices, new[] { -1 }, 0);
            UpdateShapePrimitive();

        }

        private Material FindMaterial(bool Alert)
        {
            string imageName = "";
            string globalText = Viewer.Simulator.BasePath + @"\GLOBAL\TEXTURES\";
            CabViewControlType controltype = CVFR.GetControlType();
            Material material = null;

            if (AceFile != "")
            {
                imageName = AceFile;
            }
            else if (Alert) { imageName = "alert.ace"; }
            else
            {
                switch (controltype)
                {
                    case CabViewControlType.Clock:
                        imageName = "clock.ace";
                        break;
                    case CabViewControlType.SpeedLimit:
                    case CabViewControlType.SpeedLim_Display:
                        imageName = "speedlim.ace";
                        break;
                    case CabViewControlType.Speed_Projected:
                    case CabViewControlType.Speedometer:
                    default:
                        imageName = "speed.ace";
                        break;
                }
            }

            SceneryMaterialOptions options = SceneryMaterialOptions.ShaderFullBright | SceneryMaterialOptions.AlphaBlendingAdd | SceneryMaterialOptions.UndergroundTexture;

            if (String.IsNullOrEmpty(TrainCarShape.SharedShape.ReferencePath))
            {
                if (!File.Exists(globalText + imageName))
                {
                    Trace.TraceInformation("Ignored missing " + imageName + " using default. You can copy the " + imageName + " from OR\'s AddOns folder to " + globalText +
                        ", or place it under " + TrainCarShape.SharedShape.ReferencePath);
                }
                material = Viewer.MaterialManager.Load("Scenery", Helpers.GetTextureFile(Viewer.Simulator, Helpers.TextureFlags.None, globalText, imageName), (int)(options), 0);
            }
            else
            {
                if (!File.Exists(TrainCarShape.SharedShape.ReferencePath + @"\" + imageName))
                {
                    Trace.TraceInformation("Ignored missing " + imageName + " using default. You can copy the " + imageName + " from OR\'s AddOns folder to " + globalText +
                        ", or place it under " + TrainCarShape.SharedShape.ReferencePath);
                    material = Viewer.MaterialManager.Load("Scenery", Helpers.GetTextureFile(Viewer.Simulator, Helpers.TextureFlags.None, globalText, imageName), (int)(options), 0);
                }
                else material = Viewer.MaterialManager.Load("Scenery", Helpers.GetTextureFile(Viewer.Simulator, Helpers.TextureFlags.None, TrainCarShape.SharedShape.ReferencePath + @"\", imageName), (int)(options), 0);
            }

            return material;
            //Material = Viewer.MaterialManager.Load("Scenery", Helpers.GetRouteTextureFile(Viewer.Simulator, Helpers.TextureFlags.None, "Speed"), (int)(SceneryMaterialOptions.None | SceneryMaterialOptions.AlphaBlendingBlend), 0);
        }

        //update the digits with current speed or time
        public void UpdateDigit()
        {
            NumVertices = NumIndices = 0;

            Material UsedMaterial = Material; //use default material

            //update text string
            bool Alert;
            string speed = CVFR.Get3DDigits(out Alert);

            if (Alert)//alert use alert meterial
            {
                if (AlertMaterial == null) AlertMaterial = FindMaterial(true);
                UsedMaterial = AlertMaterial;
            }
            //update vertex texture coordinate
            foreach (char ch in speed.Substring(0, Math.Min(speed.Length, MaxDigits)))
            {
                var tX = GetTextureCoordX(ch);
                var tY = GetTextureCoordY(ch);
                //create first triangle
                TriangleListIndices[NumIndices++] = (short)NumVertices;
                TriangleListIndices[NumIndices++] = (short)(NumVertices + 2);
                TriangleListIndices[NumIndices++] = (short)(NumVertices + 1);
                // Second triangle:
                TriangleListIndices[NumIndices++] = (short)NumVertices;
                TriangleListIndices[NumIndices++] = (short)(NumVertices + 3);
                TriangleListIndices[NumIndices++] = (short)(NumVertices + 2);

                VertexList[NumVertices].TextureCoordinate.X = tX; VertexList[NumVertices].TextureCoordinate.Y = tY;
                VertexList[NumVertices + 1].TextureCoordinate.X = tX + 0.25f; VertexList[NumVertices + 1].TextureCoordinate.Y = tY;
                VertexList[NumVertices + 2].TextureCoordinate.X = tX + 0.25f; VertexList[NumVertices + 2].TextureCoordinate.Y = tY - 0.25f;
                VertexList[NumVertices + 3].TextureCoordinate.X = tX; VertexList[NumVertices + 3].TextureCoordinate.Y = tY - 0.25f;
                NumVertices += 4;
            }

            //update the shape primitive
            UpdateShapePrimitive();

        }

        private void UpdateShapePrimitive()
        {
            var indexData = new short[NumIndices];
            Array.Copy(TriangleListIndices, indexData, NumIndices);
            shapePrimitive.SetIndexData(indexData);

            var vertexData = new VertexPositionNormalTexture[NumVertices];
            Array.Copy(VertexList, vertexData, NumVertices);
            shapePrimitive.SetVertexData(vertexData, 0, NumVertices, NumIndices / 3);
        }

        //ACE MAP:
        // 0 1 2 3 
        // 4 5 6 7
        // 8 9 : 
        // . - a p
        private static float GetTextureCoordX(char c)
        {
            float x = (c - '0') % 4 * 0.25f;
            if (c == '.') x = 0;
            else if (c == ':') x = 0.5f;
            else if (c == ' ') x = 0.75f;
            else if (c == '-') x = 0.25f;
            else if (c == 'a') x = 0.5f; //AM
            else if (c == 'p') x = 0.75f; //PM
            if (x < 0) x = 0;
            if (x > 1) x = 1;
            return x;
        }

        private static float GetTextureCoordY(char c)
        {
            if (c == '0' || c == '1' || c == '2' || c == '3') return 0.25f;
            if (c == '4' || c == '5' || c == '6' || c == '7') return 0.5f;
            if (c == '8' || c == '9' || c == ':' || c == ' ') return 0.75f;
            return 1.0f;
        }

        public void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            UpdateDigit();

            Matrix mx = MatrixExtension.ChangeTranslation(TrainCarShape.WorldPosition.XNAMatrix,
                (TrainCarShape.WorldPosition.TileX - Viewer.Camera.TileX) * 2048,
                0,
                (-TrainCarShape.WorldPosition.TileZ + Viewer.Camera.TileZ) * 2048);
            MatrixExtension.Multiply(XNAMatrix, mx, out Matrix m);
            // TODO: Make this use AddAutoPrimitive instead.
            frame.AddPrimitive(this.shapePrimitive.Material, this.shapePrimitive, RenderPrimitiveGroup.Interior, ref m, ShapeFlags.None);
        }

        internal void Mark()
        {
            shapePrimitive.Mark();
        }
    } // class ThreeDimCabDigit

    public class ThreeDimCabGaugeNative
    {
        private PoseableShape TrainCarShape;
        private VertexPositionNormalTexture[] VertexList;
        private int NumVertices;
        private int NumIndices;
        public short[] TriangleListIndices;// Array of indices to vertices for triangles
        private Matrix XNAMatrix;
        private Viewer Viewer;
        private MutableShapePrimitive shapePrimitive;
        private CabViewGaugeRenderer CVFR;
        private Material PositiveMaterial;
        private Material NegativeMaterial;
        private float width, maxLen; //width of the gauge, and the max length of the gauge
        private int Direction, Orientation;
        public ThreeDimCabGaugeNative(Viewer viewer, int iMatrix, string size, string len, PoseableShape trainCarShape, CabViewControlRenderer c)
        {
            if (size != string.Empty) width = float.Parse(size) / 1000f; //in mm
            if (len != string.Empty) maxLen = float.Parse(len) / 1000f; //in mm

            CVFR = (CabViewGaugeRenderer)c;
            Direction = CVFR.GetGauge().Direction;
            Orientation = CVFR.GetGauge().Orientation;

            Viewer = viewer;
            TrainCarShape = trainCarShape;
            XNAMatrix = TrainCarShape.SharedShape.Matrices[iMatrix];
            CabViewGaugeControl gauge = CVFR.GetGauge();
            var maxVertex = 4;// a rectangle
                              //Material = viewer.MaterialManager.Load("Scenery", Helpers.GetRouteTextureFile(viewer.Simulator, Helpers.TextureFlags.None, texture), (int)(SceneryMaterialOptions.None | SceneryMaterialOptions.AlphaBlendingBlend), 0);

            // Create and populate a new ShapePrimitive
            NumVertices = NumIndices = 0;
            var Size = gauge.Bounds.Width;

            VertexList = new VertexPositionNormalTexture[maxVertex];
            TriangleListIndices = new short[maxVertex / 2 * 3]; // as is NumIndices

            var tX = 1f; var tY = 1f;

            //the left-bottom vertex
            Vertex v1 = new Vertex(0f, 0f, 0.002f, 0, 0, -1, tX, tY);

            //the right-bottom vertex
            Vertex v2 = new Vertex(0f, Size, 0.002f, 0, 0, -1, tX, tY);

            Vertex v3 = new Vertex(Size, 0, 0.002f, 0, 0, -1, tX, tY);

            Vertex v4 = new Vertex(Size, Size, 0.002f, 0, 0, -1, tX, tY);

            //create first triangle
            TriangleListIndices[NumIndices++] = (short)NumVertices;
            TriangleListIndices[NumIndices++] = (short)(NumVertices + 1);
            TriangleListIndices[NumIndices++] = (short)(NumVertices + 2);
            // Second triangle:
            TriangleListIndices[NumIndices++] = (short)NumVertices;
            TriangleListIndices[NumIndices++] = (short)(NumVertices + 2);
            TriangleListIndices[NumIndices++] = (short)(NumVertices + 3);

            //create vertex
            VertexList[NumVertices].Position = v1.Position; VertexList[NumVertices].Normal = v1.Normal; VertexList[NumVertices].TextureCoordinate = v1.TexCoord;
            VertexList[NumVertices + 1].Position = v2.Position; VertexList[NumVertices + 1].Normal = v2.Normal; VertexList[NumVertices + 1].TextureCoordinate = v2.TexCoord;
            VertexList[NumVertices + 2].Position = v3.Position; VertexList[NumVertices + 2].Normal = v3.Normal; VertexList[NumVertices + 2].TextureCoordinate = v3.TexCoord;
            VertexList[NumVertices + 3].Position = v4.Position; VertexList[NumVertices + 3].Normal = v4.Normal; VertexList[NumVertices + 3].TextureCoordinate = v4.TexCoord;
            NumVertices += 4;


            //create the shape primitive
            shapePrimitive = new MutableShapePrimitive(viewer.RenderProcess.GraphicsDevice, FindMaterial(), NumVertices, NumIndices, new[] { -1 }, 0);
            UpdateShapePrimitive();

        }

        private Material FindMaterial()
        {
            bool Positive;
            Color c = this.CVFR.GetColor(out Positive);
            if (Positive)
            {
                if (PositiveMaterial == null)
                {
                    PositiveMaterial = new SolidColorMaterial(this.Viewer, 0f, c.R, c.G, c.B);
                }
                return PositiveMaterial;
            }
            else
            {
                if (NegativeMaterial == null) NegativeMaterial = new SolidColorMaterial(this.Viewer, c.A, c.R, c.G, c.B);
                return NegativeMaterial;
            }
        }

        //update the digits with current speed or time
        public void UpdateDigit()
        {
            NumVertices = 0;

            Material UsedMaterial = FindMaterial();

            float length = CVFR.GetRangeFraction();

            CabViewGaugeControl gauge = CVFR.GetGauge();

            var len = maxLen * length;
            Vertex v1, v2, v3, v4;

            //the left-bottom vertex if ori=0;dir=0, right-bottom if ori=0,dir=1; left-top if ori=1,dir=0; left-bottom if ori=1,dir=1;
            v1 = new Vertex(0f, 0f, 0.002f, 0, 0, -1, 0f, 0f);

            if (Orientation == 0)
            {
                if (Direction == 0)//moving right
                {
                    //other vertices
                    v2 = new Vertex(0f, width, 0.002f, 0, 0, 1, 0f, 0f);
                    v3 = new Vertex(len, width, 0.002f, 0, 0, 1, 0f, 0f);
                    v4 = new Vertex(len, 0f, 0.002f, 0, 0, 1, 0f, 0f);
                }
                else //moving left
                {
                    v4 = new Vertex(0f, width, 0.002f, 0, 0, 1, 0f, 0f);
                    v3 = new Vertex(-len, width, 0.002f, 0, 0, 1, 0f, 0f);
                    v2 = new Vertex(-len, 0f, 0.002f, 0, 0, 1, 0f, 0f);
                }
            }
            else
            {
                if (Direction == 1)//up
                {
                    //other vertices
                    v2 = new Vertex(0f, len, 0.002f, 0, 0, 1, 0f, 0f);
                    v3 = new Vertex(width, len, 0.002f, 0, 0, 1, 0f, 0f);
                    v4 = new Vertex(width, 0f, 0.002f, 0, 0, 1, 0f, 0f);
                }
                else //moving down
                {
                    v4 = new Vertex(0f, -len, 0.002f, 0, 0, 1, 0f, 0f);
                    v3 = new Vertex(width, -len, 0.002f, 0, 0, 1, 0f, 0f);
                    v2 = new Vertex(width, 0, 0.002f, 0, 0, 1, 0f, 0f);
                }
            }

            //create vertex list
            VertexList[NumVertices].Position = v1.Position; VertexList[NumVertices].Normal = v1.Normal; VertexList[NumVertices].TextureCoordinate = v1.TexCoord;
            VertexList[NumVertices + 1].Position = v2.Position; VertexList[NumVertices + 1].Normal = v2.Normal; VertexList[NumVertices + 1].TextureCoordinate = v2.TexCoord;
            VertexList[NumVertices + 2].Position = v3.Position; VertexList[NumVertices + 2].Normal = v3.Normal; VertexList[NumVertices + 2].TextureCoordinate = v3.TexCoord;
            VertexList[NumVertices + 3].Position = v4.Position; VertexList[NumVertices + 3].Normal = v4.Normal; VertexList[NumVertices + 3].TextureCoordinate = v4.TexCoord;
            NumVertices += 4;

            //update the shape primitive
            UpdateShapePrimitive();

        }


        //ACE MAP:
        // 0 1 2 3 
        // 4 5 6 7
        // 8 9 : 
        // . - a p
        private static float GetTextureCoordX(char c)
        {
            float x = (c - '0') % 4 * 0.25f;
            if (c == '.') x = 0;
            else if (c == ':') x = 0.5f;
            else if (c == ' ') x = 0.75f;
            else if (c == '-') x = 0.25f;
            else if (c == 'a') x = 0.5f; //AM
            else if (c == 'p') x = 0.75f; //PM
            if (x < 0) x = 0;
            if (x > 1) x = 1;
            return x;
        }

        private static float GetTextureCoordY(char c)
        {
            if (c == '0' || c == '1' || c == '2' || c == '3') return 0.25f;
            if (c == '4' || c == '5' || c == '6' || c == '7') return 0.5f;
            if (c == '8' || c == '9' || c == ':' || c == ' ') return 0.75f;
            return 1.0f;
        }

        private void UpdateShapePrimitive()
        {
            var indexData = new short[NumIndices];
            Array.Copy(TriangleListIndices, indexData, NumIndices);
            shapePrimitive.SetIndexData(indexData);

            var vertexData = new VertexPositionNormalTexture[NumVertices];
            Array.Copy(VertexList, vertexData, NumVertices);
            shapePrimitive.SetVertexData(vertexData, 0, NumVertices, NumIndices / 3);
        }

        public void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            UpdateDigit();
            Matrix mx = MatrixExtension.ChangeTranslation(TrainCarShape.WorldPosition.XNAMatrix,
                (TrainCarShape.WorldPosition.TileX - Viewer.Camera.TileX) * 2048,
                0,
                (-TrainCarShape.WorldPosition.TileZ + Viewer.Camera.TileZ) * 2048);
            MatrixExtension.Multiply(XNAMatrix, mx, out Matrix m);

            // TODO: Make this use AddAutoPrimitive instead.
            frame.AddPrimitive(this.shapePrimitive.Material, this.shapePrimitive, RenderPrimitiveGroup.Interior, ref m, ShapeFlags.None);
        }

        internal void Mark()
        {
            shapePrimitive.Mark();
        }
    } // class ThreeDimCabDigit
    public class ThreeDimCabGauge
    {
        private PoseableShape TrainCarShape;
        public short[] TriangleListIndices;// Array of indices to vertices for triangles
        private Viewer Viewer;
        private int matrixIndex;
        private CabViewGaugeRenderer CVFR;
        private Matrix XNAMatrix;
        private float GaugeSize;
        public ThreeDimCabGauge(Viewer viewer, int iMatrix, float gaugeSize, PoseableShape trainCarShape, CabViewControlRenderer c)
        {
            CVFR = (CabViewGaugeRenderer)c;
            Viewer = viewer;
            TrainCarShape = trainCarShape;
            matrixIndex = iMatrix;
            XNAMatrix = TrainCarShape.SharedShape.Matrices[iMatrix];
            GaugeSize = gaugeSize / 1000f; //how long is the scale 1? since OR cannot allow fraction number in part names, have to define it as mm
        }



        /// <summary>
        /// Transition the part toward the specified state. 
        /// </summary>
        public void Update(MSTSLocomotiveViewer locoViewer, in ElapsedTime elapsedTime)
        {
            if (!locoViewer.Has3DCabRenderer) return;

            var scale = CVFR.GetRangeFraction();

            if (CVFR.GetStyle() == CabViewControlStyle.Pointer)
            {
                this.TrainCarShape.XNAMatrices[matrixIndex] = Matrix.CreateTranslation(scale * this.GaugeSize, 0, 0) * this.TrainCarShape.SharedShape.Matrices[matrixIndex];
            }
            else
            {
                this.TrainCarShape.XNAMatrices[matrixIndex] = Matrix.CreateScale(scale * 10, 1, 1) * this.TrainCarShape.SharedShape.Matrices[matrixIndex];
            }
            //this.TrainCarShape.SharedShape.Matrices[matrixIndex] = XNAMatrix * mx * Matrix.CreateRotationX(10);
        }

    } // class ThreeDimCabGauge

    public class DigitalDisplay
    {
        private Viewer Viewer;
        private SpriteBatchMaterial _Sprite2DCabView;
        private WindowTextFont _Font;
        private PoseableShape TrainCarShape = null;
        private int digitPart;
        private int height;
        private Point coor = new Point(0, 0);
        private CabViewDigitalRenderer CVFR;
        //		Color color;
        public DigitalDisplay(Viewer viewer, PoseableShape t, int d, int h, CabViewControlRenderer c)
        {
            TrainCarShape = t;
            Viewer = viewer;
            digitPart = d;
            height = h;
            CVFR = (CabViewDigitalRenderer)c;
            _Sprite2DCabView = (SpriteBatchMaterial)viewer.MaterialManager.Load("SpriteBatch");
            _Font = viewer.WindowManager.TextManager.GetExact("Arial", height, System.Drawing.FontStyle.Regular);
        }

    }

    public class TextPrimitive : RenderPrimitive
    {
        public readonly SpriteBatchMaterial Material;
        public Point Position;
        public Color Color;
        public readonly WindowTextFont Font;
        public string Text;

        public TextPrimitive(SpriteBatchMaterial material, Point position, Color color, WindowTextFont font)
        {
            Material = material;
            Position = position;
            Color = color;
            Font = font;
        }

        public override void Draw()
        {
            Font.Draw(Material.SpriteBatch, Position, Text, Color);
        }
    }


    // This supports animation of Pantographs, Mirrors and Doors - any up/down on/off 2 state types
    // It is initialized with a list of indexes for the matrices related to this part
    // On Update( position ) it slowly moves the parts towards the specified position
    public class AnimatedPartMultiState : AnimatedPart
    {
        public CabViewControlType Type;
        public int Key;
        /// <summary>
        /// Construct with a link to the shape that contains the animated parts 
        /// </summary>
        public AnimatedPartMultiState(PoseableShape poseableShape, CabViewControlType t, int k)
            : base(poseableShape)
        {
            Type = t;
            Key = k;
        }

        /// <summary>
        /// Transition the part toward the specified state. 
        /// </summary>
        public void Update(MSTSLocomotiveViewer locoViewer, in ElapsedTime elapsedTime)
        {
            if (MatrixIndexes.Count == 0 || !locoViewer.Has3DCabRenderer)
                return;

            if (locoViewer.CabRenderer3D.ControlMap.TryGetValue(Key, out CabViewControlRenderer cvfr))
            {
                float index = cvfr is CabViewDiscreteRenderer renderer ? renderer.GetDrawIndex() : cvfr.GetRangeFraction() * FrameCount;
                SetFrameClamp(index);
            }
        }
    }
}
