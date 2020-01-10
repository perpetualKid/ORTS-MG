using System;
using System.Drawing;
using Orts.Formats.Msts.Models;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher.Widgets
{
    internal class SidingWidget : PointWidget
    {
        private static readonly Font sidingFont = new Font("Arial", 12, FontStyle.Bold);
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
            g.DrawString(trackItem.ItemName, sidingFont, sidingBrush, Location);
        }
    }
}
