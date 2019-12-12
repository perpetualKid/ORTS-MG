
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Orts.Formats.Msts
{
    public static class FolderStructure
    {
        public class ContentFolder
        {
            public class RouteFolder
            {
                private readonly string routeName;
                private readonly string routeFolder;

                internal RouteFolder(string route, string folder)
                {
                    routeName = route;
                    routeFolder = Path.Combine(folder, routeName);
                }

                public string TrackFileName
                {
                    get
                    {
                        string[] trackFiles = Directory.GetFiles(routeFolder, "*.trk");
                        if (trackFiles.Length == 0)
                            throw new FileNotFoundException($"TRK file not found in '{routeFolder}'");
                        return trackFiles[0];
                    }
                }

                public string ActivitiesFolder => Path.Combine(routeFolder, "ACTIVITIES");

                public string OrActivitiesFolder => Path.Combine(routeFolder, "ACTIVITIES", "OpenRails");

                public string WeatherFolder => Path.Combine(routeFolder, "WeatherFiles");

                public string PathsFolder => Path.Combine(routeFolder, "PATHS");

                public string ServicesFolder => Path.Combine(routeFolder, "SERVICES");

                public string TrafficFolder => Path.Combine(routeFolder, "TRAFFIC");

                public string SoundsFolder => Path.Combine(routeFolder, "SOUND");

                public string ServiceFile(string serviceName)
                {
                    return Path.Combine(ServicesFolder, serviceName + ".srv");
                }

                public string PathFile(string pathName)
                {
                    return Path.Combine(PathsFolder, pathName+ ".pat");
                }

                public string TrafficFile(string trafficName)
                {
                    return Path.Combine(TrafficFolder, trafficName + ".trf");
                }

                public string SoundFile(string soundName)
                {
                    return Path.Combine(SoundsFolder, soundName);
                }

            }

            private readonly Dictionary<string, RouteFolder> routeFolders = new Dictionary<string, RouteFolder>(StringComparer.OrdinalIgnoreCase);

            internal ContentFolder(string root)
            {
                Folder = Path.GetFullPath(root);
            }

            public string Folder { get; }

            public string RoutesFolder => Path.Combine(Folder, "ROUTES");

            public string ConsistsFolder => Path.Combine(Folder, "TRAINS", "Consists");

            public string TrainSetsFolder => Path.Combine(Folder, "TRAINS", "TrainSet");

            public string ConsistFile(string consistName)
            {
                return Path.Combine(ConsistsFolder, consistName + ".con");
            }

            public string EngineFile(string trainSetName, string engineName)
            {
                return Path.Combine(TrainSetsFolder, trainSetName, engineName + ".eng");
            }

            public string WagonFile(string trainSetName, string wagonName)
            {
                return Path.Combine(TrainSetsFolder, trainSetName, wagonName + ".wag");
            }

            public RouteFolder Route(string route)
            {
                if (!routeFolders.ContainsKey(route))
                {
                    lock (routeFolders)
                    {
                        if (!routeFolders.ContainsKey(route))
                            routeFolders.Add(route, new RouteFolder(route, RoutesFolder));
                    }
                }
                return routeFolders[route];
            }

        }

        private static readonly string mstsLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Games", "Train Simulator");   // MSTS default path.
        private static readonly Dictionary<string, ContentFolder> contentFolders = new Dictionary<string, ContentFolder>(StringComparer.OrdinalIgnoreCase);
        private static ContentFolder current;

        public static ContentFolder Current
        {
            get
            {
                if (null == current)
                    current = Content(MstsFolder);
                return current;
            }
        }

        public static string MstsFolder
        {
            get
            {
                RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft Games\Train Simulator\1.0");
                if (key == null)
                    key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Microsoft\Microsoft Games\Train Simulator\1.0");
                DirectoryInfo mstsFolder = new DirectoryInfo((string)key?.GetValue("Path", mstsLocation) ?? mstsLocation);

                // Verify installation at this location
                if (!mstsFolder?.Exists ?? true)
                    Trace.TraceInformation($"MSTS directory '{mstsLocation}' does not exist.");
                return mstsFolder.FullName;
            }
        }

        public static ContentFolder Content(string root)
        {
            if (!contentFolders.ContainsKey(root))
            {
                lock (contentFolders)
                {
                    if (!contentFolders.ContainsKey(root))
                        contentFolders.Add(root, new ContentFolder(root));
                }
            }
            return contentFolders[root];
        }

        public static ContentFolder.RouteFolder Route(string routePath)
        {
            string routeName = Path.GetFileName(routePath);
            string contentFolder = Path.GetFullPath(Path.Combine(routePath, "..\\.."));
            return Content(contentFolder).Route(routeName);
        }

        public static ContentFolder.RouteFolder RouteFromActivity(string activityPath)
        {
            string routePath = Path.GetFullPath(Path.Combine(activityPath, "..\\.."));
            string routeName = Path.GetFileName(routePath);
            string contentFolder = Path.GetFullPath(Path.Combine(routePath, "..\\.."));
            return Content(contentFolder).Route(routeName);
        }

        //public static string TrackItemTable => Path.Combine(RouteFolder, RouteName + ".TIT");

        ///// <summary>
        ///// Given a soundfile reference in a wag or eng file, return the path the sound file
        ///// </summary>
        ///// <param name="wagfilename"></param>
        ///// <param name="soundfile"></param>
        ///// <returns></returns>
        //public static string TrainSound(string waggonFile, string soundFile)
        //{
        //    string trainsetSoundPath = Path.Combine(Path.GetDirectoryName(waggonFile), "SOUND", soundFile);
        //    string globalSoundPath = Path.Combine(SoundsFolder, soundFile);

        //    return File.Exists(trainsetSoundPath) ? trainsetSoundPath : globalSoundPath;
        //}

        ///// <summary>
        ///// Given a soundfile reference in a cvf file, return the path to the sound file
        ///// </summary>
        //public static string SmsSoundPath(string smsFile, string soundFile)
        //{
        //    string smsSoundPath = Path.Combine(Path.GetDirectoryName(smsFile), soundFile);
        //    string globalSoundPath = Path.Combine(SoundsFolder, soundFile);

        //    return File.Exists(smsSoundPath) ? smsSoundPath : globalSoundPath;
        //}

        /// <summary>
        /// Static variables to reduce occurrence of duplicate warning messages.
        /// </summary>
        static string badBranch = "";
        static string badPath = "";
        static readonly Dictionary<string, StringDictionary> filesFound = new Dictionary<string, StringDictionary>();

        /// <summary>
        /// Search an array of paths for a file. Paths must be in search sequence.
        /// No need for trailing "\" on path or leading "\" on branch parameter.
        /// </summary>
        /// <param name="pathArray">2 or more folders, e.g. "D:\MSTS", E:\OR"</param>
        /// <param name="branch">a filename possibly prefixed by a folder, e.g. "folder\file.ext"</param>
        /// <returns>null or the full file path of the first file found</returns>
        public static string FindFileFromFolders(IEnumerable<string> paths, string fileRelative)
        {
            if (string.IsNullOrEmpty(fileRelative))
                return string.Empty;

            if (filesFound.TryGetValue(fileRelative, out StringDictionary existingFiles))
            {
                foreach (string path in paths)
                {
                    if (existingFiles.ContainsKey(path))
                        return existingFiles[path];
                }
            }
            foreach (string path in paths)
            {
                string fullPath = Path.Combine(path, fileRelative);
                if (File.Exists(fullPath))
                {
                    if (null != existingFiles)
                        existingFiles.Add(path, fullPath);
                    else
                        filesFound.Add(fileRelative, new StringDictionary
                                {
                                    { path, fullPath }
                                });
                    return fullPath;
                }
            }

            var firstPath = paths.First();
            if (fileRelative != badBranch || firstPath != badPath)
            {
                Trace.TraceWarning("File {0} missing from {1}", fileRelative, firstPath);
                badBranch = fileRelative;
                badPath = firstPath;
            }
            return null;
        }

    }
}
