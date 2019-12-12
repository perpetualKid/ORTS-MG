using System;
using System.Drawing;

using Microsoft.Xna.Framework;

using Orts.Common.Position;
using Orts.Formats.Msts;
using Orts.Formats.Msts.Models;
using Orts.Simulation.Signalling;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher
{
    #region Widget base class
    public abstract class Widget
    {
        public Vector2 Location { get; protected set; }

        protected static Vector2 VectorFromLocation(in WorldLocation location)
        {
            return new Vector2((location.TileX * WorldLocation.TileSize + location.Location.X), (location.TileZ * WorldLocation.TileSize + location.Location.Z));
        }

    }
    #endregion

    #region SignalWidget
    /// <summary>
    /// Defines a signal being drawn in a 2D view.
    /// </summary>
    public class SignalWidget : Widget
    {
        public TrackItem Item { get; private set; }
        /// <summary>
        /// The underlying signal object as referenced by the TrItem.
        /// </summary>
        public Signal Signal { get; private set; }

        public Vector2 Direction { get; private set; }
        public bool DirectionEnabled { get; private set; }

        /// <summary>
        /// For now, returns true if any of the signal heads shows any "clear" aspect.
        /// This obviously needs some refinement.
        /// </summary>
        public int CanProceed
        {
            get
            {
                int returnValue = 2;

                foreach (var head in Signal.SignalHeads)
                {
                    if (head.state == SignalAspectState.Clear_1 ||
                        head.state == SignalAspectState.Clear_2)
                    {
                        returnValue = 0;
                    }
                    if (head.state == SignalAspectState.Approach_1 ||
                        head.state == SignalAspectState.Approach_2 || head.state == SignalAspectState.Approach_3)
                    {
                        returnValue = 1;
                    }
                }
                return returnValue;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="item"></param>
        /// <param name="signal"></param>
        public SignalWidget(SignalItem item, Signal signal)
        {
            Item = item;
            Signal = signal;
            DirectionEnabled = false;
            Location = VectorFromLocation(item.Location);
            try
            {
                TrackNode node = Program.Simulator.TDB.TrackDB.TrackNodes[signal.trackNode];
                if (node is TrackVectorNode trackVectorNode)
                {
                    var ts = trackVectorNode.TrackVectorSections[0];
                    Direction = VectorFromLocation(ts.Location);
                }
                else if (node is TrackJunctionNode)
                {
                    var ts = node.UiD;
                    Direction = VectorFromLocation(ts.Location);
                }
                else
                    throw new ArgumentException();
                var v1 = new Vector2(Location.X, Location.Y);
                var v3 = v1 - Direction;
                v3.Normalize();
                Direction = v1 - Vector2.Multiply(v3, signal.direction == 0 ? 12f : -12f);
                Direction = v1 - Vector2.Multiply(v3, signal.direction == 0 ? 1.5f : -1.5f);//shift signal along the dir for 2m, so signals will not be overlapped
                Location = Direction;
                DirectionEnabled = true;
            }
            catch { }
        }
    }
    #endregion

    #region SwitchWidget
    /// <summary>
    /// Defines a signal being drawn in a 2D view.
    /// </summary>
    public class SwitchWidget : Widget
    {
        public TrackJunctionNode Item { get; private set; }
        public uint MainRoute { get; private set; }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="item"></param>
        /// <param name="signal"></param>
        public SwitchWidget(TrackJunctionNode item)
        {
            Item = item;
            MainRoute = Program.Simulator.TSectionDat.TrackShapes[item.ShapeIndex]?.MainRoute ?? 0;  // TSECTION.DAT tells us which is the main route
            Location = VectorFromLocation(Item.UiD.Location);
        }
    }

    #endregion

    #region LineSegment
    /// <summary>
    /// Defines a geometric line segment.
    /// </summary>
    public class LineSegment
    {
        public ref PointF StartPoint => ref CurvePoints[0];
        public ref PointF EndPoint => ref CurvePoints[2];
        public ref  PointF MidPoint => ref CurvePoints[1];

        public PointF[] CurvePoints { get; } = new PointF[3];

        public bool IsCurved { get; private set; }

        const double TileSize = 2048.0;

        public LineSegment(in WorldLocation start, in WorldLocation end, uint? sectionIndex)
        {
            CurvePoints[0] = new PointF((float)(start.TileX * TileSize + start.Location.X), (float)(start.TileZ * TileSize + start.Location.Z));
            CurvePoints[2] = new PointF((float)(end.TileX * TileSize + end.Location.X), (float)(end.TileZ * TileSize + end.Location.Z));

            if (!sectionIndex.HasValue)
                return;
            TrackSection ts = Program.Simulator.TSectionDat.TrackSections.Get(sectionIndex.Value);
            if (ts != null)
            {
                if (ts.Curved)
                {
                    double offset = ts.Radius * (1 - Math.Cos(ts.Angle * Math.PI / 360.0));
                    if (offset < 3)
                        return;
                    Vector3 v = new Vector3((float)((end.TileX - start.TileX) * TileSize + end.Location.X - start.Location.X), 0,
                        (float)((end.TileZ - start.TileZ) * TileSize + end.Location.Z - start.Location.Z));
                    IsCurved = true;
                    Vector3 v2 = Vector3.Cross(Vector3.Up, v);
                    v2.Normalize();
                    v = v / 2;
                    v.X += (float)(start.TileX * TileSize + start.Location.X);
                    v.Z += (float)(start.TileZ * TileSize + start.Location.Z);
                    if (ts.Angle > 0)
                        v = v2 * -(float)offset + v;
                    else
                        v = v2 * (float)offset + v;
                    CurvePoints[1] = new PointF(v.X, v.Z);
                }
            }
        }

        public PointF[] ScaledPoints(float scale, float startx, float starty)
        {
            PointF[] result = new PointF[3];
            for (int i = 0; i < CurvePoints.Length; i++)
            {
                result[i].X = (CurvePoints[i].X - startx) * scale;
                result[i].Y = (CurvePoints[i].Y - starty) * scale;
            }
            return result;
        }

        public void Normalize(PointF origin)
        {
            CurvePoints[0] = new PointF(CurvePoints[0].X - origin.X, CurvePoints[0].Y - origin.Y);
            CurvePoints[1] = new PointF(CurvePoints[1].X - origin.X, CurvePoints[1].Y - origin.Y);
            CurvePoints[2] = new PointF(CurvePoints[2].X - origin.X, CurvePoints[2].Y - origin.Y);
        }
    }

    #endregion

}
