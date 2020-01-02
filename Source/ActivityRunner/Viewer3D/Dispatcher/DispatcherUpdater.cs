using System;
using System.Threading;

namespace Orts.ActivityRunner.Viewer3D.Dispatcher
{
    internal class DispatcherUpdater
    {
        private CancellationToken token;
        private Thread updaterThread;
        private readonly ManualResetEventSlim waitToUpdate = new ManualResetEventSlim();

        private RenderFrame currentFrame;
        private readonly DispatcherContent content;

        public DispatcherUpdater(CancellationToken token, DispatcherContent content)
        {
            updaterThread = new Thread(DispatcherUpdate);
            this.content = content;
            this.token = token;
        }

        public void Initialize()
        {
            updaterThread.Start();
        }

        public void StartUpdate()
        {
            currentFrame = content.Background;
            waitToUpdate.Set();
        }

        private void DispatcherUpdate()
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    waitToUpdate.Wait(token);
                    waitToUpdate.Reset();
                    //update the current RenderFrame
                    if (currentFrame.Update())
                    {
                        content.SwapFrames();
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

    }
}
