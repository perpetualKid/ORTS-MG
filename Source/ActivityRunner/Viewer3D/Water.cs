// COPYRIGHT 2009, 2010, 2011, 2012, 2013, 2014 by the Open Rails project.
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

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orts.Common;
using Orts.Common.Xna;

namespace Orts.ActivityRunner.Viewer3D
{
    [DebuggerDisplay("TileX = {TileX}, TileZ = {TileZ}, Size = {Size}")]
    public class WaterPrimitive : RenderPrimitive
    {
        private static KeyValuePair<float, Material>[] WaterLayers;
        private readonly Viewer Viewer;
        private readonly int TileX, TileZ, Size;
        private readonly VertexBuffer VertexBuffer;
        private readonly IndexBuffer IndexBuffer;
        private readonly int PrimitiveCount;
        private readonly VertexBufferBinding[] VertexBufferBindings;
        private Matrix xnaMatrix = Matrix.Identity;

        public WaterPrimitive(Viewer viewer, Tile tile)
        {
            Viewer = viewer;
            TileX = tile.TileX;
            TileZ = tile.TileZ;
            Size = tile.Size;

            if (Viewer.ENVFile.WaterLayers != null)
            WaterLayers = Viewer.ENVFile.WaterLayers.Select(layer => new KeyValuePair<float, Material>(layer.Height, Viewer.MaterialManager.Load("Water", Viewer.Simulator.RoutePath + @"\envfiles\textures\" + layer.TextureName))).ToArray();

            LoadGeometry(Viewer.RenderProcess.GraphicsDevice, tile, out PrimitiveCount, out IndexBuffer, out VertexBuffer);
            
            VertexBufferBindings = new[] { new VertexBufferBinding(VertexBuffer), new VertexBufferBinding(GetDummyVertexBuffer(viewer.RenderProcess.GraphicsDevice)) };
        }

        public void PrepareFrame(RenderFrame frame)
        {
            var dTileX = TileX - Viewer.Camera.TileX;
            var dTileZ = TileZ - Viewer.Camera.TileZ;
            var mstsLocation = new Vector3(dTileX * 2048 - 1024 + 1024 * Size, 0, dTileZ * 2048 - 1024 + 1024 * Size);

            if (Viewer.Camera.InFov(mstsLocation, Size * 1448f) && WaterLayers != null)
            {
                xnaMatrix.M41 = mstsLocation.X;
                xnaMatrix.M43 = -mstsLocation.Z;
                foreach (var waterLayer in WaterLayers)
                {
                    xnaMatrix.M42 = mstsLocation.Y + waterLayer.Key;
                    frame.AddPrimitive(waterLayer.Value, this, RenderPrimitiveGroup.World, ref xnaMatrix);
                }
            }
        }

        public override void Draw()
        {
            graphicsDevice.Indices = IndexBuffer;
            graphicsDevice.SetVertexBuffers(VertexBufferBindings);
            graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, PrimitiveCount);
        }

        private void LoadGeometry(GraphicsDevice graphicsDevice, Tile tile, out int primitiveCount, out IndexBuffer indexBuffer, out VertexBuffer vertexBuffer)
        {
            primitiveCount = 0;
            var waterLevels = new Matrix2x2(tile.WaterNW, tile.WaterNE, tile.WaterSW, tile.WaterSE);

            var indexData = new List<short>(16 * 16 * 2 * 3);
            for (var z = 0; z < tile.PatchCount; ++z)
            {
                for (var x = 0; x < tile.PatchCount; ++x)
                {
                    
                    var patch = tile.GetPatch(x, z);

                    if (!patch.WaterEnabled)
                        continue;

                    var nw = (short)(z * 17 + x);  // Vertex index in the north west corner
                    var ne = (short)(nw + 1);
                    var sw = (short)(nw + 17);
                    var se = (short)(sw + 1);

                    primitiveCount += 2;

                    if ((z & 1) == (x & 1))  // Triangles alternate
                    {
                        indexData.Add(nw);
                        indexData.Add(se);
                        indexData.Add(sw);
                        indexData.Add(nw);
                        indexData.Add(ne);
                        indexData.Add(se);
                    }
                    else
                    {
                        indexData.Add(ne);
                        indexData.Add(se);
                        indexData.Add(sw);
                        indexData.Add(nw);
                        indexData.Add(ne);
                        indexData.Add(sw);
                    }
                }
            }
            indexBuffer = new IndexBuffer(graphicsDevice, typeof(short), indexData.Count, BufferUsage.WriteOnly);
            indexBuffer.SetData(indexData.ToArray());
            var vertexData = new List<VertexPositionNormalTexture>(17 * 17);
            for (var z = 0; z < 17; ++z)
            {
                for (var x = 0; x < 17; ++x)
                {
                    var U = (float)x * 4;
                    var V = (float)z * 4;

                    var a = (float)x / 16;
                    var b = (float)z / 16;

                    var e = (a - 0.5f) * 2048 * Size;
                    var n = (b - 0.5f) * 2048 * Size;

                    var y = waterLevels.Interpolate2D(a, b);

                    vertexData.Add(new VertexPositionNormalTexture(new Vector3(e, y, n), Vector3.UnitY, new Vector2(U, V)));
                }
            }
            vertexBuffer = new VertexBuffer(graphicsDevice, typeof(VertexPositionNormalTexture), vertexData.Count, BufferUsage.WriteOnly);
            vertexBuffer.SetData(vertexData.ToArray());
        }

        internal static void Mark()
        {
            if (WaterLayers == null) return;
            foreach (var material in WaterLayers.Select(kvp => kvp.Value))
                material.Mark();
        }
    }

    public class WaterMaterial : Material
    {
        private readonly Texture2D waterTexture;
        private readonly SceneryShader shader;
        private readonly int techniqueIndex;
        private EffectPassCollection shaderPasses;

        public WaterMaterial(Viewer viewer, string waterTexturePath)
            : base(viewer, waterTexturePath)
        {
            waterTexture = Viewer.TextureManager.Get(waterTexturePath, true);
            shader = Viewer.MaterialManager.SceneryShader;
            for (int i = 0; i < shader.Techniques.Count; i++)
            {
                if (shader.Techniques[i].Name == "ImagePS")
                {
                    techniqueIndex = i;
                    break;
                }
            }
        }

        public override void SetState(Material previousMaterial)
        {
            shader.CurrentTechnique = shader.Techniques[techniqueIndex];
            shaderPasses = shader.CurrentTechnique.Passes;
            shader.ImageTexture = waterTexture;
            shader.ReferenceAlpha = 10;

            graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
            graphicsDevice.BlendState = BlendState.NonPremultiplied;
        }

        public override void Render(List<RenderItem> renderItems, ref Matrix view, ref Matrix projection, ref Matrix viewProjection)
        {
            for (int j = 0; j < shaderPasses.Count; j++)
            {
                for (int i = 0; i < renderItems.Count; i++)
                {
                    RenderItem item = renderItems[i];
                    shader.SetMatrix(in item.XNAMatrix, in viewProjection);
                    shader.ZBias = item.RenderPrimitive.ZBias;
                    shaderPasses[j].Apply();
                    item.RenderPrimitive.Draw();
                }
            }
        }

        public override void ResetState()
        {
            var shader = Viewer.MaterialManager.SceneryShader;
            shader.ReferenceAlpha = 0;

            graphicsDevice.BlendState = BlendState.Opaque;
        }

        public override bool GetBlending()
        {
            return true;
        }

        public override void Mark()
        {
            Viewer.TextureManager.Mark(waterTexture);
            base.Mark();
        }
    }
}
