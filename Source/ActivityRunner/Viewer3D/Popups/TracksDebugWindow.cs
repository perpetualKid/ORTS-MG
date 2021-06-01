// COPYRIGHT 2012 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

// This file is the responsibility of the 3D & Environment Team. 

using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Orts.Common;
using Orts.Formats.Msts.Models;
using Orts.Simulation;

namespace Orts.ActivityRunner.Viewer3D.Popups
{
    public class TracksDebugWindow : LayeredWindow
    {
        private const float DisplayDistance = 1000;
        private const float DisplaySegmentLength = 10;
        private const float MaximumSectionDistance = 10000;
        private const float Tolerance = 0.0001F;
        private Viewport Viewport;
        private List<DispatcherPrimitive> Primitives = new List<DispatcherPrimitive>();

        public TracksDebugWindow(WindowManager owner)
            : base(owner, 1, 1, "Tracks and Roads Debug")
        {
        }

        internal override void ScreenChanged()
        {
            base.ScreenChanged();
            Viewport = Owner.Viewer.RenderProcess.GraphicsDevice.Viewport;
        }

        public override bool Interactive
        {
            get
            {
                return false;
            }
        }

        public override void PrepareFrame(in ElapsedTime elapsedTime, bool updateFull)
        {
            base.PrepareFrame(elapsedTime, updateFull);

            if (updateFull)
            {
                var primitives = new List<DispatcherPrimitive>(Primitives.Count);
                var camera = Owner.Viewer.Camera;
                var tSectionDat = Owner.Viewer.Simulator.TSectionDat;
                var tdb = Owner.Viewer.Simulator.TDB;
                var rdb = Owner.Viewer.Simulator.RDB;
                foreach (var trackNode in tdb.TrackDB.TrackNodes.Where(
                    tn => tn is TrackVectorNode trackVectorNode
                    && Math.Abs(trackVectorNode.TrackVectorSections[0].Location.TileX - camera.TileX) <= 1
                    && Math.Abs(trackVectorNode.TrackVectorSections[0].Location.TileZ - camera.TileZ) <= 1).Cast<TrackVectorNode>())
                {
                    var currentPosition = new Traveller(tSectionDat, tdb.TrackDB.TrackNodes, trackNode);
                    while (true)
                    {
                        var previousLocation = currentPosition.WorldLocation;
                        var remaining = currentPosition.MoveInSection(DisplaySegmentLength);
                        if ((Math.Abs(remaining - DisplaySegmentLength) < Tolerance) && !currentPosition.NextVectorSection())
                            break;
                        primitives.Add(new DispatcherLineSegment(previousLocation, currentPosition.WorldLocation, Color.LightBlue, 2));
                    }
                    foreach (var trItemID in trackNode.TrackItemIndices)
                    {
                        var trItem = tdb.TrackDB.TrackItems[trItemID];
                        currentPosition = new Traveller(tSectionDat, tdb.TrackDB.TrackNodes, trackNode);
                        currentPosition.Move(trItem.SData1);
                        primitives.Add(new DispatcherLabel(currentPosition.WorldLocation, Color.LightBlue,
                            $"{trItem.TrackItemId} {trItem.GetType().Name.Replace("Item", string.Empty)} {trItem.ItemName}", Owner.TextFontDefaultOutlined));
                    }
                }
                if (rdb != null && rdb.RoadTrackDB.TrackNodes != null)
                {
                    foreach (var trackNode in rdb.RoadTrackDB.TrackNodes.Where(
                        tn => tn is TrackVectorNode trackVectorNode 
                        && Math.Abs(trackVectorNode.TrackVectorSections[0].Location.TileX - camera.TileX) <= 1 
                        && Math.Abs(trackVectorNode.TrackVectorSections[0].Location.TileZ - camera.TileZ) <= 1).Cast<TrackVectorNode>())
                    {
                        var currentPosition = new Traveller(tSectionDat, rdb.RoadTrackDB.TrackNodes, trackNode);
                        while (true)
                        {
                            var previousLocation = currentPosition.WorldLocation;
                            var remaining = currentPosition.MoveInSection(DisplaySegmentLength);
                            if ((Math.Abs(remaining - DisplaySegmentLength) < Tolerance) && !currentPosition.NextVectorSection())
                                break;
                            primitives.Add(new DispatcherLineSegment(previousLocation, currentPosition.WorldLocation, Color.LightSalmon, 2));
                        }
                        if (trackNode.TrackItemIndices != null)
                        {
                            foreach (var trItemID in trackNode.TrackItemIndices)
                            {
                                var trItem = rdb.RoadTrackDB.TrItemTable[trItemID];
                                currentPosition = new Traveller(tSectionDat, rdb.RoadTrackDB.TrackNodes, trackNode);
                                currentPosition.Move(trItem.SData1);
                                primitives.Add(new DispatcherLabel(currentPosition.WorldLocation, Color.LightSalmon, $"{trItem.TrackItemId} {trItem.GetType().Name.Replace("Item", string.Empty)} {trItem.ItemName}", Owner.TextFontDefaultOutlined));
                            }
                        }
                    }
                }
                Primitives = primitives;
            }

            var labels = new List<Rectangle>();
            foreach (var primitive in Primitives)
                primitive.PrepareFrame(labels, Viewport, Owner.Viewer.Camera);
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);
            foreach (var line in Primitives)
                line.Draw(spriteBatch);
        }
    }
}
