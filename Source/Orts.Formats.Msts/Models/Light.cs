// COPYRIGHT 2010, 2011, 2012, 2013, 2014 by the Open Rails project.
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
using System.IO;

using Microsoft.Xna.Framework;

using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Models
{
    /// <summary>
    /// A LightState object encapsulates the data for each State in the States subblock.
    /// </summary>
    public class LightState
    {
        public float Duration { get; private set; }
        public uint Color { get; private set; }
        public Vector3 Position { get; private set; }
        public float Radius { get; private set; }
        public Vector3 Azimuth { get; private set; }
        public Vector3 Elevation { get; private set; }
        public bool Transition { get; private set; }
        public float Angle { get; private set; }

        internal LightState(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new[] {
                new STFReader.TokenProcessor("duration", ()=>{ Duration = stf.ReadFloatBlock(STFReader.Units.None, null); }),
                new STFReader.TokenProcessor("lightcolour", ()=>{ Color = stf.ReadHexBlock(null); }),
                new STFReader.TokenProcessor("position", ()=>{ Position = stf.ReadVector3Block(STFReader.Units.None, Vector3.Zero); }),
                new STFReader.TokenProcessor("radius", ()=>{ Radius = stf.ReadFloatBlock(STFReader.Units.Distance, null); }),
                new STFReader.TokenProcessor("azimuth", ()=>{ Azimuth = stf.ReadVector3Block(STFReader.Units.None, Vector3.Zero); }),
                new STFReader.TokenProcessor("elevation", ()=>{ Elevation = stf.ReadVector3Block(STFReader.Units.None, Vector3.Zero); }),
                new STFReader.TokenProcessor("transition", ()=>{ Transition = 1 <= stf.ReadFloatBlock(STFReader.Units.None, 0); }),
                new STFReader.TokenProcessor("angle", ()=>{ Angle = stf.ReadFloatBlock(STFReader.Units.None, null); }),
            });
            // Color byte order changed in XNA 4 from BGRA to RGBA
            Color = new Color()
            {
                B = (byte)(Color),
                G = (byte)(Color >> 8),
                R = (byte)(Color >> 16),
                A = (byte)(Color >> 24)
            }.PackedValue;
        }

        internal LightState(LightState state, bool reverse)
        {
            Duration = state.Duration;
            Color = state.Color;
            Position = state.Position;
            Radius = state.Radius;
            Azimuth = state.Azimuth;
            Elevation = state.Elevation;
            Transition = state.Transition;
            Angle = state.Angle;

            if (reverse)
            {
                Azimuth = new Vector3((Azimuth.X + 180) % 360, (Azimuth.Y + 180) % 360, (Azimuth.Z + 180) % 360);
                Position = new Vector3(-Position.X, Position.Y, -Position.Z);
            }
        }
    }

    /// <summary>
    /// The Light class encapsulates the data for each Light object 
    /// in the Lights block of an ENG/WAG file. 
    /// </summary>
    public class Light
    {
        public int Index { get; private set; }
        public LightType Type { get; private set; }
        public LightHeadlightCondition Headlight { get; private set; }
        public LightUnitCondition Unit { get; private set; }
        public LightPenaltyCondition Penalty { get; private set; }
        public LightControlCondition Control { get; private set; }
        public LightServiceCondition Service { get; private set; }
        public LightTimeOfDayCondition TimeOfDay { get; private set; }
        public LightWeatherCondition Weather { get; private set; }
        public LightCouplingCondition Coupling { get; private set; }
        public bool Cycle { get; private set; }
        public float FadeIn { get; private set; }
        public float FadeOut { get; private set; }
        public IList<LightState> States { get; } = new List<LightState>();

        internal Light(int index, STFReader stf)
        {
            Index = index;
            stf.MustMatchBlockStart();
            stf.ParseBlock(new[] {
                new STFReader.TokenProcessor("type", ()=>{ Type = (LightType)stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("conditions", ()=>{ stf.MustMatchBlockStart(); stf.ParseBlock(new[] {
                    new STFReader.TokenProcessor("headlight", ()=>{ Headlight = (LightHeadlightCondition)stf.ReadIntBlock(null); }),
                    new STFReader.TokenProcessor("unit", ()=>{ Unit = (LightUnitCondition)stf.ReadIntBlock(null); }),
                    new STFReader.TokenProcessor("penalty", ()=>{ Penalty = (LightPenaltyCondition)stf.ReadIntBlock(null); }),
                    new STFReader.TokenProcessor("control", ()=>{ Control = (LightControlCondition)stf.ReadIntBlock(null); }),
                    new STFReader.TokenProcessor("service", ()=>{ Service = (LightServiceCondition)stf.ReadIntBlock(null); }),
                    new STFReader.TokenProcessor("timeofday", ()=>{ TimeOfDay = (LightTimeOfDayCondition)stf.ReadIntBlock(null); }),
                    new STFReader.TokenProcessor("weather", ()=>{ Weather = (LightWeatherCondition)stf.ReadIntBlock(null); }),
                    new STFReader.TokenProcessor("coupling", ()=>{ Coupling = (LightCouplingCondition)stf.ReadIntBlock(null); }),
                });}),
                new STFReader.TokenProcessor("cycle", ()=>{ Cycle = 0 != stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("fadein", ()=>{ FadeIn = stf.ReadFloatBlock(STFReader.Units.None, null); }),
                new STFReader.TokenProcessor("fadeout", ()=>{ FadeOut = stf.ReadFloatBlock(STFReader.Units.None, null); }),
                new STFReader.TokenProcessor("states", ()=>{
                    stf.MustMatchBlockStart();
                    int count = stf.ReadInt(null);
                    stf.ParseBlock(new[] {
                        new STFReader.TokenProcessor("state", ()=>{
                            if (States.Count >= count)
                                STFException.TraceWarning(stf, "Skipped extra State");
                            else
                                States.Add(new LightState(stf));
                        }),
                    });
                    if (States.Count < count)
                        STFException.TraceWarning(stf, $"{count - States.Count} missing State(s)");
                }),
            });
        }

        public Light(Light source, bool reverse)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            Index = source.Index;
            Type = source.Type;
            Headlight = source.Headlight;
            Unit = source.Unit;
            Penalty = source.Penalty;
            Control = source.Control;
            Service = source.Service;
            TimeOfDay = source.TimeOfDay;
            Weather = source.Weather;
            Coupling = source.Coupling;
            Cycle = source.Cycle;
            FadeIn = source.FadeIn;
            FadeOut = source.FadeOut;
            foreach (LightState state in source.States)
                States.Add(new LightState(state, reverse));

            if (reverse)
            {
                if (Unit == LightUnitCondition.First)
                    Unit = LightUnitCondition.FirstRev;
                else if (Unit == LightUnitCondition.Last)
                    Unit = LightUnitCondition.LastRev;
            }
        }
    }

    /// <summary>
    /// A Lights object is created for any engine or wagon having a 
    /// Lights block in its ENG/WAG file. It contains a collection of
    /// Light objects.
    /// Called from within the MSTSWagon class.
    /// </summary>
    public class Lights : List<Light>
    {
        public Lights(STFReader stf)
        {
            if (null == stf)
                throw new ArgumentNullException(nameof(stf));

            stf.MustMatchBlockStart();
            stf.ReadInt(null); // count; ignore this because its not always correct
            stf.ParseBlock(new[] {
                new STFReader.TokenProcessor("light", ()=>{ Add(new Light(Count, stf)); }),
            });
            if (Count == 0)
                throw new InvalidDataException("lights with no lights");

            // MSTSBin created reverse headlight cones automatically, so we shall do so too.
            List<Light> reverseLights = new List<Light>();
            foreach (Light light in this)
                if (light.Type == LightType.Cone)
                    reverseLights.Add(new Light(light, true));
            AddRange(reverseLights);
        }
    }
}
