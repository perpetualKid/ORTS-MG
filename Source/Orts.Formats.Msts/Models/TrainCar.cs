﻿using System.Collections.Generic;
using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Models
{
    public class WagonList
    {
        public List<WorkOrderWagon> WorkOrderWagonList { get; } = new List<WorkOrderWagon>();
        private uint uid;

        public WagonList(STFReader stf, EventType eventType)
        {
            stf.MustMatch("(");
            // "Drop Off" Wagon_List sometimes lacks a Description attribute, so we create the wagon _before_ description
            // is parsed. 
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("uid", ()=>
                {
                    uid = stf.ReadUIntBlock(null);
                }),
                new STFReader.TokenProcessor("sidingitem", ()=>
                {
                    WorkOrderWagonList.Add(new WorkOrderWagon(uid, stf.ReadUIntBlock(null)));
                }),
                new STFReader.TokenProcessor("description", ()=>
                {
                    WorkOrderWagonList[WorkOrderWagonList.Count-1].Description = stf.ReadStringBlock("");
                }),
            });
        }
    }

    public class TrainSet
    {
        public TrainConfig TrainConfig { get; private set; }

        public TrainSet(STFReader stf)
        {
            stf.MustMatch("(");
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("traincfg", ()=>{ TrainConfig = new TrainConfig(stf); }),
            });
        }
    }

    public class TrainConfig
    {
        public string Name { get; private set; } = "Loose consist.";
        public int Serial { get; private set; } = 1;
        public MaxVelocity MaxVelocity { get; private set; }
        public float Durability { get; private set; } = 1.0f;   // Value assumed if attribute not found.
        private int nextWagonUiD;

        public List<Wagon> WagonList { get; } = new List<Wagon>();

        public TrainConfig(STFReader stf)
        {
            stf.MustMatch("(");
            Name = stf.ReadString();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("name", ()=>{ Name = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("serial", ()=>{ Serial = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("maxvelocity", ()=>{ MaxVelocity = new MaxVelocity(stf); }),
                new STFReader.TokenProcessor("nextwagonuid", ()=>{ nextWagonUiD = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("durability", ()=>{ Durability = stf.ReadFloatBlock(STFReader.Units.None, null); }),
                new STFReader.TokenProcessor("wagon", ()=>{ WagonList.Add(new Wagon(stf)); }),
                new STFReader.TokenProcessor("engine", ()=>{ WagonList.Add(new Wagon(stf)); }),
            });
        }
    }

    /// <summary>
    /// Parses a wagon from the WagonList.
    /// Do not confuse with older class Wagon below, which parses TrainCfg from the *.con file.
    /// </summary>
    public class WorkOrderWagon
    {
        public uint UiD { get; private set; }        
        public uint SidingId { get; private set; }   
        public string Description { get; internal set; } = "";   // Value assumed if property not found.

        public WorkOrderWagon(uint uid, uint sidingId)
        {
            UiD = uid;
            SidingId = sidingId;
        }
    }

    public class Wagon
    {
        public string Folder { get; private set; }
        public string Name { get; private set; }
        public int UiD { get; private set; }
        public bool IsEngine { get; private set; }
        public bool Flip { get; private set; }

        public Wagon(STFReader stf)
        {
            stf.MustMatch("(");
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("uid", ()=>{ UiD = stf.ReadIntBlock(null); }),
                new STFReader.TokenProcessor("flip", ()=>{ stf.MustMatch("("); stf.MustMatch(")"); Flip = true; }),
                new STFReader.TokenProcessor("enginedata", ()=>{ stf.MustMatch("("); Name = stf.ReadString(); Folder = stf.ReadString(); stf.MustMatch(")"); IsEngine = true; }),
                new STFReader.TokenProcessor("wagondata", ()=>{ stf.MustMatch("("); Name = stf.ReadString(); Folder = stf.ReadString(); stf.MustMatch(")"); }),
            });
        }

        public string GetName(uint uId, List<Wagon> wagonList)
        {
            foreach (var item in wagonList)
            {
                var wagon = item as Wagon;
                if (wagon.UiD == uId)
                {
                    return wagon.Name;
                }
            }
            return "<unknown name>";
        }
    }

}
