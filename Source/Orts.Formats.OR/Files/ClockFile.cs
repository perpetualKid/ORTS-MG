using Orts.Formats.Msts.Parsers;
using Orts.Formats.OR.Models;

namespace Orts.Formats.OR.Files
{
    public class ClockFile
    {
        public ClockList Clocks { get; private set; }

        public ClockFile(string fileName, string shapePath)
        {
            using (STFReader stf = new STFReader(fileName, false))
            {
                Clocks = new ClockList(stf, shapePath);
            }
        }
    }

}
