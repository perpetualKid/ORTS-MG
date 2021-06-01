// COPYRIGHT 2009, 2010, 2011, 2012, 2013, 2014 by the Open Rails project.
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
using System.Diagnostics;
using System.IO;

using Orts.Common;
using Orts.Common.Calc;
using Orts.Formats.Msts.Parsers;

namespace Orts.Simulation.RollingStocks.SubSystems.Brakes.MSTS
{
    public class ManualBraking : MSTSBrakeSystem
    {
        private TrainCar Car;
        protected string DebugType = string.Empty;
        private float HandbrakePercent;

        public ManualBraking(TrainCar car)
        {
            Car = car;

        }

        private float ManualMaxBrakeValue = 100.0f;
        private float ManualReleaseRateValuepS;
        private float ManualMaxApplicationRateValuepS;
        private float ManualBrakingDesiredFraction;
        private float EngineBrakeDesiredFraction;
        private float ManualBrakingCurrentFraction;
        private float EngineBrakingCurrentFraction;
        private float SteamBrakeCompensation;
        private bool LocomotiveSteamBrakeFitted = false;
        private float SteamBrakePressurePSI = 0;
        private float SteamBrakeCylinderPressurePSI = 0;
        private float BrakeForceFraction;

        public override bool GetHandbrakeStatus()
        {
            return HandbrakePercent > 0;
        }

        public override void Parse(string lowercasetoken, STFReader stf)
        {
            switch (lowercasetoken)
            {
                case "wagon(maxreleaserate": ManualReleaseRateValuepS = stf.ReadFloatBlock(STFReader.Units.PressureRateDefaultPSIpS, null); break;
                case "wagon(maxapplicationrate": ManualMaxApplicationRateValuepS = stf.ReadFloatBlock(STFReader.Units.PressureRateDefaultPSIpS, null); break;
            }
        }

        public override void InitializeFromCopy(BrakeSystem copy)
        {
            ManualBraking thiscopy = (ManualBraking)copy;
            ManualMaxApplicationRateValuepS = thiscopy.ManualMaxApplicationRateValuepS;
            ManualReleaseRateValuepS = thiscopy.ManualReleaseRateValuepS;

        }

        public override void Save(BinaryWriter outf)
        {
            outf.Write(ManualBrakingCurrentFraction);
        }

        public override void Restore(BinaryReader inf)
        {
            ManualBrakingCurrentFraction = inf.ReadSingle();
        }

        public override void Initialize(bool handbrakeOn, float maxPressurePSI, float fullServPressurePSI, bool immediateRelease)
        {
            if ((Car as MSTSWagon).ManualBrakePresent)
                DebugType = "M";
            else
                DebugType = "-";

            // Changes brake type if locomotive fitted with steam brakes
            if (Car is MSTSSteamLocomotive)
            {
                var locoident = Car as MSTSSteamLocomotive;
                if (locoident.SteamEngineBrakeFitted)
                {
                    DebugType = "S";
                }
            }

            // Changes brake type if tender fitted with steam brakes
            if (Car.WagonType == MSTSWagon.WagonTypes.Tender) 
            {
                var wagonid = Car as MSTSWagon;
                // Find the associated steam locomotive for this tender
                if (wagonid.TendersSteamLocomotive == null) wagonid.FindTendersSteamLocomotive();

                if (wagonid.TendersSteamLocomotive != null)
                {
                    if (wagonid.TendersSteamLocomotive.SteamEngineBrakeFitted) // if steam brakes are fitted to the associated locomotive, then add steam brakes here.
                    {
                        DebugType = "S";
                    }
                }
            }
        }

        public override void Update(double elapsedClockSeconds)
        {
            MSTSLocomotive lead = (MSTSLocomotive)Car.Train.LeadLocomotive;
            float BrakemanBrakeSettingValue = 0;
            float EngineBrakeSettingValue = 0;
            ManualBrakingDesiredFraction = 0;

            SteamBrakeCompensation = 1.0f;

            // Process manual braking on all cars
            if (lead != null)
            {
                BrakemanBrakeSettingValue = lead.BrakemanBrakeController.CurrentValue;
            }

            ManualBrakingDesiredFraction = BrakemanBrakeSettingValue * ManualMaxBrakeValue;

            if (ManualBrakingCurrentFraction < ManualBrakingDesiredFraction)
            {
                ManualBrakingCurrentFraction += ManualMaxApplicationRateValuepS;
                if (ManualBrakingCurrentFraction > ManualBrakingDesiredFraction)
                {
                    ManualBrakingCurrentFraction = ManualBrakingDesiredFraction;
                }

            }
            else if (ManualBrakingCurrentFraction > ManualBrakingDesiredFraction)
            {
                ManualBrakingCurrentFraction -= ManualReleaseRateValuepS;
                if (ManualBrakingCurrentFraction < 0)
                {
                    ManualBrakingCurrentFraction = 0;
                }

            }

            BrakeForceFraction = ManualBrakingCurrentFraction / ManualMaxBrakeValue;
          
            // If car is a locomotive or tender, then process engine brake
            if (Car.WagonType == MSTSWagon.WagonTypes.Engine || Car.WagonType == MSTSWagon.WagonTypes.Tender) // Engine brake
            {
                if (lead != null)
                {
                    EngineBrakeSettingValue = lead.EngineBrakeController.CurrentValue;
                    if (lead.SteamEngineBrakeFitted)
                    {
                        LocomotiveSteamBrakeFitted = true;
                        EngineBrakeDesiredFraction = EngineBrakeSettingValue * lead.MaxBoilerPressurePSI;
                    }
                    else
                    {
                        EngineBrakeDesiredFraction = EngineBrakeSettingValue * ManualMaxBrakeValue;
                    }
              

                    if (EngineBrakingCurrentFraction < EngineBrakeDesiredFraction)
                    {

                        EngineBrakingCurrentFraction += (float)(elapsedClockSeconds * lead.EngineBrakeController.ApplyRatePSIpS);
                        if (EngineBrakingCurrentFraction > EngineBrakeDesiredFraction)
                        {
                            EngineBrakingCurrentFraction = EngineBrakeDesiredFraction;
                        }

                    }
                    else if (EngineBrakingCurrentFraction > EngineBrakeDesiredFraction)
                    {
                        EngineBrakingCurrentFraction -= (float)(elapsedClockSeconds * lead.EngineBrakeController.ReleaseRatePSIpS);
                        if (EngineBrakingCurrentFraction < 0)
                        {
                            EngineBrakingCurrentFraction = 0;
                        }
                    }

                    if (lead.SteamEngineBrakeFitted)
                    {
                        SteamBrakeCompensation = lead.BoilerPressurePSI / lead.MaxBoilerPressurePSI;
                        SteamBrakePressurePSI = EngineBrakeSettingValue * SteamBrakeCompensation * lead.MaxBoilerPressurePSI;
                        SteamBrakeCylinderPressurePSI = EngineBrakingCurrentFraction * SteamBrakeCompensation; // For display purposes
                        BrakeForceFraction = EngineBrakingCurrentFraction / lead.MaxBoilerPressurePSI; // Manual braking value overwritten by engine calculated value
                    }
                    else
                    {
                        BrakeForceFraction = EngineBrakingCurrentFraction / ManualMaxBrakeValue;
                    }
                }
            }

                float f;
            if (!Car.BrakesStuck)
            {
                f = Car.MaxBrakeForceN * Math.Min(BrakeForceFraction, 1);
                if (f < Car.MaxHandbrakeForceN * HandbrakePercent / 100)
                    f = Car.MaxHandbrakeForceN * HandbrakePercent / 100;
            }
            else f = Math.Max(Car.MaxBrakeForceN, Car.MaxHandbrakeForceN / 2);
            Car.BrakeRetardForceN = f * Car.BrakeShoeRetardCoefficientFrictionAdjFactor; // calculates value of force applied to wheel, independent of wheel skid
            if (Car.BrakeSkid) // Test to see if wheels are skiding to excessive brake force
            {
                Car.BrakeForceN = f * Car.SkidFriction;   // if excessive brakeforce, wheel skids, and loses adhesion
            }
            else
            {
                Car.BrakeForceN = f * Car.BrakeShoeCoefficientFrictionAdjFactor; // In advanced adhesion model brake shoe coefficient varies with speed, in simple model constant force applied as per value in WAG file, will vary with wheel skid.
            }

        }

        // Get the brake BC & BP for EOT conditions
        public override string GetStatus(Dictionary<BrakeSystemComponent, Pressure.Unit> units)
        {
            string s = Simulator.Catalog.GetString("Manual Brake");
            return s;
        }

        // Get Brake information for train
        public override string GetFullStatus(BrakeSystem lastCarBrakeSystem, Dictionary<BrakeSystemComponent, Pressure.Unit> units)
        {
            string s = Simulator.Catalog.GetString("Manual Brake");
            return s;
        }


        // This overides the information for each individual wagon in the extended HUD  
        public override string[] GetDebugStatus(Dictionary<BrakeSystemComponent, Pressure.Unit> units)
        {
            // display differently depending upon whether manual brake is present or not

            if ((Car as MSTSWagon).ManualBrakePresent && LocomotiveSteamBrakeFitted)
            {
                return new string[] {
                DebugType,
                $"{FormatStrings.FormatPressure(SteamBrakeCylinderPressurePSI, Pressure.Unit.PSI,  Pressure.Unit.PSI, true):F0}",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty, // Spacer because the state above needs 2 columns.
                (Car as MSTSWagon).HandBrakePresent ? $"{HandbrakePercent:F0}%" : string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                };
            }
            else if ((Car as MSTSWagon).ManualBrakePresent) // Just manual brakes fitted
            {
                return new string[] {
                DebugType,
                $"{ManualBrakingCurrentFraction:F0} %",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty, // Spacer because the state above needs 2 columns.
                (Car as MSTSWagon).HandBrakePresent ? $"{HandbrakePercent:F0}%" : string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                };
            }
            else
            {
                return new string[] {
                DebugType,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty, // Spacer because the state above needs 2 columns.
                (Car as MSTSWagon).HandBrakePresent ? $"{HandbrakePercent:F0}%" : string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                };
            }


        }

        // Required to override BrakeSystem
        public override void AISetPercent(float percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            //  Car.Train.EqualReservoirPressurePSIorInHg = Vac.FromPress(OneAtmospherePSI - MaxForcePressurePSI * (1 - percent / 100));
        }

        public override void SetHandbrakePercent(float percent)
        {
            if (!(Car as MSTSWagon).HandBrakePresent)
            {
                HandbrakePercent = 0;
                return;
            }
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            HandbrakePercent = percent;
        }

        public override float GetCylPressurePSI()
        {
            if (LocomotiveSteamBrakeFitted)
            {
                return SteamBrakeCylinderPressurePSI;
            }
            else
            {
                return ManualBrakingCurrentFraction;
            }
        }

        public override float GetCylVolumeM3()
        {
            return 0;
        }

        public override float GetVacResVolume()
        {
            return 0;
        }

        public override float GetVacBrakeCylNumber()
        {
            return 0;
        }


        public override float GetVacResPressurePSI()
        {
            return 0;
        }

        public override bool IsBraking()
        {
            return false;
        }

        public override void CorrectMaxCylPressurePSI(MSTSLocomotive loco)
        {

        }

        public override void SetRetainer(RetainerSetting setting)
        {
        }

        public override float InternalPressure(float realPressure)
        {
            return (float)Pressure.Vacuum.ToPressure(realPressure);
        }

        public override void PropagateBrakePressure(double elapsedClockSeconds)
        {

        }

        public override void InitializeMoving() // used when initial speed > 0
        {

        }

        public override void LocoInitializeMoving() // starting conditions when starting speed > 0
        {

        }


    }
}