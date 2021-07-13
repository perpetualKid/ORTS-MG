// COPYRIGHT 2012, 2013 by the Open Rails project.
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
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

using Orts.Common.Position;
using Orts.Formats.Msts.Files;
using Orts.Formats.Msts.Models;

namespace Orts.Simulation.World
{
    public class HazardManager
    {
        private readonly int hornDist = 200;
        private readonly int approachDist = 160;
        private readonly int scaredDist = 147;
        private readonly Dictionary<int, Hazard> hazards;
        private readonly Dictionary<int, Hazard> currentHazards;
        private readonly Dictionary<string, HazardFile> hazardFiles;

        public HazardManager(Simulator simulator)
        {
            if (null == simulator)
                throw new ArgumentNullException(nameof(simulator));

            currentHazards = new Dictionary<int, Hazard>();
            hazardFiles = new Dictionary<string, HazardFile>();
            hazards = simulator.TrackDatabase != null && simulator.TrackDatabase.TrackDB != null ? GetHazardsFromDB(simulator.TrackDatabase.TrackDB.TrackNodes, simulator.TrackDatabase.TrackDB.TrackItems) : new Dictionary<int, Hazard>();
        }

        private static Dictionary<int, Hazard> GetHazardsFromDB(TrackNode[] trackNodes, TrackItem[] trItemTable)
        {
            return (from trackNode in trackNodes
                    where trackNode is TrackVectorNode tvn && tvn.TrackItemIndices.Length > 0
                    from itemRef in (trackNode as TrackVectorNode)?.TrackItemIndices.Distinct()
                    where trItemTable[itemRef] != null && trItemTable[itemRef] is HazardItem
                    select new KeyValuePair<int, Hazard>(itemRef, new Hazard(trackNode, trItemTable[itemRef])))
                    .ToDictionary(_ => _.Key, _ => _.Value);
        }

        public Hazard AddHazardIntoGame(int itemID, string hazFileName)
        {
            if (!currentHazards.ContainsKey(itemID))
            {
                if (!hazardFiles.TryGetValue(hazFileName, out HazardFile hazardFile))
                {
                    hazardFile = new HazardFile(Simulator.Instance.RouteFolder.HazardFile(hazFileName));
                    hazardFiles.Add(hazFileName, hazardFile);
                }
                hazards[itemID].HazFile = hazardFile;
                //based on act setting for frequency
                if (Simulator.Instance.ActivityFile != null)
                {
                    if (hazards[itemID].Animal && (RandomNumberGenerator.GetInt32(100) > Simulator.Instance.ActivityFile.Activity.Header.Animals))
                        return null;
                }
                else //in explore mode
                {
                    if (!hazards[itemID].Animal)
                        return null;//not show worker in explore mode
                    if (RandomNumberGenerator.GetInt32(100) > 20)
                        return null;//show 10% animals
                }
                currentHazards.Add(itemID, hazards[itemID]);
                return hazards[itemID];//successfully added the hazard with associated haz file
            }
            return null;
        }

        public void RemoveHazardFromGame(int itemID)
        {
            if (currentHazards.ContainsKey(itemID))
                currentHazards.Remove(itemID);
        }

        public void Update(double elapsedClockSeconds)
        {
            _ = elapsedClockSeconds;
            WorldLocation playerLocation = Simulator.Instance.PlayerLocomotive.WorldPosition.WorldLocation;

            foreach (KeyValuePair<int, Hazard> item in hazards)
                item.Value.Update(playerLocation, approachDist, scaredDist);
        }

        public void Horn()
        {
            WorldLocation playerLocation = Simulator.Instance.PlayerLocomotive.WorldPosition.WorldLocation;
            foreach (KeyValuePair<int, Hazard> item in hazards)
                if (WorldLocation.Within(item.Value.Location, playerLocation, hornDist))
                    item.Value.State = Hazard.HazardState.LookLeft;
        }
    }

    public class Hazard
    {
        public enum HazardState
        {
            Idle1,
            Idle2,
            LookLeft,
            LookRight,
            Scared
        };

        //private readonly TrackNode TrackNode;
        private HazardFile hazardFile;

        internal WorldLocation Location;
        public HazardFile HazFile
        {
            get => hazardFile;
            set
            {
                hazardFile = value;
                Animal = hazardFile.Hazard.Workers != null;
            }
        }

        public HazardState State { get; set; }
        internal bool Animal { get; private set; } = true;

        public Hazard(TrackNode trackNode, TrackItem trItem)
        {
            _ = trackNode;
            //TrackNode = trackNode;
            Location = trItem?.Location ?? throw new ArgumentNullException(nameof(trItem));
            State = HazardState.Idle1;
        }

        public void Update(in WorldLocation playerLocation, int approachDist, int scaredDist)
        {
            if (State == HazardState.Idle1 && RandomNumberGenerator.GetInt32(10) == 0)
                State = HazardState.Idle2;
            else if (State == HazardState.Idle2 && RandomNumberGenerator.GetInt32(5) == 0)
                    State = HazardState.Idle1;

            bool within = WorldLocation.Within(Location, playerLocation, scaredDist);
            if (!within && State < HazardState.LookLeft)
                if (WorldLocation.Within(Location, playerLocation, approachDist) && State < HazardState.LookLeft)
                    State = HazardState.LookRight;
            if (within && State == HazardState.LookRight || State == HazardState.LookLeft)
                State = HazardState.Scared;
        }
    }
}
