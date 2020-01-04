using Orts.Common.Position;
using Orts.Formats.Msts.Models;
using Orts.Simulation;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher
{
    internal class DispatcherContent
    {
        public List<TrackSegment> TrackSegments = new List<TrackSegment>();

        public RectangleF Bounds { get; private set; }

        private RectangleF viewPort;

        public RectangleF DisplayPort { get; private set; }

        public double Scale { get; private set; }

        public Size WindowSize { get; private set; }

        private readonly Simulator simulator;

        public bool MetricUnits { get; private set; }

        public RenderFrame Foreground { get; private set; }
        public RenderFrame Background { get; private set; }

        internal int ViewVersion { get; private set; }

        public DispatcherContent(Simulator simulator)
        {
            this.simulator = simulator;
            MetricUnits = simulator.Settings.Units == "Metric" || simulator.Settings.Units == "Automatic" && System.Globalization.RegionInfo.CurrentRegion.IsMetric ||
                simulator.Settings.Units == "Route" && simulator.TRK.Route.MilepostUnitsMetric;
            Foreground = new RenderFrame(this);
            Background = new RenderFrame(this);
        }

        public async Task Initialize()
        {
            List<Task> initializer = new List<Task>
            {
                Task.Run(async () => Bounds = await InitializeTrackSegments())
            };

            await Task.WhenAll(initializer).ConfigureAwait(false);

            viewPort = new RectangleF(0, 0, Bounds.Width - Bounds.X, Bounds.Height - Bounds.Y);
            UpdateScale();

        }

        internal void SwapFrames()
        {
            (Background, Foreground) = (Foreground, Background);
        }

        private void UpdateScale()
        {
            double xScale = WindowSize.Width / viewPort.Width;
            double yScale = WindowSize.Height / viewPort.Height;
            Scale = Math.Min(xScale, yScale);
            //update displayport from viewport to match windows dimensions
            SizeF scaledViewportSize = new SizeF((float)(WindowSize.Width / Scale), (float)(WindowSize.Height / Scale));
            PointF location = new PointF(viewPort.Left + (viewPort.Width - scaledViewportSize.Width) / 2, viewPort.Top + (viewPort.Height - scaledViewportSize.Height) / 2);
            DisplayPort = new RectangleF(location, scaledViewportSize);
            ViewVersion++;
        }

        public void UpdateScale(double factor)
        {
            //TODO 2020-01-03 check for min/max scale
            SizeF scaledSize = new SizeF((float)(viewPort.Width * factor), (float)(viewPort.Height * factor));
            PointF offset = new PointF((viewPort.Width - scaledSize.Width) / 2, (viewPort.Height - scaledSize.Height) / 2);
            viewPort.Size = scaledSize;
            viewPort.Offset(offset);
            UpdateScale();
        }

        public void UpdateLocation(PointF delta)
        {
            //TODO 20200103 check for bounds (all tracks out of view)
            delta.X *= (int)(40 / Scale);
            delta.Y *= (int)(40 / Scale);
            viewPort.Offset(delta);
            UpdateScale();
        }

        public void UpdateSize(Size windowSize)
        {
            WindowSize = windowSize;
            UpdateScale();
        }

        private Task<RectangleF> InitializeTrackSegments()
        {
            List<Task<RectangleF>> segmentBounds = new List<Task<RectangleF>>();
            foreach (TrackNode trackNode in simulator.TDB.TrackDB.TrackNodes)
            {
                switch (trackNode)
                {
                    case TrackEndNode trackEndNode:
                        break;
                    case TrackVectorNode trackVectorNode:
                        if (trackVectorNode.TrackVectorSections.Length > 1)
                        {
                            segmentBounds.Add(AddSegments(trackVectorNode.TrackVectorSections));
                        }
                        else
                        {
                            TrackVectorSection section = trackVectorNode.TrackVectorSections[0];

                            foreach (TrackPin pin in trackVectorNode.TrackPins)
                            {
                                TrackNode connectedNode = simulator.TDB.TrackDB.TrackNodes[pin.Link];
                                TrackSegments.Add(new TrackSegment(section.Location, connectedNode.UiD.Location, null));
                            }
                        }
                        break;
                    case TrackJunctionNode trackJunctionNode:
                        foreach (TrackPin pin in trackJunctionNode.TrackPins)
                        {
                            TrackVectorSection item = null;
                            if (simulator.TDB.TrackDB.TrackNodes[pin.Link] is TrackVectorNode vectorNode && vectorNode.TrackVectorSections.Length > 0)
                            {
                                item = pin.Direction == 1 ? vectorNode.TrackVectorSections.First() : vectorNode.TrackVectorSections.Last();
                                if (WorldLocation.GetDistanceSquared(trackJunctionNode.UiD.Location, item.Location) >= 0.1)
                                    TrackSegments.Add(new TrackSegment(item.Location, trackJunctionNode.UiD.Location, item.SectionIndex));
                            }
                        }
                        //TODO switches.Add(new SwitchWidget(trackJunctionNode));
                        break;
                }
            }
            var result = Task.WhenAll(segmentBounds).ConfigureAwait(false).GetAwaiter().GetResult();
            //find the bounds of this route
            float maxX = 0f, minX = 0f, maxY = 0f, minY = 0f;
            maxX = result.Max((r) => r.Location.X + r.Size.Width);
            maxY = result.Max((r) => r.Location.Y + r.Size.Height);
            minX = result.Min((r) => r.Location.X);
            minY = result.Min((r) => r.Location.Y);
            RectangleF bounds = new RectangleF(minX, minY, maxX, maxY);
            //normalize all segments to the top left corner of this route
            foreach (TrackSegment segment in TrackSegments)
                segment.Normalize(bounds.Location);
            return Task.FromResult(bounds);
        }

        /// Generates track segments from an array of TrVectorSection. returns the bounds of this segment 
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

                TrackSegments.Add(new TrackSegment(start, end, items[i].SectionIndex));
            }
            return Task.FromResult(new RectangleF((float)minX, (float)minY, (float)(maxX - minX), (float)(maxY - minY)));
        }

    }
}
