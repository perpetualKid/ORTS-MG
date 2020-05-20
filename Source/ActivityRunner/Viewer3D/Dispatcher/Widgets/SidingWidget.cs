using System;
using System.Drawing;
using Orts.Formats.Msts.Models;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher.Widgets
{
    internal class SidingWidget : PointWidget
    {
        private static readonly Font sidingFont = new Font("Arial", 8, FontStyle.Bold);
        private static readonly Brush sidingBrush = new SolidBrush(Color.Blue);


        /// <summary>
        /// The underlying track item.
        /// </summary>
        private readonly TrackItem trackItem;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="item"></param>
        /// <param name="signal"></param>
        public SidingWidget(TrackItem item)
        {
            trackItem = item;
            Location = PointFFromLocation(item.Location);
        }

        internal override void Draw(Graphics g)
        {
            RectangleF bounds = g.VisibleClipBounds;
            if (Location.X < bounds.Left || Location.X > bounds.Right
                || Location.Y < bounds.Top || Location.Y > bounds.Bottom)
                return;

            g.DrawString(trackItem.ItemName, sidingFont, sidingBrush, Location);
        }
    }
}
