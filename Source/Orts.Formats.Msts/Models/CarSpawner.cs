using Orts.Formats.Msts.Parsers;

using System;
using System.Collections.Generic;
using System.IO;

namespace Orts.Formats.Msts.Models
{
    public class CarSpawners: List<CarSpawner>
    {
        public string ListName { get; private set; }
        public bool IgnoreXRotation { get; private set; }// true for humans

        public CarSpawners(STFReader stf, string shapePath, string listName)
        {
            if (null == stf)
                throw new ArgumentNullException(nameof(stf));

            ListName = listName;
            int count = stf.ReadInt(null);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("ignorexrotation", ()=>{IgnoreXRotation = stf.ReadBoolBlock(true); }),
                new STFReader.TokenProcessor("carspawneritem", ()=>{
                    if (--count < 0)
                        STFException.TraceWarning(stf, "Skipped extra CarSpawnerItem");
                    else
                    {
                        CarSpawner dataItem = new CarSpawner(stf, shapePath);
                        if (File.Exists(dataItem.Name))
                            Add(dataItem);
                        else
                            STFException.TraceWarning(stf, $"Non-existent shape file {dataItem.Name} referenced");
                    }
                    }),
                });
            if (count > 0)
                STFException.TraceWarning(stf, count + " missing CarSpawnerItem(s)");
        }
    }

    public class CarSpawner
    {
        public string Name { get; private set; }
        public float Distance { get; private set; }

        internal CarSpawner(STFReader stf, string shapePath)
        {
            stf.MustMatchBlockStart();
            //pre fit in the shape path so no need to do it again and again later
            Name = shapePath + stf.ReadString();
            Distance = stf.ReadFloat(STFReader.Units.Distance, null);
            stf.SkipRestOfBlock();
        }
    }
}
