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

using Orts.Simulation.RollingStocks;

namespace Orts.Simulation.World
{
    //public class FuelManager
    //{
    //    private readonly Simulator Simulator;
    //    public readonly Dictionary<int, FuelPickupItem> FuelPickupItems;

    //    public FuelManager(Simulator simulator)
    //    {
    //        Simulator = simulator;
    //        FuelPickupItems = simulator.TDB != null && simulator.TDB.TrackDB != null ? GetFuelPickupItemsFromDB(simulator.TDB.TrackDB.TrackNodes, simulator.TDB.TrackDB.TrackItems) : new Dictionary<int, FuelPickupItem>();
    //    }

    //    private static Dictionary<int, FuelPickupItem> GetFuelPickupItemsFromDB(TrackNode[] trackNodes, TrackItem[] trItemTable)
    //    {
    //        return (from trackNode in trackNodes
    //                where trackNode is TrackVectorNode tvn && tvn.TrackItemIndices.Length > 0
    //                from itemRef in (trackNode as TrackVectorNode)?.TrackItemIndices.Distinct()
    //                where trItemTable[itemRef] != null && trItemTable[itemRef] is PickupItem
    //                select new KeyValuePair<int, FuelPickupItem>(itemRef, new FuelPickupItem(trackNode, trItemTable[itemRef])))
    //                .ToDictionary(_ => _.Key, _ => _.Value);
    //    }

    //    public FuelPickupItem CreateFuelStation(in WorldPosition position, IEnumerable<int> trackIDs)
    //    {
    //        var trackItems = trackIDs.Select(id => FuelPickupItems[id]).ToArray();
    //        return new FuelPickupItem(trackItems);
    //    }
    //}

    //public class FuelPickupItem
    //{
    //    internal WorldLocation Location;
    //    private readonly TrackNode TrackNode;

    //    public FuelPickupItem(TrackNode trackNode, TrackItem trItem)
    //    {
    //        TrackNode = trackNode;
    //        Location = trItem.Location;
    //    }

    //    public FuelPickupItem(IEnumerable<FuelPickupItem> items) { }

    //    public bool ReFill()
    //    {
    //        while (MSTSWagon.RefillProcess.OkToRefill)
    //        {
    //            return true;
    //        }
    //        if (!MSTSWagon.RefillProcess.OkToRefill)
    //            return false;
    //        return false;
    //    }
    //}
    public static class FuelPickupItem
    {
        public static bool ReFill()
        {
            while (MSTSWagon.RefillProcess.OkToRefill)
                return true;
            if (!MSTSWagon.RefillProcess.OkToRefill)
                return false;
            return false;
        }
    }
}
