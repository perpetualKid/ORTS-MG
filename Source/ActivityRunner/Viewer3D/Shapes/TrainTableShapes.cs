using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Microsoft.Xna.Framework;

using Orts.ActivityRunner.Viewer3D.Sound;
using Orts.Common;
using Orts.Common.Position;
using Orts.Common.Xna;
using Orts.Simulation;
using Orts.Simulation.World;

using SharpDX.Direct3D9;

namespace Orts.ActivityRunner.Viewer3D.Shapes
{
    public class TurntableShape : PoseableShape
    {
        protected double animationKey;  // advances with time
        protected TurnTable Turntable; // linked turntable data
        private readonly SoundSource Sound;
        private bool Rotating;
        protected int IAnimationMatrix = -1; // index of animation matrix

        /// <summary>
        /// Construct and initialize the class
        /// </summary>
        public TurntableShape(string path, IWorldPosition positionSource, ShapeFlags flags, TurnTable turntable, double startingY)
            : base(path, positionSource, flags)
        {
            Turntable = turntable;
            //Turntable.StartingY = (float)startingY;
            Turntable.TurntableFrameRate = SharedShape.Animations[0].FrameRate;
            animationKey = (Turntable.YAngle / (float)Math.PI * 1800.0f + 3600) % 3600.0f;
            for (var imatrix = 0; imatrix < SharedShape.Matrices.Length; ++imatrix)
            {
                if (SharedShape.MatrixNames[imatrix].Equals(turntable.Animations[0], StringComparison.OrdinalIgnoreCase))
                {
                    IAnimationMatrix = imatrix;
                    break;
                }
            }
            if (viewer.Simulator.Route.DefaultTurntableSMS != null)
            {
                string soundPath = viewer.Simulator.RouteFolder.SoundFile(viewer.Simulator.Route.DefaultTurntableSMS);
                if (File.Exists(soundPath))
                {
                    Sound = new SoundSource(viewer, WorldPosition.WorldLocation, SoundEventSource.Turntable, soundPath);
                    viewer.SoundProcess.AddSoundSources(this, new List<SoundSourceBase>() { Sound });
                }
                else if (File.Exists(soundPath = viewer.Simulator.RouteFolder.ContentFolder.SoundFile(viewer.Simulator.Route.DefaultTurntableSMS)))
                {
                    Sound = new SoundSource(viewer, WorldPosition.WorldLocation, SoundEventSource.Turntable, soundPath);
                    viewer.SoundProcess.AddSoundSources(this, new List<SoundSourceBase>() { Sound });
                }
                else
                {
                    Trace.WriteLine($"Turntable soundfile {soundPath} not found");
                }
            }
            for (var matrix = 0; matrix < SharedShape.Matrices.Length; ++matrix)
                AnimateMatrix(matrix, animationKey);

            MatrixExtension.Multiply(in XNAMatrices[IAnimationMatrix], in WorldPosition.XNAMatrix, out Matrix absAnimationMatrix);
            Turntable.ReInitTrainPositions(absAnimationMatrix);
        }

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            double nextKey;
            if (Turntable.GoToTarget || Turntable.GoToAutoTarget)
            {
                nextKey = Turntable.TargetY / MathHelper.TwoPi * SharedShape.Animations[0].FrameCount;
            }
            else
            {
                double moveFrames;
                switch(Turntable.RotationDirection)
                {
                    case Rotation.CounterClockwise:
                        moveFrames = SharedShape.Animations[0].FrameRate * elapsedTime.ClockSeconds;
                        break;
                    case Rotation.Clockwise:
                        moveFrames = -SharedShape.Animations[0].FrameRate * elapsedTime.ClockSeconds;
                        break;
                    default:
                        moveFrames = 0;
                        break;
                }
                nextKey = animationKey + moveFrames;
            }
            animationKey = nextKey % SharedShape.Animations[0].FrameCount;
            if (animationKey < 0)
                animationKey += SharedShape.Animations[0].FrameCount;
            Turntable.YAngle = MathHelper.WrapAngle((float)(nextKey / SharedShape.Animations[0].FrameCount * MathHelper.TwoPi));

            if ((Turntable.RotationDirection != Rotation.None || Turntable.AutoRotationDirection != Rotation.None) && !Rotating)
            {
                Rotating = true;
                if (Sound != null) Sound.HandleEvent(Turntable.TrainsOnMovingTable.Count == 1 &&
                    Turntable.TrainsOnMovingTable[0].FrontOnBoard && Turntable.TrainsOnMovingTable[0].BackOnBoard ? TrainEvent.MovingTableMovingLoaded : TrainEvent.MovingTableMovingEmpty);
            }
            else if (Turntable.RotationDirection == Rotation.None && Turntable.AutoRotationDirection == Rotation.None && Rotating)
            {
                Rotating = false;
                if (Sound != null) Sound.HandleEvent(TrainEvent.MovingTableStopped);
            }

            // Update the pose for each matrix
            for (var matrix = 0; matrix < SharedShape.Matrices.Length; ++matrix)
                AnimateMatrix(matrix, animationKey);

            MatrixExtension.Multiply(in XNAMatrices[IAnimationMatrix], in WorldPosition.XNAMatrix, out Matrix absAnimationMatrix);
            Turntable.PerformUpdateActions(absAnimationMatrix);
            SharedShape.PrepareFrame(frame, WorldPosition, XNAMatrices, Flags);
        }
    }

    public class TransfertableShape : PoseableShape
    {
        protected double animationKey;  // advances with time
        protected TransferTable Transfertable; // linked turntable data
        private readonly SoundSource Sound;
        private bool Translating;
        protected int IAnimationMatrix = -1; // index of animation matrix

        /// <summary>
        /// Construct and initialize the class
        /// </summary>
        public TransfertableShape(string path, IWorldPosition positionSource, ShapeFlags flags, TransferTable transfertable)
            : base(path, positionSource, flags)
        {
            Transfertable = transfertable;
            animationKey = (Transfertable.XPos - Transfertable.CenterOffset.X) / Transfertable.Width * SharedShape.Animations[0].FrameCount;
            for (var imatrix = 0; imatrix < SharedShape.Matrices.Length; ++imatrix)
            {
                if (SharedShape.MatrixNames[imatrix].Equals(transfertable.Animations[0], StringComparison.OrdinalIgnoreCase))
                {
                    IAnimationMatrix = imatrix;
                    break;
                }
            }
            if (viewer.Simulator.Route.DefaultTurntableSMS != null)
            {
                string soundPath = viewer.Simulator.RouteFolder.SoundFile(viewer.Simulator.Route.DefaultTurntableSMS);
                if (File.Exists(soundPath))
                {
                    Sound = new SoundSource(viewer, WorldPosition.WorldLocation, SoundEventSource.Turntable, soundPath);
                    viewer.SoundProcess.AddSoundSources(this, new List<SoundSourceBase>() { Sound });
                }
                else if (File.Exists(soundPath = viewer.Simulator.RouteFolder.ContentFolder.SoundFile(viewer.Simulator.Route.DefaultTurntableSMS)))
                {
                    Sound = new SoundSource(viewer, WorldPosition.WorldLocation, SoundEventSource.Turntable, soundPath);
                    viewer.SoundProcess.AddSoundSources(this, new List<SoundSourceBase>() { Sound });
                }
                else
                {
                    Trace.WriteLine($"Turntable soundfile {soundPath} not found");
                }
            }
            for (var matrix = 0; matrix < SharedShape.Matrices.Length; ++matrix)
                AnimateMatrix(matrix, animationKey);

            MatrixExtension.Multiply(in XNAMatrices[IAnimationMatrix], in WorldPosition.XNAMatrix, out Matrix absAnimationMatrix);
            Transfertable.ReInitTrainPositions(absAnimationMatrix);
        }

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            if (Transfertable.GoToTarget)
            {
                animationKey = (Transfertable.TargetX - Transfertable.CenterOffset.X) / Transfertable.Width * SharedShape.Animations[0].FrameCount;
            }

            else if (Transfertable.MotionDirection == MidpointDirection.Forward)
            {
                animationKey += SharedShape.Animations[0].FrameRate * elapsedTime.ClockSeconds;
            }
            else if (Transfertable.MotionDirection == MidpointDirection.Reverse)
            {
                animationKey -= SharedShape.Animations[0].FrameRate * elapsedTime.ClockSeconds;
            }
            if (animationKey > SharedShape.Animations[0].FrameCount) animationKey = SharedShape.Animations[0].FrameCount;
            if (animationKey < 0) animationKey = 0;

            Transfertable.XPos = (float)animationKey / SharedShape.Animations[0].FrameCount * Transfertable.Width + Transfertable.CenterOffset.X;

            if (Transfertable.MotionDirection != MidpointDirection.N && !Translating)
            {
                Translating = true;
                if (Sound != null) Sound.HandleEvent(Transfertable.TrainsOnMovingTable.Count == 1 &&
                    Transfertable.TrainsOnMovingTable[0].FrontOnBoard && Transfertable.TrainsOnMovingTable[0].BackOnBoard ? TrainEvent.MovingTableMovingLoaded : TrainEvent.MovingTableMovingEmpty);
            }
            else if (Transfertable.MotionDirection == MidpointDirection.N && Translating)
            {
                Translating = false;
                if (Sound != null) Sound.HandleEvent(TrainEvent.MovingTableStopped);
            }

            // Update the pose for each matrix
            for (var matrix = 0; matrix < SharedShape.Matrices.Length; ++matrix)
                AnimateMatrix(matrix, animationKey);

            MatrixExtension.Multiply(in XNAMatrices[IAnimationMatrix], in WorldPosition.XNAMatrix, out Matrix absAnimationMatrix);
            Transfertable.PerformUpdateActions(absAnimationMatrix, WorldPosition);
            SharedShape.PrepareFrame(frame, WorldPosition, XNAMatrices, Flags);
        }
    }

}
