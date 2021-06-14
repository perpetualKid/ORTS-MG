using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using Orts.Common.Info;

namespace Orts.Common.Logging
{
    public static class LoggingUtil
    {
        public static readonly string SeparatorLine = new string('-', 80);

        public const string BugTrackerUrl = "https://github.com/perpetualKid/ORTS-MG/issues";

        static LoggingUtil()
        {
            if (Debugger.IsLogging())
            {
                if (!Trace.Listeners.OfType<ConsoleTraceListener>().Any())
                    Trace.Listeners.Add(new ConsoleTraceListener());
            }
        }

        public static string CustomizeLogFileName(string fileNamePattern)
        {
            Regex paramReplacement = new Regex(@"\{(\w*?)\}"); // store this...

            Dictionary<string, string> replacementValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "application", FileVersionInfo.GetVersionInfo(Assembly.GetCallingAssembly().Location).FileDescription},
                { "product", RuntimeInfo.ProductName},
                { "version", VersionInfo.Version},
                { "date", DateTime.Now.Date.ToString("d", CultureInfo.CurrentCulture) },
                { "time", TimeSpan.FromSeconds((int)DateTime.Now.TimeOfDay.TotalSeconds).ToString("t", CultureInfo.CurrentCulture) },
            };

            string result = paramReplacement.Replace(fileNamePattern, delegate (Match match) {
                string key = match.Groups[1].Value;
                return replacementValues[key];
            });

            Regex invalidCharReplacement = new Regex(string.Format(CultureInfo.CurrentCulture, "[{0}]", Regex.Escape(new string(Path.GetInvalidFileNameChars()))));
            return invalidCharReplacement.Replace(result, "_");
        }

        public static void InitLogging(string logFileName, bool errorsOnly, bool appendLog)
        {
            if (string.IsNullOrEmpty(logFileName))
                return;

            try
            {
                if (!appendLog)
                    File.Delete(logFileName);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is ArgumentException || ex is IOException || ex is DirectoryNotFoundException)
            {
            }

            try
            {

                StreamWriter writer = new StreamWriter(logFileName, true, Encoding.Default, 512)
                {
                    AutoFlush = true
                };

                // Captures Trace.Trace* calls and others and formats.
                ORTraceListener traceListener = new ORTraceListener(writer, errorsOnly)
                {
                    TraceOutputOptions = TraceOptions.Callstack
                };
                Trace.Listeners.Add(traceListener);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is ArgumentException || ex is IOException || ex is DirectoryNotFoundException)
            {
            }
            Trace.WriteLine($"This is a log file for {RuntimeInfo.ProductName} {Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly().Location)}. Please include this file in bug reports.");
            Trace.WriteLine(SeparatorLine);
            if (errorsOnly)
            {
                Trace.WriteLine("Logging is disabled, only fatal errors will appear here.");
                Trace.WriteLine(SeparatorLine);
            }
            else
            {
                SystemInfo.WriteSystemDetails();
                Trace.WriteLine(SeparatorLine);
                Trace.WriteLine($"{"Version",-12}= {VersionInfo.Version}");
                Trace.WriteLine($"{"Code Version",-12}= {VersionInfo.CodeVersion}");
                if (logFileName.Length > 0)
                    Trace.WriteLine($"{"Logfile",-12}= {logFileName.Replace(Environment.UserName, "********", StringComparison.OrdinalIgnoreCase)}");
                Trace.WriteLine($"{"Executable",-12}= {Path.GetFileName(Assembly.GetEntryAssembly().Location)}");
                foreach (string arg in Environment.GetCommandLineArgs())
                    Trace.WriteLine($"{"Argument",-12}= {arg}");
                Trace.WriteLine(SeparatorLine);
            }
        }
    }
}
