using System.Collections.Generic;
using System.Diagnostics;

using Orts.Common;
using Orts.Common.Calc;
using Orts.Common.Position;
using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Models
{
    public class Route
    {
        internal Route(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("routeid", ()=>{ RouteID = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("name", ()=>{ Name = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("filename", ()=>{ FileName = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("description", ()=>{ Description = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("maxlinevoltage", ()=>{ MaxLineVoltage = stf.ReadFloatBlock(STFReader.Units.None, null); }),
                new STFReader.TokenProcessor("routestart", ()=>{ RouteStart = new RouteStart(stf); }),
                new STFReader.TokenProcessor("environment", ()=>{ Environment = new Environment(stf); }),
                new STFReader.TokenProcessor("milepostunitskilometers", ()=>{ MilepostUnitsMetric = true; }),
                new STFReader.TokenProcessor("electrified", ()=>{ Electrified = stf.ReadBoolBlock(false); }),
                new STFReader.TokenProcessor("overheadwireheight", ()=>{ OverheadWireHeight = stf.ReadFloatBlock(STFReader.Units.Distance, 6.0f);}),
                new STFReader.TokenProcessor("speedlimit", ()=>{ SpeedLimit = stf.ReadFloatBlock(STFReader.Units.Speed, 500.0f); }),
                new STFReader.TokenProcessor("defaultcrossingsms", ()=>{ DefaultCrossingSMS = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("defaultcoaltowersms", ()=>{ DefaultCoalTowerSMS = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("defaultdieseltowersms", ()=>{ DefaultDieselTowerSMS = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("defaultwatertowersms", ()=>{ DefaultWaterTowerSMS = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("defaultsignalsms", ()=>{ DefaultSignalSMS = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("temprestrictedspeed", ()=>{ TempRestrictedSpeed = stf.ReadFloatBlock(STFReader.Units.Speed, -1f); }),
                // values for tunnel operation
                new STFReader.TokenProcessor("ortssingletunnelarea", ()=>{ SingleTunnelAreaM2 = stf.ReadFloatBlock(STFReader.Units.AreaDefaultFT2, null); }),
                new STFReader.TokenProcessor("ortssingletunnelperimeter", ()=>{ SingleTunnelPerimeterM = stf.ReadFloatBlock(STFReader.Units.Distance, null); }),
                new STFReader.TokenProcessor("ortsdoubletunnelarea", ()=>{ DoubleTunnelAreaM2 = stf.ReadFloatBlock(STFReader.Units.AreaDefaultFT2, null); }),
                new STFReader.TokenProcessor("ortsdoubletunnelperimeter", ()=>{ DoubleTunnelPerimeterM = stf.ReadFloatBlock(STFReader.Units.Distance, null); }),
                // if > 0 indicates distance from track without forest trees
				new STFReader.TokenProcessor("ortsuserpreferenceforestcleardistance", ()=>{ ForestClearDistance = stf.ReadFloatBlock(STFReader.Units.Distance, 0); }),
                // if true removes forest trees also from roads
				new STFReader.TokenProcessor("ortsuserpreferenceremoveforesttreesfromroads", ()=>{ RemoveForestTreesFromRoads = stf.ReadBoolBlock(false); }),
                // values for superelevation
                new STFReader.TokenProcessor("ortstracksuperelevation", ()=>{ SuperElevationHgtpRadiusM = stf.CreateInterpolator(); }),
                // images
                new STFReader.TokenProcessor("graphic", ()=>{ Thumbnail = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("loadingscreen", ()=>{ LoadingScreen = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("ortsloadingscreenwide", ()=>{ LoadingScreenWide = stf.ReadStringBlock(null); }),
                 // values for OHLE
                new STFReader.TokenProcessor("ortsdoublewireenabled", ()=>{ DoubleWireEnabled = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("ortsdoublewireheight", ()=>{ DoubleWireHeight = stf.ReadFloatBlock(STFReader.Units.Distance, null); }),
                new STFReader.TokenProcessor("ortstriphaseenabled", ()=>{ TriphaseEnabled = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("ortstriphasewidth", ()=>{ TriphaseWidth = stf.ReadFloatBlock(STFReader.Units.Distance, null); }),
                // default sms file for turntables and transfertables
                new STFReader.TokenProcessor("ortsdefaultturntablesms", ()=>{ DefaultTurntableSMS = stf.ReadStringBlock(null); }),
                // sms file number in Ttype.dat when train over switch
                new STFReader.TokenProcessor("ortsswitchsmsnumber", ()=>{ SwitchSMSNumber = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("ortscurvesmsnumber", ()=>{ CurveSMSNumber = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("ortscurveswitchsmsnumber", ()=>{ CurveSwitchSMSNumber = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("ortsopendoorsinaitrains", ()=>{ OpenDoorsInAITrains = stf.ReadBoolBlock(false); }),

           });
            //TODO This should be changed to STFException.TraceError() with defaults values created
            if (RouteID == null)
                throw new STFException(stf, "Missing RouteID");
            if (Name == null)
                throw new STFException(stf, "Missing Name");
            if (Description == null)
                throw new STFException(stf, "Missing Description");
            if (RouteStart == null)
                throw new STFException(stf, "Missing RouteStart");
            if (ForestClearDistance == 0 && RemoveForestTreesFromRoads)
                Trace.TraceWarning("You must define also ORTSUserPreferenceForestClearDistance to avoid trees on roads");
        }

        public string RouteID { get; private set; }  // ie JAPAN1  - used for TRK file and route folder name
        public string FileName { get; private set; } // ie OdakyuSE - used for MKR,RDB,REF,RIT,TDB,TIT
        public string Name { get; private set; }
        public string Description { get; private set; }
        public RouteStart RouteStart { get; private set; }
        public Environment Environment { get; private set; }
        public bool MilepostUnitsMetric { get; private set; }
        public double MaxLineVoltage { get; private set; }
        public bool Electrified { get; private set; } = true;
        public double OverheadWireHeight { get; private set; } = 6.0;
        public double SpeedLimit { get; private set; } = 500.0f; //global speed limit m/s.
        public string DefaultCrossingSMS { get; private set; }
        public string DefaultCoalTowerSMS { get; private set; }
        public string DefaultDieselTowerSMS { get; private set; }
        public string DefaultWaterTowerSMS { get; private set; }
        public string DefaultSignalSMS { get; private set; }
        public float TempRestrictedSpeed { get; private set; } = -1f;
        public Interpolator SuperElevationHgtpRadiusM { get; private set; } // Superelevation of tracks

        // Values for calculating Tunnel Resistance - will override default values.
        public float SingleTunnelAreaM2 { get; private set; }
        public float SingleTunnelPerimeterM { get; private set; }
        public float DoubleTunnelAreaM2 { get; private set; }
        public float DoubleTunnelPerimeterM { get; private set; }

        public float ForestClearDistance { get; private set; }
        public bool RemoveForestTreesFromRoads { get; private set; } 

        // images
        public string Thumbnail { get; private set; }
        public string LoadingScreen { get; private set; }
        public string LoadingScreenWide { get; private set; }

        // Values for OHLE
        public string DoubleWireEnabled { get; private set; }
        public float DoubleWireHeight { get; private set; }
        public string TriphaseEnabled { get; private set; }
        public float TriphaseWidth { get; private set; }

        public string DefaultTurntableSMS { get; private set; }
        public bool? OpenDoorsInAITrains { get; private set; } // true if option active

        public int SwitchSMSNumber { get; private set; } = -1; // defines the number of the switch SMS files in file ttypedat
        public int CurveSMSNumber { get; private set; } = -1; // defines the number of the curve SMS files in file ttype.dat
        public int CurveSwitchSMSNumber { get; private set; } = -1; // defines the number of the curve-switch SMS files in file ttype.dat

    }

    public class RouteStart
    {
        private readonly WorldLocation location;

        public ref readonly WorldLocation Location => ref location;

        internal RouteStart(STFReader stf)
        {
            stf.MustMatchBlockStart();
            location = new WorldLocation(stf.ReadInt(null), stf.ReadInt(null), stf.ReadFloat(null), 0f, stf.ReadFloat(null));
            stf.SkipRestOfBlock();
        }
    }

    /// <summary>
    /// Environment is the combination of Season and Weather, hence there should be 12 entries always
    /// </summary>
    public class Environment
    {
        private readonly Dictionary<string, string> fileNames = new Dictionary<string, string>();

        internal Environment(STFReader stf)
        {
            stf.MustMatchBlockStart();
            for (int i = 0; i < 12; ++i)
            {
                string envfilekey = stf.ReadString();
                string envfile = stf.ReadStringBlock(null);
                fileNames.Add(envfilekey, envfile);
            }
            stf.SkipRestOfBlock();
        }

        public string GetEnvironmentFileName(SeasonType seasonType, WeatherType weatherType)
        {
            string envfilekey = seasonType.ToString() + weatherType.ToString();
            return fileNames[envfilekey];
        }
    }
}
