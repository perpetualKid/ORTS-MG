// COPYRIGHT 2018 by the Open Rails project.
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
using System.Linq;
using Orts.Common.Logging;

[assembly: CLSCompliant(true)]

namespace Orts.ContentChecker
{
    internal class Program
    {
        /// <summary> Contains the list of originally requested files</summary>
        private static readonly List<string> requestedFiles = new List<string>();

        /// <summary> Contains the list of originally requested files, in order, as well as whether they have been loaded already </summary>
        private static readonly Dictionary<string, bool> loaded = new Dictionary<string, bool>();
        private static int FilesLoaded;
        private static int FilesSkipped;

        private static void Main(string[] args)
        {
            bool optionVerbose = OptionsContain(args, new string[] { "/v", "/verbose" });
            bool optionHelp = OptionsContain(args, new string[] { "/h", "/help" });
            bool optionDependent = OptionsContain(args, new string[] { "/d", "/dependent" });
            bool optionReferenced = OptionsContain(args, new string[] { "/r", "/referenced" });
            bool optionAll = OptionsContain(args, new string[] { "/a", "/all" });
            if (optionHelp)
            {
                ShowHelp();
                return;
            }

            AdditionType additionType = optionAll ? AdditionType.All : optionReferenced ? AdditionType.Referenced : optionDependent ? AdditionType.Dependent : AdditionType.None;

            IEnumerable<string> files = args.Where(arg => !arg.StartsWith("/", StringComparison.OrdinalIgnoreCase));
            if (files.Any())
            {
                LoadFiles(files, optionVerbose, additionType);
            }
            else
            {
                ShowHelp();
            }
            Console.ReadLine();
        }

        #region Initialization
        /// <summary>
        /// Some simple helper method to determine whether the arguments <paramref name="args"/> contain at least one of the given <paramref name="args"/>
        /// </summary>
        /// <param name="args">The command line arguments that need to be checked for an iption</param>
        /// <param name="optionNames">The list of option names that need to be found</param>
        /// <returns>true if one of the optionNames is given</returns>
        private static bool OptionsContain(string[] args, IEnumerable<string> optionNames) {
            return optionNames.Any((option) => args.Contains(option, StringComparer.OrdinalIgnoreCase) );
        }

        /// <summary>
        /// Just show an help message
        /// </summary>
        private static void ShowHelp()
        {
#pragma warning disable CA1303 // Do not pass literals as localized parameters
            Console.WriteLine("Open Rails File Loader and Cross-check utility");
            Console.WriteLine();
            Console.WriteLine("{0} [<option> ...] FILE [...]", Path.GetFileNameWithoutExtension(AppDomain.CurrentDomain.FriendlyName));
            Console.WriteLine();
            //                "1234567890123456789012345678901234567890123456789012345678901234567890123456789"
            Console.WriteLine("Options:");
            Console.WriteLine("    /h, /help      Show help.");
            Console.WriteLine("    /v, /verbose   Displays all expected/valid values in addition to any errors");
            Console.WriteLine("    /d, /dependent Also load dependent files.");
            Console.WriteLine("");
            Console.WriteLine("Partially implemented options:");
            Console.WriteLine("    /r, /referenced  Also load files that are directly referenced");
            Console.WriteLine("                       This implies /d");
            Console.WriteLine("    /a, /all         Load all related files");
            Console.WriteLine("                       This implies /d and /r");
            Console.WriteLine("");
            Console.WriteLine("This utility needs as input one or more files.");
            Console.WriteLine("You can either give a file with a full path or with a relative path.");
            Console.WriteLine("It is also possible to give a search-pattern like *.ws.");
            Console.WriteLine("In that case all files in the current and any subdirectories will be searched.");
            Console.WriteLine("It is not yet possible to have search patterns in the directory name.");
            Console.WriteLine("");
            Console.WriteLine("For all files this utility will try to load the file using the ORTS routines.");
            Console.WriteLine("That means that you will get similar output as in the OpenRailsLog.txt");
            Console.WriteLine("");
            Console.WriteLine("A number of files cannot be loaded independently: They need another file to be");
            Console.WriteLine("loaded first. When using the option /d the dependent files will be loaded if");
            Console.WriteLine("file they depend on is also loaded.");
        }

        /// <summary>
        /// Initialize tracing such that all trace output coming from the loading of files is captured
        /// </summary>
        /// <param name="verbose"></param>
        /// <returns></returns>
        private static ORTraceListener SetUpTracing(bool verbose)
        {
            // Captures Trace.Trace* calls and others and formats.
            ORTraceListener traceListener = new ORTraceListener(Console.Out)
            {
                TraceOutputOptions = TraceOptions.Callstack
            };
            if (verbose)
                traceListener.Filter = new EventTypeFilter(SourceLevels.Critical | SourceLevels.Error | SourceLevels.Warning | SourceLevels.Information);
            else
                traceListener.Filter = new EventTypeFilter(SourceLevels.Critical | SourceLevels.Error | SourceLevels.Warning);

            // Trace.Listeners and Debug.Listeners are the same list.
            Trace.Listeners.Add(traceListener);

            return traceListener;
        }

        /// <summary>
        /// Determine the set of files that are requested from the command line. This includes dealing with the search pattern
        /// </summary>
        /// <param name="files">Unsorted list of input files that might still contain search patterns</param>
        private static void SetRequestedFiles(IEnumerable<string> files)
        {
            foreach (string file in files)
            {
                if (file.Contains("*"))
                {
                    string path = Path.GetDirectoryName(file);
                    if (string.IsNullOrEmpty(path))
                    {
                        path = Directory.GetCurrentDirectory();
                    }
                    if (Directory.Exists(path))
                    {
                        string searchPattern = Path.GetFileName(file);
                        SetRequestedFiles(Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories));
                    }
                    else
                    {
                        Console.WriteLine($"Error: Directory does not exist: {path}");
                    }
                }
                else
                {
                    if (File.Exists(file))
                    {
                        string fileFull = Path.GetFullPath(file);
                        requestedFiles.Add(fileFull);
                        loaded[fileFull] = false;
                    }
                    else
                    {
                        Console.WriteLine($"Error: File does not exist: {file}");
                    }

                }
            }

        }
        #endregion

        #region Loading

        /// <summary>
        /// Find the list of real files (so without search pattern) and try to load all of them
        /// </summary>
        /// <param name="files">Unsorted list of input files that might still contain search patterns</param>
        /// <param name="verbose">True if more verbose messages need to be shown</param>
        /// <param name="additionType"> The type of files that need to be added</param>
        private static void LoadFiles(IEnumerable<string> files, bool verbose, AdditionType additionType)
        {
            ORTraceListener traceListener = SetUpTracing(verbose);

            SetRequestedFiles(files);
            if (additionType == AdditionType.None)
            {
                LoadAllFlat();
            }
            else {
                LoadWithAdditional(additionType);
            }

            ShowTracingReport(traceListener);
        }

        /// <summary>
        /// Load all the requested files one by one in the given order without caring about relations between files.
        /// Many files might not be loadable in this way.
        /// </summary>
        private static void LoadAllFlat()
        {
            foreach (string file in requestedFiles)
            {
                Loader currentLoader = LoaderFactory.GetLoader(file);
                LoadSingleFile(file, currentLoader, 0);
            }
        }

        /// <summary>
        /// Load all the requested files one by one in the given order without caring about relations between files.
        /// Many files might not be loadable in this way.
        /// </summary>
        private static void LoadWithAdditional(AdditionType additionType)
        {
            for (int pass = 0; pass < 2; pass++)
            {   // we do two passes, postponing loading of some files to a later stage
                foreach (string file in requestedFiles)
                {
                    if (loaded[file])
                    {
                        continue;
                    }
                    Loader currentLoader = LoaderFactory.GetLoader(file);
                    if (!currentLoader.IsDependent || pass > 0)
                    {
                        LoadFileAndAdditions(file, currentLoader, additionType, 0);
                    }
                }
            }

        }

        /// <summary>
        /// Load a file and all the files that depend on it, recursively, depth-first
        /// </summary>
        /// <param name="file">The name of the file that needs to be loaded</param>
        /// <param name="loader">The loader to be used for the actually loading</param>
        /// <param name="additionType"> The type of files that need to be added</param>
        /// <param name="indentLevel"> Indentation level indicating the depth of files added</param>
        private static void LoadFileAndAdditions(string file, Loader loader, AdditionType additionType, int indentLevel)
        {
            if (!File.Exists(file))
            {
                Console.WriteLine($"Error in loading dependent files: File does not exist: {file}");
                loaded[file] = true;  // do not try to load it again
                return;
            }

            List<FileAndLoader> dependentFiles = new List<FileAndLoader>();
            LoadSingleFile(file, loader, indentLevel);
            loader.AddAdditionalFiles(additionType, (newfile, newLoader) => dependentFiles.Add(new FileAndLoader(newfile, newLoader)));

            foreach (FileAndLoader fileAndLoader in dependentFiles)
            {
                LoadFileAndAdditions(fileAndLoader.File, fileAndLoader.Loader, additionType, indentLevel + 1);
            }

        }

        /// <summary>
        /// Load a single file, do all the single file output, and deal with possible exceptions during loading
        /// </summary>
        /// <param name="file">The name of the file that needs to be loaded</param>
        /// <param name="loader">The loader to be used for the actually loading</param>
        /// <param name="indentLevel"> Indentation level indicating the depth of files added</param>
        private static void LoadSingleFile(string file, Loader loader, int indentLevel)
        {
            if (indentLevel > 0)
            {
                Console.Write('.');
                Console.Write(new string(' ', indentLevel));
            }
            Console.Write("{0}: ", file);
            try
            {
                loader.TryLoading(file);
                if (loader.FilesLoaded > 0)
                {
                    Console.WriteLine("OK");
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception error)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                Trace.WriteLine(error);
            }

            //Even if the loading fails, we should update the amount of files that were loaded or at least attempted
            FilesLoaded += loader.FilesLoaded;
            FilesSkipped += loader.FilesSkipped;
            loaded[file] = true;

            SetConsoleBufferSize();
        }

        #endregion

        #region Reporting

        /// <summary>
        /// Make sure that the console buffer (the amount of lines it remembers) is large enough.
        /// But at the same time make sure we do not increase it too often for performance reasons
        /// </summary>
        private static void SetConsoleBufferSize()
        {
            //The amount of lines we might expect to be written during the loading of one file
            //This is surprisingly large
            int BUFFERMARGIN = 500;
            //Additional buffer lines to be added just to make sure we do not extend too often
            int BUFFEREXTENSION = 250;
            int minimumBuffer = BUFFERMARGIN + Console.CursorTop;
            if (Console.BufferHeight < minimumBuffer)
            {
                Console.SetBufferSize(Console.BufferWidth, minimumBuffer + BUFFEREXTENSION );
            }

        }

        /// <summary>
        /// Print the final report of files loaded, skipped and the amount of errors, warnings, informations
        /// </summary>
        /// <param name="traceListener"></param>
        private static void ShowTracingReport(ORTraceListener traceListener)
        {
            if (FilesLoaded <= 1)
            {
                // No need for a summary if only one file was requested
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Loader summary");
            Console.WriteLine("  Files loaded:  {0}", FilesLoaded);
            Console.WriteLine("  Files skipped: {0}", FilesSkipped);
            Console.WriteLine();
            Console.WriteLine("  Errors:        {0}", traceListener.EventCount(TraceEventType.Critical) + traceListener.EventCount(TraceEventType.Error));
            Console.WriteLine("  Warnings:      {0}", traceListener.EventCount(TraceEventType.Warning));
            Console.WriteLine("  Informations:  {0}", traceListener.EventCount(TraceEventType.Information));
            Console.WriteLine();
        }

        #endregion
#pragma warning restore CA1303 // Do not pass literals as localized parameters

        private readonly struct FileAndLoader
        {
            public readonly string File;
            public readonly Loader Loader;

            public FileAndLoader(string file, Loader loader)
            {
                File = file;
                Loader = loader;
            }
        }
    }
}
