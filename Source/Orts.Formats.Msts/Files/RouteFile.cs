// COPYRIGHT 2009, 2010, 2011, 2013 by the Open Rails project.
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

using Orts.Formats.Msts.Models;
using Orts.Formats.Msts.Parsers;

using System.IO;

namespace Orts.Formats.Msts.Files
{
    public class RouteFile
    {
        public Route Route { get; private set; }
        public ORTrackData TrackData { get; private set; }

        public RouteFile(string fileName)
        {
            string dir = Path.GetDirectoryName(fileName);
            string file = Path.GetFileName(fileName);
            string orFile = Path.Combine(dir, "openrails", file);
            if (File.Exists(orFile))
                fileName = orFile;
            try
            {
                using (STFReader stf = new STFReader(fileName, false))
                {
                    stf.ParseFile(new STFReader.TokenProcessor[] {
                        new STFReader.TokenProcessor("tr_routefile", ()=>{ Route = new Route(stf); }),
                        new STFReader.TokenProcessor("_OpenRails", ()=>{ TrackData = new ORTrackData(stf); }),
                    });
                    if (Route == null)
                        throw new STFException(stf, "Missing Tr_RouteFile");
                }
            }
            finally
            {
                if (TrackData == null)
                    TrackData = new ORTrackData();
            }
        }
    }

    public class ORTrackData
    {
        public float MaxViewingDistance { get; private set; } = float.MaxValue;  // disables local route override

        public ORTrackData()
        {
        }

        internal ORTrackData(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("ortsmaxviewingdistance", ()=>{ MaxViewingDistance = stf.ReadFloatBlock(STFReader.Units.Distance, null); }),
            });
        }
    }

}
