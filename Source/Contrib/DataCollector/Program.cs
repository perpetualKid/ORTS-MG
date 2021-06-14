// COPYRIGHT 2014 by the Open Rails project.
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
using System.Linq;

using Orts.Common.Info;
using Orts.Formats.Msts.Files;

[assembly: CLSCompliant(false)]


namespace Orts.DataCollector
{
    internal class Program
    {
#pragma warning disable CA1303 // Do not pass literals as localized parameters
        private static void Main(string[] args)
        {
            Trace.Listeners.Add(new ConsoleTraceListener());

            if (args.Contains("/system", StringComparer.OrdinalIgnoreCase))
                SystemInfo.WriteSystemDetails();
            else if (args.Contains("/tile-terrtex", StringComparer.OrdinalIgnoreCase))
                CollectTileTerrtex(args);
            else
                ShowHelp();
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Open Rails Data Collector utility");
            Console.WriteLine();
            Console.WriteLine("{0} [/system | /tile-terrtex PATH [...]]", Path.GetFileNameWithoutExtension(AppDomain.CurrentDomain.FriendlyName));
            Console.WriteLine();
            //                "1234567890123456789012345678901234567890123456789012345678901234567890123456789"
            Console.WriteLine("    /system       Collects and reports on various system information.");
            Console.WriteLine("    /tile-terrtex Scans the provided PATHs for MSTS tile files (.t) and");
            Console.WriteLine("                  produces a statistical summary of the terrtex used.");
        }

        private struct TileTerrtexDirectory
        {
            public string Path;
            public int TileCount;
            public float Tile1Count;
            public float Tile4Count;
            public float Tile16Count;
            public float Tile64Count;
            public TileTerrtexDirectory(string path)
            {
                Path = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
                TileCount = 0;
                Tile1Count = 0;
                Tile4Count = 0;
                Tile16Count = 0;
                Tile64Count = 0;
            }
        }

        private static void CollectTileTerrtex(string[] args)
        {
            List<TileTerrtexDirectory> summary = new List<TileTerrtexDirectory>();
            foreach (string arg in args)
            {
                if (Directory.Exists(arg))
                {
                    Trace.WriteLine($"Scanning {args}...");
                    foreach (string path in Directory.GetDirectories(arg, "Tiles", SearchOption.AllDirectories))
                    {
                        Trace.WriteLine($"Scanning {path}...");
                        TileTerrtexDirectory data = new TileTerrtexDirectory(path);
                        foreach (string file in Directory.GetFiles(path, "*.t"))
                        {
                            try
                            {
                                TerrainFile t = new TerrainFile(file);
                                if (t.Terrain.Patchsets.Length != 1)
                                    throw new InvalidDataException($"Tile has {t.Terrain.Patchsets.Length} patch sets; expected 1.");
                                if (t.Terrain.Patchsets[0].PatchSize != 16)
                                    throw new InvalidDataException($"Tile has {t.Terrain.Patchsets[0].PatchSize} patches; expected 16.");
                                if (t.Terrain.Patchsets[0].Patches.Count != 256)
                                    throw new InvalidDataException($"Tile has {t.Terrain.Patchsets[0].Patches.Count} patches; expected 256.");

                                data.TileCount++;
                                Formats.Msts.Models.PatchSet patchset = t.Terrain.Patchsets[0];
                                List<string> textures = new List<string>(patchset.PatchSize * patchset.PatchSize);
                                foreach (Formats.Msts.Models.Patch patch in patchset.Patches)
                                {
                                    textures.Add(string.Join("|", (from ts in t.Terrain.Shaders[patch.ShaderIndex].Textureslots
                                                                   select ts.FileName).ToArray()));
                                }

                                // 1th
                                if (textures.Distinct().Count() == 1)
                                    data.Tile1Count++;

                                // 4th
                                List<string>[] textures4 = new List<string>[4];
                                for (int i = 0; i < textures4.Length; i++)
                                    textures4[i] = new List<string>();
                                for (int x = 0; x < 16; x++)
                                {
                                    for (int y = 0; y < 16; y++)
                                    {
                                        int tx = x / 8;
                                        int ty = y / 8;
                                        textures4[tx + ty * 2].Add(textures[x + y * 16]);
                                    }
                                }
                                for (int i = 0; i < textures4.Length; i++)
                                    if (textures4[i].Distinct().Count() == 1)
                                        data.Tile4Count++;

                                // 16th
                                List<string>[] textures16 = new List<string>[16];
                                for (int i = 0; i < textures16.Length; i++)
                                    textures16[i] = new List<string>();
                                for (int x = 0; x < 16; x++)
                                {
                                    for (int y = 0; y < 16; y++)
                                    {
                                        int tx = x / 4;
                                        int ty = y / 4;
                                        textures16[tx + ty * 4].Add(textures[x + y * 16]);
                                    }
                                }
                                for (int i = 0; i < textures16.Length; i++)
                                    if (textures16[i].Distinct().Count() == 1)
                                        data.Tile16Count++;

                                // 64th
                                List<string>[] textures64 = new List<string>[64];
                                for (int i = 0; i < textures64.Length; i++)
                                    textures64[i] = new List<string>();
                                for (int x = 0; x < 16; x++)
                                {
                                    for (int y = 0; y < 16; y++)
                                    {
                                        int tx = x / 2;
                                        int ty = y / 2;
                                        textures64[tx + ty * 8].Add(textures[x + y * 16]);
                                    }
                                }
                                for (int i = 0; i < textures64.Length; i++)
                                    if (textures64[i].Distinct().Count() == 1)
                                        data.Tile64Count++;
                            }
#pragma warning disable CA1031 // Do not catch general exception types
                            catch (Exception error)
#pragma warning restore CA1031 // Do not catch general exception types
                            {
                                Trace.WriteLine($"Error reading tile {file}: {error}");
                            }
                        }
                        if (data.TileCount > 0)
                            summary.Add(data);
                    }
                }
            }
            Trace.WriteLine(string.Empty);
            foreach (TileTerrtexDirectory data in from data in summary
                                                  orderby data.Path
                                                  select data)
            {
                Trace.WriteLine($"{data.Path,30} / {data.TileCount,-4} / 1th {data.Tile1Count / data.TileCount,5:P0} / 4th {data.Tile4Count / 4 / data.TileCount,5:P0} / 16th {data.Tile16Count / 16 / data.TileCount,5:P0} / 64th {data.Tile64Count / 64 / data.TileCount,5:P0}");
            }
        }
#pragma warning restore CA1303 // Do not pass literals as localized parameters
    }
}
