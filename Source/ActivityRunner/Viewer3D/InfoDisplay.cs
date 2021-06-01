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

#define GEARBOX_DEBUG_LOG

#define DEBUG_DUMP_STEAM_POWER_CURVE
// Uses the DataLogger to record power curve data for steam locos when no other option is chosen.
// To use this, on the Menu, check the Logging box and cancel all Options > DataLogger.
// The data logger records data in the file "Program\dump.csv".
// For steam locomotives only this replaces the default data with a record for each speed increment (mph).
// Collect the data by starting from rest and accelerating the loco to maximum speed.
// Only horsepower and mph available currently.
// Analyse the data using a spreadsheet and graph with an XY chart.


using System;
using System.IO;
using System.Linq;
using System.Text;

using Orts.Common;
using Orts.Common.Calc;
using Orts.Common.Info;
using Orts.Common.Input;
using Orts.Common.Logging;
using Orts.Simulation.RollingStocks;

namespace Orts.ActivityRunner.Viewer3D
{
    /// <summary>
    /// Displays Viewer frame rate and Viewer.Text debug messages in the upper left corner of the screen.
    /// </summary>
    public class InfoDisplay : IDisposable
    {
        private readonly Viewer viewer;
        private readonly DataLogger dataLog;
        private readonly int ProcessorCount = Environment.ProcessorCount;

        private int frameNumber;
        private double lastUpdateRealTime;   // update text message only 4 times per second

        private float previousLoggedSteamSpeedMpH = -5.0f;
        private bool recordSteamPerformance;
        private bool recordSteamPowerCurve;

#if DEBUG_DUMP_STEAM_POWER_CURVE
        private float previousLoggedSpeedMpH = -1.0f;
        private bool disposedValue;
#endif

        public InfoDisplay(Viewer viewer)
        {
            this.viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));
            dataLog = new DataLogger(Path.Combine(viewer.Settings.LoggingPath, "OpenRailsDump.csv"), viewer.Settings.DataLoggerSeparator);

            if (viewer.Settings.DataLogger)
                DataLoggerStart();

            viewer.UserCommandController.AddEvent(UserCommand.DebugLogger, KeyEventType.KeyPressed, () =>
            {
                viewer.Settings.DataLogger = !viewer.Settings.DataLogger;
                if (viewer.Settings.DataLogger)
                    DataLoggerStart();
                else
                    DataLoggerStop();
            });
        }

        internal void Terminate()
        {
            if (viewer.Settings.DataLogger)
                DataLoggerStop();
        }

        private void RecordSteamPerformance()
        {
            MSTSSteamLocomotive steamloco = viewer.PlayerLocomotive as MSTSSteamLocomotive;

            double steamspeedMpH = Speed.MeterPerSecond.ToMpH(steamloco.SpeedMpS);
            if (steamspeedMpH >= previousLoggedSteamSpeedMpH + 5) // Add a new record every time speed increases by 5 mph
            {
                previousLoggedSteamSpeedMpH = (int)steamspeedMpH; // Keep speed records close to whole numbers

                dataLog.Data($"{Speed.MeterPerSecond.FromMpS(steamloco.SpeedMpS, false):F0}");
                dataLog.Data($"{Time.Second.ToM(steamloco.SteamPerformanceTimeS):F1}");
                dataLog.Data($"{steamloco.ThrottlePercent:F0}");
                dataLog.Data($"{steamloco.Train.MUReverserPercent:F0}");
                dataLog.Data($"{Dynamics.Force.ToLbf(steamloco.MotiveForceN):F0}");
                dataLog.Data($"{steamloco.IndicatedHorsePowerHP:F0}");
                dataLog.Data($"{steamloco.DrawBarPullLbsF:F0}");
                dataLog.Data($"{steamloco.DrawbarHorsePowerHP:F0}");
                dataLog.Data($"{Dynamics.Force.ToLbf(steamloco.LocomotiveCouplerForceN):F0}");
                dataLog.Data($"{Dynamics.Force.ToLbf(steamloco.LocoTenderFrictionForceN):F0}");
                dataLog.Data($"{Dynamics.Force.ToLbf(steamloco.TotalFrictionForceN):F0}");
                dataLog.Data($"{Mass.Kilogram.ToTonsUK(steamloco.TrainLoadKg):F0}");
                dataLog.Data($"{steamloco.BoilerPressurePSI:F0}");
                dataLog.Data($"{steamloco.LogSteamChestPressurePSI:F0}");
                dataLog.Data($"{steamloco.LogInitialPressurePSI:F0}");
                dataLog.Data($"{steamloco.LogCutoffPressurePSI:F0}");
                dataLog.Data($"{steamloco.LogReleasePressurePSI:F0}");
                dataLog.Data($"{steamloco.LogBackPressurePSI:F0}");

                dataLog.Data($"{steamloco.MeanEffectivePressurePSI:F0}");

                dataLog.Data($"{steamloco.CurrentSuperheatTempF:F0}");

                dataLog.Data($"{Frequency.Periodic.ToHours(steamloco.CylinderSteamUsageLBpS):F0}");
                dataLog.Data($"{Frequency.Periodic.ToHours(steamloco.WaterConsumptionLbpS):F0}");
                dataLog.Data($"{Mass.Kilogram.ToLb(Frequency.Periodic.ToHours(steamloco.FuelBurnRateSmoothedKGpS)):F0}");

                dataLog.Data($"{steamloco.SuperheaterSteamUsageFactor:F2}");
                dataLog.Data($"{steamloco.CumulativeCylinderSteamConsumptionLbs:F0}");
                dataLog.Data($"{steamloco.CumulativeWaterConsumptionLbs:F0}");

                dataLog.Data($"{steamloco.CutoffPressureDropRatio:F0}");

                dataLog.Data($"{steamloco.HPCylinderMEPPSI:F0}");
                dataLog.Data($"{steamloco.LogLPInitialPressurePSI:F0}");
                dataLog.Data($"{steamloco.LogLPCutoffPressurePSI:F0}");
                dataLog.Data($"{steamloco.LogLPReleasePressurePSI:F0}");
                dataLog.Data($"{steamloco.LogLPBackPressurePSI:F0}");
                dataLog.Data($"{steamloco.CutoffPressureDropRatio:F0}");
                dataLog.Data($"{steamloco.LPCylinderMEPPSI:F0}");

                dataLog.EndLine();
            }
        }

        private void RecordSteamPowerCurve()
        {
            MSTSSteamLocomotive steamloco = viewer.PlayerLocomotive as MSTSSteamLocomotive;
            double speedMpH = Speed.MeterPerSecond.ToMpH(steamloco.SpeedMpS);
            if (speedMpH >= previousLoggedSpeedMpH + 1) // Add a new record every time speed increases by 1 mph
            {
                previousLoggedSpeedMpH = (int)speedMpH; // Keep speed records close to whole numbers
                dataLog.Data($"{speedMpH:F1}");
                double power = Dynamics.Power.ToHp(steamloco.MotiveForceN * steamloco.SpeedMpS);
                dataLog.Data($"{power:F1}");
                dataLog.Data($"{steamloco.ThrottlePercent:F0}");
                dataLog.Data($"{steamloco.Train.MUReverserPercent:F0}");
                dataLog.EndLine();
            }
        }

        public void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            _ = frame;
            _ = elapsedTime;

            frameNumber++;

            double elapsedRealSeconds = viewer.RealTime - lastUpdateRealTime;
            if (elapsedRealSeconds >= 0.25)
            {
                lastUpdateRealTime = viewer.RealTime;
                Profile(elapsedRealSeconds);
            }
                        
#if DEBUG_DUMP_STEAM_POWER_CURVE
            if (recordSteamPowerCurve && viewer.PlayerLocomotive is MSTSSteamLocomotive)
            {
                RecordSteamPowerCurve();
            }
            else
            {
#endif
                if (recordSteamPerformance && viewer.PlayerLocomotive is MSTSSteamLocomotive)
                {
                    RecordSteamPerformance();
                }
                else


            //Here's where the logger stores the data from each frame
                if (viewer.Settings.DataLogger)
                {
                    if (viewer.Settings.DataLogPerformance)
                    {
                        viewer.CurrentProcess.Refresh();
                        dataLog.Data(VersionInfo.Version);
                        dataLog.Data($"{frameNumber:F0}");
                        dataLog.Data($"{viewer.CurrentProcess.WorkingSet64:F0}");
                        dataLog.Data($"{GC.GetTotalMemory(false):F0}");
                        dataLog.Data($"{GC.CollectionCount(0):F0}");
                        dataLog.Data($"{GC.CollectionCount(1):F0}");
                        dataLog.Data($"{GC.CollectionCount(2):F0}");
                        dataLog.Data($"{ProcessorCount:F0}");
                        dataLog.Data($"{viewer.RenderProcess.FrameRate.Value:F0}");
                        dataLog.Data($"{viewer.RenderProcess.FrameTime.Value:F6}");
                        dataLog.Data($"{viewer.RenderProcess.ShadowPrimitivePerFrame.Sum():F0}");
                        dataLog.Data($"{viewer.RenderProcess.PrimitivePerFrame.Sum():F0}");
                        dataLog.Data($"{viewer.RenderProcess.Profiler.Wall.Value:F0}");
                        dataLog.Data($"{viewer.UpdaterProcess.Profiler.Wall.Value:F0}");
                        dataLog.Data($"{viewer.LoaderProcess.Profiler.Wall.Value:F0}");
                        dataLog.Data($"{viewer.SoundProcess.Profiler.Wall.Value:F0}");
                    }
                    if (viewer.Settings.DataLogPhysics)
                    {
                        dataLog.Data(FormatStrings.FormatPreciseTime(viewer.Simulator.ClockTime));
                        dataLog.Data(viewer.PlayerLocomotive.Direction.ToString());
                        dataLog.Data($"{viewer.PlayerTrain.MUReverserPercent:F0}");
                        dataLog.Data($"{viewer.PlayerLocomotive.ThrottlePercent:F0}");
                        dataLog.Data($"{viewer.PlayerLocomotive.MotiveForceN:F0}");
                        dataLog.Data($"{viewer.PlayerLocomotive.BrakeForceN:F0}");
                        dataLog.Data($"{(viewer.PlayerLocomotive as MSTSLocomotive).LocomotiveAxle.AxleForceN:F2}");
                        dataLog.Data($"{(viewer.PlayerLocomotive as MSTSLocomotive).LocomotiveAxle.SlipSpeedPercent:F1}");

                        string LogSpeed(float speedMpS)
                        {
                            switch (viewer.Settings.DataLogSpeedUnits)
                            {
                                case SpeedUnit.Mps:
                                    return $"{speedMpS:F1}";
                                case SpeedUnit.Mph:
                                    return $"{Speed.MeterPerSecond.FromMpS(speedMpS, false):F1}";
                                case SpeedUnit.Kmph:
                                    return $"{Speed.MeterPerSecond.FromMpS(speedMpS, true):F1}";
                                case SpeedUnit.Route:
                                default:
                                    return FormatStrings.FormatSpeed(speedMpS, viewer.MilepostUnitsMetric);
                            }
                        }
                        dataLog.Data(LogSpeed(viewer.PlayerLocomotive.SpeedMpS));
                        dataLog.Data(LogSpeed(viewer.PlayerTrain.AllowedMaxSpeedMpS));

                        dataLog.Data($"{viewer.PlayerLocomotive.DistanceM:F0}");
                        dataLog.Data($"{viewer.PlayerLocomotive.GravityForceN:F0}");

                        if ((viewer.PlayerLocomotive as MSTSLocomotive).TrainBrakeController != null)
                            dataLog.Data($"{(viewer.PlayerLocomotive as MSTSLocomotive).TrainBrakeController.CurrentValue:F2}");
                        else
                            dataLog.Data("null");

                        if ((viewer.PlayerLocomotive as MSTSLocomotive).EngineBrakeController != null)
                            dataLog.Data($"{(viewer.PlayerLocomotive as MSTSLocomotive).EngineBrakeController.CurrentValue:F2}");
                        else
                            dataLog.Data("null");

                        if ((viewer.PlayerLocomotive as MSTSLocomotive).BrakemanBrakeController != null)
                            dataLog.Data($"{(viewer.PlayerLocomotive as MSTSLocomotive).BrakemanBrakeController.CurrentValue:F2}");
                        else
                            dataLog.Data("null");
                        
                        dataLog.Data($"{viewer.PlayerLocomotive.BrakeSystem.GetCylPressurePSI():F0}");
                        dataLog.Data($"{(viewer.PlayerLocomotive as MSTSLocomotive).MainResPressurePSI:F0}");
                        dataLog.Data($"{(viewer.PlayerLocomotive as MSTSLocomotive).CompressorIsOn}");
#if GEARBOX_DEBUG_LOG
                        if (viewer.PlayerLocomotive is MSTSDieselLocomotive dieselLoco)
                        {
                            dataLog.Data($"{dieselLoco.DieselEngines[0].RealRPM:F0}");
                            dataLog.Data($"{dieselLoco.DieselEngines[0].DemandedRPM:F0}");
                            dataLog.Data($"{dieselLoco.DieselEngines[0].LoadPercent:F0}");
                            if (dieselLoco.DieselEngines.HasGearBox)
                            {
                                dataLog.Data($"{dieselLoco.DieselEngines[0].GearBox.CurrentGearIndex}");
                                dataLog.Data($"{dieselLoco.DieselEngines[0].GearBox.NextGearIndex}");
                                dataLog.Data($"{dieselLoco.DieselEngines[0].GearBox.ClutchPercent}");
                            }
                            else
                            {
                                dataLog.Data("null");
                                dataLog.Data("null");
                                dataLog.Data("null");
                            }
                            dataLog.Data($"{dieselLoco.DieselFlowLps:F2}");
                            dataLog.Data($"{dieselLoco.DieselLevelL:F0}");
                            dataLog.Data("null");
                            dataLog.Data("null");
                            dataLog.Data("null");
                        }
                        else if (viewer.PlayerLocomotive is MSTSElectricLocomotive electricLoco)
                        {
                            dataLog.Data(electricLoco.Pantographs[1].CommandUp.ToString());
                            dataLog.Data(electricLoco.Pantographs[2].CommandUp.ToString());
                            dataLog.Data(electricLoco.Pantographs.List.Count > 2 ?
                                electricLoco.Pantographs[3].CommandUp.ToString() : null);
                            dataLog.Data(electricLoco.Pantographs.List.Count > 3 ?
                                electricLoco.Pantographs[4].CommandUp.ToString() : null);
                            dataLog.Data("null");
                            dataLog.Data("null");
                            dataLog.Data("null");
                            dataLog.Data("null");
                            dataLog.Data("null");
                            dataLog.Data("null");
                            dataLog.Data("null");
                            dataLog.Data("null");
                            dataLog.Data("null");
                        }
                        else if (viewer.PlayerLocomotive is MSTSSteamLocomotive steamLoco)
                        {
                            dataLog.Data($"{steamLoco.BlowerSteamUsageLBpS:F0}");
                            dataLog.Data($"{steamLoco.BoilerPressurePSI:F0}");
                            dataLog.Data($"{steamLoco.CylinderCocksAreOpen}");
                            dataLog.Data($"{steamLoco.CylinderCompoundOn}");
                            dataLog.Data($"{steamLoco.EvaporationLBpS:F0}");
                            dataLog.Data($"{steamLoco.FireMassKG:F0}");
                            dataLog.Data($"{steamLoco.CylinderSteamUsageLBpS:F0}");
                            if (steamLoco.BlowerController != null)
                                dataLog.Data($"{steamLoco.BlowerController.CurrentValue:F0}");
                            else
                                dataLog.Data("null");

                            if (steamLoco.DamperController != null)
                                dataLog.Data($"{steamLoco.DamperController.CurrentValue:F0}");
                            else
                                dataLog.Data("null");
                            if (steamLoco.FiringRateController != null)
                                dataLog.Data($"{steamLoco.FiringRateController.CurrentValue:F0}");
                            else
                                dataLog.Data("null");
                            if (steamLoco.Injector1Controller != null)
                                dataLog.Data($"{steamLoco.Injector1Controller.CurrentValue:F0}");
                            else
                                dataLog.Data("null");
                            if (steamLoco.Injector2Controller != null)
                                dataLog.Data($"{steamLoco.Injector2Controller.CurrentValue:F0}");
                            else
                                dataLog.Data("null");
                        }
#endif
                    }
                dataLog.EndLine();
#if DEBUG_DUMP_STEAM_POWER_CURVE
                }
#endif
            }
        }

        private void DataLoggerStart()
        {
            StringBuilder headline = new StringBuilder();

            recordSteamPerformance = false;
            recordSteamPowerCurve = false;
            if (viewer.Settings.DataLogPerformance)
            {
                headline.Append(string.Join(((char)dataLog.Separator).ToString(), new string[] {
                    "SVN",
                    "Frame",
                    "Memory",
                    "Memory (Managed)",
                    "Gen 0 GC",
                    "Gen 1 GC",
                    "Gen 2 GC",
                    "Processors",
                    "Frame Rate",
                    "Frame Time",
                    "Shadow Primitives",
                    "Render Primitives",
                    "Render Process",
                    "Updater Process",
                    "Loader Process",
                    "Sound Process" } ));
            }
            if (viewer.Settings.DataLogPhysics)
            {
                if (headline.Length > 0)
                    headline.Append(((char)dataLog.Separator));

                headline.Append(string.Join(((char)dataLog.Separator).ToString(), new string[] {
                    "Time",
                    "Player Direction",
                    "Player Reverser [%]",
                    "Player Throttle [%]",
                    "Player Motive Force [N]",
                    "Player Brake Force [N]",
                    "Player Axle Force [N]",
                    "Player Wheelslip",
                    $"Player Speed [{viewer.Settings.DataLogSpeedUnits}]",
                    $"Speed Limit [{viewer.Settings.DataLogSpeedUnits}]",
                    "Distance [m]",
                    "Player Gravity Force [N]",
                    "Train Brake",
                    "Engine Brake",
                    "Player Cylinder PSI",
                    "Player Main Res PSI",
                    "Player Compressor On",
                    "D:Real RPM / E:panto 1 / S:Blower usage LBpS",
                    "D:Demanded RPM / E:panto 2 / S:Boiler PSI",
                    "D:Load % / E:panto 3 / S:Cylinder Cocks open",
                    "D:Gearbox Current Gear / E:panto 4 / S:Evaporation LBpS",
                    "D:Gearbox Next Gear / E:null / S:Fire Mass KG",
                    "D:Clutch % / E:null / S:Steam usage LBpS",
                    "D:Fuel Flow Lps / E:null / S:Blower",
                    "D:Fuel level L / E:null / S:Damper",
                    "D:null / E:null / S:Firing Rate",
                    "D:null / E:null / S:Injector 1",
                    "D:null / E:null / S:Injector 2" } ));
            }
            if (viewer.Settings.DataLogSteamPerformance)
            {
                recordSteamPerformance = true;
                if (headline.Length > 0)
                    headline.Append(((char)dataLog.Separator));

                headline.Append(string.Join(((char)dataLog.Separator).ToString(), new string[] {
                    "Speed (mph)",
                    "Time (M)",
                    "Throttle (%)",
                    "Cut-off (%)",
                    "ITE (MotiveForce - lbf)",
                    "IHP (hp)",
                    "Drawbar TE (lbf)",
                    "Drawbar HP (hp)",
                    "Coupler Force (lbf)",
                    "Loco & Tender Resistance (lbf)",
                    "Train Resistance (lbf)",
                    "Train Load (t-uk)",
                    "Boiler Pressure (psi)",
                    "Steam Chest Pressure (psi)",
                    "Initial Pressure (psi)",
                    "Cutoff Pressure (psi)",
                    "Release Pressure (psi)",
                    "Back Pressure (psi)",
                    "MEP (psi)",
                    "Superheat Temp (F)",
                    "Steam consumption (lbs/h)",
                    "Water consumption (lbs/h)",
                    "Coal consumption (lbs/h)",
                    "Cylinder Thermal Efficiency",
                    "Cumulative Steam (lbs)",
                    "Cumulative Water (lbs)",
                    "Cutoff pressure Ratio",
                    "HP MEP (psi)",
                    "LPInitial Pressure (psi)",
                    "LPCutoff Pressure (psi)",
                    "LPRelease Pressure (psi)",
                    "LPBack Pressure (psi)",
                    "LPCutoff pressure Ratio",
                    "LP MEP (psi)" } ));
            }

#if DEBUG_DUMP_STEAM_POWER_CURVE
            if (!viewer.Settings.DataLogPerformance && !viewer.Settings.DataLogPhysics && !viewer.Settings.DataLogMisc && !viewer.Settings.DataLogSteamPerformance)
            {
                recordSteamPowerCurve = true;
                headline.Append(string.Join(((char)dataLog.Separator).ToString(), new string[] {
                    "speed (mph)",
                    "power (hp)",
                    "throttle (%)",
                    "cut-off (%)" } ));
            }
#endif
            dataLog.AddHeadline(headline.ToString());

        }

        private void DataLoggerStop()
        {
            dataLog.Flush();
        }

        public void Profile(double elapsedRealSeconds) // should be called every 100mS
        {
            if (elapsedRealSeconds < 0.01)  // just in case
                return;

            viewer.RenderProcess.Profiler.Mark();
            viewer.UpdaterProcess.Profiler.Mark();
            viewer.LoaderProcess.Profiler.Mark();
            viewer.SoundProcess.Profiler.Mark();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    dataLog?.Dispose();
                }

                disposedValue = true;
            }
        }
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
