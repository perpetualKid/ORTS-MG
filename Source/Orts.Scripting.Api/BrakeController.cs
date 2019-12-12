using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orts.Common;

namespace Orts.Scripting.Api
{
    public abstract class BrakeController : ScriptBase
    {
        /// <summary>
        /// True if the driver has asked for an emergency braking (push button)
        /// </summary>
        public Func<bool> EmergencyBrakingPushButton;
        /// <summary>
        /// True if the TCS has asked for an emergency braking
        /// </summary>
        public Func<bool> TCSEmergencyBraking;
        /// <summary>
        /// True if the TCS has asked for a full service braking
        /// </summary>
        public Func<bool> TCSFullServiceBraking;
        /// <summary>
        /// Main reservoir pressure
        /// </summary>
        public Func<float> MainReservoirPressureBar;
        /// <summary>
        /// Maximum pressure in the brake pipes and the equalizing reservoir
        /// </summary>
        public Func<float> MaxPressureBar;
        /// <summary>
        /// Release rate of the equalizing reservoir
        /// </summary>
        public Func<float> ReleaseRateBarpS;
        /// <summary>
        /// Quick release rate of the equalizing reservoir
        /// </summary>
        public Func<float> QuickReleaseRateBarpS;
        /// <summary>
        /// Apply rate of the equalizing reservoir
        /// </summary>
        public Func<float> ApplyRateBarpS;
        /// <summary>
        /// Emergency rate of the equalizing reservoir
        /// </summary>
        public Func<float> EmergencyRateBarpS;
        /// <summary>
        /// Depressure needed in order to obtain the full service braking
        /// </summary>
        public Func<float> FullServReductionBar;
        /// <summary>
        /// Release rate of the equalizing reservoir
        /// </summary>
        public Func<float> MinReductionBar;
        /// <summary>
        /// Current value of the brake controller
        /// </summary>
        public Func<float> CurrentValue;
        /// <summary>
        /// Minimum value of the brake controller
        /// </summary>
        public Func<float> MinimumValue;
        /// <summary>
        /// Maximum value of the brake controller
        /// </summary>
        public Func<float> MaximumValue;
        /// <summary>
        /// Step size of the brake controller
        /// </summary>
        public Func<float> StepSize;
        /// <summary>
        /// State of the brake pressure (1 = increasing, -1 = decreasing)
        /// </summary>
        public Func<float> UpdateValue;
        /// <summary>
        /// Gives the list of notches
        /// </summary>
        public Func<List<INotchController>> Notches;

        /// <summary>
        /// Sets the current value of the brake controller lever
        /// </summary>
        public Action<float> SetCurrentValue;
        /// <summary>
        /// Sets the state of the brake pressure (1 = increasing, -1 = decreasing)
        /// </summary>
        public Action<float> SetUpdateValue;

        /// <summary>
        /// Called once at initialization time.
        /// </summary>
        public abstract void Initialize();
        /// <summary>
        /// Called regularly at every simulator update cycle.
        /// </summary>
        ///
        public abstract void InitializeMoving();
        /// <summary>
        /// Called when starting speed > 0
        /// </summary>
        /// 
        public abstract float Update(double elapsedClockSeconds);
        /// <summary>
        /// Called regularly at every simulator update cycle.
        /// </summary>
        public abstract Tuple<double, double> UpdatePressure(double pressureBar, double epPressureBar, double elapsedClockSeconds);
        /// <summary>
        /// Called regularly at every simulator update cycle.
        /// </summary>
        public abstract double UpdateEngineBrakePressure(double pressureBar, double elapsedClockSeconds);
        /// <summary>
        /// Called when an event happens (like the alerter button pressed)
        /// </summary>
        /// <param name="evt">The event happened</param>
        public abstract void HandleEvent(BrakeControllerEvent evt);
        /// <summary>
        /// Called when an event happens (like the alerter button pressed)
        /// </summary>
        /// <param name="evt">The event happened</param>
        /// <param name="value">The value assigned to the event (a target for example). May be null.</param>
        public abstract void HandleEvent(BrakeControllerEvent evt, float? value);
        /// <summary>
        /// Called in order to check if the controller is valid
        /// </summary>
        public abstract bool IsValid();
        /// <summary>
        /// Called in order to get a state for the debug overlay
        /// </summary>
        public abstract ControllerState GetState();
        /// <summary>
        /// Called in order to get a state fraction for the debug overlay
        /// </summary>
        /// <returns>The nullable state fraction</returns>
        public abstract float? GetStateFraction();
    }

}
