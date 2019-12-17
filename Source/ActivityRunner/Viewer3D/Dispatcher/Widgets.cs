using System;
using System.Drawing;

using Microsoft.Xna.Framework;

using Orts.Common.Position;
using Orts.Common.Xna;
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
            return new Vector2((float)(location.TileX * WorldLocation.TileSize + location.Location.X), (float)(location.TileZ * WorldLocation.TileSize + location.Location.Z));
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
    public class TrackSegment
    {
        private Vector2D[] curvePoints = new Vector2D[3];

        public PointF[] CurvePoints { get; } = new PointF[3];

        public bool IsCurved { get; private set; }

        public TrackSegment(in WorldLocation start, in WorldLocation end, uint? sectionIndex)
        {
            curvePoints[0] = new Vector2D(start.TileX * WorldLocation.TileSize + start.Location.X, start.TileZ * WorldLocation.TileSize + start.Location.Z);
            curvePoints[2] = new Vector2D(end.TileX * WorldLocation.TileSize + end.Location.X, end.TileZ * WorldLocation.TileSize + end.Location.Z);

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

        public void Normalize(PointF origin)
        {
            CurvePoints[0] = new PointF((float)(curvePoints[0].X - origin.X), (float)(curvePoints[0].Y - origin.Y));
            CurvePoints[1] = new PointF((float)(curvePoints[1].X - origin.X), (float)(curvePoints[1].Y - origin.Y));
            CurvePoints[2] = new PointF((float)(curvePoints[2].X - origin.X), (float)(curvePoints[2].Y - origin.Y));
            curvePoints = null; //no longer needed from here
        }
    }

    #endregion

}
