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

using Microsoft.Xna.Framework;

using Orts.Formats.Msts.Models;
using Orts.Formats.Msts.Parsers;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;

namespace Orts.Formats.Msts.Files
{
    public class CabViewFile
	{
        public IList<Vector3> Locations { get; } = new List<Vector3>();   // Head locations for front, left and right views
        public IList<Vector3> Directions { get; } = new List<Vector3>();  // Head directions for each view
        public StringCollection Views2D { get; } = new StringCollection();     // 2D CAB Views - by GeorgeS
        public StringCollection ViewsNight { get; } = new StringCollection();    // Night CAB Views - by GeorgeS
        public StringCollection ViewsLight { get; } = new StringCollection();    // Light CAB Views - by GeorgeS
        public CabViewControls CabViewControls { get; private set; }                 // Controls in CAB - by GeorgeS

        public CabViewFile(string fileName) : 
            this(Path.GetDirectoryName(fileName), fileName)
        {

        }

        public CabViewFile(string basePath, string fileName)
		{
            using (STFReader stf = new STFReader(Path.GetFullPath(Path.Combine(basePath, fileName)), false))
                stf.ParseFile(new STFReader.TokenProcessor[] {
                    new STFReader.TokenProcessor("tr_cabviewfile", ()=>{ stf.MustMatchBlockStart(); stf.ParseBlock(new STFReader.TokenProcessor[] {
                        new STFReader.TokenProcessor("position", ()=>{ Locations.Add(stf.ReadVector3Block(STFReader.Units.None, new Vector3())); }),
                        new STFReader.TokenProcessor("direction", ()=>{ Directions.Add(stf.ReadVector3Block(STFReader.Units.None, new Vector3())); }),
                        new STFReader.TokenProcessor("cabviewfile", ()=>{
                            string cvfileName = stf.ReadStringBlock(null);
                            string path = Path.Combine(basePath, Path.GetDirectoryName(cvfileName));
                            string name = Path.GetFileName(cvfileName);

                            // Use *Frnt1024.ace if available
                            if (!Path.GetFileNameWithoutExtension(cvfileName).EndsWith("1024", StringComparison.OrdinalIgnoreCase))
                            {
                                string name1024 = Path.GetFileNameWithoutExtension(cvfileName) + "1024" + Path.GetExtension(cvfileName);
                                if (File.Exists(Path.Combine(path, name1024)))
                                    name = name1024;
                            }

                            Views2D.Add(Path.Combine(path, name));
                            ViewsNight.Add(Path.Combine(path, "NIGHT", name));
                            ViewsLight.Add(Path.Combine(path, "CABLIGHT", name));
                        }),
                        new STFReader.TokenProcessor("cabviewcontrols", ()=>{ CabViewControls = new CabViewControls(stf, basePath); }),
                    });}),
                });
		}

	} // class CVFFile
}

