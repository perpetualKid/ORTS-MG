﻿
using System;

using Microsoft.Xna.Framework;

using Orts.Common;
using Orts.Common.Position;
using Orts.Formats.Msts.Models;
using Orts.View.Track.Shapes;
using Orts.View.Xna;

namespace Orts.View.Track.Widgets
{
    internal class TrackSegment: VectorWidget
    {
        internal readonly bool Curved;
        
        internal readonly float Direction;
        internal readonly float Length;

        internal readonly uint TrackNodeIndex;

        internal readonly float Angle;

        public TrackSegment(TrackVectorSection trackVectorSection, TrackSection trackSection, uint trackNodeIndex)
        {
            ref readonly WorldLocation location = ref trackVectorSection.Location;
            double cosA = Math.Cos(trackVectorSection.Direction.Y);
            double sinA = Math.Sin(trackVectorSection.Direction.Y);

            if (trackSection.Curved)
            {
                Angle = trackSection.Angle;
                Length = trackSection.Radius;

                double length = trackSection.Radius * Math.Abs(MathHelper.ToRadians(trackSection.Angle));

                int sign = -Math.Sign(trackSection.Angle);
                double angleRadians = -length / trackSection.Radius;
                double cosArotated = Math.Cos(trackVectorSection.Direction.Y + sign * angleRadians);
                double sinArotated = Math.Sin(trackVectorSection.Direction.Y + sign * angleRadians);
                double deltaX = sign * trackSection.Radius * (cosA - cosArotated);
                double deltaZ = sign * trackSection.Radius * (sinA - sinArotated);
                vector = new PointD(location.TileX * WorldLocation.TileSize + location.Location.X - deltaX, location.TileZ * WorldLocation.TileSize + location.Location.Z + deltaZ);
            }
            else
            {
                Length = trackSection.Length;

                // note, angle is 90 degrees off, and different sign. 
                // So Delta X = cos(90-A)=sin(A); Delta Y,Z = sin(90-A) = cos(A)    
                vector = new PointD(location.TileX * WorldLocation.TileSize + location.Location.X + sinA * Length, location.TileZ * WorldLocation.TileSize + location.Location.Z +cosA * Length);
            }

            base.location = PointD.FromWorldLocation(location);
            tile = new Tile(location.TileX, location.TileZ);
            otherTile = new Tile(Tile.TileFromAbs(vector.X), Tile.TileFromAbs(vector.Y));
            Size = trackSection.Width;
            Curved = trackSection.Curved;
            Direction = trackVectorSection.Direction.Y - MathHelper.PiOver2;
            TrackNodeIndex = trackNodeIndex;
        }

        internal override void Draw(ContentArea contentArea, ColorVariation colorVariation = ColorVariation.None)
        {
            Color drawColor = GetColor<TrackSegment>(colorVariation);
            if (Curved)
                BasicShapes.DrawArc(contentArea.WorldToScreenSize(Size), drawColor, contentArea.WorldToScreenCoordinates(in Location), contentArea.WorldToScreenSize(Length), Direction, Angle, 0, contentArea.SpriteBatch);
            else
                BasicShapes.DrawLine(contentArea.WorldToScreenSize(Size), drawColor, contentArea.WorldToScreenCoordinates(in Location), contentArea.WorldToScreenSize(Length), Direction, contentArea.SpriteBatch);
        }
    }

    internal class RoadSegment : TrackSegment
    {
        public RoadSegment(TrackVectorSection trackVectorSection, TrackSection trackSection, uint trackNodeIndex) : base(trackVectorSection, trackSection, trackNodeIndex)
        {
        }

        internal override void Draw(ContentArea contentArea, ColorVariation colorVariation = ColorVariation.None)
        {
            Color drawColor = GetColor<RoadSegment>(colorVariation);
            if (Curved)
                BasicShapes.DrawArc(contentArea.WorldToScreenSize(Size), drawColor, contentArea.WorldToScreenCoordinates(in Location), contentArea.WorldToScreenSize(Length), Direction, Angle, 0, contentArea.SpriteBatch);
            else
                BasicShapes.DrawLine(contentArea.WorldToScreenSize(Size), drawColor, contentArea.WorldToScreenCoordinates(in Location), contentArea.WorldToScreenSize(Length), Direction, contentArea.SpriteBatch);
        }
    }
}
