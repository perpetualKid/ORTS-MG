// COPYRIGHT 2018 by the Open Rails project.
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

using Orts.Formats.Msts.Files;
using Path = System.IO.Path;

namespace Orts.ContentChecker
{
    /// <summary>
    /// Loader class for the tsection.dat file in a Route folder
    /// </summary>
    internal class TSectionLoader : Loader
    {
        private TrackSectionsFile globalTsection;

        /// <summary>
        /// default constructor when not enough information is available
        /// </summary>
        public TSectionLoader() : base()
        {
            IsDependent = true;
        }

        /// <summary>
        /// Constructor giving the information this loaded depends on
        /// </summary>
        /// <param name="globalTsection">The global Tsection that is used as a base</param>
        public TSectionLoader(TrackSectionsFile globalTsection) : this()
        {
            this.globalTsection = globalTsection;
        }


        /// <summary>
        /// Try to load the file.
        /// Possibly this might raise an exception. That exception is not caught here
        /// </summary>
        /// <param name="file">The file that needs to be loaded</param>
        public override void TryLoading(string file)
        {
            string subdirname = Path.GetFileName(Path.GetDirectoryName(file));
            if (subdirname.Equals("openrails", System.StringComparison.OrdinalIgnoreCase))
            {
                //todo Need good examples for this. Might not actually be found via SIMIS header
                //Also not clear if this needs a global tracksection or not
                _ = new TrackSectionsFile(file);
            }
            else
            {
                globalTsection.AddRouteTSectionDatFile(file);
            }


        }
    }
}

