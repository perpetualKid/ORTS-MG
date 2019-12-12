// COPYRIGHT 2009, 2010, 2011, 2012, 2013 by the Open Rails project.
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

/* ELECTRIC LOCOMOTIVE CLASSES
 * 
 * The locomotive is represented by two classes:
 *  ...Simulator - defines the behaviour, ie physics, motion, power generated etc
 *  ...Viewer - defines the appearance in a 3D viewer
 * 
 * The ElectricLocomotive classes add to the basic behaviour provided by:
 *  LocomotiveSimulator - provides for movement, throttle controls, direction controls etc
 *  LocomotiveViewer - provides basic animation for running gear, wipers, etc
 * 
 */

using System.Diagnostics;
using System.IO;
using System.Text;

using Orts.Common;
using Orts.Formats.Msts;
using Orts.Formats.Msts.Models;
using Orts.Formats.Msts.Parsers;
using Orts.Simulation.RollingStocks.SubSystems.PowerSupplies;

namespace Orts.Simulation.RollingStocks
{
    ///////////////////////////////////////////////////
    ///   SIMULATION BEHAVIOUR
    ///////////////////////////////////////////////////


    /// <summary>
    /// Adds pantograph control to the basic LocomotiveSimulator functionality
    /// </summary>
    public class MSTSElectricLocomotive : MSTSLocomotive
    {
        public ScriptedElectricPowerSupply PowerSupply;

        public MSTSElectricLocomotive(Simulator simulator, string wagFile) :
            base(simulator, wagFile)
        {
            PowerSupply = new ScriptedElectricPowerSupply(this);
        }

        /// <summary>
        /// Parse the wag file parameters required for the simulator and viewer classes
        /// </summary>
        public override void Parse(string lowercasetoken, STFReader stf)
        {
            switch (lowercasetoken)
            {
                case "engine(ortspowerondelay":
                case "engine(ortsauxpowerondelay":
                case "engine(ortspowersupply":
                case "engine(ortscircuitbreaker":
                case "engine(ortscircuitbreakerclosingdelay":
                    PowerSupply.Parse(lowercasetoken, stf);
                    break;

                default:
                    base.Parse(lowercasetoken, stf);
                    break;
            }
        }

        /// <summary>
        /// This initializer is called when we are making a new copy of a car already
        /// loaded in memory.  We use this one to speed up loading by eliminating the
        /// need to parse the wag file multiple times.
        /// NOTE:  you must initialize all the same variables as you parsed above
        /// </summary>
        public override void Copy(MSTSWagon copy)
        {
            base.Copy(copy);  // each derived level initializes its own variables

            // for example
            //CabSoundFileName = locoCopy.CabSoundFileName;
            //CVFFileName = locoCopy.CVFFileName;
            MSTSElectricLocomotive locoCopy = (MSTSElectricLocomotive)copy;

            PowerSupply.Copy(locoCopy.PowerSupply);
        }

        /// <summary>
        /// We are saving the game.  Save anything that we'll need to restore the 
        /// status later.
        /// </summary>
        public override void Save(BinaryWriter outf)
        {
            PowerSupply.Save(outf);

            base.Save(outf);
        }

        /// <summary>
        /// We are restoring a saved game.  The TrainCar class has already
        /// been initialized.   Restore the game state.
        /// </summary>
        public override void Restore(BinaryReader inf)
        {
            PowerSupply.Restore(inf);

            base.Restore(inf);
        }

        public override void Initialize()
        {
            if (!PowerSupply.RouteElectrified)
                Trace.WriteLine("Warning: The route is not electrified. Electric driven trains will not run!");

            PowerSupply.Initialize();

            base.Initialize();

            // If DrvWheelWeight is not in ENG file, then calculate drivewheel weight freom FoA

            if (DrvWheelWeightKg == 0) // if DrvWheelWeightKg not in ENG file.
            {
                DrvWheelWeightKg = MassKG; // set Drive wheel weight to total wagon mass if not in ENG file
            }
        }

        //================================================================================================//
        /// <summary>
        /// Initialization when simulation starts with moving train
        /// <\summary>
        /// 
        public override void InitializeMoving()
        {
            base.InitializeMoving();
            WheelSpeedMpS = SpeedMpS;
            DynamicBrakePercent = -1;
            ThrottleController.SetValue(Train.MUThrottlePercent / 100);

            Pantographs.InitializeMoving();
            PowerSupply.InitializeMoving();
        }

        /// <summary>
        /// This function updates periodically the states and physical variables of the locomotive's power supply.
        /// </summary>
        protected override void UpdatePowerSupply(double elapsedClockSeconds)
        {
            PowerSupply.Update(elapsedClockSeconds);
        }

        /// <summary>
        /// This function updates periodically the locomotive's sound variables.
        /// </summary>
        protected override void UpdateSoundVariables(double elapsedClockSeconds)
        {
            Variable1 = ThrottlePercent;
            if (ThrottlePercent == 0f) Variable2 = 0;
            else
            {
                float dV2;
                dV2 = TractiveForceN / MaxForceN * 100f - Variable2;
                float max = 2f;
                if (dV2 > max) dV2 = max;
                else if (dV2 < -max) dV2 = -max;
                Variable2 += dV2;
            }
            if (DynamicBrakePercent > 0)
                Variable3 = MaxDynamicBrakeForceN == 0 ? DynamicBrakePercent / 100f : DynamicBrakeForceN / MaxDynamicBrakeForceN;
            else
                Variable3 = 0;
        }

        /// <summary>
        /// Used when someone want to notify us of an event
        /// </summary>
        public override void SignalEvent(TrainEvent evt)
        {
            base.SignalEvent(evt);
        }

        public override void SignalEvent(PowerSupplyEvent evt)
        {
            if (Simulator.Confirmer != null && Simulator.PlayerLocomotive == this)
            {
                switch (evt)
                {
                    case PowerSupplyEvent.RaisePantograph:
                        Simulator.Confirmer.Confirm(CabControl.Pantograph1, CabSetting.On);
                        Simulator.Confirmer.Confirm(CabControl.Pantograph2, CabSetting.On);
                        Simulator.Confirmer.Confirm(CabControl.Pantograph3, CabSetting.On);
                        Simulator.Confirmer.Confirm(CabControl.Pantograph4, CabSetting.On);
                        break;

                    case PowerSupplyEvent.LowerPantograph:
                        Simulator.Confirmer.Confirm(CabControl.Pantograph1, CabSetting.Off);
                        Simulator.Confirmer.Confirm(CabControl.Pantograph2, CabSetting.Off);
                        Simulator.Confirmer.Confirm(CabControl.Pantograph3, CabSetting.Off);
                        Simulator.Confirmer.Confirm(CabControl.Pantograph4, CabSetting.Off);
                        break;
                }
            }

            switch (evt)
            {
                case PowerSupplyEvent.CloseCircuitBreaker:
                case PowerSupplyEvent.OpenCircuitBreaker:
                case PowerSupplyEvent.CloseCircuitBreakerButtonPressed:
                case PowerSupplyEvent.CloseCircuitBreakerButtonReleased:
                case PowerSupplyEvent.OpenCircuitBreakerButtonPressed:
                case PowerSupplyEvent.OpenCircuitBreakerButtonReleased:
                case PowerSupplyEvent.GiveCircuitBreakerClosingAuthorization:
                case PowerSupplyEvent.RemoveCircuitBreakerClosingAuthorization:
                    PowerSupply.HandleEvent(evt);
                    break;
            }

            base.SignalEvent(evt);
        }

        public override void SignalEvent(PowerSupplyEvent evt, int id)
        {
            if (Simulator.Confirmer != null && Simulator.PlayerLocomotive == this)
            {
                switch (evt)
                {
                    case PowerSupplyEvent.RaisePantograph:
                        if (id == 1) Simulator.Confirmer.Confirm(CabControl.Pantograph1, CabSetting.On);
                        if (id == 2) Simulator.Confirmer.Confirm(CabControl.Pantograph2, CabSetting.On);
                        if (id == 3) Simulator.Confirmer.Confirm(CabControl.Pantograph3, CabSetting.On);
                        if (id == 4) Simulator.Confirmer.Confirm(CabControl.Pantograph4, CabSetting.On);

                        if (!Simulator.TRK.Route.Electrified)
                            Simulator.Confirmer.Warning(Simulator.Catalog.GetString("No power line!"));
                        if (Simulator.Settings.OverrideNonElectrifiedRoutes)
                            Simulator.Confirmer.Information(Simulator.Catalog.GetString("Power line condition overridden."));
                        break;

                    case PowerSupplyEvent.LowerPantograph:
                        if (id == 1) Simulator.Confirmer.Confirm(CabControl.Pantograph1, CabSetting.Off);
                        if (id == 2) Simulator.Confirmer.Confirm(CabControl.Pantograph2, CabSetting.Off);
                        if (id == 3) Simulator.Confirmer.Confirm(CabControl.Pantograph3, CabSetting.Off);
                        if (id == 4) Simulator.Confirmer.Confirm(CabControl.Pantograph4, CabSetting.Off);
                        break;
                }
            }

            base.SignalEvent(evt, id);
        }

        public override void SetPower(bool ToState)
        {
            if (Train != null)
            {
                if (!ToState)
                    SignalEvent(PowerSupplyEvent.LowerPantograph);
                else
                    SignalEvent(PowerSupplyEvent.RaisePantograph, 1);
            }

            base.SetPower(ToState);
        }

        public override float GetDataOf(CabViewControl cvc)
        {
            float data = 0;

            switch (cvc.ControlType)
            {
                case CabViewControlType.Line_Voltage:
                    data = PowerSupply.PantographVoltageV;
                    if (cvc.ControlUnit == CabViewControlUnit.KiloVolts)
                        data /= 1000;
                    break;

                case CabViewControlType.Panto_Display:
                    data = Pantographs.State == PantographState.Up ? 1 : 0;
                    break;

                case CabViewControlType.Pantograph:
                    data = Pantographs[1].CommandUp ? 1 : 0;
                    break;

                case CabViewControlType.Pantograph2:
                    data = Pantographs[2].CommandUp ? 1 : 0;
                    break;

                case CabViewControlType.Orts_Pantograph3:
                    data = Pantographs.List.Count > 2 && Pantographs[3].CommandUp ? 1 : 0;
                    break;

                case CabViewControlType.Orts_Pantograph4:
                    data = Pantographs.List.Count > 3 && Pantographs[4].CommandUp ? 1 : 0;
                    break;

                case CabViewControlType.Pantographs_4:
                case CabViewControlType.Pantographs_4C:
                    if (Pantographs[1].CommandUp && Pantographs[2].CommandUp)
                        data = 2;
                    else if (Pantographs[1].CommandUp)
                        data = 1;
                    else if (Pantographs[2].CommandUp)
                        data = 3;
                    else
                        data = 0;
                    break;

                case CabViewControlType.Pantographs_5:
                    if (Pantographs[1].CommandUp && Pantographs[2].CommandUp)
                        data = 0; // TODO: Should be 0 if the previous state was Pan2Up, and 4 if that was Pan1Up
                    else if (Pantographs[2].CommandUp)
                        data = 1;
                    else if (Pantographs[1].CommandUp)
                        data = 3;
                    else
                        data = 2;
                    break;

                case CabViewControlType.Orts_Circuit_Breaker_Driver_Closing_Order:
                    data = PowerSupply.CircuitBreaker.DriverClosingOrder ? 1 : 0;
                    break;

                case CabViewControlType.Orts_Circuit_Breaker_Driver_Opening_Order:
                    data = PowerSupply.CircuitBreaker.DriverOpeningOrder ? 1 : 0;
                    break;

                case CabViewControlType.Orts_Circuit_Breaker_Driver_Closing_Authorization:
                    data = PowerSupply.CircuitBreaker.DriverClosingAuthorization ? 1 : 0;
                    break;

                case CabViewControlType.Orts_Circuit_Breaker_State:
                    switch (PowerSupply.CircuitBreaker.State)
                    {
                        case CircuitBreakerState.Open:
                            data = 0;
                            break;
                        case CircuitBreakerState.Closing:
                            data = 1;
                            break;
                        case CircuitBreakerState.Closed:
                            data = 2;
                            break;
                    }
                    break;

                case CabViewControlType.Orts_Circuit_Breaker_Closed:
                    switch (PowerSupply.CircuitBreaker.State)
                    {
                        case CircuitBreakerState.Open:
                        case CircuitBreakerState.Closing:
                            data = 0;
                            break;
                        case CircuitBreakerState.Closed:
                            data = 1;
                            break;
                    }
                    break;

                case CabViewControlType.Orts_Circuit_Breaker_Open:
                    switch (PowerSupply.CircuitBreaker.State)
                    {
                        case CircuitBreakerState.Open:
                        case CircuitBreakerState.Closing:
                            data = 1;
                            break;
                        case CircuitBreakerState.Closed:
                            data = 0;
                            break;
                    }
                    break;

                case CabViewControlType.Orts_Circuit_Breaker_Authorized:
                    data = PowerSupply.CircuitBreaker.ClosingAuthorization ? 1 : 0;
                    break;

                case CabViewControlType.Orts_Circuit_Breaker_Open_And_Authorized:
                    data = (PowerSupply.CircuitBreaker.State < CircuitBreakerState.Closed && PowerSupply.CircuitBreaker.ClosingAuthorization) ? 1 : 0;
                    break;

                default:
                    data = base.GetDataOf(cvc);
                    break;
            }

            return data;
        }

        public override string GetStatus()
        {
            var status = new StringBuilder();
            status.AppendFormat("{0} = ", Simulator.Catalog.GetString("Pantographs"));
            foreach (var pantograph in Pantographs.List)
                status.AppendFormat("{0} ", Simulator.Catalog.GetParticularString("Pantograph", pantograph.State.GetDescription()));
            status.AppendLine();
            status.AppendFormat("{0} = {1}",
                Simulator.Catalog.GetString("Circuit breaker"),
                Simulator.Catalog.GetParticularString("CircuitBreaker", PowerSupply.CircuitBreaker.State.GetDescription()));
            status.AppendLine();
            status.AppendFormat("{0} = {1}",
                Simulator.Catalog.GetParticularString("PowerSupply", "Power"),
                Simulator.Catalog.GetParticularString("PowerSupply", PowerSupply.State.GetDescription()));
            return status.ToString();
        }

        public override string GetDebugStatus()
        {
            var status = new StringBuilder(base.GetDebugStatus());
            status.AppendFormat("\t{0}\t\t", Simulator.Catalog.GetParticularString("CircuitBreaker", PowerSupply.CircuitBreaker.State.GetDescription()));
            status.AppendFormat("{0}\t", PowerSupply.CircuitBreaker.TCSClosingAuthorization ? Simulator.Catalog.GetString("OK") : Simulator.Catalog.GetString("NOT OK"));
            status.AppendFormat("{0}\t", PowerSupply.CircuitBreaker.DriverClosingAuthorization ? Simulator.Catalog.GetString("OK") : Simulator.Catalog.GetString("NOT OK"));
            status.AppendFormat("{0}", Simulator.Catalog.GetParticularString("PowerSupply", PowerSupply.AuxiliaryState.GetDescription()));
            return status.ToString();
        }


    } // class ElectricLocomotive
}
