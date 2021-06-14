using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Models
{
    /// <summary>
    /// This class represents the Road Track Database. This is pretty similar to the (rail) Track Database. So for more details see there
    /// </summary>
	public class RoadTrackDB
    {
        /// <summary>
        /// Array of all TrackNodes in the road database
        /// Warning, the first TrackNode is always null.
        /// </summary>
        public TrackNode[] TrackNodes { get; private set; }

        /// <summary>
        /// Array of all Track Items (TrItem) in the road database
        /// </summary>
        public TrackItem[] TrItemTable { get; private set; }

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        internal RoadTrackDB(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("tracknodes", ()=>{
                    stf.MustMatchBlockStart();
                    int count = stf.ReadInt(null);
                    TrackNodes = new TrackNode[count + 1];
                    int idx = 1;
                    stf.ParseBlock(new STFReader.TokenProcessor[] {
                        new STFReader.TokenProcessor("tracknode", ()=>{ TrackNodes[idx] = TrackNode.ReadTrackNode(stf, idx, count); ++idx; }),
                    });
                }),
                new STFReader.TokenProcessor("tritemtable", ()=>{
                    stf.MustMatchBlockStart();
                    int count = stf.ReadInt(null);
                    TrItemTable = new TrackItem[count];
                    int idx = -1;
                    stf.ParseBlock(()=> ++idx == -1, new STFReader.TokenProcessor[] {
                        new STFReader.TokenProcessor("levelcritem", ()=>{ TrItemTable[idx] = new RoadLevelCrossingItem(stf,idx); }),
                        new STFReader.TokenProcessor("emptyitem", ()=>{ TrItemTable[idx] = new EmptyItem(stf,idx); }),
                        new STFReader.TokenProcessor("carspawneritem", ()=>{ TrItemTable[idx] = new RoadCarSpawner(stf,idx); })
                    });
                }),
            });
        }
    }

    /// <summary>
    /// Represents a Level crossing Item on the road (i.e. where cars must stop when a train is passing).
    /// </summary>
    public class RoadLevelCrossingItem : TrackItem
    {
        /// <summary>Direction along track: 0 or 1 depending on which way signal is facing</summary>
        public uint Direction { get; private set; }
        /// <summary>index to Signal Object Table</summary>
        public int SignalObject { get; private set; }

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        /// <param name="idx">The index of this TrItem in the list of TrItems</param>
        internal RoadLevelCrossingItem(STFReader stf, int idx)
        {
            SignalObject = -1;
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("tritemid", ()=>{ ParseTrackItemId(stf, idx); }),
                new STFReader.TokenProcessor("tritemrdata", ()=>{ ParseTrackItemRData(stf); }),
                new STFReader.TokenProcessor("tritemsdata", ()=>{ ParseTrackItemSData(stf); }),
            });
        }
    }

    /// <summary>
    /// Represent a Car Spawner: the place where cars start to appear or disappear again
    /// </summary>
	public class RoadCarSpawner : TrackItem
    {
        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        /// <param name="idx">The index of this TrItem in the list of TrItems</param>
		internal RoadCarSpawner(STFReader stf, int idx)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("tritemid", ()=>{ ParseTrackItemId(stf, idx); }),
                new STFReader.TokenProcessor("tritemrdata", ()=>{ ParseTrackItemRData(stf); }),
                new STFReader.TokenProcessor("tritemsdata", ()=>{ ParseTrackItemSData(stf); })
            });
        }
    }
}
