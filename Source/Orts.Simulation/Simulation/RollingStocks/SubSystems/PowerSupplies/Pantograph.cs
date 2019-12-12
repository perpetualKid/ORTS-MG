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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Orts.Common;
using Orts.Formats.Msts.Parsers;

namespace Orts.Simulation.RollingStocks.SubSystems.PowerSupplies
{
    public class Pantographs
    {
        readonly MSTSWagon Wagon;

        public List<Pantograph> List = new List<Pantograph>();

        public Pantographs(MSTSWagon wagon)
        {
            Wagon = wagon;
        }

        public void Parse(string lowercasetoken, STFReader stf)
        {
            switch (lowercasetoken)
            {
                case "wagon(ortspantographs":
                    List.Clear();

                    stf.MustMatch("(");
                    stf.ParseBlock(
                        new[] {
                            new STFReader.TokenProcessor(
                                "pantograph",
                                () => {
                                    List.Add(new Pantograph(Wagon));
                                    List.Last().Parse(stf);
                                }
                            )
                        }
                    );

                    if (List.Count() == 0)
                        throw new InvalidDataException("ORTSPantographs block with no pantographs");

                    break;
            }
        }

        public void Copy(Pantographs pantographs)
        {
            List.Clear();

            foreach (Pantograph pantograph in pantographs.List)
            {
                List.Add(new Pantograph(Wagon));
                List.Last().Copy(pantograph);
            }
        }

        public void Restore(BinaryReader inf)
        {
            List.Clear();

            int n = inf.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                List.Add(new Pantograph(Wagon));
                List.Last().Restore(inf);
            }
        }

        public void Initialize()
        {
            while (List.Count() < 2)
            {
                Add(new Pantograph(Wagon));
            }

            foreach (Pantograph pantograph in List)
            {
                pantograph.Initialize();
            }
        }

        public void InitializeMoving()
        {
            foreach (Pantograph pantograph in List)
            {
                if (pantograph != null)
                {
                    pantograph.InitializeMoving();

                    break;
                }
            }
        }

        public void Update(double elapsedClockSeconds)
        {
            foreach (Pantograph pantograph in List)
            {
                pantograph.Update(elapsedClockSeconds);
            }
        }

        public void HandleEvent(PowerSupplyEvent evt)
        {
            foreach (Pantograph pantograph in List)
            {
                pantograph.HandleEvent(evt);
            }
        }

        public void HandleEvent(PowerSupplyEvent evt, int id)
        {
            if (id <= List.Count)
            {
                List[id - 1].HandleEvent(evt);
            }
        }

        public void Save(BinaryWriter outf)
        {
            outf.Write(List.Count());
            foreach (Pantograph pantograph in List)
            {
                pantograph.Save(outf);
            }
        }

        #region ListManipulation

        public void Add(Pantograph pantograph)
        {
            List.Add(pantograph);
        }

        public int Count { get { return List.Count; } }

        public Pantograph this[int i]
        {
            get
            {
                if (i <= 0 || i > List.Count)
                    return null;
                else
                    return List[i - 1];
            }
        }

        #endregion

        public PantographState State
        {
            get
            {
                PantographState state = PantographState.Down;

                foreach (Pantograph pantograph in List)
                {
                    if (pantograph.State > state)
                        state = pantograph.State;
                }

                return state;
            }
        }
    }

    public class Pantograph
    {
        readonly MSTSWagon Wagon;

        public PantographState State { get; private set; }
        public float DelayS { get; private set; }
        public float TimeS { get; private set; }
        public bool CommandUp {
            get
            {
                bool value;

                switch (State)
                {
                    default:
                    case PantographState.Down:
                    case PantographState.Lowering:
                        value = false;
                        break;

                    case PantographState.Up:
                    case PantographState.Raising:
                        value = true;
                        break;
                }

                return value;
            }
        }
        public int Id
        {
            get
            {
                return Wagon.Pantographs.List.IndexOf(this) + 1;
            }
        }

        public Pantograph(MSTSWagon wagon)
        {
            Wagon = wagon;

            State = PantographState.Down;
            DelayS = 0f;
            TimeS = 0f;
        }

        public void Parse(STFReader stf)
        {
            stf.MustMatch("(");
            stf.ParseBlock(
                new[] {
                    new STFReader.TokenProcessor(
                        "delay",
                        () => {
                            DelayS = stf.ReadFloatBlock(STFReader.Units.Time, null);
                        }
                    )
                }
            );
        }

        public void Copy(Pantograph pantograph)
        {
            State = pantograph.State;
            DelayS = pantograph.DelayS;
            TimeS = pantograph.TimeS;
        }

        public void Restore(BinaryReader inf)
        {
            State = (PantographState) Enum.Parse(typeof(PantographState), inf.ReadString());
            DelayS = inf.ReadSingle();
            TimeS = inf.ReadSingle();
        }

        public void InitializeMoving()
        {
            State = PantographState.Up;
        }

        public void Initialize()
        {

        }

        public void Update(double elapsedClockSeconds)
        {
            switch (State)
            {
                case PantographState.Lowering:
                    TimeS -= (float)elapsedClockSeconds;

                    if (TimeS <= 0f)
                    {
                        TimeS = 0f;
                        State = PantographState.Down;
                    }
                    break;

                case PantographState.Raising:
                    TimeS += (float)elapsedClockSeconds;

                    if (TimeS >= DelayS)
                    {
                        TimeS = DelayS;
                        State = PantographState.Up;
                    }
                    break;
            }
        }

        public void HandleEvent(PowerSupplyEvent evt)
        {
            TrainEvent soundEvent = TrainEvent.None;

            switch (evt)
            {
                case PowerSupplyEvent.LowerPantograph:
                    if (State == PantographState.Up || State == PantographState.Raising)
                    {
                        State = PantographState.Lowering;

                        switch (Id)
                        {
                            default:
                            case 1:
                                soundEvent = TrainEvent.Pantograph1Down;
                                break;

                            case 2:
                                soundEvent = TrainEvent.Pantograph2Down;
                                break;

                            case 3:
                                soundEvent = TrainEvent.Pantograph3Down;
                                break;

                            case 4:
                                soundEvent = TrainEvent.Pantograph4Down;
                                break;
                        }
                    }

                    break;

                case PowerSupplyEvent.RaisePantograph:
                    if (State == PantographState.Down || State == PantographState.Lowering)
                    {
                        State = PantographState.Raising;

                        switch (Id)
                        {
                            default:
                            case 1:
                                soundEvent = TrainEvent.Pantograph1Up;
                                break;

                            case 2:
                                soundEvent = TrainEvent.Pantograph2Up;
                                break;

                            case 3:
                                soundEvent = TrainEvent.Pantograph3Up;
                                break;

                            case 4:
                                soundEvent = TrainEvent.Pantograph4Up;
                                break;
                        }
                    }
                    break;
            }

            if (soundEvent != TrainEvent.None)
            {
                foreach (var eventHandler in Wagon.EventHandlers)
                {
                    eventHandler.HandleEvent(soundEvent);
                }
            }
        }

        public void Save(BinaryWriter outf)
        {
            outf.Write(State.ToString());
            outf.Write(DelayS);
            outf.Write(TimeS);
        }
    }
}
