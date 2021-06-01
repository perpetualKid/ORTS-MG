// COPYRIGHT 2009, 2010, 2011, 2012, 2013 by the Open Rails project.
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
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

using GetText;

using Microsoft.Xna.Framework;

using Orts.Common;
using Orts.Common.Calc;
using Orts.Common.Info;
using Orts.Common.Xna;
using Orts.Formats.Msts;
using Orts.Formats.Msts.Files;
using Orts.Formats.Msts.Models;
using Orts.Formats.OR.Files;
using Orts.Formats.OR.Models;
using Orts.MultiPlayer;
using Orts.Scripting.Api;
using Orts.Settings;
using Orts.Simulation.AIs;
using Orts.Simulation.Commanding;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.Signalling;
using Orts.Simulation.Timetables;
using Orts.Simulation.Track;

namespace Orts.Simulation
{
    /// <summary>
    /// This contains all the essential code to operate trains along paths as defined
    /// in the activity.   It is meant to operate in a separate thread it handles the
    /// following:
    ///    track paths
    ///    switch track positions
    ///    signal indications
    ///    calculating positions and velocities of trains
    ///    
    /// Update is called regularly to
    ///     do physics calculations for train movement
    ///     compute new signal indications
    ///     operate ai trains
    ///     
    /// All keyboard input comes from the viewer class as calls on simulator's methods.
    /// </summary>
    public class Simulator
    {
        public static ICatalog Catalog { get; private set; }

        public static Simulator Instance { get; private set; }

        public static Random Random { get; private set; }
        public static double Resolution = 1000000; // resolution for calculation of random value with a pseudo-gaussian distribution
        public const float MaxStoppedMpS = 0.1f; // stopped is taken to be a speed less than this 

        public bool Paused = true;          // start off paused, set to true once the viewer is fully loaded and initialized
        public float GameSpeed = 1;
        /// <summary>
        /// Monotonically increasing time value (in seconds) for the simulation. Starts at 0 and only ever increases, at <see cref="GameSpeed"/>.
        /// Does not change if game is <see cref="Paused"/>.
        /// </summary>
        public double GameTime;
        /// <summary>
        /// "Time of day" clock value (in seconds) for the simulation. Starts at activity start time and may increase, at <see cref="GameSpeed"/>,
        /// or jump forwards or jump backwards.
        /// </summary>
        public double ClockTime;
        // while Simulator.Update() is running, objects are adjusted to this target time 
        // after Simulator.Update() is complete, the simulator state matches this time

        public UserSettings Settings { get; }

        public FolderStructure.ContentFolder.RouteFolder RouteFolder { get; }

        public string BasePath;     // ie c:\program files\microsoft games\train simulator
        public string RoutePath;    // ie c:\program files\microsoft games\train simulator\routes\usa1  - may be different on different pc's

        // Primary Simulator Data 
        // These items represent the current state of the simulator 
        // In multiplayer games, these items must be kept in sync across all players
        // These items are what are saved and loaded in a game save.
        public string RoutePathName;    // ie LPS, USA1  represents the folder name
        public string RouteName;
        public string ActivityFileName;
        public string TimetableFileName;
        public bool TimetableMode;
        public ActivityFile Activity;
        public Activity ActivityRun;
        public TrackDatabaseFile TDB;
        public RouteFile TRK;
        public TrackSectionsFile TSectionDat;
        public TrainList Trains;
        public Dictionary<int, Physics.Train> TrainDictionary = new Dictionary<int, Physics.Train>();
        public Dictionary<string, Physics.Train> NameDictionary = new Dictionary<string, Physics.Train>();
        public Dictionary<int, AITrain> AutoGenDictionary = new Dictionary<int, AITrain>();
        public List<int> StartReference = new List<int>();
        public Weather Weather = new Weather();

        public float CurveDurability;  // Sets the durability due to curve speeds in TrainCars - read from consist file.

        public static int DbfEvalOverSpeedCoupling;//Debrief eval

        public SignalEnvironment SignalEnvironment { get; private set; }
        public AI AI;
        public SeasonType Season;
        public WeatherType WeatherType;
        public string UserWeatherFile = string.Empty;
        public SignalConfigurationFile SIGCFG;
        public string ExplorePathFile;
        public string ExploreConFile;
        public string patFileName;
        public string conFileName;
        public AIPath PlayerPath;
        public LevelCrossings LevelCrossings;
        public RoadDatabaseFile RDB;
        public bool UseAdvancedAdhesion;
        public bool BreakCouplers;
        public int DayAmbientLight;
        public int CarVibrating;
        public int UseSuperElevation; //amount of superelevation
        public SuperElevation SuperElevation;
        public int SuperElevationMinLen = 50;
        public float SuperElevationGauge = 1.435f;//1.435 guage

        // Used in save and restore form
        public string PathName = "<unknown>";
        private string timeTableFile;
        public float InitialTileX;
        public float InitialTileZ;
        public HazardManager HazzardManager;
        public FuelManager FuelManager;
        public bool InControl = true;//For multiplayer, a player may not control his/her own train (as helper)
        public TurntableFile TurntableFile;
        public List<MovingTable> MovingTables = new List<MovingTable>();
        public List<CarSpawnerList> CarSpawnerLists;
        public ClockList Clocks;           // List of OR-Clocks given by externe file "openrails\clocks.dat"

        // timetable pools
        public Poolholder PoolHolder;

        // player locomotive
        public TrainCar PlayerLocomotive;    // Set by the Viewer - TODO there could be more than one player so eliminate this.

        // <CJComment> Works but not entirely happy about this arrangement. 
        // Confirmer should be part of the Viewer, rather than the Simulator, as it is part of the user interface.
        // Perhaps an Observer design pattern would be better, so the Simulator sends messages to any observers. </CJComment>
        public Confirmer Confirmer;                 // Set by the Viewer
        public TrainEvent SoundNotify = TrainEvent.None;
        public ScriptManager ScriptManager;

        public bool IsAutopilotMode = false;

        public bool soundProcessWorking = false;
        public bool updaterWorking = false;
        public Physics.Train selectedAsPlayer = null;
        public Physics.Train OriginalPlayerTrain = null; // Used in Activity mode
        public bool playerSwitchOngoing = false;

        public bool PlayerIsInCab = false;
        public readonly bool MilepostUnitsMetric;
        public bool OpenDoorsInAITrains;

        public int ActiveMovingTableIndex = -1;
        public MovingTable ActiveMovingTable
        {
            get
            {
                return ActiveMovingTableIndex >= 0 && ActiveMovingTableIndex < MovingTables.Count ? MovingTables[ActiveMovingTableIndex] : null;
            }
            set
            {
                ActiveMovingTableIndex = -1;
                if (MovingTables.Count < 1) return;
                for (int i = 0; i < MovingTables.Count; i++)
                    if (value == MovingTables[i])
                    {
                        ActiveMovingTableIndex = i;
                    }
            }
        }


        // Replay functionality!
        public CommandLog Log { get; set; }
        public List<ICommand> ReplayCommandList { get; set; }

        /// <summary>
        /// True if a replay is in progress.
        /// Used to show some confirmations which are only valuable during replay (e.g. uncouple or resume activity).
        /// Also used to show the replay countdown in the HUD.
        /// </summary>
        public bool IsReplaying
        {
            get
            {
                if (ReplayCommandList != null)
                {
                    return (ReplayCommandList.Count > 0);
                }
                return false;
            }
        }

        public class TrainSwitcherData
        {
            public Physics.Train PickedTrainFromList;
            public bool ClickedTrainFromList;
            public Physics.Train SelectedAsPlayer;
            public bool ClickedSelectedAsPlayer;
            public bool SuspendOldPlayer;
        }

        public readonly TrainSwitcherData TrainSwitcher = new TrainSwitcherData();

        public class PlayerTrainChangedEventArgs : EventArgs
        {
            public readonly Physics.Train OldTrain;
            public readonly Physics.Train NewTrain;

            public PlayerTrainChangedEventArgs(Physics.Train oldTrain, Physics.Train newTrain)
            {
                OldTrain = oldTrain;
                NewTrain = newTrain;
            }
        }

        public class QueryCarViewerLoadedEventArgs : EventArgs
        {
            public readonly TrainCar Car;
            public bool Loaded;

            public QueryCarViewerLoadedEventArgs(TrainCar car)
            {
                Car = car;
            }
        }

        public event System.EventHandler WeatherChanged;
        public event System.EventHandler AllowedSpeedRaised;
        public event System.EventHandler PlayerLocomotiveChanged;
        public event System.EventHandler<PlayerTrainChangedEventArgs> PlayerTrainChanged;
        public event System.EventHandler<QueryCarViewerLoadedEventArgs> QueryCarViewerLoaded;
        public event System.EventHandler RequestTTDetachWindow;

        public Simulator(UserSettings settings, string activityPath, bool useOpenRailsDirectory)
        {
            Instance = this;
            CatalogManager.SetCatalogDomainPattern(CatalogDomainPattern.AssemblyName, null, RuntimeInfo.LocalesFolder);
            Catalog = CatalogManager.Catalog;

            Random = new Random();

            MPManager.Simulator = this;

            TimetableMode = false;

            Settings = settings;
            UseAdvancedAdhesion = Settings.UseAdvancedAdhesion;
            BreakCouplers = Settings.BreakCouplers;
            CarVibrating = Settings.CarVibratingLevel; //0 no vib, 1-2 mid vib, 3 max vib
            UseSuperElevation = Settings.UseSuperElevation;
            SuperElevationMinLen = Settings.SuperElevationMinLen;
            SuperElevationGauge = (float)Settings.SuperElevationGauge / 1000f;//gauge transfer from mm to m
            RoutePath = Path.GetDirectoryName(Path.GetDirectoryName(activityPath));
            if (useOpenRailsDirectory) RoutePath = Path.GetDirectoryName(RoutePath); // starting one level deeper!
            RoutePathName = Path.GetFileName(RoutePath);
            BasePath = Path.GetDirectoryName(Path.GetDirectoryName(RoutePath));
            RouteFolder = FolderStructure.RouteFromActivity(activityPath);
            DayAmbientLight = (int)Settings.DayAmbientLight;


            string ORfilepath = System.IO.Path.Combine(RoutePath, "OpenRails");

            Trace.Write("Loading ");

            Trace.Write(" TRK");
            TRK = new RouteFile(FolderStructure.Route(RoutePath).TrackFileName);
            RouteName = TRK.Route.Name;
            MilepostUnitsMetric = TRK.Route.MilepostUnitsMetric;
            OpenDoorsInAITrains = TRK.Route.OpenDoorsInAITrains == null ? Settings.OpenDoorsInAITrains : (bool)TRK.Route.OpenDoorsInAITrains;

            Trace.Write(" TDB");
            TDB = new TrackDatabaseFile(RoutePath + @"\" + TRK.Route.FileName + ".tdb");

            if (File.Exists(ORfilepath + @"\sigcfg.dat"))
            {
                Trace.Write(" SIGCFG_OR");
                SIGCFG = new SignalConfigurationFile(ORfilepath + @"\sigcfg.dat", true);
            }
            else
            {
                Trace.Write(" SIGCFG");
                SIGCFG = new SignalConfigurationFile(RoutePath + @"\sigcfg.dat", false);
            }

            Trace.Write(" DAT");
            if (File.Exists(RoutePath + @"\Openrails\TSECTION.DAT"))
            {
                TSectionDat = new TrackSectionsFile(RoutePath + @"\Openrails\TSECTION.DAT");
            }
            else if (File.Exists(RoutePath + @"\GLOBAL\TSECTION.DAT"))
                TSectionDat = new TrackSectionsFile(RoutePath + @"\GLOBAL\TSECTION.DAT");
            else
                TSectionDat = new TrackSectionsFile(BasePath + @"\GLOBAL\TSECTION.DAT");
            if (File.Exists(RoutePath + @"\TSECTION.DAT"))
                TSectionDat.AddRouteTSectionDatFile(RoutePath + @"\TSECTION.DAT");

            SuperElevation = new SuperElevation(this);

            Trace.Write(" ACT");

            var rdbFile = RoutePath + @"\" + TRK.Route.FileName + ".rdb";
            if (File.Exists(rdbFile))
            {
                Trace.Write(" RDB");
                RDB = new RoadDatabaseFile(rdbFile);
            }

            var carSpawnFile = RoutePath + @"\carspawn.dat";
            if (File.Exists(carSpawnFile))
            {
                Trace.Write(" CARSPAWN");
                CarSpawnerLists = new List<CarSpawnerList>();
                CarSpawnerFile csf = new CarSpawnerFile(RoutePath + @"\carspawn.dat", RoutePath + @"\shapes\");
                CarSpawnerLists.Add(csf.CarSpawners);

            }

            // Extended car spawner file
            var extCarSpawnFile = RoutePath + @"\openrails\carspawn.dat";
            if (File.Exists(extCarSpawnFile))
            {
                if (CarSpawnerLists == null)
                    CarSpawnerLists = new List<CarSpawnerList>();
                Trace.Write(" EXTCARSPAWN");
                ORCarSpawnerFile acsf = new ORCarSpawnerFile(RoutePath + @"\openrails\carspawn.dat", RoutePath + @"\shapes\");
                CarSpawnerLists.AddRange(acsf.CarSpawners);
            }

            //Load OR-Clock if external file "openrails\clock.dat" exists --------------------------------------------------------
            var extClockFile = RoutePath + @"\openrails\clocks.dat";
            if (File.Exists(extClockFile))
            {
                Trace.Write(" EXTCLOCK");
                ClockFile cf = new ClockFile(RoutePath + @"\openrails\clocks.dat", RoutePath + @"\shapes\");
                Clocks = cf.Clocks;
            }

            Confirmer = new Confirmer(this, 1.5);
            HazzardManager = new HazardManager(this);
            FuelManager = new FuelManager(this);
            ScriptManager = new ScriptManager();
            Log = new CommandLog(this);
        }

        public void SetActivity(string activityPath)
        {
            ActivityFileName = Path.GetFileNameWithoutExtension(activityPath);
            Activity = new ActivityFile(activityPath);

            // check for existence of activity file in OpenRails subfolder

            activityPath = RoutePath + @"\Activities\Openrails\" + ActivityFileName + ".act";
            if (File.Exists(activityPath))
            {
                ORActivitySettingsFile orActivitySettings = new ORActivitySettingsFile(activityPath);
                OverrideUserSettings(Settings, orActivitySettings.Activity);    // Override user settings for the purposes of this activity
                //TODO override Activity.Activity.AIHornAtCrossings from orActivitySettings
            }

            ActivityRun = new Activity(Activity, this);
            // <CSComment> There can also be an activity without events and without station stops
            //            if (ActivityRun.Current == null && ActivityRun.EventList.Count == 0)
            //                ActivityRun = null;

            ClockTime = Activity.Activity.Header.StartTime.TotalSeconds;
            Season = Activity.Activity.Header.Season;
            WeatherType = Activity.Activity.Header.Weather;
            if (Activity.Activity.ActivityRestrictedSpeedZones != null)
            {
                ActivityRun.AddRestrictZones(TRK.Route, TSectionDat, TDB.TrackDB, Activity.Activity.ActivityRestrictedSpeedZones);
            }
            IsAutopilotMode = true;
        }
        public void SetExplore(string path, string consist, TimeSpan startTime, SeasonType season, WeatherType weather)
        {
            ExplorePathFile = path;
            ExploreConFile = consist;
            patFileName = Path.ChangeExtension(path, "PAT");
            conFileName = Path.ChangeExtension(consist, "CON");
            ClockTime = startTime.TotalSeconds;
            Season = season;
            WeatherType = weather;
        }

        public void SetExploreThroughActivity(string path, string consist, TimeSpan startTime, SeasonType season, WeatherType weather)
        {
            ActivityFileName = "ea$" + RoutePathName + "$" + DateTime.Today.Year.ToString() + DateTime.Today.Month.ToString() + DateTime.Today.Day.ToString() +
                DateTime.Today.Hour.ToString() + DateTime.Today.Minute.ToString() + DateTime.Today.Second.ToString();
            Activity = new ActivityFile((int)startTime.TotalSeconds, Path.GetFileNameWithoutExtension(consist));
            ActivityRun = new Activity(Activity, this);
            ExplorePathFile = path;
            ExploreConFile = consist;
            patFileName = Path.ChangeExtension(path, "PAT");
            conFileName = Path.ChangeExtension(consist, "CON");
            ClockTime = startTime.TotalSeconds;
            Season = season;
            WeatherType = weather;
            IsAutopilotMode = true;
        }

        public void SetTimetableOptions(string timeTableFile, string train, SeasonType season, WeatherType weather, string weatherFile)
        {
            this.timeTableFile = timeTableFile;
            PathName = train;

            Season = season;
            WeatherType = weather;
            // check for user defined weather file
            UserWeatherFile = weatherFile;
        }

        public void Start(CancellationToken cancellationToken)
        {
            SignalEnvironment = new SignalEnvironment(SIGCFG, Settings.UseLocationPassingPaths, cancellationToken);
            TurntableFile = new TurntableFile(RoutePath + @"\openrails\turntables.dat", RoutePath + @"\shapes\", MovingTables, this);
            LevelCrossings = new LevelCrossings(this);
            FuelManager = new FuelManager(this);
            Trains = new TrainList(this);
            PoolHolder = new Poolholder();

            Train playerTrain;

            switch (IsAutopilotMode)
            {
                case true:
                    playerTrain = InitializeAPTrains(cancellationToken);
                    break;
                default:
                    playerTrain = InitializeTrains(cancellationToken);
                    break;
            }
            MPManager.Instance().RememberOriginalSwitchState();

            // start activity logging if required
            if (Settings.EvaluationStationStops && ActivityRun != null)
            {
                string stationLogFile = DeriveLogFile("Stops");
                if (!string.IsNullOrEmpty(stationLogFile))
                {
                    ActivityRun.StartStationLogging(stationLogFile);
                }
            }
        }

        public void StartTimetable(CancellationToken cancellationToken)
        {
            TimetableMode = true;
            SignalEnvironment = new SignalEnvironment(SIGCFG, true, System.Threading.CancellationToken.None);
            TurntableFile = new TurntableFile(RoutePath + @"\openrails\turntables.dat", RoutePath + @"\shapes\", MovingTables, this);
            LevelCrossings = new LevelCrossings(this);
            FuelManager = new FuelManager(this);
            Trains = new TrainList(this);
            PoolHolder = new Poolholder(this, timeTableFile, cancellationToken);

            TimetableInfo TTinfo = new TimetableInfo(this);

            TTTrain playerTTTrain = null;
            List<TTTrain> allTrains = TTinfo.ProcessTimetable(timeTableFile, PathName, cancellationToken);
            playerTTTrain = allTrains[0];

            AI = new AI(this, allTrains, ref ClockTime, playerTTTrain.FormedOf, playerTTTrain.FormedOfType, playerTTTrain, cancellationToken);

            if (playerTTTrain != null)
            {
                playerTTTrain.CalculatePositionOfCars(); // calculate position of player train cars
                playerTTTrain.PostInit();               // place player train after pre-running of AI trains
                if (!TrainDictionary.ContainsKey(playerTTTrain.Number)) TrainDictionary.Add(playerTTTrain.Number, playerTTTrain);
                if (!NameDictionary.ContainsKey(playerTTTrain.Name.ToLower())) NameDictionary.Add(playerTTTrain.Name.ToLower(), playerTTTrain);
            }
        }

        public void Stop()
        {
            if (MPManager.IsMultiPlayer()) MPManager.Stop();
        }

        public void Restore(BinaryReader inf, string pathName, float initialTileX, float initialTileZ, CancellationToken cancellation)
        {
            ClockTime = inf.ReadDouble();
            Season = (SeasonType)inf.ReadInt32();
            WeatherType = (WeatherType)inf.ReadInt32();
            TimetableMode = inf.ReadBoolean();
            UserWeatherFile = inf.ReadString();
            PathName = pathName;
            InitialTileX = initialTileX;
            InitialTileZ = initialTileZ;
            PoolHolder = new Poolholder(inf, this);

            SignalEnvironment = new SignalEnvironment(SIGCFG, false, System.Threading.CancellationToken.None);
            SignalEnvironment.Restore(inf);

            RestoreTrains(inf);
            LevelCrossings = new LevelCrossings(this);
            AI = new AI(this, inf);
            // Find original player train
            OriginalPlayerTrain = Trains.Find(item => item.Number == 0);
            if (OriginalPlayerTrain == null) OriginalPlayerTrain = AI.AITrains.Find(item => item.Number == 0);

            // initialization of turntables
            ActiveMovingTableIndex = inf.ReadInt32();
            TurntableFile = new TurntableFile(RoutePath + @"\openrails\turntables.dat", RoutePath + @"\shapes\", MovingTables, this);
            if (MovingTables.Count >= 0)
            {
                foreach (var movingTable in MovingTables) movingTable.Restore(inf, this);
            }

            ActivityRun = Orts.Simulation.Activity.Restore(inf, this, ActivityRun);
            SignalEnvironment.RestoreTrains(Trains);  // restore links to trains
            SignalEnvironment.Update(true);           // update all signals once to set proper stat
            MPManager.Instance().RememberOriginalSwitchState(); // this prepares a string that must then be passed to clients
        }

        public void Save(BinaryWriter outf)
        {
            outf.Write(ClockTime);
            outf.Write((int)Season);
            outf.Write((int)WeatherType);
            outf.Write(TimetableMode);
            outf.Write(UserWeatherFile);
            PoolHolder.Save(outf);
            SignalEnvironment.Save(outf);
            SaveTrains(outf);
            // LevelCrossings
            // InterlockingSystem
            AI.Save(outf);

            outf.Write(ActiveMovingTableIndex);
            if (MovingTables != null && MovingTables.Count >= 0)
                foreach (var movingtable in MovingTables) movingtable.Save(outf);

            Orts.Simulation.Activity.Save(outf, ActivityRun);
        }

        private Train InitializeTrains(CancellationToken cancellationToken)
        {
            Train playerTrain = InitializePlayerTrain();
            InitializeStaticConsists();
            AI = new AI(this, cancellationToken, ClockTime);
            if (playerTrain != null)
            {
                var validPosition = playerTrain.PostInit();
                TrainDictionary.Add(playerTrain.Number, playerTrain);
                NameDictionary.Add(playerTrain.Name, playerTrain);
            }
            return (playerTrain);
        }

        private AITrain InitializeAPTrains(CancellationToken cancellationToken)
        {
            AITrain playerTrain = InitializeAPPlayerTrain();
            InitializeStaticConsists();
            AI = new AI(this, cancellationToken, ClockTime);
            playerTrain.AI = AI;
            if (playerTrain != null)
            {
                var validPosition = playerTrain.PostInit();  // place player train after pre-running of AI trains
                if (validPosition && AI != null) AI.PreUpdate = false;
                if (playerTrain.InitialSpeed > 0 && playerTrain.MovementState != AiMovementState.StationStop)
                {
                    playerTrain.InitializeMoving();
                    playerTrain.MovementState = AiMovementState.Braking;
                }
                else if (playerTrain.InitialSpeed == 0)
                    playerTrain.InitializeBrakes();
            }
            return (playerTrain);
        }


        /// <summary>
        /// Which locomotive does the activity specified for the player.
        /// </summary>
        public TrainCar InitialPlayerLocomotive()
        {
            Physics.Train playerTrain = Trains[0];    // we install the player train first
            PlayerLocomotive = SetPlayerLocomotive(playerTrain);
            return PlayerLocomotive;
        }

        public TrainCar SetPlayerLocomotive(Physics.Train playerTrain)
        {
            TrainCar PlayerLocomotive = null;
            foreach (TrainCar car in playerTrain.Cars)
                if (car.IsDriveable)  // first loco is the one the player drives
                {
                    PlayerLocomotive = car;
                    playerTrain.LeadLocomotive = car;
                    playerTrain.InitializeBrakes();
                    PlayerLocomotive.LocalThrottlePercent = playerTrain.AITrainThrottlePercent;
                    break;
                }
            if (PlayerLocomotive == null)
                throw new InvalidDataException("Can't find player locomotive in activity");
            return PlayerLocomotive;
        }

        /// <summary>
        /// Gets path and consist of player train in multiplayer resume in activity
        /// </summary>
        public void GetPathAndConsist()
        {
            var PlayerServiceFileName = Activity.Activity.PlayerServices.Name;
            var srvFile = new ServiceFile(RoutePath + @"\SERVICES\" + PlayerServiceFileName + ".SRV");
            conFileName = BasePath + @"\TRAINS\CONSISTS\" + srvFile.TrainConfig + ".CON";
            patFileName = RoutePath + @"\PATHS\" + srvFile.PathId + ".PAT";
        }


        /// <summary>
        /// Convert and elapsed real time into clock time based on simulator
        /// running speed and paused state.
        /// </summary>
        public double GetElapsedClockSeconds(double elapsedRealSeconds)
        {
            return elapsedRealSeconds * (Paused ? 0 : GameSpeed);
        }

        /// <summary>
        /// Update the simulator state 
        /// elapsedClockSeconds represents the time since the last call to Simulator.Update
        /// Executes in the UpdaterProcess thread.
        /// </summary>
        public void Update(double elapsedClockSeconds)
        {
            // Advance the times.
            GameTime += elapsedClockSeconds;
            ClockTime += elapsedClockSeconds;

            // Check if there is a request to switch to another played train

            if (TrainSwitcher.ClickedSelectedAsPlayer && !playerSwitchOngoing)
                StartSwitchPlayerTrain();
            if (playerSwitchOngoing)
            {
                // We need to check whether the player locomotive has loaded before we complete the train switch.
                if (!OnQueryCarViewerLoaded(PlayerLocomotive))
                    return;
                CompleteSwitchPlayerTrain();
            }

            // Must be done before trains so that during turntable rotation train follows it
            if (ActiveMovingTable != null) ActiveMovingTable.Update();

            // Represent conditions at the specified clock time.
            List<Physics.Train> movingTrains = new List<Physics.Train>();

            if (PlayerLocomotive != null)
            {
                movingTrains.Add(PlayerLocomotive.Train);
                if (PlayerLocomotive.Train.LeadLocomotive != null
                    && PlayerLocomotive.Train.TrainType != TrainType.AiPlayerHosting
                    && string.Compare(PlayerLocomotive.Train.LeadLocomotive.CarID, PlayerLocomotive.CarID) != 0
                    && !MPManager.IsMultiPlayer())
                {
                    PlayerLocomotive = PlayerLocomotive.Train.LeadLocomotive;
                }
            }

            foreach (Physics.Train train in Trains)
            {
                if ((train.SpeedMpS != 0 || (train.ControlMode == TrainControlMode.Explorer && train.TrainType == TrainType.Remote && MPManager.IsServer())) &&
                    train.GetType() != typeof(AITrain) && train.GetType() != typeof(TTTrain) &&
                    (PlayerLocomotive == null || train != PlayerLocomotive.Train))
                {
                    movingTrains.Add(train);
                }
            }

            foreach (Physics.Train train in movingTrains)
            {
                if (MPManager.IsMultiPlayer())
                {
                    try
                    {
                        if (train.TrainType != TrainType.AiPlayerHosting)
                            train.Update(elapsedClockSeconds, false);
                        else ((AITrain)train).AIUpdate(elapsedClockSeconds, ClockTime, false);
                    }
                    catch (Exception e) { Trace.TraceWarning(e.Message); }
                }
                else if (train.TrainType != TrainType.AiPlayerHosting)
                {
                    train.Update(elapsedClockSeconds, false);
                }
                else
                {
                    ((AITrain)train).AIUpdate(elapsedClockSeconds, ClockTime, false);
                }
            }

            if (!TimetableMode)
            {
                if (!MPManager.IsMultiPlayer() || !MPManager.IsClient())
                {
                    foreach (Physics.Train train in movingTrains)
                    {
                        CheckForCoupling(train, elapsedClockSeconds);
                    }
                }
                else if (PlayerLocomotive != null)
                {
                    CheckForCoupling(PlayerLocomotive.Train, elapsedClockSeconds);
                }
            }

            if (SignalEnvironment != null)
            {
                if (!MPManager.IsMultiPlayer() || MPManager.IsServer()) SignalEnvironment.Update(false);
            }

            if (AI != null)
            {
                if (TimetableMode)
                {
                    AI.TimetableUpdate(elapsedClockSeconds);
                }
                else
                {
                    AI.ActivityUpdate(elapsedClockSeconds);
                }
            }

            if (LevelCrossings != null)
            {
                LevelCrossings.Update(elapsedClockSeconds);
            }

            if (ActivityRun != null)
            {
                ActivityRun.Update();
            }

            if (HazzardManager != null) HazzardManager.Update(elapsedClockSeconds);
        }

        internal void SetWeather(WeatherType weather, SeasonType season)
        {
            WeatherType = weather;
            Season = season;

            WeatherChanged?.Invoke(this, EventArgs.Empty);
        }

        private void FinishFrontCoupling(Physics.Train drivenTrain, Physics.Train train, TrainCar lead, bool sameDirection)
        {
            drivenTrain.LeadLocomotive = lead;
            drivenTrain.CalculatePositionOfCars();
            FinishCoupling(drivenTrain, train, true, sameDirection);
        }

        private void FinishRearCoupling(Physics.Train drivenTrain, Physics.Train train, bool sameDirection)
        {
            drivenTrain.RepositionRearTraveller();
            FinishCoupling(drivenTrain, train, false, sameDirection);
        }

        private void FinishCoupling(Physics.Train drivenTrain, Physics.Train train, bool couple_to_front, bool sameDirection)
        {
            // if coupled train was on turntable and static, remove it from list of trains on turntable
            if (ActiveMovingTable != null && ActiveMovingTable.TrainsOnMovingTable.Count != 0)
            {
                foreach (var trainOnMovingTable in ActiveMovingTable.TrainsOnMovingTable)
                {
                    if (trainOnMovingTable.Train.Number == train.Number)
                    {
                        ActiveMovingTable.TrainsOnMovingTable.Remove(trainOnMovingTable);
                        break;
                    }
                }
            }
            if (train.TrainType == TrainType.Ai && (((AITrain)train).UncondAttach ||
                train.TCRoute.ActiveSubPath < train.TCRoute.TCRouteSubpaths.Count - 1 || train.ValidRoute[0].Count > 5))
            {
                if (((drivenTrain.TCRoute != null && drivenTrain.TCRoute.ActiveSubPath == drivenTrain.TCRoute.TCRouteSubpaths.Count - 1 &&
                    drivenTrain.ValidRoute[0].Count < 5) || (drivenTrain is AITrain && ((AITrain)drivenTrain).UncondAttach)) && drivenTrain != OriginalPlayerTrain)
                {
                    // Switch to the attached train as the one where we are now is at the end of its life
                    TrainSwitcher.PickedTrainFromList = train;
                    TrainSwitcher.ClickedTrainFromList = true;
                    train.TrainType = TrainType.AiPlayerHosting;
                    Confirmer.Message(ConfirmLevel.Information, Catalog.GetString("Player train has been included into train {0} service {1}, that automatically becomes the new player train",
                        train.Number, train.Name));
                    train.Cars.Clear();
                    if (sameDirection)
                    {
                        foreach (TrainCar car in drivenTrain.Cars)
                        {
                            train.Cars.Add(car);
                            car.Train = train;
                        }
                    }
                    else
                    {
                        for (int i = drivenTrain.Cars.Count - 1; i >= 0; --i)
                        {
                            TrainCar car = drivenTrain.Cars[i];
                            train.Cars.Add(car);
                            car.Train = train;
                            car.Flipped = !car.Flipped;
                        }
                        if (drivenTrain.LeadLocomotiveIndex != -1) train.LeadLocomotiveIndex = train.Cars.Count - drivenTrain.LeadLocomotiveIndex - 1;
                    }
                    drivenTrain.Cars.Clear();
                    AI.TrainsToRemoveFromAI.Add((AITrain)train);
                    PlayerLocomotive = SetPlayerLocomotive(train);
                    (train as AITrain).SwitchToPlayerControl();
                    OnPlayerLocomotiveChanged();
                    if (drivenTrain.TCRoute.ActiveSubPath == drivenTrain.TCRoute.TCRouteSubpaths.Count - 1 && drivenTrain.ValidRoute[0].Count < 5)
                    {
                        (drivenTrain as AITrain).RemoveTrain();
                        train.UpdateTrackActionsCoupling(couple_to_front);
                        return;
                    }
                    // if there is just here a reversal point, increment subpath in order to be in accordance with train
                    var ppTCSectionIndex = drivenTrain.PresentPosition[Direction.Forward].TrackCircuitSectionIndex;
                    if (ppTCSectionIndex == drivenTrain.TCRoute.TCRouteSubpaths[drivenTrain.TCRoute.ActiveSubPath][drivenTrain.TCRoute.TCRouteSubpaths[drivenTrain.TCRoute.ActiveSubPath].Count - 1].TrackCircuitSection.Index)
                        Train.IncrementSubpath(drivenTrain);
                    // doubled check in case of double reverse point.
                    if (ppTCSectionIndex == drivenTrain.TCRoute.TCRouteSubpaths[drivenTrain.TCRoute.ActiveSubPath][drivenTrain.TCRoute.TCRouteSubpaths[drivenTrain.TCRoute.ActiveSubPath].Count - 1].TrackCircuitSection.Index)
                        Train.IncrementSubpath(drivenTrain);
                    var tempTrain = drivenTrain;
                    drivenTrain = train;
                    train = tempTrain;
                    AI.AITrains.Add(train as AITrain);
                }
                else
                {
                    var ppTCSectionIndex = train.PresentPosition[Direction.Forward].TrackCircuitSectionIndex;
                    if (ppTCSectionIndex == train.TCRoute.TCRouteSubpaths[train.TCRoute.ActiveSubPath][train.TCRoute.TCRouteSubpaths[train.TCRoute.ActiveSubPath].Count - 1].TrackCircuitSection.Index)
                        Train.IncrementSubpath(train);
                    // doubled check in case of double reverse point.
                    if (ppTCSectionIndex == train.TCRoute.TCRouteSubpaths[train.TCRoute.ActiveSubPath][train.TCRoute.TCRouteSubpaths[train.TCRoute.ActiveSubPath].Count - 1].TrackCircuitSection.Index)
                        Train.IncrementSubpath(train);
                }
                train.IncorporatingTrain = drivenTrain;
                train.IncorporatingTrainNo = drivenTrain.Number;
                ((AITrain)train).SuspendTrain(drivenTrain);
                drivenTrain.IncorporatedTrainNo = train.Number;
                if (MPManager.IsMultiPlayer()) MPManager.BroadCast((new MSGCouple(drivenTrain, train, false)).ToString());
            }
            else
            {
                train.RemoveFromTrack();
                if (train.TrainType != TrainType.AiIncorporated)
                {
                    Trains.Remove(train);
                    TrainDictionary.Remove(train.Number);
                    NameDictionary.Remove(train.Name.ToLower());
                }
                if (MPManager.IsMultiPlayer()) MPManager.BroadCast((new MSGCouple(drivenTrain, train, train.TrainType != TrainType.AiIncorporated)).ToString());
            }
            if (train.UncoupledFrom != null)
                train.UncoupledFrom.UncoupledFrom = null;

            if (PlayerLocomotive != null && PlayerLocomotive.Train == train)
            {
                drivenTrain.AITrainThrottlePercent = train.AITrainThrottlePercent;
                drivenTrain.AITrainBrakePercent = train.AITrainBrakePercent;
                drivenTrain.LeadLocomotive = PlayerLocomotive;
            }

            drivenTrain.UpdateTrackActionsCoupling(couple_to_front);
            AI.aiListChanged = true;
        }

        private static void UpdateUncoupled(Physics.Train drivenTrain, Physics.Train train, float d1, float d2, bool rear)
        {
            if (train == drivenTrain.UncoupledFrom && d1 > .5 && d2 > .5)
            {
                Traveller traveller = rear ? drivenTrain.RearTDBTraveller : drivenTrain.FrontTDBTraveller;
                float d3 = traveller.OverlapDistanceM(train.FrontTDBTraveller, rear);
                float d4 = traveller.OverlapDistanceM(train.RearTDBTraveller, rear);
                if (d3 > .5 && d4 > .5)
                {
                    train.UncoupledFrom = null;
                    drivenTrain.UncoupledFrom = null;
                }
            }
        }

        /// <summary>
        /// Scan other trains
        /// </summary>
        public void CheckForCoupling(Physics.Train drivenTrain, double elapsedClockSeconds)
        {
            if (MPManager.IsMultiPlayer() && !MPManager.IsServer()) return; //in MultiPlayer mode, server will check coupling, client will get message and do things
            if (drivenTrain.SpeedMpS < 0)
            {
                foreach (Physics.Train train in Trains)
                    if (train != drivenTrain && train.TrainType != TrainType.AiIncorporated)
                    {
                        //avoid coupling of player train with other players train
                        if (MPManager.IsMultiPlayer() && !MPManager.TrainOK2Couple(this, drivenTrain, train)) continue;

                        float d1 = drivenTrain.RearTDBTraveller.OverlapDistanceM(train.FrontTDBTraveller, true);
                        // Give another try if multiplayer
                        if (d1 >= 0 && drivenTrain.TrainType == TrainType.Remote &&
                            drivenTrain.PresentPosition[Direction.Backward].TrackCircuitSectionIndex == train.PresentPosition[Direction.Forward].TrackCircuitSectionIndex && drivenTrain.PresentPosition[Direction.Backward].TrackCircuitSectionIndex != -1)
                            d1 = drivenTrain.RearTDBTraveller.RoughOverlapDistanceM(train.FrontTDBTraveller, drivenTrain.FrontTDBTraveller, train.RearTDBTraveller, drivenTrain.Length, train.Length, true);
                        if (d1 < 0)
                        {
                            if (train == drivenTrain.UncoupledFrom)
                            {
                                if (drivenTrain.SpeedMpS < train.SpeedMpS)
                                    drivenTrain.SetCoupleSpeed(train, 1);
                                drivenTrain.CalculatePositionOfCars(elapsedClockSeconds, -d1);
                                return;
                            }
                            // couple my rear to front of train
                            //drivenTrain.SetCoupleSpeed(train, 1);
                            drivenTrain.LastCar.SignalEvent(TrainEvent.Couple);
                            if (drivenTrain.SpeedMpS > 1.5)
                                DbfEvalOverSpeedCoupling += 1;

                            foreach (TrainCar car in train.Cars)
                            {
                                drivenTrain.Cars.Add(car);
                                car.Train = drivenTrain;
                            }
                            FinishRearCoupling(drivenTrain, train, true);
                            return;
                        }
                        float d2 = drivenTrain.RearTDBTraveller.OverlapDistanceM(train.RearTDBTraveller, true);
                        // Give another try if multiplayer
                        if (d2 >= 0 && drivenTrain.TrainType == TrainType.Remote &&
                            drivenTrain.PresentPosition[Direction.Backward].TrackCircuitSectionIndex == train.PresentPosition[Direction.Backward].TrackCircuitSectionIndex && drivenTrain.PresentPosition[Direction.Backward].TrackCircuitSectionIndex != -1)
                            d2 = drivenTrain.RearTDBTraveller.RoughOverlapDistanceM(train.RearTDBTraveller, drivenTrain.FrontTDBTraveller, train.FrontTDBTraveller, drivenTrain.Length, train.Length, true);
                        if (d2 < 0)
                        {
                            if (train == drivenTrain.UncoupledFrom)
                            {
                                if (drivenTrain.SpeedMpS < -train.SpeedMpS)
                                    drivenTrain.SetCoupleSpeed(train, 11);
                                drivenTrain.CalculatePositionOfCars(elapsedClockSeconds, -d2);
                                return;
                            }
                            // couple my rear to rear of train
                            //drivenTrain.SetCoupleSpeed(train, -1);
                            drivenTrain.LastCar.SignalEvent(TrainEvent.Couple);
                            if (drivenTrain.SpeedMpS > 1.5)
                                DbfEvalOverSpeedCoupling += 1;

                            for (int i = train.Cars.Count - 1; i >= 0; --i)
                            {
                                TrainCar car = train.Cars[i];
                                drivenTrain.Cars.Add(car);
                                car.Train = drivenTrain;
                                car.Flipped = !car.Flipped;
                            }
                            FinishRearCoupling(drivenTrain, train, false);
                            return;
                        }
                        UpdateUncoupled(drivenTrain, train, d1, d2, false);
                    }
            }
            else if (drivenTrain.SpeedMpS > 0)
            {
                foreach (Physics.Train train in Trains)
                    if (train != drivenTrain && train.TrainType != TrainType.AiIncorporated)
                    {
                        //avoid coupling of player train with other players train if it is too short alived (e.g, when a train is just spawned, it may overlap with another train)
                        if (MPManager.IsMultiPlayer() && !MPManager.TrainOK2Couple(this, drivenTrain, train)) continue;
                        //	{
                        //		if ((MPManager.Instance().FindPlayerTrain(train) && drivenTrain == PlayerLocomotive.Train) || (MPManager.Instance().FindPlayerTrain(drivenTrain) && train == PlayerLocomotive.Train)) continue;
                        //		if ((MPManager.Instance().FindPlayerTrain(train) && MPManager.Instance().FindPlayerTrain(drivenTrain))) continue; //if both are player-controlled trains
                        //	}
                        float d1 = drivenTrain.FrontTDBTraveller.OverlapDistanceM(train.RearTDBTraveller, false);
                        // Give another try if multiplayer
                        if (d1 >= 0 && drivenTrain.TrainType == TrainType.Remote &&
                            drivenTrain.PresentPosition[Direction.Forward].TrackCircuitSectionIndex == train.PresentPosition[Direction.Backward].TrackCircuitSectionIndex && drivenTrain.PresentPosition[Direction.Forward].TrackCircuitSectionIndex != -1)
                            d1 = drivenTrain.FrontTDBTraveller.RoughOverlapDistanceM(train.RearTDBTraveller, drivenTrain.RearTDBTraveller, train.FrontTDBTraveller, drivenTrain.Length, train.Length, false);
                        if (d1 < 0)
                        {
                            if (train == drivenTrain.UncoupledFrom)
                            {
                                if (drivenTrain.SpeedMpS > train.SpeedMpS)
                                    drivenTrain.SetCoupleSpeed(train, 1);
                                drivenTrain.CalculatePositionOfCars(elapsedClockSeconds, d1);
                                return;
                            }
                            // couple my front to rear of train
                            //drivenTrain.SetCoupleSpeed(train, 1);

                            TrainCar lead = drivenTrain.LeadLocomotive;
                            if (lead == null)
                            {//Like Rear coupling with changed data  
                                lead = train.LeadLocomotive;
                                train.LastCar.SignalEvent(TrainEvent.Couple);
                                if (drivenTrain.SpeedMpS > 1.5)
                                    DbfEvalOverSpeedCoupling += 1;

                                for (int i = 0; i < drivenTrain.Cars.Count; ++i)
                                {
                                    TrainCar car = drivenTrain.Cars[i];
                                    train.Cars.Add(car);
                                    car.Train = train;
                                }
                                //Rear coupling
                                FinishRearCoupling(train, drivenTrain, false);
                            }
                            else
                            {
                                drivenTrain.FirstCar.SignalEvent(TrainEvent.Couple);
                                if (drivenTrain.SpeedMpS > 1.5)
                                    DbfEvalOverSpeedCoupling += 1;

                                lead = drivenTrain.LeadLocomotive;
                                for (int i = 0; i < train.Cars.Count; ++i)
                                {
                                    TrainCar car = train.Cars[i];
                                    drivenTrain.Cars.Insert(i, car);
                                    car.Train = drivenTrain;
                                }
                                if (drivenTrain.LeadLocomotiveIndex >= 0) drivenTrain.LeadLocomotiveIndex += train.Cars.Count;
                                FinishFrontCoupling(drivenTrain, train, lead, true);
                            }
                            return;
                        }
                        float d2 = drivenTrain.FrontTDBTraveller.OverlapDistanceM(train.FrontTDBTraveller, false);
                        // Give another try if multiplayer
                        if (d2 >= 0 && drivenTrain.TrainType == TrainType.Remote &&
                            drivenTrain.PresentPosition[Direction.Forward].TrackCircuitSectionIndex == train.PresentPosition[Direction.Forward].TrackCircuitSectionIndex && drivenTrain.PresentPosition[Direction.Forward].TrackCircuitSectionIndex != -1)
                            d2 = drivenTrain.FrontTDBTraveller.RoughOverlapDistanceM(train.FrontTDBTraveller, drivenTrain.RearTDBTraveller, train.RearTDBTraveller, drivenTrain.Length, train.Length, false);
                        if (d2 < 0)
                        {
                            if (train == drivenTrain.UncoupledFrom)
                            {
                                if (drivenTrain.SpeedMpS > -train.SpeedMpS)
                                    drivenTrain.SetCoupleSpeed(train, -1);
                                drivenTrain.CalculatePositionOfCars(elapsedClockSeconds, d2);
                                return;
                            }
                            // couple my front to front of train
                            //drivenTrain.SetCoupleSpeed(train, -1);
                            drivenTrain.FirstCar.SignalEvent(TrainEvent.Couple);
                            if (drivenTrain.SpeedMpS > 1.5)
                                DbfEvalOverSpeedCoupling += 1;

                            TrainCar lead = drivenTrain.LeadLocomotive;
                            for (int i = 0; i < train.Cars.Count; ++i)
                            {
                                TrainCar car = train.Cars[i];
                                drivenTrain.Cars.Insert(0, car);
                                car.Train = drivenTrain;
                                car.Flipped = !car.Flipped;
                            }
                            if (drivenTrain.LeadLocomotiveIndex >= 0) drivenTrain.LeadLocomotiveIndex += train.Cars.Count;
                            FinishFrontCoupling(drivenTrain, train, lead, false);
                            return;
                        }

                        UpdateUncoupled(drivenTrain, train, d1, d2, true);
                    }
            }
        }

        private Train InitializePlayerTrain()
        {

            Debug.Assert(Trains != null, "Cannot InitializePlayerTrain() without Simulator.Trains.");
            // set up the player locomotive

            Train train = new Train();
            train.TrainType = TrainType.Player;
            train.Number = 0;
            train.Name = "PLAYER";

            string playerServiceFileName;
            ServiceFile srvFile;

            playerServiceFileName = Path.GetFileNameWithoutExtension(ExploreConFile);
            srvFile = new ServiceFile(playerServiceFileName, playerServiceFileName, Path.GetFileNameWithoutExtension(ExplorePathFile));

            conFileName = BasePath + @"\TRAINS\CONSISTS\" + srvFile.TrainConfig + ".CON";
            patFileName = RoutePath + @"\PATHS\" + srvFile.PathId + ".PAT";
            OriginalPlayerTrain = train;

            if (conFileName.Contains("tilted")) train.IsTilting = true;


            AIPath aiPath = new AIPath(TDB, TSectionDat, patFileName, TimetableMode);
            PathName = aiPath.pathName;

            if (aiPath.Nodes == null)
            {
                throw new InvalidDataException("Broken path " + patFileName + " for Player train - activity cannot be started");
            }

            // place rear of train on starting location of aiPath.
            train.RearTDBTraveller = new Traveller(TSectionDat, TDB.TrackDB.TrackNodes, aiPath);

            ConsistFile conFile = new ConsistFile(conFileName);
            CurveDurability = conFile.Train.Durability;   // Finds curve durability of consist based upon the value in consist file

            // add wagons
            foreach (Wagon wagon in conFile.Train.Wagons)
            {

                string wagonFolder = BasePath + @"\trains\trainset\" + wagon.Folder;
                string wagonFilePath = wagonFolder + @"\" + wagon.Name + ".wag"; ;
                if (wagon.IsEngine)
                    wagonFilePath = Path.ChangeExtension(wagonFilePath, ".eng");

                if (!File.Exists(wagonFilePath))
                {
                    // First wagon is the player's loco and required, so issue a fatal error message
                    if (wagon == conFile.Train.Wagons[0])
                        Trace.TraceError("Player's locomotive {0} cannot be loaded in {1}", wagonFilePath, conFileName);
                    Trace.TraceWarning($"Ignored missing {(wagon.IsEngine ? "engine" : "wagon")} {wagonFilePath} in consist {conFileName}");
                    continue;
                }

                try
                {
                    TrainCar car = RollingStock.Load(this, wagonFilePath);
                    car.Flipped = wagon.Flip;
                    car.UiD = wagon.UiD;
                    if (MPManager.IsMultiPlayer()) car.CarID = MPManager.GetUserName() + " - " + car.UiD; //player's train is always named train 0.
                    else car.CarID = "0 - " + car.UiD; //player's train is always named train 0.
                    train.Cars.Add(car);
                    car.Train = train;
                    train.Length += car.CarLengthM;

                    var mstsDieselLocomotive = car as MSTSDieselLocomotive;
                    if (Activity != null && mstsDieselLocomotive != null)
                        mstsDieselLocomotive.DieselLevelL = mstsDieselLocomotive.MaxDieselLevelL * Activity.Activity.Header.FuelDiesel / 100.0f;

                    var mstsSteamLocomotive = car as MSTSSteamLocomotive;
                    if (Activity != null && mstsSteamLocomotive != null)
                    {
                        mstsSteamLocomotive.CombinedTenderWaterVolumeUKG = (float)(Mass.Kilogram.ToLb(mstsSteamLocomotive.MaxLocoTenderWaterMassKG) / 10.0f) * Activity.Activity.Header.FuelWater / 100.0f;
                        mstsSteamLocomotive.TenderCoalMassKG = mstsSteamLocomotive.MaxTenderCoalMassKG * Activity.Activity.Header.FuelCoal / 100.0f;
                    }
                }
                catch (Exception error)
                {
                    // First wagon is the player's loco and required, so issue a fatal error message
                    if (wagon == conFile.Train.Wagons[0])
                        throw new FileLoadException(wagonFilePath, error);
                    Trace.WriteLine(new FileLoadException(wagonFilePath, error));
                }
            }// for each rail car

            train.CheckFreight();

            train.PresetExplorerPath(aiPath);
            train.ControlMode = TrainControlMode.Explorer;

            TrackCircuitPartialPathRoute tempRoute = train.CalculateInitialTrainPosition();
            if (tempRoute.Count == 0)
            {
                throw new InvalidDataException("Player train original position not clear");
            }

            train.SetInitialTrainRoute(tempRoute);
            train.CalculatePositionOfCars();
            train.ResetInitialTrainRoute(tempRoute);

            train.CalculatePositionOfCars();
            Trains.Add(train);

            // Note the initial position to be stored by a Save and used in Menu.exe to calculate DistanceFromStartM 
            InitialTileX = Trains[0].FrontTDBTraveller.TileX + (Trains[0].FrontTDBTraveller.X / 2048);
            InitialTileZ = Trains[0].FrontTDBTraveller.TileZ + (Trains[0].FrontTDBTraveller.Z / 2048);

            PlayerLocomotive = InitialPlayerLocomotive();
            if ((conFile.Train.MaxVelocity == null) ||
                ((conFile.Train.MaxVelocity?.A <= 0f) || (conFile.Train.MaxVelocity?.A == 40f)))
                train.TrainMaxSpeedMpS = Math.Min((float)TRK.Route.SpeedLimit, ((MSTSLocomotive)PlayerLocomotive).MaxSpeedMpS);
            else
                train.TrainMaxSpeedMpS = Math.Min((float)TRK.Route.SpeedLimit, conFile.Train.MaxVelocity.A);

            float prevEQres = train.EqualReservoirPressurePSIorInHg;
            train.AITrainBrakePercent = 100; //<CSComment> This seems a tricky way for the brake modules to test if it is an AI train or not
            train.EqualReservoirPressurePSIorInHg = prevEQres; // The previous command modifies EQ reservoir pressure, causing issues with EP brake systems, so restore to prev value
            return (train);
        }

        // used for activity and activity in explore mode; creates the train within the AITrain class
        private AITrain InitializeAPPlayerTrain()
        {
            string playerServiceFileName;
            ServiceFile srvFile;
            if (Activity != null && Activity.Activity.Serial != -1)
            {
                playerServiceFileName = Activity.Activity.PlayerServices.Name;
                srvFile = new ServiceFile(RoutePath + @"\SERVICES\" + playerServiceFileName + ".SRV");
            }
            else
            {
                playerServiceFileName = Path.GetFileNameWithoutExtension(ExploreConFile);
                srvFile = new ServiceFile(playerServiceFileName, playerServiceFileName, Path.GetFileNameWithoutExtension(ExplorePathFile));
            }
            conFileName = BasePath + @"\TRAINS\CONSISTS\" + srvFile.TrainConfig + ".CON";
            patFileName = RoutePath + @"\PATHS\" + srvFile.PathId + ".PAT";
            PlayerTraffics player_Traffic_Definition = Activity.Activity.PlayerServices.PlayerTraffics;
            ServiceTraffics aPPlayer_Traffic_Definition = new ServiceTraffics(playerServiceFileName, player_Traffic_Definition);
            Services aPPlayer_Service_Definition = new Services(playerServiceFileName, player_Traffic_Definition);

            AI AI = new AI(this);
            AITrain train = AI.CreateAITrainDetail(aPPlayer_Service_Definition, aPPlayer_Traffic_Definition, srvFile, TimetableMode, true);
            AI = null;
            train.Name = "PLAYER";
            train.Cars[0].Headlight = 0;
            OriginalPlayerTrain = train;
            train.Efficiency = 0.9f; // Forced efficiency, as considered most similar to human player
            TrackCircuitPartialPathRoute tempRoute = train.CalculateInitialTrainPosition();
            if (tempRoute.Count == 0)
            {
                throw new InvalidDataException("Player train original position not clear");
            }
            train.SetInitialTrainRoute(tempRoute);
            train.CalculatePositionOfCars();
            train.ResetInitialTrainRoute(tempRoute);

            train.CalculatePositionOfCars();
            train.TrainType = TrainType.AiPlayerDriven;
            Trains.Add(train);

            // Note the initial position to be stored by a Save and used in Menu.exe to calculate DistanceFromStartM 
            InitialTileX = Trains[0].FrontTDBTraveller.TileX + (Trains[0].FrontTDBTraveller.X / 2048);
            InitialTileZ = Trains[0].FrontTDBTraveller.TileZ + (Trains[0].FrontTDBTraveller.Z / 2048);

            PlayerLocomotive = InitialPlayerLocomotive();
            if (train.MaxVelocityA <= 0f || train.MaxVelocityA == 40f)
                train.TrainMaxSpeedMpS = Math.Min((float)TRK.Route.SpeedLimit, ((MSTSLocomotive)PlayerLocomotive).MaxSpeedMpS);
            else
                train.TrainMaxSpeedMpS = Math.Min((float)TRK.Route.SpeedLimit, train.MaxVelocityA);
            if (train.InitialSpeed > 0 && train.MovementState != AiMovementState.StationStop)
            {
                train.InitializeMoving();
                train.MovementState = AiMovementState.Braking;
            }
            else if (train.InitialSpeed == 0)
                train.InitializeBrakes();

            // process player passing paths as required
            if (SignalEnvironment.UseLocationPassingPaths)
            {
                TrackDirection orgDirection = (TrackDirection)(train.RearTDBTraveller != null ? (int)train.RearTDBTraveller.Direction : -2);
                _ = new TrackCircuitRoutePath(train.Path, orgDirection, 0, -1);
            }

            if (conFileName.Contains("tilted")) train.IsTilting = true;

            return train;
        }

        /// <summary>
        /// Set up trains based on info in the static consists listed in the activity file.
        /// </summary>
        private void InitializeStaticConsists()
        {
            if (Activity == null) return;
            if (Activity.Activity == null) return;
            if (Activity.Activity.ActivityObjects == null) return;
            // for each static consist
            foreach (ActivityObject activityObject in Activity.Activity.ActivityObjects)
            {
                try
                {
                    // construct train data
                    Train train = new Train();
                    train.TrainType = TrainType.Static;
                    train.Name = "STATIC" + "-" + activityObject.ID;
                    int consistDirection;
                    switch (activityObject.Direction)  // TODO, we don't really understand this
                    {
                        case 0: consistDirection = 0; break;  // reversed ( confirmed on L&PS route )
                        case 18: consistDirection = 1; break;  // forward ( confirmed on ON route )
                        case 131: consistDirection = 1; break; // forward ( confirmed on L&PS route )
                        default: consistDirection = 1; break;  // forward ( confirmed on L&PS route )
                    }
                    // FIXME: Where are TSectionDat and TDB from?
                    train.RearTDBTraveller = new Traveller(TSectionDat, TDB.TrackDB.TrackNodes, activityObject.Location);
                    if (consistDirection != 1)
                        train.RearTDBTraveller.ReverseDirection();
                    // add wagons in reverse order - ie first wagon is at back of train
                    // static consists are listed back to front in the activities, so we have to reverse the order, and flip the cars
                    // when we add them to ORTS
                    for (int iWagon = activityObject.TrainSet.Wagons.Count - 1; iWagon >= 0; --iWagon)
                    {
                        Wagon wagon = (Wagon)activityObject.TrainSet.Wagons[iWagon];
                        string wagonFolder = BasePath + @"\trains\trainset\" + wagon.Folder;
                        string wagonFilePath = wagonFolder + @"\" + wagon.Name + ".wag"; ;
                        if (wagon.IsEngine)
                            wagonFilePath = Path.ChangeExtension(wagonFilePath, ".eng");

                        if (!File.Exists(wagonFilePath))
                        {
                            Trace.TraceWarning($"Ignored missing {(wagon.IsEngine? "engine" : "wagon")} {wagonFilePath} in activity definition {activityObject.TrainSet.Name}");
                            continue;
                        }

                        try // Load could fail if file has bad data.
                        {
                            TrainCar car = RollingStock.Load(this, wagonFilePath);
                            car.Flipped = !wagon.Flip;
                            car.UiD = wagon.UiD;
                            car.CarID = activityObject.ID + " - " + car.UiD;
                            train.Cars.Add(car);
                            car.Train = train;
                        }
                        catch (Exception error)
                        {
                            Trace.WriteLine(new FileLoadException(wagonFilePath, error));
                        }
                    }// for each rail car

                    if (train.Cars.Count == 0) return;

                    // in static consists, the specified location represents the middle of the last car, 
                    // our TDB traveller is always at the back of the last car so it needs to be repositioned
                    TrainCar lastCar = train.LastCar;
                    train.RearTDBTraveller.ReverseDirection();
                    train.RearTDBTraveller.Move(lastCar.CarLengthM / 2f);
                    train.RearTDBTraveller.ReverseDirection();

                    train.CalculatePositionOfCars();
                    train.InitializeBrakes();
                    train.CheckFreight();
                    train.ReverseFormation(false); // When using autopilot mode this is needed for correct working of train switching
                    bool validPosition = train.PostInit();
                    if (validPosition)
                        Trains.Add(train);
                }
                catch (Exception error)
                {
                    Trace.WriteLine(error);
                }
            }// for each train
        }

        private void SaveTrains(BinaryWriter outf)
        {
            if (PlayerLocomotive.Train != Trains[0])
            {
                for (int i = 1; i < Trains.Count; i++)
                {
                    if (PlayerLocomotive.Train == Trains[i])
                    {
                        Trains[i] = Trains[0];
                        Trains[0] = PlayerLocomotive.Train;
                        break;
                    }
                }
            }

            // do not save AI trains (done by AITrain)
            // do not save Timetable Trains (done by TTTrain through AITrain)

            foreach (Physics.Train train in Trains)
            {
                if (train.TrainType != TrainType.Ai && train.TrainType != TrainType.AiPlayerDriven && train.TrainType != TrainType.AiPlayerHosting &&
                    train.TrainType != TrainType.AiIncorporated && train.GetType() != typeof(TTTrain))
                {
                    outf.Write(0);
                    if (train is AITrain && train.TrainType == TrainType.Static)
                        ((AITrain)train).SaveBase(outf);
                    else train.Save(outf);
                }
                else if (train.TrainType == TrainType.AiPlayerDriven || train.TrainType == TrainType.AiPlayerHosting)
                {
                    outf.Write(-2);
                    AI.SaveAutopil(train, outf);
                }
            }
            outf.Write(-1);

        }

        //================================================================================================//
        //
        // Restore trains
        //

        private void RestoreTrains(BinaryReader inf)
        {

            Trains = new TrainList(this);

            int trainType = inf.ReadInt32();
            while (trainType != -1)
            {
                if (trainType >= 0) Trains.Add(new Train(inf));
                else if (trainType == -2)                   // Autopilot mode
                {
                    AI = new AI(this, inf, true);
                    AI = null;
                }
                trainType = inf.ReadInt32();
            }

            // find player train
            foreach (Physics.Train thisTrain in Trains)
            {
                if (thisTrain.TrainType == TrainType.Player
                    || thisTrain.TrainType == TrainType.AiPlayerDriven || thisTrain.TrainType == TrainType.AiPlayerHosting)
                {
                    TrainDictionary.Add(thisTrain.Number, thisTrain);
                    NameDictionary.Add(thisTrain.Name, thisTrain);
                    // restore signal references depending on state
                    if (thisTrain.ControlMode == TrainControlMode.Explorer)
                    {
                        thisTrain.RestoreExplorerMode();
                    }
                    else if (thisTrain.ControlMode == TrainControlMode.Manual)
                    {
                        thisTrain.RestoreManualMode();
                    }
                    else if (thisTrain.TrainType == TrainType.Player)
                    {
                        thisTrain.InitializeSignals(true);
                    }
                }
            }
        }

        /// <summary>
        ///  Get Autogenerated train by number
        /// </summary>
        /// <param name="reqNumber"></param>
        /// <returns></returns>

        public TTTrain GetAutoGenTTTrainByNumber(int reqNumber)
        {
            TTTrain returnTrain = null;
            if (AutoGenDictionary.ContainsKey(reqNumber))
            {
                AITrain tempTrain = AutoGenDictionary[reqNumber];
                returnTrain = tempTrain as TTTrain;
                returnTrain.AI.AutoGenTrains.Remove(tempTrain);
                AutoGenDictionary.Remove(reqNumber);
                returnTrain.RoutedBackward = new Physics.Train.TrainRouted(returnTrain, 1);
                returnTrain.RoutedForward = new Physics.Train.TrainRouted(returnTrain, 0);
            }
            return (returnTrain);
        }

        /// <summary>
        /// The front end of a railcar is at MSTS world coordinates x1,y1,z1
        /// The other end is at x2,y2,z2
        /// Return a rotation and translation matrix for the center of the railcar.
        /// </summary>
        public static Matrix XNAMatrixFromMSTSCoordinates(float x1, float y1, float z1, float x2, float y2, float z2)
        {
            // translate 1st coordinate to be relative to 0,0,0
            float dx = (float)(x1 - x2);
            float dy = (float)(y1 - y2);
            float dz = (float)(z1 - z2);

            // compute the rotational matrix  
            float length = (float)Math.Sqrt(dx * dx + dz * dz + dy * dy);
            float run = (float)Math.Sqrt(dx * dx + dz * dz);
            // normalize to coordinate to a length of one, ie dx is change in x for a run of 1
            if (length != 0)    // Avoid zero divide
            {
                dx /= length;
                dy /= length;   // ie if it is tilted back 5 degrees, this is sin 5 = 0.087
                run /= length;  //                              and   this is cos 5 = 0.996
                dz /= length;
            }
            else
            {                   // If length is zero all elements of its calculation are zero. Since dy is a sine and is zero,
                run = 1f;       // run is therefore 1 since it is cosine of the same angle?  See comments above.
            }


            // setup matrix values

            Matrix xnaTilt = new Matrix(1, 0, 0, 0,
                                     0, run, dy, 0,
                                     0, -dy, run, 0,
                                     0, 0, 0, 1);

            Matrix xnaRotation = new Matrix(dz, 0, dx, 0,
                                            0, 1, 0, 0,
                                            -dx, 0, dz, 0,
                                            0, 0, 0, 1);

            Matrix xnaLocation = Matrix.CreateTranslation((x1 + x2) / 2f, (y1 + y2) / 2f, -(z1 + z2) / 2f);
            MatrixExtension.Multiply(xnaTilt, xnaRotation, out Matrix result);
            return MatrixExtension.Multiply(result, xnaLocation);
//            return xnaTilt * xnaRotation * xnaLocation;
        }

        public void UncoupleBehind(int carPosition)
        {
            // check on car position in case of mouse jitter
            if (carPosition <= PlayerLocomotive.Train.Cars.Count - 1) UncoupleBehind(PlayerLocomotive.Train.Cars[carPosition], true);
        }

        public void UncoupleBehind(TrainCar car, bool keepFront)
        {
            Physics.Train train = car.Train;

            if (MPManager.IsMultiPlayer() && !MPManager.TrainOK2Decouple(Confirmer, train)) return;
            int i = 0;
            while (train.Cars[i] != car) ++i;  // it can't happen that car isn't in car.Train
            if (i == train.Cars.Count - 1) return;  // can't uncouple behind last car
            ++i;

            TrainCar lead = train.LeadLocomotive;
            Physics.Train train2;
            if (train.IncorporatedTrainNo == -1)
            {
                train2 = new Train(train);
                Trains.Add(train2);
            }
            else
            {
                train2 = TrainDictionary[train.IncorporatedTrainNo];
            }

            if (MPManager.IsMultiPlayer() && !(train2 is AITrain)) train2.ControlMode = TrainControlMode.Explorer;
            // Player locomotive is in first or in second part of train?
            int j = 0;
            while (train.Cars[j] != PlayerLocomotive && j < i) j++;

            // This is necessary, because else we had to create an AI train and not a train when in autopilot mode
            if ((train.IsActualPlayerTrain && j >= i) || !keepFront)
            {
                // Player locomotive in second part of train, move first part of cars to the new train
                for (int k = 0; k < i; ++k)
                {
                    TrainCar newcar = train.Cars[k];
                    train2.Cars.Add(newcar);
                    newcar.Train = train2;
                }

                // and drop them from the old train
                for (int k = i - 1; k >= 0; --k)
                {
                    train.Cars.RemoveAt(k);
                }

                train.FirstCar.CouplerSlackM = 0;
                if (train.LeadLocomotiveIndex >= 0) train.LeadLocomotiveIndex -= i;
            }
            else
            {
                // move rest of cars to the new train

                for (int k = i; k < train.Cars.Count; ++k)
                {
                    TrainCar newcar = train.Cars[k];
                    train2.Cars.Add(newcar);
                    newcar.Train = train2;
                }

                // and drop them from the old train
                for (int k = train.Cars.Count - 1; k >= i; --k)
                {
                    train.Cars.RemoveAt(k);
                }

                train.LastCar.CouplerSlackM = 0;

            }

            // and fix up the travellers
            if (train.IsActualPlayerTrain && j >= i || !keepFront)
            {
                train2.FrontTDBTraveller = new Traveller(train.FrontTDBTraveller);
                train.CalculatePositionOfCars();
                train2.RearTDBTraveller = new Traveller(train.FrontTDBTraveller);
                train2.CalculatePositionOfCars();  // fix the front traveller
                train.DistanceTravelledM -= train2.Length;
            }
            else
            {
                train2.RearTDBTraveller = new Traveller(train.RearTDBTraveller);
                train2.CalculatePositionOfCars();  // fix the front traveller
                train.RepositionRearTraveller();    // fix the rear traveller
            }

            train.ActivityClearingDistanceM = train.Cars.Count < Physics.Train.StandardTrainMinCarNo ? Physics.Train.ShortClearingDistanceM : Physics.Train.StandardClearingDistanceM;
            train2.ActivityClearingDistanceM = train2.Cars.Count < Physics.Train.StandardTrainMinCarNo ? Physics.Train.ShortClearingDistanceM : Physics.Train.StandardClearingDistanceM;


            train.UncoupledFrom = train2;
            train2.UncoupledFrom = train;
            train2.SpeedMpS = train.SpeedMpS;
            train2.Cars[0].BrakeSystem.FrontBrakeHoseConnected = false;
            train2.AITrainDirectionForward = train.AITrainDirectionForward;

            // It is an action, not just a simple copy, thus don't do it if the train is driven by the player:
            if (PlayerLocomotive == null)
                train2.AITrainBrakePercent = train.AITrainBrakePercent;

            if (train.IncorporatedTrainNo != -1)
            {
                train2.AITrainBrakePercent = 100;
                train2.TrainType = TrainType.Ai;
                train.IncorporatedTrainNo = -1;
                train2.MUDirection = MidpointDirection.Forward;
            }
            else train2.TrainType = TrainType.Static;
            train2.LeadLocomotive = null;
            if ((train.TrainType == TrainType.Ai || train.TrainType == TrainType.AiPlayerHosting) && train2.TrainType == TrainType.Static)
                train2.InitializeBrakes();
            else
            {
                train2.Cars[0].BrakeSystem.PropagateBrakePressure(5);
                foreach (MSTSWagon wagon in train2.Cars)
                    wagon.MSTSBrakeSystem.Update(5);
            }
            bool inPath;

            if ((train.IsActualPlayerTrain && j >= i) || !keepFront)
            {
                train.TemporarilyRemoveFromTrack();
                inPath = train2.UpdateTrackActionsUncoupling(false);
                train.UpdateTrackActionsUncoupling(false);
            }
            else
            {
                train.UpdateTrackActionsUncoupling(true);
                inPath = train2.UpdateTrackActionsUncoupling(false);
            }
            if (!inPath && train2.TrainType == TrainType.Ai)
            // Out of path, degrade to static
            {
                train2.TrainType = TrainType.Static;
                ((AITrain)train2).AI.TrainsToRemoveFromAI.Add((AITrain)train2);
            }
            if (train2.TrainType == TrainType.Ai)
            {
                // Move reversal point under train if there is one in the section where the train is
                if (train2.PresentPosition[Direction.Forward].TrackCircuitSectionIndex ==
                                    train2.TCRoute.TCRouteSubpaths[train2.TCRoute.ActiveSubPath][train2.TCRoute.TCRouteSubpaths[train2.TCRoute.ActiveSubPath].Count - 1].TrackCircuitSection.Index &&
                    train2.TCRoute.ActiveSubPath < train2.TCRoute.TCRouteSubpaths.Count - 1)
                {
                    train2.TCRoute.ReversalInfo[train2.TCRoute.ActiveSubPath].ReverseReversalOffset = train2.PresentPosition[Direction.Forward].Offset - 10f;
                    train2.AuxActionsContainer.MoveAuxActionAfterReversal(train2);
                }
                else if ((train.IsActualPlayerTrain && j >= i) || !keepFront)
                {
                    train2.AuxActionsContainer.MoveAuxAction(train2);
                }
                ((AITrain)train2).ResetActions(true);
            }
            if (MPManager.IsMultiPlayer())
            {
                if (!(train is AITrain)) train.ControlMode = TrainControlMode.Explorer;
                if (!(train2 is AITrain)) train2.ControlMode = TrainControlMode.Explorer;
            }

            if (MPManager.IsMultiPlayer() && !MPManager.IsServer())
            {
                //add the new train to a list of uncoupled trains, handled specially
                if (PlayerLocomotive != null && PlayerLocomotive.Train == train) MPManager.Instance().AddUncoupledTrains(train2);
            }


            train.CheckFreight();
            train2.CheckFreight();

            train.Update(0);   // stop the wheels from moving etc
            train2.Update(0);  // stop the wheels from moving etc

            car.SignalEvent(TrainEvent.Uncouple);
            // TODO which event should we fire
            //car.CreateEvent(62);  these are listed as alternate events
            //car.CreateEvent(63);
            if (MPManager.IsMultiPlayer())
            {
                MPManager.Notify((new Orts.MultiPlayer.MSGUncouple(train, train2, Orts.MultiPlayer.MPManager.GetUserName(), car.CarID, PlayerLocomotive)).ToString());
            }
            if (Confirmer != null && IsReplaying) Confirmer.Confirm(CabControl.Uncouple, train.LastCar.CarID);
            if (AI != null) AI.aiListChanged = true;
            if (train2.TrainType == TrainType.Static && (train.TrainType == TrainType.Player || train.TrainType == TrainType.AiPlayerDriven))
            {
                // check if detached on turntable or transfertable
                if (ActiveMovingTable != null) ActiveMovingTable.CheckTrainOnMovingTable(train2);
            }
        }

        /// <summary>
        /// Performs first part of player train switch
        /// </summary>
        private void StartSwitchPlayerTrain()
        {
            if (TrainSwitcher.SelectedAsPlayer != null && !TrainSwitcher.SelectedAsPlayer.IsActualPlayerTrain)
            {
                var selectedAsPlayer = TrainSwitcher.SelectedAsPlayer;
                var oldTrainReverseFormation = false;
                var newTrainReverseFormation = false;
                if (PlayerLocomotive.Train is AITrain && !PlayerLocomotive.Train.IsPathless)
                {
                    var playerTrain = PlayerLocomotive.Train as AITrain;
                    if (playerTrain != null)
                    {
                        if (playerTrain.ControlMode == TrainControlMode.Manual) 
                            TrainSwitcher.SuspendOldPlayer = true; // force suspend state to avoid disappearing of train;
                        if (TrainSwitcher.SuspendOldPlayer && 
                            (playerTrain.SpeedMpS < -0.025 || playerTrain.SpeedMpS > 0.025 || playerTrain.IsMoving()))
                        {
                            Confirmer.Message(ConfirmLevel.Warning, Catalog.GetString("Train can't be suspended with speed not equal 0"));
                            TrainSwitcher.SuspendOldPlayer = false;
                            TrainSwitcher.ClickedSelectedAsPlayer = false;
                            return;
                        }
                        if (playerTrain.TrainType == TrainType.AiPlayerDriven)
                        {
                            // it must be autopiloted first
                            playerTrain.SwitchToAutopilotControl();
                        }
                        // and now switch!
                        playerTrain.TrainType = TrainType.Ai;
                        AI.AITrains.Add(playerTrain);
                        if (TrainSwitcher.SuspendOldPlayer)
                        {
                            playerTrain.MovementState = AiMovementState.Suspended;
                            if (playerTrain.ValidRoute[0] != null && playerTrain.PresentPosition[Direction.Forward].RouteListIndex != -1 &&
                                playerTrain.ValidRoute[0].Count > playerTrain.PresentPosition[Direction.Forward].RouteListIndex + 1)
                                SignalEnvironment.BreakDownRoute(playerTrain.ValidRoute[0][playerTrain.PresentPosition[Direction.Forward].RouteListIndex + 1].TrackCircuitSection.Index,
                                   playerTrain.RoutedForward);
                            TrainSwitcher.SuspendOldPlayer = false;
                        }

                    }
                }
                else if (selectedAsPlayer.TrainType == TrainType.AiIncorporated && selectedAsPlayer.IncorporatingTrain.IsPathless)
                {
                    // the former static train disappears now and becomes part of the other train. TODO; also wagons must be moved.
                    var dyingTrain = PlayerLocomotive.Train;

                    // move all cars to former incorporated train

                    for (int k = 0; k < dyingTrain.Cars.Count; ++k)
                    {
                        TrainCar newcar = dyingTrain.Cars[k];
                        selectedAsPlayer.Cars.Add(newcar);
                        newcar.Train = selectedAsPlayer;
                    }

                    // and drop them from the old train
                    for (int k = dyingTrain.Cars.Count - 1; k >= 0; --k)
                    {
                        dyingTrain.Cars.RemoveAt(k);
                    }

                    // and fix up the travellers
                    selectedAsPlayer.RearTDBTraveller = new Traveller(dyingTrain.RearTDBTraveller);
                    selectedAsPlayer.FrontTDBTraveller = new Traveller(dyingTrain.FrontTDBTraveller);
                    // are following lines needed?
                    //                       selectedAsPlayer.CalculatePositionOfCars(0);  // fix the front traveller
                    //                       selectedAsPlayer.RepositionRearTraveller();    // fix the rear traveller

                    selectedAsPlayer.ActivityClearingDistanceM = dyingTrain.ActivityClearingDistanceM;

                    selectedAsPlayer.SpeedMpS = dyingTrain.SpeedMpS;
                    selectedAsPlayer.AITrainDirectionForward = dyingTrain.AITrainDirectionForward;

                    selectedAsPlayer.AITrainBrakePercent = 100;
                    selectedAsPlayer.TrainType = TrainType.Ai;
                    selectedAsPlayer.MUDirection = MidpointDirection.Forward;

                    selectedAsPlayer.LeadLocomotive = null;
                    selectedAsPlayer.Cars[0].BrakeSystem.PropagateBrakePressure(5);
                    foreach (MSTSWagon wagon in selectedAsPlayer.Cars)
                        wagon.MSTSBrakeSystem.Update(5);

                    // and now let the former static train die

                    dyingTrain.RemoveFromTrack();
                    dyingTrain.ClearDeadlocks();
                    Trains.Remove(dyingTrain);
                    TrainDictionary.Remove(dyingTrain.Number);
                    NameDictionary.Remove(dyingTrain.Name.ToLower());

                    bool inPath;

                    inPath = selectedAsPlayer.UpdateTrackActionsUncoupling(false);

                    if (!inPath && selectedAsPlayer.TrainType == TrainType.Ai)
                    // Out of path, degrade to static
                    {
                        selectedAsPlayer.TrainType = TrainType.Static;
                        ((AITrain)selectedAsPlayer).AI.TrainsToRemoveFromAI.Add((AITrain)selectedAsPlayer);
                    }
                    if (selectedAsPlayer.TrainType == TrainType.Ai)
                    {
                        ((AITrain)selectedAsPlayer).AI.aiListChanged = true;
                        // Move reversal point under train if there is one in the section where the train is
                        if (selectedAsPlayer.PresentPosition[Direction.Forward].TrackCircuitSectionIndex ==
                                            selectedAsPlayer.TCRoute.TCRouteSubpaths[selectedAsPlayer.TCRoute.ActiveSubPath][selectedAsPlayer.TCRoute.TCRouteSubpaths[selectedAsPlayer.TCRoute.ActiveSubPath].Count - 1].TrackCircuitSection.Index &&
                            selectedAsPlayer.TCRoute.ActiveSubPath < selectedAsPlayer.TCRoute.TCRouteSubpaths.Count - 1)
                        {
                            selectedAsPlayer.TCRoute.ReversalInfo[selectedAsPlayer.TCRoute.ActiveSubPath].ReverseReversalOffset = selectedAsPlayer.PresentPosition[Direction.Forward].Offset - 10f;
                            selectedAsPlayer.AuxActionsContainer.MoveAuxActionAfterReversal(selectedAsPlayer);
                        }
                        ((AITrain)selectedAsPlayer).ResetActions(true);
                    }
                    if (MPManager.IsMultiPlayer() && !MPManager.IsServer())
                    {
                        selectedAsPlayer.ControlMode = TrainControlMode.Explorer;
                        //add the new train to a list of uncoupled trains, handled specially
                        if (PlayerLocomotive != null) MPManager.Instance().AddUncoupledTrains(selectedAsPlayer);
                    }


                    selectedAsPlayer.CheckFreight();

                    selectedAsPlayer.Update(0);  // stop the wheels from moving etc
                    TrainSwitcher.PickedTrainFromList = selectedAsPlayer;
                    TrainSwitcher.ClickedTrainFromList = true;


                }
                else
                {
                    // this was a static train before
                    var playerTrain = PlayerLocomotive.Train;
                    if (playerTrain != null)
                    {
                        if (playerTrain.SpeedMpS < -0.1 || playerTrain.SpeedMpS > 0.1)
                        {
                            Confirmer.Message(ConfirmLevel.Warning, Catalog.GetString("To return to static train speed must be = 0"));
                            TrainSwitcher.SuspendOldPlayer = false;
                            TrainSwitcher.ClickedSelectedAsPlayer = false;
                            return;
                        }
                        if (playerTrain.ValidRoute[0] != null && playerTrain.ValidRoute[0].Count > playerTrain.PresentPosition[Direction.Forward].RouteListIndex + 1)
                            SignalEnvironment.BreakDownRoute(playerTrain.ValidRoute[0][playerTrain.PresentPosition[Direction.Forward].RouteListIndex + 1].TrackCircuitSection.Index,
                            playerTrain.RoutedForward);
                        if (playerTrain.ValidRoute[1] != null && playerTrain.ValidRoute[1].Count > playerTrain.PresentPosition[Direction.Backward].RouteListIndex + 1)
                            SignalEnvironment.BreakDownRoute(playerTrain.ValidRoute[1][playerTrain.PresentPosition[Direction.Backward].RouteListIndex + 1].TrackCircuitSection.Index,
                            playerTrain.RoutedBackward);
                        playerTrain.ControlMode = TrainControlMode.Undefined;
                        playerTrain.TrainType = TrainType.Static;
                        playerTrain.SpeedMpS = 0;
                        foreach (TrainCar car in playerTrain.Cars) car.SpeedMpS = 0;
                        playerTrain.CheckFreight();
                        playerTrain.InitializeBrakes();
                    }
                }
                var oldPlayerTrain = PlayerLocomotive.Train;
                if (selectedAsPlayer.TrainType != TrainType.Static)
                {
                    var playerTrain = selectedAsPlayer as AITrain;
                    if (!(playerTrain.TrainType == TrainType.AiIncorporated && playerTrain.IncorporatingTrain == PlayerLocomotive.Train))
                    {
                        PlayerLocomotive = SetPlayerLocomotive(playerTrain);
                        if (oldPlayerTrain != null) oldPlayerTrain.LeadLocomotiveIndex = -1;
                    }

                }
                else
                {
                    Physics.Train pathlessPlayerTrain = selectedAsPlayer;
                    pathlessPlayerTrain.IsPathless = true;
                    PlayerLocomotive = SetPlayerLocomotive(pathlessPlayerTrain);
                    if (oldPlayerTrain != null) oldPlayerTrain.LeadLocomotiveIndex = -1;
                }
                playerSwitchOngoing = true;
                if (MPManager.IsMultiPlayer())
                {
                    MPManager.Notify((new MSGPlayerTrainSw(MPManager.GetUserName(), PlayerLocomotive.Train, PlayerLocomotive.Train.Number, oldTrainReverseFormation, newTrainReverseFormation)).ToString());
                }

            }
            else
            {
                TrainSwitcher.ClickedSelectedAsPlayer = false;
                AI.aiListChanged = true;
            }
        }

        private void CompleteSwitchPlayerTrain()
        {
            if (PlayerLocomotive.Train.TrainType != TrainType.Static)
            {
                AI.AITrains.Remove(PlayerLocomotive.Train as AITrain);
                if ((PlayerLocomotive.Train as AITrain).MovementState == AiMovementState.Suspended)
                {
                    PlayerLocomotive.Train.Reinitialize();
                    (PlayerLocomotive.Train as AITrain).MovementState = Math.Abs(PlayerLocomotive.Train.SpeedMpS) <= MaxStoppedMpS ?
                        AiMovementState.Init : AiMovementState.Braking;
                }
                (PlayerLocomotive.Train as AITrain).SwitchToPlayerControl();
            }
            else
            {
                PlayerLocomotive.Train.CreatePathlessPlayerTrain();
            }
            OnPlayerLocomotiveChanged();
            playerSwitchOngoing = false;
            TrainSwitcher.ClickedSelectedAsPlayer = false;
            AI.aiListChanged = true;
        }

        /// <summary>
        /// Finds train to restart
        /// </summary>
        public void RestartWaitingTrain(RestartWaitingTrain restartWaitingTrain)
        {
            AITrain trainToRestart = null;
            foreach (var train in TrainDictionary.Values)
            {
                if (train is AITrain && train.Name.ToLower() == restartWaitingTrain.WaitingTrainToRestart.ToLower())
                {
                    if (restartWaitingTrain.WaitingTrainStartingTime == -1 || (train is AITrain && restartWaitingTrain.WaitingTrainStartingTime == ((AITrain)train).StartTime))
                    {
                        trainToRestart = (AITrain)train;
                        trainToRestart.RestartWaitingTrain(restartWaitingTrain);
                        return;
                    }
                }
            }
            if (trainToRestart == null)
                Trace.TraceWarning("Train {0} to restart not found", restartWaitingTrain.WaitingTrainToRestart);            
        }

 

        /// <summary>
        /// Derive log-file name from route path and activity name
        /// </summary>

        public string DeriveLogFile(string appendix)
        {
            const int maxLogFiles = 2;
            StringBuilder logfile = new StringBuilder();

            logfile.Append(RouteName);

            logfile.Append(string.IsNullOrEmpty(ActivityFileName) ? "_explorer" : "_" + ActivityFileName);

            logfile.Append(appendix);

            string logfileName = Path.Combine(UserSettings.UserDataFolder, Path.ChangeExtension(logfile.ToString(), "csv"));

            int logCount = 0;

            while (File.Exists(logfileName) && logCount < maxLogFiles)
            {
                logfileName = Path.Combine(UserSettings.UserDataFolder, Path.ChangeExtension($"{logfile}{logCount:00}", "csv"));
                logCount++;
            }

            if (logCount >= maxLogFiles)
            {
                logfileName = string.Empty;
            }
            return logfileName;
        }

        /// <summary>
        /// Class TrainList extends class List<Train> with extra search methods
        /// </summary>

        public class TrainList : List<Physics.Train>
        {
            private Simulator simulator;

            /// <summary>
            /// basis constructor
            /// </summary>

            public TrainList(Simulator in_simulator)
                : base()
            {
                simulator = in_simulator;
            }

            /// <summary>
            /// Search and return TRAIN by number - any type
            /// </summary>

            public Train GetTrainByNumber(int reqNumber)
            {
                Physics.Train returnTrain = null;
                if (simulator.TrainDictionary.ContainsKey(reqNumber))
                {
                    returnTrain = simulator.TrainDictionary[reqNumber];
                }

                // check player train's original number
                if (returnTrain == null && simulator.TimetableMode && simulator.PlayerLocomotive != null)
                {
                    Physics.Train playerTrain = simulator.PlayerLocomotive.Train;
                    TTTrain TTPlayerTrain = playerTrain as TTTrain;
                    if (TTPlayerTrain.OrgAINumber == reqNumber)
                    {
                        return (playerTrain);
                    }
                }

                // dictionary is not always updated in normal activity and explorer mode, so double check
                // if not correct, search in the 'old' way
                if (returnTrain == null || returnTrain.Number != reqNumber)
                {
                    returnTrain = null;
                    for (int iTrain = 0; iTrain <= this.Count - 1; iTrain++)
                    {
                        if (this[iTrain].Number == reqNumber)
                            returnTrain = this[iTrain];
                    }
                }

                return (returnTrain);
            }

            /// <summary>
            /// Search and return Train by name - any type
            /// </summary>

            public Train GetTrainByName(string reqName)
            {
                Physics.Train returnTrain = null;
                if (simulator.NameDictionary.ContainsKey(reqName))
                {
                    returnTrain = simulator.NameDictionary[reqName];
                }

                return (returnTrain);
            }

            /// <summary>
            /// Check if numbered train is on startlist
            /// </summary>
            /// <param name="reqNumber"></param>
            /// <returns></returns>

            public Boolean CheckTrainNotStartedByNumber(int reqNumber)
            {
                return simulator.StartReference.Contains(reqNumber);
            }

            /// <summary>
            /// Search and return AITrain by number
            /// </summary>

            public AITrain GetAITrainByNumber(int reqNumber)
            {
                AITrain returnTrain = null;
                if (simulator.TrainDictionary.ContainsKey(reqNumber))
                {
                    returnTrain = simulator.TrainDictionary[reqNumber] as AITrain;
                }

                return (returnTrain);
            }

            /// <summary>
            /// Search and return AITrain by name
            /// </summary>

            public AITrain GetAITrainByName(string reqName)
            {
                AITrain returnTrain = null;
                if (simulator.NameDictionary.ContainsKey(reqName))
                {
                    returnTrain = simulator.NameDictionary[reqName] as AITrain;
                }

                return (returnTrain);
            }

        } // TrainList

        internal void OnAllowedSpeedRaised(Physics.Train train)
        {
            AllowedSpeedRaised?.Invoke(train, EventArgs.Empty);
        }

        internal void OnPlayerLocomotiveChanged()
        {
            PlayerLocomotiveChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void OnPlayerTrainChanged(Physics.Train oldTrain, Physics.Train newTrain)
        {
            var eventArgs = new PlayerTrainChangedEventArgs(oldTrain, newTrain);
            PlayerTrainChanged?.Invoke(this, eventArgs);
        }

        internal void OnRequestTTDetachWindow()
        {
            var requestTTDetachWindow = RequestTTDetachWindow;
            requestTTDetachWindow(this, EventArgs.Empty);
        }

        private bool OnQueryCarViewerLoaded(TrainCar car)
        {
            var query = new QueryCarViewerLoadedEventArgs(car);
            QueryCarViewerLoaded?.Invoke(this, query);
            return query.Loaded;
        }

        // Override User settings with activity creator settings if present in INCLUDE file
        public void OverrideUserSettings(UserSettings setting, ORActivity activitySettings)
        {
            if (activitySettings.IsActivityOverride)
            {
                Trace.Write("\n------------------------------------------------------------------------------------------------");
                Trace.Write("\nThe following Option settings have been temporarily set by this activity (no permanent changes have been made to your settings):");

                // General TAB 

                if (activitySettings.Options.RetainersOnAllCars == 1)
                {
                    setting.RetainersOnAllCars = true;
                    Trace.Write("\nRetainers on all cars            =   True");
                }
                else if (activitySettings.Options.RetainersOnAllCars == 0)
                {
                    setting.RetainersOnAllCars = false;
                    Trace.Write("\nRetainers on all cars            =   True");
                }

                if (activitySettings.Options.GraduatedBrakeRelease == 1)
                {
                    setting.GraduatedRelease = true;
                    Trace.Write("\nGraduated Brake Release          =   True");
                }
                else if (activitySettings.Options.GraduatedBrakeRelease == 0)
                {
                    setting.GraduatedRelease = false;
                    Trace.Write("\nGraduated Brake Release          =   False");
                }

                if (activitySettings.Options.ViewDispatcherWindow == 1)
                {
                    setting.ViewDispatcher = true;
                    Trace.Write("\nView Dispatch Window             =   True");
                }
                else if (activitySettings.Options.ViewDispatcherWindow == 0)
                {
                    setting.ViewDispatcher = false;
                    Trace.Write("\nView Dispatch Window             =   False");
                }

                if (activitySettings.Options.SoundSpeedControl == 1)
                {
                    setting.SpeedControl = true;
                    Trace.Write("\nSound speed control              =   True");
                }
                else if (activitySettings.Options.SoundSpeedControl == 0)
                {
                    setting.SpeedControl = false;
                    Trace.Write("\nSound speed control              =   True");
                }

                // Video TAB
                if (activitySettings.Options.FastFullScreenAltTab == 1)
                {
                    setting.FastFullScreenAltTab = true;
                    Trace.Write("\nFast Full Screen Alt TAB         =   True");
                }
                else if (activitySettings.Options.FastFullScreenAltTab == 0)
                {
                    setting.FastFullScreenAltTab = false;
                    Trace.Write("\nFast Full Screen Alt TAB         =   False");
                }


                // Simulation TAB
                if (activitySettings.Options.ForcedRedAtStationStops == 1)
                {
                    setting.NoForcedRedAtStationStops = false; // Note this parameter is reversed in its logic to others.
                    Trace.Write("\nForced Red at Station Stops      =   True");
                }
                else if (activitySettings.Options.ForcedRedAtStationStops == 0)
                {
                    setting.NoForcedRedAtStationStops = true; // Note this parameter is reversed in its logic to others.
                    Trace.Write("\nForced Red at Station Stops      =   False");
                }


                if (activitySettings.Options.UseLocationPassingPaths == 1)
                {
                    setting.UseLocationPassingPaths = true;
                    Trace.Write("\nLocation Based Passing Paths     =   True");
                }
                else if (activitySettings.Options.UseLocationPassingPaths == 0)
                {
                    setting.UseLocationPassingPaths = false;
                    Trace.Write("\nLocation Based Passing Paths     =   False");
                }

                if (activitySettings.Options.UseAdvancedAdhesion == 1)
                {
                    setting.UseAdvancedAdhesion = true;
                    Trace.Write("\nUse Advanced Adhesion            =   True");
                }
                else if (activitySettings.Options.UseAdvancedAdhesion == 0)
                {
                    setting.UseAdvancedAdhesion = false;
                    Trace.Write("\nUse Advanced Adhesion            =   False");
                }

                if (activitySettings.Options.BreakCouplers == 1)
                {
                    setting.BreakCouplers = true;
                    Trace.Write("\nBreak Couplers                   =   True");
                }
                else if (activitySettings.Options.BreakCouplers == 0)
                {
                    setting.BreakCouplers = false;
                    Trace.Write("\nBreak Couplers                   =   False");
                }

                if (activitySettings.Options.CurveResistanceDependent == 1)
                {
                    setting.CurveResistanceDependent = true;
                    Trace.Write("\nCurve Resistance Dependent       =   True");
                }
                else if (activitySettings.Options.CurveResistanceDependent == 0)
                {
                    setting.CurveResistanceDependent = false;
                    Trace.Write("\nCurve Resistance Dependent       =   False");
                }

                if (activitySettings.Options.CurveSpeedDependent == 1)
                {
                    setting.CurveSpeedDependent = true;
                    Trace.Write("\nCurve Speed Dependent            =   True");
                }
                else if (activitySettings.Options.CurveSpeedDependent == 1)
                {
                    setting.CurveSpeedDependent = false;
                    Trace.Write("\nCurve Speed Dependent            =   False");
                }

                if (activitySettings.Options.TunnelResistanceDependent == 1)
                {
                    setting.TunnelResistanceDependent = true;
                    Trace.Write("\nTunnel Resistance Dependent      =   True");
                }
                else if (activitySettings.Options.TunnelResistanceDependent == 0)
                {
                    setting.TunnelResistanceDependent = false;
                    Trace.Write("\nTunnel Resistance Dependent      =   False");
                }

                if (activitySettings.Options.WindResistanceDependent == 1)
                {
                    setting.WindResistanceDependent = true;
                    Trace.Write("\nWind Resistance Dependent        =   True");
                }
                else if (activitySettings.Options.WindResistanceDependent == 0)
                {
                    setting.WindResistanceDependent = false;
                    Trace.Write("\nWind Resistance Dependent        =   False");
                }

                if (activitySettings.Options.HotStart == 1)
                {
                    setting.HotStart = true;
                    Trace.Write("\nHot Start                        =   True");
                }
                else if (activitySettings.Options.HotStart == 0)
                {
                    setting.HotStart = false;
                    Trace.Write("\nHot Start                        =   False");
                }
                if (activitySettings.Options.SimpleControlPhysics == 1)
                {
                    Trace.Write("\nSimple Control/Physics                        =   Not Active");
                }
                else if (activitySettings.Options.SimpleControlPhysics == 0)
                {
                    Trace.Write("\nSimple Control/Physics                        =   Not Active");
                }

                // Data Logger TAB
                if (activitySettings.Options.VerboseConfigurationMessages == 1)
                {
                    Trace.Write("\nVerbose Configuration Messages               =   Not Active");
                }
                else if (activitySettings.Options.VerboseConfigurationMessages == 0)
                {
                    Trace.Write("\nVerbose Configuration Messages                        =   Not Active");
                }

                // Experimental TAB
                if (activitySettings.Options.UseLocationPassingPaths == 1)
                {
                    setting.UseLocationPassingPaths = true;
                    Trace.Write("\nLocation Linked Passing Paths    =   True");
                }
                else if (activitySettings.Options.UseLocationPassingPaths == 0)
                {
                    setting.UseLocationPassingPaths = false;
                    Trace.Write("\nLocation Linked Passing Paths    =   False");
                }

                if (activitySettings.Options.AdhesionFactor > 0)
                {
                    setting.AdhesionFactor = activitySettings.Options.AdhesionFactor;
                    setting.AdhesionFactor = MathHelper.Clamp(setting.AdhesionFactor, 10, 200);
                    Trace.Write("\nAdhesion Factor Correction       =   " + setting.AdhesionFactor.ToString());
                }

                if (activitySettings.Options.AdhesionFactorChange > 0)
                {
                    setting.AdhesionFactorChange = activitySettings.Options.AdhesionFactorChange;
                    setting.AdhesionFactorChange = MathHelper.Clamp(setting.AdhesionFactorChange, 0, 100);
                    Trace.Write("\nAdhesion Factor Change           =   " + setting.AdhesionFactorChange.ToString());
                }

                if (activitySettings.Options.AdhesionProportionalToWeather == 1)
                {
                    setting.AdhesionProportionalToWeather = true;
                    Trace.Write("\nAdhesion Proportional to Weather =   True");
                }
                else if (activitySettings.Options.AdhesionProportionalToWeather == 0)
                {
                    setting.AdhesionProportionalToWeather = true;
                    Trace.Write("\nAdhesion Proportional to Weather =   False");
                }

                if (activitySettings.Options.ActivityRandomization > 0)
                {
                    setting.ActRandomizationLevel = activitySettings.Options.ActivityRandomization;
                    setting.ActRandomizationLevel = MathHelper.Clamp(setting.ActRandomizationLevel, 0, 3);
                    Trace.Write("\nActivity Randomization           =   " + setting.ActRandomizationLevel.ToString());
                }

                if (activitySettings.Options.ActivityWeatherRandomization > 0)
                {
                    setting.ActWeatherRandomizationLevel = activitySettings.Options.ActivityWeatherRandomization;
                    setting.ActWeatherRandomizationLevel = MathHelper.Clamp(setting.ActWeatherRandomizationLevel, 0, 3);
                    Trace.Write("\nActivity Weather Randomization   =   " + setting.ActWeatherRandomizationLevel.ToString());
                }

                if (activitySettings.Options.SuperElevationLevel > 0)
                {
                    setting.UseSuperElevation = activitySettings.Options.SuperElevationLevel;
                    setting.UseSuperElevation = MathHelper.Clamp(setting.UseSuperElevation, 0, 10);
                    Trace.Write("\nSuper elevation - level          =   " + setting.UseSuperElevation.ToString());
                }

                if (activitySettings.Options.SuperElevationMinimumLength > 0)
                {
                    setting.SuperElevationMinLen = activitySettings.Options.SuperElevationMinimumLength;
                    setting.SuperElevationMinLen = MathHelper.Clamp(setting.SuperElevationMinLen, 50, 1000000);
                    Trace.Write("\nSuper elevation - minimum length =   " + setting.SuperElevationMinLen.ToString());
                }

                if (activitySettings.Options.SuperElevationGauge > 0)
                {
                    setting.SuperElevationGauge = activitySettings.Options.SuperElevationGauge;
                    setting.SuperElevationGauge = MathHelper.Clamp(setting.SuperElevationGauge, 300, 2500);
                    Trace.Write("\nSuper elevation - gauge          =   " + setting.SuperElevationGauge.ToString());
                }


                Trace.Write("\n------------------------------------------------------------------------------------------------");

            }
        }

        public void InitializeAiPlayerHosting()
        {
            Trains[0].LeadLocomotive = null;
            Trains[0].LeadLocomotiveIndex = -1;
        }
    } // Simulator
}
