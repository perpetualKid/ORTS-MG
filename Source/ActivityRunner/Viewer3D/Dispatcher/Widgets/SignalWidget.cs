using System;
using System.Drawing;
using Microsoft.Xna.Framework;
using Orts.Formats.Msts;
using Orts.Formats.Msts.Models;
using Orts.Simulation.Signalling;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher.Widgets
{
    internal class SignalWidget : PointWidget
    {
        private static readonly Pen greenPen = new Pen(System.Drawing.Color.Green);
        private static readonly Pen orangePen = new Pen(System.Drawing.Color.Orange);
        private static readonly Pen redPen = new Pen(System.Drawing.Color.Red);

        private readonly TrackItem item;
        /// <summary>
        /// The underlying signal object as referenced by the TrItem.
        /// </summary>
        private readonly Signal signal;

        private PointF direction;
        private readonly bool hasDirection;

        public SignalWidget(SignalItem item, Signal signal)
        {
            Vector2 directionVector;
            this.item = item;
            this.signal = signal;
            hasDirection = false;
            Location = PointFFromLocation(item.Location);
            TrackNode node = Program.Simulator.TDB.TrackDB.TrackNodes[signal.trackNode];
            if (node is TrackVectorNode trackVectorNode)
            {
                directionVector = VectorFromLocation(trackVectorNode.TrackVectorSections[0].Location);
            }
            else if (node is TrackJunctionNode)
            {
                directionVector = VectorFromLocation(node.UiD.Location);
            }
            else
                return;
            Vector2 v1 = new Vector2(Location.X, Location.Y);
            Vector2 v3 = v1 - directionVector;
            v3.Normalize();
            directionVector = v1 - Vector2.Multiply(v3, signal.direction == 0 ? 2f : -2f);//shift signal along the dir for 2m, so signals for both directions will not be overlapped
            Location = new PointF(directionVector.X, directionVector.Y);
            directionVector = v1 - Vector2.Multiply(v3, signal.direction == 0 ? 12f : -12f);
            direction = new PointF(directionVector.X, directionVector.Y);
            hasDirection = true;
        }

        /// <summary>
        /// For now, returns true if any of the signal heads shows any "clear" aspect.
        /// This obviously needs some refinement.
        /// </summary>
        public int CanProceed
        {
            get
            {
                int returnValue = 2;

                foreach (var head in signal.SignalHeads)
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

        internal override void Normalize(in RectangleF bounds)
        {
            base.Normalize(bounds);
            if (hasDirection)
                direction = new PointF(direction.X - bounds.Location.X, bounds.Size.Height - (direction.Y - bounds.Location.Y));
        }

        internal override void Draw(Graphics g)
        {
            if (signal.isSignalNormal())//only show nor
            {
                Brush colorBrush = Brushes.Green;
                Pen pen;
                switch (CanProceed)
                {
                    case 0:
                        colorBrush = Brushes.Green;
                        pen = greenPen;
                        break;
                    case 1:
                        colorBrush = Brushes.Orange;
                        pen = orangePen;
                        break;
                    default:
                        colorBrush = Brushes.Red;
                        pen = redPen;
                        break;
                }
                g.FillEllipse(colorBrush, CenterRectangle(Location));
                if (hasDirection)
                {
                    g.DrawLine(pen, Location, direction);
                }
            }
        }
    }
}
