using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using GetText;
using GetText.WindowsForms;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Orts.Common;
using Orts.Common.Calc;
using Orts.Common.Info;
using Orts.Common.Input;
using Orts.Common.Logging;
using Orts.TrackViewer.Control;
using Orts.TrackViewer.Settings;
using Orts.View;
using Orts.View.DrawableComponents;
using Orts.View.Track;
using Orts.View.Track.Shapes;
using Orts.View.Window;
using Orts.View.Xna;

using UserCommand = Orts.TrackViewer.Control.UserCommand;

namespace Orts.TrackViewer
{
    public partial class GameWindow : Game, IInputCapture
    {
        private readonly GraphicsDeviceManager graphicsDeviceManager;
        private readonly System.Windows.Forms.Form windowForm;
        private readonly SmoothedData frameRate;

        private SpriteBatch spriteBatch;

        private bool syncing;
        private ScreenMode currentScreenMode;
        private System.Windows.Forms.Screen currentScreen;
        private Point windowPosition;
        private System.Drawing.Size windowSize;
        private Point clientRectangleOffset;
        private Vector2 centerPoint;

        private readonly Action onClientSizeChanged;

        private ContentArea contentArea;

        internal ContentArea ContentArea
        {
            get => contentArea;
            set => windowForm.Invoke((System.Windows.Forms.MethodInvoker)delegate {
                if (value != null)
                {
                    value.ResetSize(Window.ClientBounds.Size, 60);
                    Components.Add(value);
                    value.Enabled = true;
                    Window.Title = windowTitle + Catalog.GetString($" Route: {value.RouteName}");
                }
                else
                {
                    Components.Remove(contentArea);
                    Window.Title = windowTitle;
                }
                if (contentArea != null)
                    contentArea.Enabled = false;
                contentArea = value;
            });
        }

        internal string StatusMessage { get; set; }

        internal TrackViewerSettings Settings { get; }

        internal string LogFileName { get; }

        private Color BackgroundColor;
        internal string backgroundColor;

        #region preferences
        private readonly EnumArray<string, ColorSetting> colorPreferences = new EnumArray<string, ColorSetting>();
        private TrackViewerViewSettings viewSettings;

        #endregion

        internal Catalog Catalog { get; private set; }
        private readonly ObjectPropertiesStore store = new ObjectPropertiesStore();
        private readonly string windowTitle
;
        public GameWindow()
        {
            IEnumerable<string> options = Environment.GetCommandLineArgs().Where(a => a.StartsWith("-", StringComparison.OrdinalIgnoreCase) || a.StartsWith("/", StringComparison.OrdinalIgnoreCase)).Select(a => a.Substring(1));
            Settings = new TrackViewerSettings(options);

            CatalogManager.SetCatalogDomainPattern(CatalogDomainPattern.AssemblyName, null, RuntimeInfo.LocalesFolder);

            if (Settings.UserSettings.Logging)
            {
                LogFileName = System.IO.Path.Combine(Settings.UserSettings.LoggingPath, LoggingUtil.CustomizeLogFileName(Settings.LogFilename));
                LoggingUtil.InitLogging(LogFileName, Settings.UserSettings.LogErrorsOnly, false);
                Settings.Log();
                Trace.WriteLine(LoggingUtil.SeparatorLine);
            }
            LoadSettings();
            frameRate = new SmoothedData();
            windowForm = (System.Windows.Forms.Form)System.Windows.Forms.Control.FromHandle(Window.Handle);
            currentScreen = System.Windows.Forms.Screen.PrimaryScreen;

            InitializeComponent();
            graphicsDeviceManager = new GraphicsDeviceManager(this);
            graphicsDeviceManager.PreparingDeviceSettings += GraphicsPreparingDeviceSettings;
            graphicsDeviceManager.PreferMultiSampling = Settings.UserSettings.MultisamplingCount > 0;
            IsMouseVisible = true;

            // Set title to show revision or build info.
            windowTitle = $"{RuntimeInfo.ProductName} {VersionInfo.Version}";
#if DEBUG
            windowTitle += " (debug)";
#endif
            Window.Title = windowTitle;
            Window.AllowUserResizing = true;

            Window.ClientSizeChanged += Window_ClientSizeChanged; // not using the GameForm event as it does not raise when Window is moved (ie to another screeen) using keyboard shortcut

            //graphicsDeviceManager.SynchronizeWithVerticalRetrace = false;
            //IsFixedTimeStep = false;
            //TargetElapsedTime = TimeSpan.FromMilliseconds(5);
            windowPosition = new Point(
                currentScreen.WorkingArea.Left + (currentScreen.WorkingArea.Size.Width - windowSize.Width) / 2,
                currentScreen.WorkingArea.Top + (currentScreen.WorkingArea.Size.Height - windowSize.Height) / 2);
            clientRectangleOffset = new Point(windowForm.Width - windowForm.ClientRectangle.Width, windowForm.Height - windowForm.ClientRectangle.Height);
            Window.Position = windowPosition;

            SetScreenMode(currentScreenMode);

            windowForm.LocationChanged += WindowForm_LocationChanged;
            windowForm.ClientSizeChanged += WindowForm_ClientSizeChanged;

            // using reflection to be able to trigger ClientSizeChanged event manually as this is not 
            // reliably raised otherwise with the resize functionality below in SetScreenMode
            MethodInfo m = Window.GetType().GetMethod("OnClientSizeChanged", BindingFlags.NonPublic | BindingFlags.Instance);
            onClientSizeChanged = (Action)Delegate.CreateDelegate(typeof(Action), Window, m);

            windowForm.FormClosing += WindowForm_FormClosing;
            LoadLanguage();

            WindowBase.SetGraphicsDevice(GraphicsDevice);
        }

        private void WindowForm_FormClosing(object sender, System.Windows.Forms.FormClosingEventArgs e)
        {
            if (!ExitApplication())
                e.Cancel = true;
        }

        #region window size/position handling
        private void Window_ClientSizeChanged(object sender, EventArgs e)
        {
            ContentArea?.UpdateSize(Window.ClientBounds.Size);
        }

        private void WindowForm_ClientSizeChanged(object sender, EventArgs e)
        {
            centerPoint = new Vector2(Window.ClientBounds.Size.X / 2, Window.ClientBounds.Size.Y / 2);
            if (syncing)
                return;
            if (currentScreenMode == ScreenMode.Windowed)
                windowSize = new System.Drawing.Size(Window.ClientBounds.Width, Window.ClientBounds.Height);
            //originally, following code would be in Window.LocationChanged handler, but seems to be more reliable here for MG version 3.7.1
            if (currentScreenMode == ScreenMode.Windowed)
                windowPosition = Window.Position;
            // if (fullscreen) gameWindow is moved to different screen we may need to refit for different screen resolution
            System.Windows.Forms.Screen newScreen = System.Windows.Forms.Screen.FromControl(windowForm);
            (newScreen, currentScreen) = (currentScreen, newScreen);
            if (newScreen.DeviceName != currentScreen.DeviceName && currentScreenMode != ScreenMode.Windowed)
            {
                SetScreenMode(currentScreenMode);
                //reset Window position to center on new screen
                windowPosition = new Point(
                    currentScreen.WorkingArea.Left + (currentScreen.WorkingArea.Size.Width - windowSize.Width) / 2,
                    currentScreen.WorkingArea.Top + (currentScreen.WorkingArea.Size.Height - windowSize.Height) / 2);
            }
        }

        private void WindowForm_LocationChanged(object sender, EventArgs e)
        {
            WindowForm_ClientSizeChanged(sender, e);
        }

        internal void UpdateColorPreference(ColorSetting setting, string colorName)
        {
            colorPreferences[setting] = colorName;
            contentArea?.UpdateColor(setting, ColorExtension.FromName(colorName));
            if (setting == ColorSetting.Background)
            {
                backgroundColor = colorName;
                BackgroundColor = ColorExtension.FromName(colorName);
            }
        }

        internal void UpdateItemVisibilityPreference(TrackViewerViewSettings setting, bool enabled)
        {
            viewSettings = enabled ? viewSettings | setting : viewSettings & ~setting;
            contentArea?.UpdateItemVisiblity(viewSettings);
        }

        internal void UpdateLanguagePreference(string language)
        {
            Settings.UserSettings.Language = language;
            LoadLanguage();
        }

        private void LoadSettings()
        {
            windowSize = new System.Drawing.Size(Settings.WindowSize[0], Settings.WindowSize[1]);

            colorPreferences[ColorSetting.Background] = Settings.ColorBackground;
            colorPreferences[ColorSetting.RailTrack] = Settings.ColorRailTrack;
            colorPreferences[ColorSetting.RailTrackEnd] = Settings.ColorRailTrackEnd;
            colorPreferences[ColorSetting.RailTrackJunction] = Settings.ColorRailTrackJunction;
            colorPreferences[ColorSetting.RailTrackCrossing] = Settings.ColorRailTrackCrossing;
            colorPreferences[ColorSetting.RailLevelCrossing] = Settings.ColorRailLevelCrossing;
            colorPreferences[ColorSetting.RoadTrack] = Settings.ColorRoadTrack;
            colorPreferences[ColorSetting.RoadTrackEnd] = Settings.ColorRoadTrackEnd;
            colorPreferences[ColorSetting.RoadLevelCrossing] = Settings.ColorRoadLevelCrossing;
            colorPreferences[ColorSetting.RoadCarSpawner] = Settings.ColorRoadCarSpawner;
            colorPreferences[ColorSetting.SignalItem] = Settings.ColorSignalItem;
            colorPreferences[ColorSetting.PlatformItem] = Settings.ColorPlatformItem;
            colorPreferences[ColorSetting.SidingItem] = Settings.ColorSidingItem;
            colorPreferences[ColorSetting.SpeedPostItem] = Settings.ColorSpeedpostItem;
            colorPreferences[ColorSetting.HazardItem] = Settings.ColorHazardItem;
            colorPreferences[ColorSetting.PickupItem] = Settings.ColorPickupItem;
            colorPreferences[ColorSetting.SoundRegionItem] = Settings.ColorSoundRegionItem;
            BackgroundColor = ColorExtension.FromName(colorPreferences[ColorSetting.Background]);
            viewSettings = Settings.ViewSettings;

        }

        private void SaveSettings()
        {
            Settings.WindowSize[0] = windowSize.Width;
            Settings.WindowSize[1] = windowSize.Height;

            Settings.ColorBackground = colorPreferences[ColorSetting.Background];
            Settings.ColorRailTrack = colorPreferences[ColorSetting.RailTrack];
            Settings.ColorRailTrackEnd = colorPreferences[ColorSetting.RailTrackEnd];
            Settings.ColorRailTrackJunction = colorPreferences[ColorSetting.RailTrackJunction];
            Settings.ColorRailTrackCrossing = colorPreferences[ColorSetting.RailTrackCrossing];
            Settings.ColorRailLevelCrossing = colorPreferences[ColorSetting.RailLevelCrossing];
            Settings.ColorRoadTrack = colorPreferences[ColorSetting.RoadTrack];
            Settings.ColorRoadTrackEnd = colorPreferences[ColorSetting.RoadTrackEnd];
            Settings.ColorRoadLevelCrossing = colorPreferences[ColorSetting.RoadLevelCrossing];
            Settings.ColorRoadCarSpawner = colorPreferences[ColorSetting.RoadCarSpawner];
            Settings.ColorSignalItem = colorPreferences[ColorSetting.SignalItem];
            Settings.ColorPlatformItem = colorPreferences[ColorSetting.PlatformItem];
            Settings.ColorSidingItem = colorPreferences[ColorSetting.SidingItem];
            Settings.ColorSpeedpostItem = colorPreferences[ColorSetting.SpeedPostItem];
            Settings.ColorHazardItem = colorPreferences[ColorSetting.HazardItem];
            Settings.ColorPickupItem = colorPreferences[ColorSetting.PickupItem];
            Settings.ColorSoundRegionItem = colorPreferences[ColorSetting.SoundRegionItem];
            Settings.ViewSettings = viewSettings;
            if (null != contentArea)
            {
                string[] location = new string[] { $"{contentArea.CenterX}", $"{contentArea.CenterY}", $"{contentArea.Scale}" };
                Settings.LastLocation = location;
                    }
            string[] routeSelection = null;
            if (selectedFolder != null)
            {
                if (selectedRoute != null)
                    routeSelection = new string[] { selectedFolder.Name, selectedRoute.Name };
                else
                    routeSelection = new string[] { selectedFolder.Name };
            }
            Settings.RouteSelection = routeSelection;
            Settings.Save();
        }

        private void LoadLanguage()
        {
            Localizer.Revert(windowForm, store);
            CatalogManager.Reset();

            if (!string.IsNullOrEmpty(Settings.UserSettings.Language))
            {
                try
                {
                    CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(Settings.UserSettings.Language);
                }
                catch (CultureNotFoundException exception)
                {
                    System.Diagnostics.Trace.WriteLine(exception.Message);
                }
            }
            else
            {
                CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InstalledUICulture;
            }
            Catalog = CatalogManager.Catalog;
            Localizer.Localize(windowForm, Catalog, store);
        }

        private void GraphicsPreparingDeviceSettings(object sender, PreparingDeviceSettingsEventArgs e)
        {
            e.GraphicsDeviceInformation.GraphicsProfile = GraphicsProfile.HiDef;
            e.GraphicsDeviceInformation.PresentationParameters.RenderTargetUsage = RenderTargetUsage.DiscardContents;
            e.GraphicsDeviceInformation.PresentationParameters.DepthStencilFormat = DepthFormat.Depth24Stencil8;
            e.GraphicsDeviceInformation.PresentationParameters.MultiSampleCount = Settings.UserSettings.MultisamplingCount;
        }

        private void SetScreenMode(ScreenMode targetMode)
        {
            syncing = true;
            windowForm.Invoke((System.Windows.Forms.MethodInvoker)delegate {
                if (graphicsDeviceManager.IsFullScreen)
                    graphicsDeviceManager.ToggleFullScreen();
                switch (targetMode)
                {
                    case ScreenMode.Windowed:
                        if (targetMode != currentScreenMode)
                            Window.Position = windowPosition;
                        windowForm.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
                        windowForm.Size = windowSize;
                        graphicsDeviceManager.PreferredBackBufferWidth = windowSize.Width;
                        graphicsDeviceManager.PreferredBackBufferHeight = windowSize.Height;
                        graphicsDeviceManager.ApplyChanges();
                        statusbar.UpdateStatusbarVisibility(true);
                        break;
                    case ScreenMode.WindowedFullscreen:
                        graphicsDeviceManager.PreferredBackBufferWidth = currentScreen.WorkingArea.Width - clientRectangleOffset.X;
                        graphicsDeviceManager.PreferredBackBufferHeight = currentScreen.WorkingArea.Height - clientRectangleOffset.Y;
                        windowForm.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
                        Window.Position = new Point(currentScreen.WorkingArea.Location.X, currentScreen.WorkingArea.Location.Y);
                        graphicsDeviceManager.ApplyChanges();
                        statusbar.UpdateStatusbarVisibility(false);
                        break;
                    case ScreenMode.BorderlessFullscreen:
                        graphicsDeviceManager.PreferredBackBufferWidth = currentScreen.Bounds.Width;
                        graphicsDeviceManager.PreferredBackBufferHeight = currentScreen.Bounds.Height;
                        graphicsDeviceManager.ApplyChanges();
                        windowForm.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
                        Window.Position = new Point(currentScreen.Bounds.X, currentScreen.Bounds.Y);
                        graphicsDeviceManager.ApplyChanges();
                        statusbar.UpdateStatusbarVisibility(false);
                        break;
                }
            });
            currentScreenMode = targetMode;
            onClientSizeChanged?.Invoke();
            syncing = false;
        }
        #endregion

        protected override async void Initialize()
        {
            List<Task> initTasks = new List<Task>()
            {
                LoadFolders(),
            };
            InputSettings.Initialize();

            spriteBatch = new SpriteBatch(GraphicsDevice);
            TextDrawShape.Initialize(this, spriteBatch);
            BasicShapes.Initialize(spriteBatch);

            UserCommandController<UserCommand> userCommandController = new UserCommandController<UserCommand>();

            KeyboardInputGameComponent keyboardInputGameComponent = new KeyboardInputGameComponent(this);
            Components.Add(keyboardInputGameComponent);
            KeyboardInputHandler<UserCommand> keyboardInput = new KeyboardInputHandler<UserCommand>();
            keyboardInput.Initialize(InputSettings.UserCommands, keyboardInputGameComponent, userCommandController);

            MouseInputGameComponent mouseInputGameComponent = new MouseInputGameComponent(this);
            Components.Add(mouseInputGameComponent);
            MouseInputHandler<UserCommand> mouseInput = new MouseInputHandler<UserCommand>();
            mouseInput.Initialize(mouseInputGameComponent, keyboardInputGameComponent, userCommandController);

            userCommandController.AddEvent(UserCommand.PrintScreen, KeyEventType.KeyPressed, PrintScreen);
            userCommandController.AddEvent(UserCommand.ChangeScreenMode, KeyEventType.KeyPressed, ChangeScreenMode);
            userCommandController.AddEvent(UserCommand.QuitGame, KeyEventType.KeyPressed, CloseWindow);
            userCommandController.AddEvent(UserCommand.MoveLeft, KeyEventType.KeyDown, MoveByKeyLeft);
            userCommandController.AddEvent(UserCommand.MoveRight, KeyEventType.KeyDown, MoveByKeyRight);
            userCommandController.AddEvent(UserCommand.MoveUp, KeyEventType.KeyDown, MoveByKeyUp);
            userCommandController.AddEvent(UserCommand.MoveDown, KeyEventType.KeyDown, MoveByKeyDown);
            userCommandController.AddEvent(UserCommand.NewInstance,KeyEventType.KeyPressed, () => new Thread(GameWindowThread).Start());
            userCommandController.AddEvent(UserCommand.ZoomIn, KeyEventType.KeyDown, ZoomIn);
            userCommandController.AddEvent(UserCommand.ZoomOut, KeyEventType.KeyDown, ZoomOut);
            userCommandController.AddEvent(UserCommand.ResetZoomAndLocation, KeyEventType.KeyPressed, ResetZoomAndLocation);

            userCommandController.AddEvent(CommonUserCommand.PointerDragged, MouseDragging);
            userCommandController.AddEvent(CommonUserCommand.VerticalScrollChanged, MouseWheel);

            base.Initialize();

            await Task.WhenAll(initTasks).ConfigureAwait(false);
            await PreSelectRoute(Settings.RouteSelection).ConfigureAwait(false);
            ContentArea?.PresetPosition(Settings.LastLocation);

            window = new TestWindow();

        }
        private static void GameWindowThread(object data)
        {
            using (GameWindow game = new GameWindow())
                game.Run();
        }

        protected override void LoadContent()
        {
            // Create a new SpriteBatch, which can be used to draw textures.
            BasicShapes.LoadContent(GraphicsDevice);
            DigitalClockComponent clock = new DigitalClockComponent(this, TimeType.RealWorldLocalTime,
                new System.Drawing.Font("Segoe UI", 14, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel), Color.White, new Vector2(-200, -100), true);
            Components.Add(clock);
            ScaleRulerComponent scaleRuler = new ScaleRulerComponent(this, new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, 14, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel), Color.Black, new Vector2(-20, -55));
            Components.Add(scaleRuler);
            Components.Add(new InsetComponent(this, BackgroundColor, new Vector2(-10, 30)));
            Components.Add(new WorldCoordinatesComponent(this, new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, 20, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel), Color.Blue, new Vector2(40, 40)));
        }

        protected override void Update(GameTime gameTime)
        {
            // TODO: Add your update logic here
            base.Update(gameTime);
        }

        private System.Drawing.Font drawfont = new System.Drawing.Font("Segoe UI", (int)Math.Round(25.0), System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
        private int drawTime;

        public bool InputCaptured { get; internal set; }

        public TestWindow window;

        protected override void Draw(GameTime gameTime)
        {
            double elapsedRealTime = gameTime.ElapsedGameTime.TotalSeconds;
            frameRate.Update(elapsedRealTime, 1.0 / elapsedRealTime);
            if ((int)gameTime.TotalGameTime.TotalSeconds > drawTime)
            {
                drawTime = (int)gameTime.TotalGameTime.TotalSeconds;
                statusbar.toolStripStatusLabel1.Text = $"{1 / gameTime.ElapsedGameTime.TotalSeconds:0.0} - {frameRate.SmoothedValue:0.0}";

            }
            GraphicsDevice.Clear(BackgroundColor);
            statusbar.toolStripStatusLabel2.Text = contentArea?.Scale.ToString() ?? string.Empty;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, null, null, null);
            //if (contentArea == null)
            //{
            //    BasicShapes.DrawTexture(BasicTextureType.Pickup, new Vector2(180, 180), 0, -1, Color.Green, false, false, true);
            //    BasicShapes.DrawTexture(BasicTextureType.PlayerTrain, new Vector2(240, 180), 0, -3, Color.Blue, true, false, true);
            //    BasicShapes.DrawTexture(BasicTextureType.Ring, new Vector2(80, 220), 0, -0.5f, Color.Yellow, true, false, false);
            //    BasicShapes.DrawTexture(BasicTextureType.Circle, new Vector2(80, 220), 0, -0.2f, Color.Red, true, false, false);
            //    BasicShapes.DrawTexture(BasicTextureType.RingCrossed, new Vector2(240, 220), 0.0f, -2, Color.Yellow, true, false, false);
            //    BasicShapes.DrawTexture(BasicTextureType.Disc, new Vector2(340, 220), 0, -1, Color.Red, true, false, false);

            //    BasicShapes.DrawArc(3, Color.Green, new Vector2(330, 330), 120, 90 * Math.PI / 180, 90, 0);
            //    BasicShapes.DrawDashedLine(2, Color.Aqua, new Vector2(330, 330), new Vector2(450, 330));
            //    TextDrawShape.DrawString(new Vector2(200, 450), Color.Red, "Test Message", drawfont, Vector2.One);
            //    TextDrawShape.DrawString(new Vector2(200, 500), Color.Lime, gameTime.TotalGameTime.TotalSeconds.ToString(), drawfont, Vector2.One);

            //    BasicShapes.DrawTexture(BasicTextureType.Disc, new Vector2(480, 180), 0, -2f, Color.Green, false, false, false);
            //    BasicShapes.DrawTexture(BasicTextureType.Disc, new Vector2(640, 180), 0, -2f, Color.Green, true, false, true);

            //    BasicShapes.DrawArc(5, Color.IndianRed, new Vector2(240, 220), 120, Math.PI, -270, 0);
            //    BasicShapes.DrawLine(10, Color.DarkGoldenrod, new Vector2(100, 100), new Vector2(250, 250));
            //}
            if (!string.IsNullOrEmpty(StatusMessage))
                TextDrawShape.DrawString(centerPoint, Color.Red, StatusMessage, drawfont, Vector2.One, TextHorizontalAlignment.Center);

            spriteBatch.End();
            window?.Draw();
            base.Draw(gameTime);
        }
    }
}
