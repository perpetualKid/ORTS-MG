using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orts.Scripting.Api
{
    /// <summary>
    /// Base class for Timer and OdoMeter. Not to be used directly.
    /// </summary>
    public abstract class Counter
    {
        private double endValue;
        protected Func<double> CurrentValue;

        public double AlarmValue { get; private set; }
        public double RemainingValue { get { return endValue - CurrentValue(); } }
        public bool Started { get; private set; }
        public void Setup(float alarmValue) { AlarmValue = alarmValue; }
        public void Start() { endValue = CurrentValue() + AlarmValue; Started = true; }
        public void Stop() { Started = false; }
        public bool Triggered { get { return Started && CurrentValue() >= endValue; } }
    }

    public class Timer : Counter
    {
        public Timer(ScriptBase script)
        {
            CurrentValue = script.GameTime;
        }
    }

    public class Odometer : Counter
    {
        public Odometer(ScriptBase script)
        {
            CurrentValue = script.DistanceM;
        }
    }
}
