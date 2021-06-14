// COPYRIGHT 2009, 2010, 2011, 2012, 2013, 2014 by the Open Rails project.
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

using Orts.Formats.Msts.Models;
using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Files
{
    public class WorldFile
    {
        public int TileX { get; private set; }
        public int TileZ { get; private set; }
        public WorldObjects Objects { get; private set; }

        public WorldFile(string fileName)
            : this(fileName, null)
        {
        }

        public WorldFile(string fileName, IList<TokenID> allowedTokens)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                    throw new InvalidDataException($"Empty filename could not be parsed for for X and Z tile information.");
                // Parse the tile location out of the filename.
                int p = fileName.LastIndexOf("\\WORLD\\W", StringComparison.OrdinalIgnoreCase);
                if (!int.TryParse(fileName.Substring(p + 8, 7), out int tileX) || !int.TryParse(fileName.Substring(p + 15, 7), out int tileZ))
                    throw new InvalidDataException($"Could not parse filename {fileName} for X and Z tile information.");
                TileX = tileX;
                TileZ = tileZ;

                using (SBR sbr = SBR.Open(fileName))
                {
                    using (SBR block = sbr.ReadSubBlock())
                    {
                        Objects = new WorldObjects(block, allowedTokens, TileX, TileZ);
                    }
                    // some w files have additional comments at the end 
                    //       eg _Skip ( "TS DB-Utility - Version: 3.4.05(13.10.2009), Filetype='World', Copyright (C) 2003-2009 by ...CarlosHR..." )
                    sbr.Skip();
                }
            }
            catch (Exception error)
            {
                throw new FileLoadException(fileName, error);
            }
        }

        public void InsertORSpecificData (string fileName)
        {
            using (SBR sbr = SBR.Open(fileName))
            {
                using (SBR block = sbr.ReadSubBlock())
                {
                    Objects.InsertORSpecificData(block);
                }
                // some w files have additional comments at the end 
                //       eg _Skip ( "TS DB-Utility - Version: 3.4.05(13.10.2009), Filetype='World', Copyright (C) 2003-2009 by ...CarlosHR..." )
                sbr.Skip();
            }
        }
    }
}
