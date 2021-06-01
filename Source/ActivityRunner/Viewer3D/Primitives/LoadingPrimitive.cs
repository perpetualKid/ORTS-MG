
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Orts.ActivityRunner.Viewer3D.Materials;

using Game = Orts.ActivityRunner.Viewer3D.Processes.Game;

namespace Orts.ActivityRunner.Viewer3D.Primitives
{
    internal class LoadingPrimitive : RenderPrimitive
    {
        public readonly LoadingMaterial Material;
        private readonly VertexBuffer VertexBuffer;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public LoadingPrimitive(Game game)
        {
            Material = GetMaterial(game);
            var verticies = GetVerticies(game);
            VertexBuffer = new VertexBuffer(game.GraphicsDevice, typeof(VertexPositionTexture), verticies.Length, BufferUsage.WriteOnly);
            VertexBuffer.SetData(verticies);
        }

        protected virtual LoadingMaterial GetMaterial(Game game)
        {
            return new LoadingMaterial(game);
        }

        protected virtual VertexPositionTexture[] GetVerticies(Game game)
        {
            var dd = (float)Material.TextureWidth / 2;
            return new[] {
                    new VertexPositionTexture(new Vector3(-dd - 0.5f, +dd + 0.5f, -3), new Vector2(0, 0)),
                    new VertexPositionTexture(new Vector3(+dd - 0.5f, +dd + 0.5f, -3), new Vector2(1, 0)),
                    new VertexPositionTexture(new Vector3(-dd - 0.5f, -dd + 0.5f, -3), new Vector2(0, 1)),
                    new VertexPositionTexture(new Vector3(+dd - 0.5f, -dd + 0.5f, -3), new Vector2(1, 1)),
                };
        }

        public override void Draw()
        {
            graphicsDevice.SetVertexBuffer(VertexBuffer);
            graphicsDevice.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 2);
        }
    }

}
