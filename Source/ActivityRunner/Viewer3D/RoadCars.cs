// COPYRIGHT 2011, 2012, 2014 by the Open Rails project.
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
using System.Diagnostics;
using System.Linq;

using Microsoft.Xna.Framework;

using Orts.ActivityRunner.Viewer3D.Shapes;
using Orts.Common;
using Orts.Common.Position;
using Orts.Formats.Msts.Models;
using Orts.Simulation;

namespace Orts.ActivityRunner.Viewer3D
{
    // TODO: Move to simulator!
    public class RoadCarSpawner
    {
        public const float StopDistance = 10;
        private const float RampLength = 2;
        private const float TrackHalfWidth = 1;
        private const float TrackMergeDistance = 7; // Must be >= 2 * (RampLength + TrackHalfWidth).
        private const float TrackRailHeight = 0.275f;
        private const float TrainRailHeightMaximum = 1;
        private readonly Viewer Viewer;
        public readonly CarSpawnerObject CarSpawnerObj;

        // THREAD SAFETY:
        //   All accesses must be done in local variables. No modifications to the objects are allowed except by
        //   assignment of a new instance (possibly cloned and then modified).
        public List<RoadCar> Cars = new List<RoadCar>();
        // Level crossing which interact with this spawner. Distances are used for speed curves and the list must be sorted by distance from spawner.
        public List<Crossing> Crossings = new List<Crossing>();

        public readonly Traveller Traveller;
        public readonly float Length;
        private float LastSpawnedTime;
        private float NextSpawnTime;

        public RoadCarSpawner(Viewer viewer, in WorldPosition position, CarSpawnerObject carSpawnerObj)
        {
            Debug.Assert(TrackMergeDistance >= 2 * (RampLength + TrackHalfWidth), "TrackMergeDistance is less than 2 * (RampLength + TrackHalfWidth); vertical inconsistencies will occur at close, but not merged, tracks.");
            Viewer = viewer;
            CarSpawnerObj = carSpawnerObj;

            if (viewer.Simulator.RoadDatabase == null || viewer.Simulator.CarSpawnerLists == null)
                throw new InvalidOperationException("RoadCarSpawner requires a RDB and CARSPAWN.DAT");

            var start = CarSpawnerObj.TrackItemIds.RoadDbItems.Count > 0 ? CarSpawnerObj.TrackItemIds.RoadDbItems[0] : -1;
            var end = CarSpawnerObj.TrackItemIds.RoadDbItems.Count > 1 ? CarSpawnerObj.TrackItemIds.RoadDbItems[1] : -1;
            var trItems = viewer.Simulator.RoadDatabase.RoadTrackDB.TrItemTable;
            ref readonly WorldLocation startLocation = ref trItems[start].Location;
            ref readonly WorldLocation endLocation = ref trItems[end].Location;

            Traveller = new Traveller(viewer.Simulator.TSectionDat, viewer.Simulator.RoadDatabase.RoadTrackDB.TrackNodes, startLocation);
            Length = Traveller.DistanceTo(endLocation);
            if (Length < 0)
            {
                Traveller.ReverseDirection();
                Length = Traveller.DistanceTo(endLocation);
                if (Length < 0)
                    Trace.TraceWarning("{0} car spawner {1} doesn't have connected road route between {2} and {3}", position, carSpawnerObj.UiD, startLocation, endLocation);
            }

            var sortedLevelCrossings = new SortedList<float, Simulation.World.LevelCrossingItem>();
            for (var crossingTraveller = new Traveller(Traveller); crossingTraveller.NextSection(); )
                if (crossingTraveller.IsTrack && (crossingTraveller.TN as TrackVectorNode).TrackItemIndices != null)
                    foreach (var trItemRef in (crossingTraveller.TN as TrackVectorNode).TrackItemIndices)
                        if (Viewer.Simulator.LevelCrossings.RoadCrossingItems.ContainsKey(trItemRef))
                            sortedLevelCrossings[Viewer.Simulator.LevelCrossings.RoadCrossingItems[trItemRef].DistanceTo(Traveller)] = Viewer.Simulator.LevelCrossings.RoadCrossingItems[trItemRef];

            Crossings = sortedLevelCrossings.Select(slc => new Crossing(slc.Value, slc.Key, float.NaN)).ToList();
        }

        public void Update(in ElapsedTime elapsedTime)
        {
            var cars = Cars;
            foreach (var car in cars)
                car.Update(elapsedTime);

            LastSpawnedTime += (float)elapsedTime.ClockSeconds;
            if (Length > 0 && LastSpawnedTime >= NextSpawnTime && (cars.Count == 0 || cars.Last().Travelled > cars.Last().Length))
            {
                var newCars = new List<RoadCar>(cars);
                newCars.Add(new RoadCar(Viewer, this, CarSpawnerObj.CarAverageSpeed, CarSpawnerObj.CarSpawnerListIndex));
                Cars = cars = newCars;

                LastSpawnedTime = 0;
                NextSpawnTime = CarSpawnerObj.CarFrequency * (0.75f + (float)Viewer.Random.NextDouble() / 2);
            }

            if (cars.Any(car => car.Travelled > Length))
                Cars = cars = cars.Where(car => car.Travelled <= Length).ToList();

            var crossings = Crossings;
            if (crossings.Any(c => float.IsNaN(c.TrackHeight)))
            {
                Crossings = crossings.Select(c =>
                {
                    if (!float.IsNaN(c.TrackHeight) || !Viewer.Simulator.LevelCrossings.RoadToTrackCrossingItems.ContainsKey(c.Item))
                        return c;
                    var height = Viewer.Simulator.LevelCrossings.RoadToTrackCrossingItems[c.Item].Location.Location.Y + TrackRailHeight - c.Item.Location.Location.Y;
                    return new Crossing(c.Item, c.Distance, height <= TrainRailHeightMaximum ? height : 0);
                }).ToList();
            }
        }

        internal float GetRoadHeightAdjust(float distance)
        {
            var crossings = Crossings;
            for (var i = 0; i < crossings.Count; i++)
            {
                // Crossing is too far down the path, we can quit.
                if (distance <= crossings[i].DistanceAdjust1)
                    break;
                if (!float.IsNaN(crossings[i].TrackHeight))
                {
                    // Location is approaching a track.
                    if (crossings[i].DistanceAdjust1 <= distance && distance <= crossings[i].DistanceAdjust2)
                        return MathHelper.Lerp(0, crossings[i].TrackHeight, (distance - crossings[i].DistanceAdjust1) / RampLength);
                    // Location is crossing a track.
                    if (crossings[i].DistanceAdjust2 <= distance && distance <= crossings[i].DistanceAdjust3)
                        return crossings[i].TrackHeight;
                    // Crossings are close enough to count as joined.
                    if (i + 1 < crossings.Count && !float.IsNaN(crossings[i + 1].TrackHeight) && crossings[i + 1].Distance - crossings[i].Distance < TrackMergeDistance)
                    {
                        // Location is between two crossing tracks.
                        if (crossings[i].DistanceAdjust3 <= distance && distance <= crossings[i + 1].DistanceAdjust2)
                            return MathHelper.Lerp(crossings[i].TrackHeight, crossings[i + 1].TrackHeight, (distance - crossings[i].DistanceAdjust3) / (crossings[i + 1].DistanceAdjust2 - crossings[i].DistanceAdjust3));
                    }
                    else
                    {
                        // Location is passing a track.
                        if (crossings[i].DistanceAdjust3 <= distance && distance <= crossings[i].DistanceAdjust4)
                            return MathHelper.Lerp(crossings[i].TrackHeight, 0, (distance - crossings[i].DistanceAdjust3) / RampLength);
                    }
                }
            }
            return 0;
        }

        public class Crossing
        {
            public readonly Simulation.World.LevelCrossingItem Item;
            public readonly float Distance;
            public readonly float DistanceAdjust1;
            public readonly float DistanceAdjust2;
            public readonly float DistanceAdjust3;
            public readonly float DistanceAdjust4;
            public readonly float TrackHeight;
            internal Crossing(Simulation.World.LevelCrossingItem item, float distance, float trackHeight)
            {
                Item = item;
                Distance = distance;
                DistanceAdjust1 = distance - RoadCarSpawner.TrackHalfWidth - RoadCarSpawner.RampLength;
                DistanceAdjust2 = distance - RoadCarSpawner.TrackHalfWidth;
                DistanceAdjust3 = distance + RoadCarSpawner.TrackHalfWidth;
                DistanceAdjust4 = distance + RoadCarSpawner.TrackHalfWidth + RoadCarSpawner.RampLength;
                TrackHeight = trackHeight;
            }
        }
    }

    // TODO: Move to simulator!
    public class RoadCar : IWorldPosition
    {
        public const float VisualHeightAdjustment = 0.1f;
        private const float AccelerationFactor = 5;
        private const float BrakingFactor = 5;
        private const float BrakingMinFactor = 1;

        public readonly RoadCarSpawner Spawner;

        public readonly int Type;
        public readonly float Length;
        public float Travelled;
        public readonly bool IgnoreXRotation;
        public bool CarriesCamera;

        public int TileX { get { return FrontTraveller.TileX; } }
        public int TileZ { get { return FrontTraveller.TileZ; } }

        private WorldPosition position;

        public Vector3 FrontLocation
        {
            get
            {
                return new Vector3(FrontTraveller.WorldLocation.Location.X,
                    FrontTraveller.WorldLocation.Location.Y + Math.Max(Spawner.GetRoadHeightAdjust(Travelled - Length * 0.25f), 0) + VisualHeightAdjustment,
                    FrontTraveller.WorldLocation.Location.Z);
            }
        }
        public Vector3 RearLocation
        {
            get
            {
                WorldLocation location = RearTraveller.WorldLocation.NormalizeTo(TileX, TileZ);
                return new Vector3(location.Location.X,
                    location.Location.Y + Math.Max(Spawner.GetRoadHeightAdjust(Travelled + Length * 0.25f), 0) + VisualHeightAdjustment,
                    location.Location.Z);
            }
        }

        public ref readonly WorldPosition WorldPosition
        {
            get
            {
                // TODO: Add 0.1f to Y to put wheels above road. Matching MSTS?
                var front = FrontLocation;
                var rear = RearLocation;
                var frontY = front.Y;
                var rearY = rear.Y;
                if (IgnoreXRotation)
                {
                    frontY = frontY - VisualHeightAdjustment;
                    rearY = rearY - VisualHeightAdjustment;
                    if (Math.Abs(frontY - rearY) > 0.01f)
                    {
                        if (frontY > rearY) rearY = frontY;
                        else frontY = rearY;
                    }
                }
                position = new WorldPosition(TileX, TileZ, Simulator.XNAMatrixFromMSTSCoordinates(front.X, frontY, front.Z, rear.X, rearY, rear.Z));
                return ref position;
            }
        }

        public readonly Traveller FrontTraveller;
        public readonly Traveller RearTraveller;
        public float Speed;
        private float SpeedMax;
        private int NextCrossingIndex;
        public int CarSpawnerListIdx;

        public RoadCar(Viewer viewer, RoadCarSpawner spawner, float averageSpeed, int carSpawnerListIdx)
        {
            Spawner = spawner;
            CarSpawnerListIdx = carSpawnerListIdx;
            Type = Viewer.Random.Next() % viewer.Simulator.CarSpawnerLists[CarSpawnerListIdx].Count;
            Length = viewer.Simulator.CarSpawnerLists[CarSpawnerListIdx][Type].Distance;
            // Front and rear travellers approximate wheel positions at 25% and 75% along vehicle.
            FrontTraveller = new Traveller(spawner.Traveller);
            FrontTraveller.Move(Length * 0.15f);
            RearTraveller = new Traveller(spawner.Traveller);
            RearTraveller.Move(Length * 0.85f);
            // Travelled is the center of the vehicle.
            Travelled = Length * 0.50f;
            Speed = SpeedMax = averageSpeed * (0.75f + (float)Viewer.Random.NextDouble() / 2);
            IgnoreXRotation = viewer.Simulator.CarSpawnerLists[CarSpawnerListIdx].IgnoreXRotation;
        }

        public void Update(in ElapsedTime elapsedTime)
        {
            var crossings = Spawner.Crossings;

            // We skip any crossing that we have passed (Travelled + Length / 2) or are too close to stop at (+ Speed * BrakingMinFactor).
            // We skip any crossing that is part of the same group as the previous.
            while (NextCrossingIndex < crossings.Count
                && ((Travelled + Length / 2 + Speed * BrakingMinFactor > crossings[NextCrossingIndex].Distance)
                || (NextCrossingIndex > 0 && crossings[NextCrossingIndex].Item.CrossingGroup != null && crossings[NextCrossingIndex].Item.CrossingGroup == crossings[NextCrossingIndex - 1].Item.CrossingGroup)))
            {
                NextCrossingIndex++;
            }

            // Calculate all the distances to items we need to stop at (level crossings, other cars).
            var stopDistances = new List<float>();
            for (var crossing = NextCrossingIndex; crossing < crossings.Count; crossing++)
            {
                if (crossings[crossing].Item.CrossingGroup != null && crossings[crossing].Item.CrossingGroup.HasTrain)
                {
                    // TODO: Stopping distance for level crossings!
                    stopDistances.Add(crossings[crossing].Distance - RoadCarSpawner.StopDistance);
                    break;
                }
            }
            // TODO: Maybe optimise this?
            var cars = Spawner.Cars;
            var spawnerIndex = cars.IndexOf(this);
            if (spawnerIndex > 0)
            {
                if (!cars[spawnerIndex - 1].CarriesCamera)
                    stopDistances.Add(cars[spawnerIndex - 1].Travelled - cars[spawnerIndex - 1].Length / 2);
                else
                    stopDistances.Add(cars[spawnerIndex - 1].Travelled - cars[spawnerIndex - 1].Length * 0.65f - 4 - cars[spawnerIndex - 1].Speed * 0.5f);
                }

            // Calculate whether we're too close to the minimum stopping distance (and need to slow down) or going too slowly (and need to speed up).
            var stopDistance = stopDistances.Count > 0 ? stopDistances.Min() - Travelled - Length / 2 : float.MaxValue;
            var slowingDistance = BrakingFactor * Length;
            if (stopDistance < slowingDistance)
                Speed = SpeedMax * (float)Math.Sin((Math.PI / 2) * (stopDistance / slowingDistance));
            else if (Speed < SpeedMax)
                Speed = (float)Math.Min(Speed + AccelerationFactor / Length * elapsedTime.ClockSeconds, SpeedMax);
            else if (Speed > SpeedMax)
                Speed = (float)Math.Max(Speed - AccelerationFactor / Length * elapsedTime.ClockSeconds * 2, SpeedMax);

            var distance = elapsedTime.ClockSeconds * Speed;
            Travelled += (float)distance;
            FrontTraveller.Move(distance);
            RearTraveller.Move(distance);
        }

        public void ChangeSpeed (float speed)
        {
            if (speed > 0)
            {
                if (SpeedMax < Spawner.CarSpawnerObj.CarAverageSpeed * 1.25f) SpeedMax = Math.Min(SpeedMax + speed * 2, Spawner.CarSpawnerObj.CarAverageSpeed * 1.25f);
            }
            else if (speed < 0)
            {
                if (SpeedMax > Spawner.CarSpawnerObj.CarAverageSpeed * 0.25f) SpeedMax = Math.Max(SpeedMax + speed * 2, Spawner.CarSpawnerObj.CarAverageSpeed * 0.25f);
            }
        }
    }

    public class RoadCarViewer
    {
        private readonly Viewer Viewer;

        // THREAD SAFETY:
        //   All accesses must be done in local variables. No modifications to the objects are allowed except by
        //   assignment of a new instance (possibly cloned and then modified).
        private Dictionary<RoadCar, RoadCarPrimitive> Cars = new Dictionary<RoadCar, RoadCarPrimitive>();
        public List<RoadCar> VisibleCars = new List<RoadCar>();

        public RoadCarViewer(Viewer viewer)
        {
            Viewer = viewer;
        }

        public void Load()
        {
            var cancellation = Viewer.LoaderProcess.CancellationToken;
            var visibleCars = VisibleCars;
            var cars = Cars;
            if (visibleCars.Any(c => !cars.ContainsKey(c)) || cars.Keys.Any(c => !visibleCars.Contains(c)))
            {
                var newCars = new Dictionary<RoadCar, RoadCarPrimitive>();
                foreach (var car in visibleCars)
                {
                    if (cancellation.IsCancellationRequested)
                        break;
                    if (cars.ContainsKey(car))
                        newCars.Add(car, cars[car]);
                    else
                        newCars.Add(car, LoadCar(car));
                }
                Cars = newCars;
            }
        }

        public void LoadPrep()
        {
            // TODO: Maybe optimise this with some serial numbers?
            var visibleCars = VisibleCars;
            var newVisibleCars = new List<RoadCar>(visibleCars.Count);
            foreach (var tile in Viewer.World.Scenery.WorldFiles)
                foreach (var spawner in tile.carSpawners)
                    newVisibleCars.AddRange(spawner.Cars);
            VisibleCars = newVisibleCars;
        }

        public void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            foreach (var car in Cars.Values)
                car.PrepareFrame(frame, elapsedTime);
        }

        private RoadCarPrimitive LoadCar(RoadCar car)
        {
            return new RoadCarPrimitive(Viewer, car);
        }

        internal void Mark()
        {
            var cars = Cars;
            foreach (var car in cars.Values)
                car.Mark();
        }
    }

    public class RoadCarPrimitive
    {
        private readonly RoadCar Car;
        private readonly RoadCarShape CarShape;

        public RoadCarPrimitive(Viewer viewer, RoadCar car)
        {
            Car = car;
            CarShape = new RoadCarShape(viewer.Simulator.CarSpawnerLists[Car.CarSpawnerListIdx][car.Type].Name, car);
        }

        public void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            //// TODO: Add 0.1f to Y to put wheels above road. Matching MSTS?
            //var front = Car.FrontLocation;
            //var rear = Car.RearLocation;
            //var frontY = front.Y;
            //var rearY = rear.Y;
            //if (Car.IgnoreXRotation)
            //{
            //    frontY = frontY - RoadCar.VisualHeightAdjustment;
            //    rearY = rearY - RoadCar.VisualHeightAdjustment;
            //    if (Math.Abs(frontY - rearY) > 0.01f)
            //    {
            //        if (frontY > rearY) rearY = frontY;
            //        else frontY = rearY;
            //    }
            //}
            //CarShape.Location = new WorldPosition(Car.TileX, Car.TileZ, Simulator.XNAMatrixFromMSTSCoordinates(front.X, frontY, front.Z, rear.X, rearY, rear.Z));
            CarShape.PrepareFrame(frame, elapsedTime);
        }

        internal void Mark()
        {
            CarShape.Mark();
        }
    }
}
