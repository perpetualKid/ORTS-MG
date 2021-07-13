using System.IO;

using Microsoft.Xna.Framework.Graphics;

using Orts.ActivityRunner.Viewer3D.Processes;
using Orts.Formats.Msts.Files;
using Orts.Simulation;

namespace Orts.ActivityRunner.Viewer3D.Materials
{
    internal class LoadingScreenMaterial : LoadingMaterial
    {
        public LoadingScreenMaterial(Game game)
            : base(game)
        {
        }

        private static bool IsWideScreen(Game game)
        {
            float x = game.RenderProcess.DisplaySize.X;
            float y = game.RenderProcess.DisplaySize.Y;

            return (x / y > 1.5);
        }

        protected override Texture2D GetTexture(Game game)
        {
            Texture2D texture;
            GraphicsDevice gd = game.RenderProcess.GraphicsDevice;
            string defaultScreen = "load.ace";

            string loadingScreen = Simulator.Instance.Route.LoadingScreen;
            if (IsWideScreen(game))
            {
                string loadingScreenWide = Simulator.Instance.Route.LoadingScreenWide;
                loadingScreen = loadingScreenWide ?? loadingScreen;
            }
            loadingScreen = loadingScreen ?? defaultScreen;
            string path = Path.Combine(Simulator.Instance.RouteFolder.CurrentFolder, loadingScreen);
            if (Path.GetExtension(path) == ".dds" && File.Exists(path))
            {
                DDSLib.DDSFromFile(path, gd, true, out texture);
            }
            else if (Path.GetExtension(path) == ".ace")
            {
                string alternativeTexture = Path.ChangeExtension(path, ".dds");

                if (File.Exists(alternativeTexture) && game.Settings.PreferDDSTexture)
                {
                    DDSLib.DDSFromFile(alternativeTexture, gd, true, out texture);
                }
                else if (File.Exists(path))
                {
                    texture = AceFile.Texture2DFromFile(gd, path);
                }
                else
                {
                    path = Path.Combine(Simulator.Instance.RouteFolder.CurrentFolder, defaultScreen);
                    if (File.Exists(path))
                    {
                        texture = AceFile.Texture2DFromFile(gd, path);
                    }
                    else
                    {
                        texture = null;
                    }
                }

            }
            else
            {
                texture = null;
            }
            return texture;
        }
    }
}
