﻿using System;
using System.Threading;
using System.Windows.Forms;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using Orts.ActivityRunner.Viewer3D.Dispatcher.Controls;
using Orts.Common;
using Orts.Common.Native;
using Orts.Settings;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher
{
    public class DispatcherViewer: Game
    {
        private readonly Form dispatcherForm;
        private GraphicsDeviceManager graphicsDeviceManager;
        private UserSettings settings;
        private Screen currentScreen;
        private Point windowPosition;
        private Point windowSize;
        private ScreenMode currentScreenMode;
        private Point clientRectangleOffset;
        private bool syncMode;
        private DispatcherViewControl dispatcherView;

        public DispatcherViewer(UserSettings settings)
        {
            dispatcherForm = (Form)Control.FromHandle(Window.Handle);
            this.settings = settings;
            Window.Title = "Open Rails Dispatcher";
            // Run at 10FPS fixed-time-step.
            IsFixedTimeStep = true;
            TargetElapsedTime = TimeSpan.FromMilliseconds(50);
            InactiveSleepTime = TimeSpan.FromMilliseconds(50);

            graphicsDeviceManager = new GraphicsDeviceManager(this)
            {
                SynchronizeWithVerticalRetrace = settings.VerticalSync,
                PreferredBackBufferFormat = SurfaceFormat.Color,
                PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8,
                PreferMultiSampling = settings.MultisamplingCount > 0,
                GraphicsProfile = GraphicsProfile.HiDef,
            };
            graphicsDeviceManager.PreparingDeviceSettings += GraphicsDeviceManager_PreparingDeviceSettings;

            if (Screen.AllScreens.Length > 1 && Screen.AllScreens[0].Primary)
                currentScreen = Screen.AllScreens[1];
            else
                currentScreen = Screen.AllScreens[0];

            windowSize = new Point(settings.WindowSize_DispatcherViewer[0], settings.WindowSize_DispatcherViewer[1]);
            windowPosition = new Point(
                currentScreen.WorkingArea.Left + (currentScreen.WorkingArea.Size.Width - windowSize.X) / 2, 
                currentScreen.WorkingArea.Top + (currentScreen.WorkingArea.Size.Height - windowSize.Y) / 2);
            clientRectangleOffset = new Point(dispatcherForm.Width - dispatcherForm.ClientRectangle.Width,
                dispatcherForm.Height - dispatcherForm.ClientRectangle.Height);

            SynchronizeGraphicsDeviceManager(ScreenMode.WindowedPresetResolution);

            Window.ClientSizeChanged += Window_ClientSizeChanged;

            dispatcherView = new DispatcherViewControl
            {
                Dock = DockStyle.Fill,
            };
            Control.FromHandle(Window.Handle).Controls.Add(dispatcherView);
            dispatcherView.Initialize(Program.Simulator);
        }

        private void GraphicsDeviceManager_PreparingDeviceSettings(object sender, PreparingDeviceSettingsEventArgs e)
        {
            // This stops ResolveBackBuffer() clearing the back buffer.
            e.GraphicsDeviceInformation.PresentationParameters.RenderTargetUsage = RenderTargetUsage.PreserveContents;
            e.GraphicsDeviceInformation.PresentationParameters.DepthStencilFormat = DepthFormat.Depth24Stencil8;
            e.GraphicsDeviceInformation.PresentationParameters.MultiSampleCount = settings.MultisamplingCount;
        }

        private void Window_ClientSizeChanged(object sender, EventArgs e)
        {
            if (syncMode) return;
            if (currentScreenMode == ScreenMode.WindowedPresetResolution)
                windowSize = Window.ClientBounds.Size;
            //originally, following code would be in Window.LocationChanged handler, but seems to be more reliable here for MG version 3.7.1
            if (currentScreenMode == ScreenMode.WindowedPresetResolution)
                windowPosition = Window.Position;
            // if (fullscreen) gameWindow is moved to different screen we may need to refit for different screen resolution
            Screen newScreen = Screen.FromControl(dispatcherForm);
            (newScreen, currentScreen) = (currentScreen, newScreen);
            if (newScreen.DeviceName != currentScreen.DeviceName && currentScreenMode != ScreenMode.WindowedPresetResolution)
            {
                SynchronizeGraphicsDeviceManager(currentScreenMode);
                //reset Window position to center on new screen
                windowPosition = new Point(
                    currentScreen.WorkingArea.Left + (currentScreen.WorkingArea.Size.Width - windowSize.X) / 2,
                    currentScreen.WorkingArea.Top + (currentScreen.WorkingArea.Size.Height - windowSize.Y) / 2);

            }
        }

        private void SynchronizeGraphicsDeviceManager(ScreenMode targetMode)
        {
            syncMode = true;
            if (graphicsDeviceManager.IsFullScreen)
                graphicsDeviceManager.ToggleFullScreen();
            switch (targetMode)
            {
                case ScreenMode.WindowedPresetResolution:
                    if (targetMode != currentScreenMode)
                        Window.Position = windowPosition;
                    dispatcherForm.FormBorderStyle = FormBorderStyle.Sizable;
                    dispatcherForm.Size = new System.Drawing.Size(settings.WindowSize_DispatcherViewer[0], settings.WindowSize_DispatcherViewer[1]);
                    graphicsDeviceManager.PreferredBackBufferWidth = windowSize.X;
                    graphicsDeviceManager.PreferredBackBufferHeight = windowSize.Y;
                    graphicsDeviceManager.ApplyChanges();
                    break;
                case ScreenMode.FullscreenPresetResolution:
                    graphicsDeviceManager.PreferredBackBufferWidth = currentScreen.WorkingArea.Width - clientRectangleOffset.X;
                    graphicsDeviceManager.PreferredBackBufferHeight = currentScreen.WorkingArea.Height - clientRectangleOffset.Y;
                    graphicsDeviceManager.ApplyChanges();
                    dispatcherForm.FormBorderStyle = FormBorderStyle.FixedSingle;
                    Window.Position = new Point(currentScreen.WorkingArea.Location.X, currentScreen.WorkingArea.Location.Y);
                    graphicsDeviceManager.ApplyChanges();
                    break;
                case ScreenMode.FullscreenNativeResolution:
                    graphicsDeviceManager.PreferredBackBufferWidth = currentScreen.Bounds.Width;
                    graphicsDeviceManager.PreferredBackBufferHeight = currentScreen.Bounds.Height;
                    graphicsDeviceManager.ApplyChanges();
                    dispatcherForm.FormBorderStyle = FormBorderStyle.None;
                    Window.Position = new Point(currentScreen.Bounds.X, currentScreen.Bounds.Y);
                    graphicsDeviceManager.ApplyChanges();
                    break;
            }
            currentScreenMode = targetMode;
            syncMode = false;
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
            dispatcherView.GenerateView();
            // other draw code here
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
            Window.Position = windowPosition;
            base.Initialize();
        }

        private Debugging.DispatchViewer viewer;
        private MouseState previous;

        protected override void Update(GameTime gameTime)
        {
            if (IsActive)
            {
                KeyboardState state = Keyboard.GetState();
                if (state.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.A))
                {
                    if (null == viewer)
                        viewer = new Debugging.DispatchViewer(Program.Viewer.Simulator, null);
                    viewer.Show();
                }
                MouseState mousestate = Mouse.GetState(Window);
                if (mousestate.RightButton == Microsoft.Xna.Framework.Input.ButtonState.Released && previous.RightButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed)
                {
                    SynchronizeGraphicsDeviceManager(currentScreenMode.Next());
                }
                previous = mousestate;
            }
            base.Update(gameTime);
        }
    }
}