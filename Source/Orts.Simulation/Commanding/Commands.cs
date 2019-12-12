// COPYRIGHT 2012, 2013, 2014, 2015 by the Open Rails project.
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
using System.Diagnostics;   // Used by Trace.Warnings
using Orts.Common;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;

namespace Orts.Simulation.Commanding
{

    #region base Command class
    [Serializable()]
    public abstract class Command : ICommand
    {
        public double Time { get; set; }

        protected static object CommandReceiver;

        /// <summary>
        /// Each command adds itself to the log when it is constructed.
        /// </summary>
        public Command(CommandLog log)
        {
            log.CommandAdd(this);
        }

        // Method required by ICommand
        public abstract void Redo();

        public override string ToString()
        {
            return GetType().Name;
        }

        protected string ToString(string param)
        {
            return $"{GetType().Name} - {param}";
        }

        protected string ToString(string param, string target)
        {
            return $"{GetType().Name} - {param}, target= {target}";
        }

        // Method required by ICommand
        public virtual void Report()
        {
            Trace.WriteLine($"Command: {FormatStrings.FormatPreciseTime(Time)} {ToString()}");
        }
    }
    #endregion

    // <Superclasses>
    [Serializable()]
    public abstract class BooleanCommand : Command
    {
        protected bool targetState;

        public BooleanCommand(CommandLog log, bool targetState)
            : base(log)
        {
            this.targetState = targetState;
        }
    }

    [Serializable()]
    public abstract class IndexCommand : Command
    {
        protected int index;

        public IndexCommand(CommandLog log, int index)
            : base(log)
        {
            this.index = index;
        }
    }

    /// <summary>
    /// Superclass for continuous commands. Do not create a continuous command until the operation is complete.
    /// </summary>
    [Serializable()]
    public abstract class ContinuousCommand : BooleanCommand
    {
        protected float? target;

        public ContinuousCommand(CommandLog log, bool targetState, float? target, double startTime)
            : base(log, targetState)
        {
            this.target = target;
            Time = startTime;   // Continuous commands are created at end of change, so overwrite time when command was created
        }

        public override string ToString()
        {
            return ToString((targetState ? "increase" : "decrease"), target.ToString());
        }
    }

    [Serializable()]
    public abstract class PausedCommand : Command
    {
        public double PauseDurationS { get; private set; }

        public PausedCommand(CommandLog log, double pauseDurationS)
            : base(log)
        {
            PauseDurationS = pauseDurationS;
        }

        public override string ToString()
        {
            return $"{base.ToString()} Paused Duration: {PauseDurationS}";
        }
    }

    [Serializable()]
    public abstract class CameraCommand : Command
    {
        public CameraCommand(CommandLog log)
            : base(log)
        {
        }
    }

    [Serializable()]
    public sealed class SaveCommand : Command
    {
        public string FileStem { get; private set; }

        public SaveCommand(CommandLog log, string fileStem)
            : base(log)
        {
            FileStem = fileStem;
            Redo();
        }

        public override void Redo()
        {
            // Redo does nothing as SaveCommand is just a marker and saves the fileStem but is not used during replay to redo the save.
            // Report();
        }

        public override string ToString()
        {
            return $"{GetType().Name} to file \"{FileStem}.replay\"";
        }

    }

    // Direction
    [Serializable()]
    public sealed class ReverserCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public ReverserCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            if (targetState)
            {
                Receiver.StartReverseIncrease(null);
            }
            else
            {
                Receiver.StartReverseDecrease(null);
            }
        }

        public override string ToString()
        {
            return ToString(targetState ? "step forward" : "step back");
        }
    }

    [Serializable()]
    public sealed class ContinuousReverserCommand : ContinuousCommand
    {
        public static MSTSSteamLocomotive Receiver { get; set; }

        public ContinuousReverserCommand(CommandLog log, bool targetState, float? target, double startTime)
            : base(log, targetState, target, startTime)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            Receiver.ReverserChangeTo(targetState, target);
        }
    }

    // Power : Raise/lower pantograph
    [Serializable()]
    public sealed class PantographCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        private int item;

        public PantographCommand(CommandLog log, int item, bool targetState)
            : base(log, targetState)
        {
            this.item = item;
            Redo();
        }

        public override void Redo()
        {
            Receiver?.Train?.SignalEvent((targetState ? PowerSupplyEvent.RaisePantograph : PowerSupplyEvent.LowerPantograph), item);
        }

        public override string ToString()
        {
            return ToString(targetState ? "raise" : "lower") + $", item = {item.ToString()}";
        }
    }

    // Power : Close/open circuit breaker
    [Serializable()]
    public sealed class CircuitBreakerClosingOrderCommand : BooleanCommand
    {
        public static MSTSElectricLocomotive Receiver { get; set; }

        public CircuitBreakerClosingOrderCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver?.Train?.SignalEvent(targetState ? PowerSupplyEvent.CloseCircuitBreaker : PowerSupplyEvent.OpenCircuitBreaker);
        }

        public override string ToString()
        {
            return ToString(targetState ? "close" : "open");
        }
    }

    // Power : Close circuit breaker button
    [Serializable()]
    public sealed class CircuitBreakerClosingOrderButtonCommand : BooleanCommand
    {
        public static MSTSElectricLocomotive Receiver { get; set; }

        public CircuitBreakerClosingOrderButtonCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver?.Train?.SignalEvent(targetState ? PowerSupplyEvent.CloseCircuitBreakerButtonPressed : PowerSupplyEvent.CloseCircuitBreakerButtonReleased);
        }

        public override string ToString()
        {
            return ToString(targetState ? "pressed" : "released");
        }
    }

    // Power : Open circuit breaker button
    [Serializable()]
    public sealed class CircuitBreakerOpeningOrderButtonCommand : BooleanCommand
    {
        public static MSTSElectricLocomotive Receiver { get; set; }

        public CircuitBreakerOpeningOrderButtonCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver?.Train?.SignalEvent(targetState ? PowerSupplyEvent.OpenCircuitBreakerButtonPressed : PowerSupplyEvent.OpenCircuitBreakerButtonReleased);
        }

        public override string ToString()
        {
            return ToString(targetState ? "pressed" : "released");
        }
    }

    // Power : Give/remove circuit breaker authorization
    [Serializable()]
    public sealed class CircuitBreakerClosingAuthorizationCommand : BooleanCommand
    {
        public static MSTSElectricLocomotive Receiver { get; set; }

        public CircuitBreakerClosingAuthorizationCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver?.Train?.SignalEvent(targetState ? PowerSupplyEvent.GiveCircuitBreakerClosingAuthorization : PowerSupplyEvent.RemoveCircuitBreakerClosingAuthorization);
        }

        public override string ToString()
        {
            return ToString(targetState ? "given" : "removed");
        }
    }

    // Power
    [Serializable()]
    public sealed class PowerCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public PowerCommand(CommandLog log, MSTSLocomotive receiver, bool targetState)
            : base(log, targetState)
        {
            Receiver = receiver;
            Redo();
        }

        public override void Redo()
        {
            Receiver?.SetPower(targetState);
        }

        public override string ToString()
        {
            return ToString(targetState ? "ON" : "OFF");
        }
    }

    // MU commands connection
    [Serializable()]
    public sealed class ToggleMUCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public ToggleMUCommand(CommandLog log, MSTSLocomotive receiver, bool targetState)
            : base(log, targetState)
        {
            Receiver = receiver;
            Redo();
        }

        public override void Redo()
        {
            Receiver?.ToggleMUCommand(targetState);
        }

        public override string ToString()
        {
            return ToString(targetState ? "ON" : "OFF");
        }
    }

    [Serializable()]
    public sealed class NotchedThrottleCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public NotchedThrottleCommand(CommandLog log, bool targetState) : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.AdjustNotchedThrottle(targetState);
        }

        public override string ToString()
        {
            return ToString(targetState ? "step forward" : "step back");
        }
    }

    [Serializable()]
    public sealed class ContinuousThrottleCommand : ContinuousCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public ContinuousThrottleCommand(CommandLog log, bool targetState, float? target, double startTime)
            : base(log, targetState, target, startTime)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.ThrottleChangeTo(targetState, target);
        }
    }

    // Brakes
    [Serializable()]
    public sealed class TrainBrakeCommand : ContinuousCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public TrainBrakeCommand(CommandLog log, bool targetState, float? target, double startTime)
            : base(log, targetState, target, startTime)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.TrainBrakeChangeTo(targetState, target);
        }
    }

    [Serializable()]
    public sealed class EngineBrakeCommand : ContinuousCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public EngineBrakeCommand(CommandLog log, bool targetState, float? target, double startTime)
            : base(log, targetState, target, startTime)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.EngineBrakeChangeTo(targetState, target);
        }
    }

    [Serializable()]
    public sealed class DynamicBrakeCommand : ContinuousCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public DynamicBrakeCommand(CommandLog log, bool targetState, float? target, double startTime)
            : base(log, targetState, target, startTime)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.DynamicBrakeChangeTo(targetState, target);
        }
    }

    [Serializable()]
    public sealed class InitializeBrakesCommand : Command
    {
        public static Train Receiver { get; set; }

        public InitializeBrakesCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.UnconditionalInitializeBrakes();
        }
    }

    [Serializable()]
    public sealed class EmergencyPushButtonCommand : Command
    {
        public static MSTSLocomotive Receiver { get; set; }

        public EmergencyPushButtonCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.EmergencyButtonPressed = !Receiver.EmergencyButtonPressed;
            Receiver.TrainBrakeController.EmergencyBrakingPushButton = Receiver.EmergencyButtonPressed;
        }
    }

    [Serializable()]
    public sealed class BailOffCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public BailOffCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.SetBailOff(targetState);
        }

        public override string ToString()
        {
            return ToString(targetState ? "disengage" : "engage");
        }
    }

    [Serializable()]
    public sealed class HandbrakeCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public HandbrakeCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.SetTrainHandbrake(targetState);
        }

        public override string ToString()
        {
            return ToString(targetState ? "apply" : "release");
        }
    }

    [Serializable()]
    public sealed class WagonHandbrakeCommand : BooleanCommand
    {
        public static MSTSWagon Receiver { get; set; }

        public WagonHandbrakeCommand(CommandLog log, MSTSWagon car, bool targetState)
            : base(log, targetState)
        {
            Receiver = car;
            Redo();
        }

        public override void Redo()
        {
            Receiver.SetWagonHandbrake(targetState);
        }

        public override string ToString()
        {
            return ToString(targetState ? "apply" : "release");
        }
    }

    [Serializable()]
    public sealed class RetainersCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public RetainersCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.SetTrainRetainers(targetState);
        }

        public override string ToString()
        {
            return ToString(targetState ? "apply" : "release");
        }
    }

    [Serializable()]
    public sealed class BrakeHoseConnectCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public BrakeHoseConnectCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.BrakeHoseConnect(targetState);
        }

        public override string ToString()
        {
            return ToString(targetState ? "connect" : "disconnect");
        }
    }

    [Serializable()]
    public sealed class WagonBrakeHoseConnectCommand : BooleanCommand
    {
        public static MSTSWagon Receiver { get; set; }

        public WagonBrakeHoseConnectCommand(CommandLog log, MSTSWagon car, bool targetState)
            : base(log, targetState)
        {
            Receiver = car;
            Redo();
        }

        public override void Redo()
        {
            Receiver.BrakeSystem.FrontBrakeHoseConnected = targetState;
        }

        public override string ToString()
        {
            return ToString(targetState ? "connect" : "disconnect");
        }
    }

    [Serializable()]
    public sealed class ToggleAngleCockACommand : BooleanCommand
    {
        public static MSTSWagon Receiver { get; set; }

        public ToggleAngleCockACommand(CommandLog log, MSTSWagon car, bool targetState)
            : base(log, targetState)
        {
            Receiver = car;
            Redo();
        }

        public override void Redo()
        {
            Receiver.BrakeSystem.AngleCockAOpen = targetState;
            // Report();
        }

        public override string ToString()
        {
            return ToString(targetState ? "open" : "close");
        }
    }

    [Serializable()]
    public sealed class ToggleAngleCockBCommand : BooleanCommand
    {
        public static MSTSWagon Receiver { get; set; }

        public ToggleAngleCockBCommand(CommandLog log, MSTSWagon car, bool targetState)
            : base(log, targetState)
        {
            Receiver = car;
            Redo();
        }

        public override void Redo()
        {
            Receiver.BrakeSystem.AngleCockBOpen = targetState;
        }

        public override string ToString()
        {
            return ToString(targetState ? "open" : "close");
        }
    }

    [Serializable()]
    public sealed class ToggleBleedOffValveCommand : BooleanCommand
    {
        public static MSTSWagon Receiver { get; set; }

        public ToggleBleedOffValveCommand(CommandLog log, MSTSWagon car, bool targetState)
            : base(log, targetState)
        {
            Receiver = car;
            Redo();
        }

        public override void Redo()
        {
            Receiver.BrakeSystem.BleedOffValveOpen = targetState;
        }

        public override string ToString()
        {
            return ToString(targetState ? "open" : "close");
        }
    }

    [Serializable()]
    public sealed class SanderCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public SanderCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            if (targetState)
            {
                if (!Receiver.Sander)
                    Receiver.Train.SignalEvent(TrainEvent.SanderOn);
            }
            else
            {
                Receiver.Train.SignalEvent(TrainEvent.SanderOff);
            }
        }

        public override string ToString()
        {
            return ToString(targetState ? "on" : "off");
        }
    }

    [Serializable()]
    public sealed class AlerterCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public AlerterCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            if (targetState)
                Receiver.SignalEvent(TrainEvent.VigilanceAlarmReset); // There is no Event.VigilanceAlarmResetReleased
            Receiver.AlerterPressed(targetState);
        }
    }

    [Serializable()]
    public sealed class HornCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public HornCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.ManualHorn = targetState;
            if (targetState)
            {
                Receiver.AlerterReset(TCSEvent.HornActivated);
                Receiver.Simulator.HazzardManager.Horn();
            }
        }

        public override string ToString()
        {
            return ToString(targetState ? "sound" : "off");
        }
    }

    [Serializable()]
    public sealed class BellCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public BellCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.ManualBell = targetState;
        }

        public override string ToString()
        {
            return ToString(targetState ? "ring" : "off");
        }
    }

    [Serializable()]
    public sealed class ToggleCabLightCommand : Command
    {
        public static MSTSLocomotive Receiver { get; set; }

        public ToggleCabLightCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.ToggleCabLight();
        }

        public override string ToString()
        {
            return GetType().Name;
        }
    }

    [Serializable()]
    public sealed class HeadlightCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public HeadlightCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            if (targetState)
            {
                switch (Receiver.Headlight)
                {
                    case 0: Receiver.Headlight = 1; Receiver.Simulator.Confirmer.Confirm(CabControl.Headlight, CabSetting.Neutral); break;
                    case 1: Receiver.Headlight = 2; Receiver.Simulator.Confirmer.Confirm(CabControl.Headlight, CabSetting.On); break;
                }
                Receiver.SignalEvent(TrainEvent.LightSwitchToggle);
            }
            else
            {
                switch (Receiver.Headlight)
                {
                    case 1: Receiver.Headlight = 0; Receiver.Simulator.Confirmer.Confirm(CabControl.Headlight, CabSetting.Off); break;
                    case 2: Receiver.Headlight = 1; Receiver.Simulator.Confirmer.Confirm(CabControl.Headlight, CabSetting.Neutral); break;
                }
                Receiver.SignalEvent(TrainEvent.LightSwitchToggle);
            }
        }
    }

    [Serializable()]
    public sealed class WipersCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public WipersCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.ToggleWipers(targetState);
        }
    }

    [Serializable()]
    public sealed class ToggleDoorsLeftCommand : Command
    {
        public static MSTSWagon Receiver { get; set; }

        public ToggleDoorsLeftCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.GetCabFlipped()) Receiver.ToggleDoorsRight();
            else Receiver.ToggleDoorsLeft();
        }
    }

    [Serializable()]
    public sealed class ToggleDoorsRightCommand : Command
    {
        public static MSTSWagon Receiver { get; set; }

        public ToggleDoorsRightCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.GetCabFlipped()) Receiver.ToggleDoorsLeft();
            else Receiver.ToggleDoorsRight();
        }
    }

    [Serializable()]
    public sealed class ToggleMirrorsCommand : Command
    {
        public static MSTSWagon Receiver { get; set; }

        public ToggleMirrorsCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.ToggleMirrors();
        }
    }

    // Steam controls
    [Serializable()]
    public sealed class ContinuousSteamHeatCommand : ContinuousCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public ContinuousSteamHeatCommand(CommandLog log, int injector, bool targetState, float? target, double startTime)
            : base(log, targetState, target, startTime)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            {
                Receiver.SteamHeatChangeTo(targetState, target);
            }
        }
    }

    [Serializable()]
    public sealed class ContinuousSmallEjectorCommand : ContinuousCommand
    {
        public static MSTSSteamLocomotive Receiver { get; set; }

        public ContinuousSmallEjectorCommand(CommandLog log, int injector, bool targetState, float? target, double startTime)
            : base(log, targetState, target, startTime)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            {
                Receiver.SmallEjectorChangeTo(targetState, target);
            }
        }
    }

    [Serializable()]
    public sealed class ContinuousInjectorCommand : ContinuousCommand
    {
        public static MSTSSteamLocomotive Receiver { get; set; }
        private int injector;

        public ContinuousInjectorCommand(CommandLog log, int injector, bool targetState, float? target, double startTime)
            : base(log, targetState, target, startTime)
        {
            this.injector = injector;
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            switch (injector)
            {
                case 1: { Receiver.Injector1ChangeTo(targetState, target); break; }
                case 2: { Receiver.Injector2ChangeTo(targetState, target); break; }
            }
        }

        public override string ToString()
        {
            return $"Command: {FormatStrings.FormatPreciseTime(Time)} {GetType().Name} {injector.ToString()}"
                + (targetState ? "open" : "close") + ", target = " + target.ToString();
        }
    }

    [Serializable()]
    public sealed class ToggleInjectorCommand : Command
    {
        public static MSTSSteamLocomotive Receiver { get; set; }
        private int injector;

        public ToggleInjectorCommand(CommandLog log, int injector)
            : base(log)
        {
            this.injector = injector;
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            switch (injector)
            {
                case 1: { Receiver.ToggleInjector1(); break; }
                case 2: { Receiver.ToggleInjector2(); break; }
            }
        }

        public override string ToString()
        {
            return base.ToString() + injector.ToString();
        }
    }

    [Serializable()]
    public sealed class ContinuousBlowerCommand : ContinuousCommand
    {
        public static MSTSSteamLocomotive Receiver { get; set; }

        public ContinuousBlowerCommand(CommandLog log, bool targetState, float? target, double startTime)
            : base(log, targetState, target, startTime)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            Receiver.BlowerChangeTo(targetState, target);
        }
    }

    [Serializable()]
    public sealed class ContinuousDamperCommand : ContinuousCommand
    {
        public static MSTSSteamLocomotive Receiver { get; set; }

        public ContinuousDamperCommand(CommandLog log, bool targetState, float? target, double startTime)
            : base(log, targetState, target, startTime)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            Receiver.DamperChangeTo(targetState, target);
        }
    }

    [Serializable()]
    public sealed class ContinuousFireboxDoorCommand : ContinuousCommand
    {
        public static MSTSSteamLocomotive Receiver { get; set; }

        public ContinuousFireboxDoorCommand(CommandLog log, bool targetState, float? target, double startTime)
            : base(log, targetState, target, startTime)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            Receiver.FireboxDoorChangeTo(targetState, target);
        }
    }

    [Serializable()]
    public sealed class ContinuousFiringRateCommand : ContinuousCommand
    {
        public static MSTSSteamLocomotive Receiver { get; set; }

        public ContinuousFiringRateCommand(CommandLog log, bool targetState, float? target, double startTime)
            : base(log, targetState, target, startTime)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            Receiver.FiringRateChangeTo(targetState, target);
        }
    }

    [Serializable()]
    public sealed class ToggleManualFiringCommand : Command
    {
        public static MSTSSteamLocomotive Receiver { get; set; }

        public ToggleManualFiringCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            Receiver.ToggleManualFiring();
        }
    }

    [Serializable()]
    public sealed class AIFireOnCommand : Command
    {
        public static MSTSSteamLocomotive Receiver { get; set; }

        public AIFireOnCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.AIFireOn();

        }

    }

    [Serializable()]
    public sealed class AIFireOffCommand : Command
    {
        public static MSTSSteamLocomotive Receiver { get; set; }

        public AIFireOffCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.AIFireOff();

        }

    }

    [Serializable()]
    public sealed class AIFireResetCommand : Command
    {
        public static MSTSSteamLocomotive Receiver { get; set; }

        public AIFireResetCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.AIFireReset();

        }

    }

    [Serializable()]
    public sealed class FireShovelfullCommand : Command
    {
        public static MSTSSteamLocomotive Receiver { get; set; }

        public FireShovelfullCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            Receiver.FireShovelfull();
        }
    }

    [Serializable()]
    public sealed class ToggleOdometerCommand : Command
    {
        public static MSTSLocomotive Receiver { get; set; }

        public ToggleOdometerCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.OdometerToggle();
        }

        public override string ToString()
        {
            return base.ToString();
        }
    }

    [Serializable()]
    public sealed class ResetOdometerCommand : Command
    {
        public static MSTSLocomotive Receiver { get; set; }

        public ResetOdometerCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.OdometerReset();
        }

        public override string ToString()
        {
            return base.ToString();
        }
    }

    [Serializable()]
    public sealed class ToggleOdometerDirectionCommand : Command
    {
        public static MSTSLocomotive Receiver { get; set; }

        public ToggleOdometerDirectionCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.OdometerToggleDirection();
        }

        public override string ToString()
        {
            return base.ToString();
        }
    }

    [Serializable()]
    public sealed class ToggleWaterScoopCommand : Command
    {
        public static MSTSLocomotive Receiver { get; set; }

        public ToggleWaterScoopCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            Receiver.ToggleWaterScoop();
        }
    }


    [Serializable()]
    public sealed class ToggleCylinderCocksCommand : Command
    {
        public static MSTSSteamLocomotive Receiver { get; set; }

        public ToggleCylinderCocksCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            Receiver.ToggleCylinderCocks();
        }
    }

    // Compound Valve command
    [Serializable()]
    public sealed class ToggleCylinderCompoundCommand : Command
    {
        public static MSTSSteamLocomotive Receiver { get; set; }

        public ToggleCylinderCompoundCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            Receiver.ToggleCylinderCompound();
        }
    }

    // Diesel player engine on / off command
    [Serializable()]
    public sealed class TogglePlayerEngineCommand : Command
    {
        public static MSTSDieselLocomotive Receiver { get; set; }

        public TogglePlayerEngineCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            Receiver.TogglePlayerEngine();
        }
    }

    // Diesel helpers engine on / off command
    [Serializable()]
    public sealed class ToggleHelpersEngineCommand : Command
    {
        public static MSTSLocomotive Receiver { get; set; }

        public ToggleHelpersEngineCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            Receiver.ToggleHelpersEngine();
        }
    }

    // Cab radio switch on-switch off command
    [Serializable()]
    public sealed class CabRadioCommand : BooleanCommand
    {
        public static MSTSLocomotive Receiver { get; set; }

        public CabRadioCommand(CommandLog log, bool targetState)
            : base(log, targetState)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver != null)
            {
                Receiver.ToggleCabRadio(targetState);
            }
        }

        public override string ToString()
        {
            return ToString(targetState ? "switched on" : "switched off");
        }
    }

    [Serializable()]
    public sealed class TurntableClockwiseCommand : Command
    {
        public static MovingTable Receiver { get; set; }
        public TurntableClockwiseCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.StartContinuous(true);
        }

        public override string ToString()
        {
            return ToString(" Clockwise");
        }
    }


    [Serializable()]
    public sealed class TurntableClockwiseTargetCommand : Command
    {
        public static MovingTable Receiver { get; set; }
        public TurntableClockwiseTargetCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.ComputeTarget(true);
        }

        public override string ToString()
        {
            return ToString(" Clockwise with target");
        }
    }

    [Serializable()]
    public sealed class TurntableCounterclockwiseCommand : Command
    {
        public static MovingTable Receiver { get; set; }
        public TurntableCounterclockwiseCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.StartContinuous(false);
        }

        public override string ToString()
        {
            return ToString(" Counterclockwise");
        }
    }


    [Serializable()]
    public sealed class TurntableCounterclockwiseTargetCommand : Command
    {
        public static MovingTable Receiver { get; set; }
        public TurntableCounterclockwiseTargetCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.ComputeTarget(false);
        }

        public override string ToString()
        {
            return ToString(" Counterclockwise with target");
        }
    }

}
