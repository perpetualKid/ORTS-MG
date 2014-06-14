﻿// COPYRIGHT 2009, 2010, 2011, 2012 by the Open Rails project.
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

/*
 * 
 * COORDINATE SYSTEMS - XNA uses a different coordinate system than MSTS.  In XNA, +ve Z is toward the camera, 
 * whereas in MSTS it is the opposite.  As a result you will see the sign of all Z coordinates gets negated
 * and matrices are adjusted as they are loaded into XNA.  In addition the winding order of triangles is reversed in XNA.
 * Generally - X,Y,Z coordinates, vectors, quaternions, and angles will be expressed using MSTS coordinates 
 * unless otherwise noted with the prefix XNA.  Matrix's are usually constructed using XNA coordinates so they can be 
 * used directly in XNA draw routines.  So most matrix's will have XNA prepended to their name.
 * 
 * WorldCoordinates
 * X increases to the east
 * Y increases up
 * Z increases to the north
 * AX increases tilting down
 * AY increases turning to the right
 * 
 * LEXICON
 * Location - the x,y,z point where the center of the object is located - usually a Vector3
 * Pose - the orientation of an object in 3D, ie tilt, rotation - usually an XNAMatrix
 * Position - combines pose and location
 * WorldLocation - adds tile coordinates to a Location
 * WorldPosition - adds tile coordinates to a Position
 */

using System;
using System.IO;
using Microsoft.Xna.Framework;

namespace ORTS.Common
{
    /// <summary>
    /// Represents the position and orientation of an object within a tile in XNA coordinates.
    /// </summary>
    public class WorldPosition
    {
        /// <summary>The x-value of the tile</summary>
        public int TileX { get; set; }
        /// <summary>The z-value of the tile</summary>
        public int TileZ { get; set; }
        /// <summary>The position within a tile (relative to the center of tile)</summary>
        public Matrix XNAMatrix = Matrix.Identity;

        /// <summary>
        /// Default empty constructor
        /// </summary>
        public WorldPosition()
        {
        }

        /// <summary>
        /// Copy constructor using another world position
        /// </summary>
        public WorldPosition(WorldPosition copy)
        {
            TileX = copy.TileX;
            TileZ = copy.TileZ;
            XNAMatrix = copy.XNAMatrix;
        }

        /// <summary>
        /// Copy constructor using a MSTS-coordinates world-location 
        /// </summary>
        public WorldPosition(WorldLocation copy)
        {
            TileX = copy.TileX;
            TileZ = copy.TileZ;
            Location = copy.Location;
        }

        /// <summary>
        /// The world-location in MSTS coordinates of the current position
        /// </summary>
        public WorldLocation WorldLocation
        {
            get
            {
                WorldLocation worldLocation = new WorldLocation();
                worldLocation.TileX = TileX;
                worldLocation.TileZ = TileZ;
                worldLocation.Location = XNAMatrix.Translation;
                worldLocation.Location.Z *= -1;  // convert to MSTS coordinates
                return worldLocation;
            }
        }

        /// <summary>
        /// Describes the location as 3D vector in MSTS coordinates within the tile
        /// </summary>
        public Vector3 Location
        {
            get
            {
                Vector3 location = XNAMatrix.Translation;
                location.Z *= -1;  // convert to MSTS coordinates
                return location;
            }
            set
            {
                value.Z *= -1;
                XNAMatrix.Translation = value;
            }
        }

        /// <summary>
        /// Ensure tile coordinates are within tile boundaries
        /// </summary>
        public void Normalize()
        {
            var TileLocation = XNAMatrix.Translation;
            while (TileLocation.X > 1024) { TileLocation.X -= 2048; TileX++; }
            while (TileLocation.X < -1024) { TileLocation.X += 2048; TileX--; }
            while (TileLocation.Z > 1024) { TileLocation.Z -= 2048; TileZ++; }
            while (TileLocation.Z < -1024) { TileLocation.Z += 2048; TileZ--; }
            XNAMatrix.Translation = TileLocation;
        }

        /// <summary>
        /// Change tile and location values to make it as if the location where on the requested tile.
        /// </summary>
        /// <param name="tileX">The x-value of the tile to normalize to</param>
        /// <param name="tileZ">The x-value of the tile to normalize to</param>
        public void NormalizeTo(int tileX, int tileZ)
        {
            Vector3 TileLocation = XNAMatrix.Translation;
            while (TileX < tileX) { TileLocation.X -= 2048; TileX++; }
            while (TileX > tileX) { TileLocation.X += 2048; TileX--; }
            while (TileZ < tileZ) { TileLocation.Z -= 2048; TileZ++; }
            while (TileZ > tileZ) { TileLocation.Z += 2048; TileZ--; }
            XNAMatrix.Translation = TileLocation;
        }

        /// <summary>
        /// Create a nice string-representation of the world position
        /// </summary>
        public override string ToString()
        {
            return WorldLocation.ToString();
        }
    }

    /// <summary>
    /// Represents the position of an object within a tile in MSTS coordinates.
    /// </summary>
    public class WorldLocation
    {
        /// <summary>The x-value of the tile</summary>
        public int TileX { get; set; }
        /// <summary>The z-value of the tile</summary>
        public int TileZ { get; set; }
        /// <summary>The vector to the location within a tile, relative to center of tile in MSTS coordinates</summary>
        public Vector3 Location;

        /// <summary>
        /// Default empty constructor
        /// </summary>
        public WorldLocation()
        {
            Location = new Vector3();
        }

        /// <summary>
        /// Constructor from another location
        /// </summary>
        /// <param name="worldLocation">the other location to use as initialization</param>
        public WorldLocation(WorldLocation worldLocation)
        {
            TileX = worldLocation.TileX;
            TileZ = worldLocation.TileZ;
            Location = worldLocation.Location;
        }

        /// <summary>
        /// Constructor using values for tileX, tileZ, x, y, and z.
        /// </summary>
        public WorldLocation(int tileX, int tileZ, float x, float y, float z)
        {
            TileX = tileX;
            TileZ = tileZ;
            Location = new Vector3(x, y, z);
        }

        /// <summary>
        /// Constructor using values for tileX and tileZ, and a vector for x, y, z
        /// </summary>
        public WorldLocation(int tileX, int tileZ, Vector3 location)
        {
            TileX = tileX;
            TileZ = tileZ;
            Location = location;
        }

        /// <summary>
        /// Ensure tile coordinates are within tile boundaries
        /// </summary>
        public void Normalize()
        {
            while (Location.X >= 1024) { Location.X -= 2048; TileX++; }
            while (Location.X < -1024) { Location.X += 2048; TileX--; }
            while (Location.Z >= 1024) { Location.Z -= 2048; TileZ++; }
            while (Location.Z < -1024) { Location.Z += 2048; TileZ--; }
        }

        /// <summary>
        /// Change tile and location values to make it as if the location where on the requested tile.
        /// </summary>
        /// <param name="tileX">The x-value of the tile to normalize to</param>
        /// <param name="tileZ">The x-value of the tile to normalize to</param>
        public void NormalizeTo(int tileX, int tileZ)
        {
            while (TileX < tileX) { Location.X -= 2048; TileX++; }
            while (TileX > tileX) { Location.X += 2048; TileX--; }
            while (TileZ < tileZ) { Location.Z -= 2048; TileZ++; }
            while (TileZ > tileZ) { Location.Z += 2048; TileZ--; }
        }

        /// <summary>
        /// Check whether location1 and location2 are within given distance from each other
        /// </summary>
        /// <param name="location1">first location</param>
        /// <param name="location2">second location</param>
        /// <param name="distance">distance defining the boundary between 'within' and 'outside'</param>
        public static bool Within(WorldLocation location1, WorldLocation location2, float distance)
        {
            return GetDistanceSquared(location1, location2) < distance * distance;
        }

        /// <summary>
        /// Get squared distance between two world locations (in meters)
        /// </summary>
        public static float GetDistanceSquared(WorldLocation location1, WorldLocation location2)
        {
            var dx = location1.Location.X - location2.Location.X;
            var dy = location1.Location.Y - location2.Location.Y;
            var dz = location1.Location.Z - location2.Location.Z;
            dx += 2048 * (location1.TileX - location2.TileX);
            dz += 2048 * (location1.TileZ - location2.TileZ);
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>
        /// Get a (3D) vector pointing from <paramref name="locationFrom"/> to <paramref name="locationTo"/>
        /// </summary>
        public static Vector3 GetDistance(WorldLocation locationFrom, WorldLocation locationTo)
        {
            return new Vector3(locationTo.Location.X - locationFrom.Location.X + (locationTo.TileX - locationFrom.TileX) * 2048, locationTo.Location.Y - locationFrom.Location.Y, locationTo.Location.Z - locationFrom.Location.Z + (locationTo.TileZ - locationFrom.TileZ) * 2048);
        }

        /// <summary>
        /// Get a (2D) vector pointing from <paramref name="locationFrom"/> to <paramref name="locationTo"/>, so neglecting height (y) information
        /// </summary>
        public static Vector2 GetDistance2D(WorldLocation locationFrom, WorldLocation locationTo)
        {
            return new Vector2(locationTo.Location.X - locationFrom.Location.X + (locationTo.TileX - locationFrom.TileX) * 2048, locationTo.Location.Z - locationFrom.Location.Z + (locationTo.TileZ - locationFrom.TileZ) * 2048);
        }

        /// <summary>
        /// Create a nice string-representation of the world location
        /// </summary>
        public override string ToString()
        {
            return String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{TileX:{0} TileZ:{1} X:{2} Y:{3} Z:{4}}}", TileX, TileZ, Location.X, Location.Y, Location.Z);
        }

        /// <summary>
        /// Save the object to binary format
        /// </summary>
        /// <param name="outf">output file</param>
        public void Save(BinaryWriter outf)
        {
            outf.Write(TileX);
            outf.Write(TileZ);
            outf.Write(Location.X);
            outf.Write(Location.Y);
            outf.Write(Location.Z);
        }

        /// <summary>
        /// Restore the object from binary format
        /// </summary>
        /// <param name="inf">input file</param>
        public void Restore(BinaryReader inf)
        {
            TileX = inf.ReadInt32();
            TileZ = inf.ReadInt32();
            Location.X = inf.ReadSingle();
            Location.Y = inf.ReadSingle();
            Location.Z = inf.ReadSingle();
        }
    }
}
