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

using System.Collections.Generic;
using System.Diagnostics;

using Orts.Formats.Msts;
using Orts.Formats.Msts.Files;
using Orts.Formats.Msts.Models;

using Path = System.IO.Path;

namespace Orts.ContentChecker
{
    /// <summary>
    /// Loader class for .sms files
    /// </summary>
    internal class SmsLoader : Loader
    {
        private SoundManagmentFile sms;
        /// <summary>
        /// Try to load the file.
        /// Possibly this might raise an exception. That exception is not caught here
        /// </summary>
        /// <param name="file">The file that needs to be loaded</param>
        public override void TryLoading(string file)
        {
            loadedFile = file;
            sms = new SoundManagmentFile(file);

        }

        protected override void AddReferencedFiles()
        {
            GetRouteAndBasePath(loadedFile, out string routePath, out string basePath);

            List<string> possiblePaths = new List<string>
            {
                Path.GetDirectoryName(loadedFile)
            };
            if (routePath != null)
            {
                possiblePaths.Add(Path.Combine(routePath, "SOUND"));
            }
            if (basePath != null)
            {
                possiblePaths.Add(Path.Combine(basePath, "SOUND"));
            }

            // Try to also load all sound files. This is tricky, because quite deep into the structure of a sms
            foreach (ScalabiltyGroup group in sms.ScalabiltyGroups)
            {
                if (group.Streams == null)
                { 
                    continue; 
                }
                foreach (SmsStream stream in group.Streams)
                {
                    foreach (Trigger trigger in stream.Triggers)
                    {
                        SoundPlayCommand playCommand = trigger.SoundCommand as SoundPlayCommand;
                        if (playCommand == null) { continue; }
                        foreach (string file in playCommand.Files)
                        {
                            if (file == null)
                            {
                                Trace.TraceWarning("Missing well-defined file name in {0}\n", loadedFile);
                                continue;
                            }

                            //The file can be in multiple places
                            //Assume .wav file for now
                            string fullPath = FolderStructure.FindFileFromFolders(possiblePaths, file);
                            if (fullPath == null)
                            {
                                //apparently the file does not exist, but we want to make that known to the user, so we make a path anyway
                                fullPath = Path.Combine(possiblePaths[0], file);
                            }
                            AddAdditionalFileAction.Invoke(fullPath, new WavLoader());
                        }
                    }
                }

            }
        }

        private static void GetRouteAndBasePath(string file, out string routePath, out string basePath)
        {
            routePath = null;
            basePath = null;
            Stack<string> subDirectories = new Stack<string>();
            string directory = Path.GetDirectoryName(file);
            string root = Path.GetPathRoot(file);
            while (directory.Length > root.Length)
            {
                string subdDirectoryName = Path.GetFileName(directory);
                if (subdDirectoryName.Equals("routes", System.StringComparison.OrdinalIgnoreCase))
                {
                    routePath = Path.Combine(directory, subDirectories.Pop());
                    basePath = Path.GetDirectoryName(Path.GetDirectoryName(routePath));
                    return;
                }
                if (subdDirectoryName.Equals("trains", System.StringComparison.OrdinalIgnoreCase))
                {
                    basePath = Path.GetDirectoryName(directory);
                    return;
                }
                if (subdDirectoryName.Equals("trains", System.StringComparison.OrdinalIgnoreCase))
                {
                    basePath = Path.GetDirectoryName(directory);
                }
                if (subdDirectoryName.Equals("sound", System.StringComparison.OrdinalIgnoreCase))
                {
                    basePath = Path.GetDirectoryName(directory);
                }
                subDirectories.Push(Path.GetFileName(directory));
                directory = Path.GetDirectoryName(directory);
            }
        }
    }
}
