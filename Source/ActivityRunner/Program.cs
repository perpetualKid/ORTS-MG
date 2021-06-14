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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Orts.ActivityRunner.Viewer3D;
using Orts.ActivityRunner.Viewer3D.Debugging;
using Orts.ActivityRunner.Viewer3D.Processes;
using Orts.Common.Info;
using Orts.Common.Native;
using Orts.Settings;

[assembly: CLSCompliant(false)]

namespace Orts.ActivityRunner
{
    internal static class Program
    {
        public static Viewer Viewer;
        public static DispatchViewer DebugViewer;
        public static SoundDebugForm SoundDebugForm;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        private static void Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            IEnumerable<string> options = args.Where(a => a.StartsWith("-", StringComparison.OrdinalIgnoreCase) || a.StartsWith("/", StringComparison.OrdinalIgnoreCase)).Select(a => a.Substring(1));
            UserSettings settings = new UserSettings(options);

            //enables loading of dll for specific architecture(32 or 64bit) from distinct folders, useful when both versions require same name (as for OpenAL32.dll)
            string path = Path.Combine(RuntimeInfo.ApplicationFolder, "Native", (Environment.Is64BitProcess) ? "x64" : "x86");
            NativeMethods.SetDllDirectory(path);

            using (Game game = new Game(settings))
            {
#pragma warning disable CA2000 // Dispose objects before losing scope
                game.PushState(new GameStateRunActivity(args));
#pragma warning restore CA2000 // Dispose objects before losing scope
                game.Run();
            }
        }
    }
}
