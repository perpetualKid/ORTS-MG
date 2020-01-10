using System;
using System.Drawing;
using Orts.Formats.Msts.Models;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher.Widgets
{
    internal class SwitchWidget : PointWidget
    {
        private readonly TrackJunctionNode item;
        private readonly uint mainRoute;

        public SwitchWidget(TrackJunctionNode item)
        {
            this.item = item;
            mainRoute = Program.Simulator.TSectionDat.TrackShapes[item.ShapeIndex]?.MainRoute ?? 0;  // TSECTION.DAT tells us which is the main route
            Location = PointFFromLocation(this.item.UiD.Location);
        }

        internal override void Draw(Graphics g)
        {
            if (item.SelectedRoute == mainRoute)
                g.FillEllipse(Brushes.Black, GetRect(Location));
            else
                g.FillEllipse(Brushes.Gray, GetRect(Location));
        }
    }
}
