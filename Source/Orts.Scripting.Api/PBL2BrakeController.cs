// COPYRIGHT 2010, 2012 by the Open Rails project.
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
using Orts.Common;
using Orts.Scripting.Api;

namespace Orts.Scripting.Script
{
    public class PBL2BrakeController : BrakeController
    {
        enum State
        {
            Overcharge,
            OverchargeElimination,
            QuickRelease,
            Release,
            Lap,
            Apply,
            Emergency
        }

        private float overchargeValue;
        private float quickReleaseValue;
        private float releaseValue;
        private float lapValue;
        private float applyValue;
        private float emergencyValue;

        // brake controller values
        private const double overchargePressureBar = 0.4;
        private const double overchargeEleminationPressureRateBarpS = 0.0025;
        private const double firstDepressureBar = 0.5;
        private const double brakeReleasedDepressureBar = 0.2;

        private State currentState;

        private bool firstDepression;
        private bool overcharge;
        private bool quickRelease;
        private bool release;
        private bool apply;

        private double regulatorPressureBar = 0.0;

        public PBL2BrakeController()
        {
        }

        public override void Initialize()
        {
            foreach (INotchController notch in Notches())
            {
                switch (notch.NotchStateType)
                {
                    case ControllerState.Release:
                        releaseValue = notch.Value;
                        break;
                    case ControllerState.FullQuickRelease:
                        overchargeValue = notch.Value;
                        quickReleaseValue = notch.Value;
                        break;
                    case ControllerState.Lap:
                        lapValue = notch.Value;
                        break;
                    case ControllerState.Apply:
                    case ControllerState.GSelfLap:
                    case ControllerState.GSelfLapH:
                        applyValue = notch.Value;
                        break;
                    case ControllerState.Emergency:
                        emergencyValue = notch.Value;
                        break;
                }
            }
        }

        public override void InitializeMoving()
        {
        }

        public override float Update(double elapsedSeconds)
        {
            if (apply)
                SetCurrentValue(applyValue);
            else if (release)
                SetCurrentValue(releaseValue);
            else
                SetCurrentValue(lapValue);

            return CurrentValue();
        }

        public override Tuple<double, double> UpdatePressure(double pressureBar, double epPressureBar, double elapsedClockSeconds)
        {
            regulatorPressureBar = Math.Min(MaxPressureBar(), MainReservoirPressureBar());

            if (!firstDepression && apply && pressureBar > Math.Max(regulatorPressureBar - firstDepressureBar, 0))
                firstDepression = true;
            else if (firstDepression && pressureBar <= Math.Max(regulatorPressureBar - firstDepressureBar, 0))
                firstDepression = false;

            if (apply && overcharge)
                overcharge = false;
            if (apply && quickRelease)
                quickRelease = false;

            if (EmergencyBrakingPushButton() || TCSEmergencyBraking())
                currentState = State.Emergency;
            else if (
                apply && pressureBar > regulatorPressureBar - FullServReductionBar()
                || firstDepression && !release && !quickRelease && pressureBar > regulatorPressureBar - firstDepressureBar
                )
                currentState = State.Apply;
            else if (/*overchargeElimination && */pressureBar > regulatorPressureBar)
                currentState = State.OverchargeElimination;
            else if (overcharge && pressureBar <= regulatorPressureBar + overchargePressureBar)
                currentState = State.Overcharge;
            else if (quickRelease && pressureBar < regulatorPressureBar)
                currentState = State.QuickRelease;
            else if (release && pressureBar < regulatorPressureBar
                    || !firstDepression && pressureBar > regulatorPressureBar - brakeReleasedDepressureBar && pressureBar < regulatorPressureBar
                    || pressureBar < regulatorPressureBar - FullServReductionBar())
                currentState = State.Release;
            else
                currentState = State.Lap;

            switch (currentState)
            {
                case State.Overcharge:
                    SetUpdateValue(-1);

                    pressureBar += QuickReleaseRateBarpS() * elapsedClockSeconds;
                    epPressureBar -= QuickReleaseRateBarpS() * elapsedClockSeconds;

                    if (pressureBar > MaxPressureBar() + overchargePressureBar)
                        pressureBar = MaxPressureBar() + overchargePressureBar;
                    break;

                case State.OverchargeElimination:
                    SetUpdateValue(-1);

                    pressureBar -= overchargeEleminationPressureRateBarpS * elapsedClockSeconds;

                    if (pressureBar < MaxPressureBar())
                        pressureBar = MaxPressureBar();
                    break;

                case State.QuickRelease:
                    SetUpdateValue(-1);

                    pressureBar += QuickReleaseRateBarpS() * elapsedClockSeconds;
                    epPressureBar -= QuickReleaseRateBarpS() * elapsedClockSeconds;

                    if (pressureBar > regulatorPressureBar)
                        pressureBar = regulatorPressureBar;
                    break;

                case State.Release:
                    SetUpdateValue(-1);

                    pressureBar += ReleaseRateBarpS() * elapsedClockSeconds;
                    epPressureBar -= ReleaseRateBarpS() * elapsedClockSeconds;

                    if (pressureBar > regulatorPressureBar)
                        pressureBar = regulatorPressureBar;
                    break;

                case State.Lap:
                    SetUpdateValue(0);
                    break;

                case State.Apply:
                    SetUpdateValue(1);

                    pressureBar -= ApplyRateBarpS() * elapsedClockSeconds;
                    epPressureBar += ApplyRateBarpS() * elapsedClockSeconds;

                    if (pressureBar < Math.Max(regulatorPressureBar - FullServReductionBar(), 0.0))
                        pressureBar = Math.Max(regulatorPressureBar - FullServReductionBar(), 0.0);
                    break;

                case State.Emergency:
                    SetUpdateValue(1);

                    pressureBar -= EmergencyRateBarpS() * elapsedClockSeconds;

                    if (pressureBar < 0)
                        pressureBar = 0;
                    break;
            }

            if (epPressureBar > MaxPressureBar())
                epPressureBar = MaxPressureBar();
            if (epPressureBar < 0)
                epPressureBar = 0;

            if (quickRelease && pressureBar == regulatorPressureBar)
                quickRelease = false;

            return new Tuple<double, double>(pressureBar, epPressureBar);
        }

        public override double UpdateEngineBrakePressure(double pressureBar, double elapsedClockSeconds)
        {
            switch (currentState)
            {
                case State.Release:
                    SetCurrentValue(releaseValue);
                    SetUpdateValue(-1);
                    pressureBar -= ReleaseRateBarpS() * elapsedClockSeconds;
                    break;

                case State.Apply:
                    SetCurrentValue(applyValue);
                    SetUpdateValue(0);
                    pressureBar += ApplyRateBarpS() * elapsedClockSeconds;
                    break;

                case State.Emergency:
                    SetCurrentValue(emergencyValue);
                    SetUpdateValue(1);
                    pressureBar += EmergencyRateBarpS() * elapsedClockSeconds;
                    break;
            }

            if (pressureBar > MaxPressureBar())
                pressureBar = MaxPressureBar();
            if (pressureBar < 0)
                pressureBar = 0;
            return pressureBar;
        }

        public override void HandleEvent(BrakeControllerEvent evt)
        {
            switch (evt)
            {
                case BrakeControllerEvent.StartIncrease:
                    apply = true;
                    break;

                case BrakeControllerEvent.StopIncrease:
                    apply = false;
                    break;

                case BrakeControllerEvent.StartDecrease:
                    release = true;
                    break;

                case BrakeControllerEvent.StopDecrease:
                    release = false;
                    break;
            }
        }

        public override void HandleEvent(BrakeControllerEvent evt, float? value)
        {
            switch (evt)
            {
                case BrakeControllerEvent.StartIncrease:
                    apply = true;
                    break;

                case BrakeControllerEvent.StartDecrease:
                    release = true;
                    break;

                case BrakeControllerEvent.SetCurrentPercent:
                    if (value != null)
                    {
                        float percent = value ?? 0F;
                        percent *= 100;

                        if (percent < 40)
                        {
                            apply = true;
                            release = false;
                        }
                        else if (percent > 60)
                        {
                            apply = false;
                            release = true;
                        }
                        else
                        {
                            apply = false;
                            release = false;
                        }
                    }
                    break;

                case BrakeControllerEvent.SetCurrentValue:
                    if (value != null)
                    {
                        float newValue = value ?? 0F;
                        SetValue(newValue);
                    }
                    break;
            }
        }

        public override bool IsValid()
        {
            return true;
        }

        public override ControllerState GetState()
        {
            switch (currentState)
            {
                case State.Overcharge:
                    return ControllerState.Overcharge;

                case State.OverchargeElimination:
                    return ControllerState.Overcharge;

                case State.QuickRelease:
                    return ControllerState.FullQuickRelease;

                case State.Release:
                    return ControllerState.Release;

                case State.Lap:
                    return ControllerState.Lap;

                case State.Apply:
                    return ControllerState.Apply;

                case State.Emergency:
                    if (EmergencyBrakingPushButton())
                        return ControllerState.EBPB;
                    else if (TCSEmergencyBraking())
                        return ControllerState.TCSEmergency;
                    else if (TCSFullServiceBraking())
                        return ControllerState.TCSFullServ;
                    else
                        return ControllerState.Emergency;

                default:
                    return ControllerState.Dummy;
            }
        }

        public override float? GetStateFraction()
        {
            return null;
        }

        private void SetValue(float v)
        {
            SetCurrentValue(v);

            if (CurrentValue() == emergencyValue)
            {
                apply = false;
                release = false;
                quickRelease = false;
            }
            else if (CurrentValue() == applyValue)
            {
                apply = true;
                release = false;
                quickRelease = false;
            }
            else if (CurrentValue() == lapValue)
            {
                apply = false;
                release = false;
                quickRelease = false;
            }
            else if (CurrentValue() == releaseValue)
            {
                apply = false;
                release = true;
                quickRelease = false;
            }
            else if (CurrentValue() == quickReleaseValue)
            {
                apply = false;
                release = false;
                quickRelease = true;
            }
        }
    }
}
