// COPYRIGHT 2013 by the Open Rails project.
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

/* TRAINS
 * 
 * Contains code to represent a train as a list of TrainCars and to handle the physics of moving
 * the train through the Track Database.
 * 
 * A train has:
 *  - a list of TrainCars 
 *  - a front and back position in the TDB ( represented by TDBTravellers )
 *  - speed
 *  - MU signals that are relayed from player locomtive to other locomotives and cars such as:
 *      - direction
 *      - throttle percent
 *      - brake percent  ( TODO, this should be changed to brake pipe pressure )
 *      
 *  Individual TrainCars provide information on friction and motive force they are generating.
 *  This is consolidated by the train class into overall movement for the train.
 */

// Compiler flags for debug print-out facilities
// #define DEBUG_SIGNALPASS

// Debug Calculation of Aux Tender operation
// #define DEBUG_AUXTENDER

// Debug for calculation of speed forces
// #define DEBUG_SPEED_FORCES

// Debug for calculation of Advanced coupler forces
// #define DEBUG_COUPLER_FORCES

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Xna.Framework;

using Orts.Common;
using Orts.Common.Calc;
using Orts.Common.Position;
using Orts.Common.Xna;
using Orts.Formats.Msts;
using Orts.Formats.Msts.Models;
using Orts.MultiPlayer;
using Orts.Simulation.AIs;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems.Brakes;
using Orts.Simulation.Signalling;
using Orts.Simulation.Track;

namespace Orts.Simulation.Physics
{
    public partial class Train
    {
        #region const
        private const int TileSize = 2048;
        protected const float InitialThrottlepercent = 25; // initial value of throttle when train starts activity at speed > 0

        internal const float ShortClearingDistanceM = 15.0f;     // clearing distance for short trains in activities
        internal const float StandardClearingDistanceM = 30.0f;  // standard clearing distance for trains in activities
        internal const int StandardTrainMinCarNo = 10;           // Minimum number of cars for a train to have standard clearing distance

        private const float EmissivityFactor = 0.79f; // Oxidised steel
        private const float OneAtmospherePSI = 14.696f;      // Atmospheric Pressure
        private const float PipeHeatTransCoeffWpM2K = 22.0f;    // heat transmission coefficient for a steel pipe.
        private const float BoltzmanConstPipeWpM2 = 0.0000000567f; // Boltzman's Constant

        private const float SpecificHeatCapcityAirKJpKgK = 1.006f; // Specific Heat Capacity of Air
        private const float DensityAirKgpM3 = 1.247f;   // Density of air - use a av value

        private const float RearPositionOverlap = 25.0f;       // allowed overlap when slipping
        private const float StandardWaitTimeS = 60.0f;         // wait for 1 min before claim state

        private const float BackwardThreshold = 20;            // counter threshold to detect backward move

        internal const float StandardOverlapM = 15.0f;           // standard overlap on clearing sections
        internal const float JunctionOverlapM = 75.0f;           // standard overlap on clearing sections

        internal const float MaxTimeS = 120;                     // check ahead for distance covered in 2 mins.
        internal const float MinCheckDistanceM = 5000;           // minimum distance to check ahead
        internal const float MinCheckDistanceManualM = 3000;     // minimum distance to check ahead in manual mode

        internal float MinCheckDistanceExplorerM => Math.Max(AllowedMaxSpeedMpS * MaxTimeS, MinCheckDistanceM);      // minimum distance to check ahead in explorer mode


        #endregion

        public List<TrainCar> Cars { get; } = new List<TrainCar>();           // listed front to back
        public int Number { get; internal set; }
        public string Name { get; internal set; }
        public static int TotalNumber { get; private set; } = 1; // start at 1 (0 is reserved for player train)

        public TrainCar FirstCar => Cars[0];
        public TrainCar LastCar => Cars[Cars.Count - 1];

        public Traveller RearTDBTraveller { get; internal set; }               // positioned at the back of the last car in the train
        public Traveller FrontTDBTraveller { get; internal set; }              // positioned at the front of the train by CalculatePositionOfCars
        public float Length { get; internal set; }                             // length of train from FrontTDBTraveller to RearTDBTraveller
        public float MassKg { get; internal set; }                             // weight of the train
        public float SpeedMpS { get; internal set; }                           // meters per second +ve forward, -ve when backing
        private float previousSpeedMpS;                              // variable to remember last speed used for projected speed
        public SmoothedData AccelerationMpSpS { get; } = new SmoothedData(); // smoothed acceleration data
        public float ProjectedSpeedMpS { get; private set; }                  // projected speed
        public float LastReportedSpeed { get; internal set; }

        internal Train UncoupledFrom { get; set; }                      // train not to coupled back onto
        public float TotalCouplerSlackM { get; private set; }
        private float maximumCouplerForceN;
        public int CouplersPulled { get; private set; }     // Count of number of couplings being stretched (pulled)
        public int CouplersPushed { get; private set; }     // Count of number of couplings being compressed (pushed)

        public int LeadLocomotiveIndex { get; internal set; } = -1;
        public bool IsFreight { get; protected set; }                           // has at least one freight car
        public int PassengerCarsNumber { get; private set; }              // Number of passenger cars
        internal float SlipperySpotDistanceM { get; set; }              // distance to extra slippery part of track
        internal float SlipperySpotLengthM { get; set; }

        internal float WagonCoefficientFriction { get; set; } = 0.35f; // Initialise coefficient of Friction for wagons - 0.35 for dry rails, 0.1 - 0.25 for wet rails
        internal float LocomotiveCoefficientFriction { get; set; } = 0.35f; // Initialise coefficient of Friction for locomotives - 0.5 for dry rails, 0.1 - 0.25 for wet rails

        // These signals pass through to all cars and locomotives on the train
        public MidpointDirection MUDirection { get; internal set; } = MidpointDirection.N;      // set by player locomotive to control MU'd locomotives
        public float MUThrottlePercent { get; internal set; }                  // set by player locomotive to control MU'd locomotives
        public int MUGearboxGearIndex { get; internal set; }                   // set by player locomotive to control MU'd locomotives
        public float MUReverserPercent { get; internal set; } = 100;            // steam engine direction/cutoff control for MU'd locomotives
        public float MUDynamicBrakePercent { get; internal set; } = -1;         // dynamic brake control for MU'd locomotives, <0 for off
        internal float EqualReservoirPressurePSIorInHg { get; set; } = 90;   // Pressure in equalising reservoir - set by player locomotive - train brake pipe use this as a reference to set brake pressure levels

        // Class AirSinglePipe etc. use this property for pressure in PSI, 
        // but Class VacuumSinglePipe uses it for vacuum in InHg.
        internal float BrakeLine2PressurePSI { get; set; }              // extra line for dual line systems, main reservoir
        internal float BrakeLine3PressurePSI { get; set; }              // extra line just in case, engine brake pressure
        internal float BrakeLine4 { get; set; } = -1;                    // extra line just in case, ep brake control line. -1: release/inactive, 0: hold, 0 < value <=1: apply
        public RetainerSetting RetainerSetting { get; internal set; } = RetainerSetting.Exhaust;
        public int RetainerPercent { get; internal set; } = 100;
        public float TotalTrainBrakePipeVolumeM3 { get; internal set; } // Total volume of train brake pipe
        public float TotalTrainBrakeCylinderVolumeM3 { get; internal set; } // Total volume of train brake cylinders
        internal float TotalTrainBrakeSystemVolumeM3 { get; set; } // Total volume of train brake system
        public float TotalCurrentTrainBrakeSystemVolumeM3 { get; internal set; } // Total current volume of train brake system
        internal bool EQEquippedVacLoco { get; set; }          // Flag for locomotives fitted with vacuum brakes that have an Equalising reservoir fitted
        internal float PreviousCarCount { get; set; }                  // Keeps track of the last number of cars in the train consist (for vacuum brakes)
        internal bool TrainBPIntact { get; set; } = true;           // Flag to indicate that the train BP is not intact, ie due to disconnection or an open valve cock.

        internal int FirstCarUiD { get; set; }                          // UiD of first car in the train
        internal float HUDWagonBrakeCylinderPSI { get; set; }         // Display value for wagon HUD
        internal float HUDLocomotiveBrakeCylinderPSI { get; set; }    // Display value for locomotive HUD
        internal bool HUDBrakeSlide { get; set; }                     // Display indication for brake wheel slip
        internal bool WagonsAttached { get; set; }    // Wagons are attached to train
        internal float LeadPipePressurePSI { get; set; }       // Keeps record of Lead locomootive brake pipe pressure

        public bool IsWheelSlipWarninq { get; private set; }
        public bool IsWheelSlip { get; private set; }
        public bool IsBrakeSkid { get; private set; }

        internal bool HotBoxSetOnTrain { get; set; }

        // Carriage Steam Heating
        private bool heatedCarAttached;
        private bool heatingBoilerCarAttached;
        private bool isFirstTimeBoilerCarAttached = true;
        private float trainInsideTempC;                  // Desired inside temperature for carriage steam heating depending upon season
        public float TrainOutsideTempC { get; internal set; }                 // External ambient temeprature for carriage steam heating.
        internal bool CarSteamHeatOn { get; set; }    // Is steam heating turned on
        private bool lowSteamHeat;        // Flag to indicate when steam heat temp is low

        // Values for Wind Direction and Speed - needed for wind resistance and lateral force
        public float PhysicsWindDirectionDeg { get; private set; }
        public float PhysicsWindSpeedMpS { get; private set; }
        public float PhysicsTrainLocoDirectionDeg { get; internal set; }
        public float ResultantWindComponentDeg { get; private set; }
        public float WindResultantSpeedMpS { get; private set; }

        public static bool TrainWindResistanceDependent => simulator.Settings.WindResistanceDependent;

        // Auxiliary Water Tenders
        public float MaxAuxTenderWaterMassKG { get; set; }
        public bool IsAuxTenderCoupled { get; set; }

        //To investigate coupler breaks on route
        private bool numOfCouplerBreaksNoted;
        public static int NumOfCouplerBreaks;//Debrief Eval //TODO 20201130 this sure is bound to the instance
        public bool DbfEvalValueChanged { get; set; }//Debrief Eval

        public TrainType TrainType { get; internal set; } = TrainType.Player;

        public float? DistanceToSignal { get; protected set; }
        internal List<SignalItemInfo> SignalObjectItems { get; set; }
        private int nextSignalIndex = -1;                 // Index in SignalObjectItems for next signal
        private int nextSpeedLimitIndex = -1;             // Index in SignalObjectItems for next speedpost
        internal Signal[] NextSignalObject { get; } = new Signal[2];  // direct reference to next signal
        public Signal AllowedCallOnSignal { get; internal set; }         // Signal for which train has call on allowed by dispatcher

        // Local max speed independently from signal and speedpost speed;
        // depends from various parameters like route max speed, overall or section efficiency of service,
        // max speed of player locomotive, max speed of consist (MaxVelocityA)
        internal float TrainMaxSpeedMpS { get; set; }
        public float AllowedMaxSpeedMpS { get; internal set; }                 // Max speed as allowed
        internal float AllowedMaxSpeedSignalMpS { get; set; }           // Max speed as set by signal
        internal float AllowedMaxSpeedLimitMpS { get; set; }            // Max speed as set by limit
        private protected float allowedMaxTempSpeedLimitMpS;        // Max speed as set by temp speed limit
        private protected float allowedAbsoluteMaxSpeedSignalMpS;   // Max speed as set by signal independently from train features
        private protected float allowedAbsoluteMaxSpeedLimitMpS;    // Max speed as set by limit independently from train features
        private protected float allowedAbsoluteMaxTempSpeedLimitMpS;    // Max speed as set by temp speed limit independently from train features

        internal TrackCircuitRoutePath TCRoute;                      // train path converted to TC base
        public TrackCircuitPartialPathRoute[] ValidRoute { get; } = new TrackCircuitPartialPathRoute[2] { null, null };  // actual valid path
        private TrackCircuitPartialPathRoute manualTrainRoute;     // partial route under train for Manual mode
        internal bool ClaimState { get; set; }              // train is allowed to perform claim on sections
        internal double ActualWaitTimeS { get; set; }       // actual time waiting for signal
        private protected int movedBackward;                           // counter to detect backward move

        public List<TrackCircuitSection> OccupiedTrack { get; } = new List<TrackCircuitSection>();

        // Station Info
        internal List<int> HoldingSignals { get; } = new List<int>();// list of signals which must not be cleared (eg station stops)
        public List<StationStop> StationStops { get; } = new List<StationStop>();  //list of station stop details
        public StationStop PreviousStop { get; internal set; }                          //last stop passed
        internal bool AtStation { get; set; }               //set if train is in station
        internal bool MayDepart { get; set; }       //set if train is ready to depart
        public string DisplayMessage { get; protected set; } = string.Empty;                                //string to be displayed in station information window
        public Color DisplayColor { get; protected set; } = Color.LightGreen;                     //color for DisplayMessage
        public bool CheckStations { get; protected set; }               //used when in timetable mode to check on stations
        public TimeSpan? Delay { get; protected set; }                                    // present delay of the train (if any)

        private protected int attachTo = -1;                              // attach information : train to which to attach at end of run
        internal int IncorporatedTrainNo { get; set; } = -1;                        // number of train incorporated in actual train
        public Train IncorporatingTrain { get; internal set; }                      // train incorporating another train
        internal int IncorporatingTrainNo { get; set; } = -1;                   // number of train incorporating the actual train

        private protected ServiceTraffics trafficService;
        private EnumArray2D<int, Direction, TrackDirection> misalignedSwitch = new EnumArray2D<int, Direction, TrackDirection>(-1);  // misaligned switch indication per direction:
        // cell 0 : index of switch, cell 1 : required linked section; -1 if not valid
        public Dictionary<int, float> PassedSignalSpeeds { get; } = new Dictionary<int, float>();  // list of signals and related speeds pending processing (manual and explorer mode)
        public int[] LastPassedSignal { get; } = new int[2] { -1, -1 };  // index of last signal which set speed limit per direction (manual and explorer mode)

        // Variables used for autopilot mode and played train switching
        public bool IsActualPlayerTrain => this == simulator.PlayerLocomotive?.Train;

        internal float MaxDistanceCheckedAhead => Math.Max((IsActualPlayerTrain ? (float)simulator.TRK.Route.SpeedLimit : AllowedMaxSpeedMpS) * MaxTimeS, MinCheckDistanceM);

        public bool IsPlayerDriven => TrainType == TrainType.Player || TrainType == TrainType.AiPlayerDriven;

        public bool IsPlayable { get; private set; }
        public bool IsPathless { get; internal set; }

        // End variables used for autopilot mode and played train switching

        internal TrainRouted RoutedForward { get; set; }                 // routed train class for forward moves (used in signalling)
        internal TrainRouted RoutedBackward { get; set; }                // routed train class for backward moves (used in signalling)

        public TrainControlMode ControlMode { get; internal set; } = TrainControlMode.Undefined;     // train control mode

        public OutOfControlReason OutOfControlReason { get; private set; } = OutOfControlReason.UnDefined; // train out of control

        public EnumArray<TrackCircuitPosition, Direction> PresentPosition { get; } =
            new EnumArray<TrackCircuitPosition, Direction>(new TrackCircuitPosition[] { new TrackCircuitPosition(), new TrackCircuitPosition() });         // present position : 0 = front, 1 = rear
        public EnumArray<TrackCircuitPosition, Direction> PreviousPosition { get; } =
            new EnumArray<TrackCircuitPosition, Direction>(new TrackCircuitPosition[] { new TrackCircuitPosition(), new TrackCircuitPosition() });        // previous train position

        public float DistanceTravelledM { get; internal set; }      // actual distance travelled
        internal float ReservedTrackLengthM { get; private set; }   // lenght of reserved section

        internal float DistanceTravelled { get; set; }                                          // distance travelled, but not exactly
        private float targetSpeedMpS;                                    // target speed for remote trains; used for sound management
        internal DistanceTravelledActions requiredActions { get; } = new DistanceTravelledActions(); // distance travelled action list
        internal AuxActionsContainer AuxActionsContainer { get; } // Action To Do during activity, like WP

        internal float ActivityClearingDistanceM { get; set; } = 30.0f;        // clear distance to stopping point for activities

        private float clearanceAtRearM = -1;              // save distance behind train (when moving backward)
        private Signal rearSignalObject;            // direct reference to signal at rear (when moving backward)

        public bool IsTilting { get; internal set; }
        internal float InitialSpeed { get; set; }   // initial speed of train in activity as set in .srv file

        public double BrakingTime { get; internal set; }              // Total braking time, used to check whether brakes get stuck
        public double ContinuousBrakingTime { get; internal set; }     // Consecutive braking time, used to check whether brakes get stuck
        public double RunningTime { get; internal set; }              // Total running time, used to check whether a locomotive is partly or totally unpowered due to a fault
        public int UnpoweredLoco { get; internal set; } = -1;          // car index of unpowered loco
        public bool ColdStart { get; internal set; } = true;           // False if train is moving at game start or if game resumed

        // TODO: Replace this with an event
        public bool FormationReversed { get; set; }          // flags the execution of the ReverseFormation method (executed at reversal points)

        //TODO 20201126 next three properties should be made private, with some helper to update from external, and potentially using EnumArray
        internal EndAuthorityType[] EndAuthorityTypes { get; set; } = new EndAuthorityType[2] { EndAuthorityType.NoPathReserved, EndAuthorityType.NoPathReserved };
        internal int[] LastReservedSection { get; set; } = new int[2] { -1, -1 };         // index of furthest cleared section (for NODE control)
        internal float[] DistanceToEndNodeAuthorityM { get; set; } = new float[2];      // distance to end of authority

        internal int LoopSection { get; set; } = -1;                                    // section where route loops back onto itself

        internal bool NextRouteReady { get; set; }                             // indication to activity.cs that a reversal has taken place

        private static double lastLogTime;
        private protected bool evaluateTrainSpeed;                  // logging of train speed required
        private protected int evaluationInterval;                   // logging interval
        private protected EvaluationLogContents evaluationContent;  // logging selection
        private protected string evaluationLogFile;                 // required datalog file

        private protected static readonly Simulator simulator = Simulator.Instance;                 // reference to the simulator
        private protected static readonly char Separator = (char)simulator.Settings.DataLoggerSeparator;

        #region steam and heating
        private static readonly double desiredCompartmentAlarmTempSetpointC = Temperature.Celsius.FromF(45.0); // Alarm temperature
        private static readonly double resetCompartmentAlarmTempSetpointC = Temperature.Celsius.FromF(65.0);
        #endregion


        // For AI control of the train
        public float AITrainBrakePercent
        {
            get
            {
                return aiBrakePercent;
            }
            set
            {
                aiBrakePercent = value;
                foreach (TrainCar car in Cars)
                    car.BrakeSystem.AISetPercent(aiBrakePercent);
            }
        }
        private float aiBrakePercent;

        public float AITrainThrottlePercent
        {
            get
            {
                return MUThrottlePercent;
            }
            set
            {
                MUThrottlePercent = value;
            }
        }

        public int AITrainGearboxGearIndex
        {
            set
            {
                MUGearboxGearIndex = value;
            }
            get
            {
                return MUGearboxGearIndex;
            }
        }
        public bool AITrainDirectionForward
        {
            get
            {
                return MUDirection == MidpointDirection.Forward;
            }
            set
            {
                MUDirection = value ? MidpointDirection.Forward : MidpointDirection.Reverse;
                MUReverserPercent = value ? 100 : -100;
            }
        }

        public TrainCar LeadLocomotive
        {
            get
            {
                return LeadLocomotiveIndex >= 0 && LeadLocomotiveIndex < Cars.Count ? Cars[LeadLocomotiveIndex] : null;
            }
            internal set
            {
                LeadLocomotiveIndex = -1;
                for (int i = 0; i < Cars.Count; i++)
                    if (value == Cars[i] && value.IsDriveable)
                    {
                        LeadLocomotiveIndex = i;
                        //MSTSLocomotive lead = (MSTSLocomotive)Cars[LeadLocomotiveIndex];
                        //if (lead.EngineBrakeController != null)
                        //    lead.EngineBrakeController.UpdateEngineBrakePressure(ref BrakeLine3PressurePSI, 1000);
                    }
            }
        }

        // Get the UiD value of the first wagon - searches along train, and gets the integer UiD of the first wagon that is not an engine or tender
        public virtual int GetFirstWagonUiD()
        {
            FirstCarUiD = 0; // Initialise at zero every time routine runs
            foreach (TrainCar car in Cars)
            {
                if (car.WagonType != TrainCar.WagonTypes.Engine && car.WagonType != TrainCar.WagonTypes.Tender) // If car is not a locomotive or tender, then set UiD
                {
                    FirstCarUiD = car.UiD;
                }
                if (FirstCarUiD != 0)
                {
                    break; // If UiD has been set, then don't go any further
                }
            }
            return FirstCarUiD;
        }

        // Determine whther there are any wagons attached to the locomotive
        public virtual bool GetWagonsAttachedIndication()
        {
            WagonsAttached = false;
            foreach (TrainCar car in Cars)
            {
                // Test to see if freight or passenger wagons attached (used to set BC pressure in locomotive or wagons)
                if (car.WagonType == TrainCar.WagonTypes.Freight || car.WagonType == TrainCar.WagonTypes.Passenger)
                {
                    WagonsAttached = true;
                    break;
                }
                else
                {
                    WagonsAttached = false;
                }
            }
            return WagonsAttached;
        }

        #region .ctor
        private void Init()
        {
            allowedAbsoluteMaxSpeedSignalMpS = (float)simulator.TRK.Route.SpeedLimit;
            allowedAbsoluteMaxSpeedLimitMpS = allowedAbsoluteMaxSpeedSignalMpS;
            allowedAbsoluteMaxTempSpeedLimitMpS = allowedAbsoluteMaxSpeedSignalMpS;
        }

        // Constructor
        public Train()
        {
            Init();

            if (simulator.IsAutopilotMode && TotalNumber == 1 && simulator.TrainDictionary.Count == 0)
                TotalNumber = 0; //The autopiloted train has number 0

            Number = TotalNumber;
            TotalNumber++;
            SignalObjectItems = new List<SignalItemInfo>();
            Name = string.Empty;

            RoutedForward = new TrainRouted(this, 0);
            RoutedBackward = new TrainRouted(this, 1);
            AuxActionsContainer = new AuxActionsContainer(this);
        }

        // Constructor for Dummy entries used on restore
        // Signals is restored before Trains, links are restored by Simulator
        public Train(int number)
        {
            Init();
            Number = number;
            RoutedForward = new TrainRouted(this, 0);
            RoutedBackward = new TrainRouted(this, 1);
            AuxActionsContainer = new AuxActionsContainer(this);
        }

        // Constructor for uncoupled trains
        // copy path info etc. from original train
        public Train(Train source)
        {
            if (null == source)
                throw new ArgumentNullException(nameof(source));

            Init();
            Number = TotalNumber;
            Name = $"{source.Name}{TotalNumber}";
            TotalNumber++;
            SignalObjectItems = new List<SignalItemInfo>();

            AuxActionsContainer = new AuxActionsContainer(this);
            if (source.trafficService != null)
            {
                trafficService = new ServiceTraffics(source.trafficService.Time);

                foreach (ServiceTrafficItem thisTrafficItem in source.trafficService)
                {
                    trafficService.Add(thisTrafficItem);
                }
            }

            if (source.TCRoute != null)
            {
                TCRoute = new TrackCircuitRoutePath(source.TCRoute);
            }

            ValidRoute[0] = source.ValidRoute[0] != null ? new TrackCircuitPartialPathRoute(source.ValidRoute[0]) : null;
            ValidRoute[1] = source.ValidRoute[1] != null ? new TrackCircuitPartialPathRoute(source.ValidRoute[1]) : null;

            DistanceTravelledM = source.DistanceTravelledM;

            if (source.requiredActions.Count > 0)
            {
                requiredActions = source.requiredActions.Copy();
            }

            RoutedForward = new TrainRouted(this, 0);
            RoutedBackward = new TrainRouted(this, 1);

            ControlMode = source.ControlMode;

            AllowedMaxSpeedMpS = source.AllowedMaxSpeedMpS;
            AllowedMaxSpeedLimitMpS = source.AllowedMaxSpeedLimitMpS;
            AllowedMaxSpeedSignalMpS = source.AllowedMaxSpeedSignalMpS;
            allowedAbsoluteMaxSpeedLimitMpS = source.allowedAbsoluteMaxSpeedLimitMpS;
            allowedAbsoluteMaxSpeedSignalMpS = source.allowedAbsoluteMaxSpeedSignalMpS;

            if (source.StationStops != null)
            {
                foreach (StationStop stationStop in source.StationStops)
                {
                    StationStops.Add(stationStop.CreateCopy());
                }
            }
            else
            {
                StationStops = null;
            }
        }

        /// Restore
        public Train(BinaryReader inf)
        {
            if (null == inf)
                throw new ArgumentNullException(nameof(inf));
            Init();

            RoutedForward = new TrainRouted(this, 0);
            RoutedBackward = new TrainRouted(this, 1);
            ColdStart = false;
            RestoreCars(inf);
            Number = inf.ReadInt32();
            TotalNumber = Math.Max(Number + 1, TotalNumber);
            Name = inf.ReadString();
            SpeedMpS = previousSpeedMpS = inf.ReadSingle();
            AccelerationMpSpS.Preset(inf.ReadSingle());
            TrainType = (TrainType)inf.ReadInt32();
            if (TrainType == TrainType.Static)
                ColdStart = true;
            MUDirection = (MidpointDirection)inf.ReadInt32();
            MUThrottlePercent = inf.ReadSingle();
            MUGearboxGearIndex = inf.ReadInt32();
            MUDynamicBrakePercent = inf.ReadSingle();
            EqualReservoirPressurePSIorInHg = inf.ReadSingle();
            BrakeLine2PressurePSI = inf.ReadSingle();
            BrakeLine3PressurePSI = inf.ReadSingle();
            BrakeLine4 = inf.ReadSingle();
            aiBrakePercent = inf.ReadSingle();
            LeadLocomotiveIndex = inf.ReadInt32();
            RetainerSetting = (RetainerSetting)inf.ReadInt32();
            RetainerPercent = inf.ReadInt32();
            RearTDBTraveller = new Traveller(simulator.TSectionDat, simulator.TDB.TrackDB.TrackNodes, inf);
            SlipperySpotDistanceM = inf.ReadSingle();
            SlipperySpotLengthM = inf.ReadSingle();
            TrainMaxSpeedMpS = inf.ReadSingle();
            AllowedMaxSpeedMpS = inf.ReadSingle();
            AllowedMaxSpeedSignalMpS = inf.ReadSingle();
            AllowedMaxSpeedLimitMpS = inf.ReadSingle();
            allowedMaxTempSpeedLimitMpS = inf.ReadSingle();
            allowedAbsoluteMaxSpeedSignalMpS = inf.ReadSingle();
            allowedAbsoluteMaxSpeedLimitMpS = inf.ReadSingle();
            allowedAbsoluteMaxTempSpeedLimitMpS = inf.ReadSingle();
            BrakingTime = inf.ReadDouble();
            ContinuousBrakingTime = inf.ReadDouble();
            RunningTime = inf.ReadDouble();
            IncorporatedTrainNo = inf.ReadInt32();
            IncorporatingTrainNo = inf.ReadInt32();
            IsAuxTenderCoupled = inf.ReadBoolean();
            if (IncorporatedTrainNo > -1)
            {
                Train train = GetOtherTrainByNumber(IncorporatedTrainNo);
                if (train != null)
                {
                    train.IncorporatingTrain = this;
                    train.IncorporatingTrainNo = Number;
                }
            }
            if (IncorporatingTrainNo > -1)
            {
                Train train = GetOtherTrainByNumber(IncorporatingTrainNo);
                if (train != null)
                {
                    IncorporatingTrain = train;
                }
            }
            CheckFreight();


            SignalObjectItems = new List<SignalItemInfo>();

            TrainType = (TrainType)inf.ReadInt32();
            IsTilting = inf.ReadBoolean();
            ClaimState = inf.ReadBoolean();
            evaluateTrainSpeed = inf.ReadBoolean();
            evaluationInterval = inf.ReadInt32();
            evaluationContent = (EvaluationLogContents)inf.ReadInt32();

            int dsfile = inf.ReadInt32();
            if (dsfile < 0)
            {
                evaluationLogFile = string.Empty;
            }
            else
            {
                evaluationLogFile = inf.ReadString();
            }

            TCRoute = null;
            bool routeAvailable = inf.ReadBoolean();
            if (routeAvailable)
            {
                TCRoute = new TrackCircuitRoutePath(inf);
            }

            ValidRoute[0] = null;
            bool validRouteAvailable = inf.ReadBoolean();
            if (validRouteAvailable)
            {
                ValidRoute[0] = new TrackCircuitPartialPathRoute(inf);
            }

            ValidRoute[1] = null;
            validRouteAvailable = inf.ReadBoolean();
            if (validRouteAvailable)
            {
                ValidRoute[1] = new TrackCircuitPartialPathRoute(inf);
            }

            int count = inf.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                OccupiedTrack.Add(TrackCircuitSection.TrackCircuitList[inf.ReadInt32()]);
            }

            count = inf.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                HoldingSignals.Add(inf.ReadInt32());
            }

            count = inf.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                StationStops.Add(new StationStop(inf));
            }

            count = inf.ReadInt32();
            if (count >= 0)
            {
                PreviousStop = new StationStop(inf);
            }
            else
            {
                PreviousStop = null;
            }

            AtStation = inf.ReadBoolean();
            MayDepart = inf.ReadBoolean();
            CheckStations = inf.ReadBoolean();
            attachTo = inf.ReadInt32();

            DisplayMessage = inf.ReadString();

            int delaySeconds = inf.ReadInt32();
            if (delaySeconds < 0) // delay value (in seconds, as integer)
            {
                Delay = null;
            }
            else
            {
                Delay = TimeSpan.FromSeconds(delaySeconds);
            }

            count = inf.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int passedSignalKey = inf.ReadInt32();
                float passedSignalValue = inf.ReadSingle();
                PassedSignalSpeeds.Add(passedSignalKey, passedSignalValue);
            }
            LastPassedSignal[0] = inf.ReadInt32();
            LastPassedSignal[1] = inf.ReadInt32();

            bool trafficServiceAvailable = inf.ReadBoolean();
            if (trafficServiceAvailable)
            {
                trafficService = RestoreTrafficSDefinition(inf);
            }

            ControlMode = (TrainControlMode)inf.ReadInt32();
            OutOfControlReason = (OutOfControlReason)inf.ReadInt32();
            EndAuthorityTypes[0] = (EndAuthorityType)inf.ReadInt32();
            EndAuthorityTypes[1] = (EndAuthorityType)inf.ReadInt32();
            LastReservedSection[0] = inf.ReadInt32();
            LastReservedSection[1] = inf.ReadInt32();
            LoopSection = inf.ReadInt32();
            DistanceToEndNodeAuthorityM[0] = inf.ReadSingle();
            DistanceToEndNodeAuthorityM[1] = inf.ReadSingle();

            if (TrainType != TrainType.AiNotStarted && TrainType != TrainType.AiAutoGenerated)
            {
                CalculatePositionOfCars();

                DistanceTravelledM = inf.ReadSingle();
                PresentPosition[Direction.Forward] = new TrackCircuitPosition();
                PresentPosition[Direction.Forward].RestorePresentPosition(inf, this);
                PresentPosition[Direction.Backward] = new TrackCircuitPosition();
                PresentPosition[Direction.Backward].RestorePresentRear(inf, this);
                PreviousPosition[Direction.Forward] = new TrackCircuitPosition();
                PreviousPosition[Direction.Forward].RestorePreviousPosition(inf);

                PresentPosition[Direction.Forward].RouteListIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0);
                PresentPosition[Direction.Backward].RouteListIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Backward].TrackCircuitSectionIndex, 0);
            }
            else
            {
                DistanceTravelledM = inf.ReadSingle();
                PresentPosition[Direction.Forward] = new TrackCircuitPosition();
                PresentPosition[Direction.Forward].RestorePresentPositionDummy(inf);
                PresentPosition[Direction.Backward] = new TrackCircuitPosition();
                PresentPosition[Direction.Backward].RestorePresentRearDummy(inf);
                PreviousPosition[Direction.Forward] = new TrackCircuitPosition();
                PreviousPosition[Direction.Forward].RestorePreviousPositionDummy(inf);
            }
            DistanceTravelled = DistanceTravelledM;
            count = inf.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int actionType = inf.ReadInt32();
                switch (actionType)
                {
                    case 1:
                        ActivateSpeedLimit speedLimit = new ActivateSpeedLimit(inf);
                        requiredActions.InsertAction(speedLimit);
                        break;
                    case 2:
                        ClearSectionItem clearSection = new ClearSectionItem(inf);
                        requiredActions.InsertAction(clearSection);
                        break;
                    case 3:
                        AIActionItem actionItem = new AIActionItem(inf);
                        requiredActions.InsertAction(actionItem);
                        break;
                    case 4:
                        AuxActionItem auxAction = new AuxActionItem(inf);
                        requiredActions.InsertAction(auxAction);
                        Trace.TraceWarning("DistanceTravelledItem type 4 restored as AuxActionItem");
                        break;
                    default:
                        Trace.TraceWarning($"Unknown type of DistanceTravelledItem (type {actionType}");
                        break;
                }
            }

            AuxActionsContainer = new AuxActionsContainer(this, inf, simulator.RoutePath);
            RestoreDeadlockInfo(inf);

            InitialSpeed = inf.ReadSingle();
            IsPathless = inf.ReadBoolean();

            if (TrainType != TrainType.Remote)
            {
                // restore leadlocomotive
                if (LeadLocomotiveIndex >= 0)
                {
                    LeadLocomotive = Cars[LeadLocomotiveIndex];
                    if (TrainType != TrainType.Static)
                        simulator.PlayerLocomotive = LeadLocomotive;
                }

                // restore logfile
                if (evaluateTrainSpeed)
                {
                    CreateLogFile();
                }
            }
        }
        #endregion

        private void RestoreCars(BinaryReader inf)
        {
            int count = inf.ReadInt32();
            if (count > 0)
            {
                for (int i = 0; i < count; ++i)
                    Cars.Add(RollingStock.Restore(simulator, inf, this));
            }
        }

        private static ServiceTraffics RestoreTrafficSDefinition(BinaryReader inf)
        {
            ServiceTraffics serviceDefinition = new ServiceTraffics(inf.ReadInt32());

            int totalTrafficItems = inf.ReadInt32();

            for (int i = 0; i < totalTrafficItems; i++)
            {
                ServiceTrafficItem trafficItem = new ServiceTrafficItem(inf.ReadInt32(), inf.ReadInt32(), 0, inf.ReadSingle(), inf.ReadInt32());
                serviceDefinition.Add(trafficItem);
            }

            return serviceDefinition;
        }

        private void RestoreDeadlockInfo(BinaryReader inf)
        {
            int totalDeadlock = inf.ReadInt32();
            for (int iDeadlockList = 0; iDeadlockList < totalDeadlock; iDeadlockList++)
            {
                int deadlockListKey = inf.ReadInt32();
                int deadlockListLength = inf.ReadInt32();

                List<Dictionary<int, int>> thisDeadlockList = new List<Dictionary<int, int>>();

                for (int iDeadlock = 0; iDeadlock < deadlockListLength; iDeadlock++)
                {
                    int deadlockInfoLength = inf.ReadInt32();
                    Dictionary<int, int> thisDeadlockDetails = new Dictionary<int, int>();

                    for (int iDeadlockDetails = 0; iDeadlockDetails < deadlockInfoLength; iDeadlockDetails++)
                    {
                        int deadlockKey = inf.ReadInt32();
                        int deadlockValue = inf.ReadInt32();

                        thisDeadlockDetails.Add(deadlockKey, deadlockValue);
                    }

                    thisDeadlockList.Add(thisDeadlockDetails);
                }
                DeadlockInfo.Add(deadlockListKey, thisDeadlockList);
            }
        }


        /// save game state
        public virtual void Save(BinaryWriter outf)
        {
            if (null == outf)
                throw new ArgumentNullException(nameof(outf));

            SaveCars(outf);
            outf.Write(Number);
            outf.Write(Name);
            outf.Write(SpeedMpS);
            outf.Write((float)AccelerationMpSpS.SmoothedValue);
            outf.Write((int)TrainType);
            outf.Write((int)MUDirection);
            outf.Write(MUThrottlePercent);
            outf.Write(MUGearboxGearIndex);
            outf.Write(MUDynamicBrakePercent);
            outf.Write(EqualReservoirPressurePSIorInHg);
            outf.Write(BrakeLine2PressurePSI);
            outf.Write(BrakeLine3PressurePSI);
            outf.Write(BrakeLine4);
            outf.Write(aiBrakePercent);
            outf.Write(LeadLocomotiveIndex);
            outf.Write((int)RetainerSetting);
            outf.Write(RetainerPercent);
            RearTDBTraveller.Save(outf);
            outf.Write(SlipperySpotDistanceM);
            outf.Write(SlipperySpotLengthM);
            outf.Write(TrainMaxSpeedMpS);
            outf.Write(AllowedMaxSpeedMpS);
            outf.Write(AllowedMaxSpeedSignalMpS);
            outf.Write(AllowedMaxSpeedLimitMpS);
            outf.Write(allowedMaxTempSpeedLimitMpS);
            outf.Write(allowedAbsoluteMaxSpeedSignalMpS);
            outf.Write(allowedAbsoluteMaxSpeedLimitMpS);
            outf.Write(allowedAbsoluteMaxTempSpeedLimitMpS);
            outf.Write(BrakingTime);
            outf.Write(ContinuousBrakingTime);
            outf.Write(RunningTime);
            outf.Write(IncorporatedTrainNo);
            outf.Write(IncorporatingTrainNo);
            outf.Write(IsAuxTenderCoupled);

            outf.Write((int)TrainType);
            outf.Write(IsTilting);
            outf.Write(ClaimState);
            outf.Write(evaluateTrainSpeed);
            outf.Write(evaluationInterval);

            outf.Write((int)evaluationContent);

            if (string.IsNullOrEmpty(evaluationLogFile))
            {
                outf.Write(-1);
            }
            else
            {
                outf.Write(1);
                outf.Write(evaluationLogFile);
            }

            if (TCRoute == null)
            {
                outf.Write(false);
            }
            else
            {
                outf.Write(true);
                TCRoute.Save(outf);
            }

            if (ValidRoute[0] == null)
            {
                outf.Write(false);
            }
            else
            {
                outf.Write(true);
                ValidRoute[0].Save(outf);
            }

            if (ValidRoute[1] == null)
            {
                outf.Write(false);
            }
            else
            {
                outf.Write(true);
                ValidRoute[1].Save(outf);
            }

            outf.Write(OccupiedTrack.Count);
            foreach (TrackCircuitSection thisSection in OccupiedTrack)
            {
                outf.Write(thisSection.Index);
            }

            outf.Write(HoldingSignals.Count);
            foreach (int thisHold in HoldingSignals)
            {
                outf.Write(thisHold);
            }

            outf.Write(StationStops.Count);
            foreach (StationStop thisStop in StationStops)
            {
                thisStop.Save(outf);
            }

            if (PreviousStop == null)
            {
                outf.Write(-1);
            }
            else
            {
                outf.Write(1);
                PreviousStop.Save(outf);
            }

            outf.Write(AtStation);
            outf.Write(MayDepart);
            outf.Write(CheckStations);
            outf.Write(attachTo);

            outf.Write(DisplayMessage);

            int DelaySeconds = Delay.HasValue ? (int)Delay.Value.TotalSeconds : -1;
            outf.Write(DelaySeconds);

            outf.Write(PassedSignalSpeeds.Count);
            foreach (KeyValuePair<int, float> thisPair in PassedSignalSpeeds)
            {
                outf.Write(thisPair.Key);
                outf.Write(thisPair.Value);
            }
            outf.Write(LastPassedSignal[0]);
            outf.Write(LastPassedSignal[1]);

            if (trafficService == null)
            {
                outf.Write(false);
            }
            else
            {
                outf.Write(true);
                SaveTrafficSDefinition(outf, trafficService);
            }

            outf.Write((int)ControlMode);
            outf.Write((int)OutOfControlReason);
            outf.Write((int)EndAuthorityTypes[0]);
            outf.Write((int)EndAuthorityTypes[1]);
            outf.Write(LastReservedSection[0]);
            outf.Write(LastReservedSection[1]);
            outf.Write(LoopSection);
            outf.Write(DistanceToEndNodeAuthorityM[0]);
            outf.Write(DistanceToEndNodeAuthorityM[1]);

            outf.Write(DistanceTravelledM);
            PresentPosition[Direction.Forward].Save(outf);
            PresentPosition[Direction.Backward].Save(outf);
            PreviousPosition[Direction.Forward].Save(outf);
            //  Save requiredAction, the original actions
            outf.Write(requiredActions.Count);
            foreach (DistanceTravelledItem thisAction in requiredActions)
            {
                thisAction.Save(outf);
            }
            //  Then, save the Auxiliary Action Container
            SaveAuxContainer(outf);

            SaveDeadlockInfo(outf);
            // Save initial speed
            outf.Write(InitialSpeed);
            outf.Write(IsPathless);
        }

        private void SaveCars(BinaryWriter outf)
        {
            outf.Write(Cars.Count);
            foreach (TrainCar car in Cars)
                RollingStock.Save(outf, car);
        }

        private static void SaveTrafficSDefinition(BinaryWriter outf, ServiceTraffics trafficServices)
        {
            outf.Write(trafficServices.Time);
            outf.Write(trafficServices.Count);
            foreach (ServiceTrafficItem serviceItem in trafficServices)
            {
                SaveTrafficItem(outf, serviceItem);
            }
        }

        private static void SaveTrafficItem(BinaryWriter outf, ServiceTrafficItem thisTI)
        {
            outf.Write(thisTI.ArrivalTime);
            outf.Write(thisTI.DepartTime);
            outf.Write(thisTI.DistanceDownPath);
            outf.Write(thisTI.PlatformStartID);
        }

        private void SaveDeadlockInfo(BinaryWriter outf)
        {
            outf.Write(DeadlockInfo.Count);
            foreach (KeyValuePair<int, List<Dictionary<int, int>>> deadlockInfo in DeadlockInfo)
            {
                outf.Write(deadlockInfo.Key);
                outf.Write(deadlockInfo.Value.Count);

                foreach (Dictionary<int, int> thisDeadlock in deadlockInfo.Value)
                {
                    outf.Write(thisDeadlock.Count);
                    foreach (KeyValuePair<int, int> thisDeadlockDetails in thisDeadlock)
                    {
                        outf.Write(thisDeadlockDetails.Key);
                        outf.Write(thisDeadlockDetails.Value);
                    }
                }
            }
        }

        private void SaveAuxContainer(BinaryWriter outf)
        {
            AuxActionsContainer.Save(outf, (int)simulator.ClockTime);
        }

        //================================================================================================//
        /// <summary>
        /// Changes the Lead locomotive (i.e. the loco which the player controls) to the next in the consist.
        /// Steps back through the train, ignoring any cabs that face rearwards until there are no forward-facing
        /// cabs left. Then continues from the rearmost, rearward-facing cab, reverses the train and resumes stepping back.
        /// E.g. if consist is made of 3 cars, each with front and rear-facing cabs
        ///     (A-b]:(C-d]:[e-F)
        /// then pressing Ctrl+E cycles the cabs in the sequence
        ///     A -> b -> C -> d -> e -> F
        /// </summary>
        public TrainCar GetNextCab()
        {
            // negative numbers used if rear cab selected
            // because '0' has no negative, all indices are shifted by 1!!!!

            int presentIndex = LeadLocomotiveIndex + 1;
            if (((MSTSLocomotive)LeadLocomotive).UsingRearCab)
                presentIndex = -presentIndex;

            List<int> cabList = new List<int>();

            for (int i = 0; i < Cars.Count; i++)
            {
                if (SkipOtherUsersCar(i))
                    continue;
                bool cab3d = Cars[i].HasFront3DCab || Cars[i].HasRear3DCab;
                bool hasFrontCab = cab3d ? Cars[i].HasFront3DCab : Cars[i].HasFrontCab;
                bool hasRearCab = cab3d ? Cars[i].HasRear3DCab : Cars[i].HasRearCab;
                if (Cars[i].Flipped)
                {
                    if (hasRearCab) cabList.Add(-(i + 1));
                    if (hasFrontCab) cabList.Add(i + 1);
                }
                else
                {
                    if (hasFrontCab) cabList.Add(i + 1);
                    if (hasRearCab) cabList.Add(-(i + 1));
                }
            }

            int lastIndex = cabList.IndexOf(presentIndex);
            if (lastIndex >= cabList.Count - 1) lastIndex = -1;

            int nextCabIndex = cabList[lastIndex + 1];

            TrainCar oldLead = LeadLocomotive;
            LeadLocomotiveIndex = Math.Abs(nextCabIndex) - 1;
            Trace.Assert(LeadLocomotive != null, "Tried to switch to non-existent loco");
            TrainCar newLead = LeadLocomotive;  // Changing LeadLocomotiveIndex also changed LeadLocomotive
            ((MSTSLocomotive)newLead).UsingRearCab = nextCabIndex < 0;

            if (oldLead != null && newLead != null && oldLead != newLead)
            {
                newLead.CopyControllerSettings(oldLead);
                // TODO :: need to link HeadOut cameras to new lead locomotive
                // following should do it but cannot be used due to protection level
                // Program.Viewer.HeadOutBackCamera.SetCameraCar(Cars[LeadLocomotiveIndex]);
                // seems there is nothing to attach camera to car
            }

            // If there is a player locomotive, and it is in this train, update it to match the new lead locomotive.
            if (simulator.PlayerLocomotive?.Train == this)

                simulator.PlayerLocomotive = newLead;

            return newLead;
        }

        //this function is needed for Multiplayer games as they do not need to have cabs, but need to know lead locomotives
        // Sets the Lead locomotive to the next in the consist
        internal void LeadNextLocomotive()
        {
            // First driveable
            int firstLead = -1;
            // Next driveale to the current
            int nextLead = -1;
            // Count of driveable locos
            int coud = 0;

            for (int i = 0; i < Cars.Count; i++)
            {
                if (Cars[i].IsDriveable)
                {
                    // Count the driveables
                    coud++;

                    // Get the first driveable
                    if (firstLead == -1)
                        firstLead = i;

                    // If later than current select the next
                    if (LeadLocomotiveIndex < i && nextLead == -1)
                    {
                        nextLead = i;
                    }
                }
            }

            TrainCar prevLead = LeadLocomotive;

            // If found one after the current
            if (nextLead != -1)
                LeadLocomotiveIndex = nextLead;
            // If not, and have more than one, set the first
            else if (coud > 1)
                LeadLocomotiveIndex = firstLead;
            TrainCar newLead = LeadLocomotive;
            if (prevLead != null && newLead != null && prevLead != newLead)
                newLead.CopyControllerSettings(prevLead);
        }

        //================================================================================================//
        /// <summary>
        /// Is there another cab in the player's train to change to?
        /// </summary>
        public bool IsChangeCabAvailable()
        {
            Trace.Assert(simulator.PlayerLocomotive != null, "Player loco is null when trying to switch locos");
            Trace.Assert(simulator.PlayerLocomotive.Train == this, "Trying to switch locos but not on player's train");

            int driveableCabs = 0;
            for (int i = 0; i < Cars.Count; i++)
            {
                if (SkipOtherUsersCar(i))
                    continue;
                if (Cars[i].HasFrontCab || Cars[i].HasFront3DCab)
                    driveableCabs++;
                if (Cars[i].HasRearCab || Cars[i].HasRear3DCab)
                    driveableCabs++;
            }
            if (driveableCabs < 2)
            {
                simulator.Confirmer.Warning(CabControl.ChangeCab, CabSetting.Warn1);
                return false;
            }
            return true;
        }

        /// In multiplayer, don't want to switch to a locomotive which is player locomotive of another user
        private bool SkipOtherUsersCar(int i)
        {
            if (!MPManager.IsMultiPlayer())
                return false;
            else
            {
                string username = MPManager.GetUserName();
                foreach (OnlinePlayer onlinePlayer in MPManager.OnlineTrains.Players.Values)
                {
                    // don't consider the present user
                    if (onlinePlayer.Username == username)
                        continue;
                    if (onlinePlayer.LeadingLocomotiveID == Cars[i].CarID)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// Reverse train formation
        /// Only performed when train activates a reversal point
        /// NOTE : this routine handles the physical train orientation only, all related route settings etc. must be handled separately
        internal void ReverseFormation(bool setMUParameters)
        {
            if (MPManager.IsMultiPlayer())
                MPManager.BroadCast((new MSGFlip(this, setMUParameters, Number)).ToString()); // message contains data before flip
            ReverseCars();
            // Flip the train's travellers.
            Traveller t = FrontTDBTraveller;
            FrontTDBTraveller = new Traveller(RearTDBTraveller, Traveller.TravellerDirection.Backward);
            RearTDBTraveller = new Traveller(t, Traveller.TravellerDirection.Backward);
            // If we are updating the controls...
            if (setMUParameters)
            {
                // Flip the controls.
                MUDirection = (MidpointDirection)((int)MUDirection * -1);
                MUReverserPercent = -MUReverserPercent;
            }
            if (!((this is AITrain aitrain && aitrain.AI.PreUpdate) || TrainType == TrainType.Static))
                FormationReversed = true;
        }

        /// Reverse cars and car order
        internal void ReverseCars()
        {
            // Shift all the coupler data along the train by 1 car. Not sure whether this logic is correct, as it appears to give incorrect coupler information - To Be Checked
            for (int i = Cars.Count - 1; i > 0; i--)
            {
                Cars[i].CopyCoupler(Cars[i - 1]);
            }
            // Reverse brake hose connections and angle cocks
            for (int i = 0; i < Cars.Count; i++)
            {
                bool ac = Cars[i].BrakeSystem.AngleCockAOpen;
                Cars[i].BrakeSystem.AngleCockAOpen = Cars[i].BrakeSystem.AngleCockBOpen;
                Cars[i].BrakeSystem.AngleCockBOpen = ac;
                if (i == Cars.Count - 1)
                    Cars[i].BrakeSystem.FrontBrakeHoseConnected = false;
                else
                    Cars[i].BrakeSystem.FrontBrakeHoseConnected = Cars[i + 1].BrakeSystem.FrontBrakeHoseConnected;
            }
            // Reverse the actual order of the cars in the train.
            Cars.Reverse();
            // Update leading locomotive index.
            if (LeadLocomotiveIndex >= 0)
                LeadLocomotiveIndex = Cars.Count - LeadLocomotiveIndex - 1;
            // Update flipped state of each car.
            for (int i = 0; i < Cars.Count; i++)
                Cars[i].Flipped = !Cars[i].Flipped;
        }

        /// Someone is sending an event notification to all cars on this train.
        /// ie doors open, pantograph up, lights on etc.
        public void SignalEvent(TrainEvent evt)
        {
            foreach (TrainCar car in Cars)
                car.SignalEvent(evt);
        }

        public void SignalEvent(PowerSupplyEvent evt)
        {
            foreach (TrainCar car in Cars)
                car.SignalEvent(evt);
        }

        public void SignalEvent(PowerSupplyEvent evt, int id)
        {
            foreach (TrainCar car in Cars)
                car.SignalEvent(evt, id);
        }

        //================================================================================================//
        /// <summary>
        /// Set starting conditions when speed > 0 
        /// <\summary>

        public virtual void InitializeMoving()
        {
            ColdStart = false;
            SpeedMpS = InitialSpeed;
            MUDirection = MidpointDirection.Forward;
            float initialThrottlepercent = InitialThrottlepercent;
            MUDynamicBrakePercent = -1;
            //            aiBrakePercent = 0;
            //            AITrainBrakePercent = 0;

            if (LeadLocomotiveIndex >= 0)
            {
                MSTSLocomotive lead = (MSTSLocomotive)Cars[LeadLocomotiveIndex];
                if (lead is MSTSSteamLocomotive) MUReverserPercent = 25;
                lead.CurrentElevationPercent = 100f * lead.WorldPosition.XNAMatrix.M32;

                //TODO: next if block has been inserted to flip trainset physics in order to get viewing direction coincident with loco direction when using rear cab.
                // To achieve the same result with other means, without flipping trainset physics, the block should be deleted
                //         
                if (lead.IsDriveable && (lead as MSTSLocomotive).UsingRearCab)
                {
                    lead.CurrentElevationPercent = -lead.CurrentElevationPercent;
                }
                // give it a bit more gas if it is uphill
                if (lead.CurrentElevationPercent < -2.0) initialThrottlepercent = 40f;
                // better block gas if it is downhill
                else if (lead.CurrentElevationPercent > 1.0) initialThrottlepercent = 0f;

                if (lead.TrainBrakeController != null)
                {
                    EqualReservoirPressurePSIorInHg = lead.TrainBrakeController.MaxPressurePSI;
                }
            }
            MUThrottlePercent = initialThrottlepercent;
            AITrainThrottlePercent = initialThrottlepercent;

            TraincarsInitializeMoving();
        }

        //================================================================================================//
        /// <summary>
        /// Set starting conditions for TrainCars when speed > 0 
        /// <\summary>

        public void TraincarsInitializeMoving()
        {
            for (int i = 0; i < Cars.Count; ++i)
            {
                TrainCar car = Cars[i];
                car.InitializeMoving();
            }
        }

        //================================================================================================//
        /// <summary>
        /// Update train 
        /// <\summary>

        public virtual void Update(double elapsedClockSeconds, bool auxiliaryUpdate = true)
        {
            if (!auxiliaryUpdate)
                FormationReversed = false;
            if (IsActualPlayerTrain && simulator.ActiveMovingTable != null)
                simulator.ActiveMovingTable.CheckTrainOnMovingTable(this);

            if (IsActualPlayerTrain && simulator.OriginalPlayerTrain != this && !CheckStations) // if player train is to check own stations
            {
                CheckStationTask();
            }


            if (IsActualPlayerTrain && simulator.Settings.ActRandomizationLevel > 0 && simulator.ActivityRun != null) // defects might occur
            {
                CheckFailures(elapsedClockSeconds);
            }

            // Update train physics, position and movement

            PhysicsUpdate(elapsedClockSeconds);

            // Update the UiD of First Wagon
            FirstCarUiD = GetFirstWagonUiD();

            // Check to see if wagons are attached to train
            WagonsAttached = GetWagonsAttachedIndication();

            //Exit here when train is static consist (no further actions required)

            if (GetAiMovementState() == AiMovementState.Static)
            {
                int presentTime = Convert.ToInt32(Math.Floor(simulator.ClockTime));
                UpdateAIStaticState(presentTime);
            }

            if (TrainType == TrainType.Static)
                return;

            // perform overall update

            if (ControlMode == TrainControlMode.TurnTable)
            {
                UpdateTurntable(elapsedClockSeconds);
            }

            else if (ControlMode == TrainControlMode.Manual)                                        // manual mode
            {
                UpdateManual(elapsedClockSeconds);
            }

            else if (ControlMode == TrainControlMode.Explorer)                                 // explorer mode
            {
                UpdateExplorer(elapsedClockSeconds);
            }

            else if (ValidRoute[0] != null && GetAiMovementState() != AiMovementState.Static)     // no actions required for static objects //
            {
                if (ControlMode != TrainControlMode.OutOfControl) movedBackward = CheckBackwardClearance();  // check clearance at rear if not out of control //
                UpdateTrainPosition();                                                          // position update         //
                UpdateTrainPositionInformation();                                               // position update         //
                int SignalObjIndex = CheckSignalPassed(0, PresentPosition[Direction.Forward], PreviousPosition[Direction.Forward]);   // check if passed signal  //
                UpdateSectionState(movedBackward);                                              // update track occupation //
                if (!(this is AITrain && (this as AITrain).MovementState == AiMovementState.Suspended)) ObtainRequiredActions(movedBackward);    // process list of actions //

                if (TrainType == TrainType.Player && CheckStations) // if player train is to check own stations
                {
                    CheckStationTask();
                }

                bool stillExist = true;
                if ((TrainType != TrainType.Ai && TrainType != TrainType.AiPlayerHosting) && ControlMode != TrainControlMode.OutOfControl)
                {
                    stillExist = CheckRouteActions(elapsedClockSeconds);                          // check routepath (AI check at other point) //
                }

                if (stillExist)
                {
                    UpdateRouteClearanceAhead(SignalObjIndex, movedBackward, elapsedClockSeconds);  // update route clearance  //
                    if (!(TrainType == TrainType.Remote && MPManager.IsClient()))
                        UpdateSignalState(movedBackward);                                               // update signal state     //
                }
            }

            // check position of train wrt tunnels
            ProcessTunnels();

            // log train details

            if (evaluateTrainSpeed)
            {
                LogTrainSpeed(simulator.GameTime);
            }

        } // end Update

        //================================================================================================//
        /// <summary>
        /// Update train physics
        /// <\summary>

        internal virtual void PhysicsUpdate(double elapsedClockSeconds)
        {
            //if out of track, will set it to stop
            if ((FrontTDBTraveller != null && FrontTDBTraveller.IsEnd) || (RearTDBTraveller != null && RearTDBTraveller.IsEnd))
            {
                if (FrontTDBTraveller.IsEnd && RearTDBTraveller.IsEnd)
                {//if both travellers are out, very rare occation, but have to treat it
                    RearTDBTraveller.ReverseDirection();
                    RearTDBTraveller.NextTrackNode();
                }
                else if (FrontTDBTraveller.IsEnd)
                    RearTDBTraveller.Move(-1);//if front is out, move back
                else if (RearTDBTraveller.IsEnd)
                    RearTDBTraveller.Move(1);//if rear is out, move forward
                foreach (TrainCar car in Cars)
                {
                    car.SpeedMpS = 0;
                }
                SignalEvent(TrainEvent.ResetWheelSlip);//reset everything to 0 power
            }

            if (TrainType == TrainType.Remote || UpdateMSGReceived) //server tolds me this train (may include mine) needs to update position
            {
                UpdateRemoteTrainPos(elapsedClockSeconds);
                return;
            }
            // Update train physics, position and movement

            PropagateBrakePressure(elapsedClockSeconds);

            bool whlslp = false;
            bool whlslpwrn = false;
            bool whlskd = false;

            TrainCar uncoupleBehindCar = null;

            float massKg = 0f;
            foreach (TrainCar car in Cars)
            {
                car.MotiveForceN = 0;
                car.Update(elapsedClockSeconds);

                // Set TotalForce at the start of each calculation cycle. This value is adjusted further through loop based upon forces acting on the train.
                car.TotalForceN = car.MotiveForceN + car.GravityForceN;

                massKg += car.MassKG;
                //TODO: next code line has been modified to flip trainset physics in order to get viewing direction coincident with loco direction when using rear cab.
                // To achieve the same result with other means, without flipping trainset physics, the line should be changed as follows:
                //                 if (car.Flipped)
                if (car.Flipped ^ (car.IsDriveable && car.Train.IsActualPlayerTrain && ((MSTSLocomotive)car).UsingRearCab))
                {
                    car.TotalForceN = -car.TotalForceN;
                    car.SpeedMpS = -car.SpeedMpS;
                }
                if (car.WheelSlip)
                    whlslp = true;
                if (car.WheelSlipWarning)
                    whlslpwrn = true;
                if (car.BrakeSkid)
                {
                    whlskd = true;
                    car.HUDBrakeSkid = true;
                }
                else
                {
                    car.HUDBrakeSkid = false;
                }

                if (car is MSTSDieselLocomotive || car is MSTSElectricLocomotive)
                {
                    // Test to see if locomotive is skidding for HUD presentation
                    if (car.BrakeRetardForceN > 25.0f && car.WheelSlip && car.ThrottlePercent < 0.1f)  // throttle is not good as it may not be zero? better brake? Think about more
                    {
                        whlskd = true;
                        car.HUDBrakeSkid = true;
                    }
                    else
                    {
                        car.HUDBrakeSkid = false;
                    }

                }

                if (car.CouplerExceedBreakLimit)
                    uncoupleBehindCar = car;
            }
            MassKg = massKg;

            IsWheelSlip = whlslp;
            IsWheelSlipWarninq = whlslpwrn;
            IsBrakeSkid = whlskd;

            // Coupler breaker
            if (uncoupleBehindCar != null)
            {
                if (uncoupleBehindCar.CouplerExceedBreakLimit)
                {
                    if (!numOfCouplerBreaksNoted)
                    {
                        NumOfCouplerBreaks++;
                        DbfEvalValueChanged = true;//Debrief eval

                        Trace.WriteLine($"Num of coupler breaks: {NumOfCouplerBreaks}");
                        numOfCouplerBreaksNoted = true;

                        if (simulator.BreakCouplers)
                        {
                            simulator.UncoupleBehind(uncoupleBehindCar, true);
                            uncoupleBehindCar.CouplerExceedBreakLimit = false;
                            simulator.Confirmer.Warning(Simulator.Catalog.GetString("Coupler broken!"));
                        }
                        else
                            simulator.Confirmer.Warning(Simulator.Catalog.GetString("Coupler overloaded!"));
                    }
                }
                else
                    numOfCouplerBreaksNoted = false;

                uncoupleBehindCar = null;
            }
            else
                numOfCouplerBreaksNoted = false;


            UpdateCarSteamHeat(elapsedClockSeconds);
            UpdateAuxTender();

            AddCouplerImpulseForces(elapsedClockSeconds);
            ComputeCouplerForces(elapsedClockSeconds);

            UpdateCarSpeeds(elapsedClockSeconds);
            UpdateCouplerSlack(elapsedClockSeconds);

            // Update wind elements for the train, ie the wind speed, and direction, as well as the angle between the train and wind
            UpdateWindComponents();

            double distanceM = LastCar.SpeedMpS * elapsedClockSeconds;
            if (double.IsNaN(distanceM))
                distanceM = 0;//avoid NaN, if so will not move
            if (TrainType == TrainType.Ai && LeadLocomotiveIndex == (Cars.Count - 1) && LastCar.Flipped)
                distanceM = -distanceM;
            DistanceTravelledM += (float)distanceM;

            SpeedMpS = 0;
            foreach (TrainCar car in Cars)
            {
                SpeedMpS += car.SpeedMpS;
                //TODO: next code line has been modified to flip trainset physics in order to get viewing direction coincident with loco direction when using rear cab.
                // To achieve the same result with other means, without flipping trainset physics, the line should be changed as follows:
                //                 if (car1.Flipped)
                if (car.Flipped ^ (car.IsDriveable && car.Train.IsActualPlayerTrain && ((MSTSLocomotive)car).UsingRearCab))
                    car.SpeedMpS = -car.SpeedMpS;
            }
#if DEBUG_SPEED_FORCES
            Trace.TraceInformation(" ========================= Train Speed #1 (Train.cs) ======================================== ");
            Trace.TraceInformation("Total Raw Speed {0} Train Speed {1}", SpeedMpS, SpeedMpS / Cars.Count);
#endif
            // This next statement looks odd - how can you find the updated speed of the train just by averaging the speeds of
            // the individual TrainCars? No problem if all the TrainCars had equal masses but, if they differ, then surely
            // you must find the total force on the train and then divide by the total mass?
            // Not to worry as comparison with those totals shows that this statement does indeed give a correct updated speed !
            //
            // The reason, I believe, is that when the train forces are balanced (e.g. constant power on a constant gradient),
            // then the calculation of forces in the couplers works out them out so that all the TrainCars share the
            // same acceleration.
            //
            // The updated speed for each TrainCar is simply calculated from the mass of the TrainCar and the force on it but
            // the force on it was previously such that all the TrainCars have the same acceleration. There is little need to
            // add them up and average them, as they only differ when the train forces are out of balance - Chris Jakeman 4-Mar-2019
            SpeedMpS /= Cars.Count;

            SlipperySpotDistanceM -= SpeedMpS * (float)elapsedClockSeconds;
            if (ControlMode != TrainControlMode.TurnTable)
                CalculatePositionOfCars(elapsedClockSeconds, distanceM);

            // calculate projected speed
            if (elapsedClockSeconds < AccelerationMpSpS.SmoothPeriodS)
                AccelerationMpSpS.Update(elapsedClockSeconds, (SpeedMpS - previousSpeedMpS) / elapsedClockSeconds);
            previousSpeedMpS = SpeedMpS;
            ProjectedSpeedMpS = SpeedMpS + 60 * (float)AccelerationMpSpS.SmoothedValue;
            ProjectedSpeedMpS = SpeedMpS > float.Epsilon ?
                Math.Max(0, ProjectedSpeedMpS) : SpeedMpS < -float.Epsilon ? Math.Min(0, ProjectedSpeedMpS) : 0;
        }

        /// Update Wind components for the train
        private void UpdateWindComponents()
        {
            // Gets wind direction and speed, and determines HUD display values for the train as a whole. 
            //These will be representative of the train whilst it is on a straight track, but each wagon will vary when going around a curve.
            // Note both train and wind direction will be positive between 0 (north) and 180 (south) through east, and negative between 0 (north) and 180 (south) through west
            // Wind and train direction to be converted to an angle between 0 and 360 deg.
            if (TrainWindResistanceDependent)
            {
                // Calculate Wind speed and direction, and train direction
                // Update the value of the Wind Speed and Direction for the train
                PhysicsWindDirectionDeg = MathHelper.ToDegrees(simulator.Weather.WindDirection);
                PhysicsWindSpeedMpS = simulator.Weather.WindSpeed;
                float TrainSpeedMpS = Math.Abs(SpeedMpS);

                // If a westerly direction (ie -ve) convert to an angle between 0 and 360
                if (PhysicsWindDirectionDeg < 0)
                    PhysicsWindDirectionDeg += 360;

                if (PhysicsTrainLocoDirectionDeg < 0)
                    PhysicsTrainLocoDirectionDeg += 360;

                // calculate angle between train and eind direction
                if (PhysicsWindDirectionDeg > PhysicsTrainLocoDirectionDeg)
                    ResultantWindComponentDeg = PhysicsWindDirectionDeg - PhysicsTrainLocoDirectionDeg;
                else if (PhysicsTrainLocoDirectionDeg > PhysicsWindDirectionDeg)
                    ResultantWindComponentDeg = PhysicsTrainLocoDirectionDeg - PhysicsWindDirectionDeg;
                else
                    ResultantWindComponentDeg = 0.0f;

                // Correct wind direction if it is greater then 360 deg, then correct to a value less then 360
                ResultantWindComponentDeg %= 360.0f;

                // Wind angle should be kept between 0 and 180 the formulas do not cope with angles > 180. If angle > 180, denotes wind of "other" side of train
                if (ResultantWindComponentDeg > 180)
                    ResultantWindComponentDeg = 360 - ResultantWindComponentDeg;

                float WindAngleRad = MathHelper.ToRadians(ResultantWindComponentDeg);

                WindResultantSpeedMpS = (float)Math.Sqrt(TrainSpeedMpS * TrainSpeedMpS + PhysicsWindSpeedMpS * PhysicsWindSpeedMpS + 2.0f * TrainSpeedMpS * PhysicsWindSpeedMpS * (float)Math.Cos(WindAngleRad));

            }
            else
            {
                WindResultantSpeedMpS = Math.Abs(SpeedMpS);
            }
        }

        /// Update Auxiliary Tenders added to train
        private void UpdateAuxTender()
        {
            if (!(Cars[0] is MSTSSteamLocomotive mstsSteamLocomotive))
                return;  // Don't process if locomotive is not steam locomotive

            bool auxTenderFound = false;    // Flag to confirm that there is still an auxiliary tender in consist
                                            // Calculate when an auxiliary tender is coupled to train
            for (int i = 0; i < Cars.Count; i++)
            {
                if (Cars[i].AuxWagonType == AuxWagonType.AuxiliaryTender && i > LeadLocomotiveIndex && IsPlayerDriven)  // If value has been entered for auxiliary tender & AuxTender car value is greater then the lead locomotive & and it is player driven
                {
                    if (Cars[i - 1].AuxWagonType == AuxWagonType.Tender || Cars[i - 1].AuxWagonType == AuxWagonType.Engine)  // Aux tender found in consist
                    {
                        if (simulator.Activity != null) // If an activity check to see if fuel presets are used.
                        {
                            if (!mstsSteamLocomotive.AuxTenderMoveFlag)  // If locomotive hasn't moved and Auxtender connected use fuel presets on aux tender
                            {
                                MaxAuxTenderWaterMassKG = Cars[i].AuxTenderWaterMassKG;
                                mstsSteamLocomotive.CurrentAuxTenderWaterMassKG = Cars[i].AuxTenderWaterMassKG * (simulator.Activity.Activity.Header.FuelWater / 100.0f); // 
                                IsAuxTenderCoupled = true;      // Flag to advise MSTSSteamLovcomotive that tender is set.
                                auxTenderFound = true;      // Auxililary tender found in consist.

                            }
                            else     // Otherwise assume aux tender not connected at start of activity and therefore full value of water mass available when connected.
                            {
                                MaxAuxTenderWaterMassKG = Cars[i].AuxTenderWaterMassKG;
                                mstsSteamLocomotive.CurrentAuxTenderWaterMassKG = Cars[i].AuxTenderWaterMassKG;
                                IsAuxTenderCoupled = true;
                                auxTenderFound = true;      // Auxililary tender found in consist.
                            }
                        }
                        else  // In explore mode set aux tender to full water value
                        {
                            MaxAuxTenderWaterMassKG = Cars[i].AuxTenderWaterMassKG;
                            mstsSteamLocomotive.CurrentAuxTenderWaterMassKG = Cars[i].AuxTenderWaterMassKG;
                            IsAuxTenderCoupled = true;
                            auxTenderFound = true;      // Auxililary tender found in consist.

                        }
                    }
                    else // Aux tender not found in consist
                    {
                        MaxAuxTenderWaterMassKG = 0.0f;
                        IsAuxTenderCoupled = false;
                    }

                }

#if DEBUG_AUXTENDER
                    Trace.TraceInformation("=============================== DEBUG_AUXTENDER (Train.cs) ==============================================================");
                   // Trace.TraceInformation("Activity Fuel Value {0}", ActivityFuelLevel);
                    Trace.TraceInformation("CarID {0} AuxWagonType {1} LeadLocomotive {2} Max WaterMass {3} Current Water Mass {4}", i, Cars[i].AuxWagonType, LeadLocomotiveIndex, MaxAuxTenderWaterMassKG, mstsSteamLocomotive.CurrentAuxTenderWaterMassKG);
                    Trace.TraceInformation("Prev {0} Coupled {1}", PrevWagonType, IsAuxTenderCoupled);
#endif

            }

            if (!auxTenderFound && IsAuxTenderCoupled)     // If an auxiliary tender is not found in the consist, then assume that it has been uncoupled
            {
                MaxAuxTenderWaterMassKG = 0.0f;     // Reset values
                IsAuxTenderCoupled = false;
            }
        }

        /// Update Steam Heating - this model calculates the total train heat losses and gains for all the cars
        private void UpdateCarSteamHeat(double elapsedClockSeconds)
        {
            // The carriage steam heating model is based upon a description provided in a number of articles, including, "The Steam Heating of Railway Carriages" by Frank W. Marillier ( http://www.gwr.org.uk/links.html ),
            // and "Some Considerations on the Problem of the Heating of British Railway Coaches" by F. J. Pepper. (Journal Institution of Locomotive Engineers - Paper No. 568 - pg 13 -74)
            // Steam is carried the length of the train by a 2" or 1.5" steam pipe that is fitted between each of the cars. Rubber pressure hoses are used to couple the carriage steam pipes together. 
            // Typically these pipes would have some level of insulation on them.
            // Typically 2" bore steam pipes are then placed at strategic locations within the car. For cars with separate compartments, a steam pipe would need to be located in each compartment.
            // The length of each of these compartment steam pipes was determined by a rule of thumb based upon the volume of the space that it was desgined to heat, so for example, a 
            // 2nd class compartment, would have 1" of pipe length for each 3.5cu ft of compartment volume.
            // Steam entering into the passenger area of the train is typically reduced to atmospheric pressure??? 
            // Some articles suggest that a typical steam heating system could use approx 100lbs of steam per hour per car. (more detail)

            // The model calculates the heat capacity of each car at a default temperature, and the various heat losses, such as heat loss from the cars, main steam pipe, leaks, etc are 
            // subtracted from this value. Heat gain from the radiation heat exchange area are added to this value. If all is ok then a balance should be achieved.

            // Leaks in system, loss of heat (and pressure) as steam moves along train

            if (!(simulator.PlayerLocomotive is MSTSLocomotive mstsLocomotive))
                return;

            if (isFirstTimeBoilerCarAttached)
            {
                foreach (TrainCar car in Cars)
                {
                    switch (car.WagonSpecialType)
                    {
                        case TrainCar.WagonSpecialTypes.HeatingBoiler:
                            heatingBoilerCarAttached = true; // A steam heating boiler is fitted in a wagon
                            break;
                        case TrainCar.WagonSpecialTypes.Heated:
                            heatedCarAttached = true; // A steam heating boiler is fitted in a wagon
                            break;
                    }
                }
                isFirstTimeBoilerCarAttached = false;
            }

            // Check to confirm that train is player driven and has passenger cars in the consist. Steam heating is OFF if steam heat valve is closed and no pressure is present
            if (IsPlayerDriven && (PassengerCarsNumber > 0 || heatedCarAttached) && (mstsLocomotive.IsSteamHeatFitted || heatingBoilerCarAttached) && mstsLocomotive.CurrentSteamHeatPressurePSI > 0)
            {
                // Set default values required
                float steamFlowRateLbpHr = 0;
                float progressiveHeatAlongTrainBTU = 0;
                float connectSteamHoseLengthFt = 2.0f * 2.0f; // Assume two hoses on each car * 2 ft long

                // Calculate total heat loss and car temperature along the train
                foreach (TrainCar car in Cars)
                {
                    // Calculate volume in carriage - note height reduced by 1.06m to allow for bogies, etc
                    float BogieHeightM = 1.06f;

                    car.CarHeatVolumeM3 = car.CarWidthM * (car.CarLengthM) * (car.CarHeightM - BogieHeightM); // Check whether this needs to be same as compartment volume
                    car.CarOutsideTempC = TrainOutsideTempC;  // Update Outside temp

                    // Only initialise these values the first time around the loop
                    if (car.IsCarSteamHeatInitial)
                    {

                        // This section sets some arbitary default values the first time that this section is processed. Real values are set on subsequent loops, once steam heat is turned on in locomotive
                        if (trainInsideTempC == 0)
                        {
                            trainInsideTempC = car.DesiredCompartmentTempSetpointC; // Set intial temp - will be set in Steam and Diesel Eng, but these are done after this step
                        }

                        if (TrainOutsideTempC == 0)
                        {

                            TrainOutsideTempC = 10.0f; // Set intial temp - will be set in Steam and Diesel Eng, but these are done after this step
                        }

                        if (mstsLocomotive.EngineType == TrainCar.EngineTypes.Steam && simulator.Settings.HotStart || mstsLocomotive.EngineType == TrainCar.EngineTypes.Diesel || mstsLocomotive.EngineType == TrainCar.EngineTypes.Electric)
                        {
                            if (TrainOutsideTempC < car.DesiredCompartmentTempSetpointC)
                            {
                                car.CarCurrentCarriageHeatTempC = car.DesiredCompartmentTempSetpointC; // Set intial temp
                            }
                            else
                            {
                                car.CarCurrentCarriageHeatTempC = TrainOutsideTempC;
                            }
                        }
                        else
                        {
                            car.CarCurrentCarriageHeatTempC = TrainOutsideTempC;
                        }

                        // Calculate a random factor for steam heat leaks in connecting pipes
                        car.SteamHoseLeakRateRandom = Simulator.Random.Next(100) / 100.0f; // Achieves a two digit random number between 0 and 1
                        car.SteamHoseLeakRateRandom = MathHelper.Clamp(car.SteamHoseLeakRateRandom, 0.5f, 1.0f); // Keep Random Factor ratio within bounds

                        // Calculate Starting Heat value in Car Q = C * M * Tdiff, where C = Specific heat capacity, M = Mass ( Volume * Density), Tdiff - difference in temperature
                        car.TotalPossibleCarHeatW = (float)(Dynamics.Power.FromKW(SpecificHeatCapcityAirKJpKgK * DensityAirKgpM3 * car.CarHeatVolumeM3 * (car.CarCurrentCarriageHeatTempC - TrainOutsideTempC)));

                        //Trace.TraceInformation("Initialise TotalCarHeat - CarID {0} Possible {1} Max {2} Out {3} Vol {4} Density {5} Specific {6}", car.CarID, car.TotalPossibleCarHeatW, car.CarCurrentCarriageHeatTempC, TrainOutsideTempC, car.CarHeatVolumeM3, DensityAirKgpM3, SpecificHeatCapcityAirKJpKgC);

                        // Initialise current Train Steam Heat based upon selected Current carriage Temp
                        car.CarHeatCurrentCompartmentHeatW = car.TotalPossibleCarHeatW;
                        car.IsCarSteamHeatInitial = false;
                    }

                    // Heat loss due to train movement and air flow, based upon convection heat transfer information - http://www.engineeringtoolbox.com/convective-heat-transfer-d_430.html
                    // The formula on this page ( hc = 10.45 - v + 10v1/2), where v = m/s. This formula is used to develop a multiplication factor with train speed.
                    // Curve is only valid between 2.0m/s and 20.0m/s

                    float LowSpeedMpS = 2.0f;
                    float HighSpeedMpS = 20.0f;
                    float ConvHeatTxfMinSpeed = 10.45f - LowSpeedMpS + (10.0f * (float)Math.Pow(LowSpeedMpS, 0.5));
                    float ConvHeatTxfMaxSpeed = 10.45f - HighSpeedMpS + (10.0f * (float)Math.Pow(HighSpeedMpS, 0.5));
                    float ConvHeatTxActualSpeed = 10.45f - car.AbsSpeedMpS + (10.0f * (float)Math.Pow(car.AbsSpeedMpS, 0.5));
                    float ConvFactor = 0;

                    if (car.AbsSpeedMpS > 2 && car.AbsSpeedMpS < 20.0f)
                    {
                        ConvFactor = ConvHeatTxActualSpeed / ConvHeatTxfMinSpeed; // Calculate fraction only between 2 and 20
                    }
                    else if (car.AbsSpeedMpS < 2)
                    {
                        ConvFactor = 1.0f; // If speed less then 2m/s then set fracftion to give stationary Kc value 
                    }
                    else
                    {
                        ConvFactor = ConvHeatTxActualSpeed / ConvHeatTxfMinSpeed; // Calculate constant fraction over 20m/s
                    }
                    ConvFactor = MathHelper.Clamp(ConvFactor, 1.0f, 1.6f); // Keep Conv Factor ratio within bounds - should not exceed 1.6.


                    if (car.WagonType == TrainCar.WagonTypes.Passenger || car.WagonSpecialType == TrainCar.WagonSpecialTypes.Heated) // Only calculate compartment heat in passenger or specially marked heated cars
                    {

                        // ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
                        // Calculate heat loss from inside the carriage
                        // Initialise car values for heating to zero
                        car.TotalCarCompartmentHeatLossWpT = 0.0f;
                        car.CarHeatCompartmentPipeAreaM2 = 0.0f;
                        car.CarHeatVolumeM3 = 0.0f;
                        float HeatLossTransmissionWpT = 0;

                        // Transmission heat loss = exposed area * heat transmission coeff (inside temp - outside temp)
                        // Calculate the heat loss through the roof, wagon sides, and floor separately  
                        // Calculate the heat loss through the carriage sides, per degree of temp change
                        // References - https://www.engineeringtoolbox.com/heat-loss-transmission-d_748.html  and https://www.engineeringtoolbox.com/heat-loss-buildings-d_113.html
                        float HeatTransCoeffRoofWm2C = 1.7f * ConvFactor; // 2 inch wood - uninsulated
                        float HeatTransCoeffEndsWm2C = 0.9f * ConvFactor; // 2 inch wood - insulated - this compensates for the fact that the ends of the cars are somewhat protected from the environment
                        float HeatTransCoeffSidesWm2C = 1.7f * ConvFactor; // 2 inch wood - uninsulated
                        float HeatTransCoeffWindowsWm2C = 4.7f * ConvFactor; // Single glazed glass window in wooden frame
                        float HeatTransCoeffFloorWm2C = 2.5f * ConvFactor; // uninsulated floor

                        // Calculate volume in carriage - note height reduced by 1.06m to allow for bogies, etc
                        float CarCouplingPipeM = 1.2f;  // Allow for connection between cars (assume 2' each end) - no heat is contributed to carriages.

                        // Calculate the heat loss through the roof, allow 15% additional heat loss through roof because of radiation to space
                        float RoofHeatLossFactor = 1.15f;
                        float HeatLossTransRoofWpT = RoofHeatLossFactor * (car.CarWidthM * (car.CarLengthM - CarCouplingPipeM)) * HeatTransCoeffRoofWm2C * (car.CarCurrentCarriageHeatTempC - car.CarOutsideTempC);

                        // Each car will have 2 x sides + 2 x ends. Each side will be made up of solid walls, and windows. A factor has been assumed to determine the ratio of window area to wall area.
                        float HeatLossTransWindowsWpT = (car.WindowDeratingFactor * (car.CarHeightM - BogieHeightM) * (car.CarLengthM - CarCouplingPipeM)) * HeatTransCoeffWindowsWm2C * (car.CarCurrentCarriageHeatTempC - car.CarOutsideTempC);
                        float HeatLossTransSidesWpT = ((1.0f - car.WindowDeratingFactor) * (car.CarHeightM - BogieHeightM) * (car.CarLengthM - CarCouplingPipeM)) * HeatTransCoeffSidesWm2C * (car.CarCurrentCarriageHeatTempC - car.CarOutsideTempC);
                        float HeatLossTransEndsWpT = ((car.CarHeightM - BogieHeightM) * (car.CarLengthM - CarCouplingPipeM)) * HeatTransCoeffEndsWm2C * (car.CarCurrentCarriageHeatTempC - car.CarOutsideTempC);

                        // Total equals 2 x sides, ends, windows
                        float HeatLossTransTotalSidesWpT = (2.0f * HeatLossTransWindowsWpT) + (2.0f * HeatLossTransSidesWpT) + (2.0f * HeatLossTransEndsWpT);

                        // Calculate the heat loss through the floor
                        float HeatLossTransFloorWpT = (car.CarWidthM * (car.CarLengthM - CarCouplingPipeM)) * HeatTransCoeffFloorWm2C * (car.CarCurrentCarriageHeatTempC - car.CarOutsideTempC);

                        HeatLossTransmissionWpT = HeatLossTransRoofWpT + HeatLossTransTotalSidesWpT + HeatLossTransFloorWpT;

                        // ++++++++++++++++++++++++
                        // Ventilation Heat loss, per degree of temp change
                        // This will occur when the train is stopped at the station and prior to being ready to depart. Typically will only apply in activity mode, and not explore mode
                        float HeatLossVentilationWpT = 0;
                        float HeatRecoveryEfficiency = 0.5f; // Assume a HRF of 50%
                        float AirFlowVolumeM3pS = car.CarHeatVolumeM3 / 300.0f; // Assume that the volume of the car is emptied over a period of 5 minutes

                        if (AtStation) // When train is at station.
                        {
                            // If the train is ready to depart, assume all doors are closed, and hence no ventilation loss
                            HeatLossVentilationWpT = MayDepart ? 0 : (float)Dynamics.Power.FromKW((1.0f - HeatRecoveryEfficiency) * SpecificHeatCapcityAirKJpKgK * DensityAirKgpM3 * AirFlowVolumeM3pS * (car.CarCurrentCarriageHeatTempC - car.CarOutsideTempC));
                        }

                        // ++++++++++++++++++++++++
                        // Infiltration Heat loss, per degree of temp change
                        float NumAirShiftspSec = (float)Frequency.Periodic.FromHours(10.0);      // Pepper article suggests that approx 14 air changes per hour happen for a train that is moving @ 50mph, use and av figure of 10.0.
                        float HeatLossInfiltrationWpT = 0;
                        car.CarHeatVolumeM3 = car.CarWidthM * (car.CarLengthM - CarCouplingPipeM) * (car.CarHeightM - BogieHeightM);
                        HeatLossInfiltrationWpT = (float)(Dynamics.Power.FromKW(SpecificHeatCapcityAirKJpKgK * DensityAirKgpM3 * NumAirShiftspSec * car.CarHeatVolumeM3 * (car.CarCurrentCarriageHeatTempC - car.CarOutsideTempC)));

                        car.TotalCarCompartmentHeatLossWpT = HeatLossTransmissionWpT + HeatLossInfiltrationWpT + HeatLossVentilationWpT;

                        //++++++++++++++++++++++++++++++++++++++++
                        // Calculate heat produced by steam pipe acting as heat exchanger inside carriage - this model is based upon the heat loss from a steam pipe. 
                        // The heat loss per metre from a bare pipe equals the heat loss by convection and radiation. Temperatures in degrees Kelvin
                        // QConv = hc * A * (Tp - To), where hc = convection coeff, A = surface area of pipe, Tp = pipe temperature, To = temperature of air around the pipe
                        // QRad = % * A * e * (Tp^4 - To^4), where % = Boltzmans constant, A = surface area of pipe, Tp^4 = pipe temperature, To^4 = temperature of air around the pipe, e = emissivity factor

                        // Calculate steam pipe surface area
                        float CompartmentSteamPipeRadiusM = (float)(Size.Length.FromIn(2.375f) / 2.0f);  // Assume the steam pipes in the compartments have  have internal diameter of 2" (50mm) - external = 2.375"
                        float DoorSteamPipeRadiusM = (float)(Size.Length.FromIn(2.75f) / 2.0f);        // Assume the steam pipes in the doors have diameter of 1.75" (50mm) - assume external = 2.0"

                        // Assume door pipes are 3' 4" (ie 3.3') long, and that there are doors at both ends of the car, ie x 2
                        float CarDoorLengthM = 2.0f * (float)(Size.Length.FromFt(3.3f));
                        float CarDoorVolumeM3 = car.CarWidthM * CarDoorLengthM * (car.CarHeightM - BogieHeightM);

                        float CarDoorPipeAreaM2 = 2.0f * MathHelper.Pi * DoorSteamPipeRadiusM * CarDoorLengthM;

                        // Use rule of thumb - 1" of 2" steam heat pipe for every 3.0 cu ft of volume in car compartment (third class)
                        float CarCompartmentPipeLengthM = (float)(Size.Length.FromIn((car.CarHeatVolumeM3 - CarDoorVolumeM3) / (Size.Volume.FromFt3(car.CompartmentHeatingPipeAreaFactor))));
                        float CarCompartmentPipeAreaM2 = 2.0f * MathHelper.Pi * CompartmentSteamPipeRadiusM * CarCompartmentPipeLengthM;

                        car.CarHeatCompartmentPipeAreaM2 = CarCompartmentPipeAreaM2 + CarDoorPipeAreaM2;

                        // Pipe convection heat produced - steam is reduced to atmospheric pressure when it is injected into compartment
                        float CompartmentSteamPipeTempC = (float)Temperature.Celsius.FromF(mstsLocomotive.SteamHeatPressureToTemperaturePSItoF[0]);
                        car.CarCompartmentSteamPipeHeatConvW = (PipeHeatTransCoeffWpM2K * car.CarHeatCompartmentPipeAreaM2 * (CompartmentSteamPipeTempC - car.CarCurrentCarriageHeatTempC));

                        // Pipe radiation heat produced
                        float PipeTempAK = (float)Math.Pow(Temperature.Kelvin.FromF(CompartmentSteamPipeTempC), 4.0f);
                        float PipeTempBK = (float)Math.Pow(Temperature.Celsius.ToK(car.CarCurrentCarriageHeatTempC), 4.0f);
                        car.CarCompartmentSteamHeatPipeRadW = (BoltzmanConstPipeWpM2 * EmissivityFactor * car.CarHeatCompartmentPipeAreaM2 * (PipeTempAK - PipeTempBK));

                        car.CarHeatCompartmentSteamPipeHeatW = car.CarCompartmentSteamHeatPipeRadW + car.CarCompartmentSteamPipeHeatConvW;

                    }

                    //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
                    // Calculate heating loss in main supply pipe that runs under carriage

                    // Set heat trans coeff
                    float HeatTransCoeffMainPipeBTUpFt2pHrpF = 0.4f * ConvFactor; // insulated pipe - BTU / sq.ft. / hr / l in / °F.
                    float HeatTransCoeffConnectHoseBTUpFt2pHrpF = 0.04f * ConvFactor; // rubber connecting hoses - BTU / sq.ft. / hr / l in / °F. TO BE CHECKED

                    // Calculate Length of carriage and heat loss in main steam pipe
                    float CarMainSteamPipeTempF = (float)mstsLocomotive.SteamHeatPressureToTemperaturePSItoF[car.CarSteamHeatMainPipeSteamPressurePSI];
                    car.CarHeatSteamMainPipeHeatLossBTU = (float)(Size.Length.ToFt(car.CarLengthM) * (MathHelper.Pi * Size.Length.ToFt(car.MainSteamHeatPipeOuterDiaM)) * HeatTransCoeffMainPipeBTUpFt2pHrpF * (CarMainSteamPipeTempF - Temperature.Celsius.ToF(car.CarOutsideTempC)));

                    // calculate steam connecting hoses heat loss - assume 1.5" hose
                    float ConnectSteamHoseOuterDiaFt = (float)Size.Length.ToFt(car.CarConnectSteamHoseOuterDiaM);
                    car.CarHeatConnectSteamHoseHeatLossBTU = (float)(connectSteamHoseLengthFt * (MathHelper.Pi * ConnectSteamHoseOuterDiaFt) * HeatTransCoeffConnectHoseBTUpFt2pHrpF * (CarMainSteamPipeTempF - Temperature.Celsius.ToF(car.CarOutsideTempC)));

                    // Use Napier formula to calculate steam discharge rate through steam trap valve, ie Discharge (lb/s) = (Valve area * Abs Pressure) / 70
                    const float SteamTrapValveDischargeFactor = 70.0f;

                    // Find area of pipe - assume 0.1875" (3/16") dia steam trap
                    float SteamTrapDiaIn = 0.1875f;
                    float SteamTrapValveSizeAreaIn2 = (float)Math.PI * (SteamTrapDiaIn / 2.0f) * (SteamTrapDiaIn / 2.0f);

                    car.CarHeatSteamTrapUsageLBpS = (SteamTrapValveSizeAreaIn2 * (car.CarSteamHeatMainPipeSteamPressurePSI + OneAtmospherePSI)) / SteamTrapValveDischargeFactor;

                    // Use Napier formula to calculate steam discharge rate through steam leak in connecting hose, ie Discharge (lb/s) = (Valve area * Abs Pressure) / 70
                    const float ConnectingHoseDischargeFactor = 70.0f;

                    // Find area of pipe - assume 0.1875" (3/16") dia steam trap
                    float ConnectingHoseLeakDiaIn = 0.1875f;
                    float ConnectingHoseLeakAreaIn2 = (float)Math.PI * (ConnectingHoseLeakDiaIn / 2.0f) * (ConnectingHoseLeakDiaIn / 2.0f);

                    car.CarHeatConnectingSteamHoseLeakageLBpS = car.SteamHoseLeakRateRandom * (ConnectingHoseLeakAreaIn2 * (car.CarSteamHeatMainPipeSteamPressurePSI + OneAtmospherePSI)) / ConnectingHoseDischargeFactor;

                    //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

                    float CurrentComparmentSteamPipeHeatW = 0;

                    // Calculate total steam loss along main pipe, by calculating heat into steam pipe at locomotive, deduct heat loss for each car, 
                    // note if pipe pressure drops, then compartment heating will stop
                    if (car.CarSteamHeatMainPipeSteamPressurePSI >= 1 && car.CarHeatCompartmentHeaterOn && (car.WagonType == TrainCar.WagonTypes.Passenger || car.WagonSpecialType == TrainCar.WagonSpecialTypes.Heated))
                    {
                        // If main pipe pressure is > 0 then heating will start to occur in comparment, so include compartment heat exchanger value
                        progressiveHeatAlongTrainBTU += (float)((car.CarHeatSteamMainPipeHeatLossBTU + car.CarHeatConnectSteamHoseHeatLossBTU) + Frequency.Periodic.ToHours(Dynamics.Power.ToBTUpS(car.CarHeatCompartmentSteamPipeHeatW)));
                        CurrentComparmentSteamPipeHeatW = car.CarHeatCompartmentSteamPipeHeatW; // Car is being heated as main pipe pressure is high enough, and temperature increase is required
                        car.SteamHeatingCompartmentSteamTrapOn = true; // turn on the compartment steam traps
                    }
                    else
                    {
                        // If main pipe pressure is < 0 or temperature in compartment is above the desired temeperature,
                        // then no heating will occur in comparment, so leave compartment heat exchanger value out
                        progressiveHeatAlongTrainBTU += (car.CarHeatSteamMainPipeHeatLossBTU + car.CarHeatConnectSteamHoseHeatLossBTU);
                        CurrentComparmentSteamPipeHeatW = 0; // Car is not being heated as main pipe pressure is not high enough, or car temp is hot enough
                        car.SteamHeatingCompartmentSteamTrapOn = false; // turn off the compartment steam traps
                    }

                    // Calculate steam flow rates and steam used
                    steamFlowRateLbpHr = (float)((progressiveHeatAlongTrainBTU / mstsLocomotive.SteamHeatPSItoBTUpLB[mstsLocomotive.CurrentSteamHeatPressurePSI]) + Frequency.Periodic.ToHours(car.CarHeatSteamTrapUsageLBpS) + Frequency.Periodic.ToHours(car.CarHeatConnectingSteamHoseLeakageLBpS));
                    mstsLocomotive.CalculatedCarHeaterSteamUsageLBpS = (float)Frequency.Periodic.FromHours(steamFlowRateLbpHr);

                    // Calculate Net steam heat loss or gain for each compartment in the car
                    car.CarNetSteamHeatLossWpTime = CurrentComparmentSteamPipeHeatW - car.TotalCarCompartmentHeatLossWpT;

                    car.DisplayTrainNetSteamHeatLossWpTime = car.CarNetSteamHeatLossWpTime;

                    // Given the net heat loss the car calculate the current heat capacity, and corresponding temperature
                    if (car.CarNetSteamHeatLossWpTime < 0)
                    {
                        car.CarNetSteamHeatLossWpTime = -1.0f * car.CarNetSteamHeatLossWpTime; // If steam heat loss is negative, convert to a positive number
                        car.CarHeatCurrentCompartmentHeatW -= (float)(car.CarNetSteamHeatLossWpTime * elapsedClockSeconds);  // Losses per elapsed time
                    }
                    else
                    {

                        car.CarHeatCurrentCompartmentHeatW += (float)(car.CarNetSteamHeatLossWpTime * elapsedClockSeconds);  // Gains per elapsed time         
                    }

                    car.CarCurrentCarriageHeatTempC = (float)(Dynamics.Power.ToKW(car.CarHeatCurrentCompartmentHeatW) / (SpecificHeatCapcityAirKJpKgK * DensityAirKgpM3 * car.CarHeatVolumeM3) + TrainOutsideTempC);

                    float DesiredCompartmentTempResetpointC = car.DesiredCompartmentTempSetpointC - 2.5f; // Allow 2.5Deg bandwidth for temperature

                    if (car.CarCurrentCarriageHeatTempC > car.DesiredCompartmentTempSetpointC)
                    {
                        car.CarHeatCompartmentHeaterOn = false;
                    }
                    else if (car.CarCurrentCarriageHeatTempC < DesiredCompartmentTempResetpointC)
                    {
                        car.CarHeatCompartmentHeaterOn = true;
                    }

                    if (car.CarCurrentCarriageHeatTempC < desiredCompartmentAlarmTempSetpointC) // If temp below 45of then alarm
                    {
                        if (!lowSteamHeat)
                        {
                            lowSteamHeat = true;
                            // Provide warning message if temperature is too hot
                            if (car.WagonType == TrainCar.WagonTypes.Passenger)
                            {
                                simulator.Confirmer.Message(ConfirmLevel.Warning, Simulator.Catalog.GetString("Carriage {0} temperature is too cold, the passengers are freezing.", car.CarID));
                            }
                            else
                            {
                                simulator.Confirmer.Message(ConfirmLevel.Warning, Simulator.Catalog.GetString("Car {0} temperature is too cold for the freight.", car.CarID));
                            }
                        }
                    }
                    else if (car.CarCurrentCarriageHeatTempC > resetCompartmentAlarmTempSetpointC)
                    {
                        lowSteamHeat = false;        // Reset temperature warning
                    }
                }

                #region Calculate Steam Pressure drop along train

                // Initialise main steam pipe pressure to same as steam heat valve setting
                float ProgressivePressureAlongTrainPSI = mstsLocomotive.CurrentSteamHeatPressurePSI;

                // Calculate pressure drop along whole train
                foreach (TrainCar car in Cars)
                {
                    // Calculate pressure drop in pipe along train. This calculation is based upon the Unwin formula - https://www.engineeringtoolbox.com/steam-pressure-drop-calculator-d_1093.html
                    // dp = 0.0001306 * q^2 * L * (1 + 3.6/d) / (3600 * ρ * d^5)
                    // where dp = pressure drop (psi), q = steam flow rate(lb/ hr), L = length of pipe(ft), d = pipe inside diameter(inches), ρ = steam density(lb / ft3)
                    // Use values for the specific volume corresponding to the average pressure if the pressure drop exceeds 10 - 15 % of the initial absolute pressure

                    float HeatPipePressureDropPSI = (float)((0.0001306f * steamFlowRateLbpHr * steamFlowRateLbpHr * Size.Length.ToFt(car.CarLengthM) * (1 + 3.6f / 2.5f)) / (3600 * mstsLocomotive.SteamDensityPSItoLBpFT3[mstsLocomotive.CurrentSteamHeatPressurePSI] * (float)Math.Pow(car.MainSteamHeatPipeInnerDiaM, 5.0f)));
                    float ConnectHosePressureDropPSI = (float)((0.0001306f * steamFlowRateLbpHr * steamFlowRateLbpHr * connectSteamHoseLengthFt * (1 + 3.6f / 2.5f)) / (3600 * mstsLocomotive.SteamDensityPSItoLBpFT3[mstsLocomotive.CurrentSteamHeatPressurePSI] * (float)Math.Pow(car.CarConnectSteamHoseInnerDiaM, 5.0f)));
                    float CarPressureDropPSI = HeatPipePressureDropPSI + ConnectHosePressureDropPSI;

                    ProgressivePressureAlongTrainPSI -= CarPressureDropPSI;
                    if (ProgressivePressureAlongTrainPSI < 0)
                    {
                        ProgressivePressureAlongTrainPSI = 0; // Make sure that pressure never goes negative
                    }
                    car.CarSteamHeatMainPipeSteamPressurePSI = ProgressivePressureAlongTrainPSI;

                    // For the boiler heating car adjust mass based upon fuel and water usage
                    if (car.WagonSpecialType == TrainCar.WagonSpecialTypes.HeatingBoiler)
                    {

                        // Don't process if water or fule capacities are low
                        if (mstsLocomotive.CurrentSteamHeatPressurePSI > 0 && car.CurrentSteamHeatBoilerFuelCapacityL > 0 && car.CurrentCarSteamHeatBoilerWaterCapacityL > 0 && !car.IsSteamHeatBoilerLockedOut)
                        {
                            // Test boiler steam capacity can deliever steam required for the system
                            if (mstsLocomotive.CalculatedCarHeaterSteamUsageLBpS > car.MaximumSteamHeatingBoilerSteamUsageRateLbpS)
                            {
                                car.IsSteamHeatBoilerLockedOut = true; // Lock steam heat boiler out is steam usage exceeds capacity
                                simulator.Confirmer.Message(ConfirmLevel.Warning, Simulator.Catalog.GetString("The steam usage has exceeded the capacity of the steam boiler. Steam boiler locked out."));
                                Trace.TraceInformation("Steam heat boiler locked out as capacity exceeded");
                            }

                            // Calculate fuel usage for steam heat boiler
                            double FuelUsageLpS = Size.LiquidVolume.FromGallonUK(Frequency.Periodic.FromHours(car.TrainHeatBoilerFuelUsageGalukpH[Frequency.Periodic.ToHours(mstsLocomotive.CalculatedCarHeaterSteamUsageLBpS)]));
                            double FuelOilConvertLtoKg = 0.85f;
                            car.CurrentSteamHeatBoilerFuelCapacityL -= (float)(FuelUsageLpS * elapsedClockSeconds); // Reduce tank capacity as fuel used.
                            // This may need to be changed at some stage, as currently weight decreases on freight cars does not happen, except when being filled or emptied at pickup point
                            car.MassKG -= (float)(FuelUsageLpS * elapsedClockSeconds * FuelOilConvertLtoKg); // Reduce locomotive weight as Steam heat boiler uses fuel.

                            // Calculate water usage for steam heat boiler
                            double WaterUsageLpS = Size.LiquidVolume.FromGallonUK(Frequency.Periodic.FromHours(car.TrainHeatBoilerWaterUsageGalukpH[Frequency.Periodic.ToHours(mstsLocomotive.CalculatedCarHeaterSteamUsageLBpS)]));
                            car.CurrentCarSteamHeatBoilerWaterCapacityL -= (float)(WaterUsageLpS * elapsedClockSeconds); // Reduce tank capacity as water used.
                            // This may need to be changed at some stage, as currently weight decreases on freight cars does not happen, except when being filled or emptied at pickup point
                            car.MassKG -= (float)(WaterUsageLpS * elapsedClockSeconds); // Reduce locomotive weight as Steam heat boiler uses water - NB 1 litre of water = 1 kg.
                        }
                    }
                }
                #endregion
            }
        }

        /// ProcessTunnels : check position of each car in train wrt tunnel
        protected void ProcessTunnels()
        {
            // start at front of train
            int sectionIndex = PresentPosition[Direction.Forward].TrackCircuitSectionIndex;
            float sectionOffset = PresentPosition[Direction.Forward].Offset;
            TrackDirection sectionDirection = PresentPosition[Direction.Forward].Direction;

            foreach (TrainCar car in Cars)
            {
                float usedCarLength = car.CarLengthM;
                float processedCarLength = 0;
                bool validSections = true;

                float? FrontCarPositionInTunnel = null;
                float? FrontCarLengthOfTunnelAhead = null;
                float? RearCarLengthOfTunnelBehind = null;
                int numTunnelPaths = 0;

                while (validSections)
                {
                    TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[sectionIndex];
                    bool inTunnel = false;

                    // car spans sections
                    if ((car.CarLengthM - processedCarLength) > sectionOffset)
                    {
                        usedCarLength = sectionOffset - processedCarLength;
                    }

                    // section has tunnels
                    foreach (TunnelInfoData tunnel in section.TunnelInfo ?? Enumerable.Empty<TunnelInfoData>())
                    {
                        float tunnelStartOffset = tunnel.Start[sectionDirection];
                        float tunnelEndOffset = tunnel.End[sectionDirection];

                        if (tunnelStartOffset > 0 && tunnelStartOffset > sectionOffset)      // start of tunnel is in section beyond present position - cannot be in this tunnel nor any following
                        {
                            break;
                        }

                        if (tunnelEndOffset > 0 && tunnelEndOffset < (sectionOffset - usedCarLength)) // beyond end of tunnel, test next
                        {
                            continue;
                        }

                        if (tunnelStartOffset <= 0 || tunnelStartOffset < (sectionOffset - usedCarLength)) // start of tunnel is behind
                        {
                            if (tunnelEndOffset < 0) // end of tunnel is out of this section
                            {
                                if (processedCarLength != 0)
                                {
                                    Trace.TraceInformation($"Train : {Name}; found tunnel in section {sectionIndex} with End < 0 while processed length : {processedCarLength}");
                                }
                            }

                            inTunnel = true;

                            numTunnelPaths = tunnel.NumberPaths;

                            // get position in tunnel
                            if (tunnelStartOffset < 0)
                            {
                                FrontCarPositionInTunnel = sectionOffset + tunnel.SectionStartOffset[sectionDirection];
                                FrontCarLengthOfTunnelAhead = tunnel.LengthTotal - FrontCarPositionInTunnel;
                                RearCarLengthOfTunnelBehind = tunnel.LengthTotal - (FrontCarLengthOfTunnelAhead + car.CarLengthM);
                            }
                            else
                            {
                                FrontCarPositionInTunnel = sectionOffset - tunnelStartOffset;
                                FrontCarLengthOfTunnelAhead = tunnel.LengthTotal - FrontCarPositionInTunnel - processedCarLength;
                                RearCarLengthOfTunnelBehind = tunnel.LengthTotal - (FrontCarLengthOfTunnelAhead + car.CarLengthM);
                            }

                            break;  // only test one tunnel
                        }
                    }
                    // tested this section, any need to go beyond?

                    processedCarLength += usedCarLength;
                    if (inTunnel || processedCarLength >= car.CarLengthM)
                    {
                        validSections = false;  // end of while loop through sections
                        sectionOffset -= usedCarLength;   // position of next car in this section

                        car.CarTunnelData.FrontPositionBeyondStartOfTunnel = FrontCarPositionInTunnel.HasValue ? FrontCarPositionInTunnel : null;
                        car.CarTunnelData.LengthMOfTunnelAheadFront = FrontCarLengthOfTunnelAhead.HasValue ? FrontCarLengthOfTunnelAhead : null;
                        car.CarTunnelData.LengthMOfTunnelBehindRear = RearCarLengthOfTunnelBehind.HasValue ? RearCarLengthOfTunnelBehind : null;
                        car.CarTunnelData.numTunnelPaths = numTunnelPaths;
                    }
                    else
                    {
                        // go back one section
                        int thisSectionRouteIndex = ValidRoute[0].GetRouteIndexBackward(sectionIndex, PresentPosition[Direction.Forward].RouteListIndex);
                        if (thisSectionRouteIndex >= 0)
                        {
                            sectionIndex = thisSectionRouteIndex;
                            section = TrackCircuitSection.TrackCircuitList[sectionIndex];
                            sectionOffset = section.Length;  // always at end of next section
                            sectionDirection = ValidRoute[0][thisSectionRouteIndex].Direction;
                        }
                        else // ran out of train
                        {
                            validSections = false;

                            car.CarTunnelData.FrontPositionBeyondStartOfTunnel = FrontCarPositionInTunnel.HasValue ? FrontCarPositionInTunnel : null;
                            car.CarTunnelData.LengthMOfTunnelAheadFront = FrontCarLengthOfTunnelAhead.HasValue ? FrontCarLengthOfTunnelAhead : null;
                            car.CarTunnelData.LengthMOfTunnelBehindRear = RearCarLengthOfTunnelBehind.HasValue ? RearCarLengthOfTunnelBehind : null;
                            car.CarTunnelData.numTunnelPaths = numTunnelPaths;
                        }
                    }
                }
            }
        }

        /// Train speed evaluation logging - open file
        protected void CreateLogFile()
        {
            //Time, Train Speed, Max Speed, Signal Aspect, Elevation, Direction, Control Mode, Distance Travelled, Throttle, Brake, Dyn Brake, Gear

            StringBuilder builder = new StringBuilder();

            if (!string.IsNullOrEmpty(evaluationLogFile) && !File.Exists(evaluationLogFile))
            {
                if ((evaluationContent & EvaluationLogContents.Time) == EvaluationLogContents.Time)
                {
                    builder.Append("TIME");
                    builder.Append(Separator);
                }

                if ((evaluationContent & EvaluationLogContents.Speed) == EvaluationLogContents.Speed)
                {
                    builder.Append("TRAINSPEED");
                    builder.Append(Separator);
                }

                if ((evaluationContent & EvaluationLogContents.MaxSpeed) == EvaluationLogContents.MaxSpeed)
                {
                    builder.Append("MAXSPEED");
                    builder.Append(Separator);
                }

                if ((evaluationContent & EvaluationLogContents.SignalAspect) == EvaluationLogContents.SignalAspect)
                {
                    builder.Append("SIGNALASPECT");
                    builder.Append(Separator);
                }

                if ((evaluationContent & EvaluationLogContents.Elevation) == EvaluationLogContents.Elevation)
                {
                    builder.Append("ELEVATION");
                    builder.Append(Separator);
                }

                if ((evaluationContent & EvaluationLogContents.Direction) == EvaluationLogContents.Direction)
                {
                    builder.Append("DIRECTION");
                    builder.Append(Separator);
                }

                if ((evaluationContent & EvaluationLogContents.ControlMode) == EvaluationLogContents.ControlMode)
                {
                    builder.Append("CONTROLMODE");
                    builder.Append(Separator);
                }

                if ((evaluationContent & EvaluationLogContents.Distance) == EvaluationLogContents.Distance)
                {
                    builder.Append("DISTANCETRAVELLED");
                    builder.Append(Separator);
                }

                if ((evaluationContent & EvaluationLogContents.Throttle) == EvaluationLogContents.Throttle)
                {
                    builder.Append("THROTTLEPERC");
                    builder.Append(Separator);
                }

                if ((evaluationContent & EvaluationLogContents.Brake) == EvaluationLogContents.Brake)
                {
                    builder.Append("BRAKEPRESSURE");
                    builder.Append(Separator);
                }

                if ((evaluationContent & EvaluationLogContents.DynBrake) == EvaluationLogContents.DynBrake)
                {
                    builder.Append("DYNBRAKEPERC");
                    builder.Append(Separator);
                }

                if ((evaluationContent & EvaluationLogContents.Gear) == EvaluationLogContents.Gear)
                {
                    builder.Append("GEARINDEX");
                    builder.Append(Separator);
                }

                builder.Append('\n');

                try
                {
                    File.AppendAllText(evaluationLogFile, builder.ToString());
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Trace.TraceWarning("Cannot open required logfile : " + evaluationLogFile + " : " + e.Message);
                    evaluateTrainSpeed = false;
                }
            }
        }

        /// Train speed evaluation logging
        protected void LogTrainSpeed(double timeStamp)
        {
            //TODO 20201125 may run in separate thread
            if (lastLogTime + evaluationInterval >= timeStamp)
            {
                lastLogTime = timeStamp;

                // User settings flag indices :
                //Time, Train Speed, Max Speed, Signal Aspect, Elevation, Direction, Control Mode, Distance Travelled, Throttle, Brake, Dyn Brake, Gear

                StringBuilder builder = new StringBuilder();

                if ((evaluationContent & EvaluationLogContents.Time) == EvaluationLogContents.Time)
                {
                    builder.Append(FormatStrings.FormatTime(simulator.ClockTime));
                    builder.Append(Separator);
                }

                bool moveForward = (Math.Sign(SpeedMpS) >= 0);
                if ((evaluationContent & EvaluationLogContents.Speed) == EvaluationLogContents.Speed)
                {
                    builder.Append($"{Speed.MeterPerSecond.FromMpS(Math.Abs(SpeedMpS), simulator.MilepostUnitsMetric):0000.0}{Separator}");
                }

                if ((evaluationContent & EvaluationLogContents.MaxSpeed) == EvaluationLogContents.MaxSpeed)
                {
                    builder.Append($"{Speed.MeterPerSecond.FromMpS(AllowedMaxSpeedMpS, simulator.MilepostUnitsMetric):0000.0}{Separator}");
                }

                if ((evaluationContent & EvaluationLogContents.SignalAspect) == EvaluationLogContents.SignalAspect)
                {
                    if (moveForward)
                    {
                        builder.Append(NextSignalObject[0]?.SignalLR(SignalFunction.Normal).ToString() ?? "-");
                    }
                    else
                    {
                        builder.Append(NextSignalObject[1]?.SignalLR(SignalFunction.Normal).ToString() ?? "-");
                    }
                    builder.Append(Separator);
                }

                if ((evaluationContent & EvaluationLogContents.Elevation) == EvaluationLogContents.Elevation)
                {
                    builder.Append($"{(0 - simulator.PlayerLocomotive.CurrentElevationPercent):00.0}{Separator}");
                }

                if ((evaluationContent & EvaluationLogContents.Direction) == EvaluationLogContents.Direction)
                {
                    builder.Append(moveForward ? 'F' : 'B');
                    builder.Append(Separator);
                }

                if ((evaluationContent & EvaluationLogContents.ControlMode) == EvaluationLogContents.ControlMode)
                {
                    builder.Append(ControlMode.ToString());
                    builder.Append(Separator);
                }

                if ((evaluationContent & EvaluationLogContents.Distance) == EvaluationLogContents.Distance)
                {
                    builder.Append($"{PresentPosition[Direction.Forward].DistanceTravelled:0.00}{Separator}");
                }

                if ((evaluationContent & EvaluationLogContents.Throttle) == EvaluationLogContents.Throttle)
                {
                    builder.Append($"{MUThrottlePercent:000}{Separator}");
                }

                if ((evaluationContent & EvaluationLogContents.Brake) == EvaluationLogContents.Brake)
                {
                    builder.Append($"{simulator.PlayerLocomotive.BrakeSystem.GetCylPressurePSI():000}{Separator}");
                }

                if ((evaluationContent & EvaluationLogContents.DynBrake) == EvaluationLogContents.DynBrake)
                {
                    builder.Append($"{MUDynamicBrakePercent:000}{Separator}");
                }

                if ((evaluationContent & EvaluationLogContents.Gear) == EvaluationLogContents.Gear)
                {
                    builder.Append($"{MUGearboxGearIndex:0}{Separator}");
                }

                builder.Append('\n');
                File.AppendAllText(evaluationLogFile, builder.ToString());
            }
        }

        /// Update in manual mode
        internal void UpdateManual(double elapsedClockSeconds)
        {
            _ = elapsedClockSeconds;
            UpdateTrainPosition();                                                                // position update                  //
            int SignalObjIndex = CheckSignalPassed(0, PresentPosition[Direction.Forward], PreviousPosition[Direction.Forward]);   // check if passed signal forward   //
            if (SignalObjIndex < 0)
            {
                SignalObjIndex = CheckSignalPassed(1, PresentPosition[Direction.Backward], PreviousPosition[Direction.Backward]);   // check if passed signal backward  //
            }
            if (SignalObjIndex >= 0)
            {
                Signal signalObject = Simulator.Instance.SignalEnvironment.Signals[SignalObjIndex];

                //the following is added by CSantucci, applying also to manual mode what Jtang implemented for activity mode: after passing a manually forced signal,
                // system will take back control of the signal
                if (signalObject.HoldState == SignalHoldState.ManualPass || signalObject.HoldState == SignalHoldState.ManualApproach)
                    signalObject.HoldState = SignalHoldState.None;

                AllowedCallOnSignal = null;
            }
            UpdateSectionStateManual();                                                           // update track occupation          //
            UpdateManualMode(SignalObjIndex);                                                     // update route clearance           //
            // for manual, also includes signal update //
        }

        /// Update in explorer mode
        internal void UpdateExplorer(double elapsedClockSeconds)
        {
            _ = elapsedClockSeconds;
            UpdateTrainPosition();                                                                // position update                  //
            int SignalObjIndex = CheckSignalPassed(0, PresentPosition[Direction.Forward], PreviousPosition[Direction.Forward]);   // check if passed signal forward   //
            if (SignalObjIndex < 0)
            {
                SignalObjIndex = CheckSignalPassed(1, PresentPosition[Direction.Backward], PreviousPosition[Direction.Backward]);   // check if passed signal backward  //
            }
            if (SignalObjIndex >= 0)
            {
                Signal signalObject = Simulator.Instance.SignalEnvironment.Signals[SignalObjIndex];

                //the following is added by CSantucci, applying also to explorer mode what Jtang implemented for activity mode: after passing a manually forced signal,
                // system will take back control of the signal
                if (signalObject.HoldState == SignalHoldState.ManualPass || signalObject.HoldState == SignalHoldState.ManualApproach)
                    signalObject.HoldState = SignalHoldState.None;

                AllowedCallOnSignal = null;
            }
            UpdateSectionStateExplorer();                                                         // update track occupation          //
            UpdateExplorerMode(SignalObjIndex);                                                   // update route clearance           //
            // for manual, also includes signal update //
        }

        /// Update in turntable mode
        internal void UpdateTurntable(double elapsedClockSeconds)
        {
            _ = elapsedClockSeconds;
            //           UpdateTrainPosition();                                                                // position update                  //
            if (LeadLocomotive != null && (LeadLocomotive.ThrottlePercent >= 1 || Math.Abs(LeadLocomotive.SpeedMpS) > 0.05 || !(LeadLocomotive.Direction == MidpointDirection.N
            || Math.Abs(MUReverserPercent) <= 1)) || ControlMode != TrainControlMode.TurnTable)
            // Go to emergency.
            {
                ((MSTSLocomotive)LeadLocomotive).SetEmergency(true);
            }
        }

        /// Post Init : perform all actions required to start
        internal virtual bool PostInit()
        {

            // if train has no valid route, build route over trainlength (from back to front)
            bool validPosition = InitialTrainPlacement();

            if (validPosition)
            {
                InitializeSignals(false);     // Get signal information - only if train has route //
                if (TrainType != TrainType.Static)
                    CheckDeadlock(ValidRoute[0], Number);    // Check deadlock against all other trains (not for static trains)
                if (TCRoute != null)
                    TCRoute.SetReversalOffset(Length, simulator.TimetableMode);

                AuxActionsContainer.SetAuxAction(this);
            }


            // set train speed logging flag (valid per activity, so will be restored after save)
            if (IsActualPlayerTrain)
            {
                SetTrainSpeedLoggingFlag();
            }

            return validPosition;
        }

        /// set train speed logging flag (valid per activity, so will be restored after save)
        protected void SetTrainSpeedLoggingFlag()
        {
            evaluateTrainSpeed = simulator.Settings.EvaluationTrainSpeed;
            evaluationInterval = simulator.Settings.EvaluationInterval;

            evaluationContent = simulator.Settings.EvaluationContent;

            // if logging required, derive filename and open file
            if (evaluateTrainSpeed)
            {
                evaluationLogFile = simulator.DeriveLogFile("Speed");
                if (string.IsNullOrEmpty(evaluationLogFile))
                {
                    evaluateTrainSpeed = false;
                }
                else
                {
                    CreateLogFile();
                }
            }
        }

        /// get aspect of next signal ahead
        protected SignalAspectState GetNextSignalAspect(int direction)
        {
            SignalAspectState thisAspect = SignalAspectState.Stop;
            if (NextSignalObject[direction] != null)
            {
                thisAspect = NextSignalObject[direction].SignalLR(SignalFunction.Normal);
            }

            return thisAspect;
        }

        /// initialize signal array
        internal void InitializeSignals(bool existingSpeedLimits)
        {
            Debug.Assert(Simulator.Instance.SignalEnvironment != null, "Cannot InitializeSignals() without Simulator.Signals.");

            // to initialize, use direction 0 only
            // preset indices

            SignalObjectItems.Clear();
            nextSignalIndex = -1;
            nextSpeedLimitIndex = -1;

            //  set overall speed limits if these do not yet exist

            if (!existingSpeedLimits)
            {
                if ((TrainMaxSpeedMpS <= 0f) && (LeadLocomotive != null))
                    TrainMaxSpeedMpS = (LeadLocomotive as MSTSLocomotive).MaxSpeedMpS;
                AllowedMaxSpeedMpS = TrainMaxSpeedMpS;   // set default
                AllowedMaxSpeedSignalMpS = TrainMaxSpeedMpS;   // set default
                allowedMaxTempSpeedLimitMpS = AllowedMaxSpeedMpS; // set default

                //  try to find first speed limits behind the train

                List<int> speedpostList = SignalEnvironment.ScanRoute(null, PresentPosition[Direction.Backward].TrackCircuitSectionIndex, PresentPosition[Direction.Backward].Offset,
                                PresentPosition[Direction.Backward].Direction, false, -1, false, true, false, false, false, false, false, true, false, IsFreight);

                if (speedpostList.Count > 0)
                {
                    Signal speedpost = Simulator.Instance.SignalEnvironment.Signals[speedpostList[0]];
                    SpeedInfo speedInfo = speedpost.SpeedLimit(SignalFunction.Speed);

                    AllowedMaxSpeedMpS = Math.Min(AllowedMaxSpeedMpS, IsFreight ? speedInfo.FreightSpeed : speedInfo.PassengerSpeed);
                    allowedAbsoluteMaxSpeedLimitMpS = Math.Min(allowedAbsoluteMaxSpeedLimitMpS, IsFreight ? speedInfo.FreightSpeed : speedInfo.PassengerSpeed);
                }

                float validSpeedMpS = AllowedMaxSpeedMpS;

                //  try to find first speed limits along train - scan back to front

                bool noMoreSpeedposts = false;
                int sectionIndex = PresentPosition[Direction.Backward].TrackCircuitSectionIndex;
                float sectionOffset = PresentPosition[Direction.Backward].Offset;
                TrackDirection direction = PresentPosition[Direction.Backward].Direction;
                float remLength = Length;

                while (!noMoreSpeedposts)
                {
                    speedpostList = SignalEnvironment.ScanRoute(null, sectionIndex, sectionOffset,
                            direction, true, remLength, false, true, false, false, false, false, false, true, false, IsFreight);

                    if (speedpostList.Count > 0)
                    {
                        Signal speedpost = Simulator.Instance.SignalEnvironment.Signals[speedpostList[0]];
                        SpeedInfo speedInfo = speedpost.SpeedLimit(SignalFunction.Speed);
                        float distanceFromFront = Length - speedpost.DistanceTo(RearTDBTraveller);
                        if (distanceFromFront >= 0)
                        {
                            float newSpeedMpS = IsFreight ? speedInfo.FreightSpeed : speedInfo.PassengerSpeed;
                            if (newSpeedMpS <= validSpeedMpS)
                            {
                                validSpeedMpS = newSpeedMpS;
                                if (validSpeedMpS < AllowedMaxSpeedMpS)
                                {
                                    AllowedMaxSpeedMpS = validSpeedMpS;
                                }
                                requiredActions.UpdatePendingSpeedlimits(validSpeedMpS);  // update any older pending speed limits
                            }
                            else
                            {
                                validSpeedMpS = newSpeedMpS;
                                float reqDistance = DistanceTravelledM + Length - distanceFromFront;
                                ActivateSpeedLimit speedLimit = new ActivateSpeedLimit(reqDistance,
                                    speedInfo.LimitedSpeedReduction == 0 ? newSpeedMpS : -1, -1f,
                                    speedInfo.LimitedSpeedReduction == 0 ? -1 : newSpeedMpS);
                                requiredActions.InsertAction(speedLimit);
                                requiredActions.UpdatePendingSpeedlimits(newSpeedMpS);  // update any older pending speed limits
                            }

                            if (newSpeedMpS < allowedAbsoluteMaxSpeedLimitMpS) allowedAbsoluteMaxSpeedLimitMpS = newSpeedMpS;
                            sectionIndex = speedpost.TrackCircuitIndex;
                            sectionOffset = speedpost.TrackCircuitOffset;
                            direction = speedpost.TrackCircuitDirection;
                            remLength = distanceFromFront;
                        }
                        else
                        {
                            noMoreSpeedposts = true;
                        }
                    }
                    else
                    {
                        noMoreSpeedposts = true;
                    }
                }

                AllowedMaxSpeedLimitMpS = AllowedMaxSpeedMpS;   // set default
            }

            float distanceToLastObject = 9E29f;  // set to overlarge value
            SignalAspectState nextAspect = SignalAspectState.Unknown;

            SignalItemInfo firstObject = Simulator.Instance.SignalEnvironment.GetNextObjectInRoute(RoutedForward, ValidRoute[0],
                PresentPosition[Direction.Forward].RouteListIndex, PresentPosition[Direction.Forward].Offset, -1,
                SignalItemType.Any);

            //  get first item from train (irrespective of distance)
            SignalItemFindState returnState = firstObject.State;
            if (returnState == SignalItemFindState.Item)
            {
                firstObject.DistanceToTrain = firstObject.DistanceFound;
                SignalObjectItems.Add(firstObject);
                if (firstObject.SignalDetails.IsSignal)
                {
                    nextAspect = firstObject.SignalDetails.SignalLR(SignalFunction.Normal);
                    firstObject.SignalState = nextAspect;
                }
                distanceToLastObject = firstObject.DistanceFound;
            }

            // get next items within max distance; longer for player train to provide correct TCS handling

            SignalItemInfo nextObject;
            SignalItemInfo prevObject = firstObject;

            int routeListIndex = PresentPosition[Direction.Forward].RouteListIndex;
            float offset = PresentPosition[Direction.Forward].Offset;
            int nextIndex = routeListIndex;

            while (returnState == SignalItemFindState.Item && distanceToLastObject < MaxDistanceCheckedAhead && nextAspect != SignalAspectState.Stop)
            {
                int foundSection = -1;

                Signal signal = prevObject.SignalDetails;

                int reqTCReference = signal.TrackCircuitIndex;
                float reqOffset = signal.TrackCircuitOffset + 0.0001f;   // make sure you find NEXT object ! //

                if (signal.TrackCircuitNextIndex > 0)
                {
                    reqTCReference = signal.TrackCircuitNextIndex;
                    reqOffset = 0.0f;
                }

                if (nextIndex < 0)
                    nextIndex = 0;
                for (int iNode = nextIndex; iNode < ValidRoute[0].Count && foundSection < 0 && reqTCReference > 0; iNode++)
                {
                    TrackCircuitRouteElement thisElement = ValidRoute[0][iNode];
                    if (thisElement.TrackCircuitSection.Index == reqTCReference)
                    {
                        foundSection = iNode;
                        nextIndex = iNode;
                        offset = reqOffset;
                    }
                }

                nextObject = Simulator.Instance.SignalEnvironment.GetNextObjectInRoute(RoutedForward, ValidRoute[0],
                nextIndex, offset, -1, SignalItemType.Any);

                returnState = nextObject.State;

                if (returnState == SignalItemFindState.Item)
                {
                    if (nextObject.SignalDetails.IsSignal)
                    {
                        nextObject.SignalState = nextObject.SignalDetails.SignalLR(SignalFunction.Normal);
                        nextAspect = nextObject.SignalState;

                    }

                    nextObject.DistanceToObject = nextObject.DistanceFound;
                    nextObject.DistanceToTrain = prevObject.DistanceToTrain + nextObject.DistanceToObject;
                    distanceToLastObject = nextObject.DistanceToTrain;
                    SignalObjectItems.Add(nextObject);
                    prevObject = nextObject;
                }
            }

            //
            // get first signal and first speedlimit
            // also initiate nextSignal variable
            //

            bool signalFound = false;
            bool speedlimFound = false;

            for (int i = 0; i < SignalObjectItems.Count && (!signalFound || !speedlimFound); i++)
            {
                if (!signalFound)
                {
                    SignalItemInfo signalInfo = SignalObjectItems[i];
                    if (signalInfo.ItemType == SignalItemType.Signal)
                    {
                        signalFound = true;
                        nextSignalIndex = i;
                    }
                }

                if (!speedlimFound)
                {
                    SignalItemInfo signalInfo = SignalObjectItems[i];
                    if (signalInfo.ItemType == SignalItemType.SpeedLimit)
                    {
                        speedlimFound = true;
                        nextSpeedLimitIndex = i;
                    }
                }
            }

            //
            // If signal in list, set signal reference,
            // else try to get first signal if in signal mode
            //
            NextSignalObject[0] = null;
            if (nextSignalIndex >= 0)
            {
                NextSignalObject[0] = SignalObjectItems[nextSignalIndex].SignalDetails;
                DistanceToSignal = SignalObjectItems[nextSignalIndex].DistanceToTrain;
            }
            else
            {
                SignalItemInfo firstSignalObject = Simulator.Instance.SignalEnvironment.GetNextObjectInRoute(RoutedForward, ValidRoute[0],
                    PresentPosition[Direction.Forward].RouteListIndex, PresentPosition[Direction.Forward].Offset, -1,
                    SignalItemType.Signal);

                if (firstSignalObject.State == SignalItemFindState.Item)
                {
                    NextSignalObject[0] = firstSignalObject.SignalDetails;
                    firstSignalObject.DistanceToTrain = firstSignalObject.DistanceFound;
                    DistanceToSignal = firstSignalObject.DistanceFound;
                }
            }

            //
            // determine actual speed limits depending on overall speed and type of train
            //

            UpdateSpeedInfo();
        }

        ///  Update the distance to and aspect of next signal
        protected void UpdateSignalState(int backward)
        {
            // for AUTO mode, use direction 0 only
            SignalItemFindState returnState = SignalItemFindState.Item;

            bool listChanged = false;
            bool signalFound = false;

            SignalItemInfo firstObject = null;

            // get distance to first object
            if (SignalObjectItems.Count > 0)
            {
                firstObject = SignalObjectItems[0];
                firstObject.DistanceToTrain = GetObjectDistanceToTrain(firstObject);

                // check if passed object - if so, remove object
                // if object is speed, set max allowed speed as distance travelled action
                while (firstObject.DistanceToTrain < 0.0f && SignalObjectItems.Count > 0)
                {
                    float temp1MaxSpeedMpS = IsFreight ? firstObject.SpeedInfo.FreightSpeed : firstObject.SpeedInfo.PassengerSpeed;
                    if (firstObject.SignalDetails.IsSignal)
                    {
                        allowedAbsoluteMaxSpeedSignalMpS = temp1MaxSpeedMpS == -1 ? (float)simulator.TRK.Route.SpeedLimit : temp1MaxSpeedMpS;
                    }
                    else if (!firstObject.SpeedInfo.Reset)
                    {
                        if (firstObject.SpeedInfo.LimitedSpeedReduction == 0) allowedAbsoluteMaxSpeedLimitMpS = temp1MaxSpeedMpS == -1 ? allowedAbsoluteMaxSpeedLimitMpS : temp1MaxSpeedMpS;
                        else allowedAbsoluteMaxTempSpeedLimitMpS = temp1MaxSpeedMpS == -1 ? allowedAbsoluteMaxTempSpeedLimitMpS : temp1MaxSpeedMpS;
                    }
                    else
                    {
                        allowedAbsoluteMaxSpeedSignalMpS = allowedAbsoluteMaxSpeedLimitMpS;
                    }

                    if (firstObject.ActualSpeed > 0)
                    {
                        if (firstObject.ActualSpeed <= AllowedMaxSpeedMpS)
                        {
                            AllowedMaxSpeedMpS = firstObject.ActualSpeed;
                            float tempMaxSpeedMps = AllowedMaxSpeedMpS;
                            if (!simulator.TimetableMode)
                            {
                                tempMaxSpeedMps = IsFreight ? firstObject.SpeedInfo.FreightSpeed : firstObject.SpeedInfo.PassengerSpeed;
                                if (tempMaxSpeedMps == -1f)
                                    tempMaxSpeedMps = AllowedMaxSpeedMpS;
                            }


                            if (firstObject.SignalDetails.IsSignal)
                            {
                                AllowedMaxSpeedSignalMpS = tempMaxSpeedMps;
                            }
                            else if (firstObject.SpeedInfo.LimitedSpeedReduction == 0)
                            {
                                AllowedMaxSpeedLimitMpS = tempMaxSpeedMps;
                            }
                            else
                            {
                                allowedMaxTempSpeedLimitMpS = tempMaxSpeedMps;
                            }
                            requiredActions.UpdatePendingSpeedlimits(AllowedMaxSpeedMpS);  // update any older pending speed limits
                        }
                        else
                        {
                            ActivateSpeedLimit speedLimit;
                            float reqDistance = DistanceTravelledM + Length;
                            if (firstObject.SignalDetails.IsSignal)
                            {
                                speedLimit = new ActivateSpeedLimit(reqDistance, -1f, firstObject.ActualSpeed);
                            }
                            else if (simulator.TimetableMode || !firstObject.SpeedInfo.Reset)
                            {
                                speedLimit = new ActivateSpeedLimit(reqDistance,
                                    firstObject.SpeedInfo.LimitedSpeedReduction == 0 ? firstObject.ActualSpeed : -1, -1f,
                                    firstObject.SpeedInfo.LimitedSpeedReduction == 0 ? -1 : firstObject.ActualSpeed);
                            }
                            else
                            {
                                speedLimit = new ActivateSpeedLimit(reqDistance, firstObject.ActualSpeed, firstObject.ActualSpeed);
                            }

                            requiredActions.InsertAction(speedLimit);
                            requiredActions.UpdatePendingSpeedlimits(firstObject.ActualSpeed);  // update any older pending speed limits
                        }
                    }
                    else if (!simulator.TimetableMode)
                    {
                        float tempMaxSpeedMps = IsFreight ? firstObject.SpeedInfo.FreightSpeed : firstObject.SpeedInfo.PassengerSpeed;
                        if (tempMaxSpeedMps >= 0)
                        {
                            if (firstObject.SignalDetails.IsSignal)
                            {
                                AllowedMaxSpeedSignalMpS = tempMaxSpeedMps;
                            }
                            else
                            {
                                if (firstObject.SpeedInfo.LimitedSpeedReduction == 0) AllowedMaxSpeedLimitMpS = tempMaxSpeedMps;
                                else allowedMaxTempSpeedLimitMpS = tempMaxSpeedMps;
                            }
                        }
                        else if (firstObject.SignalDetails.IsSignal)
                        {
                            AllowedMaxSpeedSignalMpS = allowedAbsoluteMaxSpeedSignalMpS;
                        }
                    }

                    if (firstObject.SignalDetails == NextSignalObject[0])
                    {
                        NextSignalObject[0] = null;
                    }

                    SignalObjectItems.RemoveAt(0);
                    listChanged = true;

                    if (SignalObjectItems.Count > 0)
                    {
                        firstObject = SignalObjectItems[0];
                        firstObject.DistanceToTrain = GetObjectDistanceToTrain(firstObject);
                    }
                }

                // if moving backward, check signals have been passed
                if (backward > BackwardThreshold)
                {
                    bool noMoreNewSignals = false;

                    int routeIndex = PresentPosition[Direction.Forward].RouteListIndex;
                    float offset = PresentPosition[Direction.Forward].Offset;

                    while (!noMoreNewSignals)
                    {
                        SignalItemInfo newObjectItem = Simulator.Instance.SignalEnvironment.GetNextObjectInRoute(RoutedForward, ValidRoute[0],
                           routeIndex, offset, -1, SignalItemType.Signal);

                        returnState = newObjectItem.State;
                        if (returnState == SignalItemFindState.Item)
                        {
                            int newSignalIndex = newObjectItem.SignalDetails.Index;

                            noMoreNewSignals = NextSignalObject[0] == null || (newSignalIndex == NextSignalObject[0].Index);

                            if (!noMoreNewSignals)
                            {
                                if (SignalObjectItems.Count > 0)  // reset distance to train to distance to object //
                                {
                                    firstObject = SignalObjectItems[0];
                                    firstObject.DistanceToObject =
                                        firstObject.DistanceToTrain - newObjectItem.DistanceToTrain;
                                }

                                SignalObjectItems.Insert(0, newObjectItem);
                                listChanged = true;

                                int foundIndex = ValidRoute[0].GetRouteIndex(newObjectItem.SignalDetails.TrackCircuitNextIndex, routeIndex);
                                if (foundIndex > 0)
                                {
                                    routeIndex = foundIndex;
                                    offset = 0.0f;
                                }
                            }
                        }
                        else
                        {
                            noMoreNewSignals = true;
                        }
                    }
                }
            }

            // if no objects left on list, find first object whatever the distance
            if (SignalObjectItems.Count <= 0)
            {
                firstObject = Simulator.Instance.SignalEnvironment.GetNextObjectInRoute(RoutedForward, ValidRoute[0],
                      PresentPosition[Direction.Forward].RouteListIndex, PresentPosition[Direction.Forward].Offset, -1,
                      SignalItemType.Any);

                returnState = firstObject.State;
                if (returnState == SignalItemFindState.Item)
                {
                    firstObject.DistanceToTrain = firstObject.DistanceFound;
                    SignalObjectItems.Add(firstObject);
                }
            }

            // reset next signal object if none found
            if (SignalObjectItems.Count <= 0 || (SignalObjectItems.Count == 1 && SignalObjectItems[0].ItemType == SignalItemType.SpeedLimit))
            {
                NextSignalObject[0] = null;
                DistanceToSignal = null;
                listChanged = true;
            }

            // process further if any object available
            if (SignalObjectItems.Count > 0)
            {

                // Update state and speed of first object if signal
                if (firstObject.SignalDetails.IsSignal)
                {
                    firstObject.SignalState = firstObject.SignalDetails.SignalLR(SignalFunction.Normal);
                    firstObject.SpeedInfo = new SpeedInfo(firstObject.SignalDetails.SignalSpeed(SignalFunction.Normal));
                }
                else if (firstObject.SignalDetails.SignalHeads[0]?.SignalFunction == SignalFunction.Speed)// check if object is SPEED info signal
                {
                    firstObject.SpeedInfo = new SpeedInfo(firstObject.SignalDetails.SignalSpeed(SignalFunction.Speed));
                }

                // Update all objects in list (except first)
                float lastDistance = firstObject.DistanceToTrain;

                SignalItemInfo prevObject = firstObject;

                foreach (SignalItemInfo nextObject in SignalObjectItems.Skip(1))
                {
                    nextObject.DistanceToTrain = prevObject.DistanceToTrain + nextObject.DistanceToObject;
                    lastDistance = nextObject.DistanceToTrain;

                    if (nextObject.SignalDetails.IsSignal)
                    {
                        nextObject.SignalState = nextObject.SignalDetails.SignalLR(SignalFunction.Normal);
                        if (nextObject.SignalDetails.EnabledTrain != null && nextObject.SignalDetails.EnabledTrain.Train != this)
                            nextObject.SignalState = SignalAspectState.Stop; // state not valid if not enabled for this train
                        nextObject.SpeedInfo = new SpeedInfo(nextObject.SignalState == SignalAspectState.Stop ? null : nextObject.SignalDetails.SignalSpeed(SignalFunction.Normal));
                    }
                    else if (nextObject.SignalDetails.SignalHeads[0].SignalFunction == SignalFunction.Speed) // check if object is SPEED info signal
                    {
                        nextObject.SpeedInfo = new SpeedInfo(nextObject.SignalDetails.SignalSpeed(SignalFunction.Speed));
                    }

                    prevObject = nextObject;
                }

                // check if last signal aspect is STOP, and if last signal is enabled for this train
                // If so, no check on list is required
                SignalAspectState nextAspect = SignalAspectState.Unknown;

                for (int i = SignalObjectItems.Count - 1; i >= 0 && !signalFound; i--)
                {
                    SignalItemInfo nextObject = SignalObjectItems[i];
                    if (nextObject.ItemType == SignalItemType.Signal)
                    {
                        signalFound = true;
                        nextAspect = nextObject.SignalState;
                    }
                }

                // get next items within max distance; longer for player train to provide correct TCS handling
                int routeListIndex = PresentPosition[Direction.Forward].RouteListIndex;
                int lastIndex = routeListIndex;
                float offset = PresentPosition[Direction.Forward].Offset;

                prevObject = SignalObjectItems[SignalObjectItems.Count - 1];  // last object

                while (lastDistance < MaxDistanceCheckedAhead &&
                          returnState == SignalItemFindState.Item &&
                          nextAspect != SignalAspectState.Stop)
                {

                    Signal prevSignal = prevObject.SignalDetails;
                    int reqTCReference = prevSignal.TrackCircuitIndex;
                    float reqOffset = prevSignal.TrackCircuitOffset + 0.0001f;   // make sure you find NEXT object ! //

                    if (prevSignal.TrackCircuitNextIndex > 0 && ValidRoute[0].GetRouteIndex(prevSignal.TrackCircuitNextIndex, lastIndex) > 0)
                    {
                        reqTCReference = prevSignal.TrackCircuitNextIndex;
                        reqOffset = 0.0f;
                    }

                    int foundSection = ValidRoute[0].GetRouteIndex(reqTCReference, lastIndex);
                    if (foundSection >= 0)
                    {
                        lastIndex = foundSection;
                        offset = reqOffset;
                    }

                    SignalItemInfo nextObject = Simulator.Instance.SignalEnvironment.GetNextObjectInRoute(RoutedForward, ValidRoute[0], lastIndex, offset, -1, SignalItemType.Any);

                    returnState = nextObject.State;

                    if (returnState == SignalItemFindState.Item)
                    {
                        nextObject.DistanceToObject = nextObject.DistanceFound;
                        nextObject.DistanceToTrain = prevObject.DistanceToTrain + nextObject.DistanceToObject;

                        lastDistance = nextObject.DistanceToTrain;
                        SignalObjectItems.Add(nextObject);

                        if (nextObject.SignalDetails.IsSignal)
                        {
                            nextObject.SignalState = nextObject.SignalDetails.SignalLR(SignalFunction.Normal);
                            nextAspect = nextObject.SignalState;
                            nextObject.SpeedInfo = new SpeedInfo(nextObject.SignalDetails.SignalSpeed(SignalFunction.Normal));
                        }
                        else if (nextObject.SignalDetails.SignalHeads != null)  // check if object is SPEED info signal
                        {
                            if (nextObject.SignalDetails.SignalHeads[0].SignalFunction == SignalFunction.Speed)
                            {
                                nextObject.SpeedInfo = new SpeedInfo(nextObject.SignalDetails.SignalSpeed(SignalFunction.Speed));
                            }
                        }

                        prevObject = nextObject;
                        listChanged = true;
                    }
                }

                // check if IndexNextSignal still valid, if not, force list changed
                if (nextSignalIndex >= SignalObjectItems.Count)
                {
                    listChanged = true;
                }
            }

            // if list is changed, get new indices to first signal and speedpost
            if (listChanged)
            {
                signalFound = false;
                bool speedlimFound = false;
                nextSignalIndex = -1;
                nextSpeedLimitIndex = -1;
                NextSignalObject[0] = null;

                for (int i = 0; i < SignalObjectItems.Count && (!signalFound || !speedlimFound); i++)
                {
                    SignalItemInfo nextObject = SignalObjectItems[i];
                    if (!signalFound && nextObject.ItemType == SignalItemType.Signal)
                    {
                        signalFound = true;
                        nextSignalIndex = i;
                    }
                    else if (!speedlimFound && nextObject.ItemType == SignalItemType.SpeedLimit)
                    {
                        speedlimFound = true;
                        nextSpeedLimitIndex = i;
                    }
                }
            }

            // check if any signal in list, if not get direct from train
            // get state and details
            if (nextSignalIndex < 0)
            {
                SignalItemInfo firstSignalObject = Simulator.Instance.SignalEnvironment.GetNextObjectInRoute(RoutedForward, ValidRoute[0],
                        PresentPosition[Direction.Forward].RouteListIndex, PresentPosition[Direction.Forward].Offset, -1,
                        SignalItemType.Signal);

                if (firstSignalObject.State == SignalItemFindState.Item)
                {
                    NextSignalObject[0] = firstSignalObject.SignalDetails;
                    firstSignalObject.DistanceToTrain = firstSignalObject.DistanceFound;
                }
            }
            else
            {
                NextSignalObject[0] = SignalObjectItems[nextSignalIndex].SignalDetails;
            }

            //
            // update distance of signal if out of list
            //
            if (nextSignalIndex >= 0)
            {
                DistanceToSignal = SignalObjectItems[nextSignalIndex].DistanceToTrain;
            }
            else if (NextSignalObject[0] != null)
            {
                DistanceToSignal = NextSignalObject[0].DistanceTo(FrontTDBTraveller);
            }
            else if (ControlMode != TrainControlMode.AutoNode)
            {
                bool validModeSwitch = true;

                if (this is AITrain aiTrain)
                {
                    // do not switch to node control if train is set for auxiliary action
                    if (aiTrain.nextActionInfo?.NextAction == AIActionItem.AI_ACTION_TYPE.AUX_ACTION)
                    {
                        validModeSwitch = false;
                    }
                }

                if (validModeSwitch)
                {
                    SwitchToNodeControl(LastReservedSection[0]);
                }
            }

            // determine actual speed limits depending on overall speed and type of train

            UpdateSpeedInfo();
        }

        /// set actual speed limit for all objects depending on state and type of train
        private void UpdateSpeedInfo()
        {
            float validSpeedMpS = AllowedMaxSpeedMpS;
            float validSpeedSignalMpS = AllowedMaxSpeedSignalMpS;
            float validSpeedLimitMpS = AllowedMaxSpeedLimitMpS;
            float validTempSpeedLimitMpS = allowedMaxTempSpeedLimitMpS;

            // update valid speed with pending actions
            foreach (DistanceTravelledItem distanceAction in requiredActions)
            {
                if (distanceAction is ActivateSpeedLimit speedLimit)
                {
                    if (speedLimit.MaxSpeedMpSLimit > validSpeedLimitMpS)
                    {
                        validSpeedLimitMpS = speedLimit.MaxSpeedMpSLimit;
                    }

                    if (speedLimit.MaxSpeedMpSSignal > validSpeedSignalMpS)
                    {
                        validSpeedSignalMpS = speedLimit.MaxSpeedMpSSignal;
                    }
                    if (speedLimit.MaxTempSpeedMpSLimit > validTempSpeedLimitMpS)
                    {
                        validTempSpeedLimitMpS = speedLimit.MaxTempSpeedMpSLimit;
                    }
                }
            }

            // loop through objects

            foreach (SignalItemInfo signalInfo in SignalObjectItems)
            {
                //
                // select speed on type of train 
                //

                float actualSpeedMpS = IsFreight ? signalInfo.SpeedInfo.FreightSpeed : signalInfo.SpeedInfo.PassengerSpeed;

                if (signalInfo.SignalDetails.IsSignal)
                {
                    if (actualSpeedMpS > 0 && (signalInfo.SpeedInfo.Flag || !simulator.TimetableMode))
                    {
                        validSpeedSignalMpS = actualSpeedMpS;
                        if (validSpeedSignalMpS > Math.Min(validSpeedLimitMpS, validTempSpeedLimitMpS))
                        {
                            if (validSpeedMpS < Math.Min(validSpeedLimitMpS, validTempSpeedLimitMpS))
                            {
                                actualSpeedMpS = Math.Min(validSpeedLimitMpS, validTempSpeedLimitMpS);
                            }
                            else
                            {
                                actualSpeedMpS = -1;
                            }
                        }
                    }
                    else
                    {
                        validSpeedSignalMpS = TrainMaxSpeedMpS;
                        float newSpeedMpS = Math.Min(validSpeedSignalMpS, Math.Min(validSpeedLimitMpS, validTempSpeedLimitMpS));

                        if (newSpeedMpS != validSpeedMpS)
                        {
                            actualSpeedMpS = newSpeedMpS;
                        }
                        else
                        {
                            actualSpeedMpS = -1;
                        }
                    }
                    signalInfo.ActualSpeed = actualSpeedMpS;
                    if (actualSpeedMpS > 0)
                    {
                        validSpeedMpS = actualSpeedMpS;
                    }
                }
                else if (simulator.TimetableMode)
                {
                    {
                        if (actualSpeedMpS > 998f)
                        {
                            actualSpeedMpS = TrainMaxSpeedMpS;
                        }

                        if (actualSpeedMpS > 0)
                        {
                            validSpeedMpS = actualSpeedMpS;
                            validSpeedLimitMpS = actualSpeedMpS;
                        }
                        else if (actualSpeedMpS < 0 && signalInfo.SpeedInfo.Reset)
                        {
                            validSpeedMpS = validSpeedLimitMpS;
                            actualSpeedMpS = validSpeedLimitMpS;
                        }

                        signalInfo.ActualSpeed = Math.Min(actualSpeedMpS, TrainMaxSpeedMpS);
                    }
                }
                else  // Enhanced Compatibility on & SpeedLimit
                {
                    if (actualSpeedMpS > 998f)
                    {
                        actualSpeedMpS = (float)simulator.TRK.Route.SpeedLimit;
                    }

                    if (actualSpeedMpS > 0)
                    {
                        float tempValidSpeedSignalMpS = validSpeedSignalMpS == -1 ? 999 : validSpeedSignalMpS;
                        if (signalInfo.SpeedInfo.LimitedSpeedReduction == 0)
                        {
                            validSpeedLimitMpS = actualSpeedMpS;
                            if (actualSpeedMpS > Math.Min(tempValidSpeedSignalMpS, validTempSpeedLimitMpS))
                            {
                                if (validSpeedMpS < Math.Min(tempValidSpeedSignalMpS, validTempSpeedLimitMpS))
                                {
                                    actualSpeedMpS = Math.Min(tempValidSpeedSignalMpS, validTempSpeedLimitMpS);
                                }
                                else
                                {
                                    actualSpeedMpS = -1;
                                }
                            }
                        }
                        else
                        {
                            validTempSpeedLimitMpS = actualSpeedMpS;
                            if (actualSpeedMpS > Math.Min(tempValidSpeedSignalMpS, validSpeedLimitMpS))
                            {
                                if (validSpeedMpS < Math.Min(tempValidSpeedSignalMpS, validSpeedLimitMpS))
                                {
                                    actualSpeedMpS = Math.Min(tempValidSpeedSignalMpS, validSpeedLimitMpS);
                                }
                                else
                                {
                                    actualSpeedMpS = -1;
                                }
                            }
                        }
                    }
                    else if (actualSpeedMpS < 0 && !signalInfo.SpeedInfo.Reset)
                    {
                        float newSpeedMpS1 = Math.Min(validSpeedSignalMpS, Math.Min(validSpeedLimitMpS, validTempSpeedLimitMpS));

                        if (newSpeedMpS1 != validSpeedMpS)
                        {
                            actualSpeedMpS = newSpeedMpS1;
                        }
                        else
                        {
                            actualSpeedMpS = -1;
                        }
                    }
                    else if (signalInfo.SpeedInfo.Reset)
                    {
                        actualSpeedMpS = validSpeedLimitMpS;
                    }

                    signalInfo.ActualSpeed = actualSpeedMpS;
                    if (actualSpeedMpS > 0)
                    {
                        validSpeedMpS = actualSpeedMpS;
                    }
                }
            }
        }

        /// Set retainers
        internal void SetRetainers(bool increase)
        {
            if (Math.Abs(SpeedMpS) > 0.1)
                return;
            if (!increase)
            {
                RetainerSetting = RetainerSetting.Exhaust;
                RetainerPercent = 100;
            }
            else if (RetainerPercent < 100)
                RetainerPercent *= 2;
            else if (RetainerSetting != RetainerSetting.SlowDirect)
            {
                RetainerPercent = 25;
                switch (RetainerSetting)
                {
                    case RetainerSetting.Exhaust:
                        RetainerSetting = RetainerSetting.LowPressure;
                        break;
                    case RetainerSetting.LowPressure:
                        RetainerSetting = RetainerSetting.HighPressure;
                        break;
                    case RetainerSetting.HighPressure:
                        RetainerSetting = RetainerSetting.SlowDirect;
                        break;
                }
            }

            (_, int last) = FindLeadLocomotives();
            int step = 100 / RetainerPercent;
            for (int i = 0; i < Cars.Count; i++)
            {
                int j = Cars.Count - 1 - i;
                if (j <= last)
                    break;
                Cars[j].BrakeSystem.SetRetainer(i % step == 0 ? RetainerSetting : RetainerSetting.Exhaust);
            }
        }

        /// Find lead locomotive
        internal (int first, int last) FindLeadLocomotives()
        {
            // FindLeadLocomotives stores the index of a single locomotive, or alternatively multiple locomotives, such as 
            // in the case of MU'd diesel units, the "first" and "last" values enclose the group of locomotives where the 
            // lead locomotive (the player driven one) resides. Within this group both the main reservoir pressure and the 
            // engine brake pipe pressure will be propagated. It only identifies multiple units when coupled directly together,
            // for example a double headed steam locomotive will most often have a tender separating the two locomotives, 
            // so the second locomotive will not be identified, nor will a locomotive added at the rear of the train. 

            int first = -1;
            int last = -1;
            if (LeadLocomotiveIndex >= 0)
            {
                for (int i = LeadLocomotiveIndex; i < Cars.Count && Cars[i].IsDriveable; i++)
                    last = i;
                for (int i = LeadLocomotiveIndex; i >= 0 && Cars[i].IsDriveable; i--)
                    first = i;
            }

            // If first (lead) locomotive is a steam locomotive check to see if the engine brake needs to be extended to cover the tender

            if (first != -1) // if lead locomotive is set at initialised value, then don't attempt to process engine brake extension
            {

                if (Cars[first] is MSTSSteamLocomotive)
                {

                    // If double headed tank steam locomotive (no tender is attached) then only apply engine brake to first locomotive for consistency
                    if (last != first && Cars[first] is MSTSSteamLocomotive && Cars[last] is MSTSSteamLocomotive)
                    {
                        last = first; // Reduce locomotive lead values to apply engine brakes only to lead locomotive, and not 2nd locomotive.
                    }
                    else // if last = first, ie only a single locomotive (can be two locomotives separated by a tender as 2nd locomotive is not counted in the first / last values.
                    {
                        if (last < Cars.Count - 1)  // Check that there are cars after the locomotive, if not skip extending brake to tender
                        {
                            if (last == first && Cars[first] is MSTSSteamLocomotive && Cars[first + 1].WagonType == TrainCar.WagonTypes.Tender)
                            {
                                last += 1;      // If a "standard" single steam locomotive with a tender then for the purposes of braking increment last above first by one
                            }
                        }
                    }
                }
            }
            return (first, last);
        }

        internal TrainCar FindLeadLocomotive()
        {
            (int first, int last) = FindLeadLocomotives();
            if (first > -1 && first < LeadLocomotiveIndex)
            {
                return Cars[first];
            }
            else if (last > -1 && last > LeadLocomotiveIndex)
            {
                return Cars[last];
            }
            for (int i = 0; i < Cars.Count; i++)
            {
                if (Cars[i].IsDriveable)
                    return Cars[i];
            }
            Trace.TraceWarning($"Train {Name} ({Number}) has no locomotive!");
            return null;
        }

        /// Check if train is passenger or freight train
        public void CheckFreight()
        {
            IsFreight = false;
            PassengerCarsNumber = 0;
            IsPlayable = false;

            foreach (TrainCar car in Cars)
            {
                if (car.WagonType == TrainCar.WagonTypes.Freight)
                    IsFreight = true;
                if ((car.WagonType == TrainCar.WagonTypes.Passenger) || (car.IsDriveable && car.HasPassengerCapacity))
                    PassengerCarsNumber++;
                if (car.IsDriveable && (car as MSTSLocomotive).CabViewList.Count > 0)
                    IsPlayable = true;
            }
            if (TrainType == TrainType.AiIncorporated && IncorporatingTrainNo > -1)
                IsPlayable = true;
        } // CheckFreight

        /// Cars have been added to the rear of the train, recalc the rearTDBtraveller
        internal void RepositionRearTraveller()
        {
            Traveller traveller = new Traveller(FrontTDBTraveller, Traveller.TravellerDirection.Backward);
            // The traveller location represents the front of the train.
            float length = 0f;

            // process the cars first to last
            for (int i = 0; i < Cars.Count; ++i)
            {
                TrainCar car = Cars[i];
                if (car.WheelAxlesLoaded)
                {
                    car.ComputePosition(traveller, false, 0, 0, SpeedMpS);
                }
                else
                {
                    float bogieSpacing = car.CarLengthM * 0.65f;  // we'll use this approximation since the wagfile doesn't contain info on bogie position

                    // traveller is positioned at the front of the car
                    // advance to the first bogie 
                    traveller.Move((car.CarLengthM - bogieSpacing) / 2.0f);
                    int tileX = traveller.TileX;
                    int tileZ = traveller.TileZ;
                    float x = traveller.X;
                    float y = traveller.Y;
                    float z = traveller.Z;
                    traveller.Move(bogieSpacing);

                    // normalize across tile boundaries
                    while (tileX > traveller.TileX)
                    {
                        x += TileSize;
                        --tileX;
                    }
                    while (tileX < traveller.TileX)
                    {
                        x -= TileSize;
                        ++tileX;
                    }
                    while (tileZ > traveller.TileZ)
                    {
                        z += TileSize;
                        --tileZ;
                    }
                    while (tileZ < traveller.TileZ)
                    {
                        z -= TileSize;
                        ++tileZ;
                    }

                    // note the railcar sits 0.275meters above the track database path  TODO - is this always consistent?
                    Matrix flipMatrix = Matrix.Identity;
                    if (!car.Flipped)
                    {
                        //  Rotate matrix 180' around Y axis.
                        flipMatrix.M11 = -1;
                        flipMatrix.M33 = -1;
                    }
                    car.WorldPosition = new WorldPosition(traveller.TileX, traveller.TileZ, MatrixExtension.Multiply(flipMatrix, Simulator.XNAMatrixFromMSTSCoordinates(traveller.X, traveller.Y + 0.275f, traveller.Z, x, y + 0.275f, z)));
                    traveller.Move((car.CarLengthM - bogieSpacing) / 2.0f);
                }
                if (i < Cars.Count - 1)
                {
                    traveller.Move(car.CouplerSlackM + car.GetCouplerZeroLengthM());
                    length += car.CouplerSlackM + car.GetCouplerZeroLengthM();
                }
                length += car.CarLengthM;
            }

            traveller.ReverseDirection();
            RearTDBTraveller = traveller;
            Length = length;
        } // RepositionRearTraveller

        public void CalculatePositionOfCars()
        {
            CalculatePositionOfCars(0, 0);
        }

        /// Distance is the signed distance the cars are moving.
        /// </summary>
        internal void CalculatePositionOfCars(double elapsedTime, double distance)
        {
            if (double.IsNaN(distance))
                distance = 0;//sanity check

            RearTDBTraveller.Move(distance);

            // TODO : check if train moved back into previous section
            Traveller traveller = new Traveller(RearTDBTraveller);
            // The traveller location represents the back of the train.
            float length = 0f;

            // process the cars last to first
            for (int i = Cars.Count - 1; i >= 0; --i)
            {
                TrainCar car = Cars[i];
                if (i < Cars.Count - 1)
                {
                    traveller.Move(car.CouplerSlackM + car.GetCouplerZeroLengthM());
                    length += car.CouplerSlackM + car.GetCouplerZeroLengthM();
                }
                if (car.WheelAxlesLoaded)
                {
                    car.ComputePosition(traveller, true, elapsedTime, distance, SpeedMpS);
                }
                else
                {
                    float bogieSpacing = car.CarLengthM * 0.65f;  // we'll use this approximation since the wagfile doesn't contain info on bogie position

                    // traveller is positioned at the back of the car
                    // advance to the first bogie 
                    traveller.Move((car.CarLengthM - bogieSpacing) / 2.0f);
                    int tileX = traveller.TileX;
                    int tileZ = traveller.TileZ;
                    float x = traveller.X;
                    float y = traveller.Y;
                    float z = traveller.Z;
                    traveller.Move(bogieSpacing);

                    // normalize across tile boundaries
                    while (tileX > traveller.TileX)
                    {
                        x += TileSize;
                        --tileX;
                    }
                    while (tileX < traveller.TileX)
                    {
                        x -= TileSize;
                        ++tileX;
                    }
                    while (tileZ > traveller.TileZ)
                    {
                        z += TileSize;
                        --tileZ;
                    }
                    while (tileZ < traveller.TileZ)
                    {
                        z -= TileSize;
                        ++tileZ;
                    }


                    // note the railcar sits 0.275meters above the track database path  TODO - is this always consistent?
                    Matrix flipMatrix = Matrix.Identity;
                    if (car.Flipped)
                    {
                        //  Rotate matrix 180' around Y axis.
                        flipMatrix.M11 = -1;
                        flipMatrix.M33 = -1;
                    }
                    car.WorldPosition = new WorldPosition(traveller.TileX, traveller.TileZ,
                        MatrixExtension.Multiply(flipMatrix, Simulator.XNAMatrixFromMSTSCoordinates(traveller.X, traveller.Y + 0.275f, traveller.Z, x, y + 0.275f, z)));

                    traveller.Move((car.CarLengthM - bogieSpacing) / 2.0f);  // Move to the front of the car 

                    car.UpdatedTraveler(traveller, elapsedTime, distance, SpeedMpS);
                }
                length += car.CarLengthM;
            }

            FrontTDBTraveller = traveller;
            Length = length;
            DistanceTravelled += (float)distance;
        } // CalculatePositionOfCars

        /// Update Car speeds
        protected void UpdateCarSpeeds(double elapsedTime)
        {
            // The train speed is calculated by averaging all the car speeds. The individual car speeds are calculated from the TotalForce acting on each car. 
            // Typically the TotalForce consists of the MotiveForce or Gravitational forces (though other forces like friction have a small impact as well).
            // At stop under normal circumstances the BrakeForce exceeds the TotalForces, and therefore the wagon is "held in a stationary position". 
            // In the case of "air_piped" wagons which have no BrakeForces acting on them, the car is not held stationary, and each car shows a small speed vibration in either direction.
            // To overcome this any "air_piped and vacuum_piped" cars are forced to zero speed if the preceeding car is stationary.
            int n = 0;
            float prevCarSpeedMps = 0.0f;
            float nextCarSpeedMps = 0.0f;
            bool locoBehind = true;
            for (int i = 0; i < Cars.Count; i++)
            {
                TrainCar car = Cars[i];
                if (i < Cars.Count - 1)
                    nextCarSpeedMps = Cars[i + 1].SpeedMpS;
                if (TrainMaxSpeedMpS <= 0f && car is MSTSLocomotive locomotive)
                {
                    TrainMaxSpeedMpS = locomotive.MaxSpeedMpS;
                    locoBehind = false;
                }
                if (car.SpeedMpS > 0)
                {
                    car.SpeedMpS += (float)(car.TotalForceN / car.MassKG * elapsedTime);
                    if (car.SpeedMpS < 0)
                        car.SpeedMpS = 0;
                    // If car is manual braked, air_piped car or vacuum_piped, and preceeding car is at stop, then set speed to zero.  
                    // These type of cars do not have any brake force to hold them still
                    if ((car.CarBrakeSystemType == "air_piped" || car.CarBrakeSystemType == "vacuum_piped" || car.CarBrakeSystemType == "manual_braking") && (locoBehind ? n != Cars.Count - 1 && nextCarSpeedMps == 0 : n != 0 && prevCarSpeedMps == 0))
                    {
                        car.SpeedMpS = 0;
                    }
                    prevCarSpeedMps = car.SpeedMpS;
                }
                else if (car.SpeedMpS < 0)
                {
                    car.SpeedMpS += (float)(car.TotalForceN / car.MassKG * elapsedTime);
                    if (car.SpeedMpS > 0)
                        car.SpeedMpS = 0;
                    // If car is manual braked, air_piped car or vacuum_piped, and preceeding car is at stop, then set speed to zero.  
                    // These type of cars do not have any brake force to hold them still
                    if ((car.CarBrakeSystemType == "air_piped" || car.CarBrakeSystemType == "vacuum_piped" || car.CarBrakeSystemType == "manual_braking") && (locoBehind ? n != Cars.Count - 1 && nextCarSpeedMps == 0 : n != 0 && prevCarSpeedMps == 0))
                    {
                        car.SpeedMpS = 0;
                    }
                    prevCarSpeedMps = car.SpeedMpS;
                }
                else // if speed equals zero
                    prevCarSpeedMps = car.SpeedMpS;
                n++;
#if DEBUG_SPEED_FORCES
                Trace.TraceInformation(" ========================================  Train Speed #2 (Train.cs) ===========================================================");
                Trace.TraceInformation("Car ID {0} TotalForceN {1} Mass {2} elapsedtime {3} CarSpeed {4}", car.CarID, car.TotalForceN, car.MassKG, elapsedTime, car.SpeedMpS);
                Trace.TraceInformation("Friction {0} Brake {1} Curve {2} Wind {3} Tunnel {4}", car.FrictionForceN, car.BrakeForceN, car.CurveForceN, car.WindForceN, car.TunnelForceN);
                Trace.TraceInformation("Coupler {0} Prev Car Speed {1}", car.CouplerForceU, PrevCarSpeedMps);
                Trace.TraceInformation("Calculated Total {0}", car.FrictionForceN + car.BrakeForceN + car.CurveForceN + car.WindForceN + car.TunnelForceN);
#endif
            }
            if (n == 0)
                return;

            //
            // start a car moving forward when it is stationary, once it is moving this whole section is skipped
            //
            for (int i = 0; i < Cars.Count; i++)
            {
                TrainCar car = Cars[i];
                if (car.SpeedMpS != 0 || car.TotalForceN <= (car.FrictionForceN + car.BrakeForceN + car.CurveForceN + car.WindForceN + car.TunnelForceN))
                {
                    // Skip this section to start car if car is already moving, or force not sufficient to start it moving
                    continue;
                }
                int j = i;
                float f = 0;
                float m = 0;
                for (; ; )
                {
                    // Cycle down the train consist until the first stationary car is found that has its leading couplers starting to pull it. The next car is then started by allowing its speed to increase above 0.
                    f += car.TotalForceN - (car.FrictionForceN + car.BrakeForceN + car.CurveForceN + car.WindForceN + car.TunnelForceN);
                    m += car.MassKG;
                    if (car.IsPlayerTrain && !simulator.Settings.SimpleControlPhysics && car.IsAdvancedCoupler) // "Advanced coupler" - operates in three extension zones
                    {
                        if (j == Cars.Count - 1 || car.CouplerSlackM < car.AdvancedCouplerDynamicTensionSlackLimitM)
                            break;
                    }
                    else // Simple coupler
                    {
                        if (j == Cars.Count - 1 || car.CouplerSlackM < car.GetMaximumSimpleCouplerSlack2M())
                            break;
                    }
                    j++;
                    // Increment count to next car.
                    car = Cars[j];
                }
                if (f > 0)
                {
                    for (int k = i; k <= j; k++)
                    {
                        if ((Cars[k].CarBrakeSystemType == "air_piped" || Cars[k].CarBrakeSystemType == "vacuum_piped" || car.CarBrakeSystemType == "manual_braking") && FirstCar.SpeedMpS > 0 && Cars[k - 1].SpeedMpS == 0.0)
                        {
                            // If is manual braked, air_piped car or vacuum_piped, and preceeding car is at stop, then set speed to zero.  These type of cars do not have any brake force to hold them still
                            Cars[k].SpeedMpS = 0.0f;
                        }
                        else
                        {
                            // Start this stationary car
                            Cars[k].SpeedMpS = f / m * (float)elapsedTime;
                        }

                    }
                    n -= j - i + 1;
                }
            }
            if (n == 0)
                return;
            //
            // start cars moving backward when it is stationary, once it is moving it skips this whole section
            //
            for (int i = Cars.Count - 1; i >= 0; i--)
            {
                TrainCar car = Cars[i];
                if (car.SpeedMpS != 0 || car.TotalForceN > (-1.0f * (car.FrictionForceN + car.BrakeForceN + car.CurveForceN + car.WindForceN + car.TunnelForceN)))
                {
                    // Skip this section to start car if car is already moving, or force not sufficient to start it moving
                    continue;
                }
                int j = i;
                float f = 0;
                float m = 0;
                for (; ; )
                {
                    // Cycle up the train consist until the first stationary car is found that has its leading couplers starting to pull it. The next car is then started by allowing its speed to increase above 0.
                    f += car.TotalForceN + car.FrictionForceN + car.BrakeForceN + car.CurveForceN + car.WindForceN + car.TunnelForceN;
                    m += car.MassKG;
                    if (car.IsPlayerTrain && !simulator.Settings.SimpleControlPhysics && car.IsAdvancedCoupler) // "Advanced coupler" - operates in three extension zones
                    {
                        if (j == 0 || car.CouplerSlackM > car.AdvancedCouplerDynamicCompressionSlackLimitM)
                            break;
                    }
                    else // Simple coupler
                    {
                        if (j == 0 || car.CouplerSlackM > -car.GetMaximumSimpleCouplerSlack2M())
                            break;
                    }
                    j--;
                    // Decrement the count so that next car is started
                    car = Cars[j];
                }
                if (f < 0)
                {
                    for (int k = j; k <= i; k++)
                    {

                        if ((Cars[k].CarBrakeSystemType == "air_piped" || Cars[k].CarBrakeSystemType == "vacuum_piped" || car.CarBrakeSystemType == "manual_braking") && FirstCar.SpeedMpS > 0 && Cars[k - 1].SpeedMpS == 0.0)
                        {
                            // If is manual braked, air_piped car or vacuum_piped, and preceeding car is at stop, then set speed to zero.  These type of cars do not have any brake force to hold them still
                            Cars[k].SpeedMpS = 0.0f;
                        }
                        else
                        {
                            // Start this stationary car
                            Cars[k].SpeedMpS = f / m * (float)elapsedTime;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Calculate initial position
        /// </summary>
        internal virtual TrackCircuitPartialPathRoute CalculateInitialTrainPosition()
        {

            // calculate train length

            float trainLength = 0f;

            for (int i = Cars.Count - 1; i >= 0; --i)
            {
                TrainCar car = Cars[i];
                if (i < Cars.Count - 1)
                {
                    trainLength += car.CouplerSlackM + car.GetCouplerZeroLengthM();
                }
                trainLength += car.CarLengthM;
            }

            // get starting position and route

            TrackNode tn = RearTDBTraveller.TN;
            float offset = RearTDBTraveller.TrackNodeOffset;
            TrackDirection direction = (TrackDirection)RearTDBTraveller.Direction;

            PresentPosition[Direction.Backward].SetPosition(tn.TrackCircuitCrossReferences, offset, direction);
            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[PresentPosition[Direction.Backward].TrackCircuitSectionIndex];
            offset = PresentPosition[Direction.Backward].Offset;

            //<CSComment> must do preliminary calculation of PresentPosition[Direction.Forward] parameters in order to use subsequent code
            // limited however to case of train fully in one section to avoid placement ambiguities </CSComment>
            float offsetFromEnd = section.Length - (Length + offset);
            if (PresentPosition[Direction.Forward].TrackCircuitSectionIndex == -1 && offsetFromEnd >= 0) // train is fully in one section
            {
                PresentPosition[Direction.Forward].Direction = PresentPosition[Direction.Backward].Direction;
                PresentPosition[Direction.Forward].TrackCircuitSectionIndex = PresentPosition[Direction.Backward].TrackCircuitSectionIndex;
                PresentPosition[Direction.Forward].Offset = PresentPosition[Direction.Backward].Offset + trainLength;
            }

            // create route if train has none
            if (ValidRoute[0] == null)
            {
                ValidRoute[0] = SignalEnvironment.BuildTempRoute(this, section.Index, PresentPosition[Direction.Backward].Offset,
                            PresentPosition[Direction.Backward].Direction, trainLength, true, true, false);
            }

            // find sections
            bool sectionAvailable = true;
            float remLength = trainLength;
            int routeIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Backward].TrackCircuitSectionIndex, 0);
            if (routeIndex < 0)
                routeIndex = 0;

            bool sectionsClear = true;

            TrackCircuitPartialPathRoute tempRoute = new TrackCircuitPartialPathRoute();

            TrackCircuitRouteElement routeElement = ValidRoute[0][routeIndex];
            section = routeElement.TrackCircuitSection;
            if (!section.CanPlaceTrain(this, offset, remLength))
            {
                sectionsClear = false;
            }

            while (remLength > 0 && sectionAvailable)
            {
                tempRoute.Add(routeElement);
                remLength -= (section.Length - offset);
                offset = 0.0f;

                if (remLength > 0)
                {
                    if (routeIndex < ValidRoute[0].Count - 1)
                    {
                        routeIndex++;
                        routeElement = ValidRoute[0][routeIndex];
                        section = routeElement.TrackCircuitSection;
                        if (!section.CanPlaceTrain(this, offset, remLength))
                        {
                            sectionsClear = false;
                        }
                        offset = 0.0f;
                    }
                    else
                    {
                        Trace.TraceWarning($"No sufficient track to place train {Number} , service name {Name} ");
                        sectionAvailable = false;
                    }
                }

            }

            if (MPManager.IsMultiPlayer())
                return tempRoute;
            if (!sectionAvailable || !sectionsClear)
            {
                tempRoute.Clear();
            }

            return tempRoute;
        }

        // Set initial train route
        internal void SetInitialTrainRoute(TrackCircuitPartialPathRoute partialRoute)
        {
            // reserve sections, use direction Forward only
            foreach (TrackCircuitRouteElement element in partialRoute)
            {
                element.TrackCircuitSection.Reserve(RoutedForward, partialRoute);
            }
        }

        // Reset initial train route
        internal void ResetInitialTrainRoute(TrackCircuitPartialPathRoute partialRoute)
        {
            // unreserve sections
            foreach (TrackCircuitRouteElement element in partialRoute)
            {
                element.TrackCircuitSection.RemoveTrain(this, false);
            }
        }

        // Initial train placement
        internal virtual bool InitialTrainPlacement()
        {
            // for initial placement, use direction 0 only
            // set initial positions

            TrackNode tn = FrontTDBTraveller.TN;
            float offset = FrontTDBTraveller.TrackNodeOffset;
            TrackDirection direction = (TrackDirection)FrontTDBTraveller.Direction;

            PresentPosition[Direction.Forward].SetPosition(tn.TrackCircuitCrossReferences, offset, direction);
            PreviousPosition[Direction.Forward].UpdateFrom(PresentPosition[Direction.Forward]);

            DistanceTravelledM = 0.0f;

            tn = RearTDBTraveller.TN;
            offset = RearTDBTraveller.TrackNodeOffset;
            direction = (TrackDirection)RearTDBTraveller.Direction;

            PresentPosition[Direction.Backward].SetPosition(tn.TrackCircuitCrossReferences, offset, direction);

            // check if train has route, if not create dummy
            if (ValidRoute[0] == null)
            {
                ValidRoute[0] = SignalEnvironment.BuildTempRoute(this, PresentPosition[Direction.Backward].TrackCircuitSectionIndex, PresentPosition[Direction.Backward].Offset,
                        PresentPosition[Direction.Backward].Direction, Length, true, true, false);
            }

            // get index of first section in route
            int rearIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Backward].TrackCircuitSectionIndex, 0);
            if (rearIndex < 0)
            {
                rearIndex = 0;
            }

            PresentPosition[Direction.Backward].RouteListIndex = rearIndex;

            // get index of front of train
            int frontIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0);
            if (frontIndex < 0)
            {
                Trace.TraceWarning("Start position of front of train {0}, service name {1} not on route ", Number, Name);
                frontIndex = 0;
            }

            PresentPosition[Direction.Forward].RouteListIndex = frontIndex;

            // check if train can be placed
            // get index of section in train route //
            int routeIndex = rearIndex;
            List<TrackCircuitSection> placementSections = new List<TrackCircuitSection>();

            // check if route is available
            offset = PresentPosition[Direction.Backward].Offset;
            float remLength = Length;
            bool sectionAvailable = true;

            for (int iRouteIndex = rearIndex; iRouteIndex <= frontIndex && sectionAvailable; iRouteIndex++)
            {
                TrackCircuitSection section = ValidRoute[0][iRouteIndex].TrackCircuitSection;
                if (section.CanPlaceTrain(this, offset, remLength))
                {
                    placementSections.Add(section);
                    remLength -= (section.Length - offset);

                    if (remLength > 0)
                    {
                        if (routeIndex < ValidRoute[0].Count - 1)
                        {
                            routeIndex++;
                            offset = 0.0f;
                        }
                        else
                        {
                            Trace.TraceWarning("No sufficient track to place train");
                            sectionAvailable = false;
                        }
                    }

                }
                else
                {
                    sectionAvailable = false;
                }
            }

            // if not available - return
            if (!sectionAvailable || placementSections.Count <= 0)
            {
                return false;
            }

            // set any deadlocks for sections ahead of start with end beyond start
            for (int i = 0; i < rearIndex; i++)
            {
                int rearSectionIndex = ValidRoute[0][i].TrackCircuitSection.Index;
                if (DeadlockInfo.ContainsKey(rearSectionIndex))
                {
                    foreach (Dictionary<int, int> deadlock in DeadlockInfo[rearSectionIndex])
                    {
                        foreach (KeyValuePair<int, int> deadlockDetail in deadlock)
                        {
                            int endSectionIndex = deadlockDetail.Value;
                            if (ValidRoute[0].GetRouteIndex(endSectionIndex, rearIndex) >= 0)
                            {
                                TrackCircuitSection.TrackCircuitList[endSectionIndex].SetDeadlockTrap(Number, deadlockDetail.Key);
                            }
                        }
                    }
                }
            }

            // set track occupied (if not done yet)
            foreach (TrackCircuitSection section in placementSections)
            {
                if (!section.IsSet(RoutedForward, false))
                {
                    section.Reserve(RoutedForward, ValidRoute[0]);
                    section.SetOccupied(RoutedForward);
                }
            }

            return true;
        }

        /// <summary>
        /// Set Formed Occupied
        /// Set track occupied for train formed out of other train
        /// </summary>
        internal void SetFormedOccupied()
        {

            int rearIndex = PresentPosition[Direction.Backward].RouteListIndex;
            int frontIndex = PresentPosition[Direction.Forward].RouteListIndex;

            int routeIndex = rearIndex;

            List<TrackCircuitSection> placementSections = new List<TrackCircuitSection>();

            // route is always available as previous train was there

            float offset = PresentPosition[Direction.Backward].Offset;
            float remLength = Length;

            for (int iRouteIndex = rearIndex; iRouteIndex <= frontIndex; iRouteIndex++)
            {
                TrackCircuitSection thisSection = ValidRoute[0][iRouteIndex].TrackCircuitSection;
                placementSections.Add(thisSection);
                remLength -= thisSection.Length - offset;

                if (remLength > 0)
                {
                    if (routeIndex < ValidRoute[0].Count - 1)
                    {
                        routeIndex++;
                        offset = 0.0f;
                    }
                    else
                    {
                        Trace.TraceWarning("No sufficient track to place train");
                    }
                }
            }

            // set track occupied (if not done yet)
            foreach (TrackCircuitSection section in placementSections)
            {
                if (!section.IsSet(RoutedForward, false))
                {
                    section.Reserve(RoutedForward, ValidRoute[0]);
                    section.SetOccupied(RoutedForward);
                }
            }
        }

        /// <summary>
        /// Check if train is stopped in station
        /// </summary>
        /// <param name="platform"></param>
        /// <param name="stationDirection"></param>
        /// <param name="stationTCSectionIndex"></param>
        /// <returns></returns>
        internal virtual bool CheckStationPosition(PlatformDetails platform, TrackDirection stationDirection, int stationTCSectionIndex)
        {
            bool atStation = false;
            float platformBeginOffset = platform.TrackCircuitOffset[Location.NearEnd, stationDirection];
            float platformEndOffset = platform.TrackCircuitOffset[Location.FarEnd, stationDirection];
            int endSectionIndex = stationDirection == TrackDirection.Ahead ?
                    platform.TCSectionIndex[platform.TCSectionIndex.Count - 1] :
                    platform.TCSectionIndex[0];
            int endSectionRouteIndex = ValidRoute[0].GetRouteIndex(endSectionIndex, 0);

            int beginSectionIndex = stationDirection == TrackDirection.Reverse ?
                    platform.TCSectionIndex[platform.TCSectionIndex.Count - 1] :
                    platform.TCSectionIndex[0];
            int beginSectionRouteIndex = ValidRoute[0].GetRouteIndex(beginSectionIndex, 0);

            // if rear is in platform, station is valid
            if (((((beginSectionRouteIndex != -1 && PresentPosition[Direction.Backward].RouteListIndex == beginSectionRouteIndex) || (PresentPosition[Direction.Backward].RouteListIndex == -1 && PresentPosition[Direction.Backward].TrackCircuitSectionIndex == beginSectionIndex))
                && PresentPosition[Direction.Backward].Offset >= platformBeginOffset) || PresentPosition[Direction.Backward].RouteListIndex > beginSectionRouteIndex) &&
                ((PresentPosition[Direction.Backward].TrackCircuitSectionIndex == endSectionIndex && PresentPosition[Direction.Backward].Offset <= platformEndOffset) || endSectionRouteIndex == -1 && beginSectionRouteIndex != -1 ||
                PresentPosition[Direction.Backward].RouteListIndex < endSectionRouteIndex))
            {
                atStation = true;
            }
            // if front is in platform and most of the train is as well, station is valid
            else if (((((endSectionRouteIndex != -1 && PresentPosition[Direction.Forward].RouteListIndex == endSectionRouteIndex) || (PresentPosition[Direction.Forward].RouteListIndex == -1 && PresentPosition[Direction.Forward].TrackCircuitSectionIndex == endSectionIndex))
                && PresentPosition[Direction.Forward].Offset <= platformEndOffset) && ((platform.Length - (platformEndOffset - PresentPosition[Direction.Forward].Offset)) > Length / 2)) ||
                (PresentPosition[Direction.Forward].RouteListIndex != -1 && PresentPosition[Direction.Forward].RouteListIndex < endSectionRouteIndex &&
                (PresentPosition[Direction.Forward].RouteListIndex > beginSectionRouteIndex || (PresentPosition[Direction.Forward].RouteListIndex == beginSectionRouteIndex && PresentPosition[Direction.Forward].Offset >= platformBeginOffset))))
            {
                atStation = true;
            }
            // if front is beyond platform and and most of the train is within it, station is valid (isn't it already covered by cases 1 or 4?)
            else if (endSectionRouteIndex != -1 && PresentPosition[Direction.Forward].RouteListIndex == endSectionRouteIndex && PresentPosition[Direction.Forward].Offset > platformEndOffset &&
                     (PresentPosition[Direction.Forward].Offset - platformEndOffset) < (Length / 3))
            {
                atStation = true;
            }
            // if front is beyond platform and rear is not on route or before platform : train spans platform
            else if (((endSectionRouteIndex != -1 && PresentPosition[Direction.Forward].RouteListIndex > endSectionRouteIndex) || (endSectionRouteIndex != -1 && PresentPosition[Direction.Forward].RouteListIndex == endSectionRouteIndex && PresentPosition[Direction.Forward].Offset >= platformEndOffset))
                  && (PresentPosition[Direction.Backward].RouteListIndex < beginSectionRouteIndex || (PresentPosition[Direction.Backward].RouteListIndex == beginSectionRouteIndex && PresentPosition[Direction.Backward].Offset <= platformBeginOffset)))
            {
                atStation = true;
            }

            return atStation;
        }

        //================================================================================================//
        /// <summary>
        /// Update train position
        /// </summary>
        internal void UpdateTrainPosition()
        {
            // update positions

            PreviousPosition[Direction.Forward].UpdateFrom(PresentPosition[Direction.Forward]);

            TrackNode tn = FrontTDBTraveller.TN;
            float offset = FrontTDBTraveller.TrackNodeOffset;
            TrackDirection direction = (TrackDirection)FrontTDBTraveller.Direction;
            int routeIndex;

            PresentPosition[Direction.Forward].SetPosition(tn.TrackCircuitCrossReferences, offset, direction);
            routeIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0);
            PresentPosition[Direction.Forward].RouteListIndex = routeIndex;

            tn = RearTDBTraveller.TN;
            offset = RearTDBTraveller.TrackNodeOffset;
            direction = (TrackDirection)RearTDBTraveller.Direction;

            PresentPosition[Direction.Backward].SetPosition(tn.TrackCircuitCrossReferences, offset, direction);
            routeIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Backward].TrackCircuitSectionIndex, 0);
            PresentPosition[Direction.Backward].RouteListIndex = routeIndex;

            if (jumpRequested) // jump do be performed in multiplayer mode when train re-enters game in different position
            {
                jumpRequested = false;
                PreviousPosition[Direction.Forward].UpdateFrom(PresentPosition[Direction.Forward]);
                Trace.TraceInformation("Multiplayer server requested the player train to jump");
                // reset some items
                SignalObjectItems.Clear();
                NextSignalObject[0] = null;
                InitializeSignals(true);
                LastReservedSection[0] = PresentPosition[Direction.Forward].TrackCircuitSectionIndex;
            }

            // get reserved length
            ReservedTrackLengthM = GetReservedLength();
        }

        /// <summary>
        /// Update Position linked information
        /// Switches train to Out_Of_Control if it runs out of path
        /// <\summary>
        protected void UpdateTrainPositionInformation()
        {

            // check if train still on route - set train to OUT_OF_CONTROL
            PresentPosition[Direction.Forward].DistanceTravelled = DistanceTravelledM;
            PresentPosition[Direction.Backward].DistanceTravelled = DistanceTravelledM - Length;

            if (PresentPosition[Direction.Forward].RouteListIndex < 0)
            {
                SetTrainOutOfControl(OutOfControlReason.OutOfPath);
            }
            else if (StationStops.Count > 0)
            {
                ComputeDistanceToNextStation(StationStops[0]);
            }
        }

        /// <summary>
        /// compute boarding time for activity mode
        /// also check validity of depart time value
        /// <\summary>
        internal virtual (bool, int) ComputeTrainBoardingTime(StationStop stationStop, int stopTime)
        {
            stopTime = stationStop.ComputeStationBoardingTime(this);
            return (stationStop.CheckScheduleValidity(this), stopTime);
        }

        /// <summary>
        /// Compute distance to next station
        /// <\summary>
        private void ComputeDistanceToNextStation(StationStop station)
        {
            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[PresentPosition[Direction.Forward].TrackCircuitSectionIndex];
            float leftInSectionM = section.Length - PresentPosition[Direction.Forward].Offset;
            float distanceToTrainM;
            int stationIndex;

            if (station.SubrouteIndex > TCRoute.ActiveSubPath && !simulator.TimetableMode)
            // if the station is in a further subpath, distance computation is longer
            {
                // first compute distance up to end or reverse point of activeSubpath. To be restudied for subpaths with no reversal
                if (TCRoute.ReversalInfo[TCRoute.ActiveSubPath].Valid)
                    distanceToTrainM = ComputeDistanceToReversalPoint();
                else
                {
                    int lastSectionRouteIndex = TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath].Count - 1;
                    float lastSectionLength = TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath][lastSectionRouteIndex].TrackCircuitSection.Length;
                    distanceToTrainM = TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath].GetDistanceAlongRoute(PresentPosition[Direction.Forward].RouteListIndex,
                        leftInSectionM, lastSectionRouteIndex, lastSectionLength, true);
                }

                int firstSection = 0;
                float firstSectionOffsetToGo = 0;
                int lastSection = 0;
                float lastSectionOffsetToGo = 0;
                if (distanceToTrainM >= 0)
                {

                    // compute length of intermediate subpaths, if any, from reversal or section at beginning to reversal or section at end
                    for (int i = TCRoute.ActiveSubPath + 1; i < station.SubrouteIndex; i++)
                    {
                        if (TCRoute.ReversalInfo[i - 1].Valid)
                        // skip sections before reversal at beginning of path
                        {
                            for (int j = 0; j < TCRoute.TCRouteSubpaths[i].Count; j++)
                            {
                                if (TCRoute.TCRouteSubpaths[i][j].TrackCircuitSection.Index == TCRoute.ReversalInfo[i - 1].ReversalSectionIndex)
                                {
                                    firstSection = j;
                                    firstSectionOffsetToGo = TCRoute.ReversalInfo[i - 1].ReverseReversalOffset;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            for (int j = 0; j < TCRoute.TCRouteSubpaths[i].Count; j++)
                            {
                                if (TCRoute.TCRouteSubpaths[i][j].TrackCircuitSection.Index ==
                                    TCRoute.TCRouteSubpaths[i - 1][TCRoute.TCRouteSubpaths[i - 1].Count - 1].TrackCircuitSection.Index)
                                {
                                    firstSection = j + 1;
                                    firstSectionOffsetToGo = TCRoute.TCRouteSubpaths[i][firstSection].TrackCircuitSection.Length;
                                    break;
                                }
                            }
                        }

                        if (TCRoute.ReversalInfo[i].Valid)
                        // skip sections before reversal at beginning of path
                        {
                            for (int j = TCRoute.TCRouteSubpaths[i].Count - 1; j >= 0; j--)
                            {
                                if (TCRoute.TCRouteSubpaths[i][j].TrackCircuitSection.Index == TCRoute.ReversalInfo[i].ReversalSectionIndex)
                                {
                                    lastSection = j;
                                    lastSectionOffsetToGo = TCRoute.ReversalInfo[i].ReverseReversalOffset;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            lastSection = TCRoute.TCRouteSubpaths[i].Count - 1;
                            lastSectionOffsetToGo = TCRoute.TCRouteSubpaths[i][lastSection].TrackCircuitSection.Length;
                        }

                        float lengthOfIntSubpath = TCRoute.TCRouteSubpaths[i].GetDistanceAlongRoute(firstSection, firstSectionOffsetToGo, lastSection, lastSectionOffsetToGo, true);
                        if (lengthOfIntSubpath < 0)
                        {
                            distanceToTrainM = -1;
                            break;
                        }
                        distanceToTrainM += lengthOfIntSubpath;
                    }
                }
                if (distanceToTrainM >= 0)
                {
                    // finally compute distance from start of station subpath up to station
                    if (TCRoute.ReversalInfo[station.SubrouteIndex - 1].Valid)
                    // skip sections before reversal at beginning of path
                    {
                        for (int j = 0; j < TCRoute.TCRouteSubpaths[station.SubrouteIndex].Count; j++)
                        {
                            if (TCRoute.TCRouteSubpaths[station.SubrouteIndex][j].TrackCircuitSection.Index == TCRoute.ReversalInfo[station.SubrouteIndex - 1].ReversalSectionIndex)
                            {
                                firstSection = j;
                                firstSectionOffsetToGo = TCRoute.ReversalInfo[station.SubrouteIndex - 1].ReverseReversalOffset;
                                break;
                            }
                        }
                    }
                    else
                    {
                        for (int j = 0; j < TCRoute.TCRouteSubpaths[station.SubrouteIndex].Count; j++)
                        {
                            if (TCRoute.TCRouteSubpaths[station.SubrouteIndex][j].TrackCircuitSection.Index ==
                                TCRoute.TCRouteSubpaths[station.SubrouteIndex - 1][TCRoute.TCRouteSubpaths[station.SubrouteIndex - 1].Count - 1].TrackCircuitSection.Index)
                            {
                                firstSection = j + 1;
                                firstSectionOffsetToGo = TCRoute.TCRouteSubpaths[station.SubrouteIndex][firstSection].TrackCircuitSection.Length;
                                break;
                            }
                        }
                    }

                    stationIndex = station.RouteIndex;
                    float distanceFromStartOfsubPath = TCRoute.TCRouteSubpaths[station.SubrouteIndex].GetDistanceAlongRoute(firstSection, firstSectionOffsetToGo, stationIndex, station.StopOffset, true);
                    if (distanceFromStartOfsubPath < 0)
                        distanceToTrainM = -1;
                    else
                        distanceToTrainM += distanceFromStartOfsubPath;
                }
            }
            else
            {
                // No enhanced compatibility, simple computation
                // if present position off route, try rear position
                // if both off route, skip station stop
                stationIndex = ValidRoute[0].GetRouteIndex(station.TrackCircuitSectionIndex, PresentPosition[Direction.Forward].RouteListIndex);
                distanceToTrainM = ValidRoute[0].GetDistanceAlongRoute(PresentPosition[Direction.Forward].RouteListIndex, leftInSectionM, stationIndex, station.StopOffset, true);
            }

            station.DistanceToTrainM = distanceToTrainM;
        }

        /// <summary>
        /// Compute distance to reversal point
        /// <\summary>
        protected float ComputeDistanceToReversalPoint()
        {
            float lengthToGoM = -PresentPosition[Direction.Forward].Offset;
            TrackCircuitSection section;
            if (PresentPosition[Direction.Forward].RouteListIndex == -1)
            {
                Trace.TraceWarning($"Train {Number} service {Name} off path; distance to reversal point set to -1");
                return -1;
            }
            // in case the AI train is out of its original path the reversal info is simulated to point to the end of the last route section
            int reversalRouteIndex = ValidRoute[0].GetRouteIndex(TCRoute.ReversalInfo[TCRoute.ActiveSubPath].ReversalSectionIndex, PresentPosition[Direction.Forward].RouteListIndex);
            if (reversalRouteIndex == -1)
            {
                Trace.TraceWarning($"Train {Number} service {Name}, reversal or end point off path; distance to reversal point set to -1");
                return -1;
            }

            TrackCircuitSection reversalSection = TrackCircuitSection.TrackCircuitList[TCRoute.ReversalInfo[TCRoute.ActiveSubPath].ReversalSectionIndex];
            float reverseReversalOffset = TCRoute.ReversalInfo[TCRoute.ActiveSubPath].ReverseReversalOffset;
            if (PresentPosition[Direction.Forward].RouteListIndex <= reversalRouteIndex)
            {
                for (int i = PresentPosition[Direction.Forward].RouteListIndex; i < ValidRoute[0].Count; i++)
                {
                    section = ValidRoute[0][i].TrackCircuitSection;
                    if (section.Index == reversalSection.Index)
                    {
                        break;
                    }
                    else
                        lengthToGoM += section.Length;
                }
                return lengthToGoM += reverseReversalOffset;
            }
            else
            {
                for (int i = PresentPosition[Direction.Forward].RouteListIndex - 1; i >= 0; i--)
                {
                    section = ValidRoute[0][i].TrackCircuitSection;
                    if (section.Index == reversalSection.Index)
                    {
                        break;
                    }
                    else
                        lengthToGoM -= section.Length;
                }
                return lengthToGoM += reverseReversalOffset - reversalSection.Length;
            }
        }

        /// <summary>
        /// Compute path length
        /// <\summary>
        internal float ComputePathLength()
        {
            float pathLength = 0;
            int tcRouteSubpathIndex = -1;
            foreach (TrackCircuitPartialPathRoute tcRouteSubpath in TCRoute.TCRouteSubpaths)
            {
                tcRouteSubpathIndex++;
                if (tcRouteSubpathIndex > 0 && TCRoute.ReversalInfo[tcRouteSubpathIndex - 1].Valid)
                    pathLength += TCRoute.ReversalInfo[tcRouteSubpathIndex - 1].ReverseReversalOffset;
                else if (tcRouteSubpathIndex > 0)
                    pathLength += TCRoute.ReversalInfo[tcRouteSubpathIndex - 1].ReverseReversalOffset -
                        TrackCircuitSection.TrackCircuitList[TCRoute.ReversalInfo[tcRouteSubpathIndex - 1].ReversalSectionIndex].Length;
                else { } //start point offset?

                int routeListIndex = 1;
                TrackCircuitSection section;
                int reversalRouteIndex = tcRouteSubpath.GetRouteIndex(TCRoute.ReversalInfo[tcRouteSubpathIndex].ReversalSectionIndex, routeListIndex);
                if (reversalRouteIndex == -1)
                {
                    Trace.TraceWarning($"Train {Number} service {Name}, reversal or end point off path; distance to reversal point set to -1");
                    return -1;
                }
                if (routeListIndex <= reversalRouteIndex)
                {
                    for (int i = routeListIndex; i < tcRouteSubpath.Count; i++)
                    {
                        section = tcRouteSubpath[i].TrackCircuitSection;
                        if (section.Index == TCRoute.ReversalInfo[tcRouteSubpathIndex].ReversalSectionIndex)
                        {
                            break;
                        }
                        else
                            pathLength += section.Length;
                    }
                    pathLength += TCRoute.ReversalInfo[tcRouteSubpathIndex].ReverseReversalOffset;
                }
                else
                {
                    pathLength += TCRoute.ReversalInfo[tcRouteSubpathIndex].ReverseReversalOffset -
                    TrackCircuitSection.TrackCircuitList[TCRoute.ReversalInfo[tcRouteSubpathIndex].ReversalSectionIndex].Length;
                }
            }
            return pathLength;
        }


        /// <summary>
        /// get list of required actions (only if not moving backward)
        /// </summary>
        protected void ObtainRequiredActions(int backward)
        {
            if (this is AITrain aiTrain && aiTrain.MovementState == AiMovementState.Suspended)
                return;
            if (backward < BackwardThreshold)
            {
                List<DistanceTravelledItem> nowActions = requiredActions.GetActions(DistanceTravelledM);
                if (nowActions.Count > 0)
                {
                    PerformActions(nowActions);
                }
            }
            if (backward < BackwardThreshold || SpeedMpS > -0.01)
            {
                List<DistanceTravelledItem> nowActions = AuxActionsContainer.specRequiredActions.GetAuxActions(this);

                if (nowActions.Count > 0)
                {
                    PerformActions(nowActions);
                }
            }
        }

        /// <summary>
        /// Update section occupy states
        /// Input is backward movement counter
        /// </summary>
        public void UpdateSectionState(int backward)
        {
            // don't bother with update if train out of control - all will be reset when train is stopped
            if (ControlMode == TrainControlMode.OutOfControl)
            {
                return;
            }

            int lastIndex = PreviousPosition[Direction.Forward].RouteListIndex;
            int presentIndex = PresentPosition[Direction.Forward].RouteListIndex;

            // don't bother with update if train off route - set train to out of control
            if (presentIndex < 0)
            {
                SetTrainOutOfControl(OutOfControlReason.OutOfPath);
                return;
            }

            int lastDTM = (int)PreviousPosition[Direction.Forward].DistanceTravelled;
            TrackCircuitSection lastSection = TrackCircuitSection.TrackCircuitList[PreviousPosition[Direction.Forward].TrackCircuitSectionIndex];
            int lastDTatEndLastSectionM = lastDTM + (int)(lastSection.Length - PreviousPosition[Direction.Forward].Offset);

            int presentDTM = (int)DistanceTravelledM;

            List<int[]> sectionList = new List<int[]>();

            if (backward > BackwardThreshold) // train moved backward

            {
                if (presentIndex < lastIndex)
                {
                    for (int i = lastIndex; i > presentIndex; i--)
                    {
                        sectionList.Add(new int[2] { i, presentDTM });
                    }
                    sectionList.Add(new int[2] { presentIndex, presentDTM });
                }
            }
            else // train moves forward
            {
                if (presentIndex > lastIndex)
                {
                    int lastValidDTM = lastDTatEndLastSectionM;

                    for (int i = lastIndex + 1; i < presentIndex; i++)
                    {
                        sectionList.Add(new int[2] { i, lastValidDTM });
                        lastValidDTM += (int)ValidRoute[0][i].TrackCircuitSection.Length;
                    }
                    sectionList.Add(new int[2] { presentIndex, presentDTM });
                }
            }

            // set section states, for AUTOMODE use direction 0 only
            foreach (int[] routeListIndex in sectionList)
            {
                TrackCircuitSection section = ValidRoute[0][routeListIndex[0]].TrackCircuitSection;
                if (!section.CircuitState.OccupiedByThisTrain(RoutedForward))
                {
                    section.SetOccupied(RoutedForward, routeListIndex[1]);
                    if (!simulator.TimetableMode && section.CircuitState.OccupiedByOtherTrains(RoutedForward))
                    {
                        SwitchToNodeControl(section.Index);
                        EndAuthorityTypes[0] = EndAuthorityType.TrainAhead;
                        ChangeControlModeOtherTrains(section);
                    }
                    // additional actions for child classes
                    UpdateSectionStateAdditional(section.Index);
                }
            }
        }

        /// <summary>
        /// Change control mode of other trains in same section if needed
        /// </summary>
        private void ChangeControlModeOtherTrains(TrackCircuitSection section)
        {
            TrackDirection owndirection = PresentPosition[Direction.Forward].Direction;
            foreach (KeyValuePair<TrainRouted, int> trainToCheckInfo in section.CircuitState.OccupationState)
            {
                Train otherTrain = trainToCheckInfo.Key.Train;
                if (otherTrain.ControlMode == TrainControlMode.AutoSignal) // train is still in signal mode, might need adjusting
                {
                    TrackDirection otherdirection = otherTrain.PresentPosition[Direction.Forward].TrackCircuitSectionIndex == section.Index ? otherTrain.PresentPosition[Direction.Forward].Direction :
                        otherTrain.PresentPosition[Direction.Backward].TrackCircuitSectionIndex == section.Index ? otherTrain.PresentPosition[Direction.Backward].Direction : (TrackDirection)(-1);
                    if (owndirection >= 0 && otherdirection >= 0) // both trains found
                    {
                        if (owndirection != otherdirection) // opposite directions - this train is now ahead of train in section
                        {
                            otherTrain.SwitchToNodeControl(section.Index);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Check if train went passed signal
        /// if so, and signal was at danger, set train Out_Of_Control
        /// </summary>
        private protected int CheckSignalPassed(int direction, TrackCircuitPosition trainPosition, TrackCircuitPosition trainPreviousPos)
        {
            int passedSignalIndex = -1;
            if (NextSignalObject[direction] != null)
            {

                while (NextSignalObject[direction] != null && !ValidRoute[direction].SignalIsAheadOfTrain(NextSignalObject[direction], trainPosition)) // signal not in front //
                {
                    // correct route index if necessary
                    int correctedRouteIndex = ValidRoute[0].GetRouteIndex(trainPreviousPos.TrackCircuitSectionIndex, 0);
                    if (correctedRouteIndex >= 0)
                        trainPreviousPos.RouteListIndex = correctedRouteIndex;
                    // check if train really went passed signal in correct direction
                    if (ValidRoute[direction].SignalIsAheadOfTrain(NextSignalObject[direction], trainPreviousPos)) // train was in front on last check, so we did pass
                    {
                        SignalAspectState signalState = GetNextSignalAspect(direction);
                        passedSignalIndex = NextSignalObject[direction].Index;

                        if (signalState == SignalAspectState.Stop && NextSignalObject[direction].OverridePermission == SignalPermission.Denied)
                        {
                            Trace.TraceWarning($"Train {Name} ({Number}) passing signal {NextSignalObject[direction].Index} at {DistanceTravelledM:###0.0} at danger at {SpeedMpS:##0.00}");
                            SetTrainOutOfControl(OutOfControlReason.PassedAtDanger);
                            break;
                        }
                        else if (ControlMode == TrainControlMode.AutoSignal && NextSignalObject[direction].Signalfound[(int)SignalFunction.Normal] < 0) // no next signal
                        {
                            SwitchToNodeControl(LastReservedSection[direction]);
                            break;
                        }
                        else if (ControlMode == TrainControlMode.AutoSignal && NextSignalObject[direction].BlockState() != SignalBlockState.Clear) // route to next signal not clear
                        {
                            SwitchToNodeControl(LastReservedSection[direction]);
                            break;
                        }

                        // get next signal
                        int nextSignalIndex = NextSignalObject[direction].Signalfound[(int)SignalFunction.Normal];
                        if (nextSignalIndex >= 0)
                        {
                            NextSignalObject[direction] = Simulator.Instance.SignalEnvironment.Signals[nextSignalIndex];

                            int reqSectionIndex = NextSignalObject[direction].TrackCircuitIndex;
                            float endOffset = NextSignalObject[direction].TrackCircuitOffset;

                            DistanceToSignal = GetDistanceToTrain(reqSectionIndex, endOffset);
                        }
                        else
                        {
                            NextSignalObject[direction] = null;
                        }
                    }
                    else
                    {
                        // get next signal
                        int nextSignalIndex = NextSignalObject[direction].Signalfound[(int)SignalFunction.Normal];
                        if (nextSignalIndex >= 0)
                        {
                            NextSignalObject[direction] = Simulator.Instance.SignalEnvironment.Signals[nextSignalIndex];

                            int reqSectionIndex = NextSignalObject[direction].TrackCircuitIndex;
                            float endOffset = NextSignalObject[direction].TrackCircuitOffset;

                            DistanceToSignal = GetDistanceToTrain(reqSectionIndex, endOffset);
                        }
                        else
                        {
                            NextSignalObject[direction] = null;
                        }
                    }
                }
            }
            return passedSignalIndex;
        }

        /// <summary>
        /// Check if train moves backward and if so, check clearance behindtrain
        /// If no save clearance left, set train to Out_Of_Control
        /// </summary>
        protected int CheckBackwardClearance()
        {
            bool outOfControl = false;

            int lastIndex = PreviousPosition[Direction.Forward].RouteListIndex;
            float lastOffset = PreviousPosition[Direction.Forward].Offset;
            int presentIndex = PresentPosition[Direction.Forward].RouteListIndex;
            float presentOffset = PresentPosition[Direction.Forward].Offset;

            if (presentIndex < 0) // we are off the path, stop train //
            {
                SetTrainOutOfControl(OutOfControlReason.OutOfPath);
            }

            // backward
            if (presentIndex < lastIndex || (presentIndex == lastIndex && presentOffset < lastOffset))
            {
                movedBackward = movedBackward < 2 * BackwardThreshold ? ++movedBackward : movedBackward;
            }

            if (movedBackward > BackwardThreshold)
            {
                // run through sections behind train
                // if still in train route : try to reserve section
                // if multiple train in section : calculate distance to next train, stop oncoming train
                // if section reserved for train : stop train
                // if out of route : set out_of_control
                // if signal : set distance, check if passed

                // TODO : check if other train in section, get distance to train
                // TODO : check correct alignment of any switches passed over while moving backward (reset activepins)

                if (rearSignalObject != null)
                {

                    // create new position some 25 m. behind train as allowed overlap
                    TrackCircuitPosition overlapPosition = new TrackCircuitPosition(PresentPosition[Direction.Backward]);
                    TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[overlapPosition.TrackCircuitSectionIndex];
                    overlapPosition.Offset = section.Length - (PresentPosition[Direction.Backward].Offset + RearPositionOverlap);  // reverse offset because of reversed direction
                    overlapPosition.Direction = overlapPosition.Direction.Reverse(); // looking backwards, so reverse direction

                    TrackCircuitSection rearSection = TrackCircuitSection.TrackCircuitList[rearSignalObject.TrackCircuitNextIndex];
                    if (!IsAheadOfTrain(rearSection, 0.0f, overlapPosition))
                    {
                        if (rearSignalObject.SignalLR(SignalFunction.Normal) == SignalAspectState.Stop)
                        {
                            Trace.TraceWarning($"Train {Name} ({Number}) passing rear signal {rearSignalObject.Index} at {DistanceTravelledM:###0.0} at danger at {SpeedMpS:##0.00)}");
                            SetTrainOutOfControl(OutOfControlReason.RearPassedAtDanger);
                            outOfControl = true;
                        }
                        else
                        {
                            rearSignalObject = null;   // passed signal, so reset //
                        }
                    }
                }

                if (!outOfControl && rearSignalObject == null)
                {
                    TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[PresentPosition[Direction.Backward].TrackCircuitSectionIndex];
                    float clearPath = section.Length - PresentPosition[Direction.Backward].Offset;   // looking other direction //
                    TrackDirection direction = PresentPosition[Direction.Backward].Direction.Reverse();

                    while (clearPath < RearPositionOverlap && !outOfControl && rearSignalObject == null)
                    {
                        if (section.EndSignals[direction] != null)
                        {
                            rearSignalObject = section.EndSignals[direction];
                        }
                        else
                        {
                            TrackDirection pinLink = direction.Reverse();

                            // TODO : check required junction and crossover path

                            int nextSectionIndex = section.Pins[pinLink, Location.NearEnd].Link;
                            if (nextSectionIndex >= 0)
                            {
                                TrackCircuitSection nextSection = TrackCircuitSection.TrackCircuitList[nextSectionIndex];
                                if (!nextSection.IsAvailable(this))
                                {
                                    SetTrainOutOfControl(OutOfControlReason.SlippedIntoPath);
                                    outOfControl = true;

                                    // stop train in path

                                    List<TrainRouted> trainsInSection = nextSection.CircuitState.TrainsOccupying();
                                    foreach (TrainRouted nextTrain in trainsInSection)
                                    {
                                        nextTrain.Train.ForcedStop(Simulator.Catalog.GetString("Other train is blocking path"), Name, Number);
                                    }

                                    if (nextSection.CircuitState.TrainReserved != null)
                                    {
                                        nextSection.CircuitState.TrainReserved.Train.ForcedStop(Simulator.Catalog.GetString("Other train is blocking path"), Name, Number);
                                    }
                                }
                                else
                                {
                                    clearPath += nextSection.Length;
                                    section = nextSection;
                                    if (section.CircuitType == TrackCircuitType.EndOfTrack)
                                    {
                                        SetTrainOutOfControl(OutOfControlReason.SlippedToEndOfTrack);
                                        outOfControl = true;
                                    }
                                }
                            }
                        }
                    }

                    if (outOfControl)
                    {
                        clearanceAtRearM = -1;
                        rearSignalObject = null;
                    }
                    else
                    {
                        clearanceAtRearM = clearPath;
                    }
                }
            }
            else
            {
                movedBackward = movedBackward >= 0 ? --movedBackward : movedBackward;
                clearanceAtRearM = -1;
                rearSignalObject = null;
            }

            return movedBackward;
        }

        /// <summary>
        /// Check for end of route actions - for activity PLAYER train only
        /// Reverse train if required
        /// Return parameter : true if train still exists (only used in timetable mode)
        /// </summary>
        protected virtual bool CheckRouteActions(double elapsedClockSeconds)
        {
            TrackDirection directionNow = PresentPosition[Direction.Forward].Direction;
            int positionNow = PresentPosition[Direction.Forward].TrackCircuitSectionIndex;
            TrackDirection directionNowBack = PresentPosition[Direction.Backward].Direction;
            int positionNowBack = PresentPosition[Direction.Backward].TrackCircuitSectionIndex;

            if (PresentPosition[Direction.Forward].RouteListIndex >= 0)
                directionNow = ValidRoute[0][PresentPosition[Direction.Forward].RouteListIndex].Direction;

            (bool endOfRoute, bool otherRouteAvailable) = UpdateRouteActions(elapsedClockSeconds, false);

            AuxActionsContainer.SetAuxAction(this);
            if (!endOfRoute)
                return true;  // not at end of route

            // check if train reversed
            if (otherRouteAvailable)
            {
                if (positionNowBack == PresentPosition[Direction.Forward].TrackCircuitSectionIndex && directionNowBack != PresentPosition[Direction.Forward].Direction)
                {
                    ReverseFormation(IsActualPlayerTrain);
                    TryIncrementSubpath();
                }
                else if (positionNow == PresentPosition[Direction.Backward].TrackCircuitSectionIndex && directionNow != PresentPosition[Direction.Backward].Direction)
                {
                    ReverseFormation(IsActualPlayerTrain);
                    TryIncrementSubpath();
                }
            }

            // check if next station was on previous subpath - if so, move to this subpath

            if (otherRouteAvailable && StationStops.Count > 0)
            {
                if (StationStops[0].SubrouteIndex < TCRoute.ActiveSubPath)
                {
                    StationStops[0].SubrouteIndex = TCRoute.ActiveSubPath;
                }
            }

            return true; // always return true for activity player train
        }


        /// <summary>
        /// Check for end of route actions
        /// Called every update, actions depend on route state
        /// returns :
        /// bool[0] "false" end of route not reached
        /// bool[1] "false" if no further route available
        /// </summary>
        protected (bool endOfRoute, bool otherRouteAvailable) UpdateRouteActions(double elapsedClockSeconds, bool checkLoop = true)
        {
            _ = elapsedClockSeconds;

            NextRouteReady = false;

            // check if train in loop
            // if so, forward to next subroute and continue
            if (checkLoop || StationStops.Count <= 1 || StationStops.Count > 1 && TCRoute != null && StationStops[1].SubrouteIndex > TCRoute.ActiveSubPath)
            {
                if (TCRoute != null && (ControlMode == TrainControlMode.AutoNode || ControlMode == TrainControlMode.AutoSignal) && TCRoute.LoopEnd[TCRoute.ActiveSubPath] >= 0)
                {
                    int loopSectionIndex = ValidRoute[0].GetRouteIndex(TCRoute.LoopEnd[TCRoute.ActiveSubPath], 0);

                    if (loopSectionIndex >= 0 && PresentPosition[Direction.Backward].RouteListIndex > loopSectionIndex)
                    {
                        int frontSection = TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath][PresentPosition[Direction.Forward].RouteListIndex].TrackCircuitSection.Index;
                        int rearSection = TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath][PresentPosition[Direction.Backward].RouteListIndex].TrackCircuitSection.Index;
                        TCRoute.ActiveSubPath++;
                        ValidRoute[0] = TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath];

                        PresentPosition[Direction.Forward].RouteListIndex = ValidRoute[0].GetRouteIndex(frontSection, 0);
                        PresentPosition[Direction.Backward].RouteListIndex = ValidRoute[0].GetRouteIndex(rearSection, 0);

                        // Invalidate preceding section indexes to avoid wrong indexing when building route forward (in Reserve())
                        for (int routeListIndex = 0; routeListIndex < PresentPosition[Direction.Backward].RouteListIndex; routeListIndex++)
                        {
                            ValidRoute[0][routeListIndex].Invalidate();
                        }
                        return (true, true);
                    }

                    // if loopend no longer on this valid route, remove loopend indication
                    else if (loopSectionIndex < 0)
                    {
                        TCRoute.LoopEnd[TCRoute.ActiveSubPath] = -1;
                    }
                }
            }

            // check position in relation to present end of path

            bool endOfRoute = CheckEndOfRoutePosition();

            // not end of route - no action
            if (!endOfRoute)
            {
                return (false, false);
            }

            // <CSComment> TODO: check if holding signals correctly released in case of reversal point between WP and signal
            // if next subpath available : check if it can be activated

            bool nextRouteAvailable = false;

            TrackCircuitPartialPathRoute nextRoute = null;

            if (endOfRoute && TCRoute.ActiveSubPath < (TCRoute.TCRouteSubpaths.Count - 1))
            {
                nextRouteAvailable = true;

                nextRoute = TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath + 1];
                int firstSectionIndex = PresentPosition[Direction.Backward].TrackCircuitSectionIndex;

                // find index of present rear position

                int firstRouteIndex = nextRoute.GetRouteIndex(firstSectionIndex, 0);

                // if not found try index of present front position

                if (firstRouteIndex >= 0)
                {
                    NextRouteReady = true;
                }
                else
                {
                    firstSectionIndex = PresentPosition[Direction.Forward].TrackCircuitSectionIndex;
                    firstRouteIndex = nextRoute.GetRouteIndex(firstSectionIndex, 0);

                    // cant find next part of route - check if really at end of this route, if so, error, else just wait and see (train stopped for other reason)

                    if (PresentPosition[Direction.Forward].RouteListIndex == ValidRoute[0].Count - 1)
                    {
                        if (firstRouteIndex < 0)
                        {
                            Trace.TraceInformation($"Cannot find next part of route (index {TCRoute.ActiveSubPath}) for Train {Name} ({Number}) (at section {PresentPosition[Direction.Forward].TrackCircuitSectionIndex})");
                        }
                        // search for junction and check if it is not clear
                        else
                        {
                            bool junctionFound = false;
                            bool junctionOccupied = false;

                            for (int i = firstRouteIndex + 1; i < nextRoute.Count && !junctionFound; i++)
                            {
                                TrackCircuitSection section = nextRoute[i].TrackCircuitSection;
                                if (section.CircuitType == TrackCircuitType.Junction)
                                {
                                    junctionFound = true;
                                    if (section.CircuitState.OccupiedByThisTrain(this))
                                    {
                                        // Before deciding that route is not yet ready check if the new train head is off path because at end of new route
                                        section = nextRoute[nextRoute.Count - 1].TrackCircuitSection;
                                        if (section.CircuitState.OccupiedByThisTrain(this))
                                            break;
                                        junctionOccupied = true;
                                    }
                                }
                            }

                            if (!junctionOccupied)
                            {
                                NextRouteReady = true;
                            }
                        }
                    }
                    else
                    {
                        endOfRoute = false;
                    }
                }
            }

            // if end reached : clear any remaining reservations ahead
            if (endOfRoute && (!nextRouteAvailable || (nextRouteAvailable && NextRouteReady)))
            {
                if (ControlMode == TrainControlMode.AutoSignal) // for Auto mode try forward only
                {
                    if (NextSignalObject[0]?.EnabledTrain == RoutedForward)
                    {
                        NextSignalObject[0].ResetSignalEnabled();
                        int nextRouteIndex = ValidRoute[0].GetRouteIndex(NextSignalObject[0].TrackCircuitNextIndex, 0);

                        // clear rest of route to avoid accidental signal activation
                        if (nextRouteIndex >= 0)
                        {
                            Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[0], nextRouteIndex, RoutedForward);
                            ValidRoute[0].RemoveRange(nextRouteIndex, ValidRoute[0].Count - nextRouteIndex);
                        }
                    }

                    if (PresentPosition[Direction.Forward].RouteListIndex >= 0 && PresentPosition[Direction.Forward].RouteListIndex < ValidRoute[0].Count - 1) // not at end of route
                    {
                        int nextRouteIndex = PresentPosition[Direction.Forward].RouteListIndex + 1;
                        Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[0], nextRouteIndex, RoutedForward);
                        ValidRoute[0].RemoveRange(nextRouteIndex, ValidRoute[0].Count - nextRouteIndex);
                    }
                }

                int nextIndex = PresentPosition[Direction.Forward].RouteListIndex + 1;
                if (nextIndex <= (ValidRoute[0].Count - 1))
                {
                    Simulator.Instance.SignalEnvironment.BreakDownRoute(ValidRoute[0][nextIndex].TrackCircuitSection.Index, RoutedForward);
                }

                // clear any remaining deadlocks
                ClearDeadlocks();
                DeadlockInfo.Clear();
            }

            // if next route available : reverse train, reset and reinitiate signals
            if (endOfRoute && nextRouteAvailable && NextRouteReady)
            {

                // check if reverse is required
                int newIndex = nextRoute.GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0);
                TrackDirection oldDirection = ValidRoute[0][PresentPosition[Direction.Forward].RouteListIndex].Direction;
                if (newIndex < 0)
                {
                    newIndex = nextRoute.GetRouteIndex(PresentPosition[Direction.Backward].TrackCircuitSectionIndex, 0);
                    oldDirection = ValidRoute[0][PresentPosition[Direction.Backward].RouteListIndex].Direction;
                }

                if (oldDirection != nextRoute[newIndex].Direction)
                {

                    // set new train positions and reset distance travelled
                    (PresentPosition[Direction.Forward], PresentPosition[Direction.Backward]) = (PresentPosition[Direction.Backward], PresentPosition[Direction.Forward]);

                    PresentPosition[Direction.Forward].Reverse(ValidRoute[0][PresentPosition[Direction.Forward].RouteListIndex].Direction, nextRoute, Length);
                    PreviousPosition[Direction.Forward].UpdateFrom(PresentPosition[Direction.Forward]);
                    PresentPosition[Direction.Backward].Reverse(ValidRoute[0][PresentPosition[Direction.Backward].RouteListIndex].Direction, nextRoute, 0.0f);
                }
                else
                {
                    PresentPosition[Direction.Forward].RouteListIndex = nextRoute.GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0);
                    PresentPosition[Direction.Backward].RouteListIndex = nextRoute.GetRouteIndex(PresentPosition[Direction.Backward].TrackCircuitSectionIndex, 0);
                    PreviousPosition[Direction.Forward].UpdateFrom(PresentPosition[Direction.Forward]);
                }

                DistanceTravelledM = PresentPosition[Direction.Forward].DistanceTravelled;

                // perform any remaining actions of type clear section (except sections now occupied)

                // reset old actions
                ClearActiveSectionItems();

                // set new route
                TCRoute.ActiveSubPath++;
                ValidRoute[0] = TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath];

                TCRoute.SetReversalOffset(Length, simulator.TimetableMode);

                // clear existing list of occupied track, and build new list
                for (int i = OccupiedTrack.Count - 1; i >= 0; i--)
                {
                    OccupiedTrack[i].ResetOccupied(this);
                }

                int rearIndex = PresentPosition[Direction.Backward].RouteListIndex;

                if (rearIndex < 0) // end of train not on new route
                {
                    TrackCircuitPartialPathRoute tempRoute = SignalEnvironment.BuildTempRoute(this, PresentPosition[Direction.Backward].TrackCircuitSectionIndex, PresentPosition[Direction.Backward].Offset,
                        PresentPosition[Direction.Backward].Direction, Length, false, true, false);

                    for (int i = 0; i < tempRoute.Count; i++)
                    {
                        tempRoute[i].TrackCircuitSection.SetOccupied(RoutedForward);
                    }
                }
                else
                {
                    for (int i = PresentPosition[Direction.Backward].RouteListIndex; i <= PresentPosition[Direction.Forward].RouteListIndex; i++)
                    {
                        ValidRoute[0][i].TrackCircuitSection.SetOccupied(RoutedForward);
                    }
                }

                // Check deadlock against all other trains
                CheckDeadlock(ValidRoute[0], Number);

                // reset signal information
                SignalObjectItems.Clear();
                NextSignalObject[0] = null;

                InitializeSignals(true);

                LastReservedSection[0] = PresentPosition[Direction.Forward].TrackCircuitSectionIndex;

                // clear claims of any trains which have claimed present occupied sections upto common point - this avoids deadlocks
                // trains may have claimed while train was reversing
                TrackCircuitSection presentSection = TrackCircuitSection.TrackCircuitList[LastReservedSection[0]];
                presentSection.ClearReversalClaims(RoutedForward);

                // switch to NODE mode
                if (ControlMode == TrainControlMode.AutoSignal)
                {
                    SwitchToNodeControl(PresentPosition[Direction.Forward].TrackCircuitSectionIndex);
                }
            }

            return (endOfRoute, nextRouteAvailable);
        }

        /// <summary>
        /// Check End of Route Position
        /// </summary>
        protected virtual bool CheckEndOfRoutePosition()
        {
            bool endOfRoute = false;

            // obtain reversal section index

            int reversalSectionIndex = -1;
            if (TCRoute != null && (ControlMode == TrainControlMode.AutoNode || ControlMode == TrainControlMode.AutoSignal))
            {
                TrackCircuitReversalInfo reversalInfo = TCRoute.ReversalInfo[TCRoute.ActiveSubPath];
                if (reversalInfo.Valid)
                {
                    reversalSectionIndex = reversalInfo.SignalUsed ? reversalInfo.LastSignalIndex : reversalInfo.LastDivergeIndex;
                }
            }

            // check if present subroute ends in reversal or is last subroute
            if (TCRoute.ReversalInfo[TCRoute.ActiveSubPath].Valid || TCRoute.ActiveSubPath == TCRoute.TCRouteSubpaths.Count - 1)
            {
                // can only be performed if train is stationary
                if (Math.Abs(SpeedMpS) > 0.03)
                    return endOfRoute;

                // check position in relation to present end of path
                // front is in last route section
                if (PresentPosition[Direction.Forward].RouteListIndex == (ValidRoute[0].Count - 1) &&
                    (!TCRoute.ReversalInfo[TCRoute.ActiveSubPath].Valid && TCRoute.ActiveSubPath < TCRoute.TCRouteSubpaths.Count - 1))
                {
                    endOfRoute = true;
                }
                // front is within 150m. of end of route and no junctions inbetween (only very short sections ahead of train)
                else
                {
                    TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[PresentPosition[Direction.Forward].TrackCircuitSectionIndex];
                    float remainingLength = section.Length - PresentPosition[Direction.Forward].Offset;

                    bool junctionFound = false;
                    if (TCRoute.ActiveSubPath < TCRoute.TCRouteSubpaths.Count - 1)
                    {
                        for (int iIndex = PresentPosition[Direction.Forward].RouteListIndex + 1; iIndex < ValidRoute[0].Count && !junctionFound; iIndex++)
                        {
                            section = ValidRoute[0][iIndex].TrackCircuitSection;
                            junctionFound = section.CircuitType == TrackCircuitType.Junction;
                            remainingLength += section.Length;
                        }
                    }
                    else
                        remainingLength = ComputeDistanceToReversalPoint();
                    float compatibilityNegligibleRouteChunk = ((TrainType == TrainType.Ai || TrainType == TrainType.AiPlayerHosting)
                        && TCRoute.TCRouteSubpaths.Count - 1 == TCRoute.ActiveSubPath) ? 40f : 5f;
                    float negligibleRouteChunk = compatibilityNegligibleRouteChunk;

                    if (remainingLength < negligibleRouteChunk && !junctionFound && !TCRoute.ReversalInfo[TCRoute.ActiveSubPath].Valid)
                    {
                        endOfRoute = true;
                    }
                }

                //<CSComment: check of vicinity to reverse point; only in subpaths ending with reversal
                if (TCRoute.ReversalInfo[TCRoute.ActiveSubPath].Valid)
                {
                    float distanceToReversalPoint = ComputeDistanceToReversalPoint();
                    if (distanceToReversalPoint < 50 && PresentPosition[Direction.Backward].RouteListIndex >= reversalSectionIndex)
                        endOfRoute = true;
                }
                // other checks unrelated to state
                if (!endOfRoute)
                {
                    // if last entry in route is END_OF_TRACK, check against previous entry as this can never be the trains position nor a signal reference section
                    int lastValidRouteIndex = ValidRoute[0].Count - 1;
                    if (ValidRoute[0][lastValidRouteIndex].TrackCircuitSection.CircuitType == TrackCircuitType.EndOfTrack)
                        lastValidRouteIndex--;

                    // if waiting for next signal and section beyond signal is last in route and there is no valid reversal index - end of route reached
                    if (NextSignalObject[0] != null && PresentPosition[Direction.Forward].TrackCircuitSectionIndex == NextSignalObject[0].TrackCircuitIndex &&
                         NextSignalObject[0].TrackCircuitNextIndex == ValidRoute[0][lastValidRouteIndex].TrackCircuitSection.Index && reversalSectionIndex < 0 &&
                         NextSignalObject[0].SignalLR(SignalFunction.Normal) == SignalAspectState.Stop && TCRoute.ReversalInfo[TCRoute.ActiveSubPath].Valid)
                    {
                        endOfRoute = true;
                    }
                }
            }

            // MSTS double reversal point: can be recognized and passed at speed > 0
            else
            {
                float distanceToReversalPoint = ComputeDistanceToReversalPoint();
                if (distanceToReversalPoint <= 0 && distanceToReversalPoint != -1)
                    endOfRoute = true;
            }

            return endOfRoute;
        }

        /// <summary>
        /// Update route clearance ahead of train
        /// Called every update, actions depend on present control state
        /// </summary>
        protected void UpdateRouteClearanceAhead(int signalObjectIndex, int backward, double elapsedClockSeconds)
        {
            switch (ControlMode)
            {
                case (TrainControlMode.AutoSignal):
                    {
                        UpdateSignalMode(signalObjectIndex, backward, elapsedClockSeconds);
                        break;
                    }
                case (TrainControlMode.AutoNode):
                    {
                        UpdateNodeMode();
                        break;
                    }
                case (TrainControlMode.OutOfControl):
                    {
                        UpdateOutOfControl();
                        if (LeadLocomotive != null)
                            ((MSTSLocomotive)LeadLocomotive).SetEmergency(true);
                        break;
                    }
                case (TrainControlMode.Undefined):
                    {
                        SwitchToNodeControl(-1);
                        break;
                    }

                // other modes are processed directly
                default:
                    break;
            }

            // reset signal which we've just passed

            if (signalObjectIndex >= 0)
            {
                Signal signalObject = Simulator.Instance.SignalEnvironment.Signals[signalObjectIndex];

                //the following is added by JTang, passing a hold signal, will take back control by the system
                if (signalObject.HoldState == SignalHoldState.ManualPass || signalObject.HoldState == SignalHoldState.ManualApproach)
                {
                    signalObject.HoldState = SignalHoldState.None;
                }

                AllowedCallOnSignal = null;

                signalObject.ResetSignalEnabled();
            }
        }

        /// <summary>
        /// Perform auto signal mode update
        /// </summary>
        protected void UpdateSignalMode(int signalObjectIndex, int backward, double elapsedClockSeconds)
        {
            // in AUTO mode, use forward route only
            // if moving backward, check if slipped passed signal, if so, re-enable signal

            if (backward > BackwardThreshold)
            {
                if (NextSignalObject[0] != null && NextSignalObject[0].EnabledTrain != RoutedForward)
                {
                    if (NextSignalObject[0].EnabledTrain != null)
                    {
                        NextSignalObject[0].ResetSignal(true);
                    }
                    signalObjectIndex = NextSignalObject[0].Index;
                }
            }

            // if signal passed, send request to clear to next signal
            // if next signal not enabled, also send request (can happen after choosing passing path)
            if (signalObjectIndex >= 0)
            {
                Signal signal = Simulator.Instance.SignalEnvironment.Signals[signalObjectIndex];
                int nextSignalIndex = signal.Signalfound[(int)SignalFunction.Normal];
                if (nextSignalIndex >= 0)
                {
                    Signal nextSignal = Simulator.Instance.SignalEnvironment.Signals[nextSignalIndex];
                    nextSignal.RequestClearSignal(ValidRoute[0], RoutedForward, 0, false, null);
                }
            }
            // if next signal not enabled or enabled for other train, also send request (can happen after choosing passing path or after detach)
            else if (NextSignalObject[0] != null && (!NextSignalObject[0].Enabled || NextSignalObject[0].EnabledTrain != RoutedForward))
            {
                NextSignalObject[0].RequestClearSignal(ValidRoute[0], RoutedForward, 0, false, null);
            }
            // check if waiting for signal
            else if (SpeedMpS < Math.Abs(0.1) && NextSignalObject[0] != null &&
                     GetNextSignalAspect(0) == SignalAspectState.Stop && CheckTrainWaitingForSignal(NextSignalObject[0], Direction.Forward))
            {
                // perform special actions on stopped at signal for specific train classes
                bool claimAllowed = ActionsForSignalStop();

                // cannot claim on deadlock to prevent further deadlocks
                if (CheckDeadlockWait(NextSignalObject[0]))
                    claimAllowed = false;

                // cannot claim while in waitstate as this would lock path for other train
                if (InWaitState())
                    claimAllowed = false;

                // cannot claim on hold signal
                if (HoldingSignals.Contains(NextSignalObject[0].Index))
                    claimAllowed = false;

                // process claim if allowed
                if (claimAllowed)
                {
                    if (NextSignalObject[0].SignalRoute.CheckStoppedTrains()) // do not claim when train ahead is stationary or in Manual mode
                    {
                        ActualWaitTimeS = StandardWaitTimeS;  // allow immediate claim if other train moves
                        ClaimState = false;
                    }
                    else
                    {
                        ActualWaitTimeS += elapsedClockSeconds;
                        if (ActualWaitTimeS > StandardWaitTimeS)
                        {
                            ClaimState = true;
                        }
                    }
                }
                else
                {
                    ActualWaitTimeS = 0.0;
                    ClaimState = false;

                    // Reset any invalid claims (occurs on WAIT commands, reason still to be checked!) - not unclaiming causes deadlocks
                    for (int i = PresentPosition[Direction.Forward].RouteListIndex; i <= ValidRoute[0].Count - 1; i++)
                    {
                        ValidRoute[0][i].TrackCircuitSection.CircuitState.TrainClaimed.Remove(RoutedForward);
                    }
                }
            }
            else
            {
                ActualWaitTimeS = 0.0;
                ClaimState = false;
            }
        }

        /// <summary>
        /// Test if call on allowed
        /// </summary>
        internal virtual bool TestCallOn(Signal signal, bool allowOnNonePlatform, TrackCircuitPartialPathRoute route)
        {
            if (AllowedCallOnSignal != signal)
            {
                bool intoPlatform = false;

                foreach (TrackCircuitRouteElement routeElement in signal.SignalRoute)
                {
                    // check if route leads into platform
                    if (routeElement.TrackCircuitSection.PlatformIndices.Count > 0)
                    {
                        intoPlatform = true;
                    }
                }

                //if track does not lead into platform, return state as defined in call
                // else never allow if track leads into platform
                return !intoPlatform && allowOnNonePlatform;
            }

            return true;
        }

        /// <summary>
        /// Check if train is waiting for signal
        /// </summary>
        private protected bool CheckTrainWaitingForSignal(Signal signal, Direction direction)
        {
            TrainRouted routed = direction == 0 ? RoutedForward : RoutedBackward;
            int trainRouteIndex = PresentPosition[direction].RouteListIndex;
            int signalRouteIndex = ValidRoute[(int)direction].GetRouteIndex(signal.TrackCircuitIndex, trainRouteIndex);

            // signal section is not in train route, so train can't be waiting for signal
            if (signalRouteIndex < 0)
            {
                return false;
            }

            // check if any other trains in section ahead of this train

            TrackCircuitSection section = ValidRoute[0][trainRouteIndex].TrackCircuitSection;

            Dictionary<Train, float> trainAhead = section.TestTrainAhead(this, PresentPosition[Direction.Forward].Offset, PresentPosition[Direction.Forward].Direction);

            if (trainAhead.Count > 0)
            {
                KeyValuePair<Train, float> foundTrain = trainAhead.ElementAt(0);
                // check if train is closer as signal
                if (!DistanceToSignal.HasValue || foundTrain.Value < DistanceToSignal)
                {
                    return false;
                }
            }

            // check if any other sections inbetween train and signal
            if (trainRouteIndex != signalRouteIndex)
            {
                for (int i = trainRouteIndex + 1; i <= signalRouteIndex; i++)
                {
                    TrackCircuitSection nextSection = ValidRoute[0][i].TrackCircuitSection;

                    if (nextSection.CircuitState.Occupied())  // train is ahead - it's not our signal //
                    {
                        return false;
                    }
                    else if (!nextSection.IsAvailable(this)) // is section really available to us? //
                    // something is wrong - section upto signal is not available - give warning and switch to node control
                    // also reset signal if it was enabled to us
                    {
                        Trace.TraceWarning($"Train {Name} ({Number}) in Signal control but route to signal not cleared - switching to Node control");

                        if (signal.EnabledTrain == routed)
                        {
                            signal.ResetSignal(true);
                        }
                        SwitchToNodeControl(section.Index);

                        return false;
                    }
                }
            }

            if (signal.EnabledTrain == null) // we are waiting, but is signal clearance requested ?
            {
                signal.RequestClearSignal(ValidRoute[0], routed, 0, false, null);
            }
            else if (signal.EnabledTrain != routed) // we are waiting, but is it really our signal ?
            {
                // something is wrong - we are waiting, but it is not our signal - give warning, reset signal and clear route
                Trace.TraceWarning($"Train {Name} ({Number}) waiting for signal which is enabled to train {signal.EnabledTrain.Train.Number}");

                // stop other train - switch other train to node control
                Train otherTrain = signal.EnabledTrain.Train;
                otherTrain.LastReservedSection[0] = -1;
                if (Math.Abs(otherTrain.SpeedMpS) > 0)
                {
                    otherTrain.ForcedStop(Simulator.Catalog.GetString("Stopped due to errors in route setting"), Name, Number);
                }
                otherTrain.SwitchToNodeControl(-1);

                // reset signal and clear route

                signal.ResetSignal(false);
                signal.RequestClearSignal(ValidRoute[0], routed, 0, false, null);
                return false;   // do not yet set to waiting, signal might clear //
            }

            // signal is in holding list - so not really waiting - but remove from list if held for station stop
            if (signal.HoldState == SignalHoldState.ManualLock)
            {
                return false;
            }
            else if (signal.HoldState == SignalHoldState.StationStop && HoldingSignals.Contains(signal.Index))
            {
                if (StationStops != null && StationStops.Count > 0 && StationStops[0].ExitSignal != signal.Index) // not present station stop
                {
                    HoldingSignals.Remove(signal.Index);
                    signal.HoldState = SignalHoldState.None;
                    return false;
                }
            }

            return true;  // it is our signal and we are waiting //
        }

        /// <summary>
        /// Perform auto node mode update
        /// </summary>
        protected virtual void UpdateNodeMode()
        {

            // update distance to end of authority
            int lastRouteIndex = ValidRoute[0].GetRouteIndex(LastReservedSection[0], PresentPosition[Direction.Forward].RouteListIndex);

            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[PresentPosition[Direction.Forward].TrackCircuitSectionIndex];
            DistanceToEndNodeAuthorityM[0] = section.Length - PresentPosition[Direction.Forward].Offset;

            for (int i = PresentPosition[Direction.Forward].RouteListIndex + 1; i <= lastRouteIndex; i++)
            {
                section = ValidRoute[0][i].TrackCircuitSection;
                DistanceToEndNodeAuthorityM[0] += section.Length;
            }

            // run out of authority : train is out of control

            // TODO : check end of (sub)path
            //        set variable accordingly
            //
            //            if (DistanceToEndNodeAuthorityM < 0.0f)
            //            {
            //                SetTrainOutOfControl(OUTOFCONTROL.OUT_OF_AUTHORITY);
            //                return;
            //            }

            if (EndAuthorityTypes[0] == EndAuthorityType.MaxDistance && DistanceToEndNodeAuthorityM[0] > MaxDistanceCheckedAhead)
            {
                return;   // no update required //
            }
            // perform node update - forward only

            Simulator.Instance.SignalEnvironment.RequestClearNode(RoutedForward, ValidRoute[0]);
        }

        /// <summary>
        /// Switches switch after dispatcher window command, when in auto mode
        /// </summary>
        internal void ProcessRequestAutoSetSwitch(int requiredSwitchIndex)
        {
            TrackCircuitSection reqSwitch = TrackCircuitSection.TrackCircuitList[requiredSwitchIndex];
            if (reqSwitch.CircuitState.TrainReserved != null && reqSwitch.CircuitState.TrainReserved.Train == this)
            {
                // store required position
                int reqSwitchPosition = reqSwitch.JunctionSetManual;
                ClearReservedSections();
                Reinitialize();
                reqSwitch.JunctionSetManual = reqSwitchPosition;
            }
        }

        /// <summary>
        /// Update section occupy states for manual mode
        /// Note : manual mode has no distance actions so sections must be cleared immediately
        /// </summary>
        private void UpdateSectionStateManual()
        {
            // occupation is set in forward mode only
            // build route from rear to front - before reset occupy so correct switch alignment is used
            manualTrainRoute = SignalEnvironment.BuildTempRoute(this, PresentPosition[Direction.Backward].TrackCircuitSectionIndex, PresentPosition[Direction.Backward].Offset, PresentPosition[Direction.Backward].Direction, Length, false, true, false);

            // save present occupation list
            List<TrackCircuitSection> clearedSections = new List<TrackCircuitSection>();
            for (int i = OccupiedTrack.Count - 1; i >= 0; i--)
            {
                clearedSections.Add(OccupiedTrack[i]);
            }

            // set track occupied
            OccupiedTrack.Clear();

            foreach (TrackCircuitRouteElement routeElement in manualTrainRoute)
            {
                TrackCircuitSection section = routeElement.TrackCircuitSection;

                if (clearedSections.Contains(section))
                {
                    section.ResetOccupied(this); // reset occupation if it was occupied
                    clearedSections.Remove(section);  // remove from cleared list
                }

                section.Reserve(RoutedForward, manualTrainRoute);  // reserve first to reset switch alignments
                section.SetOccupied(RoutedForward);
            }

            foreach (TrackCircuitSection clearedSection in clearedSections)
            {
                clearedSection.ClearOccupied(this, true); // sections really cleared
            }
        }

        /// <summary>
        /// Update Manual Mode
        /// </summary>
        private void UpdateManualMode(int signalObjectIndex)
        {
            // check present forward
            TrackCircuitPartialPathRoute newRouteF = CheckManualPath(Direction.Forward, PresentPosition[Direction.Forward], ValidRoute[0], true, ref EndAuthorityTypes[0],
                ref DistanceToEndNodeAuthorityM[0]);
            ValidRoute[0] = newRouteF;
            int routeIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0);
            PresentPosition[Direction.Forward].RouteListIndex = routeIndex;

            // check present reverse
            // reverse present rear position direction to build correct path backwards
            TrackCircuitPosition tempRear = new TrackCircuitPosition(PresentPosition[Direction.Backward], true);
            TrackCircuitPartialPathRoute newRouteR = CheckManualPath(Direction.Backward, tempRear, ValidRoute[1], true, ref EndAuthorityTypes[1], ref DistanceToEndNodeAuthorityM[1]);
            ValidRoute[1] = newRouteR;

            // select valid route
            if (MUDirection == MidpointDirection.Forward)
            {
                // use position from other end of section
                float reverseOffset = TrackCircuitSection.TrackCircuitList[PresentPosition[Direction.Backward].TrackCircuitSectionIndex].Length - PresentPosition[Direction.Backward].Offset;
                CheckSpeedLimitManual(ValidRoute[1], manualTrainRoute, reverseOffset, PresentPosition[Direction.Backward].Offset, signalObjectIndex, 0);
            }
            else
            {
                TrackCircuitPartialPathRoute tempRoute = new TrackCircuitPartialPathRoute(); // reversed trainRoute
                for (int i = manualTrainRoute.Count - 1; i >= 0; i--)
                {
                    TrackCircuitRouteElement routeElement = manualTrainRoute[i];
                    routeElement.Direction = routeElement.Direction.Reverse();
                    tempRoute.Add(routeElement);
                }
                float reverseOffset = TrackCircuitSection.TrackCircuitList[PresentPosition[Direction.Forward].TrackCircuitSectionIndex].Length - PresentPosition[Direction.Forward].Offset;
                CheckSpeedLimitManual(ValidRoute[0], tempRoute, PresentPosition[Direction.Forward].Offset, reverseOffset, signalObjectIndex, 1);
            }

            // reset signal
            if (signalObjectIndex >= 0)
            {
                Signal signal = Simulator.Instance.SignalEnvironment.Signals[signalObjectIndex];
                signal.OverridePermission = SignalPermission.Denied;
                //the following is added by JTang, passing a hold signal, will take back control by the system
                if (signal.HoldState == SignalHoldState.ManualPass || signal.HoldState == SignalHoldState.ManualApproach)
                    signal.HoldState = SignalHoldState.None;

                AllowedCallOnSignal = null;

                signal.ResetSignalEnabled();
            }

            // get next signal

            // forward
            TrackCircuitRouteElement element = ValidRoute[0][0];
            NextSignalObject[0] = element.TrackCircuitSection.EndSignals[element.Direction];

            // backward
            element = ValidRoute[1][0];
            NextSignalObject[1] = element.TrackCircuitSection.EndSignals[element.Direction];

            // clear all build up distance actions
            requiredActions.RemovePendingAIActionItems(true);
        }

        /// <summary>
        /// Check Manual Path
        /// <\summary>
        private TrackCircuitPartialPathRoute CheckManualPath(Direction direction, TrackCircuitPosition requiredPosition, TrackCircuitPartialPathRoute requiredRoute, bool forward,
            ref EndAuthorityType endAuthority, ref float endAuthorityDistanceM)
        {
            TrainRouted routedTrain = direction == Direction.Forward ? RoutedForward : RoutedBackward;

            // create new route or set to existing route
            TrackCircuitPartialPathRoute newRoute = requiredRoute ?? new TrackCircuitPartialPathRoute();

            // check if train on valid position in route
            int routeIndex = newRoute.GetRouteIndex(requiredPosition.TrackCircuitSectionIndex, 0);
            TrackCircuitRouteElement routeElement;

            if (routeIndex < 0)    // no valid point in route
            {
                // check if run out of route on misaligned switch
                if (newRoute.Count > 0)
                {
                    // get last section, and get next expected section
                    TrackCircuitSection lastSection = newRoute[newRoute.Count - 1].TrackCircuitSection;
                    int nextSectionIndex = lastSection.ActivePins[newRoute[newRoute.Count - 1].Direction, Location.NearEnd].Link;

                    if (nextSectionIndex >= 0)
                    {
                        TrackCircuitSection nextSection = TrackCircuitSection.TrackCircuitList[nextSectionIndex];

                        // is next expected section misaligned switch and is present section trailing end of this switch
                        if (nextSectionIndex == misalignedSwitch[direction, TrackDirection.Ahead] && lastSection.Index == misalignedSwitch[direction, TrackDirection.Reverse] &&
                            nextSection.ActivePins[TrackDirection.Ahead, Location.NearEnd].Link == requiredPosition.TrackCircuitSectionIndex)
                        {

                            // misaligned switch

                            // reset indication
                            misalignedSwitch[direction, TrackDirection.Ahead] = -1;
                            misalignedSwitch[direction, TrackDirection.Reverse] = -1;

                            // set to out of control
                            SetTrainOutOfControl(OutOfControlReason.MisalignedSwitch);

                            // recalculate track position
                            UpdateTrainPosition();

                            // rebuild this list
                            UpdateSectionStateManual();

                            // exit

                            return (newRoute);
                        }
                    }
                }

                if (requiredRoute != null && requiredRoute.Count > 0)  // if route defined, then breakdown route
                {
                    Simulator.Instance.SignalEnvironment.BreakDownRouteList(requiredRoute, 0, routedTrain);
                    requiredRoute.Clear();
                }

                // build new route
                misalignedSwitch[direction, TrackDirection.Ahead] = -1;
                misalignedSwitch[direction, TrackDirection.Reverse] = -1;

                List<int> tempSections = SignalEnvironment.ScanRoute(this, requiredPosition.TrackCircuitSectionIndex, requiredPosition.Offset,
                        requiredPosition.Direction, forward, MinCheckDistanceManualM, true, false, true, false, true, false, false, false, false, IsFreight);

                if (tempSections.Count > 0)
                {
                    // create subpath route
                    int prevSection = -2;    // preset to invalid

                    foreach (int sectionIndex in tempSections)
                    {
                        TrackDirection sectionDirection = sectionIndex > 0 ? TrackDirection.Ahead : TrackDirection.Reverse;
                        routeElement = new TrackCircuitRouteElement(TrackCircuitSection.TrackCircuitList[Math.Abs(sectionIndex)], sectionDirection, prevSection);
                        newRoute.Add(routeElement);
                        prevSection = Math.Abs(sectionIndex);
                    }
                }
            }
            // remove any sections before present position - train has passed over these sections
            else if (routeIndex > 0)
            {
                newRoute.RemoveRange(0, routeIndex - 1);
            }

            // check if route ends at signal, determine length
            float totalLengthM = 0;
            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[requiredPosition.TrackCircuitSectionIndex];
            float offsetM = direction == 0 ? requiredPosition.Offset : section.Length - requiredPosition.Offset;
            bool endWithSignal = false;    // ends with signal at STOP
            bool hasEndSignal = false;     // ends with cleared signal
            int sectionWithSignalIndex = 0;

            Signal previousSignal = null;

            TrackDirection reqDirection;
            for (int i = 0; i < newRoute.Count && !endWithSignal; i++)
            {
                routeElement = newRoute[i];

                section = routeElement.TrackCircuitSection;
                totalLengthM += (section.Length - offsetM);
                offsetM = 0.0f; // reset offset for further sections

                reqDirection = routeElement.Direction;
                if (section.EndSignals[reqDirection] != null)
                {
                    Signal endSignal = section.EndSignals[reqDirection];
                    SignalAspectState aspect = section.EndSignals[reqDirection].SignalLR(SignalFunction.Normal);
                    hasEndSignal = true;
                    if (previousSignal != null)
                        previousSignal.Signalfound[(int)SignalFunction.Normal] = endSignal.Index;
                    previousSignal = section.EndSignals[reqDirection];

                    if (aspect == SignalAspectState.Stop && endSignal.OverridePermission != SignalPermission.Granted)
                    {
                        endWithSignal = true;
                        sectionWithSignalIndex = i;
                    }
                    else if (endSignal.EnabledTrain == null && endSignal.FixedRoute) // signal cleared by default - make sure train is set
                    {
                        endSignal.EnabledTrain = routedTrain;
                        endSignal.SetDefaultRoute();
                    }
                }
            }

            // check if signal is in last section
            // if not, probably moved forward beyond a signal, so remove all beyond first signal

            if (endWithSignal && sectionWithSignalIndex < newRoute.Count - 1)
            {
                for (int iindex = newRoute.Count - 1; iindex >= sectionWithSignalIndex + 1; iindex--)
                {
                    section = newRoute[iindex].TrackCircuitSection;
                    section.RemoveTrain(this, true);
                    newRoute.RemoveAt(iindex);
                }
            }

            // if route does not end with signal and is too short, extend

            if (!endWithSignal && totalLengthM < MinCheckDistanceManualM)
            {

                float extendedDistanceM = MinCheckDistanceManualM - totalLengthM;
                TrackCircuitRouteElement lastElement = newRoute[newRoute.Count - 1];

                TrackCircuitSection lastSection = lastElement.TrackCircuitSection;

                int nextSectionIndex = lastSection.Pins[lastElement.OutPin[Location.NearEnd], (Location)lastElement.OutPin[Location.FarEnd]].Link;
                TrackDirection nextSectionDirection = lastSection.Pins[lastElement.OutPin[Location.NearEnd], (Location)lastElement.OutPin[Location.FarEnd]].Direction;

                // check if last item is non-aligned switch

                misalignedSwitch[direction, TrackDirection.Ahead] = -1;
                misalignedSwitch[direction, TrackDirection.Reverse] = -1;

                TrackCircuitSection nextSection = nextSectionIndex >= 0 ? TrackCircuitSection.TrackCircuitList[nextSectionIndex] : null;
                if (nextSection != null && nextSection.CircuitType == TrackCircuitType.Junction)
                {
                    if (nextSection.Pins[TrackDirection.Ahead, Location.NearEnd].Link != lastSection.Index &&
                        nextSection.Pins[TrackDirection.Reverse, (Location)nextSection.JunctionLastRoute].Link != lastSection.Index)
                    {
                        misalignedSwitch[direction, TrackDirection.Ahead] = nextSection.Index;
                        misalignedSwitch[direction, TrackDirection.Reverse] = lastSection.Index;
                    }
                }

                List<int> tempSections = new List<int>();

                if (nextSectionIndex >= 0 && misalignedSwitch[direction, TrackDirection.Ahead] < 0)
                {
                    bool reqAutoAlign = hasEndSignal; // auto-align switchs if route is extended from signal

                    tempSections = SignalEnvironment.ScanRoute(this, nextSectionIndex, 0, nextSectionDirection, forward, extendedDistanceM, true, reqAutoAlign,
                            true, false, true, false, false, false, false, IsFreight);
                }

                if (tempSections.Count > 0)
                {
                    // add new sections
                    int prevSection = lastElement.TrackCircuitSection.Index;

                    foreach (int sectionIndex in tempSections)
                    {
                        routeElement = new TrackCircuitRouteElement(TrackCircuitSection.TrackCircuitList[Math.Abs(sectionIndex)],
                            sectionIndex > 0 ? TrackDirection.Ahead : TrackDirection.Reverse, prevSection);
                        newRoute.Add(routeElement);
                        prevSection = Math.Abs(sectionIndex);
                    }
                }
            }
            // if route is too long, remove sections at end
            else if (totalLengthM > MinCheckDistanceManualM)
            {
                float remainingLengthM = totalLengthM - newRoute[0].TrackCircuitSection.Length; // do not count first section
                bool lengthExceeded = remainingLengthM > MinCheckDistanceManualM;

                for (int iindex = newRoute.Count - 1; iindex > 1 && lengthExceeded; iindex--)
                {
                    routeElement = newRoute[iindex];
                    section = routeElement.TrackCircuitSection;

                    if ((remainingLengthM - section.Length) > MinCheckDistanceManualM)
                    {
                        remainingLengthM -= section.Length;
                        newRoute.RemoveAt(iindex);
                    }
                    else
                    {
                        lengthExceeded = false;
                    }
                }
            }

            // route created to signal or max length, now check availability
            // check if other train in first section
            if (newRoute.Count > 0)
            {
                routeElement = newRoute[0];
                section = routeElement.TrackCircuitSection;
                reqDirection = forward ? routeElement.Direction : (routeElement.Direction).Reverse();
                offsetM = direction == 0 ? requiredPosition.Offset : section.Length - requiredPosition.Offset;

                Dictionary<Train, float> firstTrainInfo = section.TestTrainAhead(this, offsetM, reqDirection);
                if (firstTrainInfo.Count > 0)
                {
                    foreach (KeyValuePair<Train, float> thisTrainAhead in firstTrainInfo)  // there is only one value
                    {
                        endAuthority = EndAuthorityType.TrainAhead;
                        endAuthorityDistanceM = thisTrainAhead.Value;
                        if (!section.CircuitState.OccupiedByThisTrain(this))
                            section.PreReserve(routedTrain);
                    }
                    RemoveSignalEnablings(0, newRoute);
                }
                // check route availability
                // reserve sections which are available
                else
                {
                    int lastValidSectionIndex = 0;
                    bool isAvailable = true;
                    totalLengthM = 0;

                    for (int iindex = 0; iindex < newRoute.Count && isAvailable; iindex++)
                    {
                        section = newRoute[iindex].TrackCircuitSection;

                        if (isAvailable)
                        {
                            if (section.IsAvailable(this))
                            {
                                lastValidSectionIndex = iindex;
                                totalLengthM += (section.Length - offsetM);
                                offsetM = 0;
                                section.Reserve(routedTrain, newRoute);
                            }
                            else
                            {
                                isAvailable = false;
                            }
                        }
                    }

                    // set default authority to max distance

                    // if last section ends with signal, set authority to signal
                    routeElement = newRoute[lastValidSectionIndex];
                    section = routeElement.TrackCircuitSection;
                    reqDirection = forward ? routeElement.Direction : routeElement.Direction.Reverse();
                    // last section ends with signal
                    if (section.EndSignals[reqDirection] != null)
                    {
                        endAuthority = EndAuthorityType.Signal;
                        endAuthorityDistanceM = totalLengthM;
                    }
                    // sections not clear - check if end has signal
                    else
                    {

                        TrackCircuitSection nextSection = null;
                        TrackCircuitRouteElement nextElement = null;

                        if (lastValidSectionIndex < newRoute.Count - 1)
                        {
                            nextElement = newRoute[lastValidSectionIndex + 1];
                            nextSection = nextElement.TrackCircuitSection;
                        }

                        // check for end authority if not ended with signal
                        // last section is end of track
                        if (section.CircuitType == TrackCircuitType.EndOfTrack)
                        {
                            endAuthority = EndAuthorityType.EndOfTrack;
                            endAuthorityDistanceM = totalLengthM;
                        }
                        // first non-available section is switch or crossover
                        else if (nextSection != null && (nextSection.CircuitType == TrackCircuitType.Junction ||
                                     nextSection.CircuitType == TrackCircuitType.Crossover))
                        {
                            endAuthority = EndAuthorityType.ReservedSwitch;
                            endAuthorityDistanceM = totalLengthM;
                        }
                        // set authority is end of path unless train ahead
                        else
                        {
                            endAuthority = EndAuthorityType.EndOfPath;
                            endAuthorityDistanceM = totalLengthM;

                            // check if train ahead not moving in opposite direction, in first non-available section

                            if (nextSection != null)
                            {
                                int oppositeDirection = forward ? (nextElement.Direction == 0 ? 1 : 0) : (nextElement.Direction == 0 ? 0 : 1);
                                reqDirection = forward ? nextElement.Direction : (nextElement.Direction).Reverse();

                                bool oppositeTrain = nextSection.CircuitState.Occupied(oppositeDirection, false);

                                if (!oppositeTrain)
                                {
                                    Dictionary<Train, float> nextTrainInfo = nextSection.TestTrainAhead(this, 0.0f, reqDirection);
                                    if (nextTrainInfo.Count > 0)
                                    {
                                        foreach (KeyValuePair<Train, float> thisTrainAhead in nextTrainInfo)  // there is only one value
                                        {
                                            endAuthority = EndAuthorityType.TrainAhead;
                                            endAuthorityDistanceM = thisTrainAhead.Value + totalLengthM;
                                            lastValidSectionIndex++;
                                            nextSection.PreReserve(routedTrain);
                                        }
                                        RemoveSignalEnablings(lastValidSectionIndex, newRoute);
                                    }
                                }
                            }
                        }
                    }

                    // remove invalid sections from route
                    if (lastValidSectionIndex < newRoute.Count - 1)
                    {
                        for (int i = newRoute.Count - 1; i > lastValidSectionIndex; i--)
                        {
                            newRoute.RemoveAt(i);
                        }
                    }
                }
            }
            // no valid route could be found
            else
            {
                endAuthority = EndAuthorityType.NoPathReserved;
                endAuthorityDistanceM = 0.0f;
            }

            return newRoute;
        }

        /// <summary>
        /// Remove signal enablings for subsequent route sections.
        /// They were set before testing whether there is an occupying train
        /// </summary>
        private void RemoveSignalEnablings(int firstSection, TrackCircuitPartialPathRoute newRoute)
        {
            for (int i = firstSection; i <= newRoute.Count - 1; i++)
            {
                TrackCircuitRouteElement routeElement = newRoute[i];
                TrackCircuitSection routeSection = routeElement.TrackCircuitSection;
                TrackDirection thisReqDirection = routeElement.Direction;
                if (routeSection.EndSignals[thisReqDirection] != null)
                {
                    Signal endSignal = routeSection.EndSignals[thisReqDirection];
                    if (endSignal.EnabledTrain?.Train == this)
                        endSignal.EnabledTrain = null;
                }
            }
        }

        /// <summary>
        /// Restore Manual Mode
        /// </summary>
        internal void RestoreManualMode()
        {
            // get next signal

            // forward
            NextSignalObject[0] = null;
            for (int i = 0; i < ValidRoute[0].Count && NextSignalObject[0] == null; i++)
            {
                TrackCircuitRouteElement routeElement = ValidRoute[0][i];
                NextSignalObject[0] = routeElement.TrackCircuitSection.EndSignals[routeElement.Direction];
            }

            // backward
            NextSignalObject[1] = null;
            for (int i = 0; i < ValidRoute[1].Count && NextSignalObject[1] == null; i++)
            {
                TrackCircuitRouteElement routeElement = ValidRoute[1][i];
                NextSignalObject[1] = routeElement.TrackCircuitSection.EndSignals[routeElement.Direction];
            }
        }

        // Request signal permission in manual mode
        private void RequestManualSignalPermission(TrackCircuitPartialPathRoute selectedRoute, int routeIndex)
        {
            // check if route ends with signal at danger
            TrackCircuitRouteElement lastElement = selectedRoute[selectedRoute.Count - 1];
            TrackCircuitSection lastSection = lastElement.TrackCircuitSection;

            // no signal in required direction at end of path
            if (lastSection.EndSignals[lastElement.Direction] == null)
            {
                simulator.Confirmer?.Message(ConfirmLevel.Information, Simulator.Catalog.GetString("No signal in train's path"));
                return;
            }

            Signal requestedSignal = lastSection.EndSignals[lastElement.Direction];
            if (requestedSignal.EnabledTrain != null && requestedSignal.EnabledTrain.Train != this)
            {
                simulator.Confirmer?.Message(ConfirmLevel.Warning, Simulator.Catalog.GetString("Next signal already allocated to other train"));
                simulator.SoundNotify = TrainEvent.PermissionDenied;
                return;
            }

            requestedSignal.EnabledTrain = routeIndex == 0 ? RoutedForward : RoutedBackward;
            requestedSignal.SignalRoute.Clear();
            requestedSignal.HoldState = SignalHoldState.None;
            requestedSignal.OverridePermission = SignalPermission.Requested;

            // get route from next signal - extend to next signal or maximum length

            // first, get present length (except first section)

            float totalLengthM = 0;
            for (int i = 1; i < selectedRoute.Count; i++)
            {
                totalLengthM += selectedRoute[i].TrackCircuitSection.Length;
            }

            float remainingLengthM = Math.Min(MinCheckDistanceManualM, Math.Max((MinCheckDistanceManualM - totalLengthM), (MinCheckDistanceManualM * 0.25f)));

            // get section behind signal
            int nextSectionIndex = lastSection.Pins[lastElement.OutPin[Location.NearEnd], (Location)lastElement.OutPin[Location.FarEnd]].Link;
            TrackDirection nextSectionDirection = lastSection.Pins[lastElement.OutPin[Location.NearEnd], (Location)lastElement.OutPin[Location.FarEnd]].Direction;

            bool requestValid = false;

            // get route from signal - set remaining length or upto next signal

            if (nextSectionIndex > 0)
            {
                List<int> tempSections = SignalEnvironment.ScanRoute(this, nextSectionIndex, 0, nextSectionDirection, true, remainingLengthM, true, true,
                    true, false, true, false, false, false, false, IsFreight);

                // set as signal route
                if (tempSections.Count > 0)
                {
                    int prevSection = -1;

                    foreach (int sectionIndex in tempSections)
                    {
                        TrackCircuitRouteElement thisElement = new TrackCircuitRouteElement(TrackCircuitSection.TrackCircuitList[Math.Abs(sectionIndex)],
                                sectionIndex > 0 ? TrackDirection.Ahead : TrackDirection.Reverse, prevSection);
                        requestedSignal.SignalRoute.Add(thisElement);
                        selectedRoute.Add(thisElement);
                        prevSection = Math.Abs(sectionIndex);
                    }

                    requestedSignal.CheckRouteState(false, requestedSignal.SignalRoute, RoutedForward);
                    requestValid = true;
                }

                if (!requestValid)
                {
                    simulator.Confirmer?.Message(ConfirmLevel.Information, Simulator.Catalog.GetString("Request to clear signal cannot be processed"));
                    simulator.SoundNotify = TrainEvent.PermissionDenied;
                }
            }
        }

        /// <summary>
        /// Process request to set switch in manual mode
        /// Request may contain direction or actual node
        /// </summary>
        internal bool ProcessRequestManualSetSwitch(Direction direction)
        {
            // find first switch in required direction

            TrackCircuitSection reqSwitch = null;
            int routeDirectionIndex = (int)direction;
            bool switchSet = false;

            for (int i = 0; i < ValidRoute[routeDirectionIndex].Count; i++)
            {
                TrackCircuitSection section = ValidRoute[routeDirectionIndex][i].TrackCircuitSection;
                if (section.CircuitType == TrackCircuitType.Junction)
                {
                    reqSwitch = section;
                    break;
                }
            }

            if (reqSwitch == null)
            {
                // search beyond last section for switch using default pins (continue through normal sections only)

                TrackCircuitRouteElement routeElement = ValidRoute[routeDirectionIndex][ValidRoute[routeDirectionIndex].Count - 1];
                TrackCircuitSection lastSection = routeElement.TrackCircuitSection;
                TrackDirection curDirection = routeElement.Direction;
                int nextSectionIndex = routeElement.TrackCircuitSection.Index;

                bool validRoute = lastSection.CircuitType == TrackCircuitType.Normal;

                while (reqSwitch == null && validRoute)
                {
                    if (lastSection.CircuitType == TrackCircuitType.Crossover)
                    {
                        TrackDirection outPinIndex = curDirection.Reverse();
                        if (lastSection.Pins[curDirection, Location.NearEnd].Link == nextSectionIndex)
                        {
                            nextSectionIndex = lastSection.Pins[outPinIndex, Location.NearEnd].Link;
                            curDirection = lastSection.Pins[outPinIndex, Location.NearEnd].Direction;
                        }
                        else if (lastSection.Pins[curDirection, Location.FarEnd].Link == nextSectionIndex)
                        {
                            nextSectionIndex = lastSection.Pins[outPinIndex, Location.FarEnd].Link;
                            curDirection = lastSection.Pins[outPinIndex, Location.FarEnd].Direction;
                        }
                    }
                    else
                    {
                        nextSectionIndex = lastSection.Pins[curDirection, Location.NearEnd].Link;
                        curDirection = lastSection.ActivePins[curDirection, Location.NearEnd].Direction;
                        lastSection = TrackCircuitSection.TrackCircuitList[nextSectionIndex];
                    }

                    if (lastSection.CircuitType == TrackCircuitType.Junction)
                    {
                        reqSwitch = lastSection;
                    }
                    else if (lastSection.CircuitType != TrackCircuitType.Normal)
                    {
                        validRoute = false;
                    }
                }
            }

            if (reqSwitch != null)
            {
                // check if switch is clear
                if (!reqSwitch.CircuitState.Occupied() && reqSwitch.CircuitState.TrainReserved == null && reqSwitch.CircuitState.SignalReserved < 0)
                {
                    reqSwitch.JunctionSetManual = reqSwitch.JunctionLastRoute == 0 ? 1 : 0;
                    Simulator.Instance.SignalEnvironment.SetSwitch(reqSwitch.OriginalIndex, reqSwitch.JunctionSetManual, reqSwitch);
                    switchSet = true;
                }
                // check if switch reserved by this train - if so, dealign and breakdown route
                else if (reqSwitch.CircuitState.TrainReserved?.Train == this)
                {
                    int reqRouteIndex = reqSwitch.CircuitState.TrainReserved.TrainRouteDirectionIndex;
                    int routeIndex = ValidRoute[reqRouteIndex].GetRouteIndex(reqSwitch.Index, 0);
                    Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[reqRouteIndex], routeIndex, reqSwitch.CircuitState.TrainReserved);
                    if (routeIndex >= 0 && ValidRoute[reqRouteIndex].Count > routeIndex)
                        ValidRoute[reqRouteIndex].RemoveRange(routeIndex, ValidRoute[reqRouteIndex].Count - routeIndex);
                    else
                        Trace.TraceWarning($"Switch index {reqSwitch.Index} could not be found in ValidRoute[{reqRouteIndex}]; routeDirectionIndex = {routeDirectionIndex}");
                    reqSwitch.DeAlignSwitchPins();
                    reqSwitch.JunctionSetManual = reqSwitch.JunctionLastRoute == 0 ? 1 : 0;
                    Simulator.Instance.SignalEnvironment.SetSwitch(reqSwitch.OriginalIndex, reqSwitch.JunctionSetManual, reqSwitch);
                    switchSet = true;
                }

                if (switchSet)
                    ProcessManualSwitch(reqSwitch, direction);
                simulator.Confirmer?.Confirm((direction == Direction.Forward) ? CabControl.SwitchAhead : CabControl.SwitchBehind, CabSetting.On);
            }
            else
            {
                simulator.Confirmer?.Message(ConfirmLevel.Warning, Simulator.Catalog.GetString("No switch found"));
            }

            return switchSet;
        }

        internal void ProcessRequestManualSetSwitch(int reqSwitchIndex)
        {
            // find switch in route - forward first

            bool switchFound = false;
            Direction direction = Direction.Forward;

            for (int i = 0; i < ValidRoute[0].Count - 1 && !switchFound; i++)
            {
                if (ValidRoute[0][i].TrackCircuitSection.Index == reqSwitchIndex)
                {
                    direction = Direction.Forward;
                    switchFound = true;
                }
            }

            for (int i = 0; i < ValidRoute[1].Count - 1 && !switchFound; i++)
            {
                if (ValidRoute[1][i].TrackCircuitSection.Index == reqSwitchIndex)
                {
                    direction = Direction.Backward;
                    switchFound = true;
                }
            }

            if (switchFound)
            {
                ProcessManualSwitch(TrackCircuitSection.TrackCircuitList[reqSwitchIndex], direction);
            }
        }

        /// <summary>
        /// Process switching of manual switch
        /// </summary>
        private void ProcessManualSwitch(TrackCircuitSection switchSection, Direction direction)
        {
            TrainRouted trainRouted = direction == Direction.Backward ? RoutedForward : RoutedBackward; //TODO 20201109 double check why using the forward route for backward direction
            TrackCircuitPartialPathRoute selectedRoute = ValidRoute[(int)direction];

            // store required position
            int reqSwitchPosition = switchSection.JunctionSetManual;

            // find index of section in present route
            int junctionIndex = selectedRoute.GetRouteIndex(switchSection.Index, 0);

            // check if any signals between train and switch
            List<Signal> signalsFound = new List<Signal>();

            for (int i = 0; i < junctionIndex; i++)
            {
                TrackCircuitRouteElement routeElement = selectedRoute[i];
                TrackCircuitSection section = routeElement.TrackCircuitSection;
                TrackDirection signalDirection = routeElement.Direction;

                if (section.EndSignals[signalDirection] != null)
                {
                    signalsFound.Add(section.EndSignals[signalDirection]);
                }
            }

            // if any signals found : reset signals
            foreach (Signal signal in signalsFound)
            {
                signal.ResetSignal(false);
            }

            // breakdown and clear route
            Simulator.Instance.SignalEnvironment.BreakDownRouteList(selectedRoute, 0, trainRouted);
            selectedRoute.Clear();

            // restore required position (is cleared by route breakdown)
            switchSection.JunctionSetManual = reqSwitchPosition;

            // set switch
            switchSection.DeAlignSwitchPins();
            Simulator.Instance.SignalEnvironment.SetSwitch(switchSection.OriginalIndex, switchSection.JunctionSetManual, switchSection);

            // reset indication for misaligned switch
            misalignedSwitch[direction, TrackDirection.Ahead] = -1;
            misalignedSwitch[direction, TrackDirection.Reverse] = -1;

            // build new route

            int routeIndex;

            if (direction == Direction.Forward)
            {
                selectedRoute = CheckManualPath(Direction.Forward, PresentPosition[Direction.Forward], null, true, ref EndAuthorityTypes[0], ref DistanceToEndNodeAuthorityM[0]);
                routeIndex = 0;

            }
            else
            {
                TrackCircuitPosition tempRear = new TrackCircuitPosition(PresentPosition[Direction.Backward], true);
                selectedRoute = CheckManualPath(Direction.Backward, tempRear, null, true, ref EndAuthorityTypes[1], ref DistanceToEndNodeAuthorityM[1]);
                routeIndex = 1;
            }

            // if route ends at previously cleared signal, request clear signal again

            TrackCircuitRouteElement lastElement = selectedRoute[selectedRoute.Count - 1];
            TrackCircuitSection lastSection = lastElement.TrackCircuitSection;
            TrackDirection lastDirection = lastElement.Direction;

            Signal lastSignal = lastSection.EndSignals[lastDirection];

            while (signalsFound.Contains(lastSignal))
            {
                RequestManualSignalPermission(selectedRoute, routeIndex);

                lastElement = selectedRoute[selectedRoute.Count - 1];
                lastSection = lastElement.TrackCircuitSection;
                lastDirection = lastElement.Direction;

                lastSignal = lastSection.EndSignals[lastDirection];
            }

            ValidRoute[(int)direction] = selectedRoute;
        }

        /// <summary>
        /// Update speed limit in manual mode
        /// </summary>
        private void CheckSpeedLimitManual(TrackCircuitPartialPathRoute routeBehind, TrackCircuitPartialPathRoute routeUnderTrain, float offsetStart,
            float reverseOffset, int passedSignalIndex, int routeDirection)
        {
            // check backward for last speedlimit in direction of train - raise speed if passed

            TrackCircuitRouteElement routeElement = routeBehind[0];
            List<int> foundSpeedLimit = SignalEnvironment.ScanRoute(this, routeElement.TrackCircuitSection.Index, offsetStart, routeElement.Direction,
                    true, -1, false, true, false, false, false, false, false, false, true, IsFreight);

            if (foundSpeedLimit.Count > 0)
            {
                Signal speedLimit = Simulator.Instance.SignalEnvironment.Signals[Math.Abs(foundSpeedLimit[0])];
                SpeedInfo speedInfo = speedLimit.SpeedLimit(SignalFunction.Speed);
                float speedMpS = IsFreight ? speedInfo.FreightSpeed : speedInfo.PassengerSpeed;

                if (speedMpS > 0)
                {
                    if (speedInfo.LimitedSpeedReduction == 0)
                        AllowedMaxSpeedLimitMpS = speedMpS;
                    else
                        allowedMaxTempSpeedLimitMpS = speedMpS;
                    if (simulator.TimetableMode)
                        AllowedMaxSpeedMpS = speedMpS;
                    else
                        AllowedMaxSpeedMpS = Math.Min(AllowedMaxSpeedLimitMpS, Math.Min(allowedMaxTempSpeedLimitMpS,
                                       AllowedMaxSpeedSignalMpS == -1 ? 999 : AllowedMaxSpeedSignalMpS));
                }
            }
            // No speed limits behind us, initialize allowedMaxSpeedLimitMpS.
            else if (!simulator.TimetableMode)
            {
                AllowedMaxSpeedMpS = AllowedMaxSpeedLimitMpS;
            }

            // check backward for last signal in direction of train - check with list of pending signal speeds
            // search also checks for speedlimit to see which is nearest train

            foundSpeedLimit.Clear();
            foundSpeedLimit = SignalEnvironment.ScanRoute(this, routeElement.TrackCircuitSection.Index, offsetStart, (TrackDirection)routeElement.Direction,
                    true, -1, false, true, false, false, false, false, true, false, true, IsFreight, true);

            if (foundSpeedLimit.Count > 0)
            {
                Signal signal = Simulator.Instance.SignalEnvironment.Signals[Math.Abs(foundSpeedLimit[0])];
                if (signal.IsSignal)
                {
                    // if signal is now just behind train - set speed as signal speed limit, do not reenter in list
                    if (PassedSignalSpeeds.ContainsKey(signal.Index))
                    {
                        AllowedMaxSpeedSignalMpS = PassedSignalSpeeds[signal.Index];
                        AllowedMaxSpeedMpS = Math.Min(AllowedMaxSpeedSignalMpS, AllowedMaxSpeedMpS);
                        LastPassedSignal[routeDirection] = signal.Index;
                    }
                    // if signal is not last passed signal - reset signal speed limit
                    else if (signal.Index != LastPassedSignal[routeDirection])
                    {
                        AllowedMaxSpeedSignalMpS = TrainMaxSpeedMpS;
                        LastPassedSignal[routeDirection] = -1;
                    }
                    // set signal limit as speed limit
                    else
                    {
                        AllowedMaxSpeedMpS = Math.Min(AllowedMaxSpeedSignalMpS, AllowedMaxSpeedMpS);
                    }
                }
                else if (signal.SignalHeads[0].SignalFunction == SignalFunction.Speed)
                {
                    SpeedInfo speedInfo = signal.SignalSpeed(SignalFunction.Speed);
                    if (speedInfo != null && speedInfo.Reset)
                    {
                        AllowedMaxSpeedSignalMpS = TrainMaxSpeedMpS;
                        if (simulator.TimetableMode)
                            AllowedMaxSpeedMpS = AllowedMaxSpeedLimitMpS;
                        else
                            AllowedMaxSpeedMpS = Math.Min(allowedMaxTempSpeedLimitMpS, AllowedMaxSpeedLimitMpS);
                    }
                }
            }

            // check forward along train for speedlimit and signal in direction of train - limit speed if passed
            // loop as there might be more than one

            routeElement = routeUnderTrain[0];
            foundSpeedLimit.Clear();
            float remLength = Length;
            Dictionary<int, float> remainingSignals = new Dictionary<int, float>();

            foundSpeedLimit = SignalEnvironment.ScanRoute(this, routeElement.TrackCircuitSection.Index, reverseOffset, routeElement.Direction,
                    true, remLength, false, true, false, false, false, true, false, true, false, IsFreight);

            bool limitAlongTrain = true;
            while (foundSpeedLimit.Count > 0 && limitAlongTrain)
            {
                Signal signal = Simulator.Instance.SignalEnvironment.Signals[Math.Abs(foundSpeedLimit[0])];

                // check if not beyond end of train
                float speedLimitDistance = TrackCircuitSection.GetDistanceBetweenObjects(routeElement.TrackCircuitSection.Index, reverseOffset, routeElement.Direction,
                    signal.TrackCircuitIndex, signal.TrackCircuitOffset);
                if (speedLimitDistance > Length)
                {
                    limitAlongTrain = false;
                }
                else
                {
                    int nextSectionIndex = signal.TrackCircuitIndex;
                    TrackDirection direction = signal.TrackCircuitDirection;
                    float objectOffset = signal.TrackCircuitOffset;

                    if (signal.IsSignal)
                    {
                        nextSectionIndex = signal.TrackCircuitNextIndex;
                        direction = signal.TrackCircuitNextDirection;
                        objectOffset = 0.0f;

                        if (PassedSignalSpeeds.ContainsKey(signal.Index))
                        {
                            AllowedMaxSpeedSignalMpS = PassedSignalSpeeds[signal.Index];
                            if (simulator.TimetableMode)
                                AllowedMaxSpeedMpS = Math.Min(AllowedMaxSpeedMpS, AllowedMaxSpeedSignalMpS);
                            else
                                AllowedMaxSpeedMpS = Math.Min(AllowedMaxSpeedLimitMpS, Math.Min(allowedMaxTempSpeedLimitMpS, AllowedMaxSpeedSignalMpS));

                            if (!remainingSignals.ContainsKey(signal.Index))
                                remainingSignals.Add(signal.Index, AllowedMaxSpeedSignalMpS);
                        }
                    }
                    else
                    {
                        SpeedInfo speedInfo = signal.SpeedLimit(SignalFunction.Speed);
                        float speedMpS = IsFreight ? speedInfo.FreightSpeed : speedInfo.PassengerSpeed;
                        if (speedMpS > 0)
                        {
                            if (speedInfo.LimitedSpeedReduction == 0) // standard speedpost
                            {
                                if (simulator.TimetableMode)
                                {
                                    AllowedMaxSpeedLimitMpS = Math.Min(AllowedMaxSpeedLimitMpS, speedMpS);
                                    AllowedMaxSpeedMpS = AllowedMaxSpeedLimitMpS;
                                }
                                else
                                {
                                    AllowedMaxSpeedLimitMpS = Math.Min(AllowedMaxSpeedLimitMpS, speedMpS);
                                    AllowedMaxSpeedMpS = Math.Min(AllowedMaxSpeedLimitMpS, Math.Min(allowedMaxTempSpeedLimitMpS,
                                       AllowedMaxSpeedSignalMpS == -1 ? 999 : AllowedMaxSpeedSignalMpS));
                                }
                            }
                            else
                            {
                                allowedMaxTempSpeedLimitMpS = Math.Min(allowedMaxTempSpeedLimitMpS, speedMpS);
                                AllowedMaxSpeedMpS = Math.Min(AllowedMaxSpeedLimitMpS, Math.Min(allowedMaxTempSpeedLimitMpS,
                                    AllowedMaxSpeedSignalMpS == -1 ? 999 : AllowedMaxSpeedSignalMpS));
                            }
                        }
                    }

                    remLength -= signal.TrackCircuitOffset - offsetStart;

                    foundSpeedLimit = SignalEnvironment.ScanRoute(this, nextSectionIndex, objectOffset, direction,
                        true, remLength, false, true, false, false, false, true, false, true, false, IsFreight);
                }
            }

            // set list of remaining signals as new pending list
            PassedSignalSpeeds.Clear();
            foreach (KeyValuePair<int, float> element in remainingSignals)
            {
                if (!PassedSignalSpeeds.ContainsKey(element.Key))
                    PassedSignalSpeeds.Add(element.Key, element.Value);
            }

            // check if signal passed posed a speed limit lower than present limit
            if (passedSignalIndex >= 0)
            {
                Signal passedSignal = Simulator.Instance.SignalEnvironment.Signals[passedSignalIndex];
                SpeedInfo speedInfo = passedSignal.SignalSpeed(SignalFunction.Normal);

                if (speedInfo != null)
                {
                    float speedMpS = IsFreight ? speedInfo.FreightSpeed : speedInfo.PassengerSpeed;
                    if (speedMpS > 0 && !PassedSignalSpeeds.ContainsKey(passedSignal.Index))
                    {
                        AllowedMaxSpeedSignalMpS = AllowedMaxSpeedSignalMpS > 0 ? Math.Min(AllowedMaxSpeedSignalMpS, speedMpS) : speedMpS;
                        AllowedMaxSpeedMpS = Math.Min(AllowedMaxSpeedMpS, AllowedMaxSpeedSignalMpS);

                        PassedSignalSpeeds.Add(passedSignal.Index, speedMpS);
                    }
                }
            }
        }

        /// <summary>
        /// Update section occupy states fore explorer mode
        /// Note : explorer mode has no distance actions so sections must be cleared immediately
        /// </summary>
        private void UpdateSectionStateExplorer()
        {
            // occupation is set in forward mode only
            // build route from rear to front - before reset occupy so correct switch alignment is used
            manualTrainRoute = SignalEnvironment.BuildTempRoute(this, PresentPosition[Direction.Backward].TrackCircuitSectionIndex, PresentPosition[Direction.Backward].Offset,
                            PresentPosition[Direction.Backward].Direction, Length, false, true, false);

            // save present occupation list
            List<TrackCircuitSection> clearedSections = new List<TrackCircuitSection>();
            for (int iindex = OccupiedTrack.Count - 1; iindex >= 0; iindex--)
            {
                clearedSections.Add(OccupiedTrack[iindex]);
            }

            // first check for misaligned switch
            Direction direction = MUDirection == MidpointDirection.Forward ? Direction.Forward : Direction.Backward;
            foreach (TrackCircuitRouteElement routeElement in manualTrainRoute)
            {
                TrackCircuitSection section = routeElement.TrackCircuitSection;

                // occupying misaligned switch : reset routes and position
                if (section.Index == misalignedSwitch[direction, 0])
                {
                    // align switch
                    if (!MPManager.NoAutoSwitch())
                        section.AlignSwitchPins(misalignedSwitch[direction, TrackDirection.Reverse]);
                    misalignedSwitch[direction, TrackDirection.Ahead] = -1;
                    misalignedSwitch[direction, TrackDirection.Reverse] = -1;

                    // recalculate track position
                    UpdateTrainPosition();

                    // rebuild this list
                    UpdateSectionStateExplorer();

                    // exit, as routine has called itself
                    return;
                }
            }

            // if all is well, set tracks to occupied
            OccupiedTrack.Clear();

            foreach (TrackCircuitRouteElement routeElement in manualTrainRoute)
            {
                TrackCircuitSection section = routeElement.TrackCircuitSection;

                if (clearedSections.Contains(section))
                {
                    section.ResetOccupied(this); // reset occupation if it was occupied
                    clearedSections.Remove(section);  // remove from cleared list
                }

                section.Reserve(RoutedForward, manualTrainRoute);  // reserve first to reset switch alignments
                section.SetOccupied(RoutedForward);
            }

            foreach (TrackCircuitSection clearedSection in clearedSections)
            {
                clearedSection.ClearOccupied(this, true); // sections really cleared
            }
        }

        /// <summary>
        /// Update Explorer Mode
        /// </summary>
        private void UpdateExplorerMode(int signalObjectIndex)
        {
            if (MPManager.IsMultiPlayer())
            // first unreserve all route positions where train is not present
            {
                if (ValidRoute[0] != null)
                {
                    foreach (TrackCircuitRouteElement routeElement in ValidRoute[0])
                    {
                        TrackCircuitSection section = routeElement.TrackCircuitSection;
                        if (section.CheckReserved(RoutedForward) && !section.CircuitState.OccupationState.ContainsTrain(this))
                        {
                            section.Unreserve();
                            section.UnreserveTrain();
                        }
                    }
                }
                if (ValidRoute[1] != null)
                {
                    foreach (TrackCircuitRouteElement routeElement in ValidRoute[1])
                    {
                        TrackCircuitSection section = routeElement.TrackCircuitSection;
                        if (section.CheckReserved(RoutedBackward) && !section.CircuitState.OccupationState.ContainsTrain(this))
                        {
                            section.Unreserve();
                            section.UnreserveTrain();
                        }
                    }
                }
            }

            // check present forward
            TrackCircuitPartialPathRoute newRouteF = CheckExplorerPath(Direction.Forward, PresentPosition[Direction.Forward], ValidRoute[0], true, ref EndAuthorityTypes[0],
                ref DistanceToEndNodeAuthorityM[0]);
            ValidRoute[0] = newRouteF;
            int routeIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0);
            PresentPosition[Direction.Forward].RouteListIndex = routeIndex;

            // check present reverse
            // reverse present rear position direction to build correct path backwards
            TrackCircuitPosition tempRear = new TrackCircuitPosition(PresentPosition[Direction.Backward], true);
            TrackCircuitPartialPathRoute newRouteR = CheckExplorerPath(Direction.Backward, tempRear, ValidRoute[1], true, ref EndAuthorityTypes[1], ref DistanceToEndNodeAuthorityM[1]);
            ValidRoute[1] = newRouteR;

            // select valid route
            if (MUDirection == MidpointDirection.Forward)
            {
                // use position from other end of section
                float reverseOffset = TrackCircuitSection.TrackCircuitList[PresentPosition[Direction.Backward].TrackCircuitSectionIndex].Length - PresentPosition[Direction.Backward].Offset;
                CheckSpeedLimitManual(ValidRoute[1], manualTrainRoute, reverseOffset, PresentPosition[Direction.Backward].Offset, signalObjectIndex, 0);
            }
            else
            {
                TrackCircuitPartialPathRoute tempRoute = new TrackCircuitPartialPathRoute(); // reversed trainRoute
                for (int i = manualTrainRoute.Count - 1; i >= 0; i--)
                {
                    TrackCircuitRouteElement routeElement = manualTrainRoute[i];
                    routeElement.Direction = routeElement.Direction.Reverse();
                    tempRoute.Add(routeElement);
                }
                float reverseOffset = TrackCircuitSection.TrackCircuitList[PresentPosition[Direction.Forward].TrackCircuitSectionIndex].Length - PresentPosition[Direction.Forward].Offset;
                CheckSpeedLimitManual(ValidRoute[0], tempRoute, PresentPosition[Direction.Forward].Offset, reverseOffset, signalObjectIndex, 1);
            }

            // reset signal permission

            if (signalObjectIndex >= 0)
            {
                Signal signal = Simulator.Instance.SignalEnvironment.Signals[signalObjectIndex];
                signal.OverridePermission = SignalPermission.Denied;

                signal.ResetSignalEnabled();
            }

            // get next signal

            // forward
            NextSignalObject[0] = null;
            for (int i = 0; i < ValidRoute[0].Count && NextSignalObject[0] == null; i++)
            {
                TrackCircuitRouteElement routeElement = ValidRoute[0][i];
                TrackCircuitSection section = routeElement.TrackCircuitSection;
                NextSignalObject[0] = section.EndSignals[routeElement.Direction];
            }

            // backward
            NextSignalObject[1] = null;
            for (int i = 0; i < ValidRoute[1].Count && NextSignalObject[1] == null; i++)
            {
                TrackCircuitRouteElement routeElement = ValidRoute[1][i];
                TrackCircuitSection section = routeElement.TrackCircuitSection;
                NextSignalObject[1] = section.EndSignals[routeElement.Direction];
            }

            // clear all build up distance actions
            requiredActions.RemovePendingAIActionItems(true);
        }

        /// <summary>
        /// Check Explorer Path
        /// <\summary>
        private TrackCircuitPartialPathRoute CheckExplorerPath(Direction direction, TrackCircuitPosition requiredPosition, TrackCircuitPartialPathRoute requiredRoute, bool forward,
            ref EndAuthorityType endAuthority, ref float endAuthorityDistanceM)
        {
            TrainRouted trainRouted = direction == Direction.Forward ? RoutedForward : RoutedBackward;

            // create new route or set to existing route
            TrackCircuitPartialPathRoute newRoute = requiredRoute ?? new TrackCircuitPartialPathRoute();

            // check if train on valid position in route
            int routeIndex = newRoute.GetRouteIndex(requiredPosition.TrackCircuitSectionIndex, 0);
            TrackCircuitRouteElement routeElement;
            if (routeIndex < 0)    // no valid point in route
            {
                if (requiredRoute != null && requiredRoute.Count > 0)  // if route defined, then breakdown route
                {
                    Simulator.Instance.SignalEnvironment.BreakDownRouteList(requiredRoute, 0, trainRouted);
                    requiredRoute.Clear();
                }

                // build new route
                List<int> tempSections = SignalEnvironment.ScanRoute(this, requiredPosition.TrackCircuitSectionIndex, requiredPosition.Offset,
                    requiredPosition.Direction, forward, MinCheckDistanceExplorerM, true, false, true, false, true, false, false, false, false, IsFreight);

                if (tempSections.Count > 0)
                {
                    // create subpath route
                    int prevSection = -2;    // preset to invalid

                    foreach (int sectionIndex in tempSections)
                    {
                        routeElement = new TrackCircuitRouteElement(TrackCircuitSection.TrackCircuitList[Math.Abs(sectionIndex)],
                                sectionIndex > 0 ? TrackDirection.Ahead : TrackDirection.Reverse, prevSection);
                        newRoute.Add(routeElement);
                        prevSection = Math.Abs(sectionIndex);
                    }
                }
            }
            // remove any sections before present position - train has passed over these sections
            else if (routeIndex > 0)
            {
                for (int i = routeIndex - 1; i >= 0; i--)
                {
                    newRoute.RemoveAt(i);
                }
            }

            // check if route ends at signal, determine length

            float totalLengthM = 0;
            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[requiredPosition.TrackCircuitSectionIndex];
            float offsetM = direction == Direction.Forward ? requiredPosition.Offset : section.Length - requiredPosition.Offset;
            bool endWithSignal = false;    // ends with signal at STOP
            bool hasEndSignal = false;     // ends with cleared signal
            int sectionWithSignalIndex = 0;

            TrackDirection reqDirection;
            for (int i = 0; i < newRoute.Count && !endWithSignal; i++)
            {
                routeElement = newRoute[i];

                section = routeElement.TrackCircuitSection;
                totalLengthM += (section.Length - offsetM);
                offsetM = 0.0f; // reset offset for further sections

                // check on state of signals
                // also check if signal properly enabled

                reqDirection = routeElement.Direction;
                if (section.EndSignals[reqDirection] != null)
                {
                    Signal endSignal = section.EndSignals[reqDirection];
                    SignalAspectState aspect = section.EndSignals[reqDirection].SignalLR(SignalFunction.Normal);
                    hasEndSignal = true;

                    if (aspect == SignalAspectState.Stop && endSignal.OverridePermission != SignalPermission.Granted)
                    {
                        endWithSignal = true;
                        sectionWithSignalIndex = i;
                    }
                    else if (endSignal.EnabledTrain == null)   // signal cleared by default only - request for proper clearing
                    {
                        endSignal.RequestClearSignalExplorer(newRoute, trainRouted, true, 0);  // do NOT propagate
                        TrackCircuitPartialPathRoute extendedRoute = endSignal.RequestClearSignalExplorer(newRoute, trainRouted, true, 0);  // do NOT propagate
                        if (i + 1 == newRoute.Count)
                            newRoute = extendedRoute;
                    }
                }
            }

            // check if signal is in last section
            // if not, probably moved forward beyond a signal, so remove all beyond first signal
            if (endWithSignal && sectionWithSignalIndex < newRoute.Count - 1)
            {
                for (int i = newRoute.Count - 1; i >= sectionWithSignalIndex + 1; i--)
                {
                    section = newRoute[i].TrackCircuitSection;
                    section.RemoveTrain(this, true);
                    newRoute.RemoveAt(i);
                }
            }

            // if route does not end with signal and is too short, extend
            if (!endWithSignal && totalLengthM < MinCheckDistanceExplorerM)
            {

                float extendedDistanceM = MinCheckDistanceExplorerM - totalLengthM;
                TrackCircuitRouteElement lastElement = newRoute[newRoute.Count - 1];

                int lastSectionIndex = lastElement.TrackCircuitSection.Index;
                TrackCircuitSection lastSection = TrackCircuitSection.TrackCircuitList[lastSectionIndex];

                int nextSectionIndex = lastSection.Pins[lastElement.OutPin[Location.NearEnd], (Location)lastElement.OutPin[Location.FarEnd]].Link;
                TrackDirection nextSectionDirection = lastSection.Pins[lastElement.OutPin[Location.NearEnd], (Location)lastElement.OutPin[Location.FarEnd]].Direction;

                // check if last item is non-aligned switch

                misalignedSwitch[direction, TrackDirection.Ahead] = -1;
                misalignedSwitch[direction, TrackDirection.Reverse] = -1;

                TrackCircuitSection nextSection = nextSectionIndex >= 0 ? TrackCircuitSection.TrackCircuitList[nextSectionIndex] : null;
                if (nextSection != null && nextSection.CircuitType == TrackCircuitType.Junction)
                {
                    if (nextSection.Pins[0, 0].Link != lastSectionIndex && nextSection.Pins[TrackDirection.Ahead, Location.FarEnd].Link != lastSectionIndex && nextSection.Pins[TrackDirection.Reverse, (Location)nextSection.JunctionLastRoute].Link != lastSectionIndex)
                    {
                        misalignedSwitch[direction, TrackDirection.Ahead] = nextSection.Index;
                        misalignedSwitch[direction, TrackDirection.Reverse] = lastSectionIndex;
                    }
                }

                List<int> tempSections = null;

                if (nextSectionIndex >= 0 && misalignedSwitch[direction, TrackDirection.Ahead] < 0)
                {
                    bool reqAutoAlign = hasEndSignal; // auto-align switches if route is extended from signal

                    tempSections = SignalEnvironment.ScanRoute(this, nextSectionIndex, 0, nextSectionDirection, forward, extendedDistanceM, true, reqAutoAlign, true, false, true, false, false, false, false, IsFreight);
                }

                if (tempSections?.Count > 0)
                {
                    // add new sections

                    int prevSection = lastElement.TrackCircuitSection.Index;

                    foreach (int sectionIndex in tempSections)
                    {
                        routeElement = new TrackCircuitRouteElement(TrackCircuitSection.TrackCircuitList[Math.Abs(sectionIndex)], sectionIndex > 0 ? TrackDirection.Ahead : TrackDirection.Reverse, prevSection);
                        newRoute.Add(routeElement);
                        prevSection = Math.Abs(sectionIndex);
                    }
                }
            }
            // check for any uncleared signals in route - if first found, request clear signal
            bool unclearedSignal = false;
            int signalIndex = newRoute.Count - 1;
            int nextUnclearSignalIndex = -1;

            for (int i = 0; i <= newRoute.Count - 1 && !unclearedSignal; i++)
            {
                routeElement = newRoute[i];
                section = routeElement.TrackCircuitSection;

                Signal nextSignal = section.EndSignals[routeElement.Direction];
                if (nextSignal?.SignalLR(SignalFunction.Normal) == SignalAspectState.Stop && nextSignal.OverridePermission != SignalPermission.Granted)
                {
                    unclearedSignal = true;
                    signalIndex = i;
                    nextUnclearSignalIndex = nextSignal.Index;
                }
            }

            // route created to signal or max length, now check availability - but only up to first unclear signal
            // check if other train in first section
            if (newRoute.Count > 0)
            {
                routeElement = newRoute[0];
                section = routeElement.TrackCircuitSection;
                reqDirection = forward ? routeElement.Direction : routeElement.Direction.Reverse();
                offsetM = direction == 0 ? requiredPosition.Offset : section.Length - requiredPosition.Offset;

                Dictionary<Train, float> firstTrainInfo = section.TestTrainAhead(this, offsetM, reqDirection);
                if (firstTrainInfo.Count > 0)
                {
                    foreach (KeyValuePair<Train, float> item in firstTrainInfo)  // there is only one value
                    {
                        endAuthority = EndAuthorityType.TrainAhead;
                        endAuthorityDistanceM = item.Value;
                        if (!section.CircuitState.OccupiedByThisTrain(this)) section.PreReserve(trainRouted);
                    }
                }
                // check route availability
                // reserve sections which are available
                else
                {
                    int lastValidSectionIndex = 0;
                    totalLengthM = 0;

                    for (int i = 0; i <= signalIndex; i++)
                    {
                        section = newRoute[i].TrackCircuitSection;

                        if (section.IsAvailable(this))
                        {
                            lastValidSectionIndex = i;
                            totalLengthM += (section.Length - offsetM);
                            offsetM = 0;
                            section.Reserve(trainRouted, newRoute);
                        }
                        else
                        {
                            break;
                        }
                    }

                    // if last section ends with signal, set authority to signal
                    routeElement = newRoute[lastValidSectionIndex];
                    section = routeElement.TrackCircuitSection;
                    reqDirection = forward ? routeElement.Direction : routeElement.Direction.Reverse();
                    // last section ends with signal
                    if (section.EndSignals[reqDirection] != null)
                    {
                        endAuthority = EndAuthorityType.Signal;
                        endAuthorityDistanceM = totalLengthM;
                    }
                    // sections not clear - check if end has signal
                    else
                    {
                        TrackCircuitSection nextSection = null;
                        TrackCircuitRouteElement nextElement = null;

                        if (lastValidSectionIndex < newRoute.Count - 1)
                        {
                            nextElement = newRoute[lastValidSectionIndex + 1];
                            nextSection = nextElement.TrackCircuitSection;
                        }

                        // check for end authority if not ended with signal
                        // last section is end of track
                        if (section.CircuitType == TrackCircuitType.EndOfTrack)
                        {
                            endAuthority = EndAuthorityType.EndOfTrack;
                            endAuthorityDistanceM = totalLengthM;
                        }
                        // first non-available section is switch or crossover
                        else if (nextSection != null && (nextSection.CircuitType == TrackCircuitType.Junction || nextSection.CircuitType == TrackCircuitType.Crossover))
                        {
                            endAuthority = EndAuthorityType.ReservedSwitch;
                            endAuthorityDistanceM = totalLengthM;
                        }
                        // set authority is end of path unless train ahead
                        else
                        {
                            endAuthority = EndAuthorityType.EndOfPath;
                            endAuthorityDistanceM = totalLengthM;

                            // check if train ahead not moving in opposite direction, in first non-available section

                            if (nextSection != null)
                            {
                                TrackDirection oppositeDirection = forward ? nextElement.Direction.Reverse() : nextElement.Direction;
                                reqDirection = forward ? nextElement.Direction : nextElement.Direction.Reverse();

                                bool oppositeTrain = nextSection.CircuitState.Occupied((int)oppositeDirection, false);

                                if (!oppositeTrain)
                                {
                                    Dictionary<Train, float> nextTrainInfo = nextSection.TestTrainAhead(this, 0.0f, reqDirection);
                                    if (nextTrainInfo.Count > 0)
                                    {
                                        foreach (KeyValuePair<Train, float> thisTrainAhead in nextTrainInfo)  // there is only one value
                                        {
                                            endAuthority = EndAuthorityType.TrainAhead;
                                            endAuthorityDistanceM = thisTrainAhead.Value + totalLengthM;
                                            lastValidSectionIndex++;
                                            nextSection.PreReserve(trainRouted);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // remove invalid sections from route
                    if (lastValidSectionIndex < newRoute.Count - 1)
                    {
                        Simulator.Instance.SignalEnvironment.BreakDownRouteList(newRoute, lastValidSectionIndex + 1, trainRouted);
                        newRoute.RemoveRange(lastValidSectionIndex + 1, newRoute.Count - lastValidSectionIndex - 1);
                    }
                }

                // check if route ends at signal and this is first unclear signal
                // if so, request clear signal

                if (endAuthority == EndAuthorityType.Signal)
                {
                    // The logic here is to keep the value of SNCA of first signal found in path, and reduce this value as cleared signals are passed.
                    // When an uncleared signal is found, it is requested to clear if SNCA of first signal is not satisfied.
                    // If we are far away from the first signal, there is no point in clearing it until we get closer.
                    if (unclearedSignal && signalIndex < newRoute.Count)
                    {
                        Signal reqSignal = Simulator.Instance.SignalEnvironment.Signals[nextUnclearSignalIndex];
                        bool firstSignalPassed = false;
                        int numCleared = 0;
                        totalLengthM = 0;
                        offsetM = direction == Direction.Forward ? requiredPosition.Offset : section.Length - requiredPosition.Offset;
                        for (int i = 0; i < newRoute.Count; i++)
                        {
                            section = TrackCircuitSection.TrackCircuitList[newRoute[i].TrackCircuitSection.Index];
                            TrackDirection currentDirection = newRoute[i].Direction;

                            if (!section.IsAvailable(this))
                                break;

                            totalLengthM += section.Length - offsetM;
                            offsetM = 0;

                            // Stop if first signal is far, there's no need to clear it.
                            if (!firstSignalPassed && totalLengthM > MinCheckDistanceExplorerM)
                                break;

                            if (section.EndSignals[currentDirection] != null)
                            {
                                Signal signal = section.EndSignals[currentDirection];
                                if (!firstSignalPassed)
                                {
                                    firstSignalPassed = true;
                                    if (signal == reqSignal)
                                    {
                                        Simulator.Instance.SignalEnvironment.BreakDownRouteList(newRoute, i + 1, trainRouted);
                                        newRoute.RemoveRange(i + 1, newRoute.Count - i - 1);
                                        newRoute = signal.RequestClearSignalExplorer(newRoute, trainRouted, false, 0);
                                        break;
                                    }
                                    numCleared = signal.GetRequestNumberClearAheadExplorer(false, 0);
                                }
                                else
                                {
                                    if (signal == reqSignal)
                                    {
                                        Simulator.Instance.SignalEnvironment.BreakDownRouteList(newRoute, i + 1, trainRouted);
                                        newRoute.RemoveRange(i + 1, newRoute.Count - i - 1);
                                        newRoute = signal.RequestClearSignalExplorer(newRoute, trainRouted, true, numCleared);
                                        break;
                                    }
                                    numCleared = signal.GetRequestNumberClearAheadExplorer(true, numCleared);
                                }
                                // Stop if no more signals to clear
                                if (numCleared == 0)
                                    break;
                            }
                        }
                    }
                }
            }
            // no valid route could be found
            else
            {
                endAuthority = EndAuthorityType.NoPathReserved;
                endAuthorityDistanceM = 0.0f;
            }

            return newRoute;
        }

        /// <summary>
        /// Restore Explorer Mode
        /// </summary>
        internal void RestoreExplorerMode()
        {
            // get next signal

            // forward
            NextSignalObject[0] = null;
            for (int i = 0; i < ValidRoute[0].Count && NextSignalObject[0] == null; i++)
            {
                TrackCircuitRouteElement routeElement = ValidRoute[0][i];
                NextSignalObject[0] = routeElement.TrackCircuitSection.EndSignals[routeElement.Direction];
            }

            // backward
            NextSignalObject[1] = null;
            for (int i = 0; i < ValidRoute[1].Count && NextSignalObject[1] == null; i++)
            {
                TrackCircuitRouteElement routeElement = ValidRoute[1][i];
                NextSignalObject[1] = routeElement.TrackCircuitSection.EndSignals[routeElement.Direction];
            }
        }

        // Request signal permission in explorer mode
        private void RequestExplorerSignalPermission(TrackCircuitPartialPathRoute selectedRoute, Direction routeDirection)
        {
            _ = selectedRoute;
            // check route for first signal at danger, from present position

            Signal reqSignal = null;
            bool signalFound = false;

            if (ValidRoute[(int)routeDirection] != null)
            {
                for (int i = PresentPosition[routeDirection].RouteListIndex; i <= ValidRoute[(int)routeDirection].Count - 1 && !signalFound; i++)
                {
                    TrackCircuitSection section = ValidRoute[(int)routeDirection][i].TrackCircuitSection;
                    TrackDirection direction = ValidRoute[(int)routeDirection][i].Direction;

                    if (section.EndSignals[direction] != null)
                    {
                        reqSignal = section.EndSignals[direction];
                        signalFound = reqSignal.SignalLR(SignalFunction.Normal) == SignalAspectState.Stop;
                    }
                }
            }

            // if no signal at danger is found - report warning
            if (!signalFound)
            {
                if (TrainType != TrainType.Remote) // As Confirmer may not be created until after a restore.
                    simulator.Confirmer?.Message(ConfirmLevel.Information, Simulator.Catalog.GetString("No signal in train's path"));
                return;
            }

            // signal at danger is found - set PERMISSION REQUESTED, and request clear signal
            // if signal has a route, set PERMISSION REQUESTED, and perform signal update
            reqSignal.OverridePermission = SignalPermission.Requested;

            TrackCircuitPosition tempPos = new TrackCircuitPosition(PresentPosition[Direction.Backward], routeDirection != Direction.Forward);
            TrackCircuitPartialPathRoute newRouteR = CheckExplorerPath(routeDirection, tempPos, ValidRoute[(int)routeDirection], true, ref EndAuthorityTypes[(int)routeDirection],
                ref DistanceToEndNodeAuthorityM[(int)routeDirection]);
            ValidRoute[(int)routeDirection] = newRouteR;
            simulator.SoundNotify = reqSignal.OverridePermission == SignalPermission.Granted ? TrainEvent.PermissionGranted : TrainEvent.PermissionDenied;
        }

        /// <summary>
        /// Process request to set switch in explorer mode
        /// Request may contain direction or actual node
        /// </summary>
        internal bool ProcessRequestExplorerSetSwitch(Direction direction)
        {
            // find first switch in required direction

            TrackCircuitSection reqSwitch = null;
            int routeDirectionIndex = (int)direction;
            bool switchSet = false;

            for (int i = 0; i < ValidRoute[routeDirectionIndex].Count; i++)
            {
                TrackCircuitSection section = ValidRoute[routeDirectionIndex][i].TrackCircuitSection;
                if (section.CircuitType == TrackCircuitType.Junction)
                {
                    reqSwitch = section;
                    break;
                }
            }

            if (reqSwitch == null)
            {
                // search beyond last section for switch using default pins (continue through normal sections only)

                TrackCircuitRouteElement routeElement = ValidRoute[routeDirectionIndex][ValidRoute[routeDirectionIndex].Count - 1];
                TrackCircuitSection lastSection = routeElement.TrackCircuitSection;
                TrackDirection curDirection = routeElement.Direction;
                int nextSectionIndex = routeElement.TrackCircuitSection.Index;

                bool validRoute = lastSection.CircuitType == TrackCircuitType.Normal;

                while (reqSwitch == null && validRoute)
                {
                    if (lastSection.CircuitType == TrackCircuitType.Crossover)
                    {
                        TrackDirection outPinIndex = curDirection.Reverse();
                        if (lastSection.Pins[curDirection, Location.NearEnd].Link == nextSectionIndex)
                        {
                            nextSectionIndex = lastSection.Pins[outPinIndex, Location.NearEnd].Link;
                            curDirection = lastSection.Pins[outPinIndex, Location.NearEnd].Direction;
                        }
                        else if (lastSection.Pins[curDirection, Location.FarEnd].Link == nextSectionIndex)
                        {
                            nextSectionIndex = lastSection.Pins[outPinIndex, Location.FarEnd].Link;
                            curDirection = lastSection.Pins[outPinIndex, Location.FarEnd].Direction;
                        }
                    }
                    else
                    {
                        nextSectionIndex = lastSection.Pins[curDirection, Location.NearEnd].Link;
                        curDirection = lastSection.ActivePins[curDirection, Location.NearEnd].Direction;
                        lastSection = TrackCircuitSection.TrackCircuitList[nextSectionIndex];
                    }

                    if (lastSection.CircuitType == TrackCircuitType.Junction)
                    {
                        reqSwitch = lastSection;
                    }
                    else if (lastSection.CircuitType != TrackCircuitType.Normal)
                    {
                        validRoute = false;
                    }
                }
            }

            if (reqSwitch != null)
            {
                // check if switch is clear
                if (!reqSwitch.CircuitState.Occupied() && reqSwitch.CircuitState.TrainReserved == null && reqSwitch.CircuitState.SignalReserved < 0)
                {
                    reqSwitch.JunctionSetManual = reqSwitch.JunctionLastRoute == 0 ? 1 : 0;
                    Simulator.Instance.SignalEnvironment.SetSwitch(reqSwitch.OriginalIndex, reqSwitch.JunctionSetManual, reqSwitch);
                    switchSet = true;
                }
                // check if switch reserved by this train - if so, dealign
                else if (reqSwitch.CircuitState.TrainReserved != null && reqSwitch.CircuitState.TrainReserved.Train == this)
                {
                    reqSwitch.DeAlignSwitchPins();
                    reqSwitch.JunctionSetManual = reqSwitch.JunctionLastRoute == 0 ? 1 : 0;
                    Simulator.Instance.SignalEnvironment.SetSwitch(reqSwitch.OriginalIndex, reqSwitch.JunctionSetManual, reqSwitch);
                    switchSet = true;
                }

                if (switchSet)
                    ProcessExplorerSwitch(routeDirectionIndex, reqSwitch, direction);
            }
            else
            {
                simulator.Confirmer?.Message(ConfirmLevel.Warning, Simulator.Catalog.GetString("No switch found"));
            }

            return switchSet;
        }

        internal void ProcessRequestExplorerSetSwitch(int reqSwitchIndex)
        {
            // find switch in route - forward first

            int routeDirectionIndex = -1;
            bool switchFound = false;
            Direction direction = Direction.Forward;

            for (int i = 0; i < ValidRoute[0].Count - 1 && !switchFound; i++)
            {
                if (ValidRoute[0][i].TrackCircuitSection.Index == reqSwitchIndex)
                {
                    routeDirectionIndex = 0;
                    direction = Direction.Forward;
                    switchFound = true;
                }
            }

            if (ValidRoute[1] != null)
            {
                for (int i = 0; i < ValidRoute[1].Count - 1 && !switchFound; i++)
                {
                    if (ValidRoute[1][i].TrackCircuitSection.Index == reqSwitchIndex)
                    {
                        routeDirectionIndex = 1;
                        direction = Direction.Backward;
                        switchFound = true;
                    }
                }
            }

            if (switchFound)
            {
                TrackCircuitSection reqSwitch = TrackCircuitSection.TrackCircuitList[reqSwitchIndex];
                ProcessExplorerSwitch(routeDirectionIndex, reqSwitch, direction);
            }
        }

        /// <summary>
        /// Process switching of explorer switch
        /// </summary>
        private void ProcessExplorerSwitch(int routeDirectionIndex, TrackCircuitSection switchSection, Direction direction)
        {
            TrainRouted trainRouted = direction == Direction.Backward ? RoutedBackward : RoutedForward;
            TrackCircuitPartialPathRoute selectedRoute = ValidRoute[routeDirectionIndex];

            // store required position
            int reqSwitchPosition = switchSection.JunctionSetManual;

            // find index of section in present route
            int junctionIndex = selectedRoute.GetRouteIndex(switchSection.Index, 0);
            int lastIndex = junctionIndex - 1; // set previous index as last valid index

            // find first signal from train and before junction
            Signal firstSignal = null;
            float coveredLength = 0;

            for (int i = 0; i < junctionIndex && firstSignal == null; i++)
            {
                TrackCircuitRouteElement routeElement = selectedRoute[i];
                TrackCircuitSection section = routeElement.TrackCircuitSection;
                if (i > 0) coveredLength += section.Length; // do not use first section

                TrackDirection signalDirection = routeElement.Direction;

                if (section.EndSignals[signalDirection]?.EnabledTrain?.Train == this)
                {
                    firstSignal = section.EndSignals[signalDirection];
                    lastIndex = i;
                }
            }

            // if last first is found : reset signal and further signals, clear route as from signal and request clear signal
            if (firstSignal != null)
            {
                firstSignal.ResetSignal(true);

                // breakdown and clear route

                // checke whether trailing or leading
                //<CSComment> Probably also in singleplayer the logic of multiplayer should be used, but it's unwise to modify it just before a release
                if (switchSection.Pins[TrackDirection.Ahead, Location.NearEnd].Link == selectedRoute[lastIndex].TrackCircuitSection.Index || !MPManager.IsMultiPlayer())
                // leading, train may still own switch
                {

                    Simulator.Instance.SignalEnvironment.BreakDownRouteList(selectedRoute, lastIndex + 1, trainRouted);
                    selectedRoute.RemoveRange(lastIndex + 1, selectedRoute.Count - lastIndex - 1);

                    // restore required position (is cleared by route breakdown)
                    switchSection.JunctionSetManual = reqSwitchPosition;

                    // set switch
                    switchSection.DeAlignSwitchPins();
                    Simulator.Instance.SignalEnvironment.SetSwitch(switchSection.OriginalIndex, switchSection.JunctionSetManual, switchSection);

                    // build new route - use signal request
                    selectedRoute = firstSignal.RequestClearSignalExplorer(selectedRoute, trainRouted, false, 0);
                    ValidRoute[routeDirectionIndex] = selectedRoute;
                }
                else
                {
                    // trailing, train must not own switch any more
                    Simulator.Instance.SignalEnvironment.BreakDownRouteList(selectedRoute, junctionIndex, trainRouted);
                    selectedRoute.RemoveRange(junctionIndex, selectedRoute.Count - junctionIndex);

                    // restore required position (is cleared by route breakdown)
                    switchSection.JunctionSetManual = reqSwitchPosition;

                    // set switch
                    switchSection.DeAlignSwitchPins();
                    Simulator.Instance.SignalEnvironment.SetSwitch(switchSection.OriginalIndex, switchSection.JunctionSetManual, switchSection);
                }
            }
            // no signal is found - build route using full update process
            else
            {
                Simulator.Instance.SignalEnvironment.BreakDownRouteList(selectedRoute, 0, trainRouted);
                selectedRoute.Clear();
                manualTrainRoute = SignalEnvironment.BuildTempRoute(this, PresentPosition[Direction.Backward].TrackCircuitSectionIndex, PresentPosition[Direction.Backward].Offset,
                    PresentPosition[Direction.Backward].Direction, Length, false, true, false);
                UpdateExplorerMode(-1);
            }
        }

        //
        // Switch to explorer mode
        //
        private void ToggleToExplorerMode()
        {
            if (ControlMode == TrainControlMode.OutOfControl && LeadLocomotive != null)
                ((MSTSLocomotive)LeadLocomotive).SetEmergency(false);

            // set track occupation (using present route)
            UpdateSectionStateExplorer();

            // breakdown present route - both directions if set

            if (ValidRoute[0] != null)
            {
                int listIndex = PresentPosition[Direction.Forward].RouteListIndex;
                Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[0], listIndex, RoutedForward);
                ClearDeadlocks();
            }

            ValidRoute[0] = null;
            LastReservedSection[0] = -1;

            if (ValidRoute[1] != null)
            {
                int listIndex = PresentPosition[Direction.Backward].RouteListIndex;
                Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[1], listIndex, RoutedBackward);
            }
            ValidRoute[1] = null;
            LastReservedSection[1] = -1;

            // clear all outstanding actions
            ClearActiveSectionItems();
            requiredActions.RemovePendingAIActionItems(true);

            // clear signal info
            NextSignalObject[0] = null;
            NextSignalObject[1] = null;

            SignalObjectItems.Clear();

            PassedSignalSpeeds.Clear();

            // set explorer mode
            ControlMode = TrainControlMode.Explorer;

            // reset routes and check sections either end of train
            PresentPosition[Direction.Forward].RouteListIndex = -1;
            PresentPosition[Direction.Backward].RouteListIndex = -1;
            PreviousPosition[Direction.Forward].RouteListIndex = -1;

            UpdateExplorerMode(-1);
        }

        /// <summary>
        /// Update out-of-control mode
        /// </summary>
        private void UpdateOutOfControl()
        {
            _ = ControlMode;
            // train is at a stand : 
            // clear all occupied blocks
            // clear signal/speedpost list 
            // clear DistanceTravelledActions 
            // clear all previous occupied sections 
            // set sections occupied on which train stands

            // all the above is still TODO
        }

        /// <summary>
        /// Switch to Auto Signal mode
        /// </summary>
        internal virtual void SwitchToSignalControl(Signal signal)
        {
            // in auto mode, use forward direction only

            ControlMode = TrainControlMode.AutoSignal;
            signal.RequestClearSignal(ValidRoute[0], RoutedForward, 0, false, null);

            // enable any none-NORMAL signals between front of train and first NORMAL signal
            int firstSectionIndex = PresentPosition[Direction.Forward].RouteListIndex;
            int lastSectionIndex = ValidRoute[0].GetRouteIndex(signal.TrackCircuitIndex, firstSectionIndex);

            // first, all signals in present section beyond position of train
            TrackCircuitSection section = ValidRoute[0][firstSectionIndex].TrackCircuitSection;
            TrackDirection direction = ValidRoute[0][firstSectionIndex].Direction;

            for (int i = 0; i < Simulator.Instance.SignalEnvironment.OrtsSignalTypeCount; i++)
            {
                TrackCircuitSignalList signalList = section.CircuitItems.TrackCircuitSignals[direction][i];
                foreach (TrackCircuitSignalItem signalItem in signalList)
                {
                    if (signalItem.SignalLocation > PresentPosition[Direction.Forward].Offset && !signalItem.Signal.SignalNormal())
                    {
                        signalItem.Signal.EnabledTrain = RoutedForward;
                    }
                }
            }

            // next, signals in any further sections
            for (int sectionIndex = firstSectionIndex + 1; sectionIndex <= lastSectionIndex; sectionIndex++)
            {
                section = ValidRoute[0][firstSectionIndex].TrackCircuitSection;
                direction = ValidRoute[0][firstSectionIndex].Direction;

                for (int i = 0; i < Simulator.Instance.SignalEnvironment.OrtsSignalTypeCount; i++)
                {
                    TrackCircuitSignalList signalList = section.CircuitItems.TrackCircuitSignals[direction][i];
                    foreach (TrackCircuitSignalItem signalItem in signalList)
                    {
                        if (!signalItem.Signal.SignalNormal())
                        {
                            signalItem.Signal.EnabledTrain = RoutedForward;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Switch to Auto Node mode
        /// </summary>
        internal virtual void SwitchToNodeControl(int sectionIndex)
        {
            // reset enabled signal if required
            if (ControlMode == TrainControlMode.AutoSignal && NextSignalObject[0]?.EnabledTrain == RoutedForward)
            {
                // reset any claims
                foreach (TrackCircuitRouteElement thisElement in NextSignalObject[0].SignalRoute)
                {
                    TrackCircuitSection thisSection = thisElement.TrackCircuitSection;
                    thisSection.CircuitState.TrainClaimed.Remove(RoutedForward);
                }

                // reset signal
                NextSignalObject[0].EnabledTrain = null;
                NextSignalObject[0].ResetSignal(true);
            }

            // use direction forward only
            int activeSectionIndex = sectionIndex;
            ControlMode = TrainControlMode.AutoNode;
            EndAuthorityTypes[0] = EndAuthorityType.NoPathReserved;
            nextSignalIndex = -1; // no next signal in Node Control

            // if section is set, check if it is on route and ahead of train
            if (activeSectionIndex > 0)
            {
                int endListIndex = ValidRoute[0].GetRouteIndex(sectionIndex, PresentPosition[Direction.Forward].RouteListIndex);

                // section is not on route - give warning and break down route, following active links and resetting reservation
                if (endListIndex < 0)
                {
                    Simulator.Instance.SignalEnvironment.BreakDownRoute(sectionIndex, RoutedForward);
                    activeSectionIndex = -1;
                }

                // if section is (still) set, check if this is at maximum distance
                if (activeSectionIndex > 0)
                {
                    TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[activeSectionIndex];
                    float clearedDistanceM = GetDistanceToTrain(activeSectionIndex, section.Length);
                    if (clearedDistanceM > MaxDistanceCheckedAhead)
                    {
                        EndAuthorityTypes[0] = EndAuthorityType.MaxDistance;
                        LastReservedSection[0] = section.Index;
                        DistanceToEndNodeAuthorityM[0] = clearedDistanceM;
                    }
                }
                else
                {
                    EndAuthorityTypes[0] = EndAuthorityType.NoPathReserved;
                }
            }

            // new request or not beyond max distance
            if (activeSectionIndex < 0 || EndAuthorityTypes[0] != EndAuthorityType.MaxDistance)
            {
                Simulator.Instance.SignalEnvironment.RequestClearNode(RoutedForward, ValidRoute[0]);
            }
        }

        /// <summary>
        /// Request to switch to or from manual mode
        /// </summary>
        public void RequestToggleManualMode()
        {
            if (TrainType == TrainType.AiPlayerHosting)
            {
                simulator.Confirmer?.Message(ConfirmLevel.Warning, Simulator.Catalog.GetString("You cannot enter manual mode when autopiloted"));
            }
            else if (IsPathless && ControlMode != TrainControlMode.OutOfControl && ControlMode == TrainControlMode.Manual)
            {
                simulator.Confirmer?.Message(ConfirmLevel.Warning, Simulator.Catalog.GetString("You cannot use this command for pathless trains"));
            }
            else if (ControlMode == TrainControlMode.Manual)
            {
                // check if train is back on path

                TrackCircuitPartialPathRoute lastRoute = TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath];
                int routeIndex = lastRoute.GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0);

                if (routeIndex < 0)
                {
                    simulator.Confirmer?.Message(ConfirmLevel.Warning, Simulator.Catalog.GetString("Train is not back on original route"));
                }
                else
                {
                    TrackDirection lastDirection = lastRoute[routeIndex].Direction;
                    TrackDirection presentDirection = PresentPosition[Direction.Forward].Direction;
                    if (lastDirection != presentDirection && Math.Abs(SpeedMpS) > 0.1f)
                    {
                        simulator.Confirmer?.Message(ConfirmLevel.Warning, Simulator.Catalog.GetString("Original route is reverse from present direction, stop train before switching"));
                    }
                    else
                    {
                        ToggleFromManualMode(routeIndex);
                        simulator.Confirmer.Confirm(CabControl.SignalMode, CabSetting.On);
                    }
                }
            }
            else if (ControlMode == TrainControlMode.Explorer)
            {
                if (LeadLocomotive is MSTSLocomotive locomotive && (locomotive.TrainBrakeController.TCSEmergencyBraking || locomotive.TrainBrakeController.TCSFullServiceBraking))
                {
                    locomotive.SetEmergency(false);
                    ResetExplorerMode();
                    return;
                }
                else
                    simulator.Confirmer?.Message(ConfirmLevel.Warning, Simulator.Catalog.GetString("Cannot change to Manual Mode while in Explorer Mode"));
            }
            else
            {
                ToggleToManualMode();
                simulator.Confirmer?.Confirm(CabControl.SignalMode, CabSetting.Off);
            }
        }

        /// <summary>
        /// Switch to manual mode 
        /// </summary>
        private void ToggleToManualMode()
        {
            if (LeadLocomotive != null)
                ((MSTSLocomotive)LeadLocomotive).SetEmergency(false);

            // set track occupation (using present route)
            UpdateSectionStateManual();

            // breakdown present route - both directions if set

            if (ValidRoute[0] != null)
            {
                int listIndex = PresentPosition[Direction.Forward].RouteListIndex;
                Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[0], listIndex, RoutedForward);
                ClearDeadlocks();
            }

            ValidRoute[0] = null;
            LastReservedSection[0] = -1;

            if (ValidRoute[1] != null)
            {
                int listIndex = PresentPosition[Direction.Backward].RouteListIndex;
                Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[1], listIndex, RoutedBackward);
            }
            ValidRoute[1] = null;
            LastReservedSection[1] = -1;

            // clear all outstanding actions

            ClearActiveSectionItems();
            requiredActions.RemovePendingAIActionItems(true);

            // clear signal info

            NextSignalObject[0] = null;
            NextSignalObject[1] = null;

            SignalObjectItems.Clear();

            PassedSignalSpeeds.Clear();

            // set manual mode

            ControlMode = TrainControlMode.Manual;

            // reset routes and check sections either end of train

            PresentPosition[Direction.Forward].RouteListIndex = -1;
            PresentPosition[Direction.Backward].RouteListIndex = -1;
            PreviousPosition[Direction.Forward].RouteListIndex = -1;

            UpdateManualMode(-1);
        }

        /// <summary>
        /// Switch back from manual mode 
        /// </summary>
        /// <param name="routeIndex"></param>
        private void ToggleFromManualMode(int routeIndex)
        {
            // extract route at present front position

            TrackCircuitPartialPathRoute newRoute = new TrackCircuitPartialPathRoute();
            TrackCircuitPartialPathRoute oldRoute = TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath];

            // test on reversal, if so check rear of train

            (bool valid, bool reversal) = CheckReversal(oldRoute);
            if (!valid)
            {
                simulator.Confirmer?.Message(ConfirmLevel.Warning, Simulator.Catalog.GetString("Reversal required and rear of train not on required route"));
                return;
            }

            // breakdown present routes, forward and backward
            Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[0], 0, RoutedForward);
            Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[1], 0, RoutedBackward);

            // clear occupied sections
            for (int i = OccupiedTrack.Count - 1; i >= 0; i--)
            {
                OccupiedTrack[i].ResetOccupied(this);
            }

            // remove any actions build up during manual mode
            requiredActions.RemovePendingAIActionItems(true);

            // restore train placement
            RestoreTrainPlacement(newRoute, oldRoute, routeIndex, reversal);

            // restore distance travelled in Present Position
            PresentPosition[Direction.Forward].DistanceTravelled = DistanceTravelledM;
            PresentPosition[Direction.Backward].DistanceTravelled = DistanceTravelledM - Length;

            // set track occupation (using present route)
            // This procedure is also needed for clearing track occupation.
            UpdateSectionStateManual();

            // restore signal information
            PassedSignalSpeeds.Clear();
            InitializeSignals(true);

            // restore deadlock information

            CheckDeadlock(ValidRoute[0], Number);    // Check deadlock against all other trains

            // switch to AutoNode mode

            LastReservedSection[0] = PresentPosition[Direction.Forward].TrackCircuitSectionIndex;
            LastReservedSection[1] = PresentPosition[Direction.Backward].TrackCircuitSectionIndex;

            if (!simulator.TimetableMode)
                AuxActionsContainer.ResetAuxAction(this);
            SwitchToNodeControl(PresentPosition[Direction.Forward].TrackCircuitSectionIndex);
            TCRoute.SetReversalOffset(Length, simulator.TimetableMode);
        }

        /// <summary>
        /// Reset Explorer Mode
        /// </summary>
        private void ResetExplorerMode()
        {
            if (ControlMode == TrainControlMode.OutOfControl && LeadLocomotive != null)
                ((MSTSLocomotive)LeadLocomotive).SetEmergency(false);

            // set track occupation (using present route)
            UpdateSectionStateExplorer();

            // breakdown present route - both directions if set
            if (ValidRoute[0] != null)
            {
                int listIndex = PresentPosition[Direction.Forward].RouteListIndex;
                Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[0], listIndex, RoutedForward);
                ClearDeadlocks();
            }

            ValidRoute[0] = null;
            LastReservedSection[0] = -1;

            if (ValidRoute[1] != null)
            {
                int listIndex = PresentPosition[Direction.Backward].RouteListIndex;
                Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[1], listIndex, RoutedBackward);
            }
            ValidRoute[1] = null;
            LastReservedSection[1] = -1;

            // clear all outstanding actions
            ClearActiveSectionItems();
            requiredActions.RemovePendingAIActionItems(true);

            // clear signal info

            NextSignalObject[0] = null;
            NextSignalObject[1] = null;

            SignalObjectItems.Clear();

            PassedSignalSpeeds.Clear();

            // set explorer mode
            ControlMode = TrainControlMode.Explorer;

            // reset routes and check sections either end of train
            PresentPosition[Direction.Forward].RouteListIndex = -1;
            PresentPosition[Direction.Backward].RouteListIndex = -1;
            PreviousPosition[Direction.Forward].RouteListIndex = -1;

            UpdateExplorerMode(-1);
        }

        /// <summary>
        /// Check if reversal is required
        /// </summary>
        private (bool valid, bool reversal) CheckReversal(TrackCircuitPartialPathRoute requiredRoute)
        {
            bool valid = true;
            bool reversal = false;

            int presentRouteIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0);
            int reqRouteIndex = requiredRoute.GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0);
            if (presentRouteIndex < 0 || reqRouteIndex < 0)
            {
                valid = false;  // front of train not on present route or not on required route
            }
            // valid point : check if reversal is required
            else
            {
                TrackCircuitRouteElement presentElement = ValidRoute[0][presentRouteIndex];
                TrackCircuitRouteElement pathElement = requiredRoute[reqRouteIndex];

                if (presentElement.Direction != pathElement.Direction)
                {
                    reversal = true;
                }
            }

            // if reversal required : check if rear of train is on required route
            if (valid && reversal)
            {
                int rearRouteIndex = requiredRoute.GetRouteIndex(PresentPosition[Direction.Backward].TrackCircuitSectionIndex, 0);
                valid = rearRouteIndex >= 0;
            }

            return (valid, reversal);
        }

        /// <summary>
        /// Restore train placement
        /// </summary>
        private void RestoreTrainPlacement(TrackCircuitPartialPathRoute newRoute, TrackCircuitPartialPathRoute oldRoute, int frontIndex, bool reversal)
        {
            // reverse train if required
            if (reversal)
            {
                ReverseFormation(true);
                // active subpath must be incremented in parallel in incorporated train if present
                if (IncorporatedTrainNo >= 0)
                    IncrementSubpath(simulator.TrainDictionary[IncorporatedTrainNo]);
            }

            // reset distance travelled
            DistanceTravelledM = 0.0f;

            // check if end of train on original route
            // copy sections from earliest start point (front or rear)

            int rearIndex = oldRoute.GetRouteIndex(PresentPosition[Direction.Backward].TrackCircuitSectionIndex, 0);
            int startIndex = rearIndex >= 0 ? Math.Min(rearIndex, frontIndex) : frontIndex;

            for (int i = startIndex; i < oldRoute.Count; i++)
            {
                newRoute.Add(oldRoute[i]);
            }

            // if rear not on route, build route under train and add sections
            if (rearIndex < 0)
            {

                TrackCircuitPartialPathRoute tempRoute = SignalEnvironment.BuildTempRoute(this, PresentPosition[Direction.Backward].TrackCircuitSectionIndex, PresentPosition[Direction.Backward].Offset,
                            PresentPosition[Direction.Backward].Direction, Length, true, true, false);

                for (int i = tempRoute.Count - 1; i >= 0; i--)
                {
                    TrackCircuitRouteElement routeElement = tempRoute[i];
                    if (!newRoute.ContainsSection(routeElement))
                    {
                        newRoute.Insert(0, routeElement);
                    }
                }
            }

            // set route as valid route
            ValidRoute[0] = newRoute;

            // Reindexes ReversalInfo items
            if (TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastDivergeIndex >= 0)
                TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastDivergeIndex = ValidRoute[0].GetRouteIndex(TCRoute.ReversalInfo[TCRoute.ActiveSubPath].DivergeSectorIndex, 0);
            if (TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastSignalIndex >= 0)
                TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastSignalIndex = ValidRoute[0].GetRouteIndex(TCRoute.ReversalInfo[TCRoute.ActiveSubPath].SignalSectorIndex, 0);



            // get index of first section in route
            rearIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Backward].TrackCircuitSectionIndex, 0);
            PresentPosition[Direction.Backward].RouteListIndex = rearIndex;

            // get index of front of train
            frontIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0);
            PresentPosition[Direction.Forward].RouteListIndex = frontIndex;

            PreviousPosition[Direction.Forward].UpdateFrom(PresentPosition[Direction.Forward]);

            // set track occupied - forward only
            foreach (TrackCircuitSection section in OccupiedTrack)
            {
                if (!section.CircuitState.OccupiedByThisTrain(this))
                {
                    section.Reserve(RoutedForward, ValidRoute[0]);
                    section.SetOccupied(RoutedForward);
                }
            }
        }


        //
        // Request permission to pass signal
        //
        public void RequestSignalPermission(Direction direction)
        {
            if (MPManager.IsClient())
            {
                MPManager.Notify((new MSGResetSignal(MPManager.GetUserName())).ToString());
                return;
            }
            if (ControlMode == TrainControlMode.Manual)
            {
                RequestManualSignalPermission(ValidRoute[(int)direction], (int)direction);
            }
            else if (ControlMode == TrainControlMode.Explorer)
            {
                RequestExplorerSignalPermission(ValidRoute[(int)Direction.Forward], direction);
            }
            else
            {
                if (direction != Direction.Forward)
                {
                    simulator.Confirmer?.Message(ConfirmLevel.Warning, Simulator.Catalog.GetString("Cannot clear signal behind train while in AUTO mode"));
                    simulator.SoundNotify = TrainEvent.PermissionDenied;
                }
                else if (NextSignalObject[0] != null)
                {
                    NextSignalObject[0].OverridePermission = SignalPermission.Requested;
                }
            }
        }

        //
        // Request reset signal
        //
        public void RequestResetSignal(Direction direction)
        {
            if (!MPManager.IsMultiPlayer())
            {
                if (ControlMode == TrainControlMode.Manual || ControlMode == TrainControlMode.Explorer)
                {
                    if (NextSignalObject[(int)direction]?.SignalLR(SignalFunction.Normal) != SignalAspectState.Stop)
                    {
                        int routeIndex = ValidRoute[(int)direction].GetRouteIndex(NextSignalObject[(int)direction].TrackCircuitNextIndex, PresentPosition[direction].RouteListIndex);
                        Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[(int)direction], routeIndex, RoutedForward);
                        ValidRoute[(int)direction].RemoveRange(routeIndex, ValidRoute[(int)direction].Count - routeIndex);

                        NextSignalObject[(int)direction].ResetSignal(true);
                    }
                }
            }
        }

        /// <summary>
        /// Get distance from train to object position using route list
        /// </summary>
        private float GetObjectDistanceToTrain(SignalItemInfo signalInfo)
        {
            // follow active links to get to object
            int reqSectionIndex = signalInfo.SignalDetails.TrackCircuitIndex;
            float endOffset = signalInfo.SignalDetails.TrackCircuitOffset;

            float distanceM = GetDistanceToTrain(reqSectionIndex, endOffset);

            //          if (distanceM < 0)
            //          {
            //              distanceM = thisObject.ObjectDetails.DistanceTo(FrontTDBTraveller);
            //          }

            return distanceM;
        }

        /// <summary>
        /// Get distance from train to location using route list
        /// TODO : rewrite to use active links, and if fails use traveller
        /// location must have same direction as train
        /// </summary>

        internal float GetDistanceToTrain(int sectionIndex, float endOffset)
        {
            // use start of list to see if passed position

            int endListIndex = ValidRoute[0].GetRouteIndex(sectionIndex, PresentPosition[Direction.Forward].RouteListIndex);
            if (endListIndex < 0)
                endListIndex = ValidRoute[0].GetRouteIndex(sectionIndex, 0);

            if (endListIndex >= 0 && endListIndex < PresentPosition[Direction.Forward].RouteListIndex) // index before present so we must have passed object
            {
                return -1.0f;
            }

            if (endListIndex == PresentPosition[Direction.Forward].RouteListIndex && endOffset < PresentPosition[Direction.Forward].Offset) // just passed
            {
                return -1.0f;
            }

            // section is not on route

            if (endListIndex < 0)
            {
                return -1.0f;
            }

            int presentSectionIndex = PresentPosition[Direction.Forward].TrackCircuitSectionIndex;
            TrackDirection direction = PresentPosition[Direction.Forward].Direction;
            float startOffset = PresentPosition[Direction.Forward].Offset;

            return TrackCircuitSection.GetDistanceBetweenObjects(presentSectionIndex, startOffset, direction, sectionIndex, endOffset);
        }

        /// Switch train to Out-of-Control
        /// Set mode and apply emergency brake
        internal void SetTrainOutOfControl(OutOfControlReason reason)
        {

            if (ControlMode == TrainControlMode.OutOfControl) // allready out of control, so exit
            {
                return;
            }

            // clear all reserved sections etc. - both directions
            if (ControlMode == TrainControlMode.AutoSignal)
            {
                if (NextSignalObject[0]?.EnabledTrain == RoutedForward)
                {
                    int routeIndexBeforeSignal = NextSignalObject[0].TrainRouteIndex - 1;
                    NextSignalObject[0].ResetSignal(true);
                    if (routeIndexBeforeSignal >= 0)
                        Simulator.Instance.SignalEnvironment.BreakDownRoute(ValidRoute[0][routeIndexBeforeSignal].TrackCircuitSection.Index, RoutedForward);
                }
                if (NextSignalObject[1]?.EnabledTrain == RoutedBackward)
                {
                    NextSignalObject[1].ResetSignal(true);
                }
            }
            else if (ControlMode == TrainControlMode.AutoNode)
            {
                Simulator.Instance.SignalEnvironment.BreakDownRoute(LastReservedSection[0], RoutedForward);
            }

            // TODO : clear routes for MANUAL
            if (!MPManager.IsMultiPlayer() || simulator.TimetableMode || reason != OutOfControlReason.OutOfPath || IsActualPlayerTrain)
            {

                // set control state and issue warning

                if (ControlMode != TrainControlMode.Explorer)
                    ControlMode = TrainControlMode.OutOfControl;

                OutOfControlReason = reason;

                StringBuilder report = new StringBuilder($"Train {Number} is out of control and will be stopped. Reason: ");
                switch (reason)
                {
                    case (OutOfControlReason.PassedAtDanger):
                        report.Append("train passed signal at Danger");
                        break;
                    case (OutOfControlReason.RearPassedAtDanger):
                        report.Append("train passed signal at Danger at rear of train");
                        break;
                    case (OutOfControlReason.OutOfAuthority):
                        report.Append("train passed limit of authority");
                        break;
                    case (OutOfControlReason.OutOfPath):
                        report.Append("train has ran off its allocated path");
                        break;
                    case (OutOfControlReason.SlippedIntoPath):
                        report.Append("train slipped back into path of another train");
                        break;
                    case (OutOfControlReason.SlippedToEndOfTrack):
                        report.Append("train slipped of the end of track");
                        break;
                    case (OutOfControlReason.OutOfTrack):
                        report.Append("train has moved off the track");
                        break;
                }
                simulator.Confirmer?.Message(ConfirmLevel.Warning, report.ToString());// As Confirmer may not be created until after a restore.

                if (LeadLocomotive != null)
                    ((MSTSLocomotive)LeadLocomotive).SetEmergency(true);
            }
            // the AI train is now out of path. Instead of killing him, we give him a chance on a new path
            else
            {
                GenerateValidRoute(PresentPosition[Direction.Forward].RouteListIndex, PresentPosition[Direction.Forward].TrackCircuitSectionIndex);
                // switch to NODE mode
                if (ControlMode == TrainControlMode.AutoSignal)
                {
                    SwitchToNodeControl(PresentPosition[Direction.Forward].TrackCircuitSectionIndex);
                }
                // reset actions to recalculate distances
                if (TrainType == TrainType.Ai || TrainType == TrainType.AiPlayerHosting)
                    ((AITrain)this).ResetActions(true);
            }
        }

        /// <summary>
        /// Re-routes a train in auto mode after a switch moved manually
        /// </summary>
        internal void ReRouteTrain(int forcedRouteSectionIndex, int forcedTCSectionIndex)
        {
            // check for any stations in abandoned path
            if (ControlMode == TrainControlMode.AutoSignal || ControlMode == TrainControlMode.AutoNode)
            // Local trains, having a defined TCRoute
            {
                int actSubpath = TCRoute.ActiveSubPath;
                Dictionary<int, StationStop> abdStations = new Dictionary<int, StationStop>();

                CheckAbandonedStations(forcedRouteSectionIndex, ValidRoute[0].Count - 1, actSubpath, abdStations);
                ResetValidRoute();
                GenerateValidRoute(forcedRouteSectionIndex, forcedTCSectionIndex);
                // check for abandoned stations - try to find alternative on passing path
                LookForReplacementStations(abdStations, ValidRoute[0], ValidRoute[0]);
            }
        }

        /// Resets ValidRoute after some event like a switch moved
        private void ResetValidRoute()
        {
            // clear all reserved sections etc. - both directions
            if (ControlMode == TrainControlMode.AutoSignal)
            {
                if (NextSignalObject[0]?.EnabledTrain == RoutedForward)
                {
                    int routeIndexBeforeSignal = NextSignalObject[0].TrainRouteIndex - 1;
                    NextSignalObject[0].ResetSignal(true);
                    if (routeIndexBeforeSignal >= 0)
                        Simulator.Instance.SignalEnvironment.BreakDownRoute(ValidRoute[0][routeIndexBeforeSignal].TrackCircuitSection.Index, RoutedForward);
                }
                if (NextSignalObject[1]?.EnabledTrain == RoutedBackward)
                {
                    NextSignalObject[1].ResetSignal(true);
                }
            }
            else if (ControlMode == TrainControlMode.AutoNode)
            {
                Simulator.Instance.SignalEnvironment.BreakDownRoute(LastReservedSection[0], RoutedForward);
            }
        }


        /// <summary>
        /// Generates a new ValidRoute after some event like a switch moved
        /// </summary>
        private void GenerateValidRoute(int forcedRouteSectionIndex, int forcedTCSectionIndex)
        {
            // We don't kill the AI train and build a new route for it
            // first of all we have to find out the new route
            if (TCRoute.OriginalSubpath == -1)
                TCRoute.OriginalSubpath = TCRoute.ActiveSubPath;
            List<int> tempSections;
            if (PresentPosition[Direction.Forward].RouteListIndex > 0)
            {
                // clean case, train is in route and switch has been forced in front of it
                tempSections = SignalEnvironment.ScanRoute(this, forcedTCSectionIndex, 0, ValidRoute[0][forcedRouteSectionIndex].Direction,
                        true, 0, true, true,
                        false, false, true, false, false, false, false, IsFreight, false, true);
            }
            else
            {
                // dirty case, train is out of route and has already passed forced switch
                tempSections = SignalEnvironment.ScanRoute(this, PresentPosition[Direction.Forward].TrackCircuitSectionIndex, PresentPosition[Direction.Forward].Offset,
                    PresentPosition[Direction.Forward].Direction, true, 0, true, true, false, false, true, false, false, false, false, IsFreight, false, true);
            }
            TrackCircuitPartialPathRoute newRoute = new TrackCircuitPartialPathRoute();
            // Copy part of route already run
            if (PresentPosition[Direction.Forward].RouteListIndex > 0)
            {
                for (int routeListIndex = 0; routeListIndex < forcedRouteSectionIndex; routeListIndex++)
                    newRoute.Add(ValidRoute[0][routeListIndex]);
            }
            else if (PresentPosition[Direction.Forward].RouteListIndex < 0)
            {
                for (int routeListIndex = 0; routeListIndex <= PreviousPosition[Direction.Forward].RouteListIndex + 1; routeListIndex++)
                    newRoute.Add(ValidRoute[0][routeListIndex]); // maybe + 1 is wrong?
            }
            if (tempSections.Count > 0)
            {
                int prevSection = -2;    // preset to invalid
                int tempSectionsIndex = 0;
                foreach (int sectionIndex in tempSections)
                {
                    TrackDirection sectionDirection = sectionIndex > 0 ? TrackDirection.Ahead : TrackDirection.Reverse;
                    // Add new part of route
                    TrackCircuitRouteElement routeElement = new TrackCircuitRouteElement(TrackCircuitSection.TrackCircuitList[Math.Abs(sectionIndex)], sectionDirection, prevSection);
                    // if junction, you have to adjust the OutPin
                    TrackCircuitSection.TrackCircuitList[Math.Abs(sectionIndex)].CircuitState.Forced = false;
                    if (TrackCircuitSection.TrackCircuitList[Math.Abs(sectionIndex)].CircuitType == TrackCircuitType.Junction && routeElement.FacingPoint == true)
                    {
                        TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[Math.Abs(sectionIndex)];
                        if (tempSectionsIndex < tempSections.Count - 1 && section.Pins[sectionDirection, Location.FarEnd].Link == tempSections[tempSectionsIndex + 1])
                            routeElement.OutPin[Location.FarEnd] = TrackDirection.Reverse;
                        else routeElement.OutPin[Location.FarEnd] = TrackDirection.Ahead;
                    }
                    newRoute.Add(routeElement);
                    prevSection = Math.Abs(sectionIndex);
                    tempSectionsIndex++;
                }

                // Check if we are returning to original route
                int lastAlternativeSectionIndex = TCRoute.TCRouteSubpaths[TCRoute.OriginalSubpath].GetRouteIndex(newRoute[newRoute.Count - 1].TrackCircuitSection.Index, 0);
                if (lastAlternativeSectionIndex != -1)
                {
                    // continued path
                    TrackCircuitPartialPathRoute route = TCRoute.TCRouteSubpaths[TCRoute.OriginalSubpath];
                    for (int i = lastAlternativeSectionIndex + 1; i < route.Count; i++)
                    {
                        newRoute.Add(route[i]);
                    }

                    if (TCRoute.ActiveSubPath != TCRoute.OriginalSubpath)
                    {
                        TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath] = null;
                        TCRoute.ReversalInfo[TCRoute.ActiveSubPath] = null;
                        TCRoute.LoopEnd.RemoveAt(TCRoute.ActiveSubPath);
                    }
                    TCRoute.ActiveSubPath = TCRoute.OriginalSubpath;
                    TCRoute.OriginalSubpath = -1;

                    // readjust item indexes
                    // Reindexes ReversalInfo items
                    int countDifference = newRoute.Count - ValidRoute[0].Count;
                    if (TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastDivergeIndex >= 0)
                        TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastDivergeIndex = TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastDivergeIndex + countDifference;
                    if (TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastSignalIndex >= 0)
                        TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastSignalIndex = TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastSignalIndex + countDifference;

                    TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath] = newRoute;

                }
                else
                {
                    // put at the end of the subpath list the new route
                    TCRoute.TCRouteSubpaths.Add(newRoute);

                    // TODO add reversalInfo here.
                    TCRoute.ActiveSubPath = TCRoute.TCRouteSubpaths.Count - 1;

                    TCRoute.ReversalInfo.Add(new TrackCircuitReversalInfo());
                    TCRoute.ReversalInfo[TCRoute.ReversalInfo.Count - 1].ReversalIndex = newRoute.Count - 1;
                    TCRoute.ReversalInfo[TCRoute.ReversalInfo.Count - 1].ReversalSectionIndex = newRoute[newRoute.Count - 1].TrackCircuitSection.Index;
                    TrackCircuitSection endSection = newRoute[newRoute.Count - 1].TrackCircuitSection;
                    TCRoute.ReversalInfo[TCRoute.ReversalInfo.Count - 1].ReverseReversalOffset = endSection.Length;
                    TCRoute.LoopEnd.Add(-1);
                }
            }
            // then we pass this route to ValidRoute[0]
            ValidRoute[0] = newRoute;
            // we set the routelistindex of the present position in case it was = -1
            if (PresentPosition[Direction.Forward].RouteListIndex == -1)
                PresentPosition[Direction.Forward].RouteListIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, PreviousPosition[Direction.Forward].RouteListIndex);

            // reset signal information

            SignalObjectItems.Clear();
            NextSignalObject[0] = null;
            // create new list
            InitializeSignals(true);
            LastReservedSection[0] = PresentPosition[Direction.Forward].TrackCircuitSectionIndex;
            CheckDeadlock(ValidRoute[0], Number);    // Check deadlock against all other trains
        }

        /// Perform actions linked to distance travelled
        private protected virtual void PerformActions(List<DistanceTravelledItem> actions)
        {
            foreach (DistanceTravelledItem action in actions)
            {
                switch (action)
                {
                    case ClearSectionItem clearSection:
                        ClearOccupiedSection(clearSection);
                        break;
                    case ActivateSpeedLimit activateSpeedLimit:
                        SetPendingSpeedLimit(activateSpeedLimit);
                        break;
                    case AuxActionItem auxAction:
                        int presentTime = (int)simulator.ClockTime;
                        auxAction.ProcessAction(this, presentTime);
                        break;
                }
            }
        }

        /// Clear section
        private protected void ClearOccupiedSection(ClearSectionItem sectionInfo)
        {
            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[sectionInfo.TrackSectionIndex];
            section.ClearOccupied(this, true);
        }

        /// <summary>
        /// Set pending speed limits
        /// </summary>
        internal void SetPendingSpeedLimit(ActivateSpeedLimit speedInfo)
        {
            float prevMaxSpeedMpS = AllowedMaxSpeedMpS;

            if (speedInfo.MaxSpeedMpSSignal > 0)
            {
                AllowedMaxSpeedSignalMpS = simulator.TimetableMode ? speedInfo.MaxSpeedMpSSignal : allowedAbsoluteMaxSpeedSignalMpS;
                AllowedMaxSpeedMpS = Math.Min(speedInfo.MaxSpeedMpSSignal, Math.Min(AllowedMaxSpeedLimitMpS, allowedMaxTempSpeedLimitMpS));
            }
            if (speedInfo.MaxSpeedMpSLimit > 0)
            {
                AllowedMaxSpeedLimitMpS = simulator.TimetableMode ? speedInfo.MaxSpeedMpSLimit : allowedAbsoluteMaxSpeedLimitMpS;
                if (simulator.TimetableMode)
                    AllowedMaxSpeedMpS = speedInfo.MaxSpeedMpSLimit;
                else
                    AllowedMaxSpeedMpS = Math.Min(speedInfo.MaxSpeedMpSLimit, Math.Min(AllowedMaxSpeedSignalMpS, allowedMaxTempSpeedLimitMpS));
            }
            if (speedInfo.MaxTempSpeedMpSLimit > 0 && !simulator.TimetableMode)
            {
                allowedMaxTempSpeedLimitMpS = allowedAbsoluteMaxTempSpeedLimitMpS;
                AllowedMaxSpeedMpS = Math.Min(speedInfo.MaxTempSpeedMpSLimit, Math.Min(AllowedMaxSpeedSignalMpS, AllowedMaxSpeedLimitMpS));
            }
            if (IsActualPlayerTrain && AllowedMaxSpeedMpS > prevMaxSpeedMpS)
            {
                simulator.OnAllowedSpeedRaised(this);
            }
        }

        /// <summary>
        /// Clear all active items on occupied track
        /// <\summary>
        internal void ClearActiveSectionItems()
        {
            List<DistanceTravelledItem> activeActions = requiredActions.GetActions(99999999f, typeof(ClearSectionItem));
            foreach (DistanceTravelledItem action in activeActions)
            {
                if (action is ClearSectionItem)
                {
                    ClearSectionItem sectionInfo = action as ClearSectionItem;
                    int thisSectionIndex = sectionInfo.TrackSectionIndex;
                    TrackCircuitSection thisSection = TrackCircuitSection.TrackCircuitList[thisSectionIndex];

                    if (!OccupiedTrack.Contains(thisSection))
                    {
                        thisSection.ClearOccupied(this, true);
                    }
                }
            }
        }

        /// <summary>
        /// Forced stop due to problems with other train
        /// <\summary>
        private void ForcedStop(string reason, string otherTrainName, int otherTrainNumber)
        {
            Trace.TraceInformation($"Train {Name} ({Number}) stopped for train {otherTrainName} ({otherTrainNumber}): {reason}");

            if (simulator.PlayerLocomotive != null && simulator.PlayerLocomotive.Train == this)
            {
                string report = Simulator.Catalog.GetString("Train stopped due to problems with other train: train {0} , reason: {1}", otherTrainNumber, reason);
                simulator.Confirmer?.Message(ConfirmLevel.Warning, report);
            }

            if (LeadLocomotive != null)
                ((MSTSLocomotive)LeadLocomotive).SetEmergency(true);
        }

        /// <summary>
        /// Remove train
        /// <\summary>
        internal virtual void RemoveTrain()
        {
            RemoveFromTrack();
            ClearDeadlocks();
            simulator.Trains.Remove(this);
        }

        //
        // Remove train from not-occupied sections only (for reset after uncoupling)
        //
        private protected void RemoveFromTrackNotOccupied(TrackCircuitPartialPathRoute newSections)
        {
            // clear occupied track

            List<int> clearedSectionIndices = new List<int>();
            TrackCircuitSection[] tempSectionArray = new TrackCircuitSection[OccupiedTrack.Count]; // copy sections as list is cleared by ClearOccupied method
            OccupiedTrack.CopyTo(tempSectionArray);

            for (int i = 0; i < tempSectionArray.Length; i++)
            {
                TrackCircuitSection section = tempSectionArray[i];
                int newRouteIndex = newSections.GetRouteIndex(section.Index, 0);
                if (newRouteIndex < 0)
                {
                    section.ClearOccupied(this, true);
                    clearedSectionIndices.Add(section.Index);
                }
            }

            // clear outstanding clear sections for sections no longer occupied

            foreach (DistanceTravelledItem action in requiredActions)
            {
                if (action is ClearSectionItem)
                {
                    ClearSectionItem clearSectionItem = action as ClearSectionItem;
                    if (clearedSectionIndices.Contains(clearSectionItem.TrackSectionIndex))
                    {
                        TrackCircuitSection.TrackCircuitList[clearSectionItem.TrackSectionIndex].ClearOccupied(this, true);
                    }
                }
            }
        }

        //
        // Remove train (after coupling or when train disappeared in multiplayer)
        //
        internal void RemoveFromTrack()
        {
            // check if no reserved sections remain

            int presentIndex = PresentPosition[Direction.Backward].RouteListIndex;

            if (presentIndex >= 0)
            {
                for (int i = presentIndex; i < ValidRoute[0].Count; i++)
                {
                    TrackCircuitRouteElement thisElement = ValidRoute[0][i];
                    TrackCircuitSection thisSection = thisElement.TrackCircuitSection;
                    thisSection.RemoveTrain(this, true);
                }
            }

            // for explorer (e.g. in Multiplayer) and manual mode check also backward route
            if (ValidRoute[1] != null && ValidRoute[1].Count > 0)
            {
                for (int i = 0; i < ValidRoute[1].Count; i++)
                {
                    ValidRoute[1][i].TrackCircuitSection.RemoveTrain(this, true);
                }
            }

            // clear occupied track
            TrackCircuitSection[] tempSectionArray = new TrackCircuitSection[OccupiedTrack.Count]; // copy sections as list is cleared by ClearOccupied method
            OccupiedTrack.CopyTo(tempSectionArray);

            foreach (TrackCircuitSection section in tempSectionArray)
            {
                section.ClearOccupied(this, true);
            }

            // clear last reserved section
            LastReservedSection[0] = -1;
            LastReservedSection[1] = -1;

            // clear outstanding clear sections

            foreach (DistanceTravelledItem actionItem in requiredActions)
            {
                if (actionItem is ClearSectionItem clearSectionItem)
                {
                    TrackCircuitSection.TrackCircuitList[clearSectionItem.TrackSectionIndex].ClearOccupied(this, true);
                }
            }
        }

        //
        // Update track actions after coupling
        //
        internal void UpdateTrackActionsCoupling(bool coupleToFront)
        {

            // remove train from track - clear all reservations etc.
            RemoveFromTrack();
            ClearDeadlocks();

            // check if new train is freight or not
            CheckFreight();

            // clear all track occupation actions
            List<DistanceTravelledItem> activeActions = requiredActions.GetActions(99999999f, typeof(ClearSectionItem));
            activeActions.Clear();

            // save existing TCPositions

            TrackCircuitPosition oldPresentPosition = PresentPosition[Direction.Forward];
            TrackCircuitPosition oldRearPosition = PresentPosition[Direction.Backward];

            PresentPosition[Direction.Forward] = new TrackCircuitPosition();
            PresentPosition[Direction.Backward] = new TrackCircuitPosition();

            // create new TCPositions

            TrackNode tn = FrontTDBTraveller.TN;
            float offset = FrontTDBTraveller.TrackNodeOffset;
            TrackDirection direction = (TrackDirection)FrontTDBTraveller.Direction;

            PresentPosition[Direction.Forward].SetPosition(tn.TrackCircuitCrossReferences, offset, direction);
            PreviousPosition[Direction.Forward].UpdateFrom(PresentPosition[Direction.Forward]);

            tn = RearTDBTraveller.TN;
            offset = RearTDBTraveller.TrackNodeOffset;
            direction = (TrackDirection)RearTDBTraveller.Direction;

            PresentPosition[Direction.Backward].SetPosition(tn.TrackCircuitCrossReferences, offset, direction);

            PresentPosition[Direction.Forward].DistanceTravelled = DistanceTravelledM;
            PresentPosition[Direction.Backward].DistanceTravelled = oldRearPosition.DistanceTravelled;

            // use difference in position to update existing DistanceTravelled

            float deltaoffset;
            if (coupleToFront)
            {
                float offset_old = oldPresentPosition.Offset;
                float offset_new = PresentPosition[Direction.Forward].Offset;

                if (oldPresentPosition.TrackCircuitSectionIndex == PresentPosition[Direction.Forward].TrackCircuitSectionIndex)
                {
                    deltaoffset = offset_new - offset_old;
                }
                else
                {
                    TrackCircuitSection thisSection = TrackCircuitSection.TrackCircuitList[oldPresentPosition.TrackCircuitSectionIndex];
                    deltaoffset = thisSection.Length - offset_old;
                    deltaoffset += offset_new;

                    for (int iIndex = oldPresentPosition.RouteListIndex + 1; iIndex < PresentPosition[Direction.Forward].RouteListIndex; iIndex++)
                    {
                        thisSection = ValidRoute[0][iIndex].TrackCircuitSection;
                        deltaoffset += thisSection.Length;
                    }
                }
                PresentPosition[Direction.Forward].DistanceTravelled += deltaoffset;
                DistanceTravelledM += deltaoffset;
            }
            else
            {
                float offset_old = oldRearPosition.Offset;
                float offset_new = PresentPosition[Direction.Backward].Offset;

                if (oldRearPosition.TrackCircuitSectionIndex == PresentPosition[Direction.Backward].TrackCircuitSectionIndex)
                {
                    deltaoffset = offset_old - offset_new;
                }
                else
                {
                    deltaoffset = offset_old;
                    TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[PresentPosition[Direction.Backward].TrackCircuitSectionIndex];
                    deltaoffset += (section.Length - offset_new);

                    for (int i = oldRearPosition.RouteListIndex - 1; i > PresentPosition[Direction.Backward].RouteListIndex; i--)
                    {
                        section = ValidRoute[0][i].TrackCircuitSection;
                        deltaoffset += section.Length;
                    }
                }
                PresentPosition[Direction.Backward].DistanceTravelled -= deltaoffset;
            }

            // Set track sections to occupied - forward direction only
            OccupiedTrack.Clear();
            UpdateOccupancies();

            // add sections to required actions list
            foreach (TrackCircuitSection section in OccupiedTrack)
            {
                float distanceToClear = DistanceTravelledM + section.Length + StandardOverlapM;
                if (section.CircuitType == TrackCircuitType.Junction ||
                    section.CircuitType == TrackCircuitType.Crossover)
                {
                    distanceToClear += Length + JunctionOverlapM;
                }

                if (PresentPosition[Direction.Forward].TrackCircuitSectionIndex == section.Index)
                {
                    distanceToClear += Length - PresentPosition[Direction.Forward].Offset;
                }
                else if (PresentPosition[Direction.Backward].TrackCircuitSectionIndex == section.Index)
                {
                    distanceToClear -= PresentPosition[Direction.Backward].Offset;
                }
                else
                {
                    distanceToClear += Length;
                }
                requiredActions.InsertAction(new ClearSectionItem(distanceToClear, section.Index));
            }

            // rebuild list of station stops

            if (StationStops.Count > 0)
            {
                int presentStop = StationStops[0].PlatformReference;
                StationStops.Clear();
                HoldingSignals.Clear();

                BuildStationList(15.0f);

                bool removeStations = false;
                for (int i = StationStops.Count - 1; i >= 0; i--)
                {
                    if (removeStations)
                    {
                        if (StationStops[i].ExitSignal >= 0 && HoldingSignals.Contains(StationStops[i].ExitSignal))
                        {
                            HoldingSignals.Remove(StationStops[i].ExitSignal);
                        }
                        StationStops.RemoveAt(i);
                    }

                    if (StationStops[i].PlatformReference == presentStop)
                    {
                        removeStations = true;
                    }
                }
            }

            // add present occupied sections to train route to avoid out-of-path detection

            AddTrackSections();

            // reset signals etc.

            SignalObjectItems.Clear();
            NextSignalObject[0] = null;
            NextSignalObject[1] = null;
            LastReservedSection[0] = PresentPosition[Direction.Forward].TrackCircuitSectionIndex;
            LastReservedSection[1] = PresentPosition[Direction.Forward].TrackCircuitSectionIndex;

            InitializeSignals(true);

            if (TCRoute != null && (ControlMode == TrainControlMode.AutoSignal || ControlMode == TrainControlMode.AutoNode))
            {
                PresentPosition[Direction.Forward].RouteListIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0);
                PresentPosition[Direction.Backward].RouteListIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Backward].TrackCircuitSectionIndex, 0);

                SwitchToNodeControl(PresentPosition[Direction.Forward].TrackCircuitSectionIndex);
                CheckDeadlock(ValidRoute[0], Number);
                TCRoute.SetReversalOffset(Length, simulator.TimetableMode);
            }
            else if (ControlMode == TrainControlMode.Manual)
            {
                // set track occupation

                UpdateSectionStateManual();

                // reset routes and check sections either end of train

                PresentPosition[Direction.Forward].RouteListIndex = -1;
                PresentPosition[Direction.Backward].RouteListIndex = -1;
                PreviousPosition[Direction.Forward].RouteListIndex = -1;

                UpdateManualMode(-1);
            }
            else if (ControlMode == TrainControlMode.Explorer)
            {
                // set track occupation

                UpdateSectionStateExplorer();

                // reset routes and check sections either end of train

                PresentPosition[Direction.Forward].RouteListIndex = -1;
                PresentPosition[Direction.Backward].RouteListIndex = -1;
                PreviousPosition[Direction.Forward].RouteListIndex = -1;

                UpdateExplorerMode(-1);
            }
            else
            {
                Simulator.Instance.SignalEnvironment.RequestClearNode(RoutedForward, ValidRoute[0]);
            }
        }

        // Update occupancies
        // Update track occupancies after coupling
        protected void UpdateOccupancies()
        {
            if (manualTrainRoute != null) manualTrainRoute.Clear();
            manualTrainRoute = SignalEnvironment.BuildTempRoute(this, PresentPosition[Direction.Backward].TrackCircuitSectionIndex, PresentPosition[Direction.Backward].Offset,
                PresentPosition[Direction.Backward].Direction, Length, false, true, false);

            foreach (TrackCircuitRouteElement thisElement in manualTrainRoute)
            {
                TrackCircuitSection thisSection = thisElement.TrackCircuitSection;
                thisSection.Reserve(RoutedForward, manualTrainRoute);
                if (!thisSection.CircuitState.OccupiedByThisTrain(this))
                    thisSection.SetOccupied(RoutedForward);
            }
        }

        //
        // AddTrackSections
        // Add track sections not present in path to avoid out-of-path detection
        //
        protected void AddTrackSections()
        {
            // check if first section in route

            if (ValidRoute[0].GetRouteIndex(OccupiedTrack[0].Index, 0) > 0)
            {
                int lastSectionIndex = OccupiedTrack[0].Index;
                int lastIndex = ValidRoute[0].GetRouteIndex(lastSectionIndex, 0);

                for (int i = 1; i <= OccupiedTrack.Count - 1; i++)
                {
                    int nextSectionIndex = OccupiedTrack[i].Index;
                    int nextIndex = ValidRoute[0].GetRouteIndex(nextSectionIndex, 0);

                    if (nextIndex < 0) // this section is not in route - if last index = 0, add to start else add to rear
                    {
                        TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[nextSectionIndex];
                        TrackDirection trackDirection = TrackDirection.Ahead;

                        foreach (Location location in EnumExtension.GetValues<Location>())
                        {
                            foreach (TrackDirection direction in EnumExtension.GetValues<TrackDirection>())
                            {
                                if (section.Pins[direction, location].Link == lastSectionIndex)
                                {
                                    trackDirection = section.Pins[direction, location].Direction;
                                    break;
                                }
                            }
                        }

                        if (lastIndex == 0)
                        {
                            ValidRoute[0].Insert(0, new TrackCircuitRouteElement(OccupiedTrack[i], trackDirection, lastSectionIndex));
                        }
                        else
                        {
                            ValidRoute[0].Add(new TrackCircuitRouteElement(OccupiedTrack[i], trackDirection, lastSectionIndex));
                        }
                    }
                    else
                    {
                        lastIndex = nextIndex;
                        lastSectionIndex = nextSectionIndex;
                    }
                }
            }
            // else start from last section
            else
            {
                int otIndex = OccupiedTrack.Count - 1;
                int lastSectionIndex = OccupiedTrack[otIndex].Index;
                int lastIndex = ValidRoute[0].GetRouteIndex(lastSectionIndex, 0);

                for (int i = otIndex - 1; i >= 0; i--)
                {
                    int nextSectionIndex = OccupiedTrack[i].Index;
                    int nextIndex = ValidRoute[0].GetRouteIndex(nextSectionIndex, 0);

                    if (nextIndex < 0) // this section is not in route - if last index = 0, add to start else add to rear
                    {
                        TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[nextSectionIndex];
                        TrackDirection trackDirection = TrackDirection.Ahead;

                        foreach (Location location in EnumExtension.GetValues<Location>())
                        {
                            foreach (TrackDirection direction in EnumExtension.GetValues<TrackDirection>())
                            {
                                if (section.Pins[direction, location].Link == lastSectionIndex)
                                {
                                    trackDirection = section.Pins[direction, location].Direction;
                                    break;
                                }
                            }
                        }

                        if (lastIndex == 0)
                        {
                            ValidRoute[0].Insert(0, new TrackCircuitRouteElement(OccupiedTrack[i], trackDirection, lastSectionIndex));
                        }
                        else
                        {
                            ValidRoute[0].Add(new TrackCircuitRouteElement(OccupiedTrack[i], trackDirection, lastSectionIndex));
                        }
                    }
                    else
                    {
                        lastIndex = nextIndex;
                        lastSectionIndex = nextSectionIndex;
                    }
                }
            }
        }

        //
        // Update track details after uncoupling
        //
        internal bool UpdateTrackActionsUncoupling(bool originalTrain)
        {
            bool inPath = true;

            if (originalTrain)
            {
                RemoveFromTrack();
                ClearDeadlocks();

                List<DistanceTravelledItem> activeActions = requiredActions.GetActions(99999999f, typeof(ClearSectionItem));
                activeActions.Clear();
            }

            // create new TCPositions

            TrackNode tn = FrontTDBTraveller.TN;
            float offset = FrontTDBTraveller.TrackNodeOffset;
            TrackDirection direction = (TrackDirection)FrontTDBTraveller.Direction;

            PresentPosition[Direction.Forward].SetPosition(tn.TrackCircuitCrossReferences, offset, direction);
            PreviousPosition[Direction.Forward].UpdateFrom(PresentPosition[Direction.Forward]);

            tn = RearTDBTraveller.TN;
            offset = RearTDBTraveller.TrackNodeOffset;
            direction = (TrackDirection)RearTDBTraveller.Direction;

            PresentPosition[Direction.Backward].SetPosition(tn.TrackCircuitCrossReferences, offset, direction);

            PresentPosition[Direction.Forward].DistanceTravelled = DistanceTravelledM;
            PresentPosition[Direction.Backward].DistanceTravelled = DistanceTravelledM - Length;

            // Set track sections to occupied

            OccupiedTrack.Clear();

            // build route of sections now occupied
            OccupiedTrack.Clear();
            if (manualTrainRoute != null) manualTrainRoute.Clear();
            manualTrainRoute = SignalEnvironment.BuildTempRoute(this, PresentPosition[Direction.Backward].TrackCircuitSectionIndex, PresentPosition[Direction.Backward].Offset,
                PresentPosition[Direction.Backward].Direction, Length, false, true, false);

            // static train
            if (TrainType == TrainType.Static)
            {

                // clear routes, required actions, traffic details

                ControlMode = TrainControlMode.Undefined;
                if (TCRoute != null)
                {
                    if (TCRoute.TCRouteSubpaths != null) TCRoute.TCRouteSubpaths.Clear();
                    if (TCRoute.TCAlternativePaths != null) TCRoute.TCAlternativePaths.Clear();
                    TCRoute.ActiveAlternativePath = -1;
                }
                if (ValidRoute[0] != null && ValidRoute[0].Count > 0)
                {
                    Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[0], 0, RoutedForward);
                    ValidRoute[0].Clear();
                }
                if (ValidRoute[1] != null && ValidRoute[1].Count > 0)
                {
                    Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[1], 0, RoutedBackward);
                    ValidRoute[1].Clear();
                }
                requiredActions.Clear();

                if (trafficService != null)
                    trafficService.Clear();

                // build dummy route
                TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[PresentPosition[Direction.Backward].TrackCircuitSectionIndex];

                ValidRoute[0] = SignalEnvironment.BuildTempRoute(this, section.Index, PresentPosition[Direction.Backward].Offset,
                            PresentPosition[Direction.Backward].Direction, Length, true, true, false);

                foreach (TrackCircuitRouteElement thisElement in manualTrainRoute)
                {
                    section = thisElement.TrackCircuitSection;
                    section.SetOccupied(RoutedForward);
                }

            }
            // player train or AI train
            else
            {
                //<CSComment> InitializeSignals needs this info sometimes, so I repeat lines below here
                if (!IsActualPlayerTrain && (ControlMode == TrainControlMode.AutoSignal || ControlMode == TrainControlMode.AutoNode))
                {
                    while (TCRoute.ActiveSubPath <= TCRoute.TCRouteSubpaths.Count - 1)
                    {
                        PresentPosition[Direction.Forward].RouteListIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0);
                        PresentPosition[Direction.Backward].RouteListIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Backward].TrackCircuitSectionIndex, 0);
                        if (PresentPosition[Direction.Forward].RouteListIndex < 0 || PresentPosition[Direction.Backward].RouteListIndex < 0)
                        {
                            // Try first to change valid route, if there are other subpaths.
                            if (TCRoute.ActiveSubPath < TCRoute.TCRouteSubpaths.Count - 1)
                            {
                                ValidRoute[0] = null;
                                TCRoute.ActiveSubPath++;
                                ValidRoute[0] = TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath];
                            }
                            else
                            {
                                inPath = false;
                                return inPath;
                            }
                        }
                        else
                        {
                            if (PresentPosition[Direction.Forward].Direction != ValidRoute[0][PresentPosition[Direction.Forward].RouteListIndex].Direction)
                            // Train must be reverted
                            {
                                ReverseFormation(false);
                                TrackCircuitPosition tempTCPosition = PresentPosition[Direction.Forward];
                                PresentPosition[Direction.Forward] = PresentPosition[Direction.Backward];
                                PresentPosition[Direction.Backward] = tempTCPosition;
                            }
                            break;
                        }
                    }
                }

                foreach (TrackCircuitRouteElement routeElement in manualTrainRoute)
                {
                    routeElement.TrackCircuitSection.SetOccupied(RoutedForward);
                }
                // rebuild list of station stops

                if (StationStops.Count > 0)
                {
                    int presentStop = StationStops[0].PlatformReference;
                    StationStops.Clear();
                    HoldingSignals.Clear();

                    BuildStationList(15.0f);

                    bool removeStations = false;
                    for (int i = StationStops.Count - 1; i >= 0; i--)
                    {
                        if (removeStations)
                        {
                            if (StationStops[i].ExitSignal >= 0 && StationStops[i].HoldSignal && HoldingSignals.Contains(StationStops[i].ExitSignal))
                            {
                                HoldingSignals.Remove(StationStops[i].ExitSignal);
                            }
                            StationStops.RemoveAt(i);
                        }

                        if (StationStops[i].PlatformReference == presentStop)
                        {
                            removeStations = true;
                        }
                    }
                }

                Reinitialize();
            }
            return inPath;
        }

        //
        // Perform various reinitializations
        //
        internal void Reinitialize()
        {
            // reset signals etc.

            SignalObjectItems.Clear();
            NextSignalObject[0] = null;
            NextSignalObject[1] = null;
            LastReservedSection[0] = PresentPosition[Direction.Forward].TrackCircuitSectionIndex;
            LastReservedSection[1] = PresentPosition[Direction.Backward].TrackCircuitSectionIndex;

            InitializeSignals(true);

            switch (ControlMode)
            {
                case TrainControlMode.AutoSignal:
                case TrainControlMode.AutoNode:
                    PresentPosition[Direction.Forward].RouteListIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0);
                    PresentPosition[Direction.Backward].RouteListIndex = ValidRoute[0].GetRouteIndex(PresentPosition[Direction.Backward].TrackCircuitSectionIndex, 0);

                    CheckDeadlock(ValidRoute[0], Number);
                    SwitchToNodeControl(PresentPosition[Direction.Forward].TrackCircuitSectionIndex);
                    TCRoute.SetReversalOffset(Length, simulator.TimetableMode);
                    break;
                case TrainControlMode.Manual:
                    // set track occupation

                    UpdateSectionStateManual();

                    // reset routes and check sections either end of train

                    PresentPosition[Direction.Forward].RouteListIndex = -1;
                    PresentPosition[Direction.Backward].RouteListIndex = -1;
                    PreviousPosition[Direction.Forward].RouteListIndex = -1;

                    UpdateManualMode(-1);
                    break;
                case TrainControlMode.Explorer:
                    // set track occupation

                    UpdateSectionStateExplorer();

                    // reset routes and check sections either end of train

                    PresentPosition[Direction.Forward].RouteListIndex = -1;
                    PresentPosition[Direction.Backward].RouteListIndex = -1;
                    PreviousPosition[Direction.Forward].RouteListIndex = -1;

                    UpdateExplorerMode(-1);
                    break;
                default:
                    CheckDeadlock(ValidRoute[0], Number);
                    Simulator.Instance.SignalEnvironment.RequestClearNode(RoutedForward, ValidRoute[0]);
                    break;
            }
        }

        //
        // Temporarily remove from track to allow decoupled train to set occupied sections
        //
        internal void TemporarilyRemoveFromTrack()
        {
            RemoveFromTrack();
            ClearDeadlocks();
            List<DistanceTravelledItem> activeActions = requiredActions.GetActions(99999999f, typeof(ClearSectionItem));
            activeActions.Clear();
        }

        // Checks if it has to go to next active subpath
        //
        private void TryIncrementSubpath()
        {
            // active subpath must be incremented in parallel in incorporated train if present; not just after incorporation
            if (IncorporatedTrainNo >= 0)
            {
                Train incorporatedTrain = simulator.TrainDictionary[IncorporatedTrainNo];
                if (incorporatedTrain.PresentPosition[Direction.Forward].TrackCircuitSectionIndex != PresentPosition[Direction.Backward].TrackCircuitSectionIndex && 
                    incorporatedTrain.PresentPosition[Direction.Backward].TrackCircuitSectionIndex != PresentPosition[Direction.Backward].TrackCircuitSectionIndex)
                    IncrementSubpath(incorporatedTrain);
                incorporatedTrain.PresentPosition[Direction.Forward].TrackCircuitSectionIndex = -1;
                incorporatedTrain.PresentPosition[Direction.Backward].TrackCircuitSectionIndex = -1;
            }
        }

        //
        // Goes to next active subpath
        //
        internal static void IncrementSubpath(Train train)
        {
            if (train.TCRoute.ActiveSubPath < train.TCRoute.TCRouteSubpaths.Count - 1)
            {
                train.TCRoute.ActiveSubPath++;
                train.ValidRoute[0] = train.TCRoute.TCRouteSubpaths[train.TCRoute.ActiveSubPath];
            }
        }

        //
        // Get end of common section
        //
        private static int EndCommonSection(int index, TrackCircuitPartialPathRoute route, TrackCircuitPartialPathRoute otherRoute)
        {
            int firstSection = route[index].TrackCircuitSection.Index;

            int trainSection = firstSection;
            int otherTrainSection = firstSection;

            int trainIndex = index;
            int otherTrainIndex = otherRoute.GetRouteIndex(firstSection, 0);

            while (trainSection == otherTrainSection && trainIndex < (route.Count - 1) && otherTrainIndex > 0)
            {
                trainIndex++;
                otherTrainIndex--;
                trainSection = route[trainIndex].TrackCircuitSection.Index;
                otherTrainSection = otherRoute[otherTrainIndex].TrackCircuitSection.Index;
            }

            return trainIndex;
        }

        /// <summary>
        /// Create station stop list
        /// <\summary>
        protected void BuildStationList(float clearingDistanceM)
        {
            if (trafficService == null)
                return;   // no traffic definition

            int beginActiveSubroute = 0;
            int activeSubrouteNodeIndex = 0;

            // loop through traffic points

            foreach (ServiceTrafficItem serviceTraffic in trafficService)
            {
                bool validStop = CreateStationStop(serviceTraffic.PlatformStartID, serviceTraffic.ArrivalTime, serviceTraffic.DepartTime, clearingDistanceM, ref beginActiveSubroute, ref activeSubrouteNodeIndex);
                if (!validStop)
                {
                    Trace.TraceInformation($"Train {Number} Service {Name}: cannot find platform {serviceTraffic.PlatformStartID}");
                }
            }
        }

        /// <summary>
        /// Create station stop list
        /// <\summary>
        private bool CreateStationStop(int platformStartID, int arrivalTime, int departTime, float clearingDistanceM, ref int beginActiveSubroute, ref int activeSubrouteNodeIndex)
        {
            int activeSubroute = beginActiveSubroute;
            bool terminalStation = false;

            TrackCircuitPartialPathRoute route = TCRoute.TCRouteSubpaths[activeSubroute];

            // get platform details

            if (Simulator.Instance.SignalEnvironment.PlatformXRefList.TryGetValue(platformStartID, out int platformIndex))
            {
                PlatformDetails platform = Simulator.Instance.SignalEnvironment.PlatformDetailsList[platformIndex];
                int sectionIndex = platform.TCSectionIndex[0];
                int routeIndex = route.GetRouteIndex(sectionIndex, activeSubrouteNodeIndex);
                // No backwards!
                if (routeIndex >= 0 && StationStops.Count > 0 && StationStops[StationStops.Count - 1].RouteIndex == routeIndex
                    && StationStops[StationStops.Count - 1].SubrouteIndex == activeSubroute
                    && StationStops[StationStops.Count - 1].PlatformItem.TrackCircuitOffset[Location.FarEnd, route[routeIndex].Direction] >= platform.TrackCircuitOffset[Location.FarEnd, route[routeIndex].Direction])
                {
                    if (activeSubrouteNodeIndex < route.Count - 1)
                        activeSubrouteNodeIndex++;
                    else if (activeSubroute < (TCRoute.TCRouteSubpaths.Count - 1))
                    {
                        activeSubroute++;
                        activeSubrouteNodeIndex = 0;
                        route = TCRoute.TCRouteSubpaths[activeSubroute];
                    }
                    else
                    {
                        Trace.TraceWarning($"Train {Number} Service {Name} : platform {platformStartID} not in correct sequence");
                        return false;
                    }
                    routeIndex = route.GetRouteIndex(sectionIndex, activeSubrouteNodeIndex);
                }

                if (!simulator.TimetableMode && routeIndex == route.Count - 1 && TCRoute.ReversalInfo[activeSubroute].Valid)
                {
                    // Check if station beyond reversal point
                    if (TCRoute.ReversalInfo[activeSubroute].ReverseReversalOffset < platform.TrackCircuitOffset[Location.NearEnd, route[routeIndex].Direction])
                        routeIndex = -1;
                }

                // if first section not found in route, try last
                if (routeIndex < 0)
                {
                    sectionIndex = platform.TCSectionIndex[platform.TCSectionIndex.Count - 1];
                    routeIndex = route.GetRouteIndex(sectionIndex, activeSubrouteNodeIndex);
                    if (!simulator.TimetableMode && routeIndex == route.Count - 1 && TCRoute.ReversalInfo[activeSubroute].Valid)
                    {
                        // Check if station beyond reversal point
                        if (TCRoute.ReversalInfo[activeSubroute].ReverseReversalOffset < platform.TrackCircuitOffset[Location.NearEnd, route[routeIndex].Direction])
                        {
                            routeIndex = -1;
                            // jump next subpath, because station stop can't be there
                            activeSubroute++;
                        }
                    }
                }

                // if neither section found - try next subroute - keep trying till found or out of subroutes
                while (routeIndex < 0 && activeSubroute < (TCRoute.TCRouteSubpaths.Count - 1))
                {
                    activeSubroute++;
                    activeSubrouteNodeIndex = 0;
                    route = TCRoute.TCRouteSubpaths[activeSubroute];
                    routeIndex = route.GetRouteIndex(sectionIndex, activeSubrouteNodeIndex);
                    if (!simulator.TimetableMode && routeIndex == route.Count - 1 && TCRoute.ReversalInfo[activeSubroute].Valid)
                    {
                        // Check if station beyond reversal point
                        if (TCRoute.ReversalInfo[activeSubroute].ReverseReversalOffset < platform.TrackCircuitOffset[Location.NearEnd, route[routeIndex].Direction])
                            routeIndex = -1;
                    }
                    // if first section not found in route, try last

                    if (routeIndex < 0)
                    {
                        sectionIndex = platform.TCSectionIndex[platform.TCSectionIndex.Count - 1];
                        routeIndex = route.GetRouteIndex(sectionIndex, activeSubrouteNodeIndex);
                        if (!simulator.TimetableMode && routeIndex == route.Count - 1 && TCRoute.ReversalInfo[activeSubroute].Valid)
                        {
                            // Check if station beyond reversal point
                            TrackDirection direction = route[routeIndex].Direction;
                            if (TCRoute.ReversalInfo[activeSubroute].ReverseReversalOffset < platform.TrackCircuitOffset[Location.NearEnd, direction])
                            {
                                routeIndex = -1;
                                // jump next subpath, because station stop can't be there
                                activeSubroute++;
                            }
                        }
                    }
                }

                // if neither section found - platform is not on route - skip

                if (routeIndex < 0)
                {
                    Trace.TraceWarning($"Train {Number} Service {Name} : platform {platformStartID} is not on route");
                    return false;
                }
                else
                {
                    activeSubrouteNodeIndex = routeIndex;
                }

                // determine end stop position depending on direction
                TrackCircuitRouteElement routeElement = route[routeIndex];

                int endSectionIndex = routeElement.Direction == 0 ?
                    platform.TCSectionIndex[platform.TCSectionIndex.Count - 1] :
                    platform.TCSectionIndex[0];
                int beginSectionIndex = routeElement.Direction == 0 ?
                    platform.TCSectionIndex[0] :
                    platform.TCSectionIndex[platform.TCSectionIndex.Count - 1];

                float endOffset = platform.TrackCircuitOffset[Location.FarEnd, (TrackDirection)routeElement.Direction];
                float beginOffset = platform.TrackCircuitOffset[Location.NearEnd, (TrackDirection)routeElement.Direction];

                float deltaLength = platform.Length - Length; // platform length - train length

                TrackCircuitSection endSection = TrackCircuitSection.TrackCircuitList[endSectionIndex];


                int firstRouteIndex = route.GetRouteIndex(beginSectionIndex, 0);
                if (firstRouteIndex < 0)
                    firstRouteIndex = routeIndex;
                int lastRouteIndex = route.GetRouteIndex(endSectionIndex, 0);
                if (lastRouteIndex < 0)
                    lastRouteIndex = routeIndex;

                // if train too long : search back for platform with same name
                float fullLength = platform.Length;

                if (deltaLength < 0)
                {
                    float actualBegin = beginOffset;

                    TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[beginSectionIndex];

                    // Other platforms in same section

                    if (section.PlatformIndices.Count > 1)
                    {
                        foreach (int nextIndex in section.PlatformIndices)
                        {
                            if (nextIndex != platformIndex)
                            {
                                PlatformDetails otherPlatform = Simulator.Instance.SignalEnvironment.PlatformDetailsList[nextIndex];
                                if (string.Compare(otherPlatform.Name, platform.Name, StringComparison.OrdinalIgnoreCase) == 0)
                                {
                                    int otherSectionIndex = routeElement.Direction == 0 ?
                                        otherPlatform.TCSectionIndex[0] :
                                        otherPlatform.TCSectionIndex[otherPlatform.TCSectionIndex.Count - 1];
                                    if (otherSectionIndex == beginSectionIndex)
                                    {
                                        if (otherPlatform.TrackCircuitOffset[Location.NearEnd, routeElement.Direction] < actualBegin)
                                        {
                                            actualBegin = otherPlatform.TrackCircuitOffset[Location.NearEnd, routeElement.Direction];
                                            fullLength = endOffset - actualBegin;
                                        }
                                    }
                                    else
                                    {
                                        int addRouteIndex = route.GetRouteIndex(otherSectionIndex, 0);
                                        float addOffset = otherPlatform.TrackCircuitOffset[Location.FarEnd, routeElement.Direction.Reverse()];
                                        // offset of begin in other direction is length of available track

                                        if (lastRouteIndex > 0)
                                        {
                                            float length = route.GetDistanceAlongRoute(addRouteIndex, addOffset, lastRouteIndex, endOffset, true);
                                            if (length > fullLength)
                                                fullLength = length;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    deltaLength = fullLength - Length;
                }

                // search back along route
                if (deltaLength < 0)
                {
                    float distance = fullLength + beginOffset;
                    bool platformFound = false;

                    for (int i = firstRouteIndex - 1; i >= 0 && distance < 500f && !platformFound; i--)
                    {
                        TrackCircuitSection nextSection = route[i].TrackCircuitSection;

                        foreach (int otherPlatformIndex in nextSection.PlatformIndices)
                        {
                            PlatformDetails otherPlatform = Simulator.Instance.SignalEnvironment.PlatformDetailsList[otherPlatformIndex];
                            if (string.Compare(otherPlatform.Name, platform.Name, StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                fullLength = otherPlatform.Length + distance;
                                // we miss a little bit (offset) - that's because we don't know direction of other platform
                                platformFound = true; // only check for one more
                            }
                        }
                        distance += nextSection.Length;
                    }

                    deltaLength = fullLength - Length;
                }

                // check whether terminal station or not
                TrackCircuitPartialPathRoute routeToEndOfTrack = SignalEnvironment.BuildTempRoute(this, endSectionIndex, endOffset, routeElement.Direction, 30, true, true, false);
                if (routeToEndOfTrack.Count > 0)
                {
                    TrackCircuitSection section = routeToEndOfTrack[routeToEndOfTrack.Count - 1].TrackCircuitSection;
                    if (section.CircuitType == TrackCircuitType.EndOfTrack)
                    {
                        terminalStation = true;
                        foreach (TrackCircuitRouteElement tcElement in routeToEndOfTrack)
                        {
                            section = tcElement.TrackCircuitSection;
                            if (section.CircuitType == TrackCircuitType.Junction)
                            {
                                terminalStation = false;
                                break;
                            }
                        }
                    }
                }

                // determine stop position
                float stopOffset = endOffset - (0.5f * deltaLength);
                if (terminalStation && deltaLength > 0 && !simulator.TimetableMode)
                    stopOffset = endOffset - 1;

                // beyond section : check for route validity (may not exceed route)
                if (stopOffset > endSection.Length)
                {
                    float addOffset = stopOffset - endSection.Length;
                    float overlap = 0f;

                    for (int i = lastRouteIndex; i < route.Count && overlap < addOffset; i++)
                    {
                        TrackCircuitSection nextSection = route[i].TrackCircuitSection;
                        overlap += nextSection.Length;
                    }

                    if (overlap < stopOffset)
                        stopOffset = overlap;
                }

                // check if stop offset beyond end signal - do not hold at signal
                int endSignal = -1;
                bool holdSignal = false;
                bool NoWaitSignal = false;
                bool NoClaimAllowed = false;

                // check if train is to reverse in platform
                // if so, set signal at other end as hold signal

                TrackDirection useDirection = routeElement.Direction;
                bool inDirection = true;

                if (TCRoute.ReversalInfo[activeSubroute].Valid)
                {
                    TrackCircuitReversalInfo thisReversal = TCRoute.ReversalInfo[activeSubroute];
                    int reversalIndex = thisReversal.SignalUsed ? thisReversal.LastSignalIndex : thisReversal.LastDivergeIndex;
                    if (reversalIndex >= 0 && reversalIndex <= lastRouteIndex &&
                        (CheckVicinityOfPlatformToReversalPoint(platform.TrackCircuitOffset[Location.FarEnd, routeElement.Direction], activeSubrouteNodeIndex, activeSubroute) || simulator.TimetableMode)
                        && !(reversalIndex == lastRouteIndex && thisReversal.ReverseReversalOffset - 50.0 > platform.TrackCircuitOffset[Location.FarEnd, routeElement.Direction])) // reversal point is this section or earlier
                    {
                        useDirection = useDirection.Reverse();
                        inDirection = false;
                    }
                }

                // check for end signal
                if (platform.EndSignals[useDirection] >= 0)
                {
                    endSignal = platform.EndSignals[useDirection];

                    // stop location is in front of signal
                    if (inDirection)
                    {
                        if (platform.DistanceToSignals[useDirection] > (stopOffset - endOffset))
                        {
                            holdSignal = true;

                            if ((platform.DistanceToSignals[useDirection] + (endOffset - stopOffset)) < clearingDistanceM)
                            {
                                stopOffset = endOffset + platform.DistanceToSignals[useDirection] - clearingDistanceM - 1.0f;
                            }
                        }
                        // at terminal station we will stop just in front of signal
                        else if (terminalStation && deltaLength <= 0 && !simulator.TimetableMode)
                        {
                            holdSignal = true;
                            stopOffset = endOffset + platform.DistanceToSignals[useDirection] - 3.0f;
                        }
                        // if most of train fits in platform then stop at signal
                        else if ((platform.DistanceToSignals[useDirection] - clearingDistanceM + platform.Length) >
                                      (0.6 * Length))
                        {
                            holdSignal = true;
                            stopOffset = endOffset + platform.DistanceToSignals[useDirection] - clearingDistanceM - 1.0f;
                            // set 1m earlier to give priority to station stop over signal
                        }
                        // train does not fit in platform - reset exit signal
                        else
                        {
                            endSignal = -1;
                        }
                    }
                    else
                    // end of train is beyond signal
                    {
                        TrackDirection oldUseDirection = useDirection.Reverse();
                        if (platform.EndSignals[oldUseDirection] >= 0 && terminalStation && deltaLength <= 0 && !simulator.TimetableMode)
                        {
                            // check also the back of train after reverse
                            stopOffset = endOffset + platform.DistanceToSignals[oldUseDirection] - 3.0f;
                        }
                        if ((beginOffset - platform.DistanceToSignals[useDirection]) < (stopOffset - Length))
                        {
                            holdSignal = true;

                            if ((stopOffset - Length - beginOffset + platform.DistanceToSignals[useDirection]) < clearingDistanceM)
                            {
                                if (!(terminalStation && deltaLength > 0 && !simulator.TimetableMode))
                                    stopOffset = beginOffset - platform.DistanceToSignals[useDirection] + Length + clearingDistanceM + 1.0f;
                            }
                        }
                        // if most of train fits in platform then stop at signal
                        else if ((platform.DistanceToSignals[useDirection] - clearingDistanceM + platform.Length) >
                                      (0.6 * Length))
                        {
                            // set 1m earlier to give priority to station stop over signal
                            if (!(terminalStation && deltaLength > 0 && !simulator.TimetableMode))
                                stopOffset = beginOffset - platform.DistanceToSignals[useDirection] + Length + clearingDistanceM + 1.0f;

                            // check if stop is clear of end signal (if any)
                            if (platform.EndSignals[routeElement.Direction] != -1)
                            {
                                if (stopOffset < (endOffset + platform.DistanceToSignals[routeElement.Direction]))
                                {
                                    holdSignal = true; // if train fits between signals
                                }
                                else
                                {
                                    if (!(terminalStation && deltaLength > 0 && !simulator.TimetableMode))
                                        stopOffset = endOffset + platform.DistanceToSignals[routeElement.Direction] - 1.0f; // stop at end signal
                                }
                            }
                        }
                        // train does not fit in platform - reset exit signal
                        else
                        {
                            endSignal = -1;
                        }
                    }
                }

                if (simulator.Settings.NoForcedRedAtStationStops)
                {
                    // We don't want reds at exit signal in this case
                    holdSignal = false;
                }

                // build and add station stop

                TrackCircuitRouteElement lastElement = route[lastRouteIndex];

                StationStop thisStation = new StationStop(
                        platformStartID,
                        platform,
                        activeSubroute,
                        lastRouteIndex,
                        lastElement.TrackCircuitSection.Index,
                        routeElement.Direction,
                        endSignal,
                        holdSignal,
                        NoWaitSignal,
                        NoClaimAllowed,
                        stopOffset,
                        arrivalTime,
                        departTime,
                        false,
                        null,
                        null,
                        null,
                        false,
                        false,
                        false,
                        false,
                        false,
                        false,
                        StationStopType.Station);

                StationStops.Add(thisStation);

                //<CSComment> should this be reused?
                // 
                //
                //                    // if station has hold signal and this signal is the same as the exit signal for previous station, remove the exit signal from the previous station
                //
                //                    if (HoldSignal && StationStops.Count > 1)
                //                    {
                //                        if (EndSignal == StationStops[StationStops.Count - 2].ExitSignal && StationStops[StationStops.Count - 2].HoldSignal)
                //                        {
                //                            StationStops[StationStops.Count - 2].HoldSignal = false;
                //                            StationStops[StationStops.Count - 2].ExitSignal = -1;
                //                            if (HoldingSignals.Contains(EndSignal))
                //                            {
                //                                HoldingSignals.Remove(EndSignal);
                //                            }
                //                        }
                //                    }

                // add signal to list of hold signals
                if (holdSignal)
                {
                    HoldingSignals.Add(endSignal);
                }
            }
            else
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Check whether train is at Platform
        /// returns true if yes
        /// </summary>
        protected bool AtPlatform()
        {
            // build list of occupied section
            bool atStation = false;
            int frontIndex = PresentPosition[Direction.Forward].RouteListIndex;
            int rearIndex = PresentPosition[Direction.Backward].RouteListIndex;
            List<int> occupiedSections = new List<int>();

            // check valid positions
            if (frontIndex < 0 && rearIndex < 0) // not on route so cannot be in station
            {
                return atStation; // no further actions possible
            }

            // correct position if either end is off route
            if (frontIndex < 0)
                frontIndex = rearIndex;
            if (rearIndex < 0)
                rearIndex = frontIndex;

            // set start and stop in correct order
            int startIndex = frontIndex < rearIndex ? frontIndex : rearIndex;
            int stopIndex = frontIndex < rearIndex ? rearIndex : frontIndex;

            for (int iIndex = startIndex; iIndex <= stopIndex; iIndex++)
            {
                occupiedSections.Add(ValidRoute[0][iIndex].TrackCircuitSection.Index);
            }

            // check if any platform section is in list of occupied sections - if so, we're in the station
            foreach (int sectionIndex in StationStops[0].PlatformItem.TCSectionIndex)
            {
                if (occupiedSections.Contains(sectionIndex))
                {
                    // TODO : check offset within section
                    atStation = true;
                    break;
                }
            }
            return atStation;
        }

        /// <summary>
        /// Check whether train has missed platform
        /// returns true if yes
        /// </summary>
        internal bool MissedPlatform(float thresholdDistance)
        {
            // check if station missed
            int stationRouteIndex = ValidRoute[0].GetRouteIndex(StationStops[0].TrackCircuitSectionIndex, 0);

            if (StationStops[0].SubrouteIndex == TCRoute.ActiveSubPath)
            {
                if (stationRouteIndex < 0)
                {
                    return true;
                }
                else if (stationRouteIndex <= PresentPosition[Direction.Backward].RouteListIndex)
                {
                    TrackCircuitSection platformSection = TrackCircuitSection.TrackCircuitList[StationStops[0].TrackCircuitSectionIndex];
                    float platformReverseStopOffset = platformSection.Length - StationStops[0].StopOffset;
                    return ValidRoute[0].GetDistanceAlongRoute(stationRouteIndex, platformReverseStopOffset, PresentPosition[Direction.Backward].RouteListIndex, PresentPosition[Direction.Backward].Offset, true) > thresholdDistance;
                }
            }
            return false;
        }

        /// <summary>
        /// Check vicinity of reversal point to Platform
        /// returns false if distance greater than preset value 
        /// </summary>
        private bool CheckVicinityOfPlatformToReversalPoint(float tcOffset, int routeListIndex, int activeSubpath)
        {
            float threshold = 100.0f;
            float lengthToGoM = -tcOffset;
            TrackCircuitSection section;
            if (routeListIndex == -1)
            {
                Trace.TraceWarning($"Train {Number} service {Name}, platform off path; reversal point considered remote");
                return false;
            }
            int reversalRouteIndex = TCRoute.TCRouteSubpaths[activeSubpath].GetRouteIndex(TCRoute.ReversalInfo[TCRoute.ActiveSubPath].ReversalSectionIndex, routeListIndex);
            if (reversalRouteIndex == -1)
            {
                Trace.TraceWarning($"Train {Number} service {Name}, reversal or end point off path; reversal point considered remote");
                return false;
            }
            if (routeListIndex <= reversalRouteIndex)
            {
                for (int i = routeListIndex; i < TCRoute.TCRouteSubpaths[activeSubpath].Count; i++)
                {
                    section = TCRoute.TCRouteSubpaths[activeSubpath][i].TrackCircuitSection;
                    if (section.Index == TCRoute.ReversalInfo[TCRoute.ActiveSubPath].ReversalSectionIndex)
                    {
                        break;
                    }
                    else
                    {
                        lengthToGoM += section.Length;
                        if (lengthToGoM > threshold)
                            return false;
                    }
                }
                return lengthToGoM + TCRoute.ReversalInfo[TCRoute.ActiveSubPath].ReverseReversalOffset < threshold;
            }
            else
                // platform is beyond reversal point
                return true;
        }

        /// <summary>
        /// in a certain % of cases depending from randomization level returns a 0 delay
        /// in the remainder of cases computes a randomized delay using a single-sided pseudo-gaussian distribution
        /// following Daniel Howard's suggestion here https://stackoverflow.com/questions/218060/random-gaussian-variables
        /// Parameters: 
        /// maxDelay maximum added random delay (may be seconds or minutes)
        /// </summary>Ac
        protected static int RandomizedDelayWithThreshold(int maxAddedDelay)
        {
            if (DateTime.UtcNow.Millisecond % 10 < 6 - simulator.Settings.ActRandomizationLevel)
                return 0;
            return (int)(Simulator.Random.Next(0, (int)(Simulator.Resolution * Simulator.Random.NextDouble()) + 1) / Simulator.Resolution * maxAddedDelay);
        }

        /// <summary>
        /// Computes a randomized delay using a single-sided pseudo-gaussian distribution
        /// following Daniel Howard's suggestion here https://stackoverflow.com/questions/218060/random-gaussian-variables
        /// Parameters: 
        /// maxDelay maximum added random delay (may be seconds or minutes)
        /// </summary>
        internal static int RandomizedDelay(int maxAddedDelay)
        {
            return (int)(Simulator.Random.Next(0, (int)(Simulator.Resolution * Simulator.Random.NextDouble()) + 1) / Simulator.Resolution * maxAddedDelay);
        }

        /// <summary>
        /// Computes a randomized delay for the various types of waiting points.
        /// </summary>
        protected static int RandomizedWPDelay(int randomizedDelay)
        {
            if (randomizedDelay < 30000) // standard WP
            {
                randomizedDelay += RandomizedDelayWithThreshold(15 + 5 * simulator.Settings.ActRandomizationLevel);
            }
            else if (randomizedDelay >= 30000 && randomizedDelay < 40000) // absolute WP
            {
                randomizedDelay += RandomizedDelayWithThreshold(2 + simulator.Settings.ActRandomizationLevel);
                if (randomizedDelay % 100 > 59)
                {
                    randomizedDelay += 40;
                    if ((randomizedDelay / 100) % 100 == 24) randomizedDelay -= 2400;
                }
            }
            else if (randomizedDelay > 40000 && randomizedDelay < 60000) // car detach WP
            {
                int additionalDelay = RandomizedDelayWithThreshold(25);
                if (randomizedDelay % 100 + additionalDelay > 99)
                    randomizedDelay += 99;
                else
                    randomizedDelay += additionalDelay;
            }
            return randomizedDelay;
        }

        /// <summary>
        /// Convert player traffic list to station list
        /// <\summary>
        internal void ConvertPlayerTraffic(List<ServiceTrafficItem> playerList)
        {

            if (playerList == null || playerList.Count == 0)
            {
                return;    // no traffic details
            }

            trafficService = new ServiceTraffics(0);

            trafficService.AddRange(playerList);
            BuildStationList(15.0f);  // use 15m. clearing distance
        }

        /// <summary>
        /// Clear station from list, clear exit signal if required
        /// <\summary>
        internal virtual void ClearStation(uint id1, uint id2, bool removeStation)
        {
            int foundStation = -1;
            StationStop station;

            for (int i = 0; i < StationStops.Count; i++)
            {
                station = StationStops[i];
                if (station.SubrouteIndex > TCRoute.ActiveSubPath)
                    break;
                if (station.PlatformReference == id1 || station.PlatformReference == id2)
                {
                    foundStation = i;
                    break;
                }

                if (station.SubrouteIndex > TCRoute.ActiveSubPath)
                    break; // stop looking if station is in next subpath
            }

            if (foundStation >= 0)
            {
                station = StationStops[foundStation];
                if (station.ExitSignal >= 0)
                {
                    HoldingSignals.Remove(station.ExitSignal);

                    if (ControlMode == TrainControlMode.AutoSignal)
                    {
                        Signal nextSignal = Simulator.Instance.SignalEnvironment.Signals[station.ExitSignal];
                        nextSignal.RequestClearSignal(ValidRoute[0], RoutedForward, 0, false, null);
                    }
                }
            }
            if (removeStation)
            {
                for (int i = foundStation; i >= 0; i--)
                {
                    PreviousStop = StationStops[i].CreateCopy();
                    StationStops.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Create pathless player train out of static train
        /// </summary>
        internal void CreatePathlessPlayerTrain()
        {
            TrainType = TrainType.Player;
            IsPathless = true;
            CheckFreight();
            ToggleToManualMode();
            InitializeBrakes();
            InitializeSpeeds();
        }

        /// <summary>
        /// Initializes speeds for pathless player train
        /// </summary>
        ///
        private void InitializeSpeeds()
        {
            AllowedMaxSpeedSignalMpS = allowedAbsoluteMaxSpeedSignalMpS;
            AllowedMaxSpeedLimitMpS = allowedAbsoluteMaxSpeedLimitMpS;
            allowedMaxTempSpeedLimitMpS = allowedAbsoluteMaxTempSpeedLimitMpS;
            TrainMaxSpeedMpS = Math.Min((float)simulator.TRK.Route.SpeedLimit, ((MSTSLocomotive)simulator.PlayerLocomotive).MaxSpeedMpS);
        }

        /// <summary>
        /// Gets the train name from one CarID; used for remote trains
        /// </summary>
        public static string GetTrainName(string trainId)
        {
            if (string.IsNullOrEmpty(trainId))
                return trainId;
            int location = trainId.LastIndexOf('-');
            if (location < 0)
                return trainId;
            return trainId.Substring(0, location - 1);
        }

        /// <summary>
        /// Create status line
        /// <\summary>
        /// <remarks>
        ///  "Train", "Travelled", "Speed", "Max", "AI mode", "AI data", "Mode", "Auth", "Distance", "Signal", "Distance", "Consist", "Path"
        ///  0   Train: Number with trailing type (F freight, P Passenger)
        ///  1   Travelled: travelled distance so far
        ///  2   Speed: Current speed
        ///  3   Max: Maximum allowed speed
        ///  4   AIMode :
        ///      INI     : AI is in INIT mode
        ///      STC     : AI is static
        ///      STP     : AI is Stopped
        ///      BRK     : AI Brakes
        ///      ACC     : AI do acceleration
        ///      FOL     : AI follows
        ///      RUN     : AI is running
        ///      EOP     : AI approch and of path
        ///      STA     : AI is on Station Stop
        ///      WTP     : AI is on Waiting Point
        ///      STE     : AI is in Stopped Existing state
        ///  5   AI Data :
        ///      000&000     : Throttel & Brake in %
        ///                  : for mode INI, BRK, ACC, FOL, RUN or EOP
        ///      HH:mm:ss    : for mode STA or WTP with actualDepart or DepartTime
        ///                  : for mode STC with Start Time Value
        ///      ..:..:..    : For other case
        ///  6   Mode:
        ///          SIGN or Sdelay: Train in AUTO_SIGNAL, with delay if train delayed
        ///          NODE or Ndelay: Train in AUTO_NODE, with delay if train delayed
        ///          MAN: Train in AUTO_MANUAL
        ///          OOC: Train in OUT_OF_CONTROL
        ///          EXP: Train in EXPLORER
        ///  7   Auth + Distance:    For Player Train
        ///          case OOC:   Distance set to blank
        ///              SPAD:   Signal Passed At Danger
        ///              RSPD:   Rear SPAD
        ///              OOAU:   Out Of Authority
        ///              OOPA:   Out Of Path
        ///              SLPP:   Slipped out Path
        ///              SLPT:   Slipped to End of Track
        ///              OOTR:   To End Of Track
        ///              MASW:   Misaligned Switch
        ///              ....:   Undefined
        ///          case Waiting Point: WAIT, Distance set to Train Number to Wait ????
        ///          case NODE:                      Distance: Blank or
        ///              EOT:    End Of Track
        ///              EOP:    End Of Path
        ///              RSW:    Reserved Switch
        ///              LP:     Loop
        ///              TAH:    Train Ahead
        ///              MXD:    Max Distance        Distance: To End Of Authority
        ///              NOP:    No Path Reserved    Distance: To End Of Authority
        ///              ...:    Undefined
        ///          Other:
        ///              Blank + Blank
        ///  7   Next Action :   For AI Train
        ///              SPDL    :   Speed limit
        ///              SIGL    :   Speed signal
        ///              STOP    :   Signal STOP
        ///              REST    :   Signal RESTRICTED
        ///              EOA     :   End Of Authority
        ///              STAT    :   Station Stop
        ///              TRAH    :   Train Ahead
        ///              EOR     :   End Of Route
        ///              NONE    :   None
        ///  9   Signal + Distance
        ///          Manual or Explorer: Distance set to blank
        ///              First:  Reverse direction
        ///                  G:  Signal at STOP but Permission Granted
        ///                  S:  Signal At STOP
        ///                  P:  Signal at STOP & PROCEED
        ///                  R:  Signal at RESTRICTING
        ///                  A:  Signal at APPROACH 1, 2 or 3
        ///                  C:  Signal at CLEAR 1 or 2
        ///                  -:  Not Defined
        ///              <>
        ///              Second: Forward direction
        ///                  G:  Signal at STOP but Permission Granted
        ///                  S:  Signal At STOP
        ///                  P:  Signal at STOP & PROCEED
        ///                  R:  Signal at RESTRICTING
        ///                  A:  Signal at APPROACH 1, 2 or 3
        ///                  C:  Signal at CLEAR 1 or 2
        ///                  -:  Not Defined
        ///          Other:  Distance is Distance to next Signal
        ///              STOP:   Signal at STOP
        ///              SPRC:   Signal at STOP & PROCEED
        ///              REST:   Signal at RESTRICTING
        ///              APP1:   Signal at APPROACH 1
        ///              APP2:   Signal at APPROACH 2
        ///              APP3:   Signal at APPROACH 3
        ///              CLR1:   Signal at CLEAR 1
        ///              CLR2:   Signal at CLEAR 2
        ///  11  Consist:
        ///          PLAYER:
        ///          REMOTE:
        ///  12  Path:
        ///          not Manual nor Explorer:
        ///              number or ?     :   Id of subpath in valid TCRoute or ? if no valid TCRoute
        ///              =[n]            :   Number of remaining station stops
        ///              {               :   Starting String
        ///              CircuitString   :   List of Circuit (see next)
        ///              }               :   Ending String
        ///              x or blank      :   x if already on TCRoute
        ///          Manual or Explorer:
        ///              CircuitString   :   Backward
        ///              ={  Dir }=      :   Dir is '<' or '>'
        ///              CircuitString   :   Forward
        ///          For AI  :
        ///              Train Name
        ///  
        ///      CircuitString analyse:
        ///          Build string for section information
        ///      returnString +
        ///      CircuitType:
        ///          >   : Junction
        ///          +   : CrossOver
        ///          [   : End of Track direction 1
        ///          ]   : End of Track direction 0
        ///          -   : Default (Track Section)
        ///      Deadlock traps:
        ///          Yes : Ended with *
        ///              Await number    : ^
        ///              Await more      : ~
        ///      Train Occupancy:    + '&' If more than one
        ///          N° of train     : If one train
        ///      If train reservation :
        ///          (
        ///          Train Number
        ///          )
        ///      If signal reserved :
        ///          (S
        ///          Signal Number
        ///          )
        ///      If one or more train claim
        ///          #
        /// <\remarks>
        public string[] GetStatus(bool metric)
        {

            string[] statusString = new string[13];

            //  0, "Train"
            if (Delay.HasValue && Delay.Value.TotalMinutes >= 1)
            {
                statusString[0] = $"{Number} D";
            }
            else if (IsFreight)
            {
                statusString[0] = $"{Number} F";
            }
            else
            {
                statusString[0] = $"{Number} P";
            }

            //  1, "Travelled"
            statusString[1] = FormatStrings.FormatDistanceDisplay(DistanceTravelledM, metric);

            //  2, "Speed"
            float trainSpeed = TrainType == TrainType.Remote && SpeedMpS != 0 ? targetSpeedMpS : SpeedMpS;
            statusString[2] = FormatStrings.FormatSpeedDisplay(trainSpeed, metric);
            if (Math.Abs(trainSpeed) > Math.Abs(AllowedMaxSpeedMpS))
                statusString[2] += "!!!";

            //  3, "Max"
            statusString[3] = FormatStrings.FormatSpeedLimit(AllowedMaxSpeedMpS, metric);

            //  4, "AI mode"
            statusString[4] = " ";  // for AI trains

            //  5, "AI data"
            statusString[5] = " ";  // for AI trains

            //  6, "Mode"
            switch (ControlMode)
            {
                case TrainControlMode.AutoSignal:
                    statusString[6] = Delay.HasValue ? $"S +{Delay.Value.TotalMinutes:00}" : "SIGN";
                    break;
                case TrainControlMode.AutoNode:
                    statusString[6] = Delay.HasValue ? $"N +{Delay.Value.TotalMinutes:00}" : "NODE";
                    break;
                case TrainControlMode.Manual:
                    statusString[6] = "MAN";
                    break;
                case TrainControlMode.OutOfControl:
                    statusString[6] = "OOC";
                    break;
                case TrainControlMode.Explorer:
                    statusString[6] = "EXPL";
                    break;
                case TrainControlMode.TurnTable:
                    statusString[6] = "TURN";
                    break;
                default:
                    statusString[6] = "----";
                    break;
            }

            //  7, "Auth"
            if (ControlMode == TrainControlMode.OutOfControl)
            {
                switch (OutOfControlReason)
                {
                    case OutOfControlReason.PassedAtDanger:
                        statusString[7] = "SPAD";
                        break;
                    case OutOfControlReason.RearPassedAtDanger:
                        statusString[7] = "RSPD";
                        break;
                    case OutOfControlReason.OutOfAuthority:
                        statusString[7] = "OOAU";
                        break;
                    case OutOfControlReason.OutOfPath:
                        statusString[7] = "OOPA";
                        break;
                    case OutOfControlReason.SlippedIntoPath:
                        statusString[7] = "SLPP";
                        break;
                    case OutOfControlReason.SlippedToEndOfTrack:
                        statusString[7] = "SLPT";
                        break;
                    case OutOfControlReason.OutOfTrack:
                        statusString[7] = "OOTR";
                        break;
                    case OutOfControlReason.MisalignedSwitch:
                        statusString[7] = "MASW";
                        break;
                    case OutOfControlReason.SlippedIntoTurnTable:
                        statusString[7] = "SLPT";
                        break;
                    default:
                        statusString[7] = "....";
                        break;
                }

                //  8, "Distance"
                statusString[8] = " ";
            }
            else if (ControlMode == TrainControlMode.AutoNode)
            {
                switch (EndAuthorityTypes[0])
                {
                    case EndAuthorityType.EndOfTrack:
                        statusString[7] = "EOT";
                        break;
                    case EndAuthorityType.EndOfPath:
                        statusString[7] = "EOP";
                        break;
                    case EndAuthorityType.ReservedSwitch:
                        statusString[7] = "RSW";
                        break;
                    case EndAuthorityType.Loop:
                        statusString[7] = "LP ";
                        break;
                    case EndAuthorityType.TrainAhead:
                        statusString[7] = "TAH";
                        break;
                    case EndAuthorityType.MaxDistance:
                        statusString[7] = "MXD";
                        break;
                    case EndAuthorityType.NoPathReserved:
                        statusString[7] = "NOP";
                        break;
                    default:
                        statusString[7] = "";
                        break;
                }

                //  8, "Distance"
                if (EndAuthorityTypes[0] != EndAuthorityType.MaxDistance && EndAuthorityTypes[0] != EndAuthorityType.NoPathReserved)
                {
                    statusString[8] = FormatStrings.FormatDistance(DistanceToEndNodeAuthorityM[0], metric);
                }
                else
                {
                    statusString[8] = " ";
                }
            }
            else
            {
                statusString[7] = " ";

                //  8, "Distance"
                statusString[7] = " ";
            }

            //  9, "Signal"
            if (ControlMode == TrainControlMode.Manual || ControlMode == TrainControlMode.Explorer)
            {
                // reverse direction
                char firstchar = '-';

                if (NextSignalObject[1] != null)
                {
                    SignalAspectState nextAspect = GetNextSignalAspect(1);
                    if (NextSignalObject[1].EnabledTrain == null || NextSignalObject[1].EnabledTrain.Train != this) nextAspect = SignalAspectState.Stop;  // aspect only valid if signal enabled for this train

                    switch (nextAspect)
                    {
                        case SignalAspectState.Stop:
                            firstchar = (NextSignalObject[1].OverridePermission == SignalPermission.Granted) ? 'G' : 'S';
                            break;
                        case SignalAspectState.Stop_And_Proceed:
                            firstchar = 'P';
                            break;
                        case SignalAspectState.Restricting:
                            firstchar = 'R';
                            break;
                        case SignalAspectState.Approach_1:
                            firstchar = 'A';
                            break;
                        case SignalAspectState.Approach_2:
                            firstchar = 'A';
                            break;
                        case SignalAspectState.Approach_3:
                            firstchar = 'A';
                            break;
                        case SignalAspectState.Clear_1:
                            firstchar = 'C';
                            break;
                        case SignalAspectState.Clear_2:
                            firstchar = 'C';
                            break;
                    }
                }

                // forward direction
                char lastchar = '-';

                if (NextSignalObject[0] != null)
                {
                    SignalAspectState nextAspect = GetNextSignalAspect(0);
                    if (NextSignalObject[0].EnabledTrain == null || NextSignalObject[0].EnabledTrain.Train != this) nextAspect = SignalAspectState.Stop;  // aspect only valid if signal enabled for this train

                    switch (nextAspect)
                    {
                        case SignalAspectState.Stop:
                            lastchar = NextSignalObject[0].OverridePermission == SignalPermission.Granted ? 'G' : 'S';
                            break;
                        case SignalAspectState.Stop_And_Proceed:
                            lastchar = 'P';
                            break;
                        case SignalAspectState.Restricting:
                            lastchar = 'R';
                            break;
                        case SignalAspectState.Approach_1:
                            lastchar = 'A';
                            break;
                        case SignalAspectState.Approach_2:
                            lastchar = 'A';
                            break;
                        case SignalAspectState.Approach_3:
                            lastchar = 'A';
                            break;
                        case SignalAspectState.Clear_1:
                            lastchar = 'C';
                            break;
                        case SignalAspectState.Clear_2:
                            lastchar = 'C';
                            break;
                    }
                }
                statusString[9] = $"{firstchar}<>{lastchar}";

                //  10, "Distance"
                statusString[10] = " ";
            }
            else
            {
                if (NextSignalObject[0] != null)
                {
                    SignalAspectState nextAspect = GetNextSignalAspect(0);

                    switch (nextAspect)
                    {
                        case SignalAspectState.Stop:
                            statusString[9] = "STOP";
                            break;
                        case SignalAspectState.Stop_And_Proceed:
                            statusString[9] = "SPRC";
                            break;
                        case SignalAspectState.Restricting:
                            statusString[9] = "REST";
                            break;
                        case SignalAspectState.Approach_1:
                            statusString[9] = "APP1";
                            break;
                        case SignalAspectState.Approach_2:
                            statusString[9] = "APP2";
                            break;
                        case SignalAspectState.Approach_3:
                            statusString[9] = "APP3";
                            break;
                        case SignalAspectState.Clear_1:
                            statusString[9] = "CLR1";
                            break;
                        case SignalAspectState.Clear_2:
                            statusString[9] = "CLR2";
                            break;
                    }

                    //  10, "Distance"
                    statusString[10] = DistanceToSignal.HasValue ? FormatStrings.FormatDistance(DistanceToSignal.Value, metric) : "-";
                }
                else
                {
                    statusString[9] = " ";
                    //  10, "Distance"
                    statusString[10] = " ";
                }
            }

            //  11, "Consist"
            statusString[11] = "PLAYER";
            if (!simulator.TimetableMode && this != simulator.OriginalPlayerTrain)
                statusString[11] = Name.Substring(0, Math.Min(Name.Length, 7));

            if (TrainType == TrainType.Remote)
            {
                string trainName;
                if (LeadLocomotive != null)
                    trainName = GetTrainName(LeadLocomotive.CarID);
                else if (Cars != null && Cars.Count > 0)
                    trainName = GetTrainName(Cars[0].CarID);
                else
                    trainName = "REMOTE";
                statusString[11] = trainName.Substring(0, Math.Min(trainName.Length, 7));
            }

            //  12, "Path"
            StringBuilder circuitString = new StringBuilder();

            if ((ControlMode != TrainControlMode.Manual && ControlMode != TrainControlMode.Explorer) || ValidRoute[1] == null)
            {
                // station stops
                if (StationStops == null || StationStops.Count == 0)
                {
                    circuitString.Append("[ ] ");
                }
                else
                {
                    circuitString.Append($"[{StationStops.Count}] ");
                }

                // route
                if (TCRoute == null)
                {
                    circuitString.Append("?={");
                }
                else
                {
                    circuitString.Append(TCRoute.ActiveSubPath);
                    circuitString.Append("={");
                }

                int startIndex = PresentPosition[Direction.Forward].RouteListIndex;
                if (startIndex < 0)
                {
                    circuitString.Append("<out of route>");
                }
                else
                {
                    for (int i = PresentPosition[Direction.Forward].RouteListIndex; i < ValidRoute[0].Count; i++)
                    {
                        BuildSectionString(circuitString, ValidRoute[0][i].TrackCircuitSection, Direction.Forward);

                    }
                }

                circuitString.Append('}');

                if (TCRoute != null && TCRoute.ActiveSubPath < TCRoute.TCRouteSubpaths.Count - 1)
                {
                    circuitString.Append('x');
                    circuitString.Append(TCRoute.ActiveSubPath + 1);
                }
                if (TCRoute != null && TCRoute.OriginalSubpath != -1)
                    circuitString.Append("???");
            }
            else
            {
                // backward path
                StringBuilder backstring = new StringBuilder();
                for (int i = ValidRoute[1].Count - 1; i >= 0; i--)
                {
                    BuildSectionString(backstring, ValidRoute[1][i].TrackCircuitSection, Direction.Backward);
                }

                if (backstring.Length > 20)
                {
                    // ensure string starts with section delimiter
                    while (backstring[0] != '-' && backstring[0] != '+' && backstring[0] != '<')
                        backstring.Remove(0, 1);

                    if (backstring.Length > 20)
                        backstring.Length = 20;

                    circuitString.Append("...");
                }
                circuitString.Append(backstring);

                // train indication and direction
                circuitString.Append("={");
                if (MUDirection == MidpointDirection.Reverse)
                {
                    circuitString.Append('<');
                }
                else
                {
                    circuitString.Append('>');
                }
                circuitString.Append("}=");

                // forward path

                StringBuilder forwardstring = new StringBuilder();
                for (int i = 0; i < ValidRoute[0].Count; i++)
                {
                    BuildSectionString(forwardstring, ValidRoute[0][i].TrackCircuitSection, 0);
                }
                circuitString.Append(forwardstring);
            }

            statusString[12] = circuitString.ToString();

            return statusString;
        }

        /// <summary>
        ///  Build string for section information
        ///  <c>returnString +
        ///     CircuitType:
        ///         >   : Junction
        ///         +   : CrossOver
        ///         [   : End of Track direction 1
        ///         ]   : End of Track direction 0
        ///     Deadlock traps:
        ///         Yes : Ended with *
        ///             Await number    : ^
        ///             Await more      : ~
        ///     Train Occupancy:    + '&' If more than one
        ///         N° of train     : If one train
        ///     If train reservation :
        ///         (
        ///         Train Number
        ///         )
        ///     If signal reserved :
        ///         (S
        ///         Signal Number
        ///         )
        ///     If one or more train claim
        ///         #</c>
        /// </summary>
        private void BuildSectionString(StringBuilder builder, TrackCircuitSection section, Direction direction)
        {
            switch (section.CircuitType)
            {
                case TrackCircuitType.Junction:
                    builder.Append('>');
                    break;
                case TrackCircuitType.Crossover:
                    builder.Append('+');
                    break;
                case TrackCircuitType.EndOfTrack:
                    builder.Append(direction == Direction.Forward ? ']' : '[');
                    break;
                default:
                    builder.Append('-');
                    break;
            }

            if (section.DeadlockTraps.ContainsKey(Number))
            {
                if (section.DeadlockAwaited.Contains(Number))
                {
                    builder.Append("^[");
                    List<int> deadlockInfo = section.DeadlockTraps[Number];
                    for (int index = 0; index < deadlockInfo.Count - 2; index++)
                    {
                        builder.Append(deadlockInfo[index]);
                        builder.Append(',');
                    }
                    builder.Append(deadlockInfo.Last());
                    builder.Append(']');
                }
                else if (section.DeadlockAwaited.Count > 0)
                {
                    builder.Append('~');
                }
                builder.Append('*');
            }

            if (section.CircuitState.OccupationState.Count > 0)
            {
                List<TrainRouted> allTrains = section.CircuitState.TrainsOccupying();
                builder.Append(allTrains[0].Train.Number);
                if (allTrains.Count > 1)
                {
                    builder.Append('&');
                }
            }

            if (section.CircuitState.TrainReserved != null)
            {
                builder.Append($"({section.CircuitState.TrainReserved.Train.Number})");
            }

            if (section.CircuitState.SignalReserved >= 0)
            {
                builder.Append($"(S{section.CircuitState.SignalReserved})");
            }

            if (section.CircuitState.TrainClaimed.Count > 0)
            {
                builder.Append('#');
            }
        }

        /// <summary>
        /// Add restart times at stations and waiting points
        /// Update the string for 'TextPageDispatcherInfo'.
        /// Modifiy fields 4 and 5
        /// <\summary>
        public void AddRestartTime(string[] stateString)
        {
            if (null == stateString)
                return;
            string movString = string.Empty;
            string abString = string.Empty;

            if (this == simulator.OriginalPlayerTrain)
            {
                if (simulator.ActivityRun?.Current is ActivityTaskPassengerStopAt passengerStop && passengerStop.BoardingS > 0)
                {
                    movString = "STA";
                    abString = $"{TimeSpan.FromSeconds(passengerStop.BoardingEndS):c}";
                }
                else if (Math.Abs(SpeedMpS) <= 0.01 && AuxActionsContainer.specRequiredActions.Count > 0 &&
                    AuxActionsContainer.specRequiredActions.First.Value is AuxActSigDelegate auxAction &&
                    auxAction.currentMvmtState == AiMovementState.HandleAction)
                {
                    movString = "WTS";
                    abString = $"{TimeSpan.FromSeconds(auxAction.ActualDepart):c}";
                }
            }
            else if (StationStops.Count > 0 && AtStation)
            {
                movString = "STA";
                abString = (StationStops[0].ActualDepart > 0) ? $"{TimeSpan.FromSeconds(StationStops[0].ActualDepart):c}" : "..:..:..";
            }
            else if (Math.Abs(SpeedMpS) <= 0.01 && this is AITrain aiTrain && aiTrain.nextActionInfo is AuxActionWPItem auxAction &&
                    aiTrain.MovementState == AiMovementState.HandleAction)
            {
                movString = "WTP";
                abString = $"{TimeSpan.FromSeconds(auxAction.ActualDepart):c}";
            }
            else if (Math.Abs(SpeedMpS) <= 0.01 && AuxActionsContainer.SpecAuxActions.Count > 0 && AuxActionsContainer.SpecAuxActions[0] is AIActionWPRef auxWPAction &&
                auxWPAction.keepIt?.currentMvmtState == AiMovementState.HandleAction)
            {
                movString = "WTP";
                abString = $"{TimeSpan.FromSeconds(auxWPAction.keepIt.ActualDepart):c}";
            }
            stateString[4] = movString;
            stateString[5] = abString;
        }

        //TODO 20210121 refactor
        // Contains data about all types of signals
        public EnumArray<List<TrainPathItem>[], Direction> PlayerTrainSignals { get; } = new EnumArray<List<TrainPathItem>[], Direction>(() =>
            {
                List<TrainPathItem>[] result = new List<TrainPathItem>[OrSignalTypes.Instance.FunctionTypes.Count];
                for (int fn_type = 0; fn_type < OrSignalTypes.Instance.FunctionTypes.Count; fn_type++)
                    result[fn_type] = new List<TrainPathItem>();
                return result;
            }); // first index 0 forward, 1 backward; second index signal type (NORMAL etc.)
        public EnumArray<List<TrainPathItem>, Direction> PlayerTrainSpeedposts { get; } = new EnumArray<List<TrainPathItem>, Direction>(() => new List<TrainPathItem>());// 0 forward, 1 backward
        public EnumArray2D<List<TrainPathItem>, Direction, SwitchDirection> PlayerTrainDivergingSwitches { get; } = new EnumArray2D<List<TrainPathItem>, Direction, SwitchDirection>(() => new List<TrainPathItem>());// 0 forward, 1 backward; second index 0 facing, 1 trailing
        public EnumArray<List<TrainPathItem>, Direction> PlayerTrainMileposts { get; } = new EnumArray<List<TrainPathItem>, Direction>(() => new List<TrainPathItem>()); // 0 forward, 1 backward
        public EnumArray<List<TrainPathItem>, Direction> PlayerTrainTunnels { get; } = new EnumArray<List<TrainPathItem>, Direction>(() => new List<TrainPathItem>());


        //================================================================================================//
        /// <summary>
        /// Initializes train data for TCS and TrackMonitor
        /// </summary>
        /// 
        public void InitializePlayerTrainData()
        {
            foreach (Direction direction in EnumExtension.GetValues<Direction>())
            {
                PlayerTrainTunnels[direction]?.Clear();
                PlayerTrainMileposts[direction]?.Clear();
                PlayerTrainSpeedposts[direction]?.Clear();
                foreach (SwitchDirection switchDirection in EnumExtension.GetValues<SwitchDirection>())
                {
                    PlayerTrainDivergingSwitches[direction, switchDirection]?.Clear();
                }
                for (int i = 0; i < OrSignalTypes.Instance.FunctionTypes.Count; i++)
                    PlayerTrainSignals[direction][i]?.Clear();
            }
        }

        //================================================================================================//
        /// <summary>
        /// Updates the train data for TCS and TrackMonitor
        /// </summary>
        public void UpdatePlayerTrainData()
        {
            UpdatePlayerTrainData(10000.0f);
            //TODO add generation of other train data
        }

        //================================================================================================//
        /// <summary>
        /// Updates the Player train data;
        /// For every section it adds the TrainObjectItems to the various lists;
        /// this first in forward direction and then in reverse direction
        /// </summary>

        public void UpdatePlayerTrainData(float maxDistanceM)
        {
            // variable used to search for NORMAL signals and speedposts when not in AUTO mode
            float maxDistanceNormalSignal = ControlMode == TrainControlMode.Explorer ? Math.Max(maxDistanceM, (float)simulator.TRK.Route.SpeedLimit * 250.0f) : maxDistanceM;
            InitializePlayerTrainData();
            // fill in the lists
            TrainPathItem trainPathItem;
            foreach (Direction dir in EnumExtension.GetValues<Direction>())
            {
                if (ValidRoute[(int)dir] == null || dir == Direction.Backward && PresentPosition[dir].TrackCircuitSectionIndex < 0)
                    continue;
                int startIndex = dir == Direction.Forward ? PresentPosition[dir].RouteListIndex : ValidRoute[(int)dir].GetRouteIndex(PresentPosition[dir].TrackCircuitSectionIndex, 0);
                if (startIndex < 0)
                    continue;
                int index = startIndex;
                float progressiveMaxSpeedLimitMpS = AllowedMaxSpeedLimitMpS;
                // NORMAL signals get data from a different place when in Auto mode
                if (dir == Direction.Forward && (ControlMode == TrainControlMode.AutoNode || ControlMode == TrainControlMode.AutoSignal))
                {
                    // we put them all without checking with max distance
                    bool signalProcessed = false;
                    foreach (SignalItemInfo signalObjectItem in SignalObjectItems)
                    {
                        if (signalObjectItem.ItemType == SignalItemType.Signal)
                        {
                            TrackMonitorSignalAspect signalAspect =
                                signalObjectItem.SignalDetails.TranslateTMAspect(signalObjectItem.SignalDetails.SignalLR(SignalFunction.Normal));
                            if (signalObjectItem.SignalDetails.EnabledTrain == null || signalObjectItem.SignalDetails.EnabledTrain.Train != this)
                            {
                                signalAspect = TrackMonitorSignalAspect.Stop;
                                TrainPathItem stopItem = new TrainPathItem(signalAspect, signalObjectItem.ActualSpeed, signalObjectItem.DistanceToTrain, signalObjectItem.SignalDetails);
                                PlayerTrainSignals[dir][0].Add(stopItem);
                                signalProcessed = true;
                                break;
                            }
                            trainPathItem = new TrainPathItem(signalAspect, signalObjectItem.ActualSpeed, signalObjectItem.DistanceToTrain, signalObjectItem.SignalDetails);
                            PlayerTrainSignals[dir][0].Add(trainPathItem);
                            signalProcessed = true;
                        }
                        else if (signalObjectItem.ItemType == SignalItemType.SpeedLimit && signalObjectItem.ActualSpeed > 0)
                        {
                            trainPathItem = new TrainPathItem(signalObjectItem.ActualSpeed, signalObjectItem.DistanceToTrain, (SpeedItemType)(signalObjectItem.SpeedInfo.LimitedSpeedReduction));
                            PlayerTrainSpeedposts[dir].Add(trainPathItem);
                        }
                    }
                    if (!signalProcessed && NextSignalObject[0] != null && NextSignalObject[0].EnabledTrain != null && NextSignalObject[0].EnabledTrain.Train == this)
                    {
                        TrackMonitorSignalAspect signalAspect = NextSignalObject[0].TranslateTMAspect(NextSignalObject[0].SignalLR(SignalFunction.Normal));
                        SpeedInfo thisSpeedInfo = NextSignalObject[0].SignalSpeed(SignalFunction.Normal);
                        float validSpeed = thisSpeedInfo == null ? -1 : (IsFreight ? thisSpeedInfo.FreightSpeed : thisSpeedInfo.PassengerSpeed);
                        trainPathItem = new TrainPathItem(signalAspect, validSpeed, DistanceToSignal.GetValueOrDefault(), NextSignalObject[0]);
                        PlayerTrainSignals[Direction.Forward][0].Add(trainPathItem);
                    }
                }
                // rear direction, auto mode
                // NORMAL signals get data from a different place when in Auto mode
                if (dir == Direction.Backward && (ControlMode == TrainControlMode.AutoNode || ControlMode == TrainControlMode.AutoSignal))
                {
                    if (clearanceAtRearM > 0 && rearSignalObject != null)
                    {
                        TrackMonitorSignalAspect signalAspect = rearSignalObject.TranslateTMAspect(rearSignalObject.SignalLR(SignalFunction.Normal));
                        trainPathItem = new TrainPathItem(signalAspect, -1.0f, clearanceAtRearM, rearSignalObject);
                        PlayerTrainSignals[Direction.Backward][0].Add(trainPathItem);
                    }
                }

                float lengthOffset = (dir == Direction.Backward) ? (-PresentPosition[dir].Offset + TrackCircuitSection.TrackCircuitList[PresentPosition[dir].TrackCircuitSectionIndex].Length) : PresentPosition[dir].Offset;
                float totalLength = 0;
                TrackCircuitPartialPathRoute routePath = ValidRoute[(int)dir];
                float prevMilepostValue = -1f;
                float prevMilepostDistance = -1f;

                while (index < routePath.Count && totalLength - lengthOffset < maxDistanceNormalSignal)
                {
                    float sectionDistanceToTrainM = totalLength - lengthOffset;
                    TrackCircuitRouteElement routeElement = routePath[index];
                    TrackDirection sectionDirection = routeElement.Direction;
                    TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[routeElement.TrackCircuitSection.Index];
                    for (int fn_type = 0; fn_type < OrSignalTypes.Instance.FunctionTypes.Count; fn_type++)
                    {
                        if (OrSignalTypes.Instance.FunctionTypes[fn_type] == "NORMAL" && (ControlMode == TrainControlMode.Manual || ControlMode == TrainControlMode.Explorer))
                        {
                            if (section.EndSignals[sectionDirection] != null)
                            {
                                Signal signal = section.EndSignals[sectionDirection];
                                SpeedInfo speedInfo = signal.SignalSpeed(SignalFunction.Normal);
                                float validSpeed = speedInfo == null ? -1 : (IsFreight ? speedInfo.FreightSpeed : speedInfo.PassengerSpeed);
                                TrackMonitorSignalAspect signalAspect = signal.TranslateTMAspect(signal.SignalLR(SignalFunction.Normal));
                                trainPathItem = new TrainPathItem(signalAspect, validSpeed, section.Length + sectionDistanceToTrainM, signal);
                                PlayerTrainSignals[dir][fn_type].Add(trainPathItem);
                            }
                        }
                        else if (OrSignalTypes.Instance.FunctionTypes[fn_type] != "NORMAL" && sectionDistanceToTrainM < maxDistanceM)
                        {
                            TrackCircuitSignalList signalList = section.CircuitItems.TrackCircuitSignals[sectionDirection][fn_type];
                            foreach (TrackCircuitSignalItem signal in signalList)
                            {
                                if (signal.SignalLocation > lengthOffset)
                                {
                                    trainPathItem = new TrainPathItem(signal.SignalLocation + sectionDistanceToTrainM, signal.Signal);
                                    PlayerTrainSignals[dir][fn_type].Add(trainPathItem);
                                }
                            }
                        }
                    }

                    if (ControlMode == TrainControlMode.Manual || ControlMode == TrainControlMode.Explorer)
                    {
                        foreach (TrackCircuitSignalItem speeditem in section.CircuitItems.TrackCircuitSpeedPosts[routeElement.Direction])
                        {
                            Signal speedpost = speeditem.Signal;
                            SpeedInfo speedInfo = speedpost.SignalSpeed(SignalFunction.Speed);
                            float validSpeed = speedInfo == null ? -1 : (IsFreight ? speedInfo.FreightSpeed : speedInfo.PassengerSpeed);


                            if (speeditem.SignalLocation > lengthOffset && (validSpeed > 0 || (speedInfo != null && speedInfo.Reset)))
                            {
                                if (speedInfo != null && speedInfo.Reset)
                                    validSpeed = progressiveMaxSpeedLimitMpS;
                                else progressiveMaxSpeedLimitMpS = validSpeed;
                                trainPathItem = new TrainPathItem(validSpeed, speeditem.SignalLocation + sectionDistanceToTrainM, (SpeedItemType)speedpost.SpeedPostType());
                                PlayerTrainSpeedposts[dir].Add(trainPathItem);
                            }
                        }
                    }
                    // search for switches
                    if (section.CircuitType == TrackCircuitType.Junction && sectionDistanceToTrainM < maxDistanceM)
                    {
                        bool rightSwitch = true;
                        TrackJunctionNode junctionNode = simulator.TDB.TrackDB.TrackNodes[section.OriginalIndex] as TrackJunctionNode;
                        if (section.Pins[sectionDirection, Location.FarEnd].Link != -1)
                        {
                            //facing
                            bool diverging = false;
                            if ((section.ActivePins[sectionDirection, Location.FarEnd].Link > 0 && section.JunctionDefaultRoute == 0) ||
                                (section.ActivePins[sectionDirection, Location.NearEnd].Link > 0 && section.JunctionDefaultRoute > 0))
                            {
                                // diverging 
                                diverging = true;
                                float junctionAngle = junctionNode.GetAngle(simulator.TSectionDat);
                                if (junctionAngle < 0) rightSwitch = false;
                            }
                            if (diverging)
                            {
                                trainPathItem = new TrainPathItem(rightSwitch, sectionDistanceToTrainM, TrainPathItemType.FacingSwitch);
                                PlayerTrainDivergingSwitches[dir, SwitchDirection.Facing].Add(trainPathItem);
                            }
                        }
                        else if (section.Pins[sectionDirection, Location.FarEnd].Link == -1)
                        {
                            // trailing
                            if ((section.Pins[sectionDirection.Reverse(), Location.NearEnd].Link == routeElement.TrackCircuitSection.Index && section.JunctionDefaultRoute == 0) ||
                                (section.Pins[sectionDirection.Reverse(), Location.NearEnd].Link == routeElement.TrackCircuitSection.Index && section.JunctionDefaultRoute > 0))
                            {
                                // trailing diverging
                                float junctionAngle = junctionNode.GetAngle(simulator.TSectionDat);
                                if (junctionAngle < 0) rightSwitch = false; // FIXME: or the opposite? untested...

                                trainPathItem = new TrainPathItem(rightSwitch, sectionDistanceToTrainM, TrainPathItemType.TrailingSwitch);
                                PlayerTrainDivergingSwitches[dir, SwitchDirection.Trailing].Add(trainPathItem);
                            }
                        }
                    }
                    // search for mileposts
                    if (section.CircuitItems.TrackCircuitMileposts != null)
                    {
                        foreach (TrackCircuitMilepost milepostItem in section.CircuitItems.TrackCircuitMileposts)
                        {
                            Milepost milepost = milepostItem.Milepost;
                            float distanceToTrainM = milepostItem.MilepostLocation[(Location)sectionDirection.Reverse()] + sectionDistanceToTrainM;
                            if (distanceToTrainM < maxDistanceM)
                            {
                                if (!(distanceToTrainM - prevMilepostDistance < 50 && milepost.Value == prevMilepostValue) && distanceToTrainM > 0 && distanceToTrainM < maxDistanceM)
                                {
                                    trainPathItem = new TrainPathItem(milepost.Value, distanceToTrainM);
                                    prevMilepostDistance = distanceToTrainM;
                                    prevMilepostValue = milepost.Value;
                                    PlayerTrainMileposts[dir].Add(trainPathItem);
                                }
                            }
                            else
                                break;
                        }
                    }
                    // search for tunnels
                    if (section.TunnelInfo != null)
                    {
                        foreach (TunnelInfoData tunnel in section.TunnelInfo)
                        {
                            float tunnelStartOffset = tunnel.Start[sectionDirection];
                            float distanceToTrainM = tunnelStartOffset + sectionDistanceToTrainM;
                            if (distanceToTrainM < maxDistanceM)
                            {
                                if (tunnelStartOffset > lengthOffset)
                                {
                                    trainPathItem = new TrainPathItem(tunnelStartOffset + sectionDistanceToTrainM, (int)tunnel.LengthTotal, TrainPathItemType.Tunnel);
                                    PlayerTrainTunnels[dir].Add(trainPathItem);
                                }
                                else if (PlayerTrainTunnels[dir].Count == 0 && (tunnel.End[sectionDirection] < 0 || tunnel.End[sectionDirection] > lengthOffset))
                                {
                                    // Train is in tunnel, compute remaining length
                                    float remainingLength = tunnel.LengthTotal - lengthOffset + (tunnelStartOffset < 0 ? tunnel.SectionStartOffset[sectionDirection] : tunnelStartOffset);
                                    trainPathItem = new TrainPathItem(-1, (int)remainingLength, TrainPathItemType.Tunnel);
                                    PlayerTrainTunnels[dir].Add(trainPathItem);
                                }

                            }
                            else
                                break;
                        }
                    }

                    totalLength += (section.Length - lengthOffset);
                    lengthOffset = 0;

                    // terminate where route not set
                    int setSection = section.ActivePins[routeElement.OutPin[0], (Location)routeElement.OutPin[Location.FarEnd]].Link;
                    index++;
                    if (setSection < 0)
                        continue;
                }
            }
        }

        /// <summary>
        /// Create TrackInfoObject for information in TrackMonitor window
        /// </summary>
        public TrainInfo GetTrainInfo()
        {
            TrainInfo result;
            switch (ControlMode)
            {
                case TrainControlMode.AutoNode:
                case TrainControlMode.AutoSignal:
                    result = GetTrainInfoAuto();
                    break;
                case TrainControlMode.Manual:
                case TrainControlMode.Explorer:
                    result = GetTrainInfoManual();
                    break;
                case TrainControlMode.OutOfControl:
                    result = GetTrainInfoOutOfControl();
                    break;
                default:// no state? should not occur, but just set no details at all
                    result = new TrainInfo(ControlMode, Direction.Forward, 0);
                    TrainPathItem dummyItem = new TrainPathItem(EndAuthorityType.NoPathReserved, 0.0f);
                    result.ObjectInfoForward.Add(dummyItem);
                    result.ObjectInfoBackward.Add(dummyItem);
                    break;
            }
            // sort items on increasing distance
            result.ObjectInfoForward.Sort();
            result.ObjectInfoBackward.Sort();

            return result;
        }

        /// <summary>
        /// Create TrackInfoObject for information in TrackMonitor window for Auto mode
        /// </summary>
        private TrainInfo GetTrainInfoAuto()
        {
            TrainInfo result = new TrainInfo(ControlMode, MidPointDirectionToDirectionUnset(MUDirection),
                SpeedMpS, ProjectedSpeedMpS, Math.Min(AllowedMaxSpeedMpS, TrainMaxSpeedMpS), simulator.PlayerLocomotive?.CurrentElevationPercent ?? 0,
                simulator.PlayerLocomotive != null ? ((simulator.PlayerLocomotive.Flipped ^ simulator.PlayerLocomotive.GetCabFlipped()) ? Direction.Backward : Direction.Forward) : Direction.Forward, true);

            AddTrainReversalInfo(result, TCRoute.ReversalInfo[TCRoute.ActiveSubPath]);

            // set waiting point
            if (this != simulator.OriginalPlayerTrain)
                AddWaitingPointInfo(result);

            bool maxAuthSet = false;
            // set object items - forward
            if (ControlMode == TrainControlMode.AutoNode)
            {
                result.ObjectInfoForward.Add(new TrainPathItem(EndAuthorityTypes[0], DistanceToEndNodeAuthorityM[0]));
                maxAuthSet = true;
            }

            const float maxDistanceM = 7000.0f;

            foreach (TrainPathItem trainItem in PlayerTrainSignals[Direction.Forward][0])
            {
                result.ObjectInfoForward.Add(trainItem);
            }
            foreach (TrainPathItem trainItem in PlayerTrainSpeedposts[Direction.Forward])
            {
                if (trainItem.DistanceToTrainM <= maxDistanceM)
                    result.ObjectInfoForward.Add(trainItem);
                else
                    break;
            }

            foreach (TrainPathItem trainItem in PlayerTrainMileposts[Direction.Forward])
            {
                if (trainItem.DistanceToTrainM <= maxDistanceM)
                    result.ObjectInfoForward.Add(trainItem);
                else
                    break;
            }
            foreach (TrainPathItem trainItem in PlayerTrainDivergingSwitches[Direction.Forward, SwitchDirection.Facing])
            {
                if (trainItem.DistanceToTrainM <= maxDistanceM)
                    result.ObjectInfoForward.Add(trainItem);
                else
                    break;
            }

            if (StationStops?.Count > 0 && (!maxAuthSet || StationStops[0].DistanceToTrainM < DistanceToEndNodeAuthorityM[0]) &&
                StationStops[0].SubrouteIndex == TCRoute.ActiveSubPath)
            {
                result.ObjectInfoForward.Add(new TrainPathItem(StationStops[0].DistanceToTrainM, (int)StationStops[0].PlatformItem.Length, TrainPathItemType.Station));
            }


            // Draft to display more station stops
            /*            if (StationStops != null && StationStops.Count > 0)
            {
                for (int iStation = 0; iStation < StationStops.Count; iStation++)
                {
                    if ((!maxAuthSet || StationStops[iStation].DistanceToTrainM <= DistanceToEndNodeAuthorityM[0]) && StationStops[iStation].SubrouteIndex == TCRoute.activeSubpath)
                    {
                        TrainObjectItem nextItem = new TrainObjectItem(StationStops[iStation].DistanceToTrainM, (int)StationStops[iStation].PlatformItem.Length);
                        thisInfo.ObjectInfoForward.Add(nextItem);
                    }
                    else break;
                }
            }*/

            // set object items - backward

            if (clearanceAtRearM <= 0)
            {
                result.ObjectInfoBackward.Add(new TrainPathItem(EndAuthorityType.NoPathReserved, 0.0f));
            }
            else
            {
                if (rearSignalObject != null)
                {
                    //TrackMonitorSignalAspect signalAspect = rearSignalObject.TranslateTMAspect(rearSignalObject.SignalLR(SignalFunction.Normal));
                    result.ObjectInfoBackward.Add(PlayerTrainSignals[Direction.Backward][0][0]);
                }
                else
                {
                    result.ObjectInfoBackward.Add(new TrainPathItem(EndAuthorityType.EndOfAuthority, clearanceAtRearM));
                }
            }
            return result;
        }

        /// <summary>
        /// Add reversal info to TrackMonitorInfo
        /// </summary>
        private protected virtual void AddTrainReversalInfo(TrainInfo trainInfo, TrackCircuitReversalInfo reversalInfo)
        {
            if (!reversalInfo.Valid && TCRoute.ActiveSubPath == TCRoute.TCRouteSubpaths.Count - 1)
                return;

            int reversalSection = reversalInfo.ReversalSectionIndex;
            if (reversalInfo.LastDivergeIndex >= 0)
            {
                reversalSection = reversalInfo.SignalUsed ? reversalInfo.SignalSectorIndex : reversalInfo.DivergeSectorIndex;
            }

            float reversalDistanceM = TrackCircuitSection.GetDistanceBetweenObjects(PresentPosition[Direction.Backward].TrackCircuitSectionIndex, PresentPosition[Direction.Backward].Offset, PresentPosition[Direction.Backward].Direction, reversalSection, 0.0f);

            bool reversalEnabled = true;
            reversalDistanceM = Math.Max(reversalDistanceM, TrackCircuitSection.GetDistanceBetweenObjects
                (PresentPosition[Direction.Forward].TrackCircuitSectionIndex, PresentPosition[Direction.Forward].Offset, PresentPosition[Direction.Forward].Direction,
                reversalInfo.ReversalSectionIndex, reversalInfo.ReverseReversalOffset));
            int reversalIndex = reversalInfo.SignalUsed ? reversalInfo.LastSignalIndex : reversalInfo.LastDivergeIndex;
            if (reversalDistanceM > 50f || (PresentPosition[Direction.Backward].RouteListIndex < reversalIndex))
            {
                reversalEnabled = false;
            }
            if (reversalDistanceM > 0)
            {
                trainInfo.ObjectInfoForward.Add(new TrainPathItem(reversalEnabled, reversalDistanceM, reversalInfo.Valid));
            }
        }

        /// <summary>
        /// Add waiting point info to TrackMonitorInfo
        /// </summary>
        private void AddWaitingPointInfo(TrainInfo trainInfo)
        {
            if (AuxActionsContainer.SpecAuxActions.Count > 0 && AuxActionsContainer.SpecAuxActions[0] is AIActionWPRef &&
                (AuxActionsContainer.SpecAuxActions[0] as AIActionWPRef).SubrouteIndex == TCRoute.ActiveSubPath)
            {
                TrackCircuitSection frontSection = TrackCircuitSection.TrackCircuitList[PresentPosition[Direction.Forward].TrackCircuitSectionIndex];
                float leftInSectionM = frontSection.Length - PresentPosition[Direction.Forward].Offset;

                // get action route index - if not found, return distances < 0
                int actionIndex0 = PresentPosition[Direction.Forward].RouteListIndex;
                int actionRouteIndex = ValidRoute[0].GetRouteIndex((AuxActionsContainer.SpecAuxActions[0] as AIActionWPRef).TCSectionIndex, actionIndex0);
                float wpDistance = ValidRoute[0].GetDistanceAlongRoute(actionIndex0, leftInSectionM, actionRouteIndex, (AuxActionsContainer.SpecAuxActions[0] as AIActionWPRef).RequiredDistance, AITrainDirectionForward);
                bool wpEnabled = false;
                if (Math.Abs(SpeedMpS) <= Simulator.MaxStoppedMpS && (((AuxActionsContainer.SpecAuxActions[0] as AIActionWPRef).keepIt != null &&
                    (AuxActionsContainer.SpecAuxActions[0] as AIActionWPRef).keepIt.currentMvmtState == AiMovementState.HandleAction) ||
                    ((this as AITrain).nextActionInfo is AuxActionWPItem && (this as AITrain).MovementState == AiMovementState.HandleAction))) wpEnabled = true;

                trainInfo.ObjectInfoForward.Add(new TrainPathItem(wpDistance, wpEnabled));
            }
        }

        /// <summary>
        /// Create TrackInfoObject for information in TrackMonitor window when in Manual mode
        /// </summary>
        private TrainInfo GetTrainInfoManual()
        {
            bool validPath = true;
            if (TCRoute != null && TCRoute.ActiveSubPath >= 0 && TCRoute.TCRouteSubpaths != null && TCRoute.TCRouteSubpaths.Count > TCRoute.ActiveSubPath)
            {
                TrackCircuitPartialPathRoute pathRoute = TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath];
                validPath = pathRoute.GetRouteIndex(PresentPosition[Direction.Forward].TrackCircuitSectionIndex, 0) >= 0;
            }

            TrainInfo result = new TrainInfo(ControlMode, MidPointDirectionToDirectionUnset(MUDirection), SpeedMpS, ProjectedSpeedMpS,
                Math.Min(AllowedMaxSpeedMpS, TrainMaxSpeedMpS), simulator.PlayerLocomotive != null ? simulator.PlayerLocomotive.CurrentElevationPercent : 0,
                (simulator.PlayerLocomotive.Flipped ^ simulator.PlayerLocomotive.GetCabFlipped()) ? Direction.Backward : Direction.Forward, validPath);


            // set forward information

            // set authority
            result.ObjectInfoForward.Add(new TrainPathItem(EndAuthorityTypes[0], DistanceToEndNodeAuthorityM[0]));

            // run along forward path to catch all speedposts, signals mileposts and diverging switches
            const float maxDistanceM = 7000.0f;

            if (ValidRoute[0] != null)
            {
                foreach (TrainPathItem trainItem in PlayerTrainSignals[Direction.Forward][0])
                {
                    result.ObjectInfoForward.Add(trainItem);
                }
                foreach (TrainPathItem trainItem in PlayerTrainSpeedposts[Direction.Forward])
                {
                    if (trainItem.DistanceToTrainM <= maxDistanceM)
                        result.ObjectInfoForward.Add(trainItem);
                    else
                        break;
                }
                foreach (TrainPathItem trainItem in PlayerTrainMileposts[Direction.Forward])
                {
                    if (trainItem.DistanceToTrainM <= maxDistanceM)
                        result.ObjectInfoForward.Add(trainItem);
                    else
                        break;
                }
                foreach (TrainPathItem trainItem in PlayerTrainDivergingSwitches[Direction.Forward, SwitchDirection.Facing])
                {
                    if (trainItem.DistanceToTrainM <= maxDistanceM)
                        result.ObjectInfoForward.Add(trainItem);
                    else
                        break;
                }
            }

            // set backward information

            // set authority
            result.ObjectInfoBackward.Add(new TrainPathItem(EndAuthorityTypes[1], DistanceToEndNodeAuthorityM[1]));

            // run along backward path to catch all speedposts and signals
            if (ValidRoute[1] != null)
            {
                foreach (TrainPathItem trainItem in PlayerTrainSignals[Direction.Backward][0])
                {
                    result.ObjectInfoBackward.Add(trainItem);
                }
                foreach (TrainPathItem trainItem in PlayerTrainSpeedposts[Direction.Backward])
                {
                    if (trainItem.DistanceToTrainM <= maxDistanceM)
                        result.ObjectInfoBackward.Add(trainItem);
                    else
                        break; ;
                }
                foreach (TrainPathItem trainItem in PlayerTrainMileposts[Direction.Backward])
                {
                    if (trainItem.DistanceToTrainM <= maxDistanceM)
                        result.ObjectInfoBackward.Add(trainItem);
                    else
                        break;
                }
                foreach (TrainPathItem trainItem in PlayerTrainDivergingSwitches[Direction.Backward, SwitchDirection.Facing])
                {
                    if (trainItem.DistanceToTrainM <= maxDistanceM)
                        result.ObjectInfoBackward.Add(trainItem);
                    else
                        break;
                }
            }

            return result;
        }

        //================================================================================================//
        /// <summary>
        /// Create TrackInfoObject for information in TrackMonitor window when OutOfControl
        /// </summary>
        private TrainInfo GetTrainInfoOutOfControl()
        {
            TrainInfo result = new TrainInfo(ControlMode, MidPointDirectionToDirectionUnset(MUDirection), SpeedMpS, ProjectedSpeedMpS,
                Math.Min(AllowedMaxSpeedMpS, TrainMaxSpeedMpS), 0,
                (simulator.PlayerLocomotive.Flipped ^ simulator.PlayerLocomotive.GetCabFlipped()) ? Direction.Backward : Direction.Forward, false);

            // set out of control reason
            result.ObjectInfoForward.Add(new TrainPathItem(OutOfControlReason));
            return result;
        }

        /// <summary>
        /// Create Track Circuit Route Path
        /// </summary>
        internal void SetRoutePath(AIPath aiPath, bool usePosition)
        {
            TrackDirection direction = (TrackDirection)(usePosition ? (int)FrontTDBTraveller.Direction : (RearTDBTraveller != null) ? (int)RearTDBTraveller.Direction : -2);
            TCRoute = new TrackCircuitRoutePath(aiPath, direction, Length, Number);
            ValidRoute[0] = TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath];
        }

        //
        // Preset switches for explorer mode
        //
        internal void PresetExplorerPath(AIPath aiPath)
        {
            TrackDirection direction = (TrackDirection)(RearTDBTraveller != null ? (int)RearTDBTraveller.Direction : -2);
            TCRoute = new TrackCircuitRoutePath(aiPath, direction, 0, Number);

            // loop through all sections in first subroute except first and last (neither can be junction)
            for (int i = 1; i <= TCRoute.TCRouteSubpaths[0].Count - 2; i++)
            {
                TrackCircuitSection section = TCRoute.TCRouteSubpaths[0][i].TrackCircuitSection;
                int nextSectionIndex = TCRoute.TCRouteSubpaths[0][i + 1].TrackCircuitSection.Index;
                int prevSectionIndex = TCRoute.TCRouteSubpaths[0][i - 1].TrackCircuitSection.Index;

                // process Junction
                if (section.CircuitType == TrackCircuitType.Junction)
                {
                    if (section.Pins[TrackDirection.Ahead, Location.NearEnd].Link == nextSectionIndex && !MPManager.NoAutoSwitch())
                    {
                        section.AlignSwitchPins(prevSectionIndex);   // trailing switch
                    }
                    else
                    {
                        section.AlignSwitchPins(nextSectionIndex);   // facing switch
                    }
                }
            }
        }

        /// <summary>
        /// Get total length of reserved section ahead of train
        /// </summary>
        /// <returns></returns>
        private float GetReservedLength()
        {
            float totalLength = 0f;
            int routeListIndex;
            TrackCircuitPartialPathRoute usedRoute;
            float presentOffset;
            TrainRouted routedTrain;
            if (MUDirection == MidpointDirection.Forward || MUDirection == MidpointDirection.N || ValidRoute[1] == null)
            {
                usedRoute = ValidRoute[0];
                routeListIndex = PresentPosition[Direction.Forward].RouteListIndex;
                presentOffset = PresentPosition[Direction.Forward].Offset;
                routedTrain = RoutedForward;
            }
            else
            {
                usedRoute = ValidRoute[1];
                routeListIndex = PresentPosition[Direction.Backward].RouteListIndex;
                presentOffset = PresentPosition[Direction.Backward].Offset;
                routedTrain = RoutedBackward;
            }

            if (routeListIndex >= 0 && usedRoute != null && routeListIndex <= (usedRoute.Count - 1))
            {
                TrackCircuitSection section = usedRoute[routeListIndex].TrackCircuitSection;
                totalLength = section.Length - presentOffset;

                while (routeListIndex < usedRoute.Count - 1)
                {
                    routeListIndex++;
                    section = usedRoute[routeListIndex].TrackCircuitSection;
                    if (section.IsSet(routedTrain, false))
                    {
                        totalLength += section.Length;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return totalLength;
        }

        // Extract alternative route
        internal TrackCircuitPartialPathRoute ExtractAlternativeRoutePathBased(int altRouteIndex)
        {
            TrackCircuitPartialPathRoute returnRoute = new TrackCircuitPartialPathRoute();

            // extract entries of alternative route upto first signal
            foreach (TrackCircuitRouteElement routeElement in TCRoute.TCAlternativePaths[altRouteIndex])
            {
                returnRoute.Add(routeElement);
                if (routeElement.TrackCircuitSection.EndSignals[routeElement.Direction] != null)
                {
                    break;
                }
            }

            return returnRoute;
        }

        // Extract alternative route
        internal static TrackCircuitPartialPathRoute ExtractAlternativeRouteLocationBased(TrackCircuitPartialPathRoute altRoute)
        {
            TrackCircuitPartialPathRoute returnRoute = new TrackCircuitPartialPathRoute();

            // extract entries of alternative route upto first signal

            foreach (TrackCircuitRouteElement routeElement in altRoute)
            {
                returnRoute.Add(routeElement);
                if (routeElement.TrackCircuitSection.EndSignals[routeElement.Direction] != null)
                {
                    break;
                }
            }

            return returnRoute;
        }

        // Set train route to alternative route - path based deadlock processing
        internal virtual void SetAlternativeRoutePathBased(int startElementIndex, int altRouteIndex, Signal nextSignal)
        {
            // set new train route

            TrackCircuitPartialPathRoute route = ValidRoute[0];
            TrackCircuitPartialPathRoute newRoute = new TrackCircuitPartialPathRoute();
            int actSubpath = TCRoute.ActiveSubPath;

            TrackCircuitPartialPathRoute altRoute = TCRoute.TCAlternativePaths[altRouteIndex];
            TCRoute.ActiveAlternativePath = altRouteIndex;

            // part upto split

            for (int i = 0; i < startElementIndex; i++)
            {
                newRoute.Add(route[i]);
            }

            // alternative path

            for (int i = 0; i < altRoute.Count; i++)
            {
                newRoute.Add(altRoute[i]);
            }
            int lastAlternativeSectionIndex = route.GetRouteIndex(altRoute[altRoute.Count - 1].TrackCircuitSection.Index, startElementIndex);

            // check for any stations in abandoned path
            Dictionary<int, StationStop> abdStations = new Dictionary<int, StationStop>();
            CheckAbandonedStations(startElementIndex, lastAlternativeSectionIndex, actSubpath, abdStations);

            // continued path

            for (int i = lastAlternativeSectionIndex + 1; i < route.Count; i++)
            {
                newRoute.Add(route[i]);
            }
            // Reindexes ReversalInfo items
            int countDifference = newRoute.Count - ValidRoute[0].Count;
            if (TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastDivergeIndex >= 0)
                TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastDivergeIndex = TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastDivergeIndex + countDifference;
            if (TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastSignalIndex >= 0)
                TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastSignalIndex = TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastSignalIndex + countDifference;

            // set new route

            ValidRoute[0] = newRoute;
            TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath] = newRoute;

            // check for abandoned stations - try to find alternative on passing path
            LookForReplacementStations(abdStations, newRoute, altRoute);

            // set signal route
            // part upto split

            TrackCircuitPartialPathRoute newSignalRoute = new TrackCircuitPartialPathRoute();

            int splitSignalIndex = nextSignal.SignalRoute.GetRouteIndex(route[startElementIndex].TrackCircuitSection.Index, 0);
            for (int i = 0; i < splitSignalIndex; i++)
            {
                newSignalRoute.Add(nextSignal.SignalRoute[i]);
            }

            // extract new route upto next signal

            TrackCircuitPartialPathRoute nextPart = ExtractAlternativeRoutePathBased(altRouteIndex);
            foreach (TrackCircuitRouteElement routeElement in nextPart)
            {
                newSignalRoute.Add(routeElement);
            }

            nextSignal.ResetSignal(true);
            nextSignal.SignalRoute = newSignalRoute;

            if (ControlMode == TrainControlMode.AutoSignal)
            {
                // keep any items allready passed
                List<SignalItemInfo> keeplist = new List<SignalItemInfo>();
                foreach (SignalItemInfo checkItem in SignalObjectItems)
                {
                    float actualDistance = GetObjectDistanceToTrain(checkItem);
                    if (actualDistance < 0)
                    {
                        keeplist.Add(checkItem);
                    }
                }

                // create new list
                InitializeSignals(true);

                // add any passed items (in reverse order at start of list)
                if (keeplist.Count > 0)
                {
                    for (int i = keeplist.Count - 1; i >= 0; i--)
                    {
                        SignalObjectItems.Insert(0, keeplist[i]);
                    }
                }

                // find new next signal
                NextSignalObject[0] = null;
                for (int i = 0; i <= SignalObjectItems.Count - 1 && NextSignalObject[0] == null; i++)
                {
                    if (SignalObjectItems[i].ItemType == SignalItemType.Signal)
                    {
                        NextSignalObject[0] = SignalObjectItems[i].SignalDetails;
                    }
                }

                NextSignalObject[0]?.RequestClearSignal(ValidRoute[0], RoutedForward, 0, false, null);
            }
        }

        // Set train route to alternative route - location based deadlock processing
        internal virtual void SetAlternativeRouteLocationBased(int startSectionIndex, DeadlockInfo sectionDeadlockInfo, int usedPath, Signal nextSignal)
        {
            // set new train route

            TrackCircuitPartialPathRoute route = ValidRoute[0];
            TrackCircuitPartialPathRoute newRoute = new TrackCircuitPartialPathRoute();

            TrackCircuitPartialPathRoute altRoute = sectionDeadlockInfo.AvailablePathList[usedPath].Path;
            int actSubpath = TCRoute.ActiveSubPath;

            // part upto split

            int startElementIndex = route.GetRouteIndex(startSectionIndex, PresentPosition[Direction.Forward].RouteListIndex);
            for (int i = 0; i < startElementIndex; i++)
            {
                newRoute.Add(route[i]);
            }

            // alternative path

            for (int i = 0; i < altRoute.Count; i++)
            {
                newRoute.Add(altRoute[i]);
            }

            // check for any deadlocks on abandoned path - but only if not on new path

            int lastAlternativeSectionIndex = route.GetRouteIndex(altRoute[altRoute.Count - 1].TrackCircuitSection.Index, startElementIndex);
            for (int i = startElementIndex; i <= lastAlternativeSectionIndex; i++)
            {
                TrackCircuitSection abdSection = route[i].TrackCircuitSection;

                if (newRoute.GetRouteIndex(abdSection.Index, 0) < 0)
                {
                    abdSection.ClearDeadlockTrap(Number);
                }
            }
            // check for any stations in abandoned path

            Dictionary<int, StationStop> abdStations = new Dictionary<int, StationStop>();
            CheckAbandonedStations(startElementIndex, lastAlternativeSectionIndex, actSubpath, abdStations);

            // continued path

            for (int i = lastAlternativeSectionIndex + 1; i < route.Count; i++)
            {
                newRoute.Add(route[i]);
            }

            // Reindexes ReversalInfo items
            int countDifference = newRoute.Count - ValidRoute[0].Count;
            if (TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastDivergeIndex >= 0)
                TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastDivergeIndex = TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastDivergeIndex + countDifference;
            if (TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastSignalIndex >= 0)
                TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastSignalIndex = TCRoute.ReversalInfo[TCRoute.ActiveSubPath].LastSignalIndex + countDifference;

            // set new route

            ValidRoute[0] = newRoute;
            TCRoute.TCRouteSubpaths[TCRoute.ActiveSubPath] = newRoute;

            // check for abandoned stations - try to find alternative on passing path
            LookForReplacementStations(abdStations, newRoute, altRoute);

            // set signal route
            // part upto split
            if (nextSignal != null)
            {
                TrackCircuitPartialPathRoute newSignalRoute = new TrackCircuitPartialPathRoute();

                int splitSignalIndex = nextSignal.SignalRoute.GetRouteIndex(route[startElementIndex].TrackCircuitSection.Index, 0);
                for (int i = 0; i < splitSignalIndex; i++)
                {
                    newSignalRoute.Add(nextSignal.SignalRoute[i]);
                }

                // extract new route upto next signal

                TrackCircuitPartialPathRoute nextPart = ExtractAlternativeRouteLocationBased(altRoute);
                foreach (TrackCircuitRouteElement thisElement in nextPart)
                {
                    newSignalRoute.Add(thisElement);
                }

                // set new signal route
                // reset signal
                // if train in signal mode, request clear signal

                nextSignal.ResetSignal(true);
                nextSignal.SignalRoute = newSignalRoute;

                if (ControlMode == TrainControlMode.AutoSignal)
                {
                    // keep any items allready passed
                    List<SignalItemInfo> keeplist = new List<SignalItemInfo>();
                    foreach (SignalItemInfo checkItem in SignalObjectItems)
                    {
                        float actualDistance = GetObjectDistanceToTrain(checkItem);
                        if (actualDistance < 0)
                        {
                            keeplist.Add(checkItem);
                        }
                    }

                    // create new list
                    InitializeSignals(true);

                    // add any passed items (in reverse order at start of list)
                    if (keeplist.Count > 0)
                    {
                        for (int i = keeplist.Count - 1; i >= 0; i--)
                        {
                            SignalObjectItems.Insert(0, keeplist[i]);
                        }
                    }

                    // find new next signal
                    NextSignalObject[0] = null;
                    for (int iObject = 0; iObject <= SignalObjectItems.Count - 1 && NextSignalObject[0] == null; iObject++)
                    {
                        if (SignalObjectItems[iObject].ItemType == SignalItemType.Signal)
                        {
                            NextSignalObject[0] = SignalObjectItems[iObject].SignalDetails;
                            DistanceToSignal = SignalObjectItems[iObject].DistanceToTrain;
                        }
                    }

                    NextSignalObject[0]?.RequestClearSignal(ValidRoute[0], RoutedForward, 0, false, null);
                }
            }
        }

        // Check for abandoned stations in the abandoned path
        private void CheckAbandonedStations(int startElementIndex, int lastAlternativeSectionIndex, int actSubpath, Dictionary<int, StationStop> abdStations)
        {
            int nextStationIndex = 0;


            if (StationStops != null && StationStops.Count > 0)
            {
                int stationRouteIndex = StationStops[nextStationIndex].RouteIndex;
                int stationSubpath = StationStops[nextStationIndex].SubrouteIndex;

                while (stationRouteIndex < lastAlternativeSectionIndex)
                {
                    if (stationSubpath == actSubpath && stationRouteIndex > startElementIndex)
                    {
                        abdStations.Add(nextStationIndex, StationStops[nextStationIndex]);
                    }

                    nextStationIndex++;
                    if (nextStationIndex > StationStops.Count - 1)
                    {
                        stationRouteIndex = lastAlternativeSectionIndex + 1;  // no more stations - set index beyond end
                    }
                    else
                    {
                        stationRouteIndex = StationStops[nextStationIndex].RouteIndex;
                        stationSubpath = StationStops[nextStationIndex].SubrouteIndex;
                        if (stationSubpath > actSubpath)
                        {
                            stationRouteIndex = lastAlternativeSectionIndex + 1; // no more stations in this subpath
                        }
                    }
                }
            }
        }

        // Look for stations in alternative route
        private void LookForReplacementStations(Dictionary<int, StationStop> abdStations, TrackCircuitPartialPathRoute newRoute, TrackCircuitPartialPathRoute altRoute)
        {

            if (StationStops != null)
            {
                List<StationStop> newStops = new List<StationStop>();
                int firstIndex = -1;

                foreach (KeyValuePair<int, StationStop> abdStop in abdStations)
                {
                    if (firstIndex < 0) firstIndex = abdStop.Key;
                    StationStop newStop = SetAlternativeStationStop(abdStop.Value, altRoute);
                    StationStops.RemoveAt(firstIndex);
                    if (newStop != null)
                    {
                        newStops.Add(newStop);
                    }
                }

                for (int i = newStops.Count - 1; i >= 0; i--)
                {
                    StationStops.Insert(firstIndex, newStops[i]);
                }

                // recalculate indices of all stops
                int prevIndex = 0;
                foreach (StationStop statStop in StationStops)
                {
                    statStop.RouteIndex = newRoute.GetRouteIndex(statStop.TrackCircuitSectionIndex, prevIndex);
                    prevIndex = statStop.RouteIndex;
                }
            }
        }

        // Find station on alternative route
        private protected virtual StationStop SetAlternativeStationStop(StationStop orgStop, TrackCircuitPartialPathRoute newRoute)
        {
            int altPlatformIndex = -1;

            // get station platform list
            if (Simulator.Instance.SignalEnvironment.StationXRefList.ContainsKey(orgStop.PlatformItem.Name))
            {
                List<int> XRefKeys = Simulator.Instance.SignalEnvironment.StationXRefList[orgStop.PlatformItem.Name];

                // search through all available platforms
                for (int platformIndex = 0; platformIndex <= XRefKeys.Count - 1 && altPlatformIndex < 0; platformIndex++)
                {
                    int platformXRefIndex = XRefKeys[platformIndex];
                    PlatformDetails altPlatform = Simulator.Instance.SignalEnvironment.PlatformDetailsList[platformXRefIndex];

                    // check if section is in new route
                    for (int iSectionIndex = 0; iSectionIndex <= altPlatform.TCSectionIndex.Count - 1 && altPlatformIndex < 0; iSectionIndex++)
                    {
                        if (newRoute.GetRouteIndex(altPlatform.TCSectionIndex[iSectionIndex], 0) > 0)
                        {
                            altPlatformIndex = platformXRefIndex;
                        }
                    }
                }

                // section found in new route - set new station details using old details
                if (altPlatformIndex > 0)
                {
                    StationStop newStop = CalculateStationStop(Simulator.Instance.SignalEnvironment.PlatformDetailsList[altPlatformIndex].PlatformReference[Location.NearEnd],
                        orgStop.ArrivalTime, orgStop.DepartTime, 15.0f);

                    return newStop;
                }
            }

            return null;
        }

        /// <summary>
        /// Create station stop (used in activity mode only)
        /// <\summary>
        private StationStop CalculateStationStop(int platformStartID, int arrivalTime, int departTime, float clearingDistanceM)
        {
            int activeSubroute = 0;

            TrackCircuitPartialPathRoute route = TCRoute.TCRouteSubpaths[activeSubroute];

            // get platform details

            if (!Simulator.Instance.SignalEnvironment.PlatformXRefList.TryGetValue(platformStartID, out int platformIndex))
            {
                return (null); // station not found
            }
            else
            {
                PlatformDetails platform = Simulator.Instance.SignalEnvironment.PlatformDetailsList[platformIndex];
                int sectionIndex = platform.TCSectionIndex[0];
                int routeIndex = route.GetRouteIndex(sectionIndex, 0);

                // if first section not found in route, try last

                if (routeIndex < 0)
                {
                    sectionIndex = platform.TCSectionIndex[platform.TCSectionIndex.Count - 1];
                    routeIndex = route.GetRouteIndex(sectionIndex, 0);
                }

                // if neither section found - try next subroute - keep trying till found or out of subroutes

                while (routeIndex < 0 && activeSubroute < (TCRoute.TCRouteSubpaths.Count - 1))
                {
                    activeSubroute++;
                    route = TCRoute.TCRouteSubpaths[activeSubroute];
                    routeIndex = route.GetRouteIndex(sectionIndex, 0);

                    // if first section not found in route, try last

                    if (routeIndex < 0)
                    {
                        sectionIndex = platform.TCSectionIndex[platform.TCSectionIndex.Count - 1];
                        routeIndex = route.GetRouteIndex(sectionIndex, 0);
                    }
                }

                // if neither section found - platform is not on route - skip

                if (routeIndex < 0)
                {
                    Trace.TraceWarning($"Train {Number} Service {Name} : platform {platformStartID} is not on route");
                    return null;
                }

                // determine end stop position depending on direction

                TrackCircuitRouteElement routeElement = route[routeIndex];

                int endSectionIndex = routeElement.Direction == 0 ? platform.TCSectionIndex[platform.TCSectionIndex.Count - 1] : platform.TCSectionIndex[0];
                int beginSectionIndex = routeElement.Direction == 0 ? platform.TCSectionIndex[0] : platform.TCSectionIndex[platform.TCSectionIndex.Count - 1];

                float endOffset = platform.TrackCircuitOffset[Location.FarEnd, routeElement.Direction];
                float beginOffset = platform.TrackCircuitOffset[Location.NearEnd, routeElement.Direction];

                float deltaLength = platform.Length - Length; // platform length - train length

                TrackCircuitSection endSection = TrackCircuitSection.TrackCircuitList[endSectionIndex];


                int firstRouteIndex = route.GetRouteIndex(beginSectionIndex, 0);
                if (firstRouteIndex < 0)
                    firstRouteIndex = routeIndex;
                int lastRouteIndex = route.GetRouteIndex(endSectionIndex, 0);
                if (lastRouteIndex < 0)
                    lastRouteIndex = routeIndex;
                float fullLength = platform.Length;


                // if train too long : search back for platform with same name
                if (deltaLength < 0)
                {
                    float actualBegin = beginOffset;

                    TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[beginSectionIndex];

                    // Other platforms in same section

                    if (section.PlatformIndices.Count > 1)
                    {
                        foreach (int i in section.PlatformIndices)
                        {
                            if (i != platformIndex)
                            {
                                PlatformDetails otherPlatform = Simulator.Instance.SignalEnvironment.PlatformDetailsList[i];
                                if (string.Compare(otherPlatform.Name, platform.Name, StringComparison.OrdinalIgnoreCase) == 0)
                                {
                                    int otherSectionIndex = routeElement.Direction == 0 ? otherPlatform.TCSectionIndex[0] : otherPlatform.TCSectionIndex[platform.TCSectionIndex.Count - 1];
                                    if (otherSectionIndex == beginSectionIndex)
                                    {
                                        if (otherPlatform.TrackCircuitOffset[Location.NearEnd, routeElement.Direction] < actualBegin)
                                        {
                                            actualBegin = otherPlatform.TrackCircuitOffset[Location.NearEnd, routeElement.Direction];
                                            fullLength = endOffset - actualBegin;
                                        }
                                    }
                                    else
                                    {
                                        int addRouteIndex = route.GetRouteIndex(otherSectionIndex, 0);
                                        float addOffset = otherPlatform.TrackCircuitOffset[Location.FarEnd, routeElement.Direction.Reverse()];
                                        // offset of begin in other direction is length of available track

                                        if (lastRouteIndex > 0)
                                        {
                                            float length = route.GetDistanceAlongRoute(addRouteIndex, addOffset, lastRouteIndex, endOffset, true);
                                            if (length > fullLength)
                                                fullLength = length;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    deltaLength = fullLength - Length;
                }

                // search back along route
                if (deltaLength < 0)
                {
                    float distance = fullLength + beginOffset;
                    bool platformFound = false;

                    for (int i = firstRouteIndex - 1; i >= 0 && distance < 500f && platformFound; i--)
                    {
                        TrackCircuitSection nextSection = route[i].TrackCircuitSection;

                        foreach (int otherPlatformIndex in nextSection.PlatformIndices)
                        {
                            PlatformDetails otherPlatform = Simulator.Instance.SignalEnvironment.PlatformDetailsList[otherPlatformIndex];
                            if (string.Compare(otherPlatform.Name, platform.Name, StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                fullLength = otherPlatform.Length + distance;
                                // we miss a little bit (offset) - that's because we don't know direction of other platform
                                platformFound = true; // only check for one more
                            }
                        }
                        distance += nextSection.Length;
                    }

                    deltaLength = fullLength - Length;
                }


                // determine stop position
                float stopOffset = endOffset - (0.5f * deltaLength);

                // beyond section : check for route validity (may not exceed route)
                if (stopOffset > endSection.Length)
                {
                    float addOffset = stopOffset - endSection.Length;
                    float overlap = 0f;

                    for (int i = lastRouteIndex; i < route.Count && overlap < addOffset; i++)
                    {
                        overlap += route[i].TrackCircuitSection.Length;
                    }

                    if (overlap < stopOffset)
                        stopOffset = overlap;
                }

                // check if stop offset beyond end signal - do not hold at signal

                int endSignal = -1;
                bool holdSignal = false;
                bool noWaitSignal = false;
                bool noClaimAllowed = false;

                // check if train is to reverse in platform
                // if so, set signal at other end as hold signal
                TrackDirection useDirection = routeElement.Direction;
                bool inDirection = true;

                if (TCRoute.ReversalInfo[activeSubroute].Valid)
                {
                    TrackCircuitReversalInfo reversal = TCRoute.ReversalInfo[activeSubroute];
                    int reversalIndex = reversal.SignalUsed ? reversal.LastSignalIndex : reversal.LastDivergeIndex;
                    if (reversalIndex >= 0 && reversalIndex <= lastRouteIndex) // reversal point is this section or earlier
                    {
                        useDirection = useDirection.Reverse();
                        inDirection = false;
                    }
                }

                // check for end signal
                if (platform.EndSignals[useDirection] >= 0)
                {
                    endSignal = platform.EndSignals[useDirection];

                    // stop location is in front of signal
                    if (inDirection)
                    {
                        if (platform.DistanceToSignals[useDirection] > (stopOffset - endOffset))
                        {
                            holdSignal = true;

                            if ((platform.DistanceToSignals[useDirection] + (endOffset - stopOffset)) < clearingDistanceM)
                            {
                                stopOffset = endOffset + platform.DistanceToSignals[useDirection] - clearingDistanceM - 1.0f;
                            }
                        }
                        // if most of train fits in platform then stop at signal
                        else if ((platform.DistanceToSignals[useDirection] - clearingDistanceM + platform.Length) > (0.6 * Length))
                        {
                            holdSignal = true;
                            stopOffset = endOffset + platform.DistanceToSignals[useDirection] - clearingDistanceM - 1.0f;
                            // set 1m earlier to give priority to station stop over signal
                        }
                        // train does not fit in platform - reset exit signal
                        else
                        {
                            endSignal = -1;
                        }
                    }
                    else
                    // end of train is beyond signal
                    {
                        if ((beginOffset - platform.DistanceToSignals[useDirection]) < (stopOffset - Length))
                        {
                            holdSignal = true;

                            if ((stopOffset - Length - beginOffset + platform.DistanceToSignals[useDirection]) < clearingDistanceM)
                            {
                                stopOffset = beginOffset - platform.DistanceToSignals[useDirection] + Length + clearingDistanceM + 1.0f;
                            }
                        }
                        // if most of train fits in platform then stop at signal
                        else if ((platform.DistanceToSignals[useDirection] - clearingDistanceM + platform.Length) > (0.6 * Length))
                        {
                            // set 1m earlier to give priority to station stop over signal
                            stopOffset = beginOffset - platform.DistanceToSignals[useDirection] + Length + clearingDistanceM + 1.0f;

                            // check if stop is clear of end signal (if any)
                            if (platform.EndSignals[routeElement.Direction] != -1)
                            {
                                if (stopOffset < (endOffset + platform.DistanceToSignals[routeElement.Direction]))
                                {
                                    holdSignal = true; // if train fits between signals
                                }
                                else
                                {
                                    stopOffset = endOffset + platform.DistanceToSignals[routeElement.Direction] - 1.0f; // stop at end signal
                                }
                            }
                        }
                        // train does not fit in platform - reset exit signal
                        else
                        {
                            endSignal = -1;
                        }
                    }
                }

                if (simulator.Settings.NoForcedRedAtStationStops)
                {
                    // We don't want reds at exit signal in this case
                    holdSignal = false;
                }

                // build and add station stop

                TrackCircuitRouteElement lastElement = route[lastRouteIndex];

                return new StationStop(
                        platformStartID,
                        platform,
                        activeSubroute,
                        lastRouteIndex,
                        lastElement.TrackCircuitSection.Index,
                        routeElement.Direction,
                        endSignal,
                        holdSignal,
                        noWaitSignal,
                        noClaimAllowed,
                        stopOffset,
                        arrivalTime,
                        departTime,
                        false,
                        null,
                        null,
                        null,
                        false,
                        false,
                        false,
                        false,
                        false,
                        false,
                        StationStopType.Station);
            }
        }

        /// <summary>
        /// Get other train from number
        /// Use Simulator.Trains to get other train
        /// </summary>
        internal static Train GetOtherTrainByNumber(int number)
        {
            return simulator.Trains.GetTrainByNumber(number);
        }

        /// <summary>
        /// Get other train from number
        /// Use Simulator.Trains to get other train
        /// </summary>
        internal static Train GetOtherTrainByName(string name)
        {
            return simulator.Trains.GetTrainByName(name);
        }

        /// <summary>
        /// Update AI Static state - dummy method to allow virtualization by child classes
        /// </summary>
        internal virtual void UpdateAIStaticState(int presentTime)
        {
        }

        /// <summary>
        /// Get AI Movement State - dummy method to allow virtualization by child classes
        /// </summary>
        internal virtual AiMovementState GetAiMovementState()
        {
            return AiMovementState.Unknown;
        }

        /// <summary>
        /// Check on station tasks, required when in timetable mode when there is no activity - dummy method to allow virtualization by child classes
        /// </summary>
        protected virtual void CheckStationTask()
        {
        }

        /// <summary>
        /// Special additional methods when stopped at signal in timetable mode - dummy method to allow virtualization by child classes
        /// </summary>
        protected virtual bool ActionsForSignalStop()
        {
            return true;
        }

        /// <summary>
        /// Check if train is in wait mode - dummy method to allow virtualization by child classes
        /// </summary>
        protected virtual bool InWaitState()
        {
            return false;
        }

        // Check if train has AnyWait valid for this section - dummy method to allow virtualization by child classes
        internal virtual bool CheckAnyWaitCondition(int index)
        {
            return false;
        }

        // Check if train has Wait valid for this section - dummy method to allow virtualization by child classes
        internal virtual bool HasActiveWait(int startSectionIndex, int endSectionIndex)
        {
            return false;
        }

        /// <summary>
        /// Update Section State - additional
        /// dummy method to allow virtualisation for Timetable trains
        /// </summary>
        protected virtual void UpdateSectionStateAdditional(int sectionIndex)
        {
        }

        /// <summary>
        /// Check wait condition
        /// Dummy method to allow virtualization by child classes
        /// <\summary>
        internal virtual bool CheckWaitCondition(int sectionIndex)
        {
            return false;
        }

        /// <summary>
        /// Check Pool Access
        /// Dummy method to allow virtualization by child classes
        /// <\summary>
        internal virtual bool CheckPoolAccess(int sectionIndex)
        {
            return false;
        }

        /// <summary>
        /// Clear moving table after moving table actions
        /// Dummy method to allow virtualization by child classes
        /// </summary>
        internal virtual void ClearMovingTable()
        {
        }

        /// <summary>
        /// TrainGetSectionStateClearNode
        /// Virtual method to allow differentiation by child classes
        /// </summary>
        internal virtual bool TrainGetSectionStateClearNode(int elementDirection, TrackCircuitPartialPathRoute routePart, TrackCircuitSection section)
        {
            return section.IsAvailable(this);
        }

        /// <summary>
        /// TestAbsDelay
        /// Tests if Waiting point delay >=30000 and <4000; under certain conditions this means that
        /// delay represents an absolute time of day, with format 3HHMM
        /// </summary>
        /// 
        internal virtual int TestAbsDelay(int delay, int correctedTime)
        {
            //TODO 20201128
            if (delay < 30000 || delay >= 40000)
                return delay;
            int hour = (delay / 100) % 100;
            int minute = delay % 100;
            int waitUntil = 60 * (minute + 60 * hour);
            int latest = Time.Compare.Latest(waitUntil, correctedTime);
            if (latest == waitUntil && waitUntil >= correctedTime)
                delay = waitUntil - correctedTime;
            else if (latest == correctedTime)
                delay = 1; // put 1 second delay if waitUntil is already over
            else
                delay = waitUntil - correctedTime + 3600 * 24; // we are over midnight here
            return delay;
        }

        /// <summary>
        /// ToggleDoors
        /// Toggles status of doors of a train
        /// Parameters: right = true if right doors; open = true if opening
        /// </summary>
        protected void ToggleDoors(bool right, bool open)
        {
            foreach (TrainCar car in Cars)
            {
                MSTSWagon mstsWagon = car as MSTSWagon;
                if (!car.Flipped && right || car.Flipped && !right)
                {
                    mstsWagon.DoorRightOpen = open;
                }
                else
                {
                    mstsWagon.DoorLeftOpen = open;
                }
                mstsWagon.SignalEvent(open ? TrainEvent.DoorOpen : TrainEvent.DoorClose); // hook for sound trigger
            }
        }

        /// <summary>
        /// Check if it's time to have a failed car or locomotive
        /// </summary>
        private void CheckFailures(double elapsedClockSeconds)
        {
            if (IsFreight)
                CheckBrakes(elapsedClockSeconds);
            CheckLocoPower(elapsedClockSeconds);
        }

        /// <summary>
        /// Check if it's time to have a car with stuck brakes
        /// </summary>
        private void CheckBrakes(double elapsedClockSeconds)
        {
            if (BrakingTime == -1)
                return;
            if (BrakingTime == -2)
            {
                BrakingTime = -1; // Viewer has seen it, can pass to this value
                return;
            }
            if (SpeedMpS > 0)
            {
                foreach (TrainCar car in Cars)
                {
                    if (!(car is MSTSLocomotive))
                    {
                        if (car.BrakeSystem.IsBraking() && BrakingTime >= 0)
                        {
                            BrakingTime += elapsedClockSeconds;
                            ContinuousBrakingTime += elapsedClockSeconds;
                            if (BrakingTime >= 1200.0 / simulator.Settings.ActRandomizationLevel || ContinuousBrakingTime >= 600.0 / simulator.Settings.ActRandomizationLevel)
                            {
                                int randInt = Simulator.Random.Next(200000);
                                bool brakesStuck = false;
                                if (randInt > 200000 - (simulator.Settings.ActRandomizationLevel == 1 ? 4 : simulator.Settings.ActRandomizationLevel == 2 ? 8 : 31))
                                // a car will have brakes stuck. Select which one
                                {
                                    int iBrakesStuckCar = Simulator.Random.Next(Cars.Count);
                                    int jBrakesStuckCar = iBrakesStuckCar;
                                    while (Cars[iBrakesStuckCar] is MSTSLocomotive && iBrakesStuckCar < Cars.Count)
                                        iBrakesStuckCar++;
                                    if (iBrakesStuckCar != Cars.Count)
                                    {
                                        brakesStuck = true;
                                    }
                                    else
                                    {
                                        while (Cars[jBrakesStuckCar] is MSTSLocomotive && jBrakesStuckCar > Cars.Count)
                                            jBrakesStuckCar--;
                                        if (jBrakesStuckCar != -1)
                                        {
                                            iBrakesStuckCar = jBrakesStuckCar;
                                            brakesStuck = true;
                                        }
                                    }
                                    if (brakesStuck)
                                    {
                                        Cars[iBrakesStuckCar].BrakesStuck = true;
                                        BrakingTime = -2; //Check no more, we already have a brakes stuck car
                                        ContinuousBrakingTime = -iBrakesStuckCar; // let's use it for two purposes
                                        simulator.Confirmer.Warning(Simulator.Catalog.GetString("Car " + Cars[iBrakesStuckCar].CarID + " has stuck brakes"));
                                    }
                                }
                            }
                        }
                        else
                            ContinuousBrakingTime = 0;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Check if it's time to have an electric or diesel loco with a bogie not powering
        /// </summary>
        private void CheckLocoPower(double elapsedClockSeconds)
        {
            if (RunningTime == -1)
                return;
            if (RunningTime == -2)
            {
                RunningTime = -1; // Viewer has seen it, can pass to this value
                return;
            }
            if (SpeedMpS > 0)
            {
                double oldRunningTime = RunningTime;
                RunningTime += elapsedClockSeconds;
                if (Math.Truncate(oldRunningTime) < Math.Truncate(RunningTime)) // Check only every second
                {
                    int nLocos = 0;
                    foreach (TrainCar car in Cars)
                    {
                        if ((car is MSTSElectricLocomotive || car is MSTSDieselLocomotive) && car.Parts.Count >= 2 &&
                            ((car as MSTSLocomotive).ThrottlePercent > 10 || (car as MSTSLocomotive).DynamicBrakePercent > 10))
                            nLocos++;
                    }
                    if (nLocos > 0)
                    {
                        int randInt = Simulator.Random.Next(2000000 / nLocos);
                        bool locoUnpowered;
                        if (randInt > 2000000 / nLocos - (simulator.Settings.ActRandomizationLevel == 1 ? 2 : simulator.Settings.ActRandomizationLevel == 2 ? 8 : 50))
                        // a loco will be partly or totally unpowered. Select which one
                        {
                            int iLocoUnpoweredCar = Simulator.Random.Next(Cars.Count);
                            int jLocoUnpoweredCar = iLocoUnpoweredCar;
                            if (iLocoUnpoweredCar % 2 == 1)
                            {
                                (locoUnpowered, iLocoUnpoweredCar) = SearchBackOfTrain(iLocoUnpoweredCar);
                                if (!locoUnpowered)
                                {
                                    iLocoUnpoweredCar = jLocoUnpoweredCar;
                                    (locoUnpowered, iLocoUnpoweredCar) = SearchFrontOfTrain(iLocoUnpoweredCar);
                                }

                            }
                            else
                            {
                                (locoUnpowered, iLocoUnpoweredCar) = SearchFrontOfTrain(iLocoUnpoweredCar);
                                if (!locoUnpowered)
                                {
                                    iLocoUnpoweredCar = jLocoUnpoweredCar;
                                    (locoUnpowered, iLocoUnpoweredCar) = SearchBackOfTrain(iLocoUnpoweredCar);
                                }
                            }

                            if (locoUnpowered)
                            {
                                RunningTime = -2; //Check no more, we already have an unpowered loco
                                MSTSLocomotive unpoweredLoco = Cars[iLocoUnpoweredCar] as MSTSLocomotive;
                                if (randInt % 2 == 1 || unpoweredLoco is MSTSElectricLocomotive)
                                {
                                    unpoweredLoco.PowerReduction = 0.5f;
                                    simulator.Confirmer.Warning(Simulator.Catalog.GetString($"Locomotive {unpoweredLoco.CarID} partial failure: 1 unpowered bogie"));
                                }
                                else
                                {
                                    unpoweredLoco.PowerReduction = 1.0f;
                                    simulator.Confirmer.Warning(Simulator.Catalog.GetString($"Locomotive {unpoweredLoco.CarID} compressor blown"));
                                }
                                UnpoweredLoco = iLocoUnpoweredCar;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Check first electric or diesel loco searching towards back of train
        /// </summary>
        private (bool unPowered, int index) SearchBackOfTrain(int startIndex)
        {
            bool locoUnpowered = false;
            while (startIndex < Cars.Count && !((Cars[startIndex] is MSTSElectricLocomotive || Cars[startIndex] is MSTSDieselLocomotive) && Cars[startIndex].Parts.Count >= 2))
                startIndex++;
            if (startIndex != Cars.Count)
            {
                locoUnpowered = true;
            }

            return (locoUnpowered, startIndex);
        }

        /// <summary>
        /// Check first electric or diesel loco searching towards front of train
        /// </summary>
        private (bool unPowered, int index) SearchFrontOfTrain(int startIndex)
        {
            bool locoUnpowered = false;
            while (startIndex >= 0 && !((Cars[startIndex] is MSTSElectricLocomotive || Cars[startIndex] is MSTSDieselLocomotive) && Cars[startIndex].Parts.Count >= 2))
                startIndex--;
            if (startIndex != -1)
            {
                locoUnpowered = true;
            }
            return (locoUnpowered, startIndex);
        }

        //================================================================================================//
        /// <summary>
        /// Routed train class : train class plus valid route direction indication
        /// Used throughout in the signalling process in order to derive correct route in Manual and Explorer modes
        /// </summary>

        public class TrainRouted
        {
            public Train Train { get; }
            public int TrainRouteDirectionIndex
            {
                get { return (int)Direction; }
                set { Direction = (Direction)value; }
            }
            public Direction Direction { get; private set; }

            //================================================================================================//
            /// <summary>
            /// Constructor
            /// </summary>

            public TrainRouted(Train thisTrain, int thisIndex)
            {
                Train = thisTrain;
                TrainRouteDirectionIndex = thisIndex;
            }
        }

        //used by remote train to update location based on message received
        private int expectedTileX, expectedTileZ, expectedTracIndex, expectedDIr, expectedTDir;
        private float expectedX, expectedZ, expectedTravelled, expectedLength;
        internal bool UpdateMSGReceived { get; set; }
        public bool RequestJump { get; internal set; } // set when a train jump has been requested by the server (when player re-enters game in old position
        private bool jumpRequested; // used in conjunction with above flag to manage thread safety
        private bool doReverseTrav; // reverse rear traveller in AI reversal points
        private int doReverseMU;

        internal void UpdateTrainJump(in WorldLocation location, int direction, float distanceTravelled, float maxSpeed)
        {
            expectedTileX = location.TileX;
            expectedTileZ = location.TileZ;
            expectedX = location.Location.X;
            expectedZ = location.Location.Z;
            expectedTDir = direction;
            expectedDIr = (int)MUDirection;
            expectedTravelled = DistanceTravelledM = DistanceTravelled = distanceTravelled;
            TrainMaxSpeedMpS = maxSpeed;
        }

        internal void ToDoUpdate(int tni, int tX, int tZ, float x, float z, float eT, float speed, int dir, int tDir, float len, bool reverseTrav = false,
            int reverseMU = 0)
        {
            SpeedMpS = speed;
            expectedTileX = tX;
            expectedTileZ = tZ;
            expectedX = x;
            expectedZ = z;
            expectedTravelled = eT;
            expectedTracIndex = tni;
            expectedDIr = dir;
            expectedTDir = tDir;
            expectedLength = len;
            if (reverseTrav)
            {
                doReverseTrav = true;
                doReverseMU = reverseMU;
            }
            UpdateMSGReceived = true;
        }

        private void UpdateCarSlack(float expectedLength)
        {
            if (Cars.Count <= 1)
                return;
            float staticLength = 0f;
            foreach (TrainCar car in Cars)
            {
                staticLength += car.CarLengthM;
            }
            staticLength = (expectedLength - staticLength) / (Cars.Count - 1);
            foreach (TrainCar car in Cars)//update slack for each car
            {
                car.CouplerSlackM = staticLength - car.GetCouplerZeroLengthM();
            }

        }

        public void UpdateRemoteTrainPos(double elapsedClockSeconds)
        {
            float newDistanceTravelledM = DistanceTravelledM;
            //           float xx = 0;

            if (UpdateMSGReceived)
            {
                UpdateMSGReceived = false;
                //try
                //{
                targetSpeedMpS = SpeedMpS;
                if (doReverseTrav)
                {
                    doReverseTrav = false;
                    ReverseFormation(doReverseMU == 1);
                    UpdateCarSlack(expectedLength);//update car slack first
                    CalculatePositionOfCars(elapsedClockSeconds, SpeedMpS * elapsedClockSeconds);
                    newDistanceTravelledM = DistanceTravelledM + (float)(SpeedMpS * elapsedClockSeconds);
                    MUDirection = (MidpointDirection)expectedDIr;
                }
                else
                {
                    UpdateCarSlack(expectedLength);//update car slack first

                    double x = DistanceTravelled + previousSpeedMpS * elapsedClockSeconds + (SpeedMpS - previousSpeedMpS) / 2 * elapsedClockSeconds;
                    //                    xx = x;
                    MUDirection = (MidpointDirection)expectedDIr;

                    if (Math.Abs(x - expectedTravelled) < 1 || Math.Abs(x - expectedTravelled) > 20)
                    {
                        CalculatePositionOfCars(elapsedClockSeconds, expectedTravelled - DistanceTravelled);
                        newDistanceTravelledM = DistanceTravelledM + expectedTravelled - DistanceTravelled;

                        //if something wrong with the switch
                        if (RearTDBTraveller.TrackNodeIndex != expectedTracIndex)
                        {
                            Traveller t = null;
                            if (expectedTracIndex <= 0)
                            {
                                t = new Traveller(simulator.TSectionDat, simulator.TDB.TrackDB.TrackNodes, new WorldLocation(expectedTileX, expectedTileZ, expectedX, 0, expectedZ), (Traveller.TravellerDirection)expectedTDir);
                            }
                            else
                            {
                                t = new Traveller(simulator.TSectionDat, simulator.TDB.TrackDB.TrackNodes, simulator.TDB.TrackDB.TrackNodes[expectedTracIndex] as TrackVectorNode, new WorldLocation(expectedTileX, expectedTileZ, expectedX, 0, expectedZ), (Traveller.TravellerDirection)expectedTDir);
                            }
                            //move = SpeedMpS > 0 ? 0.001f : -0.001f;
                            DistanceTravelled = expectedTravelled;
                            RearTDBTraveller = t;
                            CalculatePositionOfCars();

                        }
                    }
                    else//if the predicted location and reported location are similar, will try to increase/decrease the speed to bridge the gap in 1 second
                    {
                        SpeedMpS += (float)(expectedTravelled - x) / 1;
                        CalculatePositionOfCars(elapsedClockSeconds, SpeedMpS * elapsedClockSeconds);
                        newDistanceTravelledM = DistanceTravelledM + (float)(SpeedMpS * elapsedClockSeconds);
                    }
                    if (RequestJump)
                    {
                        jumpRequested = true;
                        RequestJump = false;
                    }
                }
                //}
                //catch (Exception)
                //{
                //}
                /*if (Math.Abs(requestedSpeed) < 0.00001 && Math.Abs(SpeedMpS) > 0.01) updateMSGReceived = true; //if requested is stop, but the current speed is still moving
                else*/

            }
            else//no message received, will move at the previous speed
            {
                CalculatePositionOfCars(elapsedClockSeconds, SpeedMpS * elapsedClockSeconds);
                newDistanceTravelledM = DistanceTravelledM + (float)(SpeedMpS * elapsedClockSeconds);
            }

            //update speed for each car, so wheels will rotate
            foreach (TrainCar car in Cars)
            {
                if (car != null)
                {
                    car.SpeedMpS = SpeedMpS;
                    if (car.Flipped)
                        car.SpeedMpS = -car.SpeedMpS;
                    car.AbsSpeedMpS = (float)(car.AbsSpeedMpS * (1 - elapsedClockSeconds) + targetSpeedMpS * elapsedClockSeconds);
                    if (car.IsDriveable && car is MSTSWagon mstsWagon)
                    {
                        mstsWagon.WheelSpeedMpS = SpeedMpS;
                        if (car.AbsSpeedMpS > 0.5f)
                        {
                            if (car is MSTSElectricLocomotive electricLocomotive)
                            {
                                electricLocomotive.Variable1 = 70;
                                electricLocomotive.Variable2 = 70;
                            }
                            else if (car is MSTSDieselLocomotive dieselLocomotive)
                            {
                                dieselLocomotive.Variable1 = 0.7f;
                                dieselLocomotive.Variable2 = 0.7f;
                            }
                            else if (car is MSTSSteamLocomotive steamLocomotive)
                            {
                                steamLocomotive.Variable1 = car.AbsSpeedMpS / car.DriverWheelRadiusM / MathHelper.Pi * 5;
                                steamLocomotive.Variable2 = 0.7f;
                            }
                        }
                        else if (car is MSTSLocomotive mstsLocomotive)
                        {
                            mstsLocomotive.Variable1 = 0;
                            mstsLocomotive.Variable2 = 0;
                        }
                    }
#if INDIVIDUAL_CONTROL
                if (car is MSTSLocomotive && car.CarID.StartsWith(MPManager.GetUserName()))
                        {
                            car.Update(elapsedClockSeconds);
                        }
#endif
                }
            }
            //            Trace.TraceWarning("SpeedMpS {0}  LastSpeedMpS {1}  AbsSpeedMpS {2}  targetSpeedMpS {7} x {3}  expectedTravelled {4}  travelled {5}  newDistanceTravelledM {6}",
            //                SpeedMpS, LastSpeedMpS, Cars[0].AbsSpeedMpS, xx, expectedTravelled, travelled, newDistanceTravelledM, targetSpeedMpS);
            previousSpeedMpS = SpeedMpS;
            DistanceTravelledM = newDistanceTravelledM;

            //Orient();
            return;

        }

        /// <summary>
        /// Nullify valid routes
        /// </summary>
        private void ClearValidRoutes()
        {

            if (ValidRoute[0] != null)
            {
                int listIndex = PresentPosition[Direction.Forward].RouteListIndex;
                Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[0], listIndex, RoutedForward);
                ClearDeadlocks();
            }

            ValidRoute[0] = null;
            LastReservedSection[0] = -1;

            if (ValidRoute[1] != null)
            {
                int listIndex = PresentPosition[Direction.Backward].RouteListIndex;
                Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[1], listIndex, RoutedBackward);
            }
            ValidRoute[1] = null;
            LastReservedSection[1] = -1;
        }

        /// <summary>
        /// Clears reserved sections (used after manual switching)
        /// </summary>
        private void ClearReservedSections()
        {

            if (ValidRoute[0] != null)
            {
                int listIndex = PresentPosition[Direction.Forward].RouteListIndex;
                Simulator.Instance.SignalEnvironment.BreakDownRouteList(ValidRoute[0], listIndex, RoutedForward);
                ClearDeadlocks();
            }

        }


        /// <summary>
        /// After turntable rotation, must find where it is
        /// </summary>
        /// 
        internal void ReenterTrackSections(int trackNodeIndex, Vector3 finalFrontTravellerXNALocation, Vector3 finalRearTravellerXNALocation, Traveller.TravellerDirection direction)
        {
            FrontTDBTraveller = new Traveller(simulator.TSectionDat, simulator.TDB.TrackDB.TrackNodes, simulator.TDB.TrackDB.TrackNodes[trackNodeIndex],
                 Cars[0].WorldPosition.TileX, Cars[0].WorldPosition.TileZ, finalFrontTravellerXNALocation.X, -finalFrontTravellerXNALocation.Z, FrontTDBTraveller.Direction);
            RearTDBTraveller = new Traveller(simulator.TSectionDat, simulator.TDB.TrackDB.TrackNodes, simulator.TDB.TrackDB.TrackNodes[trackNodeIndex],
                Cars[0].WorldPosition.TileX, Cars[0].WorldPosition.TileZ, finalRearTravellerXNALocation.X, -finalRearTravellerXNALocation.Z, RearTDBTraveller.Direction);
            if (direction == Traveller.TravellerDirection.Backward)
            {
                FrontTDBTraveller.ReverseDirection();
                RearTDBTraveller.ReverseDirection();
            }

            ClearValidRoutes();
            PresentPosition[Direction.Forward].TrackCircuitSectionIndex = -1;
            TrackCircuitPartialPathRoute tempRoute = CalculateInitialTrainPosition();
            if (tempRoute.Count == 0)
            {
                throw new InvalidDataException("Position of train in turntable not clear");
            }

            SetInitialTrainRoute(tempRoute);
            CalculatePositionOfCars();
            ResetInitialTrainRoute(tempRoute);

            CalculatePositionOfCars();

            TrackNode tn = FrontTDBTraveller.TN;
            float offset = FrontTDBTraveller.TrackNodeOffset;
            TrackDirection direction1 = (TrackDirection)FrontTDBTraveller.Direction;

            PresentPosition[Direction.Forward].SetPosition(tn.TrackCircuitCrossReferences, offset, direction1);
            PreviousPosition[Direction.Forward].UpdateFrom(PresentPosition[Direction.Forward]);

            if (TrainType == TrainType.Static)
            {
                ControlMode = TrainControlMode.Undefined;
                return;
            }

            if (simulator.Activity == null && !simulator.TimetableMode)
                ToggleToExplorerMode();
            else
                ToggleToManualMode();
            simulator.Confirmer.Confirm(CabControl.SignalMode, CabSetting.Off);
        }

        private static Direction MidPointDirectionToDirectionUnset(MidpointDirection midpointDirection)
        {
            return midpointDirection == MidpointDirection.Forward ? Direction.Forward :
                midpointDirection == MidpointDirection.Reverse ? Direction.Backward : (Direction)(-1);
        }

        /// <summary>
        /// returns if position is ahead of train
        /// <\summary>
        // without offset
        internal static bool IsAheadOfTrain(TrackCircuitSection section, TrackCircuitPosition position)
        {
            return IsAheadOfTrain(section, 0f, position);
        }

        // with offset
        internal static bool IsAheadOfTrain(TrackCircuitSection section, float offset, TrackCircuitPosition position)
        {
            if (null == section)
                throw new ArgumentNullException(nameof(section));
            if (null == position)
                throw new ArgumentNullException(nameof(position));

            float distanceAhead = TrackCircuitSection.GetDistanceBetweenObjects(
                position.TrackCircuitSectionIndex, position.Offset, position.Direction, section.Index, offset);
            return (distanceAhead > 0.0f);
        }

        internal bool IsMoving()
        {
            return PresentPosition[Direction.Forward].Offset != PreviousPosition[Direction.Forward].Offset;
        }
    }
    // class Train
}
