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

// This file is the responsibility of the 3D & Environment Team. 

using System;

using Microsoft.Xna.Framework;

using Orts.ActivityRunner.Viewer3D.Debugging;
using Orts.MultiPlayer;
using Orts.Simulation;

namespace Orts.ActivityRunner.Viewer3D.Processes
{
    public class GameStateViewer3D : GameState
    {
        internal readonly Viewer Viewer;
        private bool firstFrame = true;
        private int profileFrames;

        private double lastLoadRealTime;
        private double lastTotalRealSeconds = -1;
        private readonly double[] averageElapsedRealTime = new double[10];
        private int averageElapsedRealTimeIndex;


        public GameStateViewer3D(Viewer viewer)
        {
            Viewer = viewer;
            Viewer.Simulator.GamePaused = Viewer.QuitWindow.Visible = viewer.Settings.StartGamePaused;
        }

        internal override void BeginRender(RenderFrame frame)
        {
            // Do this here (instead of RenderProcess) because we only want to measure/time the running game.
            if (Game.Settings.Profiling)
                if ((Game.Settings.ProfilingFrameCount > 0 && ++profileFrames > Game.Settings.ProfilingFrameCount) || (Game.Settings.ProfilingTime > 0 && Viewer?.RealTime >= Game.Settings.ProfilingTime))
                    Game.PopState();

            if (firstFrame)
            {
                // Turn off the 10FPS fixed-time-step and return to running as fast as we can.
                Game.IsFixedTimeStep = false;
                Game.InactiveSleepTime = TimeSpan.Zero;

                // We must create these forms on the main thread (Render) or they won't pump events correctly.

                if (MPManager.IsMultiPlayer() || Game.Settings.ViewDispatcher)
                {
                    Program.DebugViewer = new DispatchViewer(Viewer);
                    Program.DebugViewer.Hide();
                    Viewer.DebugViewerEnabled = false;
                }

                Program.SoundDebugForm = new SoundDebugForm(Viewer);
                Program.SoundDebugForm.Hide();
                Viewer.SoundDebugFormEnabled = false;

                firstFrame = false;
            }
            Viewer.BeginRender(frame);
        }

        internal override void EndRender(RenderFrame frame)
        {
            Viewer.EndRender(frame);
        }

        internal override void Update(RenderFrame frame, double totalRealSeconds, GameTime gameTime)
        {
            // Every 250ms, check for new things to load and kick off the loader.
            if (lastLoadRealTime + 0.25 < totalRealSeconds && Game.LoaderProcess.Finished)
            {
                lastLoadRealTime = totalRealSeconds;
                Viewer.World.LoadPrep();
                Game.LoaderProcess.StartLoad();
            }

            // The first time we update, the TotalRealSeconds will be ~time
            // taken to load everything. We'd rather not skip that far through
            // the simulation so the first time we deliberately have an
            // elapsed real and clock time of 0.0s.
            if (lastTotalRealSeconds == -1)
                lastTotalRealSeconds = totalRealSeconds;
            // We would like to avoid any large jumps in the simulation, so
            // this is a 4FPS minimum, 250ms maximum update time.
            else if (totalRealSeconds - lastTotalRealSeconds > 0.25f)
                lastTotalRealSeconds = totalRealSeconds;

            double elapsedRealTime = totalRealSeconds - lastTotalRealSeconds;
            lastTotalRealSeconds = totalRealSeconds;

            if (elapsedRealTime > 0)
            {
                // Store the elapsed real time, but also loop through overwriting any blank entries.
                do
                {
                    averageElapsedRealTime[averageElapsedRealTimeIndex] = elapsedRealTime;
                    averageElapsedRealTimeIndex = (averageElapsedRealTimeIndex + 1) % averageElapsedRealTime.Length;
                } while (averageElapsedRealTime[averageElapsedRealTimeIndex] == 0);

                // Elapsed real time is now the average.
                elapsedRealTime = 0;
                for (int i = 0; i < averageElapsedRealTime.Length; i++)
                    elapsedRealTime += averageElapsedRealTime[i] / averageElapsedRealTime.Length;
            }

            // TODO: ComputeFPS should be called in UpdaterProcess.Update() but needs delta time.
            Game.RenderProcess.ComputeFPS(elapsedRealTime);
            Viewer.Update(frame, elapsedRealTime);
        }

        internal override void Load()
        {
            Viewer.Load();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Viewer.Terminate();
                MPManager.Server?.Stop();
                MPManager.Client?.Stop();
                Simulator.Instance.Stop();
                Program.DebugViewer?.Dispose();
                Program.SoundDebugForm?.Dispose();
            }
            base.Dispose(disposing);
        }

    }
}
