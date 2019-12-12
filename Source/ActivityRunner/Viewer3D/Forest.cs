// COPYRIGHT 2010, 2011, 2012, 2013, 2014 by the Open Rails project.
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

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Orts.ActivityRunner.Viewer3D.Common;
using Orts.ActivityRunner.Viewer3D.Shapes;
using Orts.Common;
using Orts.Common.Position;
using Orts.Common.Xna;
using Orts.Formats.Msts;
using Orts.Formats.Msts.Models;

namespace Orts.ActivityRunner.Viewer3D
{
    //[CallOnThread("Loader")]
    public class ForestViewer
    {
        readonly Viewer Viewer;
        readonly WorldPosition Position;
        readonly Material Material;
        readonly ForestPrimitive Primitive;

        public float MaximumCenterlineOffset = 0.0f;
        public bool CheckRoadsToo = false;

        public ForestViewer(Viewer viewer, ForestObject forest, in WorldPosition position)
        {
            Viewer = viewer;
            Position = position;
            MaximumCenterlineOffset = Viewer.Simulator.TRK.Route.ForestClearDistance;
            CheckRoadsToo = Viewer.Simulator.TRK.Route.RemoveForestTreesFromRoads;

            Material = viewer.MaterialManager.Load("Forest", Helpers.GetForestTextureFile(viewer.Simulator, forest.TreeTexture));
            Primitive = new ForestPrimitive(Viewer, forest, position, MaximumCenterlineOffset, CheckRoadsToo);
        }

        //[CallOnThread("Updater")]
        public void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            if ((Primitive as ForestPrimitive).PrimitiveCount > 0)
            {
                var dTileX = Position.TileX - Viewer.Camera.TileX;
                var dTileZ = Position.TileZ - Viewer.Camera.TileZ;
                var mstsLocation = Position.Location + new Vector3(dTileX * 2048, 0, dTileZ * 2048);
                var xnaMatrix = Matrix.CreateTranslation(mstsLocation.X, mstsLocation.Y, -mstsLocation.Z);
                frame.AddAutoPrimitive(mstsLocation, Primitive.ObjectRadius, float.MaxValue, Material, Primitive, RenderPrimitiveGroup.World, ref xnaMatrix, Viewer.Settings.ShadowAllShapes ? ShapeFlags.ShadowCaster : ShapeFlags.None);
            }
        }

        //[CallOnThread("Loader")]
        internal void Mark()
        {
            Material.Mark();
        }
    }

    //[CallOnThread("Loader")]
    public class ForestPrimitive : RenderPrimitive
    {
        readonly Viewer Viewer;
        readonly VertexBuffer VertexBuffer;
        public readonly int PrimitiveCount;
        public float MaximumCenterlineOffset;
        public bool CheckRoadsToo;

        public readonly float ObjectRadius;

        public ForestPrimitive(Viewer viewer, ForestObject forest, in WorldPosition position, float maximumCenterlineOffset, bool checkRoadsToo)
        {
            Viewer = viewer;
            MaximumCenterlineOffset = maximumCenterlineOffset;
            CheckRoadsToo = checkRoadsToo;

            var trees = CalculateTrees(viewer.Tiles, forest, position, out ObjectRadius);

            if (trees.Count > 0)
            {
                VertexBuffer = new VertexBuffer(viewer.RenderProcess.GraphicsDevice, typeof(VertexPositionNormalTexture), trees.Count, BufferUsage.WriteOnly);
                VertexBuffer.SetData(trees.ToArray());
            }

            PrimitiveCount = trees.Count / 3;
        }

        private List<VertexPositionNormalTexture> CalculateTrees(TileManager tiles, ForestObject forest, in WorldPosition position, out float objectRadius)
        {
            // To get consistent tree placement between sessions, derive the seed from the location.
            var random = new Random((int)(1000.0 * (position.Location.X + position.Location.Z + position.Location.Y)));
            List<TrackVectorSection> sections = new List<TrackVectorSection>();
            objectRadius = (float)Math.Sqrt(forest.ForestArea.Width * forest.ForestArea.Width + forest.ForestArea.Height * forest.ForestArea.Height) / 2;

            if (MaximumCenterlineOffset > 0)
            {
                Matrix InvForestXNAMatrix = Matrix.Invert(position.XNAMatrix);
                var addList = FindTracksAndRoadsClose(position.TileX, position.TileZ);
                FindTracksAndRoadsMoreClose(ref sections, addList, forest, position, InvForestXNAMatrix);

                System.Collections.Specialized.BitVector32 includedTiles = new System.Collections.Specialized.BitVector32();

                void UpdateIncludedTiles(in Vector3 vertex)
                {
                    if (vertex.X > 1024) includedTiles[0] = true;
                    if (vertex.X < -1024) includedTiles[1] = true;
                    if (vertex.Z > 1024) includedTiles[3] = true;
                    if (vertex.Z < -1024) includedTiles[2] = true;
                }

                // Check for cross-tile forests
                VectorExtension.Transform(new Vector3(-forest.ForestArea.Width / 2, 0, -forest.ForestArea.Height / 2), position.XNAMatrix, out Vector3 forestVertex);
                UpdateIncludedTiles(forestVertex);
                VectorExtension.Transform(new Vector3(forest.ForestArea.Width / 2, 0, -forest.ForestArea.Height / 2), position.XNAMatrix, out forestVertex);
                UpdateIncludedTiles(forestVertex);
                VectorExtension.Transform(new Vector3(-forest.ForestArea.Width / 2, 0, forest.ForestArea.Height / 2), position.XNAMatrix, out forestVertex);
                UpdateIncludedTiles(forestVertex);
                VectorExtension.Transform(new Vector3(forest.ForestArea.Width / 2, 0, forest.ForestArea.Height / 2), position.XNAMatrix, out forestVertex);
                UpdateIncludedTiles(forestVertex);

                // add sections in nearby tiles for cross-tile forests
                if (includedTiles[0])
                {
                    addList = FindTracksAndRoadsClose(position.TileX + 1, position.TileZ);
                    FindTracksAndRoadsMoreClose(ref sections, addList, forest, position, InvForestXNAMatrix);
                }
                if (includedTiles[1])
                {
                    addList = FindTracksAndRoadsClose(position.TileX - 1, position.TileZ);
                    FindTracksAndRoadsMoreClose(ref sections, addList, forest, position, InvForestXNAMatrix);
                }
                if (includedTiles[2])
                {
                    addList = FindTracksAndRoadsClose(position.TileX, position.TileZ + 1);
                    FindTracksAndRoadsMoreClose(ref sections, addList, forest, position, InvForestXNAMatrix);
                }
                if (includedTiles[3])
                {
                    addList = FindTracksAndRoadsClose(position.TileX, position.TileZ - 1);
                    FindTracksAndRoadsMoreClose(ref sections, addList, forest, position, InvForestXNAMatrix);
                }
                if (includedTiles[0] && includedTiles[2])
                {
                    addList = FindTracksAndRoadsClose(position.TileX + 1, position.TileZ +1);
                    FindTracksAndRoadsMoreClose(ref sections, addList, forest, position, InvForestXNAMatrix);
                }
                if (includedTiles[0] && includedTiles[3])
                {
                    addList = FindTracksAndRoadsClose(position.TileX + 1, position.TileZ -1);
                    FindTracksAndRoadsMoreClose(ref sections, addList, forest, position, InvForestXNAMatrix);
                }
                if (includedTiles[1] && includedTiles[2])
                {
                    addList = FindTracksAndRoadsClose(position.TileX-1, position.TileZ + 1);
                    FindTracksAndRoadsMoreClose(ref sections, addList, forest, position, InvForestXNAMatrix);
                }
                if (includedTiles[1] && includedTiles[3])
                {
                    addList = FindTracksAndRoadsClose(position.TileX-1, position.TileZ - 1);
                    FindTracksAndRoadsMoreClose(ref sections, addList, forest, position, InvForestXNAMatrix);
                }
            }

            var trees = new List<VertexPositionNormalTexture>(forest.Population * 6);
            for (var i = 0; i < forest.Population; i++)
            {
                VectorExtension.Transform(
                    new Vector3((0.5f - (float)random.NextDouble()) * forest.ForestArea.Width, 0, (0.5f - (float)random.NextDouble()) * forest.ForestArea.Height), 
                    position.XNAMatrix, out Vector3 xnaTreePosition);

                bool onTrack = false;
                var scale = MathHelper.Lerp(forest.ScaleRange.LowerLimit, forest.ScaleRange.UpperLimit, (float)random.NextDouble());
                var treeSize = new Vector3(forest.TreeSize.Width * scale, forest.TreeSize.Height * scale, 1);
                var heightComputed = false;
                if (MaximumCenterlineOffset > 0 && sections != null && sections.Count > 0)
                {
                    foreach (var section in sections)
                    {
                        onTrack = InitTrackSection(section, xnaTreePosition, position.TileX, position.TileZ, treeSize.X / 2);
                        if (onTrack)
                        {
                            try
                            {
                                var trackShape = Viewer.Simulator.TSectionDat.TrackShapes[section.ShapeIndex];
                                if (trackShape != null && trackShape.TunnelShape)
                                {
                                    xnaTreePosition.Y = tiles.LoadAndGetElevation(position.TileX, position.TileZ, xnaTreePosition.X, -xnaTreePosition.Z, false);
                                    heightComputed = true;
                                    if (xnaTreePosition.Y > section.Location.Location.Y + 10)
                                    {
                                        onTrack = false;
                                        continue;
                                    }
                                }
                            }
                            catch
                            {

                            }
                            break;
                        }
                    }
                }
                if (!onTrack)
                {
                    if (!heightComputed) xnaTreePosition.Y = tiles.LoadAndGetElevation(position.TileX, position.TileZ, xnaTreePosition.X, -xnaTreePosition.Z, false);
                    xnaTreePosition -= position.XNAMatrix.Translation;

                    trees.Add(new VertexPositionNormalTexture(xnaTreePosition, treeSize, new Vector2(1, 1)));
                    trees.Add(new VertexPositionNormalTexture(xnaTreePosition, treeSize, new Vector2(0, 0)));
                    trees.Add(new VertexPositionNormalTexture(xnaTreePosition, treeSize, new Vector2(1, 0)));
                    trees.Add(new VertexPositionNormalTexture(xnaTreePosition, treeSize, new Vector2(1, 1)));
                    trees.Add(new VertexPositionNormalTexture(xnaTreePosition, treeSize, new Vector2(0, 1)));
                    trees.Add(new VertexPositionNormalTexture(xnaTreePosition, treeSize, new Vector2(0, 0)));
                }
            }
            return trees;
        }

        //map sections to W tiles
        private static Dictionary<string, List<TrackVectorSection>> SectionMap;
        public List<TrackVectorSection> FindTracksAndRoadsClose(int TileX, int TileZ)
        {
            if (SectionMap == null)
            {
                SectionMap = new Dictionary<string, List<TrackVectorSection>>();
                if (MaximumCenterlineOffset > 0)
                {
                    foreach (var node in Viewer.Simulator.TDB.TrackDB.TrackNodes)
                    {
                        if (!(node is TrackVectorNode trackVectorNode))
                            continue;
                        foreach (var section in trackVectorNode.TrackVectorSections)
                        {
                            var key = "" + section.Location.TileX + "." + section.Location.TileZ;
                            if (!SectionMap.ContainsKey(key)) SectionMap.Add(key, new List<TrackVectorSection>());
                            SectionMap[key].Add(section);
                        }
                    }
                }
                if (CheckRoadsToo)
                {
                    if (Viewer.Simulator.RDB != null && Viewer.Simulator.RDB.RoadTrackDB.TrackNodes != null)
                    {
                        foreach (var node in Viewer.Simulator.RDB.RoadTrackDB.TrackNodes)
                        {
                            if (!(node is TrackVectorNode trackVectorNode))
                                continue;
                            foreach (var section in trackVectorNode.TrackVectorSections)
                            {
                                var key = "" + section.Location.TileX + "." + section.Location.TileZ;
                                if (!SectionMap.ContainsKey(key)) SectionMap.Add(key, new List<TrackVectorSection>());
                                SectionMap[key].Add(section);
                            }
                        }
                    }
                }
            }

            var targetKey = "" + TileX + "." + TileZ;
            if (SectionMap.ContainsKey(targetKey)) return SectionMap[targetKey];
            else return null;
        }

        TrackSection trackSection;
        bool InitTrackSection(TrackVectorSection section, Vector3 xnaTreePosition, int tileX, int tileZ, float treeWidth)
        {
            trackSection = Viewer.Simulator.TSectionDat.TrackSections.Get(section.SectionIndex);
            if (trackSection == null)
                return false;
            if (trackSection.Curved)
            {
                return InitTrackSectionCurved(tileX, tileZ, xnaTreePosition.X, -xnaTreePosition.Z, section, treeWidth);
            }
            return InitTrackSectionStraight(tileX, tileZ, xnaTreePosition.X, -xnaTreePosition.Z, section, treeWidth);
        }

        // don't consider track sections outside the forest boundaries
        public void FindTracksAndRoadsMoreClose(ref List<TrackVectorSection> sections, List<TrackVectorSection> allSections, ForestObject forest, in WorldPosition position, Matrix invForestXNAMatrix)
        {
            if (allSections != null && allSections.Count > 0)
            {
                var toAddX = MaximumCenterlineOffset + forest.ForestArea.Width / 2 + forest.ScaleRange.UpperLimit * forest.TreeSize.Width;
                var toAddZ = MaximumCenterlineOffset + forest.ForestArea.Height / 2 + forest.ScaleRange.UpperLimit * forest.TreeSize.Width;
                foreach (TrackVectorSection section in allSections)
                {
                    Vector3 sectPosition = section.Location.Location;
                    Vector3 sectPosToForest;
                    sectPosition.X += (section.Location.TileX - position.TileX) * 2048;
                    sectPosition.Z += (section.Location.TileZ - position.TileZ) * 2048;
                    sectPosition.Z *= -1;
                    sectPosToForest = Vector3.Transform(sectPosition, invForestXNAMatrix);
                    sectPosToForest.Z *= -1;
                    trackSection = Viewer.Simulator.TSectionDat.TrackSections.Get(section.SectionIndex);
                    if (trackSection == null) continue;
                    var trackSectionLength = GetLength(trackSection);
                    if (Math.Abs(sectPosToForest.X) > trackSectionLength + toAddX) continue;
                    if (Math.Abs(sectPosToForest.Z) > trackSectionLength + toAddZ) continue;
                    sections.Add(section);
                }
            }
            return;
        }

        const float InitErrorMargin = 0.5f;

        bool InitTrackSectionCurved(int tileX, int tileZ, float x, float z, TrackVectorSection trackVectorSection, float treeWidth)
        {
            // We're working relative to the track section, so offset as needed.
            x += (tileX - trackVectorSection.Location.TileX) * 2048;
            z += (tileZ - trackVectorSection.Location.TileZ) * 2048;
            var sx = trackVectorSection.Location.Location.X;
            var sz = trackVectorSection.Location.Location.Z;

            // Do a preliminary cull based on a bounding square around the track section.
            // Bounding distance is (radius * angle + error) by (radius * angle + error) around starting coordinates but no more than 2 for angle.
            var boundingDistance = trackSection.Radius * Math.Min(Math.Abs(MathHelper.ToRadians(trackSection.Angle)), 2) + MaximumCenterlineOffset+treeWidth;
            var dx = Math.Abs(x - sx);
            var dz = Math.Abs(z - sz);
            if (dx > boundingDistance || dz > boundingDistance)
                return false;

            // To simplify the math, center around the start of the track section, rotate such that the track section starts out pointing north (+z) and flip so the track curves to the right.
            x -= sx;
            z -= sz;
            var rotated = EarthCoordinates.Rotate2D(trackVectorSection.Direction.Y, x, z);
            if (trackSection.Angle < 0)
                rotated.x *= -1;

            // Compute distance to curve's center at (radius,0) then adjust to get distance from centerline.
            dx = (float)rotated.x - trackSection.Radius;
            var lat = Math.Sqrt(dx * dx + rotated.z * rotated.z) - trackSection.Radius;
            if (Math.Abs(lat) > MaximumCenterlineOffset + treeWidth)
                return false;

            // Compute distance along curve (ensure we are in the top right quadrant, otherwise our math goes wrong).
            if (rotated.z < -InitErrorMargin || rotated.x > trackSection.Radius + InitErrorMargin || rotated.z > trackSection.Radius + InitErrorMargin)
                return false;
            var radiansAlongCurve = (float)Math.Asin(rotated.z / trackSection.Radius);
            var lon = radiansAlongCurve * trackSection.Radius;
            var trackSectionLength = GetLength(trackSection);
            if (lon < -InitErrorMargin || lon > trackSectionLength + InitErrorMargin)
                return false;

            return true;
        }

        bool InitTrackSectionStraight(int tileX, int tileZ, float x, float z, TrackVectorSection trackVectorSection, float treeWidth)
        {
            // We're working relative to the track section, so offset as needed.
            x += (tileX - trackVectorSection.Location.TileX) * 2048;
            z += (tileZ - trackVectorSection.Location.TileZ) * 2048;
            var sx = trackVectorSection.Location.Location.X;
            var sz = trackVectorSection.Location.Location.Z;

            // Do a preliminary cull based on a bounding square around the track section.
            // Bounding distance is (length + error) by (length + error) around starting coordinates.
            var boundingDistance = trackSection.Length + MaximumCenterlineOffset + treeWidth;
            var dx = Math.Abs(x - sx);
            var dz = Math.Abs(z - sz);
            if (dx > boundingDistance || dz > boundingDistance)
                return false;

            // Calculate distance along and away from the track centerline.
            float lat, lon;
            (lat, lon) = EarthCoordinates.Survey(sx, sz, trackVectorSection.Direction.Y, x, z);
            var trackSectionLength = GetLength(trackSection);
            if (Math.Abs(lat) > MaximumCenterlineOffset + treeWidth)
                return false;
            if (lon < -InitErrorMargin || lon > trackSectionLength + InitErrorMargin)
                return false;

            return true;
        }

        static float GetLength(TrackSection trackSection)
        {
            return trackSection.Curved ? trackSection.Radius * Math.Abs(MathHelper.ToRadians(trackSection.Angle)) : trackSection.Length;
        }

        public override void Draw()
        {
            graphicsDevice.SetVertexBuffer(VertexBuffer);
            graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, PrimitiveCount);
        }
    }

    //[CallOnThread("Render")]
    public class ForestMaterial : Material
    {
        readonly Texture2D treeTexture;
        private readonly SceneryShader shader;
        private readonly int techniqueIndex;

        //[CallOnThread("Loader")]
        public ForestMaterial(Viewer viewer, string treeTexturePath)
            : base(viewer, treeTexturePath)
        {
            treeTexture = Viewer.TextureManager.Get(treeTexturePath, true);
            shader = Viewer.MaterialManager.SceneryShader;
            for (int i = 0; i < shader.Techniques.Count; i++)
            {
                if (shader.Techniques[i].Name == "Forest")
                {
                    techniqueIndex = i;
                    break;
                }
            }

        }

        public override void SetState(Material previousMaterial)
        {
            shader.CurrentTechnique = shader.Techniques[techniqueIndex]; //["Forest"];

            shader.ImageTexture = treeTexture;
            shader.ReferenceAlpha = 200;

            // Enable alpha blending for everything: this allows distance scenery to appear smoothly.

            graphicsDevice.BlendState = BlendState.NonPremultiplied;
            graphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
        }

        public override void Render(List<RenderItem> renderItems, ref Matrix view, ref Matrix projection, ref Matrix viewProjection)
        {
            shader.SetViewMatrix(ref view);
            foreach (var pass in shader.CurrentTechnique.Passes)
            {
                for (int i = 0; i < renderItems.Count; i++)
                {
                    RenderItem item = renderItems[i];
                    shader.SetMatrix(in item.XNAMatrix, in viewProjection);
                    shader.ZBias = item.RenderPrimitive.ZBias;
                    pass.Apply();
                    item.RenderPrimitive.Draw();
                }
            }
        }

        public override void ResetState()
        {
            Viewer.MaterialManager.SceneryShader.ReferenceAlpha = 0;

            graphicsDevice.BlendState = BlendState.Opaque;
        }

        public override Texture2D GetShadowTexture()
        {
            return treeTexture;
        }

        public override void Mark()
        {
            Viewer.TextureManager.Mark(treeTexture);
            base.Mark();
        }
    }
}
