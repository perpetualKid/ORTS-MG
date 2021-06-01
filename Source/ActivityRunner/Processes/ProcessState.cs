// COPYRIGHT 2009, 2011, 2012, 2014 by the Open Rails project.
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

using System.Threading;

namespace Orts.ActivityRunner.Processes
{
    public class ProcessState
    {
        public bool Finished { get; private set; }
        public bool Terminated { get; private set; }

        private readonly ManualResetEvent StartEvent = new ManualResetEvent(false);
        private readonly ManualResetEvent FinishEvent = new ManualResetEvent(true);
        private readonly ManualResetEvent TerminateEvent = new ManualResetEvent(false);
        private readonly WaitHandle[] StartEvents;
        private readonly WaitHandle[] FinishEvents;
#if DEBUG_THREAD_PERFORMANCE
        StreamWriter DebugFileStream;
#endif

        public ProcessState(string name)
        {
            Finished = true;
            StartEvents = new[] { StartEvent, TerminateEvent };
            FinishEvents = new[] { FinishEvent, TerminateEvent };
#if DEBUG_THREAD_PERFORMANCE
            DebugFileStream = new StreamWriter(File.OpenWrite("debug_thread_" + name.ToLowerInvariant() + "_state.csv"));
            DebugFileStream.Write("Time,Event\n");
#endif
        }

        public void SignalStart()
        {
#if DEBUG_THREAD_PERFORMANCE
            DebugFileStream.Write("{0},SS\n", DateTime.UtcNow.Ticks);
#endif
            Finished = false;
            FinishEvent.Reset();
            StartEvent.Set();
        }

        public void SignalFinish()
        {
#if DEBUG_THREAD_PERFORMANCE
            DebugFileStream.Write("{0},SF\n", DateTime.UtcNow.Ticks);
#endif
            Finished = true;
            StartEvent.Reset();
            FinishEvent.Set();
        }

        public void SignalTerminate()
        {
#if DEBUG_THREAD_PERFORMANCE
            DebugFileStream.Write("{0},ST\n", DateTime.UtcNow.Ticks);
#endif
            Terminated = true;
            TerminateEvent.Set();
        }

        public void WaitTillStarted()
        {
#if DEBUG_THREAD_PERFORMANCE
            DebugFileStream.Write("{0},WTS+\n", DateTime.UtcNow.Ticks);
#endif
            WaitHandle.WaitAny(StartEvents);
#if DEBUG_THREAD_PERFORMANCE
            DebugFileStream.Write("{0},WTS-\n", DateTime.UtcNow.Ticks);
#endif
        }

        public void WaitTillFinished()
        {
#if DEBUG_THREAD_PERFORMANCE
            DebugFileStream.Write("{0},WTF+\n", DateTime.UtcNow.Ticks);
#endif
            WaitHandle.WaitAny(FinishEvents);
#if DEBUG_THREAD_PERFORMANCE
            DebugFileStream.Write("{0},WTF-\n", DateTime.UtcNow.Ticks);
#endif
        }
    }
}
