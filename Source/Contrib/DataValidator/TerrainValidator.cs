// COPYRIGHT 2017 by the Open Rails project.
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
using System.Diagnostics;
using Orts.Formats.Msts.Files;

namespace Orts.DataValidator
{
    internal class TerrainValidator : Validator
    {
        public TerrainValidator(string file)
            : base(file)
        {
            try
            {
                var parsed = new TerrainFile(File);
                if (File.Contains("\\lo_tiles\\"))
                {
                    Equal(TraceEventType.Warning, 64, parsed.Terrain.Samples.SampleCount, "terrain_nsamples");
                    Equal(TraceEventType.Warning, 256, parsed.Terrain.Samples.SampleSize, "terrain_sample_size");
                }
                else
                {
                    Equal(TraceEventType.Warning, 256, parsed.Terrain.Samples.SampleCount, "terrain_nsamples");
                    Equal(TraceEventType.Warning, 8, parsed.Terrain.Samples.SampleSize, "terrain_sample_size");
                }
                Equal(TraceEventType.Warning, 0, parsed.Terrain.Samples.SampleRotation, "terrain_sample_rotation");
                ValidFileRef(TraceEventType.Error, parsed.Terrain.Samples.SampleBufferE, "terrain_sample_ebuffer");
                ValidFileRef(TraceEventType.Error, parsed.Terrain.Samples.SampleBufferN, "terrain_sample_nbuffer");
                ValidFileRef(TraceEventType.Error, parsed.Terrain.Samples.SampleBufferY, "terrain_sample_ybuffer");
            }
            catch (Exception error)
            {
                Trace.WriteLine(error);
            }
        }
    }
}
