using System;
using Orts.Common;

namespace Orts.Scripting.Api.PowerSupply
{
    public abstract class PowerSupply : ScriptBase
    {
        /// <summary>
        /// Current state of the power supply
        /// </summary>
        public Func<PowerSupplyState> CurrentState;
        
        /// <summary>
        /// Current state of the auxiliary power supply
        /// </summary>
        public Func<PowerSupplyState> CurrentAuxiliaryState;
        
        /// <summary>
        /// Main supply power on delay
        /// </summary>
        public Func<float> PowerOnDelayS;
        
        /// <summary>
        /// Auxiliary supply power on delay
        /// </summary>
        public Func<float> AuxPowerOnDelayS;

        /// <summary>
        /// Sets the current state of the power supply
        /// </summary>
        public Action<PowerSupplyState> SetCurrentState;
        
        /// <summary>
        /// Sets the current state of the auxiliary power supply
        /// </summary>
        public Action<PowerSupplyState> SetCurrentAuxiliaryState;

        /// <summary>
        /// Called once at initialization time.
        /// </summary>
        public abstract void Initialize();

        /// <summary>
        /// Called regularly at every simulator update cycle.
        /// </summary>
        public abstract void Update(double elapsedClockSeconds);
    }
}
