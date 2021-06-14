// COPYRIGHT 2009, 2010, 2011, 2012, 2013 by the Open Rails project.
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

// This file is the responsibility of the 3D & Environment Team. 

using Orts.Common;
using Orts.Common.Calc;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Orts.ActivityRunner.Processes
{
    public class Profiler
    {
        private readonly string name;
        public SmoothedData Wall { get; private set; }
        public SmoothedData CPU { get; private set; }
        public SmoothedData Wait { get; private set; }

        private readonly Stopwatch timeTotal;
        private readonly Stopwatch timeRunning;
        private TimeSpan timeCPU;
        private TimeSpan lastCPU;
        private ProcessThread processThread;

        public Profiler(string name)
        {
            this.name = name;
            Wall = new SmoothedData();
            CPU = new SmoothedData();
            Wait = new SmoothedData();
            timeTotal = new Stopwatch();
            timeRunning = new Stopwatch();
            timeTotal.Start();
        }

        public void SetThread()
        {
            // This is so that you can identify threads from debuggers like Visual Studio.
            try
            {
                Thread.CurrentThread.Name = $"{name} Process";
            }
            catch (InvalidOperationException) { }

            // This is so that you can identify threads from programs like Process Monitor. The call
            // should always fail but will appear in Process Monitor's log against the correct thread.
            try
            {
                File.ReadAllBytes($@"DEBUG\THREAD\{name} Process");
            }
            catch (DirectoryNotFoundException) { }
            
            int threadId = Thread.CurrentThread.ManagedThreadId;
            foreach (ProcessThread thread in Process.GetCurrentProcess().Threads)
            {
                if (thread.Id == threadId)
                {
                    processThread = thread;
                    break;
                }
            }
        }

        public void Start()
        {
            timeRunning.Start();
            if (processThread != null)
                lastCPU = processThread.TotalProcessorTime;
        }

        public void Stop()
        {
            timeRunning.Stop();
            if (processThread != null)
                timeCPU += processThread.TotalProcessorTime - lastCPU;
        }

        public void Mark()
        {
            // Collect timing data from the timers while they're running and reset them.
            bool running = this.timeRunning.IsRunning;
            double timeTotal = this.timeTotal.ElapsedMilliseconds;
            double timeRunning = this.timeRunning.ElapsedMilliseconds;
            double timeCPU = this.timeCPU.TotalMilliseconds;

            this.timeTotal.Reset();
            this.timeTotal.Start();
            this.timeRunning.Reset();
            if (running) this.timeRunning.Start();
            this.timeCPU = TimeSpan.Zero;

            // Calculate the Wall and CPU times from timer data.
            Wall.Update(timeTotal / 1000, 100 * timeRunning / timeTotal);
            CPU.Update(timeTotal / 1000, 100 * timeCPU / timeTotal);
            Wait.Update(timeTotal / 1000, Math.Max(0, (timeRunning - timeCPU) / timeTotal));
        }
    }
}
