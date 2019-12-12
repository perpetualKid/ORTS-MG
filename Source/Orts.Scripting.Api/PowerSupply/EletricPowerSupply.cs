using System;

using Orts.Common;

namespace Orts.Scripting.Api.PowerSupply
{
    /// <summary>
    /// Power supply for electric locomotives
    /// </summary>
    public abstract class ElectricPowerSupply : PowerSupply
    {
        /// <summary>
        /// Current state of the pantograph
        /// </summary>
        public Func<PantographState> CurrentPantographState;
        /// <summary>
        /// Current state of the circuit breaker
        /// </summary>
        public Func<CircuitBreakerState> CurrentCircuitBreakerState;
        /// <summary>
        /// Voltage of the pantograph
        /// </summary>
        public Func<float> PantographVoltageV;
        /// <summary>
        /// Voltage of the filter
        /// </summary>
        public Func<float> FilterVoltageV;
        /// <summary>
        /// Line voltage
        /// </summary>
        public Func<float> LineVoltageV;

        /// <summary>
        /// Sets the voltage of the pantograph
        /// </summary>
        public Action<float> SetPantographVoltageV;
        /// <summary>
        /// Sets the voltage of the filter
        /// </summary>
        public Action<float> SetFilterVoltageV;
    }
}
