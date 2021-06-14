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

using System;
using System.Threading;

namespace Orts.ActivityRunner.Processes
{
    public class ProcessState : IDisposable
    {
        public bool Finished { get; private set; }
        public bool Terminated { get; private set; }

        private readonly ManualResetEvent startEvent = new ManualResetEvent(false);
        private readonly ManualResetEvent finishEvent = new ManualResetEvent(true);
        private readonly ManualResetEvent terminateEvent = new ManualResetEvent(false);
        private readonly WaitHandle[] startEvents;
        private readonly WaitHandle[] finishEvents;
        private bool disposedValue;
        private readonly string processName;

        public ProcessState(string name)
        {
            processName = name;
            Finished = true;
            startEvents = new[] { startEvent, terminateEvent };
            finishEvents = new[] { finishEvent, terminateEvent };
        }

        public void SignalStart()
        {
            Finished = false;
            finishEvent.Reset();
            startEvent.Set();
        }

        public void SignalFinish()
        {
            Finished = true;
            startEvent.Reset();
            finishEvent.Set();
        }

        public void SignalTerminate()
        {
            Terminated = true;
            terminateEvent.Set();
        }

        public void WaitTillStarted()
        {
            WaitHandle.WaitAny(startEvents);
        }

        public void WaitTillFinished()
        {
            WaitHandle.WaitAny(finishEvents);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    startEvent?.Dispose();
                    finishEvent?.Dispose();
                    terminateEvent?.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
