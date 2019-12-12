using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

using Orts.Common.Position;
using Orts.Formats.Msts.Models;
using Orts.Simulation;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher.Controls
{
    public partial class DispatcherViewControl : UserControl
    {
        private Simulator simulator;
        private List<LineSegment> segments = new List<LineSegment>();

        private RectangleF viewPort;
        private RectangleF bounds;
        private PointF viewPoint;
        private PointF origin;

        private static readonly Pen redPen = new Pen(Color.Red);
        private static readonly Pen greenPen = new Pen(Color.Green);
        private static readonly Pen orangePen = new Pen(Color.Orange);
        private static readonly Pen trainPen = new Pen(Color.DarkGreen);
        private static readonly Pen pathPen = new Pen(Color.DeepPink);
        private static readonly Pen grayPen = new Pen(Color.Gray);

        private float scale = 1;

        public DispatcherViewControl()
        {
            InitializeComponent();
        }

        public void Initialize(Simulator simulator)
        {
            this.simulator = simulator;
            InitData().ConfigureAwait(false);
        }

        private async Task InitData()
        {
            List<Task<RectangleF>> results = new List<Task<RectangleF>>();
            foreach (TrackNode trackNode in simulator.TDB.TrackDB.TrackNodes)
            {
                switch (trackNode)
                {
                    case TrackEndNode trackEndNode:
                        break;
                    case TrackVectorNode trackVectorNode:
                        if (trackVectorNode.TrackVectorSections.Length > 1)
                        {
                            results.Add(AddSegments(trackVectorNode.TrackVectorSections));
                        }
                        else
                        {
                            TrackVectorSection section = trackVectorNode.TrackVectorSections[0];

                            foreach (TrackPin pin in trackVectorNode.TrackPins)
                            {
                                TrackNode connectedNode = simulator.TDB.TrackDB.TrackNodes[pin.Link];
                                segments.Add(new LineSegment(section.Location, connectedNode.UiD.Location, null));
                            }
                        }
                        break;
                    case TrackJunctionNode trackJunctionNode:
                        foreach (TrackPin pin in trackJunctionNode.TrackPins)
                        {
                            TrackVectorSection item = null;
                            if (simulator.TDB.TrackDB.TrackNodes[pin.Link] is TrackVectorNode vectorNode && vectorNode.TrackVectorSections.Length > 0)
                            {
                                if (pin.Direction == 1)
                                    item = vectorNode.TrackVectorSections.First();
                                else
                                    item = vectorNode.TrackVectorSections.Last();
                                if (WorldLocation.GetDistanceSquared(trackJunctionNode.UiD.Location, item.Location) >= 0.1)
                                    segments.Add(new LineSegment(item.Location, trackJunctionNode.UiD.Location, item.SectionIndex));
                            }
                        }
                        //TODO switches.Add(new SwitchWidget(trackJunctionNode));
                        break;
                }
            }
            var result = await Task.WhenAll(results).ConfigureAwait(false);
            float maxX = result.Max((r) => r.Location.X + r.Size.Width);
            float maxY = result.Max((r) => r.Location.Y + r.Size.Height);
            float minX = result.Min((r) => r.Location.X);
            float minY = result.Min((r) => r.Location.Y);
            bounds = new RectangleF(minX, minY, maxX, maxY);
            //foreach (LineSegment segment in segments)
            //    segment.Normalize(bounds.Location);
            viewPort = new RectangleF(0, 0, bounds.Width - bounds.X, bounds.Height - bounds.Y);
            viewPoint = new PointF(viewPort.Width / 2, viewPort.Height / 2);
            UpdateScale();
        }

        private void UpdateScale()
        {
            float x = pbDispatcherView.Width / viewPort.Width;
            float y = pbDispatcherView.Height / viewPort.Height;
            scale = Math.Min(x, y);
        }

        /// Generates line segments from an array of TrVectorSection. Also computes 
        /// the bounds of the entire route being drawn.
        private Task<RectangleF> AddSegments(TrackVectorSection[] items)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < items.Length - 1; i++)
            {
                ref readonly WorldLocation start = ref items[i].Location;
                ref readonly WorldLocation end = ref items[i + 1].Location;

                minX = Math.Min(minX, start.TileX * WorldLocation.TileSize + start.Location.X);
                minX = Math.Min(minX, end.TileX * WorldLocation.TileSize + end.Location.X);
                minY = Math.Min(minY, start.TileZ * WorldLocation.TileSize + start.Location.Z);
                minY = Math.Min(minY, end.TileZ * WorldLocation.TileSize + end.Location.Z);
                maxX = Math.Max(maxX, start.TileX * WorldLocation.TileSize + start.Location.X);
                maxX = Math.Max(maxX, end.TileX * WorldLocation.TileSize + end.Location.X);
                maxY = Math.Max(maxY, start.TileZ * WorldLocation.TileSize + start.Location.Z);
                maxY = Math.Max(maxY, end.TileZ * WorldLocation.TileSize + end.Location.Z);

                segments.Add(new LineSegment(start, end, items[i].SectionIndex));
            }
            return Task.FromResult(new RectangleF((float)minX, (float)minY, (float)(maxX - minX), (float)(maxY - minY)));
        }

        public void GenerateView()
        {
            Graphics g = Graphics.FromImage(pbDispatcherView.Image);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.White);
            float subX = bounds.X + origin.X; 
            float subY = bounds.Y + origin.Y; 

            foreach (var segment in segments)
            {
                if (segment.IsCurved)
                {
                    g.DrawCurve(grayPen, segment.ScaledPoints(scale, subX, subY));
                }
                else
                {
                    g.DrawLine(grayPen, segment.ScaledPoints(scale, subX, subY)[0], segment.ScaledPoints(scale, subX, subY)[2]);
                }
            }
            pbDispatcherView.Invalidate();
        }

        //public void GenerateView()
        //{
        //    Graphics g = Graphics.FromImage(pbDispatcherView.Image);
        //    g.Clear(Color.White);
        //    grayPen.Width = 0.1f;
        //    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        //    g.TranslateTransform(-viewPoint.X, -viewPoint.Y, System.Drawing.Drawing2D.MatrixOrder.Append);
        //    g.ScaleTransform(scale, -scale, System.Drawing.Drawing2D.MatrixOrder.Append);
        //    g.TranslateTransform(pbDispatcherView.Width / 2f, pbDispatcherView.Height / 2f, System.Drawing.Drawing2D.MatrixOrder.Append);
        //    foreach (var line in segments)
        //    {
        //        if (line.IsCurved)
        //        {
        //            g.DrawCurve(grayPen, line.CurvePoints);
        //        }
        //        else
        //        {
        //            g.DrawLine(grayPen, line.StartPoint, line.EndPoint);
        //        }
        //    }
        //    pbDispatcherView.Invalidate();
        //}

        private void DispatcherViewControl_KeyUp(object sender, KeyEventArgs e)
        {
            switch(e.KeyCode)
            {
                case Keys.PageDown:
                    scale *= .9f;
                    break;
                case Keys.PageUp:
                    scale /= .9f;
                    break;
                case Keys.Left:
                    origin.X += pbDispatcherView.Width / 20 / scale;
                    viewPoint.X -= viewPort.Width / scale / 400f;
                    break;
                case Keys.Right:
                    origin.X -= pbDispatcherView.Width / 20 / scale;
                    viewPoint.X += viewPort.Width / scale / 400f;
                    break;
                case Keys.Up:
                    origin.Y += pbDispatcherView.Height / 20 / scale;
                    viewPoint.Y += viewPort.Height / scale / 400f;
                    break;
                case Keys.Down:
                    origin.Y -= pbDispatcherView.Height / 20 / scale;
                    viewPoint.Y -= viewPort.Height / scale / 400f;
                    break;
            }
            GenerateView();
        }

        private void PictureBoxDispatcherView_SizeChanged(object sender, EventArgs e)
        {
            if (pbDispatcherView.Image != null)
            {
                pbDispatcherView.Image.Dispose();
            }
            pbDispatcherView.Image = new Bitmap(pbDispatcherView.Width, pbDispatcherView.Height);
            UpdateScale();
        }
    }
}
