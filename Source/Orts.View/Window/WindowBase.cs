using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using static System.Net.Mime.MediaTypeNames;

namespace Orts.View.Window
{
    public class BaseShader : Effect
    {
        public BaseShader(GraphicsDevice graphicsDevice)
            : base(graphicsDevice, GetEffectCode())
        {
        }

        private static byte[] GetEffectCode()
        {
            string filePath = Path.Combine(".\\Content", "PopupWindow.mgfx");
            return File.ReadAllBytes(filePath);
        }
    }

    public class PopupWindowShader : BaseShader
    {
        readonly EffectParameter world;
        readonly EffectParameter worldViewProjection;
        readonly EffectParameter glassColor;
        readonly EffectParameter screenSize;
        readonly EffectParameter screenTexture;

        public Texture2D Screen
        {
            set
            {
                screenTexture.SetValue(value);
                if (value == null)
                    screenSize.SetValue(new Vector2(0, 0));
                else
                    screenSize.SetValue(new Vector2(value.Width, value.Height));
            }
        }

        public Color GlassColor { set { glassColor.SetValue(new Vector3(value.R / 255f, value.G / 255f, value.B / 255f)); } }

        public void SetMatrix(in Matrix w, ref Matrix wvp)
        {
            world.SetValue(w);
            worldViewProjection.SetValue(wvp);
        }

        public PopupWindowShader(GraphicsDevice graphicsDevice)
            : base(graphicsDevice)
        {
            world = Parameters["World"];
            worldViewProjection = Parameters["WorldViewProjection"];
            glassColor = Parameters["GlassColor"];
            screenSize = Parameters["ScreenSize"];
            screenTexture = Parameters["ScreenTexture"];
            using (var stream = File.OpenRead(Path.Combine(".\\Content", "Window.png")))
            {
                Parameters["WindowTexture"].SetValue(Texture2D.FromStream(graphicsDevice, stream));
            }
        }
    }

    public abstract class WindowBase
    {
        protected static GraphicsDevice graphicsDevice;
        private VertexBuffer windowVertexBuffer;
        private IndexBuffer windowIndexBuffer;
        protected Rectangle location;
        protected PopupWindowShader shader;

        public static void SetGraphicsDevice(GraphicsDevice graphicsDevice)
        {
            WindowBase.graphicsDevice = graphicsDevice;
        }

        protected virtual void InitializeBuffers()
        {
            if (windowVertexBuffer == null)
            {
                // Edges/corners are 32px (1/4th image size).
                int gp = 32 - 16;// BaseFontSize + Owner.TextFontDefault.Height;
                VertexPositionTexture[] vertexData = new[] {
					//  0  1  2  3
					new VertexPositionTexture(new Vector3(0 * location.Width + 00, 0 * location.Height + 00, 0), new Vector2(0.00f / 2, 0.00f)),
                    new VertexPositionTexture(new Vector3(0 * location.Width + gp, 0 * location.Height + 00, 0), new Vector2(0.25f / 2, 0.00f)),
                    new VertexPositionTexture(new Vector3(1 * location.Width - gp, 0 * location.Height + 00, 0), new Vector2(0.75f / 2, 0.00f)),
                    new VertexPositionTexture(new Vector3(1 * location.Width - 00, 0 * location.Height + 00, 0), new Vector2(1.00f / 2, 0.00f)),
					//  4  5  6  7
					new VertexPositionTexture(new Vector3(0 * location.Width + 00, 0 * location.Height + gp, 0), new Vector2(0.00f / 2, 0.25f)),
                    new VertexPositionTexture(new Vector3(0 * location.Width + gp, 0 * location.Height + gp, 0), new Vector2(0.25f / 2, 0.25f)),
                    new VertexPositionTexture(new Vector3(1 * location.Width - gp, 0 * location.Height + gp, 0), new Vector2(0.75f / 2, 0.25f)),
                    new VertexPositionTexture(new Vector3(1 * location.Width - 00, 0 * location.Height + gp, 0), new Vector2(1.00f / 2, 0.25f)),
					//  8  9 10 11
					new VertexPositionTexture(new Vector3(0 * location.Width + 00, 1 * location.Height - gp, 0), new Vector2(0.00f / 2, 0.75f)),
                    new VertexPositionTexture(new Vector3(0 * location.Width + gp, 1 * location.Height - gp, 0), new Vector2(0.25f / 2, 0.75f)),
                    new VertexPositionTexture(new Vector3(1 * location.Width - gp, 1 * location.Height - gp, 0), new Vector2(0.75f / 2, 0.75f)),
                    new VertexPositionTexture(new Vector3(1 * location.Width - 00, 1 * location.Height - gp, 0), new Vector2(1.00f / 2, 0.75f)),
					// 12 13 14 15
					new VertexPositionTexture(new Vector3(0 * location.Width + 00, 1 * location.Height - 00, 0), new Vector2(0.00f / 2, 1.00f)),
                    new VertexPositionTexture(new Vector3(0 * location.Width + gp, 1 * location.Height - 00, 0), new Vector2(0.25f / 2, 1.00f)),
                    new VertexPositionTexture(new Vector3(1 * location.Width - gp, 1 * location.Height - 00, 0), new Vector2(0.75f / 2, 1.00f)),
                    new VertexPositionTexture(new Vector3(1 * location.Width - 00, 1 * location.Height - 00, 0), new Vector2(1.00f / 2, 1.00f)),
                };
                windowVertexBuffer = new VertexBuffer(graphicsDevice, typeof(VertexPositionTexture), vertexData.Length, BufferUsage.WriteOnly);
                windowVertexBuffer.SetData(vertexData);
            }
            if (windowIndexBuffer == null)
            {
                short[] indexData = new short[] {
                    0, 4, 1, 5, 2, 6, 3, 7,
                    11, 6, 10, 5, 9, 4, 8,
                    12, 9, 13, 10, 14, 11, 15,
                };
                windowIndexBuffer = new IndexBuffer(graphicsDevice, typeof(short), indexData.Length, BufferUsage.WriteOnly);
                windowIndexBuffer.SetData(indexData);
            }
        }

        private bool screen = true;
        public static float camera2DrotationZ = 0f;
        public static Vector3 camera2DScrollPosition = new Vector3(0, 0, -1);
        public static Vector3 camera2DScrollLookAt = new Vector3(0, 0, 0);

        public virtual void Draw()
        {
            shader.CurrentTechnique = shader.Techniques[screen ? 0 : 1]; //screen == null ? shader.Techniques["PopupWindow"] : shader.Techniques["PopupWindowGlass"];

            shader.GlassColor = Color.Red;

            graphicsDevice.BlendState = BlendState.NonPremultiplied;
            graphicsDevice.RasterizerState = RasterizerState.CullNone;
            graphicsDevice.DepthStencilState = DepthStencilState.None;

            Viewport viewport = graphicsDevice.Viewport;
            Matrix World = Matrix.Identity;
            Vector3 cameraUp = Vector3.Transform(new Vector3(0, -1, 0), Matrix.CreateRotationZ(camera2DrotationZ));
            Matrix View = Matrix.CreateLookAt(camera2DScrollPosition, camera2DScrollLookAt, cameraUp);
            Matrix Projection = Matrix.CreateScale(1, -1, 1) * Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, 1); // nans

            shader.Parameters["World"].SetValue(World);
            shader.Parameters["View"].SetValue(View);
            shader.Parameters["Projection"].SetValue(Projection);
            Matrix wvp = World * View * Projection;
            shader.SetMatrix(World, ref wvp);

            foreach (EffectPass pass in shader.CurrentTechnique.Passes)
            {
                pass.Apply();

                graphicsDevice.SetVertexBuffer(windowVertexBuffer);
                graphicsDevice.Indices = windowIndexBuffer;
                graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleStrip, 0, 0, 20);
            }




        }
    }

}
