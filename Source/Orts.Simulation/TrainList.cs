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

using System.Collections.Generic;

using Orts.Simulation.AIs;
using Orts.Simulation.Physics;
using Orts.Simulation.Timetables;

namespace Orts.Simulation
{

    /// <summary>
    /// Class TrainList extends class List<Train> with extra search methods
    /// </summary>
    public class TrainList : List<Train>
    {
        private readonly Simulator simulator;

        /// <summary>
        /// basis constructor
        /// </summary>

        public TrainList(Simulator simulator)
            : base()
        {
            this.simulator = simulator;
        }

        /// <summary>
        /// Search and return TRAIN by number - any type
        /// </summary>
        public Train GetTrainByNumber(int number)
        {
            if (simulator.TrainDictionary.TryGetValue(number, out Train result))
            {
                return result;
            }

            // check player train's original number
            if (simulator.TimetableMode && simulator.PlayerLocomotive != null)
            {
                if ((simulator.PlayerLocomotive.Train as TTTrain)?.OrgAINumber == number)
                {
                    return simulator.PlayerLocomotive.Train;
                }
            }

            // dictionary is not always updated in normal activity and explorer mode, so double check
            // if not correct, search in the 'old' way
            foreach(Train train in this)
            {
                if (train.Number == number)
                    return train;

            }
            return null;
        }

        /// <summary>
        /// Search and return Train by name - any type
        /// </summary>
        public Train GetTrainByName(string name)
        {
            simulator.NameDictionary.TryGetValue(name, out Train train);
            return train;
        }

        /// <summary>
        /// Check if numbered train is on startlist
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        public bool CheckTrainNotStartedByNumber(int number)
        {
            return simulator.StartReference.Contains(number);
        }

        /// <summary>
        /// Search and return AITrain by number
        /// </summary>
        public AITrain GetAITrainByNumber(int number)
        {
            simulator.TrainDictionary.TryGetValue(number, out Train aiTrain);
            return aiTrain as AITrain;
        }

        /// <summary>
        /// Search and return AITrain by name
        /// </summary>
        public AITrain GetAITrainByName(string name)
        {
            simulator.NameDictionary.TryGetValue(name, out Train aiTrain);
            return aiTrain as AITrain;
        }

    } // TrainList
}
