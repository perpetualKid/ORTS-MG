// COPYRIGHT 2009, 2010, 2011, 2012 by the Open Rails project.
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

using Orts.Common.Xna;

namespace Orts.Common.Position
{
    public interface IWorldPosition
    {
        ref readonly WorldPosition WorldPosition { get; }
    }

    /// <summary>
    /// Represents the position and orientation of an object within a tile in XNA coordinates.
    /// </summary>
    public readonly struct WorldPosition
    {
        public const double TileSize = 2048.0;

        /// <summary>The x-value of the tile</summary>
        public readonly int TileX;
        /// <summary>The z-value of the tile</summary>
        public readonly int TileZ;
        /// <summary>The position within a tile (relative to the center of tile)</summary>
        public readonly Matrix XNAMatrix;

        public WorldPosition(int tileX, int tileZ, Matrix xnaMatrix)
        {
            TileX = tileX;
            TileZ = tileZ;
            XNAMatrix = xnaMatrix;
        }

        private static readonly WorldPosition none = new WorldPosition(0, 0, Matrix.Identity);

        /// <summary>
        /// Returns a WorldPosition representing no Position at all.
        /// </summary>
        public static ref readonly WorldPosition None => ref none;

        /// <summary>
        /// Copy constructor using a MSTS-coordinates world-location 
        /// </summary>
        public WorldPosition(in WorldLocation source)
        {
            TileX = source.TileX;
            TileZ = source.TileZ;
            source.Location.Deconstruct(out float x, out float y, out float z);
            XNAMatrix = MatrixExtension.SetTranslation(Matrix.Identity, x, y, -z);
        }

        public WorldPosition ChangeTranslation(float x, float y, float z)
        {
            return new WorldPosition(TileX, TileZ, MatrixExtension.ChangeTranslation(XNAMatrix, x, y, z));
        }

        public WorldPosition SetTranslation(Vector3 translation)
        {
            return new WorldPosition(TileX, TileZ, MatrixExtension.SetTranslation(XNAMatrix, translation));
        }

        public WorldPosition SetTranslation(float x, float y, float z)
        {
            return new WorldPosition(TileX, TileZ, MatrixExtension.SetTranslation(XNAMatrix, x, y, z));
        }

        /// <summary>
        /// The world-location in MSTS coordinates of the current position
        /// </summary>
        public WorldLocation WorldLocation
        {
            // "inlined" XnaMatrix.Translation() Decomposition
            get { return new WorldLocation(TileX, TileZ, XNAMatrix.M41, XNAMatrix.M42, -XNAMatrix.M43); }
        }

        /// <summary>
        /// Describes the location as 3D vector in MSTS coordinates within the tile
        /// </summary>
        public Vector3 Location
        {
            // "inlined" XnaMatrix.Translation() Decomposition
            get { return new Vector3(XNAMatrix.M41, XNAMatrix.M42, -XNAMatrix.M43); }
        }

        /// <summary>
        /// Ensure tile coordinates are within tile boundaries
        /// </summary>
        public WorldPosition Normalize()
        {
            int xTileDistance = (int)Math.Round((int)(XNAMatrix.M41 / 1024) / 2.0, MidpointRounding.AwayFromZero);
            int zTileDistance = (int)Math.Round((int)(XNAMatrix.M43 / 1024) / 2.0, MidpointRounding.AwayFromZero);

            return (xTileDistance == 0 && zTileDistance == 0) ? this : new WorldPosition(TileX + xTileDistance, TileZ + zTileDistance,
                MatrixExtension.SetTranslation(XNAMatrix, (float)(XNAMatrix.M41 - (xTileDistance * TileSize)),
                XNAMatrix.M42, (float)(XNAMatrix.M43 - (zTileDistance * TileSize))));
        }

        /// <summary>
        /// Change tile and location values to make it as if the location where on the requested tile.
        /// </summary>
        /// <param name="tileX">The x-value of the tile to normalize to</param>
        /// <param name="tileZ">The x-value of the tile to normalize to</param>
        public WorldPosition NormalizeTo(int tileX, int tileZ)
        {
            int xDiff = TileX - tileX;
            int zDiff = TileZ - tileZ;

            return (xDiff == 0 && zDiff == 0) ? this : new WorldPosition(tileX, tileZ, 
                MatrixExtension.SetTranslation(XNAMatrix, (float)(XNAMatrix.M41 + (xDiff * TileSize)),
                XNAMatrix.M42, (float)(XNAMatrix.M43 + (zDiff * TileSize))));
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
    public readonly struct WorldLocation
    {
        public const double TileSize = 2048.0;
		private static readonly WorldLocation none = new WorldLocation();

        /// <summary>
        /// Returns a WorldLocation representing no location at all.
        /// </summary>
        public static ref readonly WorldLocation None => ref none;

        /// <summary>The x-value of the tile</summary>
        public readonly int TileX;
        /// <summary>The z-value of the tile</summary>
        public readonly int TileZ;
        /// <summary>The vector to the location within a tile, relative to center of tile in MSTS coordinates</summary>
        public readonly Vector3 Location;

        /// <summary>
        /// Constructor using values for tileX, tileZ, x, y, and z.
        /// </summary>
        public WorldLocation(int tileX, int tileZ, float x, float y, float z, bool normalize = false): 
            this(tileX, tileZ, new Vector3(x, y, z), normalize)
        {
        }

        /// <summary>
        /// Constructor using values for tileX and tileZ, and a vector for x, y, z
        /// </summary>
        public WorldLocation(int tileX, int tileZ, Vector3 location, bool normalize = false)
        {
            TileX = tileX;
            TileZ = tileZ;
            Location = location;
            if (normalize)
            {
                this = Normalize();
            }

        }

        /// <summary>
        /// Ensure tile coordinates are within tile boundaries
        /// </summary>
        public WorldLocation Normalize()
        {
            int xTileDistance = (int)Math.Round((int)(Location.X / 1024) / 2.0, MidpointRounding.AwayFromZero);
            int zTileDistance = (int)Math.Round((int)(Location.Z / 1024) / 2.0, MidpointRounding.AwayFromZero);

            return (xTileDistance == 0 && zTileDistance == 0) ? this : new WorldLocation(TileX + xTileDistance, TileZ + zTileDistance, 
                new Vector3((float)(Location.X - (xTileDistance * TileSize)), Location.Y, (float)(Location.Z - (zTileDistance * TileSize))));
        }

        /// <summary>
        /// Change tile and location values to make it as if the location where on the requested tile.
        /// </summary>
        /// <param name="tileX">The x-value of the tile to normalize to</param>
        /// <param name="tileZ">The x-value of the tile to normalize to</param>
        public WorldLocation NormalizeTo(int tileX, int tileZ)
        {
            int xDiff = TileX - tileX;
            int zDiff = TileZ - tileZ;

            return (xDiff == 0 && zDiff == 0) ? this : new WorldLocation(tileX, tileZ, 
                new Vector3((float)(Location.X + (xDiff * TileSize)), Location.Y, (float)(Location.Z + (zDiff * TileSize))));
        }

        /// <summary>
        /// Helper method to set the elevation only to specified value
        /// </summary>
        /// <param name="elevation"></param>
        /// <returns></returns>
        public WorldLocation SetElevation(float elevation)
        {
            return new WorldLocation(TileX, TileZ, Location.X, elevation, Location.Z);
        }

        /// <summary>
        /// Helper method to change (update) the elevation only by specifed delta
        /// </summary>
        /// <param name="delta"></param>
        /// <returns></returns>
        public WorldLocation ChangeElevation(float delta)
        {
            return new WorldLocation(TileX, TileZ, Location.X, Location.Y + delta, Location.Z);
        }

        /// <summary>
        /// Check whether location1 and location2 are within given distance (in meters) from each other in 3D
        /// </summary>
        public static bool Within(in WorldLocation location1, in WorldLocation location2, float distance)
        {
            return GetDistanceSquared(location1, location2) <= distance * distance;
        }

        /// <summary>
        /// Get squared distance between two world locations (in meters)
        /// </summary>
        public static double GetDistanceSquared(in WorldLocation location1, in WorldLocation location2)
        {
            double dx = location1.Location.X - location2.Location.X;
            double dy = location1.Location.Y - location2.Location.Y;
            double dz = location1.Location.Z - location2.Location.Z;
            dx += TileSize * (location1.TileX - location2.TileX);
            dz += TileSize * (location1.TileZ - location2.TileZ);
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>
        /// Get squared distance between two world locations (in meters), neglecting elevation (y) information
        /// </summary>
        public static double GetDistanceSquared2D(in WorldLocation location1, in WorldLocation location2)
        {
            double dx = location1.Location.X - location2.Location.X;
            double dz = location1.Location.Z - location2.Location.Z;
            dx += TileSize * (location1.TileX - location2.TileX);
            dz += TileSize * (location1.TileZ - location2.TileZ);
            return dx * dx + dz * dz;
        }
        /// <summary>
        /// Get a (3D) vector pointing locationFrom to locationTo
        /// </summary>
        public static Vector3 GetDistance(in WorldLocation locationFrom, in WorldLocation locationTo)
        {
            return new Vector3(
                (float)(locationTo.Location.X - locationFrom.Location.X + (locationTo.TileX - locationFrom.TileX) * TileSize), 
                locationTo.Location.Y - locationFrom.Location.Y, 
                (float)(locationTo.Location.Z - locationFrom.Location.Z + (locationTo.TileZ - locationFrom.TileZ) * TileSize));
        }

        /// <summary>
        /// Get a (2D) vector pointing from locationFrom to locationTo, neglecting elevation (y) information
        /// </summary>
        public static Vector2 GetDistance2D(in WorldLocation locationFrom, in WorldLocation locationTo)
        {
            return new Vector2(
                (float)(locationTo.Location.X - locationFrom.Location.X + (locationTo.TileX - locationFrom.TileX) * TileSize), 
                (float)(locationTo.Location.Z - locationFrom.Location.Z + (locationTo.TileZ - locationFrom.TileZ) * TileSize));
        }

        /// <summary>
        /// Create a nice string-representation of the world location
        /// </summary>
        public override string ToString()
        {
            return $"{{TileX:{TileX} TileZ:{TileZ} X:{Location.X} Y:{Location.Y} Z:{Location.Z}}}";
        }

        /// <summary>
        /// Save the object to binary format
        /// </summary>
        /// <param name="outf">output file</param>
        public static void Save(in WorldLocation instance, BinaryWriter outf)
        {
            outf.Write(instance.TileX);
            outf.Write(instance.TileZ);
            outf.Write(instance.Location.X);
            outf.Write(instance.Location.Y);
            outf.Write(instance.Location.Z);
        }

        /// <summary>
        /// Restore the object from binary format
        /// </summary>
        /// <param name="inf">input file</param>
        public static WorldLocation Restore(BinaryReader inf)
        {
            int tileX = inf.ReadInt32();
            int tileZ = inf.ReadInt32();
            float x = inf.ReadSingle();
            float y = inf.ReadSingle();
            float z = inf.ReadSingle();
            return new WorldLocation(tileX, tileZ, x, y, z);
        }

        public static bool operator ==(in WorldLocation a, in WorldLocation b)
        {
            return a.TileX == b.TileX && a.TileZ == b.TileZ && a.Location == b.Location;
        }

        public static bool operator !=(in WorldLocation a, in WorldLocation b)
        {
            return a.TileX != b.TileX || a.TileZ != b.TileZ || a.Location != b.Location;
        }

        public override bool Equals(object obj)
        {
            return (obj is WorldLocation other && this == other);
        }

        public override int GetHashCode()
        {
            return TileX.GetHashCode() ^ TileZ.GetHashCode() ^ Location.GetHashCode();
        }
	}
}
