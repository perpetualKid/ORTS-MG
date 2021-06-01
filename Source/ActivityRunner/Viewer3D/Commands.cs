// COPYRIGHT 2012, 2013, 2014, 2015 by the Open Rails project.
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
using Orts.Common;
using Orts.ActivityRunner.Viewer3D.Popups;
using Orts.ActivityRunner.Viewer3D.RollingStock;
using Orts.Simulation.Commanding;

namespace Orts.ActivityRunner.Viewer3D
{
    [Serializable()]
    public abstract class ActivityCommand : PausedCommand
        {
        public static ActivityWindow Receiver { get; set; }

        private readonly string EventNameLabel;

        public ActivityCommand( CommandLog log, string eventNameLabel, double pauseDurationS )
            : base(log, pauseDurationS)
        {
            EventNameLabel = eventNameLabel;
            //Redo(); // More consistent but untested
        }

        public override string ToString()
        {
            return $"{base.ToString()} Event: {EventNameLabel} ";
        }
    }

    /// <summary>
    /// Command to automatically re-fuel and re-water locomotive or tender.
    /// </summary>
    [Serializable()]
    public sealed class ImmediateRefillCommand : Command
    {
        public static MSTSLocomotiveViewer Receiver { get; set; }

        public ImmediateRefillCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            Receiver.ImmediateRefill(); 
            // Report();
        }
    }

    /// <summary>
    /// Continuous command to re-fuel and re-water locomotive or tender.
    /// </summary>
    [Serializable()]
    public sealed class RefillCommand : ContinuousCommand
    {
        public static MSTSLocomotiveViewer Receiver { get; set; }

        public RefillCommand(CommandLog log, float? target, double startTime)
            : base(log, true, target, startTime)
        {
            base.target = target;        // Fraction from 0 to 1.0
            this.Time = startTime;  // Continuous commands are created at end of change, so overwrite time when command was created
        }

        public override void Redo()
        {
            if (Receiver == null) return;
            Receiver.RefillChangeTo(target);
            // Report();
        }
    }

    // Other
    [Serializable()]
    public sealed class ChangeCabCommand : Command
    {
        public static Viewer Receiver { get; set; }

        public ChangeCabCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.ChangeCab();
            // Report();
        }
        }

    [Serializable()]
    public sealed class ToggleSwitchAheadCommand : Command
    {
        public static Viewer Receiver { get; set; }

        public ToggleSwitchAheadCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.ToggleSwitchAhead();
            // Report();
        }
        }

    [Serializable()]
    public sealed class ToggleSwitchBehindCommand : Command
    {
        public static Viewer Receiver { get; set; }

        public ToggleSwitchBehindCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.ToggleSwitchBehind();
            // Report();
        }
        }

    [Serializable()]
    public sealed class ToggleAnySwitchCommand : IndexCommand
        {
        public static Viewer Receiver { get; set; }

        public ToggleAnySwitchCommand( CommandLog log, int index )
            : base(log, index)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.ToggleAnySwitch(index);
            // Report();
        }
    }

    [Serializable()]
    public sealed class UncoupleCommand : Command
    {
        public static Viewer Receiver { get; set; }

        private int CarPosition;    // 0 for head of train

        public UncoupleCommand( CommandLog log, int carPosition ) 
            : base(log)
        {
            CarPosition = carPosition;
            Redo();
        }

        public override void Redo()
        {
            Receiver.UncoupleBehind( CarPosition );
            // Report();
        }

        public override string ToString()
        {
            return base.ToString() + " - " + CarPosition.ToString();
        }
    }

    [Serializable()]
    public sealed class SaveScreenshotCommand : Command
    {
        public static Viewer Receiver { get; set; }

        public SaveScreenshotCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.SaveScreenshot = true;
            // Report();
        }
    }

    [Serializable()]
    public sealed class ResumeActivityCommand : ActivityCommand
    {
        public ResumeActivityCommand( CommandLog log, string eventNameLabel, double pauseDurationS )
            : base(log, eventNameLabel, pauseDurationS)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.ResumeActivity();
            // Report();
        }
    }

    [Serializable()]
    public sealed class CloseAndResumeActivityCommand : ActivityCommand
    {
        public CloseAndResumeActivityCommand( CommandLog log, string eventNameLabel, double pauseDurationS )
            : base(log, eventNameLabel, pauseDurationS)
        {
            Redo();
        }
    
        public override void Redo()
        {
            Receiver.CloseBox();
            // Report();
        }
    }

    [Serializable()]
    public sealed class PauseActivityCommand : ActivityCommand
    {
        public PauseActivityCommand(CommandLog log, string eventNameLabel, double pauseDurationS)
            : base(log, eventNameLabel, pauseDurationS)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.PauseActivity();
            // Report();
        }
    }

    [Serializable()]
    public sealed class QuitActivityCommand : ActivityCommand
    {
        public QuitActivityCommand( CommandLog log, string eventNameLabel, double pauseDurationS )
            : base(log, eventNameLabel, pauseDurationS)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.QuitActivity();
            // Report();
        }
    }
    
    [Serializable()]
    public abstract class UseCameraCommand : CameraCommand
    {
        public static Viewer Receiver { get; set; }

        public UseCameraCommand( CommandLog log )
            : base(log)
        {
        }
    }

    [Serializable()]
    public sealed class UseCabCameraCommand : UseCameraCommand
    {

        public UseCabCameraCommand( CommandLog log ) 
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.ThreeDimCabCamera.Enabled)
            {
                Receiver.ThreeDimCabCamera.Activate();
            }
            else
            {
                Receiver.CabCamera.Activate();
            }
        }
    }

	[Serializable()]
	public sealed class ToggleThreeDimensionalCabCameraCommand : UseCameraCommand
	{

		public ToggleThreeDimensionalCabCameraCommand(CommandLog log)
			: base(log)
		{
			Redo();
		}

		public override void Redo()
		{
            Receiver.ThreeDimCabCamera.Enabled = !Receiver.ThreeDimCabCamera.Enabled;

            if (Receiver.ThreeDimCabCamera.Enabled && Receiver.Camera == Receiver.CabCamera)
            {
                Receiver.ThreeDimCabCamera.Activate();
            }
            else if (!Receiver.ThreeDimCabCamera.Enabled && Receiver.Camera == Receiver.ThreeDimCabCamera)
            {
                Receiver.CabCamera.Activate();
            }
        }
	}
	
	[Serializable()]
    public sealed class UseFrontCameraCommand : UseCameraCommand
    {

        public UseFrontCameraCommand( CommandLog log )
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.FrontCamera.Activate();
            // Report();
        }
    }

    [Serializable()]
    public sealed class UseBackCameraCommand : UseCameraCommand
    {

        public UseBackCameraCommand( CommandLog log )
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.BackCamera.Activate();
            // Report();
        }
    }

    [Serializable()]
    public sealed class UseHeadOutForwardCameraCommand : UseCameraCommand
    {

        public UseHeadOutForwardCameraCommand( CommandLog log )
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.HeadOutForwardCamera.Activate();
            // Report();
        }
    }

    [Serializable()]
    public sealed class UseFreeRoamCameraCommand : UseCameraCommand
    {

        public UseFreeRoamCameraCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            // Makes a new free roam camera that adopts the same viewpoint as the current camera.
            // List item [0] is the current free roam camera, most recent free roam camera is at item [1]. 
            // Adds existing viewpoint to the head of the history list.
            // If this is the first use of the free roam camera, then the view point is added twice, so
            // it gets stored in the history list.
            if (Receiver.FreeRoamCameraList.Count == 0)
                Receiver.FreeRoamCameraList.Insert(0, new FreeRoamCamera(Receiver, Receiver.Camera));
            Receiver.FreeRoamCameraList.Insert(0, new FreeRoamCamera(Receiver, Receiver.Camera));
            Receiver.FreeRoamCamera.Activate();
        }
    }
    
    [Serializable()]
    public sealed class UsePreviousFreeRoamCameraCommand : UseCameraCommand
    {

        public UsePreviousFreeRoamCameraCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.ChangeToPreviousFreeRoamCamera();
        }
    }
    
    [Serializable()]
    public sealed class UseHeadOutBackCameraCommand : UseCameraCommand
    {

        public UseHeadOutBackCameraCommand( CommandLog log )
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.HeadOutBackCamera.Activate();
            // Report();
        }
    }

    [Serializable()]
    public sealed class UseBrakemanCameraCommand : UseCameraCommand
    {

        public UseBrakemanCameraCommand( CommandLog log )
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.BrakemanCamera.Activate();
            // Report();
        }
    }

    [Serializable()]
    public sealed class UsePassengerCameraCommand : UseCameraCommand
    {

        public UsePassengerCameraCommand( CommandLog log )
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.PassengerCamera.Activate();
            // Report();
        }
    }

    [Serializable()]
    public sealed class UseTracksideCameraCommand : UseCameraCommand
    {

        public UseTracksideCameraCommand( CommandLog log )
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.TracksideCamera.Activate();
            // Report();
        }
    }

    [Serializable()]
    public sealed class UseSpecialTracksideCameraCommand : UseCameraCommand
    {

        public UseSpecialTracksideCameraCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            Receiver.SpecialTracksideCamera.Activate();
            // Report();
        }
    }


    [Serializable()]
    public abstract class MoveCameraCommand : CameraCommand
    {
        public static Viewer Receiver { get; set; }
        protected double EndTime;

        public MoveCameraCommand( CommandLog log, double startTime, double endTime )
            : base(log)
        {
            Time = startTime;
            EndTime = endTime;
        }

        public override string ToString()
        {
            return $"{base.ToString()} - {FormatStrings.FormatPreciseTime(EndTime)}";
        }
    }

    [Serializable()]
    public sealed class CameraRotateUpDownCommand : MoveCameraCommand
    {
        private readonly float RotationXRadians;

        public CameraRotateUpDownCommand( CommandLog log, double startTime, double endTime, float rx )
            : base(log, startTime, endTime)
        {
            RotationXRadians = rx;
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is RotatingCamera)
            {
                var c = Receiver.Camera as RotatingCamera;
                c.RotationXTargetRadians = RotationXRadians;
                c.EndTime = EndTime;
            }
            // Report();
        }

        public override string ToString()
        {
            return $"{base.ToString()} , {RotationXRadians}";
        }
    }

    [Serializable()]
    public sealed class CameraRotateLeftRightCommand : MoveCameraCommand
    {
        private float RotationYRadians;

        public CameraRotateLeftRightCommand( CommandLog log, double startTime, double endTime, float ry )
            : base(log, startTime, endTime)
        {
            RotationYRadians = ry;
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is RotatingCamera)
            {
                var c = Receiver.Camera as RotatingCamera;
                c.RotationYTargetRadians = RotationYRadians;
                c.EndTime = EndTime;
            }
            // Report();
        }

        public override string ToString()
        {
            return $"{base.ToString()} , {RotationYRadians}";
        }
    }

    /// <summary>
    /// Records rotations made by mouse movements.
    /// </summary>
    [Serializable()]
    public sealed class CameraMouseRotateCommand : MoveCameraCommand
    {
        private float RotationXRadians;
        private float RotationYRadians;

        public CameraMouseRotateCommand( CommandLog log, double startTime, double endTime, float rx, float ry )
            : base(log, startTime, endTime)
        {
            RotationXRadians = rx;
            RotationYRadians = ry;
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is RotatingCamera)
            {
                var c = Receiver.Camera as RotatingCamera;
                c.EndTime = EndTime;
                c.RotationXTargetRadians = RotationXRadians;
                c.RotationYTargetRadians = RotationYRadians;
            }
            // Report();
        }

        public override string ToString()
        {
            return $"{base.ToString()}, {EndTime} {RotationXRadians} {RotationYRadians}";
        }
    }

    [Serializable()]
    public sealed class CameraXCommand : MoveCameraCommand
    {
        private float XRadians;

        public CameraXCommand( CommandLog log, double startTime, double endTime, float xr )
            : base(log, startTime, endTime)
        {
            XRadians = xr;
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is RotatingCamera)
            {
                var c = Receiver.Camera as RotatingCamera;
                c.XTargetRadians = XRadians;
                c.EndTime = EndTime;
            }
            // Report();
        }

        public override string ToString()
        {
            return $"{base.ToString()}, {XRadians}";
        }
    }

    [Serializable()]
    public sealed class CameraYCommand : MoveCameraCommand
    {
        private float YRadians;

        public CameraYCommand( CommandLog log, double startTime, double endTime, float yr )
            : base(log, startTime, endTime)
        {
            YRadians = yr;
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is RotatingCamera)
            {
                var c = Receiver.Camera as RotatingCamera;
                c.YTargetRadians = YRadians;
                c.EndTime = EndTime;
            }
            // Report();
        }

        public override string ToString()
        {
            return $"{base.ToString()}, {YRadians}";
        }
    }

    [Serializable()]
    public sealed class CameraZCommand : MoveCameraCommand
    {
        private float ZRadians;

        public CameraZCommand( CommandLog log, double startTime, double endTime, float zr )
            : base(log, startTime, endTime)
        {
            ZRadians = zr;
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is RotatingCamera)
            {
                var c = Receiver.Camera as RotatingCamera;
                c.ZTargetRadians = ZRadians;
                c.EndTime = EndTime;
            } // Report();
        }

        public override string ToString()
        {
            return $"{base.ToString()}, {ZRadians}";
        }
    }

	[Serializable()]
	public sealed class CameraMoveXYZCommand : MoveCameraCommand
	{
        private float X, Y, Z;

        public CameraMoveXYZCommand(CommandLog log, double startTime, double endTime, float xr, float yr, float zr)
			: base(log, startTime, endTime)
		{
			X = xr; Y = yr; Z = zr;
			Redo();
		}

		public override void Redo()
		{
			if (Receiver.Camera is CabCamera3D)
			{
				var c = Receiver.Camera as CabCamera3D;
				c.MoveCameraXYZ(X, Y, Z);
				c.EndTime = EndTime;
			}
			// Report();
		}

		public override string ToString()
		{
			return $"{base.ToString()}, {X}";
		}
	}

	[Serializable()]
    public sealed class TrackingCameraXCommand : MoveCameraCommand
    {
        private float PositionXRadians;

        public TrackingCameraXCommand( CommandLog log, double startTime, double endTime, float rx )
            : base(log, startTime, endTime)
        {
            PositionXRadians = rx;
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is TrackingCamera)
            {
                var c = Receiver.Camera as TrackingCamera;
                c.PositionXTargetRadians = PositionXRadians;
                c.EndTime = EndTime;
            }
            // Report();
        }

        public override string ToString()
        {
            return $"{base.ToString()}, {PositionXRadians}";
        }
    }

    [Serializable()]
    public sealed class TrackingCameraYCommand : MoveCameraCommand
    {
        private float PositionYRadians;

        public TrackingCameraYCommand( CommandLog log, double startTime, double endTime, float ry )
            : base(log, startTime, endTime)
        {
            PositionYRadians = ry;
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is TrackingCamera)
            {
                var c = Receiver.Camera as TrackingCamera;
                c.PositionYTargetRadians = PositionYRadians;
                c.EndTime = EndTime;
            }
            // Report();
        }

        public override string ToString()
        {
            return $"{base.ToString()}, {PositionYRadians}";
        }
    }

    [Serializable()]
    public sealed class TrackingCameraZCommand : MoveCameraCommand
    {
        private float PositionDistanceMetres;

        public TrackingCameraZCommand( CommandLog log, double startTime, double endTime, float d )
            : base(log, startTime, endTime)
        {
            PositionDistanceMetres = d;
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is TrackingCamera)
            {
                var c = Receiver.Camera as TrackingCamera;
                c.PositionDistanceTargetMetres = PositionDistanceMetres;
                c.EndTime = EndTime;
            }
            // Report();
        }

        public override string ToString()
        {
            return $"{base.ToString()}, {PositionDistanceMetres}";
        }
    }

    [Serializable()]
    public sealed class NextCarCommand : UseCameraCommand
    {

        public NextCarCommand( CommandLog log )
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is AttachedCamera)
            {
                var c = Receiver.Camera as AttachedCamera;
                c.NextCar();
            }
            // Report();
        }
    }

    [Serializable()]
    public sealed class PreviousCarCommand : UseCameraCommand
    {

        public PreviousCarCommand( CommandLog log )
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is AttachedCamera)
            {
                var c = Receiver.Camera as AttachedCamera;
                c.PreviousCar();
            }
            // Report();
        }
    }

    [Serializable()]
    public sealed class FirstCarCommand : UseCameraCommand
    {

        public FirstCarCommand( CommandLog log )
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is AttachedCamera)
            {
                var c = Receiver.Camera as AttachedCamera;
                c.FirstCar();
            }
            // Report();
        }
    }

    [Serializable()]
    public sealed class LastCarCommand : UseCameraCommand
    {

        public LastCarCommand( CommandLog log )
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is AttachedCamera)
            {
                var c = Receiver.Camera as AttachedCamera;
                c.LastCar();
            }
            // Report();
        }
    }

    [Serializable]
    public sealed class FieldOfViewCommand : UseCameraCommand
    {
        private float FieldOfView;

        public FieldOfViewCommand(CommandLog log, float fieldOfView)
            : base(log)
        {
            FieldOfView = fieldOfView;
            Redo();
        }

        public override void Redo()
        {
            Receiver.Camera.FieldOfView = FieldOfView;
            Receiver.Camera.ScreenChanged();
        }
    }

    [Serializable()]
    public sealed class CameraChangePassengerViewPointCommand : UseCameraCommand
    {

        public CameraChangePassengerViewPointCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera.AttachedCar.PassengerViewpoints.Count == 1)
                Receiver.PassengerCamera.SwitchSideCameraCar(Receiver.Camera.AttachedCar);
            else Receiver.PassengerCamera.ChangePassengerViewPoint(Receiver.Camera.AttachedCar);
            // Report();
        }
    }

    [Serializable()]
    public sealed class ToggleBrowseBackwardsCommand : UseCameraCommand
    {

        public ToggleBrowseBackwardsCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is TrackingCamera)
            {
                var c = Receiver.Camera as TrackingCamera;
                c.ToggleBrowseBackwards();
            }
            // Report();
        }
    }

   [Serializable()]
    public sealed class ToggleBrowseForwardsCommand : UseCameraCommand
    {

        public ToggleBrowseForwardsCommand(CommandLog log)
            : base(log)
        {
            Redo();
        }

        public override void Redo()
        {
            if (Receiver.Camera is TrackingCamera)
            {
                var c = Receiver.Camera as TrackingCamera;
                c.ToggleBrowseForwards();
            }
            // Report();
        }
    }
}
