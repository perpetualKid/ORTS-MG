using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Orts.Formats.Msts;
using Orts.Formats.Msts.Files;
using Orts.Formats.Msts.Models;

namespace Orts.TrackViewer
{
    public class TrackData
    {
        private readonly string routePath;

        public string RouteName { get; private set; }

        public TrackDB TrackDB { get; private set; }

        public RoadTrackDB RoadTrackDB { get; private set; }

        public TrackSectionsFile TrackSections { get; private set; }

        public SignalConfigurationFile SignalConfig { get; private set; }

        public bool UseMetricUnits { get; private set; }

        public TrackData(string routePath)
        {
            this.routePath = routePath;
        }

        internal async Task LoadTrackData(bool? useMetricUnits, CancellationToken cancellationToken)
        {
            List<Task> loadTasks = new List<Task>();

            FolderStructure.ContentFolder.RouteFolder routeFolder = FolderStructure.Route(routePath);
            RouteFile routeFile = new RouteFile(routeFolder.TrackFileName);
            RouteName = routeFile.Route.Name;
            UseMetricUnits = useMetricUnits.GetValueOrDefault(routeFile.Route.MilepostUnitsMetric);

            loadTasks.Add(Task.Run(() =>
            {
                string tdbFile = routeFolder.TrackDatabaseFile(routeFile.Route.FileName);
                if (!File.Exists(tdbFile))
                {
                    Trace.TraceError($"Track Database File not found in {tdbFile}");
                    return;
                }
                TrackDB = new TrackDatabaseFile(tdbFile).TrackDB;
            }, cancellationToken));
            loadTasks.Add(Task.Run(() =>
            {
                TrackSections = new TrackSectionsFile(routeFolder.TrackSectionFile);
                if (File.Exists(routeFolder.RouteTrackSectionFile))
                    TrackSections.AddRouteTSectionDatFile(routeFolder.RouteTrackSectionFile);
            }, cancellationToken));
            loadTasks.Add(Task.Run(() =>
            {
                string rdbFile = routeFolder.RoadTrackDatabaseFile(routeFile.Route.FileName);
                if (!File.Exists(rdbFile))
                {
                    Trace.TraceError($"Road Database File not found in {rdbFile}");
                    return;
                }
                RoadTrackDB = new RoadDatabaseFile(rdbFile).RoadTrackDB;
            }, cancellationToken));
            loadTasks.Add(Task.Run(() => SignalConfig = new SignalConfigurationFile(routeFolder.SignalConfigurationFile, routeFolder.ORSignalConfigFile), cancellationToken));

            await Task.WhenAll(loadTasks).ConfigureAwait(false);
        }
    }
}
