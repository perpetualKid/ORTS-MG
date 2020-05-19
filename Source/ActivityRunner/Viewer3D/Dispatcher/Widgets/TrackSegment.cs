using System;
using System.Drawing;

using Microsoft.Xna.Framework;

using Orts.Common.Position;
using Orts.Common.Xna;
using Orts.Formats.Msts.Models;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher.Widgets
{
    internal class TrackSegment: WidgetBase
    {
        private static readonly Pen trackSegmentPen = new Pen(System.Drawing.Color.Gray);

        private Vector2D[] curvePoints = new Vector2D[3];

        public PointF[] CurvePoints { get; } = new PointF[3];

        public bool IsCurved { get; private set; }

        static TrackSegment()
        {
            trackSegmentPen.Width = trackWidth;// standard track width
        }

        public TrackSegment(in WorldLocation start, in WorldLocation end, uint? sectionIndex)
        {
            curvePoints[0] = new Vector2D(start.TileX * WorldLocation.TileSize + start.Location.X, (start.TileZ * WorldLocation.TileSize + start.Location.Z));
            curvePoints[2] = new Vector2D(end.TileX * WorldLocation.TileSize + end.Location.X, (end.TileZ * WorldLocation.TileSize + end.Location.Z));

            if (!sectionIndex.HasValue)
                return;
            TrackSection ts = Program.Simulator.TSectionDat.TrackSections.Get(sectionIndex.Value);
            if (ts != null)
            {
                if (ts.Curved)
                {
                    double offset = (ts.Radius * (1 - Math.Cos(ts.Angle * Math.PI / 360.0)));
                    if (offset < 3)
                        return;
                    Vector3 v = new Vector3((float)((end.TileX - start.TileX) * WorldLocation.TileSize + end.Location.X - start.Location.X), 0,
                        (float)((end.TileZ - start.TileZ) * WorldLocation.TileSize + end.Location.Z - start.Location.Z));
                    IsCurved = true;
                    Vector3 v2 = Vector3.Cross(Vector3.Up, v);
                    v2.Normalize();
                    v = v / 2;
                    v.X += (float)(start.TileX * WorldLocation.TileSize + start.Location.X);
                    v.Z += (float)(start.TileZ * WorldLocation.TileSize + start.Location.Z);
                    if (ts.Angle > 0)
                        v = v2 * -(float)offset + v;
                    else
                        v = v2 * (float)offset + v;
                    curvePoints[1] = new Vector2D(v.X, v.Z);
                }
            }
        }

        internal override void Normalize(in RectangleF bounds)
        {
            CurvePoints[0] = new PointF((float)(curvePoints[0].X - bounds.Location.X), bounds.Size.Height - (float)(curvePoints[0].Y - bounds.Location.Y));
            CurvePoints[1] = new PointF((float)(curvePoints[1].X - bounds.Location.X), bounds.Size.Height - (float)(curvePoints[1].Y - bounds.Location.Y));
            CurvePoints[2] = new PointF((float)(curvePoints[2].X - bounds.Location.X), bounds.Size.Height - (float)(curvePoints[2].Y - bounds.Location.Y));
            curvePoints = null; //no longer needed from here
        }

        internal override void Draw(Graphics g)
        {
            ////skip segments which are outside bounds. 
            //if ((CurvePoints[0].X < content.DisplayPort.X && CurvePoints[2].X < content.DisplayPort.X) ||
            //    (CurvePoints[0].X > content.DisplayPort.X + content.DisplayPort.Width && CurvePoints[2].X > content.DisplayPort.X + content.DisplayPort.Width) ||
            //    (-CurvePoints[0].Y < content.DisplayPort.Y && -CurvePoints[2].Y < content.DisplayPort.Y) ||
            //    (-CurvePoints[0].Y > content.DisplayPort.Y + content.DisplayPort.Height && -CurvePoints[2].Y > content.DisplayPort.Y + content.DisplayPort.Height))
            //    return;
            if (IsCurved)
            {
                g.DrawCurve(trackSegmentPen, CurvePoints);
            }
            else
            {
                g.DrawLine(trackSegmentPen, CurvePoints[0], CurvePoints[2]);
            }
        }
    }
}
