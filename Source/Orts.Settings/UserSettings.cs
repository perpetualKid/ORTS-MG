// COPYRIGHT 2012, 2013 by the Open Rails project.
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

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Orts.Common;
using Orts.Settings.Store;

namespace Orts.Settings
{

    public class UserSettings : SettingsBase
    {
        public static string UserDataFolder { get; private set; }     // ie @"C:\Users\Wayne\AppData\Roaming\Open Rails"
        public static string DeletedSaveFolder { get; private set; }  // ie @"C:\Users\Wayne\AppData\Roaming\Open Rails\Deleted Saves"
        public static string SavePackFolder { get; private set; }     // ie @"C:\Users\Wayne\AppData\Roaming\Open Rails\Save Packs"

        private static readonly StoreType SettingsStoreType;
        private static readonly string Location;

        private readonly Dictionary<string, object> customDefaultValues = new Dictionary<string, object>();

        static UserSettings()
        {
            //TODO: user settings (as any other runtime data) may not be saved in exe folder (which might be under \Program Files if installed via installer)
            // default user settings are searched in order: Json, Ini, Registry
            if (File.Exists(Location = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), EnumExtension.GetDescription(StoreType.Json))))
            {
                SettingsStoreType = StoreType.Json;
            }
            if (File.Exists(Location = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), EnumExtension.GetDescription(StoreType.Ini))))
            {
                SettingsStoreType = StoreType.Ini;
            }
            else
            {
                SettingsStoreType = StoreType.Registry;
                Location = EnumExtension.GetDescription(StoreType.Registry);
            }

            UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Application.ProductName);

            Directory.CreateDirectory(UserDataFolder);
            DeletedSaveFolder = Path.Combine(UserDataFolder, "Deleted Saves");
            SavePackFolder = Path.Combine(UserDataFolder, "Save Packs");
        }

        #region User Settings

        // Please put all user settings in here as auto-properties. Public properties
        // of type 'string', 'int', 'bool', 'string[]' and 'int[]' are automatically loaded/saved.

        // Main menu settings:
        [Default(true)]
        public bool Logging { get; set; }
        [Default(false)]
        public bool DebriefActivityEval { get; set; }
        [Default(false)]
        public bool DebriefTTActivityEval { get; set; }
        [Default("")]
        public string Multiplayer_User { get; set; }
        [Default("127.0.0.1")]
        public string Multiplayer_Host { get; set; }
        [Default(30000)]
        public int Multiplayer_Port { get; set; }

        // General settings:
        [Default(false)]
        public bool Alerter { get; set; }
        [Default(true)]
        public bool AlerterDisableExternal { get; set; }
        [Default(true)]
        public bool SpeedControl { get; set; }
        [Default(false)]
        public bool ViewDispatcher { get; set; }
        [Default(false)]
        public bool GraduatedRelease { get; set; }
        [Default(false)]
        public bool RetainersOnAllCars { get; set; }
        [Default(false)]
        public bool SuppressConfirmations { get; set; }
        [Default(21)]
        public int BrakePipeChargingRate { get; set; }
        [Default("")]
        public string Language { get; set; }
        [Default("Automatic")]
        public string PressureUnit { get; set; }
        [Default("Automatic")]
        public string Units { get; set; }
        [Default(false)]
        public bool DisableTCSScripts { get; set; }

        // Audio settings:
        [Default(true)]
        public bool MSTSBINSound { get; set; }
        [Default(100)]
        public int SoundVolumePercent { get; set; }
        [Default(5)]
        public int SoundDetailLevel { get; set; }
        [Default(50)]
        public int ExternalSoundPassThruPercent { get; set; } // higher = louder sound

        // Video settings:
        [Default(true)]
        public bool FullScreen { get; set; }
        [Default(true)]
        public bool NativeFullscreenResolution { get; set; }
        [Default(false)]
        public bool DynamicShadows { get; set; }
        [Default(false)]
        public bool ShadowAllShapes { get; set; }
        [Default(false)]
        public bool FastFullScreenAltTab { get; set; }
        [Default(false)]
        public bool WindowGlass { get; set; }
        [Default(false)]
        public bool ModelInstancing { get; set; }
        [Default(true)]
        public bool Wire { get; set; }
        [Default(false)]
        public bool VerticalSync { get; set; }
        [Default(true)]
        public bool EnableMultisampling { get; set; }
        [Default(4)]
        public int MultisamplingCount { get; set; }
        [Default(0)]
        public int Cab2DStretch { get; set; }
        [Default(2000)]
        public int ViewingDistance { get; set; }
        [Default(true)]
        public bool DistantMountains { get; set; }
        [Default(40000)]
        public int DistantMountainsViewingDistance { get; set; }
        [Default(45)] // MSTS uses 60 FOV horizontally, on 4:3 displays this is 45 FOV vertically (what OR uses).
        public int ViewingFOV { get; set; }
        [Default(10)]
        public int WorldObjectDensity { get; set; }
        [Default("1024x768")]
        public string WindowSize { get; set; }
        [Default(20)]
        public int DayAmbientLight { get; set; }

        // Simulation settings:
        [Default(true)]
        public bool UseAdvancedAdhesion { get; set; }
        [Default(10)]
        public int AdhesionMovingAverageFilterSize { get; set; }
        [Default(false)]
        public bool BreakCouplers { get; set; }
        [Default(false)]
        public bool CurveResistanceDependent { get; set; }
        [Default(false)]
        public bool CurveSpeedDependent { get; set; }
        [Default(false)]
        public bool TunnelResistanceDependent { get; set; }
        [Default(false)]
        public bool WindResistanceDependent { get; set; }
        [Default(false)]
        public bool OverrideNonElectrifiedRoutes { get; set; }
        [Default(true)]
        public bool HotStart { get; set; }
        [Default(false)]
        public bool Autopilot { get; set; }

        // Data logger settings:
        [Default("comma")]
        public string DataLoggerSeparator { set; get; }
        [Default("route")]
        public string DataLogSpeedUnits { get; set; }
        [Default(false)]
        public bool DataLogStart { get; set; }
        [Default(true)]
        public bool DataLogPerformance { get; set; }
        [Default(false)]
        public bool DataLogPhysics { get; set; }
        [Default(false)]
        public bool DataLogMisc { get; set; }
        [Default(false)]
        public bool DataLogSteamPerformance { get; set; }
        
        
        // Evaluation settings:
        [Default(false)]
        public bool DataLogTrainSpeed { get; set; }
        [Default(10)]
        public int DataLogTSInterval { get; set; }
        //Time, Train Speed, Max Speed, Signal Aspect, Elevation, Direction, Distance Travelled, Control Mode, Throttle, Brake, Dyn Brake, Gear
        [Default(new[] { 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
        public int[] DataLogTSContents { get; set; }
        [Default(false)]
        public bool DataLogStationStops { get; set; }


        // Timetable settings:
        [Default(true)]
        public bool TTUseRestartDelays { get; set; }
        [Default(true)]
        public bool TTCreateTrainOnPoolUnderflow { get; set; }
        [Default(false)]
        public bool TTOutputTimetableTrainInfo { get; set; }
        [Default(false)]
        public bool TTOutputTimetableFullEvaluation { get; set; }

        // Updater settings are saved only in "Updater.ini".

        // Experimental settings:
        [Default(0)]
        public int UseSuperElevation { get; set; }
        [Default(50)]
        public int SuperElevationMinLen { get; set; }
        [Default(1435)]
        public int SuperElevationGauge { get; set; }
        [Default(0)]
        public int LODBias { get; set; }
        [Default(false)]
        public bool PerformanceTuner { get; set; }
        [Default(true)]
        public bool SuppressShapeWarnings { get; set; }
        [Default(60)]
        public int PerformanceTunerTarget { get; set; }
        [Default(false)]
        public bool DoubleWire { get; set; }
        [Default(false)]
        public bool AuxActionEnabled { get; set; }
        [Default(false)]
        public bool CircularSpeedGauge { get; set; }
        [Default(false)]
        public bool LODViewingExtention { get; set; }
        [Default(false)]
        public bool PreferDDSTexture { get; set; }
        [Default(false)]
        public bool UseLocationPassingPaths { get; set; }
        [Default(false)]
        public bool UseMSTSEnv { get; set; }
        [Default(false)]
        public bool SignalLightGlow { get; set; }
        [Default(130)]
        public int AdhesionFactor { get; set; }
        [Default(10)]
        public int AdhesionFactorChange { get; set; }
        [Default(false)]
        public bool AdhesionProportionalToWeather { get; set; }
        [Default(false)]
        public bool NoForcedRedAtStationStops { get; set; }
        [Default(false)]
        public bool ConditionalLoadOfDayOrNightTextures { get; set; }
        [Default(false)]
        public bool ExtendedAIShunting { get; set; }
        [Default(100)]
        public int PrecipitationBoxHeight { get; set; }
        [Default(500)]
        public int PrecipitationBoxWidth { get; set; }
        [Default(500)]
        public int PrecipitationBoxLength { get; set; }
        [Default(false)]
        public bool CorrectQuestionableBrakingParams { get; set; }
        [Default(false)]
        public bool OpenDoorsInAITrains { get; set; }
        [Default(0)]
        public int ActRandomizationLevel { get; set; }
        [Default(0)]
        public int ActWeatherRandomizationLevel { get; set; }

        // Hidden settings:
        [Default(0)]
        public int CarVibratingLevel { get; set; }
        [Default("OpenRailsLog.txt")]
        public string LoggingFilename { get; set; }
        [Default("OR-DebriefEval.txt")]
        public string DebriefEvalFilename { get; set; }//
        [Default("")] // If left as "", OR will use the user's desktop folder
        public string LoggingPath { get; set; }
        [Default("")]
        public string ScreenshotPath { get; set; }
        [Default(true)]
        public bool ShadowMapBlur { get; set; }
        [Default(4)]
        public int ShadowMapCount { get; set; }
        [Default(0)]
        public int ShadowMapDistance { get; set; }
        [Default(1024)]
        public int ShadowMapResolution { get; set; }
        [Default(10)]
        public int Multiplayer_UpdateInterval { get; set; }
        [Default("http://openrails.org/images/support-logos.jpg")]
        public string AvatarURL { get; set; }
        [Default(false)]
        public bool ShowAvatar { get; set; }
        [Default(0)] // Do not offer to restore/resume any saves this age or older. Updated whenever a younger save fails to restore.
        public int YoungestFailedToRestore { get; set; }

        // Internal settings:
        [Default(false)]
        public bool DataLogger { get; set; }
        [Default(false)]
        public bool Profiling { get; set; }
        [Default(0)]
        public int ProfilingFrameCount { get; set; }
        [Default(0)]
        public int ProfilingTime { get; set; }
        [Default(0)]
        public int ReplayPauseBeforeEndS { get; set; }
        [Default(true)]
        public bool ReplayPauseBeforeEnd { get; set; }
        [Default(true)]
        public bool ShowErrorDialogs { get; set; }
        [Default(new string[0])]
        public string[] Menu_Selection { get; set; }
        [Default(false)]
        public bool Multiplayer { get; set; }
        [Default(new[] { 50, 50 })]
        public int[] WindowPosition_Activity { get; set; }
        [Default(new[] { 50, 0 })]
        public int[] WindowPosition_Compass { get; set; }
        [Default(new[] { 100, 100 })]
        public int[] WindowPosition_DriverAid { get; set; }
        [Default(new[] { 50, 50 })]
        public int[] WindowPosition_Help { get; set; }
        [Default(new[] { 75, 0 })]
        public int[] WindowPosition_HUDScroll { get; set; }
        [Default(new[] { 0, 100 })]
        public int[] WindowPosition_NextStation { get; set; }
        [Default(new[] { 50, 50 })]
        public int[] WindowPosition_Quit { get; set; }
        [Default(new[] { 0, 50 })]
        public int[] WindowPosition_Switch { get; set; }
        [Default(new[] { 100, 0 })]
        public int[] WindowPosition_TrackMonitor { get; set; }
        [Default(new[] { 50, 50 })]
        public int[] WindowPosition_TrainOperations { get; set; }
        [Default(new[] { 50, 50 })]
        public int[] WindowPosition_CarOperations { get; set; }
        [Default(new[] { 50, 50 })]
        public int[] WindowPosition_ComposeMessage { get; set; }
        [Default(new[] { 100, 0 })]
        public int[] WindowPosition_TrainList { get; set; }
        [Default(new[] { 300, 200 })]
        public int[] WindowSize_DispatcherViewer { get; set; }

        // Menu-game communication settings:
        [Default(false)]
        [DoNotSave]
        public bool MultiplayerClient { get; set; }
        [Default(false)]
        [DoNotSave]
        public bool MultiplayerServer { get; set; }

        #endregion

        public FolderSettings Folders { get; private set; }

        public InputSettings Input { get; private set; }

        public RailDriverSettings RailDriver { get; private set; }

        public UserSettings(IEnumerable<string> options) : 
            this(options, SettingsStore.GetSettingsStore(SettingsStoreType, Location, null))
        {
        }

        public UserSettings(IEnumerable<string> options, SettingsStore store) :
            base(store)
        {
            customDefaultValues["LoggingPath"] = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            customDefaultValues["ScreenshotPath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Application.ProductName);
            customDefaultValues["Multiplayer_User"] = Environment.UserName;
            LoadSettings(options);
            Folders = new FolderSettings(options, store);
            Input = new InputSettings(options, store);
            RailDriver = new RailDriverSettings(options, store);
        }

        public override object GetDefaultValue(string name)
        {
            var property = GetProperty(name);

            if (customDefaultValues.ContainsKey(property.Name))
                return customDefaultValues[property.Name];

            if (property.GetCustomAttributes(typeof(DefaultAttribute), false).Length > 0)
                return (property.GetCustomAttributes(typeof(DefaultAttribute), false)[0] as DefaultAttribute).Value;

            throw new InvalidDataException(String.Format("UserSetting {0} has no default value.", property.Name));
        }

        protected override PropertyInfo[] GetProperties()
        {
            if (properties == null)
                properties = base.GetProperties().Where(pi => !new string[] { "Folders", "Input", "RailDriver" }.Contains(pi.Name)).ToArray();
            return properties;
        }

        protected override object GetValue(string name)
        {
            return GetProperty(name).GetValue(this, null);
        }

        protected override void SetValue(string name, object value)
        {
            GetProperty(name).SetValue(this, value, null);
        }

        protected override void Load(bool allowUserSettings, NameValueCollection optionalValues)
        {
            foreach (var property in GetProperties())
                LoadSetting(allowUserSettings, optionalValues, property.Name);
            properties = null;
        }

        public override void Save()
        {
            foreach (var property in GetProperties())
                Save(property.Name);

            Folders.Save();
            Input.Save();
            RailDriver.Save();
            properties = null;
        }

        public override void Save(string name)
        {
            if (AllowPropertySaving(name))
            {
                SaveSetting(name);
            }
        }

        public override void Reset()
        {
            foreach (var property in GetProperties())
                Reset(property.Name);
        }

        public void Log()
        {
            foreach (var property in GetProperties().OrderBy(p => p.Name))
            {
                dynamic value = property.GetValue(this, null);
                string source = string.Empty;

                if (property.PropertyType == typeof(int[]))  //int array
                {
                    source = (optionalSettings.Contains(property.Name.ToLowerInvariant()) ? "(command-line)" :
                        (((value as int[]).SequenceEqual(GetDefaultValue(property.Name) as int[]))) ? "" : "(user set)");
                    value = string.Join(", ", (int[])value);
                }
                else if (property.PropertyType == typeof(string[]))  //string array
                {
                    source = (optionalSettings.Contains(property.Name.ToLowerInvariant()) ? "(command-line)" :
                        (((value as string[]).SequenceEqual(GetDefaultValue(property.Name) as string[]))) ? "" : "(user set)");
                    value = string.Join(", ", (string[])value);
                }
                else
                {
                    source = (optionalSettings.Contains(property.Name.ToLowerInvariant()) ? "(command-line)" :
                        (value.Equals(GetDefaultValue(property.Name)) ? "" : "(user set)"));
                }

                Console.WriteLine("{0,-30} = {2,-14} {1}", property.Name, value, source);
            }

            properties = null;
        }
    }
}
