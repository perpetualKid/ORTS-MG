// COPYRIGHT 2009, 2010 by the Open Rails project.
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

using System.Collections.Generic;

using Orts.Formats.Msts.Models;
using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Files
{
    public class EnvironmentFile
    {
        public float WaterWaveHeight { get; private set; }
        public float WaterWaveSpeed { get; private set; }
        public float WorldSkynLayers { get; private set; }
        public IList<WaterLayer> WaterLayers { get; private set; }
        public IList<SkyLayer> SkyLayers { get; private set; }
        public IList<SkySatellite> SkySatellites { get; private set; }

        public EnvironmentFile(string fileName)
        {
            using (STFReader stf = new STFReader(fileName, false))
                stf.ParseFile(new STFReader.TokenProcessor[] {
                    new STFReader.TokenProcessor("world", ()=>{ stf.MustMatchBlockStart(); stf.ParseBlock(new STFReader.TokenProcessor[] {
                        new STFReader.TokenProcessor("world_water", ()=>{ stf.MustMatchBlockStart(); stf.ParseBlock(new STFReader.TokenProcessor[] {
                            new STFReader.TokenProcessor("world_water_wave_height", ()=>{ WaterWaveHeight = stf.ReadFloatBlock(STFReader.Units.Distance, null); }),
                            new STFReader.TokenProcessor("world_water_wave_speed", ()=>{ WaterWaveSpeed = stf.ReadFloatBlock(STFReader.Units.Speed, null); }),
                            new STFReader.TokenProcessor("world_water_layers", ()=>{ ParseWaterLayers(stf); }),
                            });}),
                        });}),
                    });

            using (STFReader stf = new STFReader(fileName, false))
                stf.ParseFile(new STFReader.TokenProcessor[] {
                    new STFReader.TokenProcessor("world", ()=>{ stf.MustMatchBlockStart(); stf.ParseBlock(new STFReader.TokenProcessor[] {
                        new STFReader.TokenProcessor("world_sky", ()=>{ stf.MustMatchBlockStart(); stf.ParseBlock(new STFReader.TokenProcessor[] {
                               new STFReader.TokenProcessor("worldskynlayers_behind_satellites", ()=>{ WorldSkynLayers = stf.ReadFloatBlock( STFReader.Units.Any, null ); }),
                               new STFReader.TokenProcessor("world_sky_layers", ()=>{ ParseSkyLayers(stf); }),
                               new STFReader.TokenProcessor("world_sky_satellites", ()=>{ ParseWorldSkySatellites(stf); }),
                               });}),
                        });}),
                    });
        }

        private void ParseWaterLayers(STFReader stf)
        {
            stf.MustMatchBlockStart();
            int texturelayers = stf.ReadInt(null);
            WaterLayers = new List<WaterLayer>()
            {
                Capacity = texturelayers
            };
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("world_water_layer", ()=>{ if(texturelayers-- > 0) WaterLayers.Add(new WaterLayer(stf)); })
            });
        }

        private void ParseSkyLayers(STFReader stf)
        {
            stf.MustMatchBlockStart();
            int skylayers = stf.ReadInt(null);
            SkyLayers = new List<SkyLayer>()
            {
                Capacity = skylayers
            };

            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("world_sky_layer", ()=>{ if(skylayers-- > 0) SkyLayers.Add(new SkyLayer(stf)); })});

        }

        private void ParseWorldSkySatellites(STFReader stf)
        {
            stf.MustMatchBlockStart();
            int skysatellite = stf.ReadInt(null);
            SkySatellites = new List<SkySatellite>()
            { 
                Capacity = skysatellite 
            };

            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("world_sky_satellite", () => { if (skysatellite-- > 0) SkySatellites.Add(new SkySatellite(stf)); })});
        }

    }
}
