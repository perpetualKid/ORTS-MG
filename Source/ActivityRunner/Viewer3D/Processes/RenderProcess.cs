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

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orts.ActivityRunner.Processes;
using Orts.Common;
using Orts.Common.Calc;
using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace Orts.ActivityRunner.Viewer3D.Processes
{
    //[CallOnThread("Render")]
    public class RenderProcess
    {
        public const int ShadowMapCountMaximum = 4;

        public Point DisplaySize { get; private set; }
        public GraphicsDevice GraphicsDevice { get { return game.GraphicsDevice; } }
        public bool IsActive { get { return game.IsActive; } }
        public Viewer Viewer { get { return (game.State as GameStateViewer3D)?.Viewer; } }

        public Profiler Profiler { get; private set; }

        private readonly Game game;
        private readonly Form gameForm;
        private readonly System.Drawing.Size gameWindowSize;
        private readonly WatchdogToken watchdogToken;
        private System.Drawing.Point gameWindowOrigin;
        private Screen currentScreen;
        private ScreenMode currentScreenMode;
        private bool toggleScreenRequested;

        public GraphicsDeviceManager GraphicsDeviceManager { get; private set; }

        RenderFrame CurrentFrame;   // a frame contains a list of primitives to draw at a specified time
        RenderFrame NextFrame;      // we prepare the next frame in the background while the current one is rendering,

        public bool IsMouseVisible { get; set; }  // handles cross thread issues by signalling RenderProcess of a change
        public Cursor ActualCursor = Cursors.Default;

        // Diagnostic information
        public SmoothedData FrameRate { get; private set; }
        public SmoothedDataWithPercentiles FrameTime { get; private set; }
        public int[] PrimitiveCount { get; private set; }
        public int[] PrimitivePerFrame { get; private set; }
        public int[] ShadowPrimitiveCount { get; private set; }
        public int[] ShadowPrimitivePerFrame { get; private set; }

        // Dynamic shadow map setup.
        public static int ShadowMapCount { get; private set; } = -1; // number of shadow maps
        public static int[] ShadowMapDistance; // distance of shadow map center from camera
        public static int[] ShadowMapDiameter; // diameter of shadow map
        public static float[] ShadowMapLimit; // diameter of shadow map far edge from camera

        internal RenderProcess(Game game)
        {
            this.game = game;
            gameForm = (Form)Control.FromHandle(game.Window.Handle);

            watchdogToken = new WatchdogToken(System.Threading.Thread.CurrentThread);

            Profiler = new Profiler("Render");
            Profiler.SetThread();
            game.SetThreadLanguage();

            game.Window.Title = "Open Rails";
            GraphicsDeviceManager = new GraphicsDeviceManager(game);

            var windowSizeParts = game.Settings.WindowSize.Split(new[] { 'x' }, 2);
            gameWindowSize = new System.Drawing.Size(Convert.ToInt32(windowSizeParts[0]), Convert.ToInt32(windowSizeParts[1]));

            FrameRate = new SmoothedData();
            FrameTime = new SmoothedDataWithPercentiles();
            PrimitiveCount = new int[(int)RenderPrimitiveSequence.Sentinel];
            PrimitivePerFrame = new int[(int)RenderPrimitiveSequence.Sentinel];

            // Run the game initially at 10FPS fixed-time-step. Do not change this! It affects the loading performance.
            game.IsFixedTimeStep = true;
            game.TargetElapsedTime = TimeSpan.FromMilliseconds(100);
            game.InactiveSleepTime = TimeSpan.FromMilliseconds(100);

            // Set up the rest of the graphics according to the settings.
            GraphicsDeviceManager.SynchronizeWithVerticalRetrace = game.Settings.VerticalSync;
            GraphicsDeviceManager.PreferredBackBufferFormat = SurfaceFormat.Color;
            GraphicsDeviceManager.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
            GraphicsDeviceManager.IsFullScreen = true;
            GraphicsDeviceManager.PreferMultiSampling = game.Settings.MultisamplingCount > 0;
            GraphicsDeviceManager.PreparingDeviceSettings += new EventHandler<PreparingDeviceSettingsEventArgs>(GDM_PreparingDeviceSettings);

            currentScreen = Screen.PrimaryScreen;
            gameWindowOrigin = new System.Drawing.Point((currentScreen.WorkingArea.Right - gameWindowSize.Width) / 2, (currentScreen.WorkingArea.Bottom - gameWindowSize.Height) / 2);
            System.Drawing.Point tempGameWindowOrigin = gameWindowOrigin;
            SynchronizeGraphicsDeviceManager(game.Settings.FullScreen ?
                game.Settings.NativeFullscreenResolution ? ScreenMode.FullscreenNativeResolution : ScreenMode.FullscreenPresetResolution
                : ScreenMode.WindowedPresetResolution);

            //restore gameWindowOrigin which will be overriden when game started in Fullscreen ()
            gameWindowOrigin = tempGameWindowOrigin;

            RenderPrimitive.SetGraphicsDevice(game.GraphicsDevice);

            UserInput.Initialize(game);
            gameForm.LocationChanged += GameForm_LocationChanged;
        }

        private void GameForm_LocationChanged(object sender, EventArgs e)
        {
            // if (fullscreen) gameWindow is moved to different screen we may need to refit for different screen reolution
            Screen newScreen = Screen.FromControl(gameForm);
            (newScreen, currentScreen) = (currentScreen, newScreen);
            if (newScreen.DeviceName != currentScreen.DeviceName)
            {
                if (currentScreenMode != ScreenMode.WindowedPresetResolution)
                    gameWindowOrigin = new System.Drawing.Point(currentScreen.Bounds.Left + (currentScreen.WorkingArea.Width - gameWindowSize.Width) / 2, 
                        currentScreen.Bounds.Top + (currentScreen.WorkingArea.Height - gameWindowSize.Height) / 2);
                SynchronizeGraphicsDeviceManager(currentScreenMode);
            }
        }

        void GDM_PreparingDeviceSettings(object sender, PreparingDeviceSettingsEventArgs e)
        {
            e.GraphicsDeviceInformation.GraphicsProfile = GraphicsProfile.HiDef;
            // This stops ResolveBackBuffer() clearing the back buffer.
            e.GraphicsDeviceInformation.PresentationParameters.RenderTargetUsage = RenderTargetUsage.PreserveContents;
            e.GraphicsDeviceInformation.PresentationParameters.DepthStencilFormat = DepthFormat.Depth24Stencil8;
            e.GraphicsDeviceInformation.PresentationParameters.MultiSampleCount = game.Settings.MultisamplingCount;
        }

        internal void Start()
        {
            game.WatchdogProcess.Register(watchdogToken);

            DisplaySize = GraphicsDevice.Viewport.Bounds.Size;

            if (game.Settings.ShadowMapDistance == 0)
                game.Settings.ShadowMapDistance = game.Settings.ViewingDistance / 2;

            ShadowMapCount = game.Settings.ShadowMapCount;
            if (!game.Settings.DynamicShadows || ShadowMapCount < 0)
                ShadowMapCount = 0;
            else if (ShadowMapCount > ShadowMapCountMaximum)
                ShadowMapCount = ShadowMapCountMaximum;
            if (ShadowMapCount < 1)
                game.Settings.DynamicShadows = false;

            ShadowMapDistance = new int[ShadowMapCount];
            ShadowMapDiameter = new int[ShadowMapCount];
            ShadowMapLimit = new float[ShadowMapCount];

            ShadowPrimitiveCount = new int[ShadowMapCount];
            ShadowPrimitivePerFrame = new int[ShadowMapCount];

            InitializeShadowMapLocations();

            CurrentFrame = new RenderFrame(game);
            NextFrame = new RenderFrame(game);
        }

        void InitializeShadowMapLocations()
        {
            float ratio = (float)DisplaySize.X / DisplaySize.Y;
            float fov = MathHelper.ToRadians(game.Settings.ViewingFOV);
            float n = 0.5f;
            float f = game.Settings.ShadowMapDistance;
            if (f == 0)
                f = game.Settings.ViewingDistance / 2f;

            var m = (float)ShadowMapCount;
            var LastC = n;
            for (var shadowMapIndex = 0; shadowMapIndex < ShadowMapCount; shadowMapIndex++)
            {
                //     Clog  = split distance i using logarithmic splitting
                //         i
                // Cuniform  = split distance i using uniform splitting
                //         i
                //         n = near view plane
                //         f = far view plane
                //         m = number of splits
                //
                //                   i/m
                //     Clog  = n(f/n)
                //         i
                // Cuniform  = n+(f-n)i/m
                //         i

                // Calculate the two Cs and average them to get a good balance.
                var i = (float)(shadowMapIndex + 1);
                var Clog = n * (float)Math.Pow(f / n, i / m);
                var Cuniform = n + (f - n) * i / m;
                var C = (3 * Clog + Cuniform) / 4;

                // This shadow map goes from LastC to C; calculate the correct center and diameter for the sphere from the view frustum.
                var height1 = (float)Math.Tan(fov / 2) * LastC;
                var height2 = (float)Math.Tan(fov / 2) * C;
                var width1 = height1 * ratio;
                var width2 = height2 * ratio;
                var corner1 = new Vector3(height1, width1, LastC);
                var corner2 = new Vector3(height2, width2, C);
                var cornerCenter = (corner1 + corner2) / 2;
                var length = cornerCenter.Length();
                cornerCenter.Normalize();
                var center = length / Vector3.Dot(cornerCenter, Vector3.UnitZ);
                var diameter = 2 * (float)Math.Sqrt(height2 * height2 + width2 * width2 + (C - center) * (C - center));

                ShadowMapDistance[shadowMapIndex] = (int)center;
                ShadowMapDiameter[shadowMapIndex] = (int)diameter;
                ShadowMapLimit[shadowMapIndex] = C;
                LastC = C;
            }
        }

        internal void Update(GameTime gameTime)
        {
            if (IsMouseVisible != game.IsMouseVisible)
                game.IsMouseVisible = IsMouseVisible;

            Cursor.Current = ActualCursor;

            if (toggleScreenRequested)
            {
                SynchronizeGraphicsDeviceManager(currentScreenMode.Next());
                toggleScreenRequested = false;
                Viewer.DefaultViewport = GraphicsDevice.Viewport;
            }

            if (gameTime.TotalGameTime.TotalSeconds > 0.001)
            {
                game.UpdaterProcess.WaitTillFinished();

                // Must be done in XNA Game thread.
                UserInput.Update();

                // Swap frames and start the next update (non-threaded updater does the whole update).
                SwapFrames(ref CurrentFrame, ref NextFrame);
                game.UpdaterProcess.StartUpdate(NextFrame, gameTime.TotalGameTime.TotalSeconds);
            }
            else
            {
                SynchronizeGraphicsDeviceManager(currentScreenMode);
                Viewer.DefaultViewport = GraphicsDevice.Viewport;

            }
        }

        void SynchronizeGraphicsDeviceManager(ScreenMode targetMode)
        {
            gameForm.LocationChanged -= GameForm_LocationChanged;
            switch (targetMode)
            {
                case ScreenMode.WindowedPresetResolution:
                    if (GraphicsDeviceManager.IsFullScreen)
                        GraphicsDeviceManager.ToggleFullScreen();
                    if (targetMode != currentScreenMode)
                        gameForm.Location = gameWindowOrigin;
                    gameForm.FormBorderStyle = FormBorderStyle.FixedSingle;
                    GraphicsDeviceManager.PreferredBackBufferWidth = gameWindowSize.Width;
                    GraphicsDeviceManager.PreferredBackBufferHeight = gameWindowSize.Height;
                    GraphicsDeviceManager.ApplyChanges();
                    break;
                case ScreenMode.FullscreenPresetResolution:
                    gameForm.FormBorderStyle = FormBorderStyle.FixedSingle;
                    GraphicsDeviceManager.PreferredBackBufferWidth = gameWindowSize.Width;
                    GraphicsDeviceManager.PreferredBackBufferHeight = gameWindowSize.Height;
                    GraphicsDeviceManager.ApplyChanges();
                    if (!GraphicsDeviceManager.IsFullScreen)
                        GraphicsDeviceManager.ToggleFullScreen();
                    break;
                case ScreenMode.FullscreenNativeResolution:
                    if (GraphicsDeviceManager.IsFullScreen)
                        GraphicsDeviceManager.ToggleFullScreen();
                    GraphicsDeviceManager.PreferredBackBufferWidth = currentScreen.Bounds.Width;
                    GraphicsDeviceManager.PreferredBackBufferHeight = currentScreen.Bounds.Height;
                    GraphicsDeviceManager.ApplyChanges();
                    gameForm.FormBorderStyle = game.Settings.FastFullScreenAltTab ? FormBorderStyle.None : FormBorderStyle.FixedSingle;
                    if (targetMode != currentScreenMode)
                        gameWindowOrigin = gameForm.Location;
                    gameForm.Location = currentScreen.Bounds.Location;
                    GraphicsDeviceManager.ApplyChanges();
                    if (!game.Settings.FastFullScreenAltTab)
                        GraphicsDeviceManager.ToggleFullScreen();
                    break;
            }
            currentScreenMode = targetMode;
            gameForm.LocationChanged += GameForm_LocationChanged;
        }

        internal void BeginDraw()
        {
            if (game.State == null)
                return;

            Profiler.Start();
            watchdogToken.Ping();

            // Sort-of hack to allow the NVIDIA PerfHud to display correctly.
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;

            CurrentFrame.IsScreenChanged = (DisplaySize.X != GraphicsDevice.Viewport.Width) || (DisplaySize.Y != GraphicsDevice.Viewport.Height);
            if (CurrentFrame.IsScreenChanged)
            {
                DisplaySize = new Point(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
                InitializeShadowMapLocations();
            }

            game.State.BeginRender(CurrentFrame);
        }

        internal void Draw()
        {
            if (Debugger.IsAttached)
            {
                CurrentFrame.Draw();
            }
            else
            {
                try
                {
                    CurrentFrame.Draw();
                }
                catch (Exception error)
                {
                    game.ProcessReportError(error);
                }
            }
        }

        internal void EndDraw()
        {
            if (game.State == null)
                return;

            game.State.EndRender(CurrentFrame);

            Array.Copy(PrimitiveCount, PrimitivePerFrame, (int)RenderPrimitiveSequence.Sentinel);
            Array.Copy(ShadowPrimitiveCount, ShadowPrimitivePerFrame, ShadowMapCount);
            //for (var i = 0; i < (int)RenderPrimitiveSequence.Sentinel; i++)
            //{
            //    PrimitivePerFrame[i] = PrimitiveCount[i];
            //    PrimitiveCount[i] = 0;
            //}
            //for (var shadowMapIndex = 0; shadowMapIndex < ShadowMapCount; shadowMapIndex++)
            //{
            //    ShadowPrimitivePerFrame[shadowMapIndex] = ShadowPrimitiveCount[shadowMapIndex];
            //    ShadowPrimitiveCount[shadowMapIndex] = 0;
            //}

            // Sort-of hack to allow the NVIDIA PerfHud to display correctly.
            GraphicsDevice.DepthStencilState = DepthStencilState.None;

            Profiler.Stop();
        }

        internal void Stop()
        {
            game.WatchdogProcess.Unregister(watchdogToken);
        }

        static void SwapFrames(ref RenderFrame frame1, ref RenderFrame frame2)
        {
            RenderFrame temp = frame1;
            frame1 = frame2;
            frame2 = temp;
        }

        //[CallOnThread("Updater")]
        public void ToggleFullScreen()
        {
            toggleScreenRequested = true;
        }

        //[CallOnThread("Render")]
        //[CallOnThread("Updater")]
        public void ComputeFPS(float elapsedRealTime)
        {
            if (elapsedRealTime < 0.001)
                return;

            FrameRate.Update(elapsedRealTime, 1f / elapsedRealTime);
            FrameTime.Update(elapsedRealTime, elapsedRealTime);
        }
    }
}
