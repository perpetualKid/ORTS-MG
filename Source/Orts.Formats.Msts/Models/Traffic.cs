using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Orts.Formats.Msts.Files;
using Orts.Formats.Msts.Parsers;

using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace Orts.Formats.Msts.Models
{
    /// <summary>
    /// Parses Traffic Definitions in Traffic File
    /// </summary>
    public class ServiceTraffic
    {
        public string Name { get; private set; }
        public int Serial { get; private set; }
        public IList<ServiceTraffics> ServiceTraffics { get; } = new List<ServiceTraffics>();

        internal ServiceTraffic(STFReader stf)
        {
            stf.MustMatchBlockStart();
            Name = stf.ReadString();
            stf.MustMatch("serial");
            Serial = stf.ReadIntBlock(null);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("service_definition", ()=>{ ServiceTraffics.Add(new ServiceTraffics(stf)); }),
            });
        }
    }

    /// <summary>
    /// Parses Traffic Definition Items in Traffic Definitions in Traffic File
    /// </summary>
    public class ServiceTraffics : List<ServiceTrafficItem>
    {
        public string Name { get; private set; }
        public int Time { get; private set; }

        public ServiceTraffics(int serviceTime)
        {
            Time = serviceTime;
        }

        internal ServiceTraffics(STFReader stf)
        {
            int arrivalTime = 0;
            int departTime = 0;
            int skipCount = 0;
            float distanceDownPath = 0f;
            int platformStartID = 0;

            stf.MustMatchBlockStart();
            Name = stf.ReadString();
            Time = stf.ReadInt(null);   // Cannot use stt.ReadFloat(STFReader.Units.Time, null) as number will be followed by "arrivaltime"
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("arrivaltime", ()=>{ arrivalTime = (int)stf.ReadFloatBlock(STFReader.Units.Time, null); }),
                new STFReader.TokenProcessor("departtime", ()=>{ departTime = (int)stf.ReadFloatBlock(STFReader.Units.Time, null); }),
                new STFReader.TokenProcessor("skipcount", ()=>{ skipCount = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("distancedownpath", ()=>{ distanceDownPath = stf.ReadFloatBlock(STFReader.Units.Distance, null); }),
                new STFReader.TokenProcessor("platformstartid", ()=>{ platformStartID = stf.ReadIntBlock(null);
                    Add(new ServiceTrafficItem(arrivalTime, departTime, skipCount, distanceDownPath, platformStartID, this));
                }),
            });
        }

        // This is used to convert the player data taken from the .act file into a traffic service definition for autopilot mode
        public ServiceTraffics(string serviceName, PlayerTraffics playerTraffic)
        {
            Name = serviceName;
            Time = playerTraffic?.Time ?? throw new ArgumentNullException(nameof(playerTraffic));

            AddRange(playerTraffic);
        }

    }

    public class ServiceTrafficItem
    {
        public int ArrivalTime { get; private set; }
        public int DepartTime { get; private set; }
        public int SkipCount { get; private set; }
        public float DistanceDownPath { get; private set; }
        public int PlatformStartID { get; private set; }

        public ServiceTrafficItem(int arrivalTime, int departTime, int skipCount, float distanceDownPath, int platformStartID)
        {
            ArrivalTime = arrivalTime;
            DepartTime = departTime;
            SkipCount = skipCount;
            DistanceDownPath = distanceDownPath;
            PlatformStartID = platformStartID;
        }

        public ServiceTrafficItem(int arrivalTime, int departTime, int skipCount, float distanceDownPath, int platformStartID, ServiceTraffics parent)
            : this(arrivalTime, departTime, skipCount, distanceDownPath, platformStartID)

        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            if (arrivalTime < 0)
            {
                ArrivalTime = departTime < 0 ? parent.Time : Math.Min(departTime, parent.Time);
                Trace.TraceInformation($"Train Service {parent.Name} : Corrected negative arrival time within .trf or .act file");
            }
            if (departTime < 0)
            {
                DepartTime = Math.Max(ArrivalTime, parent.Time);
                Trace.TraceInformation($"Train Service {parent.Name} : Corrected negative depart time within .trf or .act file");
            }
        }
    }

    public class TrafficItem
    {
        public float Efficiency { get; private set; }
        public int SkipCount { get; private set; }
        public float DistanceDownPath { get; private set; }
        public int PlatformStartID { get; private set; }

        public TrafficItem(float efficiency, int skipCount, float distanceDownPath, int platformStartID)
        {
            Efficiency = efficiency;
            SkipCount = skipCount;
            DistanceDownPath = distanceDownPath;
            PlatformStartID = platformStartID;
        }

        public void SetAlternativeStationStop(int platformStartId)
        {
            PlatformStartID = platformStartId;
        }
    }

    public class Services : List<TrafficItem>
    {
        public string Name { get; private set; }
        public int Time { get; private set; }
        public int UiD { get; private set; }

        private float efficiency;
        private int skipCount;
        private float distanceDownPath;
        private int platformStartID;

        internal Services(STFReader stf)
        {
            stf.MustMatchBlockStart();
            Name = stf.ReadString();
            Time = (int)stf.ReadFloat(STFReader.Units.Time, null);
            stf.MustMatch("uid");
            UiD = stf.ReadIntBlock(null);
            // Clumsy parsing. You only get a new Service_Item in the list after a PlatformStartId is met.
            // Blame lies with Microsoft for poor design of syntax.
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("efficiency", ()=>{ efficiency = stf.ReadFloatBlock(STFReader.Units.Any, null); }),
                new STFReader.TokenProcessor("skipcount", ()=>{ skipCount = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("distancedownpath", ()=>{ distanceDownPath = stf.ReadFloatBlock(STFReader.Units.Distance, null); }),
                new STFReader.TokenProcessor("platformstartid", ()=>{ platformStartID = stf.ReadIntBlock(null);
                    Add(new TrafficItem(efficiency, skipCount, distanceDownPath, platformStartID)); }),
            });
        }

        // This is used to convert the player traffic definition into an AI train service definition for autopilot mode
        public Services(string serviceName, PlayerTraffics playerTraffic)
        {
            Name = serviceName;
            Time = playerTraffic?.Time ?? throw new ArgumentNullException(nameof(playerTraffic));
            UiD = 0;

            AddRange(playerTraffic.ConvertAll(x => new TrafficItem(0.95f, x.SkipCount, x.DistanceDownPath, x.PlatformStartID)));
        }

        public Services()
        { }

        public void Save(BinaryWriter outf)
        {
            if (null == outf)
                throw new ArgumentNullException(nameof(outf));

            if (Count == 0)
            {
                outf.Write(-1);
            }
            else
            {
                outf.Write(Count);
                foreach (TrafficItem traffic in this)
                {
                    outf.Write(traffic.Efficiency);
                    outf.Write(traffic.PlatformStartID);
                }
            }
        }
    }

    /// <summary>
    /// Parses Service_Definition objects and saves them in ServiceDefinitionList.
    /// </summary>
    public class Traffic
    {
        public string Name { get; private set; }
        public TrafficFile TrafficFile { get; private set; }
        public IList<Services> Services { get; } = new List<Services>();

        internal Traffic(STFReader stf)
        {
            stf.MustMatchBlockStart();
            Name = stf.ReadString();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("service_definition", ()=>{ Services.Add(new Services(stf)); }),
            });

            //            TrafficFile = new TrafficFile(FolderStructure.TrafficFile(Name));
            TrafficFile = new TrafficFile(FolderStructure.RouteFromActivity(stf.FileName).TrafficFile(Name));

        }
    }

    public class PlayerTraffics : List<ServiceTrafficItem>
    {
        public int Time { get; private set; }

        internal PlayerTraffics(STFReader stf)
        {
            int arrivalTime = 0;
            int departTime = 0;
            int skipCount = 0;
            float distanceDownPath = 0f;
            int platformStartID;

            stf.MustMatchBlockStart();
            Time = (int)stf.ReadFloat(STFReader.Units.Time, null);
            // Clumsy parsing. You only get a new Player_Traffic_Item in the list after a PlatformStartId is met.
            // Blame lies with Microsoft for poor design of syntax.
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("arrivaltime", ()=>{ arrivalTime = (int)stf.ReadFloatBlock(STFReader.Units.Time, null); }),
                new STFReader.TokenProcessor("departtime", ()=>{ departTime = (int)stf.ReadFloatBlock(STFReader.Units.Time, null); }),
                new STFReader.TokenProcessor("skipcount", ()=>{ skipCount = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("distancedownpath", ()=>{ distanceDownPath = stf.ReadFloatBlock(STFReader.Units.Distance, null); }),
                new STFReader.TokenProcessor("platformstartid", ()=>{ platformStartID = stf.ReadIntBlock(null);
                    Add(new ServiceTrafficItem(arrivalTime, departTime, skipCount, distanceDownPath, platformStartID)); }),
            });
        }

        // Used for explore in activity mode
        public PlayerTraffics(int startTime)
        {
            Time = startTime;
        }
    }

    public class PlayerServices
    {
        public string Name { get; private set; }
        public PlayerTraffics PlayerTraffics { get; private set; }

        internal PlayerServices(STFReader stf)
        {
            stf.MustMatchBlockStart();
            Name = stf.ReadString();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("player_traffic_definition", ()=>{ PlayerTraffics = new PlayerTraffics(stf); }),
            });
        }

        // Used for explore in activity mode
        public PlayerServices(int startTime, string name)
        {
            Name = name;
            PlayerTraffics = new PlayerTraffics(startTime);
        }
    }

}
