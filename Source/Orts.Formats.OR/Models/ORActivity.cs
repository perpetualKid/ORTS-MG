using Orts.Formats.Msts.Models;
using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.OR.Models
{
    public class ORActivity
    {
        public int AIHornAtCrossings = -1;

        // Override values for activity creators
        public bool IsActivityOverride { get; private set; } = false;

        public ActivityEvents Events { get; private set; }

        public class OptionsSettings //this redirection has no functional advantage, only grouping to improve clarity in development
        {
            // General TAB
            public int GraduatedBrakeRelease { get; internal set; } = -1;
            public int ViewDispatcherWindow { get; internal set; } = -1;
            public int RetainersOnAllCars { get; internal set; } = -1;
            public int SoundSpeedControl { get; internal set; } = -1;

            // Video TAB
            public int FastFullScreenAltTab { get; internal set; } = -1;

            // Simulation TAB
            public int ForcedRedAtStationStops { get; internal set; } = -1;
            public int Autopilot { get; internal set; } = -1;
            public int ExtendedAITrainShunting { get; internal set; } = -1;
            public int UseAdvancedAdhesion { get; internal set; } = -1;
            public int BreakCouplers { get; internal set; } = -1;
            public int CurveResistanceDependent { get; internal set; } = -1;
            public int CurveSpeedDependent { get; internal set; } = -1;
            public int TunnelResistanceDependent { get; internal set; } = -1;
            public int WindResistanceDependent { get; internal set; } = -1;
            public int HotStart { get; internal set; } = -1;

            // Experimental TAB
            public int UseLocationPassingPaths { get; internal set; } = -1;
            public int AdhesionFactor { get; internal set; } = -1;
            public int AdhesionFactorChange { get; internal set; } = -1;
            public int AdhesionProportionalToWeather { get; internal set; } = -1;
            public int ActivityRandomization { get; internal set; } = -1;
            public int ActivityWeatherRandomization { get; internal set; } = -1;
            public int SuperElevationLevel { get; internal set; } = -1;
            public int SuperElevationMinimumLength { get; internal set; } = -1;
            public int SuperElevationGauge { get; internal set; } = -1;
        }

        public OptionsSettings Options { get; } = new OptionsSettings();
             
        public ORActivity(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("ortsaihornatcrossings", ()=>{ AIHornAtCrossings = stf.ReadIntBlock(AIHornAtCrossings); }),

                // General TAB
                new STFReader.TokenProcessor("ortsgraduatedbrakerelease", ()=>{ Options.GraduatedBrakeRelease = stf.ReadIntBlock(Options.GraduatedBrakeRelease); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortsviewdispatchwindow", ()=>{ Options.ViewDispatcherWindow = stf.ReadIntBlock(Options.ViewDispatcherWindow); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortsretainersonallcars", ()=>{ Options.RetainersOnAllCars = stf.ReadIntBlock(Options.RetainersOnAllCars); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortssoundspeedcontrol", ()=>{ Options.SoundSpeedControl = stf.ReadIntBlock(Options.SoundSpeedControl); IsActivityOverride = true; }),

                // Video TAB
                new STFReader.TokenProcessor("ortsfastfullscreenalttab", ()=>{ Options.FastFullScreenAltTab = stf.ReadIntBlock(Options.FastFullScreenAltTab); IsActivityOverride = true; }),

                // Simulation TAB
                new STFReader.TokenProcessor("ortsforcedredatstationstops", ()=>{ Options.ForcedRedAtStationStops = stf.ReadIntBlock(Options.ForcedRedAtStationStops); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortsautopilot", ()=>{ Options.Autopilot = stf.ReadIntBlock(Options.Autopilot); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortsextendedaitrainshunting", ()=>{ Options.ExtendedAITrainShunting = stf.ReadIntBlock(Options.ExtendedAITrainShunting); IsActivityOverride = true; }),

                new STFReader.TokenProcessor("ortsuseadvancedadhesion", ()=>{ Options.UseAdvancedAdhesion = stf.ReadIntBlock(Options.UseAdvancedAdhesion); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortsbreakcouplers", ()=>{ Options.BreakCouplers = stf.ReadIntBlock(Options.BreakCouplers); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortscurveresistancedependent", ()=>{ Options.CurveResistanceDependent = stf.ReadIntBlock(Options.CurveResistanceDependent); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortscurvespeeddependent", ()=>{ Options.CurveSpeedDependent = stf.ReadIntBlock(Options.CurveSpeedDependent); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortstunnelresistancedependent", ()=>{ Options.TunnelResistanceDependent = stf.ReadIntBlock(Options.TunnelResistanceDependent); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortswindresistancedependent", ()=>{ Options.WindResistanceDependent = stf.ReadIntBlock(Options.WindResistanceDependent); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortshotstart", ()=>{ Options.HotStart = stf.ReadIntBlock(Options.HotStart); IsActivityOverride = true; }),

                // Experimental TAB
                new STFReader.TokenProcessor("ortslocationlinkedpassingpaths", ()=>{ Options.UseLocationPassingPaths = stf.ReadIntBlock(Options.UseLocationPassingPaths); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortsadhesionfactorcorrection", ()=>{ Options.AdhesionFactor = stf.ReadIntBlock(Options.AdhesionFactor); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortsadhesionfactorchange", ()=>{ Options.AdhesionFactorChange = stf.ReadIntBlock(Options.AdhesionFactorChange); IsActivityOverride = true; }),

                new STFReader.TokenProcessor("ortsadhesionproportionaltoweather", ()=>{ Options.AdhesionProportionalToWeather = stf.ReadIntBlock(Options.AdhesionProportionalToWeather); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortsactivityrandomization", ()=>{ Options.ActivityRandomization = stf.ReadIntBlock(Options.ActivityRandomization); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortsactivityweatherrandomization", ()=>{ Options.ActivityWeatherRandomization = stf.ReadIntBlock(Options.ActivityWeatherRandomization); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortssuperelevationlevel", ()=>{ Options.SuperElevationLevel = stf.ReadIntBlock(Options.SuperElevationLevel); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortssuperelevationminimumlength", ()=>{ Options.SuperElevationMinimumLength = stf.ReadIntBlock(Options.SuperElevationMinimumLength); IsActivityOverride = true; }),
                new STFReader.TokenProcessor("ortssuperelevationgauge", ()=>{ Options.SuperElevationGauge = stf.ReadIntBlock(Options.SuperElevationGauge); IsActivityOverride = true; }),

                new STFReader.TokenProcessor("events",()=>
                {
                    Events.UpdateORActivtyData (stf);
                }),
            });
        }
    }
}
