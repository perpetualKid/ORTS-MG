using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orts.Common;

namespace Orts.Scripting.Api
{
    public abstract class ScriptBase
    {
        /// <summary>
        /// Clock value (in seconds) for the simulation. Starts with a value = session start time.
        /// </summary>
        public Func<double> ClockTime;
        /// <summary>
        /// Clock value (in seconds) for the simulation. Starts with a value = 0.
        /// </summary>
        public Func<double> GameTime;
        /// <summary>
        /// Running total of distance travelled - always positive, updated by train physics.
        /// </summary>
        public Func<double> DistanceM;
        /// <summary>
        /// Confirms a command done by the player with a pre-set message on the screen.
        /// </summary>
        public Action<CabControl, CabSetting> Confirm;
        /// <summary>
        /// Displays a message on the screen.
        /// </summary>
        public Action<ConfirmLevel, string> Message;
        /// <summary>
        /// Sends an event to the locomotive.
        /// </summary>
        public Action<TrainEvent> SignalEvent;
        /// <summary>
        /// Sends an event to the train.
        /// </summary>
        public Action<TrainEvent> SignalEventToTrain;
    }
}
