using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Models
{
    public class TimeTable
    {
        public float InitialSpeed { get; private set; }

        internal TimeTable(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("startingspeed", ()=>{ InitialSpeed = stf.ReadFloatBlock(STFReader.Units.Any, null); }),
            });
        }

        // Used for explore in activity mode
        public TimeTable()
        {

        }
    }
}
