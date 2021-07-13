// COPYRIGHT 2014 by the Open Rails project.
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
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using Orts.Common;
using Orts.Formats.OR.Files;

namespace Orts.Models.Simplified
{
    public class TimetableInfo : ContentBase
    {
        private static readonly string[] extensions = { "*.timetable_or", "*.timetable-or", "*.timetablelist_or", "*.timetablelist-or" };

        public List<TimetableFile> TimeTables { get; private set; } = new List<TimetableFile>();
        public string Description { get; private set; }
        public string FileName { get; private set; }

        // items set for use as parameters, taken from main menu
        public int Day { get; set; }
        public SeasonType Season { get; set; } = SeasonType.Summer;
        public WeatherType Weather { get; set; } = WeatherType.Clear;
        public string WeatherFile { get; set; }

        // note : file is read preliminary only, extracting description and train information
        // all other information is read only when activity is started

        internal TimetableInfo(string filePath)
        {

            try
            {
                string extension = System.IO.Path.GetExtension(filePath);
                if (extension.IndexOf("list", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TimetableGroupFile groupFile = new TimetableGroupFile(filePath);
                    TimeTables = groupFile.TimeTables;
                    FileName = filePath;
                    Description = groupFile.Description;
                }
                else
                {
                    TimetableFile timeTableFile = new TimetableFile(filePath);
                    TimeTables.Add(timeTableFile);
                    FileName = filePath;
                    Description = timeTableFile.Description;
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch
#pragma warning restore CA1031 // Do not catch general exception types
            {
                Description = $"<{catalog.GetString("load error:")} {System.IO.Path.GetFileNameWithoutExtension(filePath)}>";
            }
        }

        public override string ToString()
        {
            return Description;
        }

        public static async Task<IEnumerable<TimetableInfo>> GetTimetableInfo(Route route, CancellationToken token)
        {
            if (null == route)
                throw new ArgumentNullException(nameof(route));

            using (SemaphoreSlim addItem = new SemaphoreSlim(1))
            {
                List<TimetableInfo> result = new List<TimetableInfo>();
                string orActivitiesDirectory = route.RouteFolder.OpenRailsActivitiesFolder;

                if (Directory.Exists(orActivitiesDirectory))
                {
                    TransformBlock<string, TimetableInfo> inputBlock = new TransformBlock<string, TimetableInfo>
                        (timeTableFile =>
                        {
                            return new TimetableInfo(timeTableFile);
                        },
                        new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = System.Environment.ProcessorCount, CancellationToken = token });


                    ActionBlock<TimetableInfo> actionBlock = new ActionBlock<TimetableInfo>
                        (async activity =>
                        {
                            try
                            {
                                await addItem.WaitAsync(token).ConfigureAwait(false);
                                result.Add(activity);
                            }
                            finally
                            {
                                addItem.Release();
                            }
                        });

                    inputBlock.LinkTo(actionBlock, new DataflowLinkOptions { PropagateCompletion = true });
                    foreach (string timeTableFile in extensions.SelectMany(extension => (Directory.EnumerateFiles(orActivitiesDirectory, extension))))
                        await inputBlock.SendAsync(timeTableFile).ConfigureAwait(false);

                    inputBlock.Complete();
                    await actionBlock.Completion.ConfigureAwait(false);
                }
                return result;
            }
        }
    }

    public class WeatherFileInfo
    {
        private readonly FileInfo fileDetails;

        private WeatherFileInfo(string fileName)
        {
            fileDetails = new FileInfo(fileName);
        }

        public override string ToString()
        {
            return (fileDetails.Name);
        }

        public string GetFullName()
        {
            return (fileDetails.FullName);
        }

        // get weatherfiles
        public static async Task<IEnumerable<WeatherFileInfo>> GetTimetableWeatherFiles(Route route, CancellationToken token)
        {
            if (null == route)
                throw new ArgumentNullException(nameof(route));

            using (SemaphoreSlim addItem = new SemaphoreSlim(1))
            {
                string weatherDirectory = route.RouteFolder.WeatherFolder;

                List<WeatherFileInfo> result = new List<WeatherFileInfo>();
                if (Directory.Exists(weatherDirectory))
                {
                    //https://stackoverflow.com/questions/11564506/nesting-await-in-parallel-foreach?rq=1
                    ActionBlock<string> actionBlock = new ActionBlock<string>
                        (async weatherFile =>
                        {
                            try
                            {
                                WeatherFileInfo weather = new WeatherFileInfo(weatherFile);
                                await addItem.WaitAsync().ConfigureAwait(false);
                                result.Add(weather);
                            }
                            finally
                            {
                                addItem.Release();
                            }
                        },
                        new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token });

                    foreach (string weatherFile in Directory.EnumerateFiles(weatherDirectory, "*.weather-or"))
                        await actionBlock.SendAsync(weatherFile).ConfigureAwait(false);

                    actionBlock.Complete();
                    await actionBlock.Completion.ConfigureAwait(false);
                }
                return result;
            }
        }
    }
}

