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

using System.Collections.Generic;
using System.Linq;

using Orts.Common.Position;
using Orts.Formats.Msts.Files;
using Orts.Formats.Msts.Models;

namespace Orts.Simulation
{
    public class HazardManager
	{
        private readonly int hornDist = 200;
        private readonly int approachDist = 160;
        private readonly int scaredDist = 147;
        private readonly Simulator Simulator;
		public readonly Dictionary<int, Hazard> Hazards;
		public readonly Dictionary<int, Hazard> CurrentHazards;
		public readonly Dictionary<string, HazardFile> HazFiles;
        private List<int> InterestedHazards;//those hazards is closed to player, needs to listen to horn
		public HazardManager(Simulator simulator)
		{
			Simulator = simulator;
			InterestedHazards = new List<int>();
			CurrentHazards = new Dictionary<int, Hazard>();
			HazFiles = new Dictionary<string, HazardFile>();
			Hazards = simulator.TDB != null && simulator.TDB.TrackDB != null ? GetHazardsFromDB(simulator.TDB.TrackDB.TrackNodes, simulator.TDB.TrackDB.TrackItems) : new Dictionary<int, Hazard>();
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

		public Hazard AddHazzardIntoGame(int itemID, string hazFileName)
		{
			try
			{
				if (!CurrentHazards.ContainsKey(itemID))
				{
					if (HazFiles.ContainsKey(hazFileName)) Hazards[itemID].HazFile = HazFiles[hazFileName];
					else
					{
						var hazF = new HazardFile(Simulator.RoutePath + "\\" + hazFileName);
						HazFiles.Add(hazFileName, hazF);
						Hazards[itemID].HazFile = hazF;
					}
					//based on act setting for frequency
                    if (Hazards[itemID].animal == true && Simulator.Activity != null)
                    {
                        if (Simulator.Random.Next(100) > Simulator.Activity.Activity.Header.Animals) return null;
                    }
					else if (Simulator.Activity != null)
					{
						if (Simulator.Random.Next(100) > Simulator.Activity.Activity.Header.Animals) return null;
					}
					else //in explore mode
					{
						if (Hazards[itemID].animal == false) return null;//not show worker in explore mode
						if (Simulator.Random.Next(100) > 20) return null;//show 10% animals
					}
					CurrentHazards.Add(itemID, Hazards[itemID]);
					return Hazards[itemID];//successfully added the hazard with associated haz file
				}
			}
			catch { }
			return null;
		}

		public void RemoveHazzardFromGame(int itemID)
		{
			try
			{
				if (CurrentHazards.ContainsKey(itemID))
				{
					CurrentHazards.Remove(itemID);
				}
			}
			catch { };
		}

		public void Update(double elapsedClockSeconds)
		{
			var playerLocation = Simulator.PlayerLocomotive.WorldPosition.WorldLocation;

			foreach (var haz in Hazards)
			{
				haz.Value.Update(playerLocation, approachDist, scaredDist);
			}
		}

		public void Horn()
		{
			var playerLocation = Simulator.PlayerLocomotive.WorldPosition.WorldLocation;
			foreach (var haz in Hazards)
			{
				if (WorldLocation.Within(haz.Value.Location, playerLocation, hornDist))
				{
					haz.Value.state = Hazard.State.LookLeft;
				}
			}
		}
	}

	public class Hazard

	{
        private readonly TrackNode TrackNode;

        internal WorldLocation Location;
		public HazardFile HazFile { get { return hazF; } set { hazF = value; if (hazF.Hazard.Workers != null) animal = false; else animal = true; } }
		public HazardFile hazF;
		public enum State { Idle1, Idle2, LookLeft, LookRight, Scared };
		public State state;
		public bool animal = true;

		public Hazard(TrackNode trackNode, TrackItem trItem)
        {
            TrackNode = trackNode;
            Location = trItem.Location;
			state = State.Idle1;
        }

		public void Update(in WorldLocation playerLocation, int approachDist, int scaredDist)
		{
			if (state == State.Idle1)
			{
				if (Simulator.Random.Next(10) == 0) state = State.Idle2;
			}
			else if (state == State.Idle2)
			{
				if (Simulator.Random.Next(5) == 0) state = State.Idle1;
			}

            if (!WorldLocation.Within(Location, playerLocation, scaredDist) && state < State.LookLeft)
            {
                if (WorldLocation.Within(Location, playerLocation, approachDist) && state < State.LookLeft)
                {
                    state = State.LookRight;
                }
            }
            if (WorldLocation.Within(Location, playerLocation, scaredDist) && state == State.LookRight || state == State.LookLeft)
            {
                state = State.Scared;
            }
       }
	}
}
