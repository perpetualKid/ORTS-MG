using System;

using Orts.Common;

namespace Orts.Scripting.Api.PowerSupply
{
    /// <summary>
    /// Circuit breaker for electric locomotives
    /// </summary>
    public abstract class CircuitBreaker : ScriptBase
    {
        /// <summary>
        /// Current state of the circuit breaker
        /// </summary>
        public Func<CircuitBreakerState> CurrentState;
        /// <summary>
        /// Current state of the pantograph
        /// </summary>
        public Func<PantographState> CurrentPantographState;
        /// <summary>
        /// Current state of the power supply
        /// </summary>
        public Func<PowerSupplyState> CurrentPowerSupplyState;
        /// <summary>
        /// Driver's circuit breaker closing order
        /// </summary>
        public Func<bool> DriverClosingOrder;
        /// <summary>
        /// Driver's circuit breaker closing authorization
        /// </summary>
        public Func<bool> DriverClosingAuthorization;
        /// <summary>
        /// Driver's circuit breaker opening order
        /// </summary>
        public Func<bool> DriverOpeningOrder;
        /// <summary>
        /// TCS' circuit breaker closing order
        /// </summary>
        public Func<bool> TCSClosingOrder;
        /// <summary>
        /// TCS' circuit breaker closing authorization
        /// </summary>
        public Func<bool> TCSClosingAuthorization;
        /// <summary>
        /// TCS' circuit breaker opening order
        /// </summary>
        public Func<bool> TCSOpeningOrder;
        /// <summary>
        /// Circuit breaker closing authorization
        /// </summary>
        public Func<bool> ClosingAuthorization;
        /// <summary>
        /// Delay before circuit breaker closing
        /// </summary>
        public Func<float> ClosingDelayS;

        /// <summary>
        /// Sets the current state of the circuit breaker
        /// </summary>
        public Action<CircuitBreakerState> SetCurrentState;
        /// <summary>
        /// Sets the driver's circuit breaker closing order
        /// </summary>
        public Action<bool> SetDriverClosingOrder;
        /// <summary>
        /// Sets the driver's circuit breaker closing authorization
        /// </summary>
        public Action<bool> SetDriverClosingAuthorization;
        /// <summary>
        /// Sets the driver's circuit breaker opening order
        /// </summary>
        public Action<bool> SetDriverOpeningOrder;
        /// <summary>
        /// Sets the circuit breaker closing authorization
        /// </summary>
        public Action<bool> SetClosingAuthorization;

        /// <summary>
        /// Called once at initialization time.
        /// </summary>
        public abstract void Initialize();
        /// <summary>
        /// Called regularly at every simulator update cycle.
        /// </summary>
        public abstract void Update(double elapsedClockSeconds);
        /// <summary>
        /// Called when an event happens (a closing order from the driver for example)
        /// </summary>
        /// <param name="evt">The event happened</param>
        public abstract void HandleEvent(PowerSupplyEvent powerSupplyEvent);
    }
}
