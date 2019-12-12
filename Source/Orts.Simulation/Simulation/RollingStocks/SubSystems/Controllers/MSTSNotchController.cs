// COPYRIGHT 2010, 2011, 2012, 2013 by the Open Rails project.
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

using System.Collections.Generic;
using System.IO;

using Microsoft.Xna.Framework;
using Orts.Common;
using Orts.Formats.Msts.Parsers;
using Orts.Scripting.Api;

namespace Orts.Simulation.RollingStocks.SubSystems.Controllers
{
    public class MSTSNotch: INotchController
    {
        public float Value { get ; set; }
        public bool Smooth { get; set; }
        public ControllerState NotchStateType { get; set ; }

        public MSTSNotch(float v, int s, string type, STFReader stf)
        {
            Value = v;
            Smooth = s == 0 ? false : true;
            NotchStateType = ControllerState.Dummy;  // Default to a dummy controller state if no valid alternative state used
            string lower = type.ToLower();
            if (lower.StartsWith("trainbrakescontroller"))
                lower = lower.Substring(21);
            if (lower.StartsWith("enginebrakescontroller"))
                lower = lower.Substring(22);
            switch (lower)
            {
                case "dummy": break;
                case ")": break;
                case "releasestart": NotchStateType = ControllerState.Release; break;
                case "fullquickreleasestart": NotchStateType = ControllerState.FullQuickRelease; break;
                case "runningstart": NotchStateType = ControllerState.Running; break;
                case "selflapstart": NotchStateType = ControllerState.SelfLap; break;
                case "holdstart": NotchStateType = ControllerState.Hold; break;
                case "holdlappedstart": NotchStateType = ControllerState.Lap; break;
                case "neutralhandleoffstart": NotchStateType = ControllerState.Neutral; break;
                case "graduatedselflaplimitedstart": NotchStateType = ControllerState.GSelfLap; break;
                case "graduatedselflaplimitedholdingstart": NotchStateType = ControllerState.GSelfLapH; break;
                case "applystart": NotchStateType = ControllerState.Apply; break;
                case "continuousservicestart": NotchStateType = ControllerState.ContServ; break;
                case "suppressionstart": NotchStateType = ControllerState.Suppression; break;
                case "fullservicestart": NotchStateType = ControllerState.FullServ; break;
                case "emergencystart": NotchStateType = ControllerState.Emergency; break;
                case "minimalreductionstart": NotchStateType = ControllerState.MinimalReduction; break;
                case "epapplystart": NotchStateType = ControllerState.EPApply; break;
                case "epholdstart": NotchStateType = ControllerState.SelfLap; break;
                case "vacuumcontinuousservicestart": NotchStateType = ControllerState.VacContServ; break;
                default:
                    STFException.TraceInformation(stf, "Skipped unknown notch type " + type);
                    break;
            }
        }
        public MSTSNotch(float v, bool s, int t)
        {
            Value = v;
            Smooth = s;
            NotchStateType = (ControllerState)t;
        }

        public MSTSNotch(INotchController other)
        {
            Value = other.Value;
            Smooth = other.Smooth;
            NotchStateType = other.NotchStateType;
        }

        public MSTSNotch(BinaryReader inf)
        {
            Value = inf.ReadSingle();
            Smooth = inf.ReadBoolean();
            NotchStateType = (ControllerState)inf.ReadInt32();
        }

        public MSTSNotch Clone()
        {
            return new MSTSNotch(this);
        }

        public void Save(BinaryWriter outf)
        {
            outf.Write(Value);
            outf.Write(Smooth);
            outf.Write((int)NotchStateType);
        }
    }

    /**
     * This is the most used controller. The main use is for diesel locomotives' Throttle control.
     * 
     * It is used with single keypress, this means that when the user press a key, only the keydown event is handled.
     * The user need to press the key multiple times to update this controller.
     * 
     */
    public class MSTSNotchController: IController
    {
        public float CurrentValue { get; set; }
        public float IntermediateValue;
        public float MinimumValue;
        public float MaximumValue = 1;
        public const float StandardBoost = 5.0f; // standard step size multiplier
        public const float FastBoost = 20.0f;
        public float StepSize;
        private List<INotchController> Notches = new List<INotchController>();
        public int CurrentNotch { get; set; }
        public bool ToZero = false; // true if controller zero command;

        private float OldValue;

        //Does not need to persist
        //this indicates if the controller is increasing or decreasing, 0 no changes
        public float UpdateValue { get; set; }
        private float? controllerTarget;
        public double CommandStartTime { get; set; }

        #region CONSTRUCTORS

        public MSTSNotchController()
        {
        }

        public MSTSNotchController(int numOfNotches)
        {
            MinimumValue = 0;
            MaximumValue = numOfNotches - 1;
            StepSize = 1;
            for (int i = 0; i < numOfNotches; i++)
                Notches.Add(new MSTSNotch(i, false, 0));
        }

        public MSTSNotchController(float min, float max, float stepSize)
        {
            MinimumValue = min;
            MaximumValue = max;
            StepSize = stepSize;
        }

        public MSTSNotchController(MSTSNotchController other)
        {
            CurrentValue = other.CurrentValue;
            IntermediateValue = other.IntermediateValue;
            MinimumValue = other.MinimumValue;
            MaximumValue = other.MaximumValue;
            StepSize = other.StepSize;
            CurrentNotch = other.CurrentNotch;

            foreach (MSTSNotch notch in other.Notches)
            {
                Notches.Add(notch.Clone());
            }
        }

        public MSTSNotchController(STFReader stf)
        {
            Parse(stf);
        }

        public MSTSNotchController(List<INotchController> notches)
        {
            Notches = notches;
        }
        #endregion

        public virtual IController Clone()
        {
            return new MSTSNotchController(this);
        }

        public virtual bool IsValid()
        {
            return StepSize != 0;
        }

        public void Parse(STFReader stf)
        {
            stf.MustMatch("(");
            MinimumValue = stf.ReadFloat(STFReader.Units.None, null);
            MaximumValue = stf.ReadFloat(STFReader.Units.None, null);
            StepSize = stf.ReadFloat(STFReader.Units.None, null);
            IntermediateValue = CurrentValue = stf.ReadFloat(STFReader.Units.None, null);
            string token = stf.ReadItem(); // s/b numnotches
            if (string.Compare(token, "NumNotches", true) != 0) // handle error in gp38.eng where extra parameter provided before NumNotches statement 
                stf.ReadItem();
            stf.MustMatch("(");
            stf.ReadInt(null);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("notch", ()=>{
                    stf.MustMatch("(");
                    float value = stf.ReadFloat(STFReader.Units.None, null);
                    int smooth = stf.ReadInt(null);
                    string type = stf.ReadString();
                    Notches.Add(new MSTSNotch(value, smooth, type, stf));
                    if (type != ")") stf.SkipRestOfBlock();
                }),
            });
            SetValue(CurrentValue);
        }

        public int NotchCount()
        {
            return Notches.Count;
        }

        private float GetNotchBoost()
        {
            return (ToZero && ((CurrentNotch >= 0 && Notches[CurrentNotch].Smooth) || Notches.Count == 0 || 
                IntermediateValue - CurrentValue > StepSize) ? FastBoost : StandardBoost);
        }

        /// <summary>
        /// Sets the actual value of the controller, and adjusts the actual notch to match.
        /// </summary>
        /// <param name="value">Normalized value the controller to be set to. Normally is within range [-1..1]</param>
        /// <returns>1 or -1 if there was a significant change in controller position, otherwise 0.
        /// Needed for hinting whether a serializable command is to be issued for repeatability.
        /// Sign is indicating the direction of change, being displayed by confirmer text.</returns>
        public int SetValue(float value)
        {
            CurrentValue = IntermediateValue = MathHelper.Clamp(value, MinimumValue, MaximumValue);
            var oldNotch = CurrentNotch;

            for (CurrentNotch = Notches.Count - 1; CurrentNotch > 0; CurrentNotch--)
            {
                if (Notches[CurrentNotch].Value <= CurrentValue)
                    break;
            }

            if (CurrentNotch >= 0 && !Notches[CurrentNotch].Smooth)
                CurrentValue = Notches[CurrentNotch].Value;

            var change = CurrentNotch > oldNotch || CurrentValue > OldValue + 0.1f || CurrentValue == 1 && OldValue < 1 
                ? 1 : CurrentNotch < oldNotch || CurrentValue < OldValue - 0.1f || CurrentValue == 0 && OldValue > 0 ? -1 : 0;
            if (change != 0)
                OldValue = CurrentValue;

            return change;
        }

        public float SetPercent(float percent)
        {
            float v = (MinimumValue < 0 && percent < 0 ? -MinimumValue : MaximumValue) * percent / 100;
            CurrentValue = MathHelper.Clamp(v, MinimumValue, MaximumValue);

            if (CurrentNotch >= 0)
            {
                if (Notches[Notches.Count - 1].NotchStateType == ControllerState.Emergency)
                    v = Notches[Notches.Count - 1].Value * percent / 100;
                for (; ; )
                {
                    INotchController notch = Notches[CurrentNotch];
                    if (CurrentNotch > 0 && v < notch.Value)
                    {
                        INotchController prev = Notches[CurrentNotch-1];
                        if (!notch.Smooth && !prev.Smooth && v - prev.Value > .45 * (notch.Value - prev.Value))
                            break;
                        CurrentNotch--;
                        continue;
                    }
                    if (CurrentNotch < Notches.Count - 1)
                    {
                        INotchController next = Notches[CurrentNotch + 1];
                        if (next.NotchStateType != ControllerState.Emergency)
                        {
                            if ((notch.Smooth || next.Smooth) && v < next.Value)
                                break;
                            if (!notch.Smooth && !next.Smooth && v - notch.Value < .55 * (next.Value - notch.Value))
                                break;
                            CurrentNotch++;
                            continue;
                        }
                    }
                    break;
                }
                if (Notches[CurrentNotch].Smooth)
                    CurrentValue = v;
                else
                    CurrentValue = Notches[CurrentNotch].Value;
            }
            IntermediateValue = CurrentValue;
            return 100 * CurrentValue;
        }

        public void StartIncrease( float? target ) {
            controllerTarget = target;
            ToZero = false;
            StartIncrease();
        }

        public void StartIncrease()
        {
            UpdateValue = 1;

            // When we have notches and the current Notch does not require smooth, we go directly to the next notch
            if ((Notches.Count > 0) && (CurrentNotch < Notches.Count - 1) && (!Notches[CurrentNotch].Smooth))
            {
                ++CurrentNotch;
                IntermediateValue = CurrentValue = Notches[CurrentNotch].Value;
            }
		}

        public void StopIncrease()
        {
            UpdateValue = 0;
        }

        public void StartDecrease( float? target, bool toZero = false)
        {
            controllerTarget = target;
            ToZero = toZero;
            StartDecrease();
        }
        
        public void StartDecrease()
        {
            UpdateValue = -1;

            //If we have notches and the previous Notch does not require smooth, we go directly to the previous notch
            if ((Notches.Count > 0) && (CurrentNotch > 0) && SmoothMin() == null)
            {
                //Keep intermediate value with the "previous" notch, so it will take a while to change notches
                //again if the user keep holding the key
                IntermediateValue = Notches[CurrentNotch].Value;
                CurrentNotch--;
                CurrentValue = Notches[CurrentNotch].Value;
            }
        }

        public void StopDecrease()
        {
            UpdateValue = 0;
        }

        public float Update(double elapsedSeconds)
        {
            if (UpdateValue == 1 || UpdateValue == -1)
            {
                CheckControllerTargetAchieved();
                UpdateValues(elapsedSeconds, UpdateValue);
            }
            return CurrentValue;
        }

        /// <summary>
        /// If a target has been set, then stop once it's reached and also cancel the target.
        /// </summary>
        public void CheckControllerTargetAchieved() {
            if( controllerTarget != null )
            {
                if( UpdateValue > 0.0 )
                {
                    if( CurrentValue >= controllerTarget )
                    {
                        StopIncrease();
                        controllerTarget = null;
                    }
                }
                else
                {
                    if( CurrentValue <= controllerTarget )
                    {
                        StopDecrease();
                        controllerTarget = null;
                    }
                }
            }
        }

        private float UpdateValues(double elapsedSeconds, float direction)
        {
            //We increment the intermediate value first
            IntermediateValue += StepSize * (float)elapsedSeconds * GetNotchBoost() * direction;
            IntermediateValue = MathHelper.Clamp(IntermediateValue, MinimumValue, MaximumValue);

            //Do we have notches
            if (Notches.Count > 0)
            {
                //Increasing, check if the notch has changed
                if ((direction > 0) && (CurrentNotch < Notches.Count - 1) && (IntermediateValue >= Notches[CurrentNotch + 1].Value))
                {
                    // steamer_ctn - The following code was added in relation to reported bug  #1200226. However it seems to prevent the brake controller from ever being moved to EMERGENCY position.
                    // Bug conditions indicated in the bug report have not been able to be duplicated, ie there doesn't appear to be a "safety stop" when brake key(s) held down continuously
                    // Code has been reverted pending further investigation or reports of other issues
                    // Prevent TrainBrake to continuously switch to emergency
                    //      if (Notches[CurrentNotch + 1].Type == ControllerState.Emergency)
                    //         IntermediateValue = Notches[CurrentNotch + 1].Value - StepSize;
                    //      else
                    CurrentNotch++;
                }
                //decreasing, again check if the current notch has changed
                else if((direction < 0) && (CurrentNotch > 0) && (IntermediateValue < Notches[CurrentNotch].Value))
                {
                    CurrentNotch--;
                }

                //If the notch is smooth, we use intermediate value that is being update smooth thought the frames
                if (Notches[CurrentNotch].Smooth)
                    CurrentValue = IntermediateValue;
                else
                    CurrentValue = Notches[CurrentNotch].Value;
            }
            else
            {
                //if no notches, we just keep updating the current value directly
                CurrentValue = IntermediateValue;
            }
            return CurrentValue;
        }

        public float GetNotchFraction()
        {
            if (Notches.Count == 0)
                return 0;
            INotchController notch = Notches[CurrentNotch];
            if (!notch.Smooth)
                // Respect British 3-wire EP brake configurations
                return notch.NotchStateType == ControllerState.EPApply ? CurrentValue : 1;
            float x = 1;
            if (CurrentNotch + 1 < Notches.Count)
                x = Notches[CurrentNotch + 1].Value;
            x = (CurrentValue - notch.Value) / (x - notch.Value);
            if (notch.NotchStateType == ControllerState.Release)
                x = 1 - x;
            return x;
        }

        public float? SmoothMin()
        {
            float? target = null;
            if (Notches.Count > 0)
            {
                if (CurrentNotch > 0 && Notches[CurrentNotch - 1].Smooth)
                    target = Notches[CurrentNotch - 1].Value;
                else if (Notches[CurrentNotch].Smooth && CurrentValue > Notches[CurrentNotch].Value)
                    target = Notches[CurrentNotch].Value;
            }
            else
                target = MinimumValue;
            return target;
        }

        public float? SmoothMax()
        {
            float? target = null;
            if (Notches.Count > 0 && CurrentNotch < Notches.Count - 1 && Notches[CurrentNotch].Smooth)
                target = Notches[CurrentNotch + 1].Value;
            else if (Notches.Count == 0
                || (Notches.Count == 1 && Notches[CurrentNotch].Smooth))
                target = MaximumValue;
            return target;
        }

        public virtual string GetStatus()
        {
            if (Notches.Count == 0)
                return string.Format("{0:F0}%", 100 * CurrentValue);
            INotchController notch = Notches[CurrentNotch];
            //TODO translation via (static?) Catalog
            //also if NotchStateType == ControllerState.Running, need to use GetParticularString using ControllerState enum description attribute for context
            string name = EnumExtension.GetDescription(notch.NotchStateType);
            if (!notch.Smooth && notch.NotchStateType == ControllerState.Dummy)
                return string.Format("{0:F0}%", 100 * CurrentValue);
            if (!notch.Smooth)
                return name;
            if (!string.IsNullOrEmpty(name))
                return string.Format("{0} {1:F0}%", name, 100 * GetNotchFraction());
            return string.Format("{0:F0}%", 100 * GetNotchFraction());
        }

        public virtual void Save(BinaryWriter outf)
        {
            outf.Write((int)ControllerTypes.MSTSNotchController);

            this.SaveData(outf);
        }

        protected virtual void SaveData(BinaryWriter outf)
        {            
            outf.Write(CurrentValue);            
            outf.Write(MinimumValue);
            outf.Write(MaximumValue);
            outf.Write(StepSize);
            outf.Write(CurrentNotch);            
            outf.Write(Notches.Count);
            
            foreach(MSTSNotch notch in Notches)
            {
                notch.Save(outf);                
            }            
        }

        public virtual void Restore(BinaryReader inf)
        {
            Notches.Clear();

            IntermediateValue = CurrentValue = inf.ReadSingle();            
            MinimumValue = inf.ReadSingle();
            MaximumValue = inf.ReadSingle();
            StepSize = inf.ReadSingle();
            CurrentNotch = inf.ReadInt32();

            UpdateValue = 0;

            int count = inf.ReadInt32();

            for (int i = 0; i < count; ++i)
            {
                Notches.Add(new MSTSNotch(inf));
            }           
        }

        public INotchController GetCurrentNotch()
        {
            return Notches.Count == 0 ? null : Notches[CurrentNotch];
        }

        protected void SetCurrentNotch(ControllerState type)
        {
            for (int i = 0; i < Notches.Count; i++)
            {
                if (Notches[i].NotchStateType == type)
                {
                    CurrentNotch = i;
                    CurrentValue = Notches[i].Value;

                    break;
                }
            }
        }

        public void SetStepSize ( float stepSize)
        {
            StepSize = stepSize;
        }

        public void Normalize (float ratio)
        {
            for (int i = 0; i < Notches.Count; i++)
                Notches[i].Value /= ratio;
        }

    }
}
