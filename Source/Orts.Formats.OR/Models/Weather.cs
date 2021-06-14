using System;
using System.Diagnostics;
using System.IO;

using Orts.Common;
using Orts.Common.Xna;
using Orts.Formats.Msts;
using Orts.Formats.OR.Parsers;

namespace Orts.Formats.OR.Models
{
    public abstract class WeatherCondition
    {
        public float Time { get; protected set; }               // time of change

        private static Random random { get; } = new Random();

        internal protected virtual bool TryParse(JsonReader reader)
        {
            switch (reader.Path)
            {
                case "Time": Time = reader.AsTime(Time); break;
                default: return false;
            }
            return true;
        }

        public void UpdateTime(float value)
        {
            Time = value;
        }

        // check value, set random value if allowed and value not set
        protected float CheckValue(float value, bool randomize, float minValue, float maxValue, TimeSpan duration, string description)
        {
            // overcast
            if (value < 0 && randomize)
            {
                value = (random.Next((int)maxValue * 100) / 100);  // ensure there is a value if range is 0 - 1
            }
            else
            {
                float correctedValue = (float)MathHelperD.Clamp(value, minValue, maxValue);
                if (correctedValue != value)
                {
                    Trace.TraceInformation("Invalid value for {0} for weather at {1} : {2}; value must be between {3} and {4}, clamped to {5}",
                        description, duration.ToString(), value, minValue, maxValue, correctedValue);
                    value = correctedValue;
                }
            }
            return value;
        }

        public abstract void Save(BinaryWriter outf);

        public abstract void Check(TimeSpan duration);
    }

    public class OvercastCondition : WeatherCondition
    {
        public float Overcast { get; private set; }                     // required overcast - range : 0 - 100 (percentage)
        public float Variation { get; private set; }            // variation in overcast - range : 0 - 100 (percentage change)
        public float RateOfChange { get; private set; }         // overcast rate of change - range : 0 - 1 (scaling factor)
        public float Visibility { get; private set; } = 60000; // required visibility - range 1000 - 60000 (for lower values use fog)

        internal OvercastCondition(JsonReader json)
        {
            json.ReadBlock(TryParse);
        }

        internal protected override bool TryParse(JsonReader reader)
        {
            // get values
            if (base.TryParse(reader))
                return true;
            switch (reader.Path)
            {
                case "Overcast":
                    Overcast = reader.AsFloat(Overcast); break;
                case "OvercastVariation":
                    Variation = reader.AsFloat(Variation); break;
                case "OvercastRateOfChange":
                    RateOfChange = reader.AsFloat(RateOfChange); break;
                case "OvercastVisibility":
                    Visibility = reader.AsFloat(Visibility); break;
                default: return false;
            }

            return true;
        }

        internal OvercastCondition()
        { }

        // restore
        public OvercastCondition(BinaryReader inf)
        {
            Time = inf.ReadSingle();
            Overcast = inf.ReadSingle();
            Variation = inf.ReadSingle();
            RateOfChange = inf.ReadSingle();
            Visibility = inf.ReadSingle();
        }

        // save
        public override void Save(BinaryWriter outf)
        {
            outf.Write("overcast");
            outf.Write(Time);
            outf.Write(Overcast);
            outf.Write(Variation);
            outf.Write(RateOfChange);
            outf.Write(Visibility);
        }

        public override void Check(TimeSpan duration)
        {
            Overcast = CheckValue(Overcast, true, 0, 100, duration, "Overcast");
            Variation = CheckValue(Variation, true, 0, 100, duration, "Overcast Variation");
            RateOfChange = CheckValue(RateOfChange, true, 0, 1, duration, "Overcast Rate of Change");
            Visibility = CheckValue(Visibility, false, 1000, 60000, duration, "Overcast Visibility");
        }
    }

    // precipitation

    public class PrecipitationCondition : WeatherCondition
    {
        // precipitation spell
        public WeatherType PrecipitationType { get; private set; } = WeatherType.Clear;    // required precipitation : rain or snow
        public float Density { get; private set; }                             // precipitation density - range 0 - 1
        public float Variation { get; private set; }                           // precipitation density variation - range 0 - 1
        public float RateOfChange { get; private set; }                        // precipitation rate of change - range 0 - 1
        public float Probability { get; private set; }                         // precipitation probability - range : 0 - 100
        public float Spread { get; private set; } = 1;                          // precipitation average continuity - range : 1 - ...
        public float VisibilityAtMinDensity { get; private set; } = 20000;     // visibility during precipitation at min density
        public float VisibilityAtMaxDensity { get; private set; } = 10000;     // visibility during precipitation at max density

        // build up to precipitation
        public float OvercastPrecipitationStart { get; private set; }                       // required overcast to start precipitation, also overcast during precipitation - range 0 - 100
        public float OvercastBuildUp { get; private set; }                                  // overcast rate of change ahead of precipitation spell - range : 0 - 1
        public float PrecipitationStartPhase { get; private set; } = 60;                    // measure for duration of start phase (from dry to full density) - range : 30 to 240 (secs)

        // dispersion after precipitation
        public float OvercastDispersion { get; private set; }                               // overcast rate of change after precipitation spell - range : 0 - 1
        public float PrecipitationEndPhase { get; private set; } = 60;                      // measure for duration of end phase (from full density to dry) - range : 30 to 360 (secs)

        public OvercastCondition Overcast { get; } = new OvercastCondition();   //required overcast in clear spells

        internal PrecipitationCondition(JsonReader json)
        {
            json.ReadBlock(TryParse);
        }

        internal protected override bool TryParse(JsonReader reader)
        {
            // read items
            if (base.TryParse(reader))
                return true;
            switch (reader.Path)
            {
                case "PrecipitationType":
                    PrecipitationType = reader.AsEnum(PrecipitationType); break;
                case "PrecipitationDensity":
                    Density = reader.AsFloat(Density); break;
                case "PrecipitationVariation":
                    Variation = reader.AsFloat(Variation); break;
                case "PrecipitationRateOfChange":
                    RateOfChange = reader.AsFloat(RateOfChange); break;
                case "PrecipitationProbability":
                    Probability = reader.AsFloat(Probability); break;
                case "PrecipitationSpread":
                    Spread = reader.AsFloat(Spread); break;
                case "PrecipitationVisibilityAtMinDensity":
                    VisibilityAtMinDensity = reader.AsFloat(VisibilityAtMinDensity); break;
                case "PrecipitationVisibilityAtMaxDensity":
                    VisibilityAtMaxDensity = reader.AsFloat(VisibilityAtMaxDensity); break;
                case "OvercastPrecipitationStart":
                    OvercastPrecipitationStart = reader.AsFloat(OvercastPrecipitationStart); break;
                case "OvercastBuildUp":
                    OvercastBuildUp = reader.AsFloat(OvercastBuildUp); break;
                case "PrecipitationStartPhase":
                    PrecipitationStartPhase = reader.AsFloat(PrecipitationStartPhase); break;
                case "OvercastDispersion":
                    OvercastDispersion = reader.AsFloat(OvercastDispersion); break;
                case "PrecipitationEndPhase":
                    PrecipitationEndPhase = reader.AsFloat(PrecipitationEndPhase); break;

                case "Overcast":
                case "OvercastVariation":
                case "OvercastRateOfChange":
                case "OvercastVisibility":
                    Overcast.TryParse(reader); break;
                default: return false;
            }

            return true;
        }

        // restore
        public PrecipitationCondition(BinaryReader inf)
        {
            Time = inf.ReadSingle();
            PrecipitationType = (WeatherType)inf.ReadInt32();
            Density = inf.ReadSingle();
            Variation = inf.ReadSingle();
            RateOfChange = inf.ReadSingle();
            Probability = inf.ReadSingle();
            Spread = inf.ReadSingle();
            VisibilityAtMinDensity = inf.ReadSingle();
            VisibilityAtMaxDensity = inf.ReadSingle();

            OvercastPrecipitationStart = inf.ReadSingle();
            OvercastBuildUp = inf.ReadSingle();
            PrecipitationStartPhase = inf.ReadSingle();

            OvercastDispersion = inf.ReadSingle();
            PrecipitationEndPhase = inf.ReadSingle();

            Overcast = new OvercastCondition(inf);
        }

        // save
        public override void Save(BinaryWriter outf)
        {
            outf.Write("precipitation");
            outf.Write(Time);
            outf.Write((int)PrecipitationType);
            outf.Write(Density);
            outf.Write(Variation);
            outf.Write(RateOfChange);
            outf.Write(Probability);
            outf.Write(Spread);
            outf.Write(VisibilityAtMinDensity);
            outf.Write(VisibilityAtMaxDensity);

            outf.Write(OvercastPrecipitationStart);
            outf.Write(OvercastBuildUp);
            outf.Write(PrecipitationStartPhase);

            outf.Write(OvercastDispersion);
            outf.Write(PrecipitationEndPhase);

            Overcast.Save(outf);
        }

        public override void Check(TimeSpan duration)
        {
            Overcast.Check(duration);

            // precipitation
            Density = CheckValue(Density, true, 0, 1, duration, "Precipitation Density");
            Variation = CheckValue(Variation, true, 0, 1, duration, "Precipitation Variation");
            RateOfChange = CheckValue(RateOfChange, true, 0, 1, duration, "Precipitation Rate Of Change");
            Probability = CheckValue(Probability, true, 0, 100, duration, "Precipitation Probability");
            Spread = CheckValue(Spread, false, 1, 1000, duration, "Precipitation Spread");
            VisibilityAtMinDensity = CheckValue(VisibilityAtMinDensity, false, 100, Overcast.Visibility, duration, "Precipitation Visibility At Min Density");
            VisibilityAtMaxDensity = CheckValue(VisibilityAtMaxDensity, false, 100, VisibilityAtMinDensity, duration, "Precipitation Visibility At Max Density");

            // build up
            OvercastPrecipitationStart = CheckValue(OvercastPrecipitationStart, true, Overcast.Overcast, 100, duration, "Overcast Precipitation Start");
            OvercastBuildUp = CheckValue(OvercastBuildUp, true, 0, 1, duration, "Overcast Build Up");
            PrecipitationStartPhase = CheckValue(PrecipitationStartPhase, false, 30, 240, duration, "Precipitation Start Phase");

            // dispersion
            OvercastDispersion = CheckValue(OvercastDispersion, true, 0, 1, duration, "Overcast Dispersion");
            PrecipitationEndPhase = CheckValue(PrecipitationEndPhase, false, 30, 360, duration, "Precipitation End Phase");
        }
    }

    // fog
    public class FogCondition : WeatherCondition
    {
        public float Visibility { get; private set; } = 1000;                            // required fog density - range 0 - 1000
        public float SetTime { get; private set; } = 300;                                // required rate for fog setting - range 300 - 3600
        public float LiftTime { get; private set; } = 300;                               // required rate for fog lifting - range 300 - 3600 - required visibility is taken from next weather
        public float Overcast { get; private set; }                                      // required overcast after fog lifted - range 0 - 100

        public FogCondition(JsonReader json)
        {
            json.ReadBlock(TryParse);
        }

        internal protected override bool TryParse(JsonReader reader)
        {
            if (base.TryParse(reader)) return true;
            switch (reader.Path)
            {
                case "FogVisibility": Visibility = reader.AsFloat(Visibility); break;
                case "FogSetTime": SetTime = reader.AsFloat(SetTime); break;
                case "FogLiftTime": LiftTime = reader.AsFloat(LiftTime); break;
                case "FogOvercast": Overcast = reader.AsFloat(Overcast); break;
                default: return false;
            }

            return true;
        }

        public FogCondition(BinaryReader inf)
        {
            Time = inf.ReadSingle();
            Visibility = inf.ReadSingle();
            SetTime = inf.ReadSingle();
            LiftTime = inf.ReadSingle();
            Overcast = inf.ReadSingle();
        }

        public override void Save(BinaryWriter outf)
        {
            outf.Write("fog");
            outf.Write(Time);
            outf.Write(Visibility);
            outf.Write(SetTime);
            outf.Write(LiftTime);
            outf.Write(Overcast);
        }

        public override void Check(TimeSpan duration)
        {
            Overcast = CheckValue(Overcast, true, 0, 100, duration, "Fog Overcast");
            SetTime = CheckValue(SetTime, false, 300, 3600, duration, "Fog Set Time");
            LiftTime = CheckValue(LiftTime, false, 300, 3600, duration, "Fog Lift Time");
            Visibility = CheckValue(Visibility, false, 10, 20000, duration, "Fog Visibility");
        }
    }

}
