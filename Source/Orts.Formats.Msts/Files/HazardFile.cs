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

namespace Orts.Formats.Msts.Files
{
    public class HazardFile
	{
		public HazardFile(string fileName)
		{
			try
			{
				using (STFReader stf = new STFReader(fileName, false))
				{
					stf.ParseFile(new STFReader.TokenProcessor[] {
                        new STFReader.TokenProcessor("tr_worldfile", ()=>{ Hazard = new Hazard(stf); }),
                    });
					//TODO This should be changed to STFException.TraceError() with defaults values created
					if (Hazard == null) throw new STFException(stf, "Missing Tr_WorldFile");
				}
			}
			finally
			{
			}
		}

		public Hazard Hazard { get; private set; }
	}
}
