// COPYRIGHT 2011, 2012 by the Open Rails project.
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

using Orts.Common;
using Orts.Formats.Msts.Parsers;

using System.IO;

// <Comment> This file parses only the shape names for temporary speed restrictions; the other shape names are not needed
// </Comment>
namespace Orts.Formats.Msts.Files
{
    public enum SpeedPostShapeNames
    { 
        Warning,
        StartRestriction,
        EndRestriction
    }

    public class SpeedpostDatFile
	{
        /// <summary>
        /// contains only shape names for temporary speed restrictions (warning, begin, end)
        /// </summary>
        public EnumArray<string, SpeedPostShapeNames> ShapeNames { get; } = new EnumArray<string, SpeedPostShapeNames>();

		public SpeedpostDatFile(string fileName, string shapePath)
		{
			using (STFReader stf = new STFReader(fileName, false))
			{
                stf.ParseBlock(new STFReader.TokenProcessor[] {
                    new STFReader.TokenProcessor("speed_warning_sign_shape", () => ReadShapeInfo(stf, SpeedPostShapeNames.Warning, shapePath)),
                    new STFReader.TokenProcessor("restricted_shape", () => ReadShapeInfo(stf, SpeedPostShapeNames.StartRestriction, shapePath)),
                    new STFReader.TokenProcessor("end_restricted_shape", () => ReadShapeInfo(stf, SpeedPostShapeNames.EndRestriction, shapePath)),
                });
			}
		}

        private void ReadShapeInfo(STFReader stf, SpeedPostShapeNames shapeName, string path)
        {
            string dataItem = stf.ReadStringBlock(null);
            if (dataItem != null)
            {
                dataItem = Path.Combine(path, dataItem);
                if (File.Exists(dataItem))
                    ShapeNames[shapeName] = dataItem;
                else
                    STFException.TraceWarning(stf, $"Non-existent shape file {dataItem} referenced");
            }
        }
    } // class SpeedpostDatFile
}

