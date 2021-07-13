// COPYRIGHT 2010, 2011, 2014, 2015 by the Open Rails project.
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

using Microsoft.Xna.Framework;

namespace Orts.Simulation.World
{
    public class Weather
    {
        // Rainy conditions (Glossary of Meteorology (June 2000). "Rain". American Meteorological Society. Retrieved 2010-01-15.):
        //   Type        Rate
        //   Light       <2.5mm/h
        //   Moderate     2.5-7.3mm/h
        //   Heavy           >7.3mm/h
        //   Violent         >50.0mm/h
        //
        // Snowy conditions (Glossary of Meteorology (2009). "Snow". American Meteorological Society. Retrieved 2009-06-28.):
        //   Type        Visibility
        //   Light           >1.0km
        //   Moderate     0.5-1.0km
        //   Heavy       <0.5km

        /// <summary>
        /// Overcast factor: 0.0 = almost no clouds; 0.1 = wispy clouds; 1.0 = total overcast. 
        /// </summary>
        public float OvercastFactor { get; set; }

        /// <summary>
        /// Pricipitation intensity in particles per second per meter^2 (PPSPM2). 
        /// </summary>
        public float PrecipitationIntensity { get; set; }

        /// <summary>
        /// Fog/visibility distance. Ranges from 10m (can't see anything), 5km (medium), 20km (clear) to 100km (clear arctic). 
        /// </summary>
        public float FogVisibilityDistance { get; set; }

        /// <summary>
        /// Precipitation liquidity; =1 for rain, =0 for snow; intermediate values possible with dynamic weather; 
        /// </summary>
        public float PrecipitationLiquidity { get; set; }
        public float CalculatedWindDirection { get; set; }
        public Vector2 WindSpeed { get; set; }
        public float WindDirection => CalculatedWindDirection;
    }
}
