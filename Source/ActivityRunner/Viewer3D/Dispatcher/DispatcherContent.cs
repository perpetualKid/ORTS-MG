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
        private RectangleF viewPort;

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

            viewPort = new RectangleF(PointF.Empty, bounds.Size);
            UpdateView();

        }

        internal void SwapFrames()
        {
            (Background, Foreground) = (Foreground, Background);
        }

        private void UpdateView()
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

        public void UpdateScaleAt(Point centerPoint, int steps)
        {
            double factor = Math.Pow((steps > 0 ? 0.9 : 1 / 0.9), Math.Abs(steps));
            //TODO 2020-01-03 check for min/max scale
            SizeF scaledSize = new SizeF((float)(viewPort.Width * factor), (float)(viewPort.Height * factor));
            //need to offset proportionaly to mouse position within the picturebox area
            PointF offset = new PointF((viewPort.Width - scaledSize.Width) * ((float)centerPoint.X / WindowSize.Width), 
                (viewPort.Height - scaledSize.Height) * ((WindowSize.Height - (float)centerPoint.Y) / WindowSize.Height));
            viewPort.Size = scaledSize;
            viewPort.Offset(offset);
            UpdateView();
        }

        public void UpdateLocation(PointF delta)
        {
            delta.X *= (int)(40 / Scale);
            delta.Y *= (int)(40 / Scale);
            //TODO 2020-01-03 check for bounds (all tracks out of view)
            //            if ((DisplayPort.Location.X > 0 && delta.X > 0) || (-DisplayPort.Location.X > DisplayPort.Width && delta.X < 0))
            ////                if ((-DisplayPort.Location.Y > DisplayPort.Height && delta.Y < 0) || (DisplayPort.Location.Y > DisplayPort.Height && delta.Y > 0))
            //                    //                (viewPort.Bottom + delta.Y < 0 ) || (viewPort.Top + delta.Y > displayPortSize.Height))
            //                    return;
            viewPort.Offset(delta);
            UpdateView();
        }

        public void ResetView()
        {
            viewPort = new RectangleF(PointF.Empty, bounds.Size);
            UpdateView();
        }

        public void UpdateSize(Size windowSize)
        {
            WindowSize = windowSize;
            UpdateView();
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
                segment.Normalize(bounds.Location);
            foreach (SwitchWidget switchWidget in Switches)
                switchWidget.Normalize(bounds.Location);
            foreach (SignalWidget signalWidget in Signals)
                signalWidget.Normalize(bounds.Location);
            foreach (SidingWidget sidingWidget in Sidings)
                sidingWidget.Normalize(bounds.Location);
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

        private PointF LocationFromDisplayCoordinates(Point location)
        {
            return new PointF((float)(location.X / Scale + DisplayPort.X), (float)((WindowSize.Height - location.Y) / Scale + DisplayPort.Y));
        }

    }
}
