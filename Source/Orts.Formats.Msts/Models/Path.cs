
using Orts.Common.Position;
using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Models
{
    public class PathDataPoint
    {
        private readonly WorldLocation location;
        public ref readonly WorldLocation Location => ref location;
        public int JunctionFlag { get; private set; }
        public int InvalidFlag { get; private set; }

        #region Properties
        //Note : these flags are not understood in all detail
        public bool IsJunction => JunctionFlag == 2;
        public bool IsInvalid => InvalidFlag == 9;  //TODO: probably also 12 is invalid.
        #endregion

        internal PathDataPoint(STFReader stf)
        {
            stf.MustMatchBlockStart();
            location = new WorldLocation(stf.ReadInt(null), stf.ReadInt(null),
                stf.ReadFloat(STFReader.Units.None, null), stf.ReadFloat(STFReader.Units.None, null), stf.ReadFloat(STFReader.Units.None, null));
            JunctionFlag = stf.ReadInt(null);
            InvalidFlag = stf.ReadInt(null);
            stf.SkipRestOfBlock();
        }
    }

    // for an explanation, see class PATfile 
    public class PathNode
    {
        public PathFlags PathFlags { get; private set; }
        public uint NextMainNode { get; private set; }
        public uint NextSidingNode { get; private set; }
        public uint PathDataPoint { get; private set; }

        public bool HasNextMainNode => (NextMainNode != 0xffffffff);
        public bool HasNextSidingNode => (NextSidingNode != 0xffffffff);

        public int WaitTime => (int)(((uint)PathFlags >> 16) & 0xFFFF);

        internal PathNode(STFReader stf)
        {
            stf.MustMatchBlockStart();
            PathFlags = (PathFlags)stf.ReadHex(0);
            NextMainNode = stf.ReadUInt(null);
            NextSidingNode = stf.ReadUInt(null);
            PathDataPoint = stf.ReadUInt(null);
            stf.SkipRestOfBlock();
        }

        internal PathNode(uint flags, uint nextNode, uint nextSiding, uint pathDataPoint)
        {
            PathFlags = (PathFlags)flags;
            NextMainNode = nextNode;
            NextSidingNode = nextSiding;
            PathDataPoint = pathDataPoint;
        }
    }
}
