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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Microsoft.Xna.Framework;

using Orts.ActivityRunner.Viewer3D.RollingStock;
using Orts.Common;
using Orts.Common.Input;
using Orts.Common.Position;
using Orts.Common.Xna;
using Orts.Formats.Msts.Models;
using Orts.Simulation;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.Signalling;
using Orts.Simulation.Track;

namespace Orts.ActivityRunner.Viewer3D
{
    public abstract class Camera
    {
        #region Camera event Handling setup
        private sealed class CameraEventHandler
        {
            private static CameraEventHandler instance;

            private CameraEventHandler(Viewer viewer)
            {
                viewer.UserCommandController.AddEvent(UserCommand.CameraZoomIn, KeyEventType.KeyDown, (UserCommandArgs commandArgs, GameTime gameTime) => viewer.Camera.Zoom(1, commandArgs, gameTime));
                viewer.UserCommandController.AddEvent(UserCommand.CameraZoomOut, KeyEventType.KeyDown, (UserCommandArgs commandArgs, GameTime gameTime) => viewer.Camera.Zoom(-1, commandArgs, gameTime));
                viewer.UserCommandController.AddEvent(UserCommand.CameraZoomIn, KeyEventType.KeyPressed, () => viewer.Camera.ZoomCommandStart());
                viewer.UserCommandController.AddEvent(UserCommand.CameraZoomOut, KeyEventType.KeyPressed, () => viewer.Camera.ZoomCommandStart());
                viewer.UserCommandController.AddEvent(UserCommand.CameraZoomIn, KeyEventType.KeyReleased, () => viewer.Camera.ZoomCommandEnd());
                viewer.UserCommandController.AddEvent(UserCommand.CameraZoomOut, KeyEventType.KeyReleased, () => viewer.Camera.ZoomCommandEnd());
                viewer.UserCommandController.AddEvent(UserCommand.CameraToggleLetterboxCab, KeyEventType.KeyPressed, () => viewer.Camera.ToggleLetterboxCab());
                viewer.UserCommandController.AddEvent(UserCommand.CameraChangePassengerViewPoint, KeyEventType.KeyPressed, () => viewer.Camera.ChangePassengerViewPoint());
                viewer.UserCommandController.AddEvent(UserCommand.CameraScrollLeft, KeyEventType.KeyDown, (GameTime gameTime) => viewer.Camera.Scroll(false, gameTime));
                viewer.UserCommandController.AddEvent(UserCommand.CameraScrollRight, KeyEventType.KeyDown, (GameTime gameTime) => viewer.Camera.Scroll(true, gameTime));

                viewer.UserCommandController.AddEvent(UserCommand.CameraRotateRight, KeyEventType.KeyDown, (UserCommandArgs commandArgs, GameTime gameTime) => viewer.Camera.RotateHorizontally(1, commandArgs, gameTime));
                viewer.UserCommandController.AddEvent(UserCommand.CameraRotateLeft, KeyEventType.KeyDown, (UserCommandArgs commandArgs, GameTime gameTime) => viewer.Camera.RotateHorizontally(-1, commandArgs, gameTime));
                viewer.UserCommandController.AddEvent(UserCommand.CameraRotateUp, KeyEventType.KeyDown, (UserCommandArgs commandArgs, GameTime gameTime) => viewer.Camera.RotateVertically(1, commandArgs, gameTime));
                viewer.UserCommandController.AddEvent(UserCommand.CameraRotateDown, KeyEventType.KeyDown, (UserCommandArgs commandArgs, GameTime gameTime) => viewer.Camera.RotateVertically(-1, commandArgs, gameTime));
                viewer.UserCommandController.AddEvent(UserCommand.CameraRotateRight, KeyEventType.KeyPressed, () => viewer.Camera.RotateHorizontallyCommandStart());
                viewer.UserCommandController.AddEvent(UserCommand.CameraRotateLeft, KeyEventType.KeyPressed, () => viewer.Camera.RotateHorizontallyCommandStart());
                viewer.UserCommandController.AddEvent(UserCommand.CameraRotateUp, KeyEventType.KeyPressed, () => viewer.Camera.RotateVerticallyCommandStart());
                viewer.UserCommandController.AddEvent(UserCommand.CameraRotateDown, KeyEventType.KeyPressed, () => viewer.Camera.RotateVerticallyCommandStart());
                viewer.UserCommandController.AddEvent(UserCommand.CameraRotateRight, KeyEventType.KeyReleased, () => viewer.Camera.RotateHorizontallyCommandEnd());
                viewer.UserCommandController.AddEvent(UserCommand.CameraRotateLeft, KeyEventType.KeyReleased, () => viewer.Camera.RotateHorizontallyCommandEnd());
                viewer.UserCommandController.AddEvent(UserCommand.CameraRotateUp, KeyEventType.KeyReleased, () => viewer.Camera.RotateVerticallyCommandEnd());
                viewer.UserCommandController.AddEvent(UserCommand.CameraRotateDown, KeyEventType.KeyReleased, () => viewer.Camera.RotateVerticallyCommandEnd());

                viewer.UserCommandController.AddEvent(UserCommand.CameraPanRight, KeyEventType.KeyDown, (UserCommandArgs commandArgs, GameTime gameTime) => viewer.Camera.PanHorizontally(1, commandArgs, gameTime));
                viewer.UserCommandController.AddEvent(UserCommand.CameraPanLeft, KeyEventType.KeyDown, (UserCommandArgs commandArgs, GameTime gameTime) => viewer.Camera.PanHorizontally(-1, commandArgs, gameTime));
                viewer.UserCommandController.AddEvent(UserCommand.CameraPanUp, KeyEventType.KeyDown, (UserCommandArgs commandArgs, GameTime gameTime) => viewer.Camera.PanVertically(1, commandArgs, gameTime));
                viewer.UserCommandController.AddEvent(UserCommand.CameraPanDown, KeyEventType.KeyDown, (UserCommandArgs commandArgs, GameTime gameTime) => viewer.Camera.PanVertically(-1, commandArgs, gameTime));
                viewer.UserCommandController.AddEvent(UserCommand.CameraPanRight, KeyEventType.KeyPressed, () => viewer.Camera.PanHorizontallyCommandStart());
                viewer.UserCommandController.AddEvent(UserCommand.CameraPanLeft, KeyEventType.KeyPressed, () => viewer.Camera.PanHorizontallyCommandStart());
                viewer.UserCommandController.AddEvent(UserCommand.CameraPanRight, KeyEventType.KeyPressed, () => viewer.Camera.SwitchCabView(1));
                viewer.UserCommandController.AddEvent(UserCommand.CameraPanLeft, KeyEventType.KeyPressed, () => viewer.Camera.SwitchCabView(-1));
                viewer.UserCommandController.AddEvent(UserCommand.CameraPanUp, KeyEventType.KeyPressed, () => viewer.Camera.PanVerticallyCommandStart());
                viewer.UserCommandController.AddEvent(UserCommand.CameraPanDown, KeyEventType.KeyPressed, () => viewer.Camera.PanVerticallyCommandStart());
                viewer.UserCommandController.AddEvent(UserCommand.CameraPanRight, KeyEventType.KeyReleased, () => viewer.Camera.PanHorizontallyCommandEnd());
                viewer.UserCommandController.AddEvent(UserCommand.CameraPanLeft, KeyEventType.KeyReleased, () => viewer.Camera.PanHorizontallyCommandEnd());
                viewer.UserCommandController.AddEvent(UserCommand.CameraPanUp, KeyEventType.KeyReleased, () => viewer.Camera.PanVerticallyCommandEnd());
                viewer.UserCommandController.AddEvent(UserCommand.CameraPanDown, KeyEventType.KeyReleased, () => viewer.Camera.PanVerticallyCommandEnd());

                viewer.UserCommandController.AddEvent(UserCommand.CameraCarNext, KeyEventType.KeyPressed, () => viewer.Camera.CarNext());
                viewer.UserCommandController.AddEvent(UserCommand.CameraCarPrevious, KeyEventType.KeyPressed, () => viewer.Camera.CarPrevious());
                viewer.UserCommandController.AddEvent(UserCommand.CameraCarFirst, KeyEventType.KeyPressed, () => viewer.Camera.CarFirst());
                viewer.UserCommandController.AddEvent(UserCommand.CameraCarLast, KeyEventType.KeyPressed, () => viewer.Camera.CarLast());
                viewer.UserCommandController.AddEvent(UserCommand.CameraBrowseBackwards, KeyEventType.KeyPressed, () => viewer.Camera.BrowseBackwards());
                viewer.UserCommandController.AddEvent(UserCommand.CameraBrowseForwards, KeyEventType.KeyPressed, () => viewer.Camera.BrowseForwards());
                viewer.UserCommandController.AddEvent(CommonUserCommand.VerticalScrollChanged, (UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers) => viewer.Camera.ZoomByMouseCommmand(commandArgs, gameTime, modifiers));
                viewer.UserCommandController.AddEvent(CommonUserCommand.AlternatePointerDragged, (UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers) => viewer.Camera.RotateByMouseCommmand(commandArgs, gameTime, modifiers));
                viewer.UserCommandController.AddEvent(CommonUserCommand.AlternatePointerPressed, (UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers) => viewer.Camera.RotateByMouseCommmandStart());
                viewer.UserCommandController.AddEvent(CommonUserCommand.AlternatePointerReleased, (UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers) => viewer.Camera.RotateByMouseCommmandEnd());
                viewer.UserCommandController.AddEvent(CommonUserCommand.PointerPressed, (UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers) => viewer.Camera.CabControlClickCommand(commandArgs, modifiers));
                viewer.UserCommandController.AddEvent(CommonUserCommand.PointerReleased, (UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers) => viewer.Camera.CabControlReleaseCommand(commandArgs, modifiers));
                viewer.UserCommandController.AddEvent(CommonUserCommand.PointerMoved, (UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers) => viewer.Camera.CabControlPointerMovedCommand(commandArgs, modifiers));
                viewer.UserCommandController.AddEvent(CommonUserCommand.PointerDragged, (UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers) => viewer.Camera.CabControlDraggedCommand(commandArgs, modifiers));
            }

            internal static void Initialize(Viewer viewer)
            {
                if (null == instance)
                {
                    instance = new CameraEventHandler(viewer);
                }
            }
        }

        private protected virtual void ZoomCommandStart() { }
        private protected virtual void ZoomCommandEnd() { }
        private protected virtual void Zoom(int zoomSign, UserCommandArgs commandArgs, GameTime gameTime) { }
        private protected virtual void ZoomByMouseCommmand(UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers) { }
        private protected virtual void RotateHorizontallyCommandStart() { }
        private protected virtual void RotateHorizontallyCommandEnd() { }
        private protected virtual void RotateHorizontally(int rotateSign, UserCommandArgs commandArgs, GameTime gameTime) { }
        private protected virtual void RotateVerticallyCommandStart() { }
        private protected virtual void RotateVerticallyCommandEnd() { }
        private protected virtual void RotateVertically(int rotateSign, UserCommandArgs commandArgs, GameTime gameTime) { }
        private protected virtual void PanHorizontallyCommandStart() { }
        private protected virtual void PanHorizontallyCommandEnd() { }
        private protected virtual void PanHorizontally(int panSign, UserCommandArgs commandArgs, GameTime gameTime) { }
        private protected virtual void PanVerticallyCommandStart() { }
        private protected virtual void PanVerticallyCommandEnd() { }
        private protected virtual void PanVertically(int panSign, UserCommandArgs commandArgs, GameTime gameTime) { }
        private protected virtual void SwitchCabView(int direction) { }
        private protected virtual void Scroll(bool right, GameTime gameTime) { }
        private protected virtual void ToggleLetterboxCab() { }
        private protected virtual void ChangePassengerViewPoint() { }
        private protected virtual void CarNext() { }
        private protected virtual void CarPrevious() { }
        private protected virtual void CarFirst() { }
        private protected virtual void CarLast() { }
        private protected virtual void BrowseBackwards() { }
        private protected virtual void BrowseForwards() { }
        private protected virtual void RotateByMouseCommmand(UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers) { }
        private protected virtual void RotateByMouseCommmandStart() { }
        private protected virtual void RotateByMouseCommmandEnd() { }

        private protected virtual void CabControlClickCommand(UserCommandArgs commandArgs, KeyModifiers modifiers) { }
        private protected virtual void CabControlReleaseCommand(UserCommandArgs commandArgs, KeyModifiers modifiers) { }
        private protected virtual void CabControlPointerMovedCommand(UserCommandArgs commandArgs, KeyModifiers modifiers) { }
        private protected virtual void CabControlDraggedCommand(UserCommandArgs commandArgs, KeyModifiers modifiers) { }

        #endregion
        protected double CommandStartTime;

        // 2.1 sets the limit at just under a right angle as get unwanted swivel at the full right angle.
        protected static CameraAngleClamper VerticalClamper = new CameraAngleClamper(-MathHelper.Pi / 2.1f, MathHelper.Pi / 2.1f);
        public const int TerrainAltitudeMargin = 2;

        protected readonly Viewer Viewer;

        protected WorldLocation cameraLocation = new WorldLocation();
        public int TileX { get { return cameraLocation.TileX; } }
        public int TileZ { get { return cameraLocation.TileZ; } }
        public Vector3 Location { get { return cameraLocation.Location; } }
        public WorldLocation CameraWorldLocation { get { return cameraLocation; } }
        protected int MouseScrollValue;
        internal protected float FieldOfView;

        private Matrix xnaView;
        public ref Matrix XnaView => ref xnaView;

        private Matrix projection;
        private static Matrix skyProjection;
        private static Matrix distantMountainProjection;

        public ref Matrix XnaProjection => ref projection;

        public static ref Matrix XnaDistantMountainProjection => ref distantMountainProjection;

        // This sucks. It's really not camera-related at all.
        public static ref Matrix XNASkyProjection => ref skyProjection;

        private Vector3 frustumRightProjected;
        private Vector3 frustumLeft;
        private Vector3 frustumRight;

        // The following group of properties are used by other code to vary
        // behavior by camera; e.g. Style is used for activating sounds,
        // AttachedCar for rendering the train or not, and IsUnderground for
        // automatically switching to/from cab view in tunnels.
        public enum Styles { External, Cab, Passenger, ThreeDimCab }
        public virtual Styles Style { get { return Styles.External; } }
        public virtual TrainCar AttachedCar { get { return null; } }
        public virtual bool IsAvailable { get { return true; } }
        public virtual bool IsUnderground { get { return false; } }
        public virtual string Name { get { return ""; } }

        // We need to allow different cameras to have different near planes.
        public virtual float NearPlane { get { return 1.0f; } }

        public float ReplaySpeed { get; set; }

        private const int SpeedFactorFastSlow = 8;  // Use by GetSpeed
        protected const float SpeedAdjustmentForRotation = 0.1f;

        protected Camera(Viewer viewer)
        {
            Viewer = viewer;
            FieldOfView = Viewer.Settings.ViewingFOV;
            CameraEventHandler.Initialize(viewer);
        }

        protected Camera(Viewer viewer, Camera previousCamera) // maintain visual continuity
            : this(viewer)
        {
            if (previousCamera != null)
            {
                cameraLocation = previousCamera.CameraWorldLocation;
                FieldOfView = previousCamera.FieldOfView;
            }
        }

        internal protected virtual void Save(BinaryWriter output)
        {
            WorldLocation.Save(cameraLocation, output);
            output.Write(FieldOfView);
        }

        internal protected virtual void Restore(BinaryReader input)
        {
            cameraLocation = WorldLocation.Restore(input);
            FieldOfView = input.ReadSingle();
        }

        /// <summary>
        /// Resets a camera's position, location and attachment information.
        /// </summary>
        public virtual void Reset()
        {
            FieldOfView = Viewer.Settings.ViewingFOV;
            ScreenChanged();
        }

        /// <summary>
        /// Switches the <see cref="Viewer3D"/> to this camera, updating the view information.
        /// </summary>
        public void Activate()
        {
            ScreenChanged();
            OnActivate(Viewer.Camera == this);
            Viewer.Camera = this;
            Viewer.Simulator.PlayerIsInCab = Style == Styles.Cab || Style == Styles.ThreeDimCab;
            Update(ElapsedTime.Zero);
            xnaView = GetCameraView();
            SoundBaseTile = new Point(cameraLocation.TileX, cameraLocation.TileZ);
        }

        /// <summary>
        /// A camera can use this method to handle any preparation when being activated.
        /// </summary>
        protected virtual void OnActivate(bool sameCamera)
        {
        }

        /// <summary>
        /// A camera can use this method to update any calculated data that may have changed.
        /// </summary>
        /// <param name="elapsedTime"></param>
        public virtual void Update(in ElapsedTime elapsedTime)
        {
        }

        /// <summary>
        /// A camera should use this method to return a unique view.
        /// </summary>
        protected abstract Matrix GetCameraView();

        /// <summary>
        /// Notifies the camera that the screen dimensions have changed.
        /// </summary>
        public void ScreenChanged()
        {
            var aspectRatio = (float)Viewer.DisplaySize.X / Viewer.DisplaySize.Y;
            var farPlaneDistance = SkyConstants.skyRadius + 100;  // so far the sky is the biggest object in view
            var fovWidthRadians = MathHelper.ToRadians(FieldOfView);

            if (Viewer.Settings.DistantMountains)
                distantMountainProjection = Matrix.CreatePerspectiveFieldOfView(fovWidthRadians, aspectRatio, MathHelper.Clamp(Viewer.Settings.ViewingDistance - 500, 500, 1500), Viewer.Settings.DistantMountainsViewingDistance);
            projection = Matrix.CreatePerspectiveFieldOfView(fovWidthRadians, aspectRatio, NearPlane, Viewer.Settings.ViewingDistance);
            skyProjection = Matrix.CreatePerspectiveFieldOfView(fovWidthRadians, aspectRatio, NearPlane, farPlaneDistance);    // TODO remove? 
            frustumRightProjected.X = (float)Math.Cos(fovWidthRadians / 2 * aspectRatio);  // Precompute the right edge of the view frustrum.
            frustumRightProjected.Z = (float)Math.Sin(fovWidthRadians / 2 * aspectRatio);
        }

        /// <summary>
        /// Updates view and projection from this camera's data.
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="elapsedTime"></param>
        public void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            xnaView = GetCameraView();
            frame.SetCamera(this);
            frustumLeft.X = -xnaView.M11 * frustumRightProjected.X + xnaView.M13 * frustumRightProjected.Z;
            frustumLeft.Y = -xnaView.M21 * frustumRightProjected.X + xnaView.M23 * frustumRightProjected.Z;
            frustumLeft.Z = -xnaView.M31 * frustumRightProjected.X + xnaView.M33 * frustumRightProjected.Z;
            frustumLeft.Normalize();
            frustumRight.X = xnaView.M11 * frustumRightProjected.X + xnaView.M13 * frustumRightProjected.Z;
            frustumRight.Y = xnaView.M21 * frustumRightProjected.X + xnaView.M23 * frustumRightProjected.Z;
            frustumRight.Z = xnaView.M31 * frustumRightProjected.X + xnaView.M33 * frustumRightProjected.Z;
            frustumRight.Normalize();
        }

        // Cull for fov
        public bool InFov(Vector3 mstsObjectCenter, float objectRadius)
        {
            mstsObjectCenter.X -= cameraLocation.Location.X;
            mstsObjectCenter.Y -= cameraLocation.Location.Y;
            mstsObjectCenter.Z -= cameraLocation.Location.Z;
            // TODO: This *2 is a complete fiddle because some objects don't currently pass in a correct radius and e.g. track sections vanish.
            objectRadius *= 2;
            if (frustumLeft.X * mstsObjectCenter.X + frustumLeft.Y * mstsObjectCenter.Y - frustumLeft.Z * mstsObjectCenter.Z > objectRadius)
                return false;
            if (frustumRight.X * mstsObjectCenter.X + frustumRight.Y * mstsObjectCenter.Y - frustumRight.Z * mstsObjectCenter.Z > objectRadius)
                return false;
            return true;
        }

        // Cull for distance
        public bool InRange(Vector3 mstsObjectCenter, float objectRadius, float objectViewingDistance)
        {
            mstsObjectCenter.X -= cameraLocation.Location.X;
            mstsObjectCenter.Z -= cameraLocation.Location.Z;

            // An object cannot be visible further away than the viewing distance.
            if (objectViewingDistance > Viewer.Settings.ViewingDistance)
                objectViewingDistance = Viewer.Settings.ViewingDistance;

            var distanceSquared = mstsObjectCenter.X * mstsObjectCenter.X + mstsObjectCenter.Z * mstsObjectCenter.Z;

            return distanceSquared < (objectRadius + objectViewingDistance) * (objectRadius + objectViewingDistance);
        }

        /// <summary>
        /// If the nearest part of the object is within camera viewing distance
        /// and is within the object's defined viewing distance then
        /// we can see it.   The objectViewingDistance allows a small object
        /// to specify a cutoff beyond which the object can't be seen.
        /// </summary>
        public bool CanSee(Vector3 mstsObjectCenter, float objectRadius, float objectViewingDistance)
        {
            if (!InRange(mstsObjectCenter, objectRadius, objectViewingDistance))
                return false;

            if (!InFov(mstsObjectCenter, objectRadius))
                return false;

            return true;
        }

        private protected static float GetSpeed(GameTime gameTime, UserCommandArgs userCommandArgs, Viewer viewer)
        {
            double speed = 5 * gameTime.ElapsedGameTime.TotalSeconds;
            if (userCommandArgs is ModifiableKeyCommandArgs modifiableKeyCommandArgs)
            {
                if (modifiableKeyCommandArgs.AdditionalModifiers.HasFlag(viewer.Settings.Input.CameraMoveFastModifier))
                    speed *= SpeedFactorFastSlow;
                else if (modifiableKeyCommandArgs.AdditionalModifiers.HasFlag(viewer.Settings.Input.CameraMoveSlowModifier))
                    speed /= SpeedFactorFastSlow;
            }
            return (float)speed;
        }

        private protected static float GetSpeed(GameTime gameTime, UserCommandArgs userCommandArgs, KeyModifiers modifiers, Viewer viewer)
        {
            if (userCommandArgs is ScrollCommandArgs scrollCommandArgs)
            {
                double speed = 5 * gameTime.ElapsedGameTime.TotalSeconds;
                if (modifiers.HasFlag(viewer.Settings.Input.CameraMoveFastModifier))
                    speed *= SpeedFactorFastSlow;
                else if (modifiers.HasFlag(viewer.Settings.Input.CameraMoveSlowModifier))
                    speed /= SpeedFactorFastSlow;
                return (float)speed * scrollCommandArgs.Delta;
            }
            return 0;
        }

        protected virtual void ZoomIn(float speed)
        {
        }

        // TODO: Add a way to record this zoom operation for Replay.
        private protected void ZoomByMouse(UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers, float speedAdjustmentFactor = 1)
        {
            // Will not zoom-in-out when help windows is up.
            if (Viewer.HelpWindow.Visible)
                return;

            float fieldOfView = MathHelper.Clamp(FieldOfView - GetSpeed(gameTime, commandArgs, modifiers, Viewer) * speedAdjustmentFactor / 10, 1, 135);
            _ = new FieldOfViewCommand(Viewer.Log, fieldOfView);
        }

        /// <summary>
        /// Returns a position in XNA space relative to the camera's tile
        /// </summary>
        /// <param name="worldLocation"></param>
        /// <returns></returns>
        public Vector3 XnaLocation(in WorldLocation worldLocation)
        {
            Vector3 xnaVector = worldLocation.Location;
            xnaVector.X += 2048 * (worldLocation.TileX - cameraLocation.TileX);
            xnaVector.Z += 2048 * (worldLocation.TileZ - cameraLocation.TileZ);
            xnaVector.Z *= -1;
            return xnaVector;
        }


        protected class CameraAngleClamper
        {
            private readonly float Minimum;
            private readonly float Maximum;

            public CameraAngleClamper(float minimum, float maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }

            public float Clamp(float angle)
            {
                return MathHelper.Clamp(angle, Minimum, Maximum);
            }
        }

        /// <summary>
        /// All OpenAL sound positions are normalized to this tile.
        /// Cannot be (0, 0) constantly, because some routes use extremely large tile coordinates,
        /// which would lead to imprecise absolute world coordinates, thus stuttering.
        /// </summary>
        public static Point SoundBaseTile = new Point(0, 0);

        /// <summary>
        /// Set OpenAL listener position based on CameraWorldLocation normalized to SoundBaseTile
        /// </summary>
        public void UpdateListener()
        {
            Vector3 listenerLocation = CameraWorldLocation.NormalizeTo(SoundBaseTile.X, SoundBaseTile.Y).Location;
            float[] cameraPosition = new float[] {
                        listenerLocation.X,
                        listenerLocation.Y,
                        listenerLocation.Z};

            float[] cameraVelocity = new float[] { 0, 0, 0 };

            if (!(this is TracksideCamera) && !(this is FreeRoamCamera) && AttachedCar != null)
            {
                var cars = Viewer.World.Trains.Cars;
                if (cars.ContainsKey(AttachedCar))
                    cameraVelocity = cars[AttachedCar].Velocity;
                else
                    cameraVelocity = new float[] { 0, 0, 0 };
            }

            float[] cameraOrientation = new float[] {
                        XnaView.Backward.X, XnaView.Backward.Y, XnaView.Backward.Z,
                        XnaView.Down.X, XnaView.Down.Y, XnaView.Down.Z };

            OpenAL.alListenerfv(OpenAL.AL_POSITION, cameraPosition);
            OpenAL.alListenerfv(OpenAL.AL_VELOCITY, cameraVelocity);
            OpenAL.alListenerfv(OpenAL.AL_ORIENTATION, cameraOrientation);
        }
    }

    public abstract class LookAtCamera : RotatingCamera
    {
        protected WorldLocation targetLocation = new WorldLocation();
        public WorldLocation TargetWorldLocation { get { return targetLocation; } }

        public override bool IsUnderground
        {
            get
            {
                var elevationAtTarget = Viewer.Tiles.GetElevation(targetLocation);
                return targetLocation.Location.Y + TerrainAltitudeMargin < elevationAtTarget;
            }
        }

        protected LookAtCamera(Viewer viewer)
            : base(viewer)
        {
        }

        internal protected override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            WorldLocation.Save(targetLocation, outf);
        }

        internal protected override void Restore(BinaryReader inf)
        {
            base.Restore(inf);
            targetLocation = WorldLocation.Restore(inf);
        }

        protected override Matrix GetCameraView()
        {
            return Matrix.CreateLookAt(XnaLocation(cameraLocation), XnaLocation(targetLocation), Vector3.UnitY);
        }
    }

    public abstract class RotatingCamera : Camera
    {
        // Current camera values
        protected float RotationXRadians;
        protected float RotationYRadians;
        protected float XRadians;
        protected float YRadians;
        protected float ZRadians;

        // Target camera values
        public float? RotationXTargetRadians;
        public float? RotationYTargetRadians;
        public float? XTargetRadians;
        public float? YTargetRadians;
        public float? ZTargetRadians;
        public double EndTime;

        protected RotatingCamera(Viewer viewer)
            : base(viewer)
        {
        }

        protected RotatingCamera(Viewer viewer, Camera previousCamera)
            : base(viewer, previousCamera)
        {
            if (previousCamera != null)
            {
                previousCamera.XnaView.MatrixToAngles(out float h, out float a, out float b);
                RotationXRadians = -b;
                RotationYRadians = -h;
            }
        }

        internal protected override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            outf.Write(RotationXRadians);
            outf.Write(RotationYRadians);
        }

        internal protected override void Restore(BinaryReader inf)
        {
            base.Restore(inf);
            RotationXRadians = inf.ReadSingle();
            RotationYRadians = inf.ReadSingle();
        }

        public override void Reset()
        {
            base.Reset();
            RotationXRadians = RotationYRadians = XRadians = YRadians = ZRadians = 0;
        }

        protected override Matrix GetCameraView()
        {
            var lookAtPosition = Vector3.UnitZ;
            lookAtPosition = Vector3.Transform(lookAtPosition, Matrix.CreateRotationX(RotationXRadians));
            lookAtPosition = Vector3.Transform(lookAtPosition, Matrix.CreateRotationY(RotationYRadians));
            lookAtPosition += cameraLocation.Location;
            lookAtPosition.Z *= -1;
            return Matrix.CreateLookAt(XnaLocation(cameraLocation), lookAtPosition, Vector3.Up);
        }

        private protected static float GetMouseDelta(float delta, GameTime gameTime, KeyModifiers keyModifiers, Viewer viewer)
        {
            // Ignore CameraMoveFast as that is too fast to be useful
            delta *= 0.005f;
            if (keyModifiers.HasFlag(viewer.Settings.Input.CameraMoveSlowModifier))
                delta *= 0.1f;
            return delta;
        }

        private protected virtual void RotateByMouse(UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers)
        {
            PointerMoveCommandArgs pointerMoveCommandArgs = commandArgs as PointerMoveCommandArgs;

            // Mouse movement doesn't use 'var speed' because the MouseMove 
            // parameters are already scaled down with increasing frame rates, 
            RotationXRadians += GetMouseDelta(pointerMoveCommandArgs.Delta.Y, gameTime, modifiers, Viewer);
            RotationYRadians += GetMouseDelta(pointerMoveCommandArgs.Delta.X, gameTime, modifiers, Viewer);
        }

        private protected override void RotateByMouseCommmandStart()
        {
            Viewer.CheckReplaying();
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void RotateByMouseCommmandEnd()
        {
            var commandEndTime = Viewer.Simulator.ClockTime;
            _ = new CameraMouseRotateCommand(Viewer.Log, CommandStartTime, commandEndTime, RotationXRadians, RotationYRadians);
        }

        protected void UpdateRotation(in ElapsedTime elapsedTime)
        {
            var replayRemainingS = EndTime - Viewer.Simulator.ClockTime;
            if (replayRemainingS > 0)
            {
                var replayFraction = elapsedTime.ClockSeconds / replayRemainingS;
                if (RotationXTargetRadians != null && RotationYTargetRadians != null)
                {
                    var replayRemainingX = RotationXTargetRadians - RotationXRadians;
                    var replayRemainingY = RotationYTargetRadians - RotationYRadians;
                    var replaySpeedX = (float)(replayRemainingX * replayFraction);
                    var replaySpeedY = (float)(replayRemainingY * replayFraction);

                    if (IsCloseEnough(RotationXRadians, RotationXTargetRadians, replaySpeedX))
                    {
                        RotationXTargetRadians = null;
                    }
                    else
                    {
                        RotateDown(replaySpeedX);
                    }
                    if (IsCloseEnough(RotationYRadians, RotationYTargetRadians, replaySpeedY))
                    {
                        RotationYTargetRadians = null;
                    }
                    else
                    {
                        RotateRight(replaySpeedY);
                    }
                }
                else
                {
                    if (RotationXTargetRadians != null)
                    {
                        var replayRemainingX = RotationXTargetRadians - RotationXRadians;
                        var replaySpeedX = (float)(replayRemainingX * replayFraction);
                        if (IsCloseEnough(RotationXRadians, RotationXTargetRadians, replaySpeedX))
                        {
                            RotationXTargetRadians = null;
                        }
                        else
                        {
                            RotateDown(replaySpeedX);
                        }
                    }
                    if (RotationYTargetRadians != null)
                    {
                        var replayRemainingY = RotationYTargetRadians - RotationYRadians;
                        var replaySpeedY = (float)(replayRemainingY * replayFraction);
                        if (IsCloseEnough(RotationYRadians, RotationYTargetRadians, replaySpeedY))
                        {
                            RotationYTargetRadians = null;
                        }
                        else
                        {
                            RotateRight(replaySpeedY);
                        }
                    }
                }
            }
        }

        protected virtual void RotateDown(float speed)
        {
            RotationXRadians += speed;
            RotationXRadians = VerticalClamper.Clamp(RotationXRadians);
            MoveCamera();
        }

        protected virtual void RotateRight(float speed)
        {
            RotationYRadians += speed;
            MoveCamera();
        }

        protected void MoveCamera()
        {
            MoveCamera(new Vector3(0, 0, 0));
        }

        protected void MoveCamera(Vector3 movement)
        {
            movement = Vector3.Transform(movement, Matrix.CreateRotationX(RotationXRadians));
            movement = Vector3.Transform(movement, Matrix.CreateRotationY(RotationYRadians));
            cameraLocation = new WorldLocation(cameraLocation.TileX, cameraLocation.TileZ, cameraLocation.Location + movement, true);
        }

        /// <summary>
        /// A margin of half a step (increment/2) is used to prevent hunting once the target is reached.
        /// </summary>
        /// <param name="current"></param>
        /// <param name="target"></param>
        /// <param name="increment"></param>
        /// <returns></returns>
        protected static bool IsCloseEnough(float current, float? target, float increment)
        {
            Trace.Assert(target != null, "Camera target position must not be null");
            // If a pause interrupts a camera movement, then the increment will become zero.
            if (increment == 0)
            {  // To avoid divide by zero error, just kill the movement.
                return true;
            }
            else
            {
                var error = (float)target - current;
                return error / increment < 0.5;
            }
        }
    }

    public class FreeRoamCamera : RotatingCamera
    {
        private const float maxCameraHeight = 1000f;
        private const float ZoomFactor = 2f;

        public override string Name { get { return Viewer.Catalog.GetString("Free"); } }

        public FreeRoamCamera(Viewer viewer, Camera previousCamera)
            : base(viewer, previousCamera)
        {
        }

        public void SetLocation(in WorldLocation location)
        {
            cameraLocation = location;
        }

        public override void Reset()
        {
            // Intentionally do nothing at all.
        }

        private protected override void ZoomCommandStart()
        {
            Viewer.CheckReplaying();
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void ZoomCommandEnd()
        {
            _ = new CameraZCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, ZRadians);
        }

        private protected override void Zoom(int zoomSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer);
            ZoomIn(zoomSign * speed * ZoomFactor);
        }

        private protected override void RotateHorizontallyCommandStart()
        {
            Viewer.CheckReplaying();
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void RotateHorizontallyCommandEnd()
        {
            _ = new CameraRotateLeftRightCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationYRadians);
        }

        private protected override void RotateHorizontally(int rotateSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer) * SpeedAdjustmentForRotation;
            RotateRight(rotateSign * speed);
        }

        private protected override void RotateVerticallyCommandStart()
        {
            Viewer.CheckReplaying();
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void RotateVerticallyCommandEnd()
        {
            _ = new CameraRotateUpDownCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationXRadians);
        }

        private protected override void RotateVertically(int rotateSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer) * SpeedAdjustmentForRotation;
            RotateDown(-rotateSign * speed);
        }

        private protected override void PanHorizontallyCommandStart()
        {
            Viewer.CheckReplaying();
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void PanHorizontallyCommandEnd()
        {
            _ = new CameraXCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, XRadians);
        }

        private protected override void PanHorizontally(int panSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer);
            PanRight(panSign * speed);
        }

        private protected override void PanVerticallyCommandStart()
        {
            Viewer.CheckReplaying();
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void PanVerticallyCommandEnd()
        {
            _ = new CameraYCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, YRadians);
        }

        private protected override void PanVertically(int panSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer);
            PanUp(panSign * speed);
        }

        private protected override void ZoomByMouseCommmand(UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers)
        {
            ZoomByMouse(commandArgs, gameTime, modifiers);
        }

        private protected override void RotateByMouseCommmand(UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers)
        {
            RotateByMouse(commandArgs, gameTime, modifiers);
        }

        public override void Update(in ElapsedTime elapsedTime)
        {
            UpdateRotation(elapsedTime);

            var replayRemainingS = EndTime - Viewer.Simulator.ClockTime;
            if (replayRemainingS > 0)
            {
                var replayFraction = elapsedTime.ClockSeconds / replayRemainingS;
                // Panning
                if (XTargetRadians != null)
                {
                    var replayRemainingX = XTargetRadians - XRadians;
                    var replaySpeedX = Math.Abs((float)(replayRemainingX * replayFraction));
                    if (IsCloseEnough(XRadians, XTargetRadians, replaySpeedX))
                    {
                        XTargetRadians = null;
                    }
                    else
                    {
                        PanRight(replaySpeedX);
                    }
                }
                if (YTargetRadians != null)
                {
                    var replayRemainingY = YTargetRadians - YRadians;
                    var replaySpeedY = Math.Abs((float)(replayRemainingY * replayFraction));
                    if (IsCloseEnough(YRadians, YTargetRadians, replaySpeedY))
                    {
                        YTargetRadians = null;
                    }
                    else
                    {
                        PanUp(replaySpeedY);
                    }
                }
                // Zooming
                if (ZTargetRadians != null)
                {
                    var replayRemainingZ = ZTargetRadians - ZRadians;
                    var replaySpeedZ = Math.Abs((float)(replayRemainingZ * replayFraction));
                    if (IsCloseEnough(ZRadians, ZTargetRadians, replaySpeedZ))
                    {
                        ZTargetRadians = null;
                    }
                    else
                    {
                        ZoomIn(replaySpeedZ);
                    }
                }
            }
            UpdateListener();
        }

        protected virtual void PanRight(float speed)
        {
            var movement = new Vector3(0, 0, 0);
            movement.X += speed;
            XRadians += movement.X;
            MoveCamera(movement);
        }

        protected virtual void PanUp(float speed)
        {
            var movement = new Vector3(0, 0, 0);
            movement.Y += speed;
            movement.Y = VerticalClamper.Clamp(movement.Y);    // Only the vertical needs to be clamped
            YRadians += movement.Y;
            MoveCamera(movement);
        }

        protected override void ZoomIn(float speed)
        {
            var movement = new Vector3(0, 0, 0);
            movement.Z += speed;
            ZRadians += movement.Z;
            MoveCamera(movement);
        }
    }

    public abstract class AttachedCamera : RotatingCamera
    {
        protected TrainCar attachedCar;
        public override TrainCar AttachedCar { get { return attachedCar; } }
        public bool tiltingLand;
        protected Vector3 attachedLocation;
        protected WorldPosition LookedAtPosition = WorldPosition.None;

        protected AttachedCamera(Viewer viewer)
            : base(viewer)
        {
        }

        internal protected override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            if (attachedCar != null && attachedCar.Train != null && attachedCar.Train == Viewer.SelectedTrain)
                outf.Write(Viewer.SelectedTrain.Cars.IndexOf(attachedCar));
            else
                outf.Write((int)-1);
            outf.Write(attachedLocation.X);
            outf.Write(attachedLocation.Y);
            outf.Write(attachedLocation.Z);
        }

        internal protected override void Restore(BinaryReader inf)
        {
            base.Restore(inf);
            var carIndex = inf.ReadInt32();
            if (carIndex != -1 && Viewer.SelectedTrain != null)
            {
                if (carIndex < Viewer.SelectedTrain.Cars.Count)
                    attachedCar = Viewer.SelectedTrain.Cars[carIndex];
                else if (Viewer.SelectedTrain.Cars.Count > 0)
                    attachedCar = Viewer.SelectedTrain.Cars[Viewer.SelectedTrain.Cars.Count - 1];
            }
            attachedLocation.X = inf.ReadSingle();
            attachedLocation.Y = inf.ReadSingle();
            attachedLocation.Z = inf.ReadSingle();
        }

        protected override void OnActivate(bool sameCamera)
        {
            if (attachedCar == null || attachedCar.Train != Viewer.SelectedTrain)
            {
                if (Viewer.SelectedTrain.MUDirection != MidpointDirection.Reverse)
                    SetCameraCar(GetCameraCars().First());
                else
                    SetCameraCar(GetCameraCars().Last());
            }
            base.OnActivate(sameCamera);
        }

        protected virtual List<TrainCar> GetCameraCars()
        {
            if (Viewer.SelectedTrain.TrainType == TrainType.AiIncorporated) Viewer.ChangeSelectedTrain(Viewer.SelectedTrain.IncorporatingTrain);
            return Viewer.SelectedTrain.Cars;
        }

        protected virtual void SetCameraCar(TrainCar car)
        {
            attachedCar = car;
        }

        protected virtual bool IsCameraFlipped()
        {
            return false;
        }

        public virtual void NextCar()
        {
            var trainCars = GetCameraCars();
            SetCameraCar(attachedCar == trainCars.First() ? attachedCar : trainCars[trainCars.IndexOf(attachedCar) - 1]);
        }

        public virtual void PreviousCar()
        {
            var trainCars = GetCameraCars();
            SetCameraCar(attachedCar == trainCars.Last() ? attachedCar : trainCars[trainCars.IndexOf(attachedCar) + 1]);
        }

        public virtual void FirstCar()
        {
            var trainCars = GetCameraCars();
            SetCameraCar(trainCars.First());
        }

        public virtual void LastCar()
        {
            var trainCars = GetCameraCars();
            SetCameraCar(trainCars.Last());
        }

        public void UpdateLocation(in WorldPosition worldPosition)
        {
            Vector3 source = IsCameraFlipped() ? new Vector3(-attachedLocation.X, attachedLocation.Y, attachedLocation.Z) :
                new Vector3(attachedLocation.X, attachedLocation.Y, -attachedLocation.Z);
            Vector3.Transform(source, worldPosition.XNAMatrix).Deconstruct(out float x, out float y, out float z);
            cameraLocation = new WorldLocation(worldPosition.TileX, worldPosition.TileZ, x, y, -z);
        }

        protected override Matrix GetCameraView()
        {
            var flipped = IsCameraFlipped();
            var lookAtPosition = Vector3.UnitZ;
            lookAtPosition = Vector3.Transform(lookAtPosition, Matrix.CreateRotationX(RotationXRadians));
            lookAtPosition = Vector3.Transform(lookAtPosition, Matrix.CreateRotationY(RotationYRadians + (flipped ? MathHelper.Pi : 0)));
            if (flipped)
            {
                lookAtPosition.X -= attachedLocation.X;
                lookAtPosition.Y += attachedLocation.Y;
                lookAtPosition.Z -= attachedLocation.Z;
            }
            else
            {
                lookAtPosition.X += attachedLocation.X;
                lookAtPosition.Y += attachedLocation.Y;
                lookAtPosition.Z += attachedLocation.Z;
            }
            lookAtPosition.Z *= -1;
            lookAtPosition = Vector3.Transform(lookAtPosition, Viewer.Camera is TrackingCamera ? LookedAtPosition.XNAMatrix : attachedCar.WorldPosition.XNAMatrix);
            // Don't forget to rotate the up vector so the camera rotates with us.
            Vector3 up;
            if (Viewer.Camera is TrackingCamera)
                up = Vector3.Up;
            else
            {
                var upRotation = attachedCar.WorldPosition.XNAMatrix;
                upRotation.Translation = Vector3.Zero;
                up = Vector3.Transform(Vector3.Up, upRotation);
            }
            return Matrix.CreateLookAt(XnaLocation(cameraLocation), lookAtPosition, up);
        }

        public override void Update(in ElapsedTime elapsedTime)
        {
            if (attachedCar != null)
            {
                Vector3 source = IsCameraFlipped() ? new Vector3(-attachedLocation.X, attachedLocation.Y, attachedLocation.Z) :
                    new Vector3(attachedLocation.X, attachedLocation.Y, -attachedLocation.Z);
                Vector3.Transform(source, attachedCar.WorldPosition.XNAMatrix).Deconstruct(out float x, out float y, out float z);
                cameraLocation = new WorldLocation(attachedCar.WorldPosition.TileX, attachedCar.WorldPosition.TileZ, x, y, -z);
            }
            UpdateRotation(elapsedTime);
            UpdateListener();
        }
    }

    public class TrackingCamera : AttachedCamera
    {
        private const float StartPositionDistance = 20;
        private const float StartPositionXRadians = 0.399f;
        private const float StartPositionYRadians = 0.387f;

        protected readonly bool Front;
        public enum AttachedTo { Front, Rear }

        private const float ZoomFactor = 0.1f;

        protected float PositionDistance = StartPositionDistance;
        protected float PositionXRadians = StartPositionXRadians;
        protected float PositionYRadians = StartPositionYRadians;
        public float? PositionDistanceTargetMetres;
        public float? PositionXTargetRadians;
        public float? PositionYTargetRadians;

        protected bool browseBackwards;
        protected bool browseForwards;
        private const float BrowseSpeedMpS = 4;
        protected float ZDistanceM; // used to browse train;
        protected Traveller browsedTraveller;
        protected float BrowseDistance = 20;
        public bool BrowseMode = false;
        protected float LowWagonOffsetLimit;
        protected float HighWagonOffsetLimit;

        public override bool IsUnderground
        {
            get
            {
                var elevationAtTrain = Viewer.Tiles.GetElevation(LookedAtPosition.WorldLocation);
                var elevationAtCamera = Viewer.Tiles.GetElevation(cameraLocation);
                return LookedAtPosition.WorldLocation.Location.Y + TerrainAltitudeMargin < elevationAtTrain || cameraLocation.Location.Y + TerrainAltitudeMargin < elevationAtCamera;
            }
        }
        public override string Name { get { return Front ? Viewer.Catalog.GetString("Outside Front") : Viewer.Catalog.GetString("Outside Rear"); } }

        public TrackingCamera(Viewer viewer, AttachedTo attachedTo)
            : base(viewer)
        {
            Front = attachedTo == AttachedTo.Front;
            PositionYRadians = StartPositionYRadians + (Front ? 0 : MathHelper.Pi);
            RotationXRadians = PositionXRadians;
            RotationYRadians = PositionYRadians - MathHelper.Pi;
        }

        internal protected override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            outf.Write(PositionDistance);
            outf.Write(PositionXRadians);
            outf.Write(PositionYRadians);
            outf.Write(BrowseMode);
            outf.Write(browseForwards);
            outf.Write(browseBackwards);
            outf.Write(ZDistanceM);
        }

        internal protected override void Restore(BinaryReader inf)
        {
            base.Restore(inf);
            PositionDistance = inf.ReadSingle();
            PositionXRadians = inf.ReadSingle();
            PositionYRadians = inf.ReadSingle();
            BrowseMode = inf.ReadBoolean();
            browseForwards = inf.ReadBoolean();
            browseBackwards = inf.ReadBoolean();
            ZDistanceM = inf.ReadSingle();
            if (attachedCar != null && attachedCar.Train == Viewer.SelectedTrain)
            {
                var trainCars = GetCameraCars();
                BrowseDistance = attachedCar.CarLengthM * 0.5f;
                if (Front)
                {
                    browsedTraveller = new Traveller(attachedCar.Train.FrontTDBTraveller);
                    browsedTraveller.Move(-attachedCar.CarLengthM * 0.5f + ZDistanceM);
                }
                else
                {
                    browsedTraveller = new Traveller(attachedCar.Train.RearTDBTraveller);
                    browsedTraveller.Move((attachedCar.CarLengthM - trainCars.First().CarLengthM - trainCars.Last().CarLengthM) * 0.5f + attachedCar.Train.Length - ZDistanceM);
                }
                //               LookedAtPosition = new WorldPosition(attachedCar.WorldPosition);
                ComputeCarOffsets(this);
            }
        }

        public override void Reset()
        {
            base.Reset();
            PositionDistance = StartPositionDistance;
            PositionXRadians = StartPositionXRadians;
            PositionYRadians = StartPositionYRadians + (Front ? 0 : MathHelper.Pi);
            RotationXRadians = PositionXRadians;
            RotationYRadians = PositionYRadians - MathHelper.Pi;
        }

        protected override void OnActivate(bool sameCamera)
        {
            BrowseMode = browseForwards = browseBackwards = false;
            if (attachedCar == null || attachedCar.Train != Viewer.SelectedTrain)
            {
                if (Front)
                {
                    SetCameraCar(GetCameraCars().First());
                    browsedTraveller = new Traveller(attachedCar.Train.FrontTDBTraveller);
                    ZDistanceM = -attachedCar.CarLengthM / 2;
                    HighWagonOffsetLimit = 0;
                    LowWagonOffsetLimit = -attachedCar.CarLengthM;
                }
                else
                {
                    var trainCars = GetCameraCars();
                    SetCameraCar(trainCars.Last());
                    browsedTraveller = new Traveller(attachedCar.Train.RearTDBTraveller);
                    ZDistanceM = -attachedCar.Train.Length + (trainCars.First().CarLengthM + trainCars.Last().CarLengthM) * 0.5f + attachedCar.CarLengthM / 2;
                    LowWagonOffsetLimit = -attachedCar.Train.Length + trainCars.First().CarLengthM * 0.5f;
                    HighWagonOffsetLimit = LowWagonOffsetLimit + attachedCar.CarLengthM;
                }
                BrowseDistance = attachedCar.CarLengthM * 0.5f;
            }
            base.OnActivate(sameCamera);
        }

        protected override bool IsCameraFlipped()
        {
            return BrowseMode ? false : attachedCar.Flipped;
        }

        private protected override void ZoomCommandStart()
        {
            Viewer.CheckReplaying();
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void ZoomCommandEnd()
        {
            _ = new TrackingCameraZCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, PositionDistance);
        }

        private protected override void Zoom(int zoomSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer);
            ZoomIn(zoomSign * speed * ZoomFactor);
        }

        private protected override void RotateHorizontallyCommandStart()
        {
            Viewer.CheckReplaying();
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void RotateHorizontallyCommandEnd()
        {
            _ = new CameraRotateLeftRightCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationYRadians);
        }

        private protected override void RotateHorizontally(int rotateSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer) * SpeedAdjustmentForRotation;
            RotateRight(rotateSign * speed);
        }

        private protected override void RotateVerticallyCommandStart()
        {
            Viewer.CheckReplaying();
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void RotateVerticallyCommandEnd()
        {
            _ = new CameraRotateUpDownCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationXRadians);
        }

        private protected override void RotateVertically(int rotateSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer) * SpeedAdjustmentForRotation;
            RotateDown(-rotateSign * speed);
        }

        private protected override void PanHorizontallyCommandStart()
        {
            Viewer.CheckReplaying();
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void PanHorizontallyCommandEnd()
        {
            _ = new TrackingCameraXCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, PositionXRadians);
        }

        private protected override void PanHorizontally(int panSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer) * SpeedAdjustmentForRotation;
            PanRight(panSign * speed);
        }

        private protected override void PanVerticallyCommandStart()
        {
            Viewer.CheckReplaying();
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void PanVerticallyCommandEnd()
        {
            _ = new TrackingCameraYCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, PositionYRadians);
        }

        private protected override void PanVertically(int panSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer) * SpeedAdjustmentForRotation;
            PanUp(panSign * speed);
        }

        private protected override void CarFirst()
        {
            _ = new FirstCarCommand(Viewer.Log);
        }

        private protected override void CarLast()
        {
            _ = new LastCarCommand(Viewer.Log);
        }

        private protected override void CarPrevious()
        {
            _ = new PreviousCarCommand(Viewer.Log);
        }

        private protected override void CarNext()
        {
            _ = new NextCarCommand(Viewer.Log);
        }

        private protected override void BrowseForwards()
        {
            _ = new ToggleBrowseForwardsCommand(Viewer.Log);
        }

        private protected override void BrowseBackwards()
        {
            _ = new ToggleBrowseBackwardsCommand(Viewer.Log);
        }

        private protected override void ZoomByMouseCommmand(UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers)
        {
            ZoomByMouse(commandArgs, gameTime, modifiers);
        }

        private protected override void RotateByMouseCommmand(UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers)
        {
            base.RotateByMouse(commandArgs, gameTime, modifiers);
        }

        public override void Update(in ElapsedTime elapsedTime)
        {
            var replayRemainingS = EndTime - Viewer.Simulator.ClockTime;
            if (replayRemainingS > 0)
            {
                var replayFraction = elapsedTime.ClockSeconds / replayRemainingS;
                // Panning
                if (PositionXTargetRadians != null)
                {
                    var replayRemainingX = PositionXTargetRadians - PositionXRadians;
                    var replaySpeedX = (float)(replayRemainingX * replayFraction);
                    if (IsCloseEnough(PositionXRadians, PositionXTargetRadians, replaySpeedX))
                    {
                        PositionXTargetRadians = null;
                    }
                    else
                    {
                        PanUp(replaySpeedX);
                    }
                }
                if (PositionYTargetRadians != null)
                {
                    var replayRemainingY = PositionYTargetRadians - PositionYRadians;
                    var replaySpeedY = (float)(replayRemainingY * replayFraction);
                    if (IsCloseEnough(PositionYRadians, PositionYTargetRadians, replaySpeedY))
                    {
                        PositionYTargetRadians = null;
                    }
                    else
                    {
                        PanRight(replaySpeedY);
                    }
                }
                // Zooming
                if (PositionDistanceTargetMetres != null)
                {
                    var replayRemainingZ = PositionDistanceTargetMetres - PositionDistance;
                    var replaySpeedZ = (float)(replayRemainingZ * replayFraction);
                    if (IsCloseEnough(PositionDistance, PositionDistanceTargetMetres, replaySpeedZ))
                    {
                        PositionDistanceTargetMetres = null;
                    }
                    else
                    {
                        ZoomIn(replaySpeedZ / PositionDistance);
                    }
                }
            }

            // Rotation
            UpdateRotation(elapsedTime);

            // Update location of attachment
            attachedLocation.X = 0;
            attachedLocation.Y = 2;
            attachedLocation.Z = PositionDistance;
            attachedLocation = Vector3.Transform(attachedLocation, Matrix.CreateRotationX(-PositionXRadians));
            attachedLocation = Vector3.Transform(attachedLocation, Matrix.CreateRotationY(PositionYRadians));

            // Update location of camera
            if (BrowseMode)
            {
                UpdateTrainBrowsing(elapsedTime);
                attachedLocation.Z += BrowseDistance * (Front ? 1 : -1);
                LookedAtPosition = new WorldPosition(browsedTraveller.TileX, browsedTraveller.TileZ,
                    Matrix.CreateFromYawPitchRoll(-browsedTraveller.RotY, 0, 0)).SetTranslation(browsedTraveller.X, browsedTraveller.Y, -browsedTraveller.Z);
            }
            else if (attachedCar != null)
            {
                LookedAtPosition = attachedCar.WorldPosition;
            }
            UpdateLocation(LookedAtPosition);
            UpdateListener();
        }

        protected void UpdateTrainBrowsing(in ElapsedTime elapsedTime)
        {
            var trainCars = GetCameraCars();
            if (browseBackwards)
            {
                var ZIncrM = -BrowseSpeedMpS * elapsedTime.ClockSeconds;
                ZDistanceM += (float)ZIncrM;
                if (-ZDistanceM >= attachedCar.Train.Length - (trainCars.First().CarLengthM + trainCars.Last().CarLengthM) * 0.5f)
                {
                    ZIncrM = -attachedCar.Train.Length + (trainCars.First().CarLengthM + trainCars.Last().CarLengthM) * 0.5f - (ZDistanceM - ZIncrM);
                    ZDistanceM = -attachedCar.Train.Length + (trainCars.First().CarLengthM + trainCars.Last().CarLengthM) * 0.5f;
                    browseBackwards = false;
                }
                else if (ZDistanceM < LowWagonOffsetLimit)
                {
                    base.PreviousCar();
                    HighWagonOffsetLimit = LowWagonOffsetLimit;
                    LowWagonOffsetLimit -= attachedCar.CarLengthM;
                }
                browsedTraveller.Move(elapsedTime.ClockSeconds * attachedCar.Train.SpeedMpS + ZIncrM);
            }
            else if (browseForwards)
            {
                var ZIncrM = BrowseSpeedMpS * elapsedTime.ClockSeconds;
                ZDistanceM += (float)ZIncrM;
                if (ZDistanceM >= 0)
                {
                    ZIncrM = ZIncrM - ZDistanceM;
                    ZDistanceM = 0;
                    browseForwards = false;
                }
                else if (ZDistanceM > HighWagonOffsetLimit)
                {
                    base.NextCar();
                    LowWagonOffsetLimit = HighWagonOffsetLimit;
                    HighWagonOffsetLimit += attachedCar.CarLengthM;
                }
                browsedTraveller.Move(elapsedTime.ClockSeconds * attachedCar.Train.SpeedMpS + ZIncrM);
            }
            else browsedTraveller.Move(elapsedTime.ClockSeconds * attachedCar.Train.SpeedMpS);
        }

        protected void ComputeCarOffsets(TrackingCamera camera)
        {
            var trainCars = camera.GetCameraCars();
            camera.HighWagonOffsetLimit = trainCars.First().CarLengthM * 0.5f;
            foreach (TrainCar trainCar in trainCars)
            {
                camera.LowWagonOffsetLimit = camera.HighWagonOffsetLimit - trainCar.CarLengthM;
                if (ZDistanceM > LowWagonOffsetLimit) break;
                else camera.HighWagonOffsetLimit = camera.LowWagonOffsetLimit;
            }
        }

        protected void PanUp(float speed)
        {
            PositionXRadians += speed;
            PositionXRadians = VerticalClamper.Clamp(PositionXRadians);
            RotationXRadians += speed;
            RotationXRadians = VerticalClamper.Clamp(RotationXRadians);
        }

        protected void PanRight(float speed)
        {
            PositionYRadians += speed;
            RotationYRadians += speed;
        }

        protected override void ZoomIn(float speed)
        {
            // Speed depends on distance, slows down when zooming in, speeds up zooming out.
            PositionDistance += speed * PositionDistance;
            PositionDistance = MathHelper.Clamp(PositionDistance, 1, 100);
        }

        /// <summary>
        /// Swaps front and rear tracking camera after reversal point, to avoid abrupt change of picture
        /// </summary>

        public void SwapCameras()
        {
            if (Front)
            {
                SwapParams(this, Viewer.BackCamera);
                Viewer.BackCamera.Activate();
            }
            else
            {
                SwapParams(this, Viewer.FrontCamera);
                Viewer.FrontCamera.Activate();
            }
        }


        /// <summary>
        /// Swaps parameters of Front and Back Camera
        /// </summary>
        /// 
        protected void SwapParams(TrackingCamera oldCamera, TrackingCamera newCamera)
        {
            TrainCar swapCar = newCamera.attachedCar;
            newCamera.attachedCar = oldCamera.attachedCar;
            oldCamera.attachedCar = swapCar;
            float swapFloat = newCamera.PositionDistance;
            newCamera.PositionDistance = oldCamera.PositionDistance;
            oldCamera.PositionDistance = swapFloat;
            swapFloat = newCamera.PositionXRadians;
            newCamera.PositionXRadians = oldCamera.PositionXRadians;
            oldCamera.PositionXRadians = swapFloat;
            swapFloat = newCamera.PositionYRadians;
            newCamera.PositionYRadians = oldCamera.PositionYRadians + MathHelper.Pi * (Front ? 1 : -1);
            oldCamera.PositionYRadians = swapFloat - MathHelper.Pi * (Front ? 1 : -1);
            swapFloat = newCamera.RotationXRadians;
            newCamera.RotationXRadians = oldCamera.RotationXRadians;
            oldCamera.RotationXRadians = swapFloat;
            swapFloat = newCamera.RotationYRadians;
            newCamera.RotationYRadians = oldCamera.RotationYRadians - MathHelper.Pi * (Front ? 1 : -1);
            oldCamera.RotationYRadians = swapFloat + MathHelper.Pi * (Front ? 1 : -1);

            // adjust and swap data for camera browsing

            newCamera.browseForwards = newCamera.browseBackwards = false;
            var trainCars = newCamera.GetCameraCars();
            newCamera.ZDistanceM = -newCamera.attachedCar.Train.Length + (trainCars.First().CarLengthM + trainCars.Last().CarLengthM) * 0.5f - oldCamera.ZDistanceM;
            ComputeCarOffsets(newCamera);
            // Todo travellers
        }


        public override void NextCar()
        {
            browseBackwards = false;
            browseForwards = false;
            BrowseMode = false;
            var trainCars = GetCameraCars();
            var wasFirstCar = attachedCar == trainCars.First();
            base.NextCar();
            if (!wasFirstCar)
            {
                LowWagonOffsetLimit = HighWagonOffsetLimit;
                HighWagonOffsetLimit += attachedCar.CarLengthM;
                ZDistanceM = LowWagonOffsetLimit + attachedCar.CarLengthM * 0.5f;
            }
            //           LookedAtPosition = new WorldPosition(attachedCar.WorldPosition);
        }

        public override void PreviousCar()
        {
            browseBackwards = false;
            browseForwards = false;
            BrowseMode = false;
            var trainCars = GetCameraCars();
            var wasLastCar = attachedCar == trainCars.Last();
            base.PreviousCar();
            if (!wasLastCar)
            {
                HighWagonOffsetLimit = LowWagonOffsetLimit;
                LowWagonOffsetLimit -= attachedCar.CarLengthM;
                ZDistanceM = LowWagonOffsetLimit + attachedCar.CarLengthM * 0.5f;
            }
            //           LookedAtPosition = new WorldPosition(attachedCar.WorldPosition);
        }

        public override void FirstCar()
        {
            browseBackwards = false;
            browseForwards = false;
            BrowseMode = false;
            base.FirstCar();
            ZDistanceM = 0;
            HighWagonOffsetLimit = attachedCar.CarLengthM * 0.5f;
            LowWagonOffsetLimit = -attachedCar.CarLengthM * 0.5f;
            //            LookedAtPosition = new WorldPosition(attachedCar.WorldPosition);
        }

        public override void LastCar()
        {
            browseBackwards = false;
            browseForwards = false;
            BrowseMode = false;
            base.LastCar();
            var trainCars = GetCameraCars();
            ZDistanceM = -attachedCar.Train.Length + (trainCars.First().CarLengthM + trainCars.Last().CarLengthM) * 0.5f;
            LowWagonOffsetLimit = -attachedCar.Train.Length + trainCars.First().CarLengthM * 0.5f;
            HighWagonOffsetLimit = LowWagonOffsetLimit + attachedCar.CarLengthM;
            //            LookedAtPosition = new WorldPosition(attachedCar.WorldPosition);
        }

        public void ToggleBrowseBackwards()
        {
            browseBackwards = !browseBackwards;
            if (browseBackwards)
            {
                if (!BrowseMode)
                {
                    //                    LookedAtPosition = new WorldPosition(attachedCar.WorldPosition);
                    browsedTraveller = new Traveller(attachedCar.Train.FrontTDBTraveller);
                    browsedTraveller.Move(-attachedCar.CarLengthM * 0.5f + ZDistanceM);
                    BrowseDistance = attachedCar.CarLengthM * 0.5f;
                    BrowseMode = true;
                }
            }
            browseForwards = false;
        }

        public void ToggleBrowseForwards()
        {
            browseForwards = !browseForwards;
            if (browseForwards)
            {
                if (!BrowseMode)
                {
                    //                    LookedAtPosition = new WorldPosition(attachedCar.WorldPosition);
                    browsedTraveller = new Traveller(attachedCar.Train.RearTDBTraveller);
                    var trainCars = GetCameraCars();
                    browsedTraveller.Move((attachedCar.CarLengthM - trainCars.First().CarLengthM - trainCars.Last().CarLengthM) * 0.5f + attachedCar.Train.Length + ZDistanceM);
                    BrowseDistance = attachedCar.CarLengthM * 0.5f;
                    BrowseMode = true;
                }
            }
            browseBackwards = false;
        }
    }

    public abstract class NonTrackingCamera : AttachedCamera
    {
        protected NonTrackingCamera(Viewer viewer)
            : base(viewer)
        {
        }

        private protected override void RotateHorizontallyCommandStart()
        {
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void RotateHorizontallyCommandEnd()
        {
            _ = new CameraRotateLeftRightCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationYRadians);
        }

        private protected override void RotateHorizontally(int rotateSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer) * SpeedAdjustmentForRotation;
            RotateRight(rotateSign * speed);
        }

        private protected override void RotateVerticallyCommandStart()
        {
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void RotateVerticallyCommandEnd()
        {
            _ = new CameraRotateUpDownCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationXRadians);
        }

        private protected override void RotateVertically(int rotateSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer) * SpeedAdjustmentForRotation;
            RotateDown(-rotateSign * speed);
        }

        private protected override void PanHorizontallyCommandStart()
        {
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void PanHorizontallyCommandEnd()
        {
            _ = new CameraRotateLeftRightCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationYRadians);
        }

        private protected override void PanHorizontally(int panSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer) * SpeedAdjustmentForRotation;
            RotateRight(panSign * speed);
        }

        private protected override void PanVerticallyCommandStart()
        {
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void PanVerticallyCommandEnd()
        {
            _ = new CameraRotateUpDownCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationXRadians);
        }

        private protected override void PanVertically(int panSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer) * SpeedAdjustmentForRotation;
            RotateDown(-panSign * speed);
        }

        private protected override void CarFirst()
        {
            _ = new FirstCarCommand(Viewer.Log);
        }

        private protected override void CarLast()
        {
            _ = new LastCarCommand(Viewer.Log);
        }

        private protected override void CarPrevious()
        {
            _ = new PreviousCarCommand(Viewer.Log);
        }

        private protected override void CarNext()
        {
            _ = new NextCarCommand(Viewer.Log);
        }

        private protected override void ZoomByMouseCommmand(UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers)
        {
            ZoomByMouse(commandArgs, gameTime, modifiers);
        }

        private protected override void RotateByMouseCommmand(UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers)
        {
            RotateByMouse(commandArgs, gameTime, modifiers);
        }
    }

    public class BrakemanCamera : NonTrackingCamera
    {
        protected bool attachedToRear;

        public override float NearPlane { get { return 0.25f; } }
        public override string Name { get { return Viewer.Catalog.GetString("Brakeman"); } }

        public BrakemanCamera(Viewer viewer)
            : base(viewer)
        {
        }

        protected override List<TrainCar> GetCameraCars()
        {
            var cars = base.GetCameraCars();
            return new List<TrainCar>(new[] { cars.First(), cars.Last() });
        }

        protected override void SetCameraCar(TrainCar car)
        {
            base.SetCameraCar(car);
            attachedLocation = new Vector3(1.8f, 2.0f, attachedCar.CarLengthM / 2 - 0.3f);
            attachedToRear = car.Train.Cars[0] != car;
        }

        protected override bool IsCameraFlipped()
        {
            return attachedToRear ^ attachedCar.Flipped;
        }

        // attached car may be no more part of the list, therefore base methods would return errors
        public override void NextCar()
        {
            FirstCar();
        }

        public override void PreviousCar()
        {
            LastCar();
        }
        public override void LastCar()
        {
            base.LastCar();
            attachedToRear = true;
        }

    }

    public class InsideCamera3D : NonTrackingCamera
    {
        public override float NearPlane { get { return 0.1f; } }

        public InsideCamera3D(Viewer viewer)
            : base(viewer)
        {
        }

        protected Vector3 viewPointLocation;
        protected float viewPointRotationXRadians = 0;
        protected float viewPointRotationYRadians = 0;
        protected Vector3 StartViewPointLocation;
        protected float StartViewPointRotationXRadians = 0;
        protected float StartViewPointRotationYRadians = 0;
        protected string prevcar = "";
        protected int ActViewPoint = 0;
        protected int prevViewPoint = -1;
        protected bool PrevCabWasRear = false;
        private float x, y, z;

        /// <summary>
        /// A camera can use this method to handle any preparation when being activated.
        /// </summary>
        protected override void OnActivate(bool sameCamera)
        {
            var trainCars = GetCameraCars();
            if (trainCars.Count == 0) return;//may not have passenger or 3d cab viewpoints
            if (sameCamera)
            {
                if (!trainCars.Contains(attachedCar)) { attachedCar = trainCars.First(); }
                else if (trainCars.IndexOf(attachedCar) < trainCars.Count - 1)
                {
                    attachedCar = trainCars[trainCars.IndexOf(attachedCar) + 1];
                }
                else attachedCar = trainCars.First();
            }
            else
            {
                if (!trainCars.Contains(attachedCar)) attachedCar = trainCars.First();
            }
            SetCameraCar(attachedCar);
        }

        private protected override void Zoom(int zoomSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            // Move camera
            z = zoomSign * GetSpeed(gameTime, commandArgs, Viewer) * 5;
            MoveCameraXYZ(0, 0, z);
        }

        private protected override void RotateHorizontallyCommandStart()
        {
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void RotateHorizontallyCommandEnd()
        {
            _ = new CameraMoveXYZCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, x, 0, 0);
        }

        private protected override void RotateHorizontally(int rotateSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            x = rotateSign * GetSpeed(gameTime, commandArgs, Viewer) * SpeedAdjustmentForRotation * 2;
            MoveCameraXYZ(x, 0, 0);
        }

        private protected override void RotateVerticallyCommandStart()
        {
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void RotateVerticallyCommandEnd()
        {
            _ = new CameraMoveXYZCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, 0, y, 0);
        }

        private protected override void RotateVertically(int rotateSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            y = rotateSign * GetSpeed(gameTime, commandArgs, Viewer) * SpeedAdjustmentForRotation / 2;
            MoveCameraXYZ(0, y, 0);
        }

        private protected override void PanHorizontallyCommandStart()
        {
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void PanHorizontallyCommandEnd()
        {
            _ = new CameraRotateLeftRightCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationYRadians);
        }

        private protected override void PanHorizontally(int panSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer) * SpeedAdjustmentForRotation;
            RotateRight(panSign * speed);
        }

        private protected override void PanVerticallyCommandStart()
        {
            CommandStartTime = Viewer.Simulator.ClockTime;
        }

        private protected override void PanVerticallyCommandEnd()
        {
            _ = new CameraRotateUpDownCommand(Viewer.Log, CommandStartTime, Viewer.Simulator.ClockTime, RotationXRadians);
        }

        private protected override void PanVertically(int panSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer) * SpeedAdjustmentForRotation;
            RotateDown(-panSign * speed);
        }

        private protected override void CarFirst()
        {
            _ = new FirstCarCommand(Viewer.Log);
        }

        private protected override void CarLast()
        {
            _ = new LastCarCommand(Viewer.Log);
        }

        private protected override void CarPrevious()
        {
            _ = new PreviousCarCommand(Viewer.Log);
        }

        private protected override void CarNext()
        {
            _ = new NextCarCommand(Viewer.Log);
        }

        private protected override void ZoomByMouseCommmand(UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers)
        {
            ZoomByMouse(commandArgs, gameTime, modifiers, SpeedAdjustmentForRotation);
        }

        public void MoveCameraXYZ(float x, float y, float z)
        {
            if (PrevCabWasRear)
            {
                x = -x;
                z = -z;
            }
            attachedLocation.X += x;
            attachedLocation.Y += y;
            attachedLocation.Z += z;
            viewPointLocation.X = attachedLocation.X; viewPointLocation.Y = attachedLocation.Y; viewPointLocation.Z = attachedLocation.Z;
            if (attachedCar != null) UpdateLocation(attachedCar.WorldPosition);
        }

        /// <summary>
        /// Remembers angle of camera to apply when user returns to this type of car.
        /// </summary>
        /// <param name="speed"></param>
        protected override void RotateRight(float speed)
        {
            base.RotateRight(speed);
            viewPointRotationYRadians = RotationYRadians;
        }

        /// <summary>
        /// Remembers angle of camera to apply when user returns to this type of car.
        /// </summary>
        /// <param name="speed"></param>
        protected override void RotateDown(float speed)
        {
            base.RotateDown(speed);
            viewPointRotationXRadians = RotationXRadians;
        }

        /// <summary>
        /// Remembers angle of camera to apply when user returns to this type of car.
        /// </summary>
        private protected override void RotateByMouseCommmandEnd()
        {
            base.RotateByMouseCommmandEnd();
            viewPointRotationXRadians = RotationXRadians;
            viewPointRotationYRadians = RotationYRadians;
        }

        internal protected override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            outf.Write(ActViewPoint);
            outf.Write(prevViewPoint);
            outf.Write(prevcar);
            outf.Write(StartViewPointLocation.X);
            outf.Write(StartViewPointLocation.Y);
            outf.Write(StartViewPointLocation.Z);
            outf.Write(StartViewPointRotationXRadians);
            outf.Write(StartViewPointRotationYRadians);
        }

        internal protected override void Restore(BinaryReader inf)
        {
            base.Restore(inf);
            ActViewPoint = inf.ReadInt32();
            prevViewPoint = inf.ReadInt32();
            prevcar = inf.ReadString();
            StartViewPointLocation.X = inf.ReadSingle();
            StartViewPointLocation.Y = inf.ReadSingle();
            StartViewPointLocation.Z = inf.ReadSingle();
            StartViewPointRotationXRadians = inf.ReadSingle();
            StartViewPointRotationYRadians = inf.ReadSingle();
        }

        public override void Reset()
        {
            base.Reset();
            viewPointLocation = StartViewPointLocation;
            attachedLocation = StartViewPointLocation;
            viewPointRotationXRadians = StartViewPointRotationXRadians;
            viewPointRotationYRadians = StartViewPointRotationYRadians;
            RotationXRadians = StartViewPointRotationXRadians;
            RotationYRadians = StartViewPointRotationYRadians;
            XRadians = StartViewPointRotationXRadians;
            YRadians = StartViewPointRotationYRadians;
        }
    }

    public class PassengerCamera : InsideCamera3D
    {
        public override Styles Style { get { return Styles.Passenger; } }
        public override bool IsAvailable { get { return Viewer.SelectedTrain != null && Viewer.SelectedTrain.Cars.Any(c => c.PassengerViewpoints.Count > 0); } }
        public override string Name { get { return Viewer.Catalog.GetString("Passenger"); } }

        public PassengerCamera(Viewer viewer)
            : base(viewer)
        {
        }

        protected override List<TrainCar> GetCameraCars()
        {
            return base.GetCameraCars().Where(c => c.PassengerViewpoints.Count > 0).ToList();
        }

        protected override void SetCameraCar(TrainCar car)
        {
            base.SetCameraCar(car);
            // Settings are held so that when switching back from another camera, view is not reset.
            // View is only reset on move to a different car and/or viewpoint or "Ctl + 8".
            if (car.CarID != prevcar)
            {
                ActViewPoint = 0;
                ResetViewPoint(car);
            }
            else if (ActViewPoint != prevViewPoint)
            {
                ResetViewPoint(car);
            }
        }

        protected void ResetViewPoint(TrainCar car)
        {
            prevcar = car.CarID;
            prevViewPoint = ActViewPoint;
            viewPointLocation = attachedCar.PassengerViewpoints[ActViewPoint].Location;
            viewPointRotationXRadians = attachedCar.PassengerViewpoints[ActViewPoint].RotationXRadians;
            viewPointRotationYRadians = attachedCar.PassengerViewpoints[ActViewPoint].RotationYRadians;
            RotationXRadians = viewPointRotationXRadians;
            RotationYRadians = viewPointRotationYRadians;
            attachedLocation = viewPointLocation;
            StartViewPointLocation = viewPointLocation;
            StartViewPointRotationXRadians = viewPointRotationXRadians;
            StartViewPointRotationYRadians = viewPointRotationYRadians;
        }

        private protected override void ChangePassengerViewPoint()
        {
            _ = new CameraChangePassengerViewPointCommand(Viewer.Log);
        }

        public void SwitchSideCameraCar(TrainCar car)
        {
            attachedLocation.X = -attachedLocation.X;
            RotationYRadians = -RotationYRadians;
        }

        public void ChangePassengerViewPoint(TrainCar car)
        {
            ActViewPoint++;
            if (ActViewPoint >= car.PassengerViewpoints.Count) ActViewPoint = 0;
            SetCameraCar(car);
        }
    }

    public class CabCamera3D : InsideCamera3D
    {
        private CabViewDiscreteRenderer selectedControl;
        private CabViewDiscreteRenderer pointedControl;

        public override Styles Style { get { return Styles.ThreeDimCab; } }
        public bool Enabled { get; set; }
        public override bool IsAvailable => Viewer.SelectedTrain != null && Viewer.SelectedTrain.IsActualPlayerTrain &&
                    Viewer.PlayerLocomotive != null && Viewer.PlayerLocomotive.CabViewpoints != null &&
                    (Viewer.PlayerLocomotive.HasFront3DCab || Viewer.PlayerLocomotive.HasRear3DCab);
        public override string Name => Viewer.Catalog.GetString("3D Cab");

        public CabCamera3D(Viewer viewer)
            : base(viewer)
        {
        }

        protected override List<TrainCar> GetCameraCars()
        {
            if (Viewer.SelectedTrain != null && Viewer.SelectedTrain.IsActualPlayerTrain &&
            Viewer.PlayerLocomotive != null && Viewer.PlayerLocomotive.CabViewpoints != null)
            {
                List<TrainCar> l = new List<TrainCar>
                {
                    Viewer.PlayerLocomotive
                };
                return l;
            }
            else return base.GetCameraCars();
        }

        protected override void SetCameraCar(TrainCar car)
        {
            base.SetCameraCar(car);
            // Settings are held so that when switching back from another camera, view is not reset.
            // View is only reset on move to a different cab or "Ctl + 8".
            if (attachedCar.CabViewpoints != null)
            {
                if (ActViewPoint != prevViewPoint)
                {
                    prevViewPoint = ActViewPoint;
                    viewPointLocation = attachedCar.CabViewpoints[ActViewPoint].Location;
                    viewPointRotationXRadians = attachedCar.CabViewpoints[ActViewPoint].RotationXRadians;
                    viewPointRotationYRadians = attachedCar.CabViewpoints[ActViewPoint].RotationYRadians;
                    RotationXRadians = viewPointRotationXRadians;
                    RotationYRadians = viewPointRotationYRadians;
                    attachedLocation = viewPointLocation;
                    StartViewPointLocation = viewPointLocation;
                    StartViewPointRotationXRadians = viewPointRotationXRadians;
                    StartViewPointRotationYRadians = viewPointRotationYRadians;
                }
            }
        }

        public void ChangeCab(TrainCar newCar)
        {
            try
            {
                var mstsLocomotive = newCar as MSTSLocomotive;
                if (PrevCabWasRear != mstsLocomotive.UsingRearCab)
                    RotationYRadians += MathHelper.Pi;
                ActViewPoint = mstsLocomotive.UsingRearCab ? 1 : 0;
                PrevCabWasRear = mstsLocomotive.UsingRearCab;
                SetCameraCar(newCar);
            }
            catch
            {
                Trace.TraceInformation("Change Cab failed");
            }
        }

        internal protected override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            outf.Write(PrevCabWasRear);
        }

        internal protected override void Restore(BinaryReader inf)
        {
            base.Restore(inf);
            PrevCabWasRear = inf.ReadBoolean();
        }

        public override bool IsUnderground
        {
            get
            {
                // Camera is underground if target (base) is underground or
                // track location is underground. The latter means we switch
                // to cab view instead of putting the camera above the tunnel.
                if (base.IsUnderground)
                    return true;
                var elevationAtCameraTarget = Viewer.Tiles.GetElevation(attachedCar.WorldPosition.WorldLocation);
                return attachedCar.WorldPosition.Location.Y + TerrainAltitudeMargin < elevationAtCameraTarget || attachedCar.CarTunnelData.LengthMOfTunnelAheadFront > 0;
            }
        }

        private CabViewDiscreteRenderer FindNearestControl(Point position, MSTSLocomotiveViewer mstsLocomotiveViewer, float maxDelta)
        {
            CabViewDiscreteRenderer result = null;
            Vector3 nearsource = new Vector3(position.X, position.Y, 0f);
            Vector3 farsource = new Vector3(position.X, position.Y, 1f);
            Matrix world = Matrix.CreateTranslation(0, 0, 0);
            Vector3 nearPoint = Viewer.DefaultViewport.Unproject(nearsource, XnaProjection, XnaView, world);
            Vector3 farPoint = Viewer.DefaultViewport.Unproject(farsource, XnaProjection, XnaView, world);

            Shapes.PoseableShape trainCarShape = mstsLocomotiveViewer.CabViewer3D.TrainCarShape;
            Dictionary<int, AnimatedPartMultiState> animatedParts = mstsLocomotiveViewer.CabViewer3D.AnimateParts;
            Dictionary<int, CabViewControlRenderer> controlMap = mstsLocomotiveViewer.CabRenderer3D.ControlMap;
            float bestDistance = maxDelta;  // squared click range
            foreach (KeyValuePair<int, AnimatedPartMultiState> animatedPart in animatedParts)
            {
                if (controlMap.TryGetValue(animatedPart.Value.Key, out CabViewControlRenderer cabRenderer) && cabRenderer is CabViewDiscreteRenderer)
                {
                    foreach (int i in animatedPart.Value.MatrixIndexes)
                    {
                        Matrix startingPoint = Matrix.Identity;
                        int j = i;
                        while (j >= 0 && j < trainCarShape.Hierarchy.Length && trainCarShape.Hierarchy[j] != -1)
                        {
                            startingPoint = MatrixExtension.Multiply(in startingPoint, in trainCarShape.XNAMatrices[j]);
                            j = trainCarShape.Hierarchy[j];
                        }
                        MatrixExtension.Multiply(in startingPoint, in trainCarShape.WorldPosition.XNAMatrix, out Matrix matrix);
                        WorldLocation matrixWorldLocation = new WorldLocation(trainCarShape.WorldPosition.WorldLocation.TileX, trainCarShape.WorldPosition.WorldLocation.TileZ,
                            matrix.Translation.X, matrix.Translation.Y, -matrix.Translation.Z);
                        Vector3 xnaCenter = XnaLocation(matrixWorldLocation);
                        float distance = xnaCenter.LineSegmentDistanceSquare(nearPoint, farPoint);

                        if (bestDistance > distance)
                        {
                            result = cabRenderer as CabViewDiscreteRenderer;
                            bestDistance = distance;
                        }
                    }
                }
            }
            return result;
        }

        private protected override void CabControlClickCommand(UserCommandArgs commandArgs, KeyModifiers modifiers)
        {
            PointerCommandArgs pointerCommandArgs = commandArgs as PointerCommandArgs;

            if (Viewer.PlayerLocomotiveViewer is MSTSLocomotiveViewer mstsLocomotiveViewer && mstsLocomotiveViewer.Has3DCabRenderer)
            {
                selectedControl = pointedControl ?? FindNearestControl(pointerCommandArgs.Position, mstsLocomotiveViewer, 0.015f); ;
                selectedControl?.HandleUserInput(GenericButtonEventType.Pressed, pointerCommandArgs.Position, Vector2.Zero);
            }
        }

        private protected override void CabControlReleaseCommand(UserCommandArgs commandArgs, KeyModifiers modifiers)
        {
            selectedControl?.HandleUserInput(GenericButtonEventType.Released, (commandArgs as PointerCommandArgs).Position, Vector2.Zero);
            selectedControl = null;
        }

        private protected override void CabControlDraggedCommand(UserCommandArgs commandArgs, KeyModifiers modifiers)
        {
            selectedControl?.HandleUserInput(GenericButtonEventType.Down, (commandArgs as PointerCommandArgs).Position, (commandArgs as PointerMoveCommandArgs).Delta);
        }

        private protected override void CabControlPointerMovedCommand(UserCommandArgs commandArgs, KeyModifiers modifiers)
        {
            PointerCommandArgs pointerCommandArgs = commandArgs as PointerCommandArgs;

            if (Viewer.PlayerLocomotiveViewer is MSTSLocomotiveViewer mstsLocomotiveViewer && mstsLocomotiveViewer.Has3DCabRenderer)
            {
                CabViewDiscreteRenderer control = pointedControl;
                pointedControl = FindNearestControl(pointerCommandArgs.Position, mstsLocomotiveViewer, 0.01f);

                if (pointedControl != null)
                {
                    if (pointedControl != control)
                        // say what control you have here
                        Viewer.Simulator.Confirmer.Message(ConfirmLevel.None, pointedControl.GetControlName(pointerCommandArgs.Position));
                    Viewer.RenderProcess.ActualCursor = Cursors.Hand;
                }
                else
                {
                    Viewer.RenderProcess.ActualCursor = Cursors.Default;
                }
            }
        }
    }

    public class HeadOutCamera : NonTrackingCamera
    {
        protected readonly bool Forwards;
        public enum HeadDirection { Forward, Backward }
        protected int CurrentViewpointIndex;
        protected bool PrevCabWasRear;

        // Head-out camera is only possible on the player train.
        public override bool IsAvailable { get { return Viewer.PlayerTrain != null && Viewer.PlayerTrain.Cars.Any(c => c.HeadOutViewpoints.Count > 0); } }
        public override float NearPlane { get { return 0.25f; } }
        public override string Name { get { return Viewer.Catalog.GetString("Head out"); } }

        public HeadOutCamera(Viewer viewer, HeadDirection headDirection)
            : base(viewer)
        {
            Forwards = headDirection == HeadDirection.Forward;
            RotationYRadians = Forwards ? 0 : -MathHelper.Pi;
        }

        protected override List<TrainCar> GetCameraCars()
        {
            // Head-out camera is only possible on the player train.
            return Viewer.PlayerTrain.Cars.Where(c => c.HeadOutViewpoints.Count > 0).ToList();
        }

        protected override void SetCameraCar(TrainCar car)
        {
            base.SetCameraCar(car);
            if (attachedCar.HeadOutViewpoints.Count > 0)
                attachedLocation = attachedCar.HeadOutViewpoints[CurrentViewpointIndex].Location;

            if (!Forwards)
                attachedLocation.X *= -1;
        }

        public void ChangeCab(TrainCar newCar)
        {
            var mstsLocomotive = newCar as MSTSLocomotive;
            if (PrevCabWasRear != mstsLocomotive.UsingRearCab)
                RotationYRadians += MathHelper.Pi;
            CurrentViewpointIndex = mstsLocomotive.UsingRearCab ? 1 : 0;
            PrevCabWasRear = mstsLocomotive.UsingRearCab;
            SetCameraCar(newCar);
        }
    }

    public class CabCamera : NonTrackingCamera
    {
        private CabViewDiscreteRenderer selectedControl;
        private CabViewDiscreteRenderer pointedControl;

        public int SideLocation { get; protected set; }

        public override Styles Style { get { return Styles.Cab; } }
        // Cab camera is only possible on the player train.
        public override bool IsAvailable { get { return Viewer.PlayerLocomotive != null && (Viewer.PlayerLocomotive.HasFrontCab || Viewer.PlayerLocomotive.HasRearCab); } }
        public override string Name { get { return Viewer.Catalog.GetString("Cab"); } }

        public override bool IsUnderground
        {
            get
            {
                // Camera is underground if target (base) is underground or
                // track location is underground. The latter means we switch
                // to cab view instead of putting the camera above the tunnel.
                if (base.IsUnderground)
                    return true;
                var elevationAtCameraTarget = Viewer.Tiles.GetElevation(attachedCar.WorldPosition.WorldLocation);
                return attachedCar.WorldPosition.Location.Y + TerrainAltitudeMargin < elevationAtCameraTarget || attachedCar.CarTunnelData.LengthMOfTunnelAheadFront > 0;
            }
        }

        public float RotationRatio = 0.00081f;
        public float RotationRatioHorizontal = 0.00081f;

        public CabCamera(Viewer viewer)
            : base(viewer)
        {
        }

        internal protected override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            outf.Write(SideLocation);
        }

        internal protected override void Restore(BinaryReader inf)
        {
            base.Restore(inf);
            SideLocation = inf.ReadInt32();
        }

        public override void Reset()
        {
            FieldOfView = Viewer.Settings.ViewingFOV;
            RotationXRadians = RotationYRadians = XRadians = YRadians = ZRadians = 0;
            Viewer.CabYOffsetPixels = (Viewer.DisplaySize.Y - Viewer.CabHeightPixels) / 2;
            Viewer.CabXOffsetPixels = (Viewer.CabWidthPixels - Viewer.DisplaySize.X) / 2;
            if (attachedCar != null)
            {
                Initialize();
            }
            ScreenChanged();
            OnActivate(true);
        }

        public void Initialize()
        {
            if (Viewer.Settings.Letterbox2DCab)
            {
                float fovFactor = 1f - Math.Max((float)Viewer.CabXLetterboxPixels / Viewer.DisplaySize.X, (float)Viewer.CabYLetterboxPixels / Viewer.DisplaySize.Y);
                FieldOfView = MathHelper.ToDegrees((float)(2 * Math.Atan(fovFactor * Math.Tan(MathHelper.ToRadians(Viewer.Settings.ViewingFOV / 2)))));
            }
            else if (Viewer.Settings.Cab2DStretch == 0 && Viewer.CabExceedsDisplayHorizontally <= 0)
            {
                // We must modify FOV to get correct lookout
                FieldOfView = MathHelper.ToDegrees((float)(2 * Math.Atan((float)Viewer.DisplaySize.Y / Viewer.DisplaySize.X / Viewer.CabTextureInverseRatio * Math.Tan(MathHelper.ToRadians(Viewer.Settings.ViewingFOV / 2)))));
                RotationRatio = (float)(0.962314f * 2 * Math.Tan(MathHelper.ToRadians(FieldOfView / 2)) / Viewer.DisplaySize.Y);
            }
            else if (Viewer.CabExceedsDisplayHorizontally > 0)
            {
                var halfFOVHorizontalRadians = (float)(Math.Atan((float)Viewer.DisplaySize.X / Viewer.DisplaySize.Y * Math.Tan(MathHelper.ToRadians(Viewer.Settings.ViewingFOV / 2))));
                RotationRatioHorizontal = (float)(0.962314f * 2 * Viewer.DisplaySize.X / Viewer.DisplaySize.Y * Math.Tan(MathHelper.ToRadians(Viewer.Settings.ViewingFOV / 2)) / Viewer.DisplaySize.X);
            }
            InitialiseRotation(attachedCar);
        }

        protected override void OnActivate(bool sameCamera)
        {
            // Cab camera is only possible on the player locomotive.
            SetCameraCar(GetCameraCars().First());
            tiltingLand = false;
            if (Viewer.Simulator.UseSuperElevation > 0 || Viewer.Simulator.CarVibrating > 0) tiltingLand = true;
            var car = attachedCar;
            if (car != null && car.Train != null && car.Train.IsTilting == true) tiltingLand = true;
            base.OnActivate(sameCamera);
        }

        protected override List<TrainCar> GetCameraCars()
        {
            // Cab camera is only possible on the player locomotive.
            return new List<TrainCar>(new[] { Viewer.PlayerLocomotive });
        }

        protected override void SetCameraCar(TrainCar car)
        {
            base.SetCameraCar(car);
            if (car != null)
            {
                var loco = car as MSTSLocomotive;
                var viewpoints = (loco.UsingRearCab)
                ? loco.CabViewList[(int)CabViewType.Rear].ViewPointList
                : loco.CabViewList[(int)CabViewType.Front].ViewPointList;
                attachedLocation = viewpoints[SideLocation].Location;
            }
            InitialiseRotation(attachedCar);
        }

        /// <summary>
        /// Switches to another cab view (e.g. side view).
        /// Applies the inclination of the previous external view due to PanUp() to the new external view. 
        /// </summary>
        private void ShiftView(int index)
        {
            var loco = attachedCar as MSTSLocomotive;

            var viewpointList = (loco.UsingRearCab)
            ? loco.CabViewList[(int)CabViewType.Rear].ViewPointList
            : loco.CabViewList[(int)CabViewType.Front].ViewPointList;

            SideLocation += index;

            var count = (loco.UsingRearCab)
                ? loco.CabViewList[(int)CabViewType.Rear].ViewPointList.Count
                : loco.CabViewList[(int)CabViewType.Front].ViewPointList.Count;
            // Wrap around
            if (SideLocation < 0)
                SideLocation = count - 1;
            else if (SideLocation >= count)
                SideLocation = 0;

            SetCameraCar(attachedCar);
        }

        /// <summary>
        /// Where cabview image doesn't fit the display exactly, this method mimics the player looking up
        /// and pans the image down to reveal details at the top of the cab.
        /// The external view also moves down by a similar amount.
        /// </summary>
        private void PanUp(bool up, float speed)
        {
            int max = 0;
            int min = Viewer.DisplaySize.Y - Viewer.CabHeightPixels - 2 * Viewer.CabYLetterboxPixels; // -ve value
            int cushionPixels = 40;
            int slowFactor = 4;

            // Cushioned approach to limits of travel. Within 40 pixels, travel at 1/4 speed
            if (up && Math.Abs(Viewer.CabYOffsetPixels - max) < cushionPixels)
                speed /= slowFactor;
            if (!up && Math.Abs(Viewer.CabYOffsetPixels - min) < cushionPixels)
                speed /= slowFactor;
            Viewer.CabYOffsetPixels += (up) ? (int)speed : -(int)speed;
            // Enforce limits to travel
            if (Viewer.CabYOffsetPixels >= max)
            {
                Viewer.CabYOffsetPixels = max;
                return;
            }
            if (Viewer.CabYOffsetPixels <= min)
            {
                Viewer.CabYOffsetPixels = min;
                return;
            }
            // Adjust inclination (up/down angle) of external view to match.
            var viewSpeed = (int)speed * RotationRatio; // factor found by trial and error.
            RotationXRadians -= (up) ? viewSpeed : -viewSpeed;
        }

        /// <summary>
        /// Where cabview image doesn't fit the display exactly (cabview image "larger" than display, this method mimics the player looking left and right
        /// and pans the image left/right to reveal details at the sides of the cab.
        /// The external view also moves sidewards by a similar amount.
        /// </summary>
        private void ScrollRight(bool right, float speed)
        {
            int min = 0;
            int max = Viewer.CabWidthPixels - Viewer.DisplaySize.X - 2 * Viewer.CabXLetterboxPixels; // -ve value
            int cushionPixels = 40;
            int slowFactor = 4;

            // Cushioned approach to limits of travel. Within 40 pixels, travel at 1/4 speed
            if (right && Math.Abs(Viewer.CabXOffsetPixels - max) < cushionPixels)
                speed /= slowFactor;
            if (!right && Math.Abs(Viewer.CabXOffsetPixels - min) < cushionPixels)
                speed /= slowFactor;
            Viewer.CabXOffsetPixels += (right) ? (int)speed : -(int)speed;
            // Enforce limits to travel
            if (Viewer.CabXOffsetPixels >= max)
            {
                Viewer.CabXOffsetPixels = max;
                return;
            }
            if (Viewer.CabXOffsetPixels <= min)
            {
                Viewer.CabXOffsetPixels = min;
                return;
            }
            // Adjust direction (right/left angle) of external view to match.
            var viewSpeed = (int)speed * RotationRatioHorizontal; // factor found by trial and error.
            RotationYRadians += (right) ? viewSpeed : -viewSpeed;
        }

        /// <summary>
        /// Sets direction for view out of cab front window. Also called when toggling between full screen and windowed.
        /// </summary>
        /// <param name="attachedCar"></param>
        public void InitialiseRotation(TrainCar attachedCar)
        {
            if (attachedCar == null) return;

            var loco = attachedCar as MSTSLocomotive;
            var viewpoints = (loco.UsingRearCab)
            ? loco.CabViewList[(int)CabViewType.Rear].ViewPointList
            : loco.CabViewList[(int)CabViewType.Front].ViewPointList;

            RotationXRadians = MathHelper.ToRadians(viewpoints[SideLocation].StartDirection.X) - RotationRatio * (Viewer.CabYOffsetPixels + Viewer.CabExceedsDisplay / 2);
            RotationYRadians = MathHelper.ToRadians(viewpoints[SideLocation].StartDirection.Y) - RotationRatioHorizontal * (-Viewer.CabXOffsetPixels + Viewer.CabExceedsDisplayHorizontally / 2); ;
        }

        private protected override void ToggleLetterboxCab()
        {
            Viewer.Settings.Letterbox2DCab = !Viewer.Settings.Letterbox2DCab;
            Viewer.AdjustCabHeight(Viewer.DisplaySize.X, Viewer.DisplaySize.Y);
            if (attachedCar != null)
                Initialize();
        }

        private protected override void Scroll(bool right, GameTime gameTime)
        {
            ScrollRight(right, (float)(500 * gameTime.ElapsedGameTime.TotalSeconds));
        }

        private protected override void SwitchCabView(int direction)
        {
            ShiftView(-direction);
        }

        private protected override void PanVertically(int panSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = (float)(500 * gameTime.ElapsedGameTime.TotalSeconds); // Independent of framerate
            PanUp(panSign > 0, speed);
        }

        private protected override void RotateHorizontally(int rotateSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
        }

        private protected override void RotateVertically(int rotateSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
        }

        private protected override void PanHorizontally(int panSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
        }

        private protected override void RotateByMouseCommmand(UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers)
        {
        }

        private protected override void CabControlClickCommand(UserCommandArgs commandArgs, KeyModifiers modifiers)
        {
            PointerCommandArgs pointerCommandArgs = commandArgs as PointerCommandArgs;

            if (Viewer.PlayerLocomotiveViewer is MSTSLocomotiveViewer mstsLocomotiveViewer && mstsLocomotiveViewer.HasCabRenderer)
            {
                selectedControl = pointedControl ?? mstsLocomotiveViewer.CabRenderer.ControlMap.Values.OfType<CabViewDiscreteRenderer>().Where(c => c.IsMouseWithin(pointerCommandArgs.Position)).FirstOrDefault();
                selectedControl?.HandleUserInput(GenericButtonEventType.Pressed, pointerCommandArgs.Position, Vector2.Zero);
            }
        }

        private protected override void CabControlReleaseCommand(UserCommandArgs commandArgs, KeyModifiers modifiers)
        {
            selectedControl?.HandleUserInput(GenericButtonEventType.Released, (commandArgs as PointerCommandArgs).Position, Vector2.Zero);
            selectedControl = null;
        }

        private protected override void CabControlDraggedCommand(UserCommandArgs commandArgs, KeyModifiers modifiers)
        {
            selectedControl?.HandleUserInput(GenericButtonEventType.Down, (commandArgs as PointerCommandArgs).Position, (commandArgs as PointerMoveCommandArgs).Delta);
        }

        private protected override void CabControlPointerMovedCommand(UserCommandArgs commandArgs, KeyModifiers modifiers)
        {
            PointerCommandArgs pointerCommandArgs = commandArgs as PointerCommandArgs;

            if (Viewer.PlayerLocomotiveViewer is MSTSLocomotiveViewer mstsLocomotiveViewer && mstsLocomotiveViewer.HasCabRenderer)
            {
                CabViewDiscreteRenderer control = pointedControl;
                pointedControl = mstsLocomotiveViewer.CabRenderer.ControlMap.Values.OfType<CabViewDiscreteRenderer>().Where(c => c.IsMouseWithin(pointerCommandArgs.Position)).FirstOrDefault();
                if (pointedControl != null)
                {
                    if (pointedControl != control)
                        // say what control you have here
                        Viewer.Simulator.Confirmer.Message(ConfirmLevel.None, pointedControl.GetControlName(pointerCommandArgs.Position));
                    Viewer.RenderProcess.ActualCursor = Cursors.Hand;
                }
                else
                {
                    Viewer.RenderProcess.ActualCursor = Cursors.Default;
                }
            }
        }
    }

    public class TracksideCamera : LookAtCamera
    {
        protected const int MaximumDistance = 100;
        protected const float SidewaysScale = MaximumDistance / 10;
        // Heights above the terrain for the camera.
        protected const float CameraAltitude = 2;
        // Height above the coordinate center of target.
        protected const float TargetAltitude = TerrainAltitudeMargin;

        protected TrainCar attachedCar;
        public override TrainCar AttachedCar { get { return attachedCar; } }
        public override string Name { get { return Viewer.Catalog.GetString("Trackside"); } }

        protected TrainCar LastCheckCar;
        protected WorldLocation TrackCameraLocation;
        protected float CameraAltitudeOffset;

        public override bool IsUnderground
        {
            get
            {
                // Camera is underground if target (base) is underground or
                // track location is underground. The latter means we switch
                // to cab view instead of putting the camera above the tunnel.
                if (base.IsUnderground)
                    return true;
                if (TrackCameraLocation == WorldLocation.None) return false;
                var elevationAtCameraTarget = Viewer.Tiles.GetElevation(TrackCameraLocation);
                return TrackCameraLocation.Location.Y + TerrainAltitudeMargin < elevationAtCameraTarget;
            }
        }

        public TracksideCamera(Viewer viewer)
            : base(viewer)
        {
        }

        public override void Reset()
        {
            base.Reset();
            cameraLocation = cameraLocation.ChangeElevation(-CameraAltitudeOffset);
            CameraAltitudeOffset = 0;
        }

        protected override void OnActivate(bool sameCamera)
        {
            if (sameCamera)
            {
                cameraLocation = new WorldLocation(0, 0, cameraLocation.Location);

            }
            if (attachedCar == null || attachedCar.Train != Viewer.SelectedTrain)
            {
                if (Viewer.SelectedTrain.MUDirection != MidpointDirection.Reverse)
                    attachedCar = Viewer.SelectedTrain.Cars.First();
                else
                    attachedCar = Viewer.SelectedTrain.Cars.Last();
            }
            base.OnActivate(sameCamera);
        }

        private protected override void Zoom(int zoomSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            ZoomIn(zoomSign * GetSpeed(gameTime, commandArgs, Viewer) * 2);
        }

        private protected override void PanHorizontally(int panSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = GetSpeed(gameTime, commandArgs, Viewer);
            PanRight(panSign * speed);
        }

        private protected override void PanVertically(int panSign, UserCommandArgs commandArgs, GameTime gameTime)
        {
            float speed = panSign * GetSpeed(gameTime, commandArgs, Viewer);
            RotationYRadians = -XnaView.MatrixToYAngle();

            CameraAltitudeOffset += speed;
            cameraLocation = cameraLocation.ChangeElevation(speed);

            if (panSign < 0 && CameraAltitudeOffset < 0)
            {
                cameraLocation = cameraLocation.ChangeElevation(-CameraAltitudeOffset);
                CameraAltitudeOffset = 0;
            }
        }

        private protected override void CarFirst()
        {
            attachedCar = Viewer.SelectedTrain.Cars.First();
        }

        private protected override void CarLast()
        {
            attachedCar = Viewer.SelectedTrain.Cars.Last();
        }

        private protected override void CarPrevious()
        {
            List<TrainCar> trainCars = Viewer.SelectedTrain.Cars;
            attachedCar = attachedCar == trainCars.Last() ? attachedCar : trainCars[trainCars.IndexOf(attachedCar) + 1];
        }

        private protected override void CarNext()
        {
            List<TrainCar> trainCars = Viewer.SelectedTrain.Cars;
            attachedCar = attachedCar == trainCars.First() ? attachedCar : trainCars[trainCars.IndexOf(attachedCar) - 1];
        }

        private protected override void ZoomByMouseCommmand(UserCommandArgs commandArgs, GameTime gameTime, KeyModifiers modifiers)
        {
            ZoomByMouse(commandArgs, gameTime, modifiers);
        }

        public override void Update(in ElapsedTime elapsedTime)
        {
            var train = PrepUpdate(out bool trainForwards);

            // Train is close enough if the last car we used is part of the same train and still close enough.
            var trainClose = (LastCheckCar?.Train == train) && (WorldLocation.GetDistance2D(LastCheckCar.WorldPosition.WorldLocation, cameraLocation).Length() < MaximumDistance);

            // Otherwise, let's check out every car and remember which is the first one close enough for next time.
            if (!trainClose)
            {
                foreach (var car in train.Cars)
                {
                    if (WorldLocation.GetDistance2D(car.WorldPosition.WorldLocation, cameraLocation).Length() < MaximumDistance)
                    {
                        LastCheckCar = car;
                        trainClose = true;
                        break;
                    }
                }
            }

            // Switch to new position.
            if (!trainClose || (TrackCameraLocation == WorldLocation.None))
            {
                var tdb = trainForwards ? new Traveller(train.FrontTDBTraveller) : new Traveller(train.RearTDBTraveller, Traveller.TravellerDirection.Backward);
                var newLocation = GoToNewLocation(ref tdb, train, trainForwards).Normalize();

                var newLocationElevation = Viewer.Tiles.GetElevation(newLocation);

                cameraLocation = newLocation.SetElevation(Math.Max(tdb.Y, newLocationElevation) + CameraAltitude + CameraAltitudeOffset);
            }

            targetLocation = targetLocation.ChangeElevation(TargetAltitude);

            UpdateListener();

        }

        protected Train PrepUpdate(out bool trainForwards)
        {
            var train = attachedCar.Train;

            // TODO: What is this code trying to do?
            //if (train != Viewer.PlayerTrain && train.LeadLocomotive == null) train.ChangeToNextCab();
            trainForwards = true;
            if (train.LeadLocomotive != null)
                //TODO: next code line has been modified to flip trainset physics in order to get viewing direction coincident with loco direction when using rear cab.
                // To achieve the same result with other means, without flipping trainset physics, maybe the line should be changed
                trainForwards = (train.LeadLocomotive.SpeedMpS >= 0) ^ train.LeadLocomotive.Flipped ^ ((MSTSLocomotive)train.LeadLocomotive).UsingRearCab;
            else if (Viewer.PlayerLocomotive != null && train.IsActualPlayerTrain)
                trainForwards = (Viewer.PlayerLocomotive.SpeedMpS >= 0) ^ Viewer.PlayerLocomotive.Flipped ^ ((MSTSLocomotive)Viewer.PlayerLocomotive).UsingRearCab;

            targetLocation = attachedCar.WorldPosition.WorldLocation;

            return train;
        }

        protected WorldLocation GoToNewLocation(ref Traveller tdb, Train train, bool trainForwards)
        {
            tdb.Move(MaximumDistance * 0.75f);
            TrackCameraLocation = tdb.WorldLocation;
            var directionForward = WorldLocation.GetDistance((trainForwards ? train.FirstCar : train.LastCar).WorldPosition.WorldLocation, TrackCameraLocation);
            if (Viewer.Random.Next(2) == 0)
            {
                // Use swapped -X and Z to move to the left of the track.
                return new WorldLocation(TrackCameraLocation.TileX, TrackCameraLocation.TileZ,
                    TrackCameraLocation.Location.X - (directionForward.Z / SidewaysScale), TrackCameraLocation.Location.Y, TrackCameraLocation.Location.Z + (directionForward.X / SidewaysScale));
            }
            else
            {
                // Use swapped X and -Z to move to the right of the track.
                return new WorldLocation(TrackCameraLocation.TileX, TrackCameraLocation.TileZ,
                    TrackCameraLocation.Location.X + (directionForward.Z / SidewaysScale), TrackCameraLocation.Location.Y, TrackCameraLocation.Location.Z - (directionForward.X / SidewaysScale));
            }
        }

        protected virtual void PanRight(float speed)
        {
            Vector3 movement = new Vector3(speed, 0, 0);
            XRadians += movement.X;
            MoveCamera(movement);
        }

        protected override void ZoomIn(float speed)
        {
            Vector3 movement = new Vector3(0, 0, speed);
            ZRadians += movement.Z;
            MoveCamera(movement);
        }

    }

    public class SpecialTracksideCamera : TracksideCamera
    {
        private const int MaximumSpecialPointDistance = 300;
        private const float PlatformOffsetM = 3.3f;
        protected bool SpecialPointFound = false;
        private const float CheckIntervalM = 50f; // every 50 meters it is checked wheter there is a near special point
        protected float DistanceRunM = 0f; // distance run since last check interval
        protected bool FirstUpdateLoop = true; // first update loop

        private const float MaxDistFromRoadCarM = 200.0f; // maximum distance of train traveller to spawned roadcar
        protected RoadCar NearRoadCar;
        protected bool RoadCarFound;

        public SpecialTracksideCamera(Viewer viewer)
            : base(viewer)
        {
        }

        protected override void OnActivate(bool sameCamera)
        {
            DistanceRunM = 0;
            base.OnActivate(sameCamera);
            FirstUpdateLoop = Math.Abs(AttachedCar.Train.SpeedMpS) <= 0.2f || sameCamera;
            if (sameCamera)
            {
                SpecialPointFound = false;
                TrackCameraLocation = WorldLocation.None;
                RoadCarFound = false;
                NearRoadCar = null;
            }
        }

        public override void Update(in ElapsedTime elapsedTime)
        {
            var train = PrepUpdate(out bool trainForwards);

            if (RoadCarFound)
            {
                // camera location is always behind the near road car, at a distance which increases at increased speed
                if (NearRoadCar != null && NearRoadCar.Travelled < NearRoadCar.Spawner.Length - 10f)
                {
                    var traveller = new Traveller(NearRoadCar.FrontTraveller);
                    traveller.Move(-2.5f - 0.15f * NearRoadCar.Length - NearRoadCar.Speed * 0.5f);
                    TrackCameraLocation = traveller.WorldLocation;
                    cameraLocation = traveller.WorldLocation.ChangeElevation(+1.8f);
                }
                else NearRoadCar = null;
            }

            bool trainClose = false;
            // Train is close enough if the last car we used is part of the same train and still close enough.
            if ((LastCheckCar != null) && (LastCheckCar.Train == train))
            {
                float distance = WorldLocation.GetDistance2D(LastCheckCar.WorldPosition.WorldLocation, cameraLocation).Length();
                trainClose = distance < (SpecialPointFound ? MaximumSpecialPointDistance * 0.8f : MaximumDistance);
                if (!trainClose && SpecialPointFound && NearRoadCar != null) trainClose = distance < MaximumSpecialPointDistance * 1.5f;
            }

            // Otherwise, let's check out every car and remember which is the first one close enough for next time.
            if (!trainClose)
            {
                // if camera is not close to LastCheckCar, verify if it is still close to another car of the train
                foreach (var car in train.Cars)
                {
                    if (LastCheckCar != null && car == LastCheckCar &&
                        WorldLocation.GetDistance2D(car.WorldPosition.WorldLocation, cameraLocation).Length() < (SpecialPointFound ? MaximumSpecialPointDistance * 0.8f : MaximumDistance))
                    {
                        trainClose = true;
                        break;
                    }
                    else if (WorldLocation.GetDistance2D(car.WorldPosition.WorldLocation, cameraLocation).Length() <
                        (SpecialPointFound && NearRoadCar != null && train.SpeedMpS > NearRoadCar.Speed + 10 ? MaximumSpecialPointDistance * 1.5f : MaximumDistance))
                    {
                        LastCheckCar = car;
                        trainClose = true;
                        break;
                    }
                }
                if (!trainClose)
                    LastCheckCar = null;
            }
            if (RoadCarFound && NearRoadCar == null)
            {
                RoadCarFound = false;
                SpecialPointFound = false;
                trainClose = false;
            }
            var trySpecial = false;
            DistanceRunM += (float)elapsedTime.ClockSeconds * train.SpeedMpS;
            // when camera not at a special point, try every CheckIntervalM meters if there is a new special point nearby
            if (Math.Abs(DistanceRunM) >= CheckIntervalM)
            {
                DistanceRunM = 0;
                if (!SpecialPointFound && trainClose) trySpecial = true;
            }
            // Switch to new position.
            if (!trainClose || (TrackCameraLocation == WorldLocation.None) || trySpecial)
            {
                SpecialPointFound = false;
                bool platformFound = false;
                NearRoadCar = null;
                RoadCarFound = false;
                Traveller tdb;
                // At first update loop camera location may be also behind train front (e.g. platform at start of activity)
                if (FirstUpdateLoop)
                    tdb = trainForwards ? new Traveller(train.RearTDBTraveller) : new Traveller(train.FrontTDBTraveller, Traveller.TravellerDirection.Backward);
                else
                    tdb = trainForwards ? new Traveller(train.FrontTDBTraveller) : new Traveller(train.RearTDBTraveller, Traveller.TravellerDirection.Backward);

                int tcSectionIndex;
                int routeIndex;
                TrackCircuitPartialPathRoute thisRoute = null;
                // search for near platform in fast way, using TCSection data
                if (trainForwards && train.ValidRoute[0] != null)
                {
                    thisRoute = train.ValidRoute[0];
                }
                else if (!trainForwards && train.ValidRoute[1] != null)
                {
                    thisRoute = train.ValidRoute[1];
                }

                // Search for platform
                if (thisRoute != null)
                {
                    if (FirstUpdateLoop)
                    {
                        tcSectionIndex = trainForwards ? train.PresentPosition[Direction.Backward].TrackCircuitSectionIndex : train.PresentPosition[Direction.Forward].TrackCircuitSectionIndex;
                        routeIndex = trainForwards ? train.PresentPosition[Direction.Backward].RouteListIndex : train.PresentPosition[Direction.Forward].RouteListIndex;
                    }
                    else
                    {
                        tcSectionIndex = trainForwards ? train.PresentPosition[Direction.Forward].TrackCircuitSectionIndex : train.PresentPosition[Direction.Backward].TrackCircuitSectionIndex;
                        routeIndex = trainForwards ? train.PresentPosition[Direction.Forward].RouteListIndex : train.PresentPosition[Direction.Backward].RouteListIndex;
                    }
                    if (routeIndex != -1)
                    {
                        float distanceToViewingPoint = 0;
                        TrackCircuitSection TCSection = TrackCircuitSection.TrackCircuitList[tcSectionIndex];
                        float distanceToAdd = TCSection.Length;
                        float incrDistance;
                        if (FirstUpdateLoop)
                            incrDistance = trainForwards ? -train.PresentPosition[Direction.Backward].Offset : -TCSection.Length + train.PresentPosition[Direction.Forward].Offset;
                        else
                            incrDistance = trainForwards ? -train.PresentPosition[Direction.Forward].Offset : -TCSection.Length + train.PresentPosition[Direction.Backward].Offset;
                        // scanning route in direction of train, searching for a platform
                        while (incrDistance < MaximumSpecialPointDistance * 0.7f)
                        {
                            foreach (int platformIndex in TCSection.PlatformIndices)
                            {
                                PlatformDetails thisPlatform = Simulator.Instance.SignalEnvironment.PlatformDetailsList[platformIndex];
                                if (thisPlatform.TrackCircuitOffset[Simulation.Signalling.Location.NearEnd, thisRoute[routeIndex].Direction] + incrDistance < MaximumSpecialPointDistance * 0.7f
                                    && (thisPlatform.TrackCircuitOffset[Simulation.Signalling.Location.NearEnd, thisRoute[routeIndex].Direction] + incrDistance > 0 || FirstUpdateLoop))
                                {
                                    // platform found, compute distance to viewing point
                                    distanceToViewingPoint = Math.Min(MaximumSpecialPointDistance * 0.7f,
                                        incrDistance + thisPlatform.TrackCircuitOffset[Simulation.Signalling.Location.NearEnd, thisRoute[routeIndex].Direction] + thisPlatform.Length * 0.7f);
                                    if (FirstUpdateLoop && Math.Abs(train.SpeedMpS) <= 0.2f) distanceToViewingPoint =
                                            Math.Min(distanceToViewingPoint, train.Length * 0.95f);
                                    tdb.Move(distanceToViewingPoint);
                                    // shortTrav is used to state directions, to correctly identify in which direction (left or right) to move
                                    //the camera from center of track to the platform at its side
                                    Traveller shortTrav;
                                    if (!(Viewer.Simulator.TDB.TrackDB.TrackItems[thisPlatform.PlatformFrontUiD] is PlatformItem platformItem))
                                        continue;
                                    shortTrav = new Traveller(Viewer.Simulator.TSectionDat, Viewer.Simulator.TDB.TrackDB.TrackNodes, platformItem.Location, Traveller.TravellerDirection.Forward);
                                    var distanceToViewingPoint1 = shortTrav.DistanceTo(tdb.WorldLocation, thisPlatform.Length);
                                    if (distanceToViewingPoint1 == -1) //try other direction
                                    {
                                        shortTrav.ReverseDirection();
                                        distanceToViewingPoint1 = shortTrav.DistanceTo(tdb.WorldLocation, thisPlatform.Length);
                                        if (distanceToViewingPoint1 == -1) continue;
                                    }
                                    platformFound = true;
                                    SpecialPointFound = true;
                                    trainClose = false;
                                    LastCheckCar = FirstUpdateLoop ^ trainForwards ? train.Cars.First() : train.Cars.Last();
                                    shortTrav.Move(distanceToViewingPoint1);
                                    // moving location to platform at side of track
                                    float deltaX = (PlatformOffsetM + Viewer.Simulator.SuperElevationGauge / 2) * (float)Math.Cos(shortTrav.RotY) *
                                        (((thisPlatform.PlatformSide & PlatformDetails.PlatformSides.Right) == PlatformDetails.PlatformSides.Right) ? 1 : -1);
                                    float deltaZ = -(PlatformOffsetM + Viewer.Simulator.SuperElevationGauge / 2) * (float)Math.Sin(shortTrav.RotY) *
                                        (((thisPlatform.PlatformSide & PlatformDetails.PlatformSides.Right) == PlatformDetails.PlatformSides.Right) ? 1 : -1);
                                    TrackCameraLocation = new WorldLocation(tdb.WorldLocation.TileX, tdb.WorldLocation.TileZ,
                                        tdb.WorldLocation.Location.X + deltaX, tdb.WorldLocation.Location.Y, tdb.WorldLocation.Location.Z + deltaZ);
                                    break;
                                }
                            }
                            if (platformFound) break;
                            if (routeIndex < thisRoute.Count - 1)
                            {
                                incrDistance += distanceToAdd;
                                routeIndex++;
                                TCSection = thisRoute[routeIndex].TrackCircuitSection;
                                distanceToAdd = TCSection.Length;
                            }
                            else break;
                        }
                    }
                }

                if (!SpecialPointFound)
                {

                    // Search for near visible spawned car
                    var minDistanceM = 10000.0f;
                    NearRoadCar = null;
                    foreach (RoadCar visibleCar in Viewer.World.RoadCars.VisibleCars)
                    {
                        // check for direction
                        if (Math.Abs(visibleCar.FrontTraveller.RotY - train.FrontTDBTraveller.RotY) < 0.5f && visibleCar.Travelled < visibleCar.Spawner.Length - 30)
                        {
                            TrainCar testTrainCar = null;
                            if (visibleCar.Speed < Math.Abs(train.SpeedMpS) ^ trainForwards)
                            {
                                // we want to select an intermediate car so that the car will have the time to reach the head of the train
                                // before the end of its car spawner
                                float maxTrainLengthRecoveredM = (visibleCar.Spawner.Length - 10 - visibleCar.Travelled) / visibleCar.Speed *
                                    (visibleCar.Speed - Math.Abs(train.SpeedMpS));
                                var carNumber = (int)(Math.Max(maxTrainLengthRecoveredM - 40, maxTrainLengthRecoveredM / 2) / 30);
                                testTrainCar = trainForwards ? train.Cars[Math.Min(carNumber, train.Cars.Count - 1)] :
                                    train.Cars[Math.Max(0, train.Cars.Count - 1 - carNumber)];
                            }
                            else
                            {
                                // select first car in direction of movement
                                testTrainCar = trainForwards ? train.FirstCar : train.LastCar;
                            }
                            if (Math.Abs(visibleCar.FrontTraveller.WorldLocation.Location.Y - testTrainCar.WorldPosition.WorldLocation.Location.Y) < 30.0f)
                            {
                                var distanceTo = WorldLocation.GetDistance2D(visibleCar.FrontTraveller.WorldLocation, testTrainCar.WorldPosition.WorldLocation).Length();
                                if (distanceTo < MaxDistFromRoadCarM)
                                {
                                    minDistanceM = distanceTo;
                                    NearRoadCar = visibleCar;
                                    LastCheckCar = testTrainCar;
                                    break;
                                }
                            }
                        }
                    }
                    if (NearRoadCar != null)
                    // readcar found
                    {
                        SpecialPointFound = true;
                        RoadCarFound = true;
                        // CarriesCamera needed to increase distance of following car
                        NearRoadCar.CarriesCamera = true;
                        var traveller = new Traveller(NearRoadCar.FrontTraveller);
                        traveller.Move(-2.5f - 0.15f * NearRoadCar.Length);
                        TrackCameraLocation = traveller.WorldLocation;
                    }
                }

                if (!SpecialPointFound)
                {
                    // try to find near level crossing then
                    Simulation.LevelCrossingItem newLevelCrossingItem = Simulation.LevelCrossingItem.None;
                    float FrontDist = -1;
                    newLevelCrossingItem = Viewer.Simulator.LevelCrossings.SearchNearLevelCrossing(train, MaximumSpecialPointDistance * 0.7f, trainForwards, out FrontDist);
                    if (newLevelCrossingItem != Simulation.LevelCrossingItem.None)
                    {
                        SpecialPointFound = true;
                        trainClose = false;
                        LastCheckCar = trainForwards ? train.Cars.First() : train.Cars.Last();
                        TrackCameraLocation = newLevelCrossingItem.Location;
                        Traveller roadTraveller;
                        // decide randomly at which side of the level crossing the camera will be located
                        roadTraveller = new Traveller(Viewer.Simulator.TSectionDat, Viewer.Simulator.RDB.RoadTrackDB.TrackNodes, Viewer.Simulator.RDB.RoadTrackDB.TrackNodes[newLevelCrossingItem.TrackIndex] as TrackVectorNode,
                            TrackCameraLocation, Viewer.Random.Next(2) == 0 ? Traveller.TravellerDirection.Forward : Traveller.TravellerDirection.Backward);
                        roadTraveller.Move(12.5f);
                        tdb.Move(FrontDist);
                        TrackCameraLocation = roadTraveller.WorldLocation;
                    }
                }

                if (!SpecialPointFound && !trainClose)
                {
                    tdb = trainForwards ? new Traveller(train.FrontTDBTraveller) : new Traveller(train.RearTDBTraveller, Traveller.TravellerDirection.Backward); // return to standard
                    TrackCameraLocation = GoToNewLocation(ref tdb, train, trainForwards);
                }

                if (TrackCameraLocation != WorldLocation.None && !trainClose)
                {
                    TrackCameraLocation = TrackCameraLocation.Normalize();
                    cameraLocation = TrackCameraLocation;
                    if (!RoadCarFound)
                    {
                        TrackCameraLocation = TrackCameraLocation.SetElevation(Viewer.Tiles.GetElevation(TrackCameraLocation));
                        cameraLocation = TrackCameraLocation.SetElevation(Math.Max(tdb.Y, TrackCameraLocation.Location.Y) + CameraAltitude + CameraAltitudeOffset + (platformFound ? 0.35f : 0.0f));
                    }
                    else
                    {
                        TrackCameraLocation = cameraLocation;
                        cameraLocation = cameraLocation.ChangeElevation(1.8f);
                    }
                    DistanceRunM = 0f;
                }
            }

            targetLocation = targetLocation.ChangeElevation(TargetAltitude);
            FirstUpdateLoop = false;
            UpdateListener();
        }

        protected override void ZoomIn(float speed)
        {
            if (!RoadCarFound)
            {
                var movement = new Vector3(0, 0, 0);
                movement.Z += speed;
                ZRadians += movement.Z;
                MoveCamera(movement);
            }
            else
            {
                NearRoadCar.ChangeSpeed(speed);
            }
        }
    }
}
