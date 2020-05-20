using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

using Orts.ActivityRunner.Viewer3D.Dispatcher.Widgets;
using Orts.Common.Position;
using Orts.Formats.Msts.Models;
using Orts.Simulation;
using Orts.Simulation.Signalling;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher
{
    internal class DispatcherContent
    {
        internal List<TrackSegment> TrackSegments = new List<TrackSegment>();
        internal List<SwitchWidget> Switches = new List<SwitchWidget>();
        internal List<SignalWidget> Signals = new List<Widgets.SignalWidget>();
        internal List<SidingWidget> Sidings = new List<SidingWidget>();
        internal ScaleRulerWidget scaleRuler = new ScaleRulerWidget();
        private readonly Simulator simulator;
        private RectangleF bounds;
        private double maxScale;

        public PointF Offset { get; private set; }

        public RectangleF DisplayPort { get; private set; }

        public double Scale { get; private set; }

        public Size WindowSize { get; private set; }

        public bool MetricUnits { get; private set; }

        public RenderFrame Foreground { get; private set; }
        public RenderFrame Background { get; private set; }

        internal int ViewVersion { get; private set; }

        public DispatcherContent(Simulator simulator)
        {
            this.simulator = simulator;
            //TODO 2020-01-05 this really should be a property of simulator (or viewer)
            MetricUnits = simulator.Settings.Units == "Metric" || simulator.Settings.Units == "Automatic" && System.Globalization.RegionInfo.CurrentRegion.IsMetric ||
                simulator.Settings.Units == "Route" && simulator.TRK.Route.MilepostUnitsMetric;
            WidgetBase.SetContent(this);
            Foreground = new RenderFrame(this);
            Background = new RenderFrame(this);
        }

        public async Task Initialize()
        {
            List<Task> initializer = new List<Task>
            {
                Task.Run(async () => await InitializeTrackSegments())
            };

            await Task.WhenAll(initializer).ConfigureAwait(false);

            ScaleToFit();
            CenterView();
            ViewVersion++;
        }

        internal void SwapFrames()
        {
            (Background, Foreground) = (Foreground, Background);
        }

        private void CenterView()
        {
            Offset = new PointF((float)(WindowSize.Width  / Scale - bounds.Size.Width) / 2f, (float)(WindowSize.Height / Scale - bounds.Size.Height) / 2f);
        }

        private void CenterViewAt()
        {
            //TODO tbd
        }

        private void ScaleToFit()
        {
            double xScale = WindowSize.Width / bounds.Width;
            double yScale = WindowSize.Height / bounds.Height;
            Scale = Math.Min(xScale, yScale);
            maxScale = Scale * 0.75;
        }

        public bool UpdateLocationAbsolute(PointF offset)
        {
            // checking bounds
            if (CheckBoundsOutsideWindow(in offset))
                return false;

            Offset = offset;
            ViewVersion++;
            return true;
        }

        public bool UpdateLocationRelative(Size delta)
        {
            PointF offset = PointF.Add(Offset, new SizeF((float)(delta.Width * 40 / Scale), (float)(delta.Height * 40 / Scale)));
            if (CheckBoundsOutsideWindow(in offset))
                return false;

            Offset = offset;
            ViewVersion++;
            return true;
        }

        public void UpdateScaleAt(Point focusPoint, int steps)
        {
            double scale = Scale * Math.Pow((steps > 0 ? 1 / 0.9 : (steps < 0 ? 0.9 : 1)), Math.Abs(steps));
            if (scale < maxScale || scale > 200)
                return;

            PointF location = LocationFromDisplayCoordinates(focusPoint);
            Scale = scale;
            location = DisplayCoordinatesFromLocation(in location);

            Offset = new PointF((float)(Offset.X + (focusPoint.X - location.X) / Scale), (float)(Offset.Y + (focusPoint.Y - location.Y) / Scale));
            ViewVersion++;
        }

        public void ResetView()
        {
            ScaleToFit();
            CenterView();
            ViewVersion++;
        }

        public void UpdateSize(Size windowSize)
        {
            WindowSize = windowSize;
            ScaleToFit();
            ViewVersion++;
        }

        private PointF LocationFromDisplayCoordinates(in PointF location)
        {
            return new PointF((float)(location.X / Scale - Offset.X), (float)((location.Y) / Scale - Offset.Y));
        }

        private PointF DisplayCoordinatesFromLocation(in PointF location)
        {
            return new PointF((float)((Offset.X + location.X) * Scale), (float)((Offset.Y + location.Y) * Scale));
        }

        private bool CheckBoundsOutsideWindow(in PointF offset)
        {
            return ((offset.X > (WindowSize.Width - 10) / Scale) || (offset.X + bounds.Size.Width < 10) || (offset.Y > (WindowSize.Height - 10) / Scale) || (offset.Y + bounds.Height < 10));
        }

        private async Task InitializeTrackSegments()
        {
            List<Task> renderItems = new List<Task>
            {
                Task.Run(() => AddTrackSegments()),
                Task.Run(() => AddTrackItems()), 
            };

            await Task.WhenAll(renderItems).ConfigureAwait(false);
            //normalize all segments to the top left corner of this route
            foreach (TrackSegment segment in TrackSegments)
                segment.Normalize(bounds);
            foreach (SwitchWidget switchWidget in Switches)
                switchWidget.Normalize(bounds);
            foreach (SignalWidget signalWidget in Signals)
                signalWidget.Normalize(bounds);
            foreach (SidingWidget sidingWidget in Sidings)
                sidingWidget.Normalize(bounds);
        }

        private Task AddTrackItems()
        {
            foreach (TrackItem item in simulator.TDB.TrackDB.TrackItems)
            {
                if (item is SignalItem signalItem)
                {
                    if (signalItem.SignalObject >= 0 && signalItem.SignalObject < simulator.Signals.SignalObjects.Length)
                    {
                        Signal signal = simulator.Signals.SignalObjects[signalItem.SignalObject];
                        if (/*signal != null && */signal.isSignal && signal.isSignalNormal())
                            Signals.Add(new Widgets.SignalWidget(signalItem, signal));
                    }
                }
                if (item is SidingItem || item is PlatformItem)
                {
                    Sidings.Add(new SidingWidget(item));
                }
            }
            return Task.CompletedTask;
        }

    private Task AddTrackSegments()
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

            /// update bounds 
            void UpdateBounds(in WorldLocation location)
            {
                minX = Math.Min(minX, location.TileX * WorldLocation.TileSize + location.Location.X);
                minY = Math.Min(minY, location.TileZ * WorldLocation.TileSize + location.Location.Z);
                maxX = Math.Max(maxX, location.TileX * WorldLocation.TileSize + location.Location.X);
                maxY = Math.Max(maxY, location.TileZ * WorldLocation.TileSize + location.Location.Z);
            }

            foreach (TrackNode trackNode in simulator.TDB.TrackDB.TrackNodes)
            {
                switch (trackNode)
                {
                    case TrackEndNode trackEndNode:
                        TrackVectorNode connectedVectorNode = simulator.TDB.TrackDB.TrackNodes[trackEndNode.TrackPins[0].Link] as TrackVectorNode;
                        if (connectedVectorNode.TrackPins[0].Link == trackEndNode.Index)
                        {
                            TrackSegments.Add(new TrackSegment(trackEndNode.UiD.Location, connectedVectorNode.TrackVectorSections[0].Location, null));
                        }
                        else if (connectedVectorNode.TrackPins.Last().Link == trackEndNode.Index)
                        {
                            TrackSegments.Add(new TrackSegment(trackEndNode.UiD.Location, connectedVectorNode.TrackVectorSections.Last().Location, null));
                        }
                        else
                            throw new ArgumentOutOfRangeException($"Unlinked track end node {trackEndNode.Index}");
                        UpdateBounds(trackEndNode.UiD.Location);
                        break;
                    case TrackVectorNode trackVectorNode:
                        if (trackVectorNode.TrackVectorSections.Length > 1)
                        {
                            for (int i = 0; i < trackVectorNode.TrackVectorSections.Length - 1; i++)
                            {
                                ref readonly WorldLocation start = ref trackVectorNode.TrackVectorSections[i].Location;
                                UpdateBounds(start);
                                ref readonly WorldLocation end = ref trackVectorNode.TrackVectorSections[i + 1].Location;
                                UpdateBounds(end);
                                TrackSegments.Add(new TrackSegment(start, end, trackVectorNode.TrackVectorSections[i].SectionIndex));
                            }
                        }
                        else
                        {
                            TrackVectorSection section = trackVectorNode.TrackVectorSections[0];

                            foreach (TrackPin pin in trackVectorNode.TrackPins)
                            {
                                TrackNode connectedNode = simulator.TDB.TrackDB.TrackNodes[pin.Link];
                                TrackSegments.Add(new TrackSegment(section.Location, connectedNode.UiD.Location, null));
                                UpdateBounds(section.Location);
                                UpdateBounds(connectedNode.UiD.Location);
                            }
                        }
                        break;
                    case TrackJunctionNode trackJunctionNode:
                        foreach (TrackPin pin in trackJunctionNode.TrackPins)
                        {
                            if (simulator.TDB.TrackDB.TrackNodes[pin.Link] is TrackVectorNode vectorNode && vectorNode.TrackVectorSections.Length > 0)
                            {
                                TrackVectorSection item = pin.Direction == 1 ? vectorNode.TrackVectorSections.First() : vectorNode.TrackVectorSections.Last();
                                if (WorldLocation.GetDistanceSquared(trackJunctionNode.UiD.Location, item.Location) >= 0.1)
                                    TrackSegments.Add(new TrackSegment(item.Location, trackJunctionNode.UiD.Location, item.SectionIndex));
                                UpdateBounds(item.Location);
                                UpdateBounds(trackJunctionNode.UiD.Location);

                            }
                        }
                        Switches.Add(new SwitchWidget(trackJunctionNode));
                        break;
                }
            }
            bounds = new RectangleF((float)minX, (float)minY, (float)(maxX - minX), (float)(maxY - minY));
            return Task.CompletedTask;
        }

    }
}
