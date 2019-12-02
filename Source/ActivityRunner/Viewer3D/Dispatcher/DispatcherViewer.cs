using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Orts.Simulation;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher
{
    public class DispatcherRenderer
    {
        private readonly Form dispatcherForm;
        private GraphicsDeviceManager graphicsDeviceManager;

        public DispatcherRenderer(Game game)
        {
            dispatcherForm = (Form)Control.FromHandle(game.Window.Handle);

            game.Window.Title = "Open Rails Dispatcher";
            graphicsDeviceManager = new GraphicsDeviceManager(game);
        }
    }

    public class DispatcherViewer: Game
    {
        public DispatcherViewer()
        {
            DispatcherRenderer renderer = new DispatcherRenderer(this);
        }

        protected override void BeginRun()
        {
            base.BeginRun();
        }

        protected override bool BeginDraw()
        {
            return base.BeginDraw();
        }

        protected override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
        }

        protected override void EndDraw()
        {
            base.EndDraw();
        }

        protected override void EndRun()
        {
            base.EndRun();
        }

        protected override void Initialize()
        {
            base.Initialize();
        }

        private Debugging.DispatchViewer viewer;
        protected override void Update(GameTime gameTime)
        {
            KeyboardState state = Keyboard.GetState();
            if (state.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.A))
            {
                if (null == viewer)
                    viewer = new Debugging.DispatchViewer(Program.Viewer.Simulator, null);
                viewer.Show();
            }
            base.Update(gameTime);
        }
    }
}
