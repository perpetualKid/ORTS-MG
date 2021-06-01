using System;
using System.Diagnostics;

using Microsoft.Xna.Framework;

using Orts.Common;
using Orts.Common.Calc;
using Orts.Common.Position;
using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Models
{
    #region TrackItem
    /// <summary>
    /// Describes a Track Item, that is an item located on the track that interacts with the train or train operations
    /// This is a base class. 
    /// </summary>
    public abstract class TrackItem
    {
        protected WorldLocation location;
        /// <summary>
        /// The name of the item (used for the label shown by F6)
        /// </summary>
        public string ItemName { get; protected set; }

        public ref readonly WorldLocation Location => ref location;

        /// <summary>Id if track item</summary>
        public uint TrackItemId { get; internal protected set; }
        /// <summary>Extra data 1, related to location along section</summary>
        public float SData1 { get; protected set; }
        /// <summary>Extra data 2</summary>
        public string SData2 { get; protected set; }

        /// <summary>
        /// Base constructor
        /// </summary>
        protected TrackItem()
        {
        }

        /// <summary>
        /// Reads the ID from filestream
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        /// <param name="index">The index of this TrItem in the list of TrItems</param>
        protected void ParseTrackItemId(STFReader stf, int index)
        {
            stf.MustMatchBlockStart();
            TrackItemId = stf.ReadUInt(null);
            Debug.Assert(index == TrackItemId, "Index Mismatch");
            stf.SkipRestOfBlock();
        }

        /// <summary>
        /// Reads the Rdata from filestream
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        protected void ParseTrackItemRData(STFReader stf)
        {
            stf.MustMatchBlockStart();
            float x = stf.ReadFloat(null);
            float y = stf.ReadFloat(null);
            float z = stf.ReadFloat(null);
            location = new WorldLocation(stf.ReadInt(null), stf.ReadInt(null), x, y, z);
            stf.SkipRestOfBlock();
        }

        /// <summary>
        /// Reads the SData from filestream
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        protected void ParseTrackItemSData(STFReader stf)
        {
            stf.MustMatchBlockStart();
            SData1 = stf.ReadFloat(STFReader.Units.None, null);
            SData2 = stf.ReadString();
            stf.SkipRestOfBlock();
        }
    } // TrItem

    /// <summary>
    /// Describes a cross-over track item
    /// <summary>A place where two tracks cross over each other</summary>
    /// </summary>
    public class CrossoverItem : TrackItem
    {
        /// <summary>Index to the tracknode</summary>
        public uint TrackNode { get; private set; }
        /// <summary>Index to the shape ID</summary>
        public uint ShapeId { get; private set; }
        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        /// <param name="index">The index of this TrItem in the list of TrItems</param>
        public CrossoverItem(STFReader stf, int index)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("tritemid", ()=>{ ParseTrackItemId(stf, index); }),
                new STFReader.TokenProcessor("tritemrdata", ()=>{ ParseTrackItemRData(stf); }),
                new STFReader.TokenProcessor("tritemsdata", ()=>{ ParseTrackItemSData(stf); }),

                new STFReader.TokenProcessor("crossovertritemdata", ()=>{
                    stf.MustMatchBlockStart();
                    TrackNode = stf.ReadUInt(null);
                    ShapeId = stf.ReadUInt(null);
                    stf.SkipRestOfBlock();
                }),
            });
        }
    }

    /// <summary>
    /// Describes a signal item
    /// </summary>
    public class SignalItem : TrackItem
    {
        /// <summary>
        /// Struct to describe details of the signal for junctions
        /// </summary>
        public class TrackItemSignalDirection
        {
            /// <summary>Index to the junction track node</summary>
            public uint TrackNode { get; private set; }
            ///// <summary>Used with junction signals, appears to be either 1 or 0</summary>
            //public uint SData1 { get; private set; }
            /// <summary>Used with junction signals, appears to be either 1 or 0</summary>
            public uint LinkLRPath { get; private set; }
            ///// <summary>Used with junction signals, appears to be either 1 or 0</summary>
            //public uint SData3 { get; private set; }

            public TrackItemSignalDirection(STFReader stf)
            {
                stf.MustMatchBlockStart();
                TrackNode = stf.ReadUInt(null);
                //SData1 = stf.ReadUInt(null);
                stf.ReadUInt(null);
                LinkLRPath = stf.ReadUInt(null);
                //SData3 = stf.ReadUInt(null);
                stf.ReadUInt(null);
                stf.SkipRestOfBlock();
            }
        }

        /// <summary>Set to  00000001 if junction link set</summary>
        public uint Flags1 { get; private set; }
        /// <summary>0 or 1 depending on which way signal is facing</summary>
        public TrackDirection Direction { get; private set; }
        /// <summary>index to Sigal Object Table</summary>
        public int SignalObject { get; set; }
        /// <summary>Signal Data 1</summary>
        public float SignalData { get; private set; }
        /// <summary>Type of signal</summary>
        public string SignalType { get; private set; }
        /// <summary></summary>
        public TrackItemSignalDirection[] SignalDirections { get; private set; }

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        /// <param name="index">The index of this TrItem in the list of TrItems</param>
        public SignalItem(STFReader stf, int index)
        {
            SignalObject = -1;
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("tritemid", ()=>{ ParseTrackItemId(stf, index); }),
                new STFReader.TokenProcessor("tritemrdata", ()=>{ ParseTrackItemRData(stf); }),
                new STFReader.TokenProcessor("tritemsdata", ()=>{ ParseTrackItemSData(stf); }),

                new STFReader.TokenProcessor("trsignaltype", ()=>{
                    stf.MustMatchBlockStart();
                    Flags1 = stf.ReadUInt(null);
                    Direction = (TrackDirection)stf.ReadUInt(null);
                    SignalData = stf.ReadFloat(STFReader.Units.None, null);
                    SignalType = stf.ReadString();
                    // To do get index to Sigtypes table corresponding to this sigmal
                    stf.SkipRestOfBlock();
                }),
                new STFReader.TokenProcessor("trsignaldirs", ()=>{
                    stf.MustMatchBlockStart();
                    uint signalDirs = stf.ReadUInt(null);
                    SignalDirections = new TrackItemSignalDirection[signalDirs];
                    int i = 0;
                    stf.ParseBlock(new STFReader.TokenProcessor[] {
                        new STFReader.TokenProcessor("trsignaldir", ()=>{
                            if (i >= signalDirs)
                            {
                                STFException.TraceWarning(stf, $"Adding extra TrSignalDirs in SignalItem {TrackItemId}");
                                var temp = new TrackItemSignalDirection[signalDirs+1];
                                Array.Copy(SignalDirections, temp, SignalDirections.Length);
                                SignalDirections = temp;
                            }
                            SignalDirections[i] = new TrackItemSignalDirection(stf);
                            i++;
                        }),
                    });
                    if (i < signalDirs)
                        STFException.TraceWarning(stf, $"{(signalDirs - i)} missing TrSignalDirs(s)");
                }),
            });
        }
    }

    /// <summary>
    /// Describes SpeedPost of MilePost (could be Kilometer post as well)
    /// </summary>
    public class SpeedPostItem : TrackItem
    {
        /// <summary>Flags from raw file describing exactly what this is.</summary>
        protected uint flags;

        /// <summary>true to be milepost</summary>
        public bool IsMilePost { get; protected set; }
        /// <summary>speed warning</summary>
        public bool IsWarning => ((flags & 1) != 0);
        /// <summary>speed limit</summary>
        public bool IsLimit => ((flags & (1 << 1)) != 0);
        /// <summary>speed resume sign (has no speed defined!)</summary>
        public bool IsResume { get; protected set; }
        /// <summary>is passenger speed limit</summary>
        public bool IsPassenger => (flags & (1 << 5)) != 0 || ((flags & (1 << 7)) != 0);
        /// <summary>is freight speed limit</summary>
        public bool IsFreight => ((flags & (1 << 6)) != 0) || ((flags & (1 << 7)) != 0);
        /// <summary>is the digit in MPH or KPH</summary>
        public bool IsMPH => ((flags & (1 << 8)) != 0);
        /// <summary>show numbers instead of KPH, like 5 means 50KMH</summary>
        public bool ShowNumber => ((flags & (1 << 4)) != 0);
        /// <summary>if ShowNumber is true and this is set, will show 1.5 as for 15KMH</summary>
        public bool ShowDot => ShowNumber && ((flags & (1 << 9)) != 0);
        /// <summary>Or distance if mile post.</summary>
        public float Distance { get; protected set; }

        /// <summary>index to Signal Object Table</summary>
        public int SignalObject { get; set; }
        /// <summary>speedpost (normalized) angle</summary>
        public float Angle { get; protected set; }
        /// <summary>derived direction relative to track</summary>
        public int Direction { get; protected set; }
        /// <summary>number to be displayed if ShowNumber is true</summary>
        public int NumberShown { get; protected set; }

        /// <summary>Get the direction the signal is NOT facing</summary>
        public int ReverseDirection
        {
            get { return Direction == 0 ? 1 : 0; }
        }

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        /// <param name="idx">The index of this TrItem in the list of TrItems</param>
        public SpeedPostItem(STFReader stf, int idx)
        {
            SignalObject = -1;
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("tritemid", ()=>{ ParseTrackItemId(stf, idx); }),
                new STFReader.TokenProcessor("tritemrdata", ()=>{ ParseTrackItemRData(stf); }),
                new STFReader.TokenProcessor("tritemsdata", ()=>{ ParseTrackItemSData(stf); }),

                new STFReader.TokenProcessor("speedposttritemdata", ()=>{
                    stf.MustMatchBlockStart();
                    flags = stf.ReadUInt(null);
                    if (!IsWarning && !IsLimit) {
                        IsMilePost = true;
                    }
                    else
                    {
                        if (IsWarning && IsLimit)
                        {
                            flags |= 0x1;
                            IsResume = true;
                        }
                    }

                    //  The number of parameters depends on the flags seeting
                    //  To do: Check flags seetings and parse accordingly.
		            if (!IsResume)
                    {
                        //SpeedInd = stf.ReadFloat(STFReader.Units.None, null);
                        if (IsMilePost && ((flags & (1 << 9)) == 0)) Distance = (float)Math.Truncate(stf.ReadDouble(null));
                        else Distance = stf.ReadFloat(STFReader.Units.None, null);
                    }

                    if (ShowNumber)
                    {
                        NumberShown = stf.ReadInt(null);
                    }

                    Angle = MathHelper.WrapAngle(stf.ReadFloat(STFReader.Units.None, null));

                    stf.SkipRestOfBlock();
                }),
            });
        }

        // used as base for TempSpeedPostItem
        protected SpeedPostItem()
        { }
    }

    public class TempSpeedPostItem : SpeedPostItem
    {
        private WorldPosition position;
        /// <summary>
        /// Constructor for creating a speedpost from activity speed restriction zone
        /// </summary>
        /// <param name="routeFile">The routeFile with relevant data about speeds</param>
        /// <param name="position">Position/location of the speedposts</param>
        /// <param name="isStart">Is this the start of a speed zone?</param>
        /// 
        public ref readonly WorldPosition WorldPosition => ref position;

        public TempSpeedPostItem(Route routeFile, in WorldLocation location, bool isStart, in WorldPosition worldPosition, bool isWarning)
        {
            // TrItemId needs to be set later
            position = worldPosition;
            this.location = location;

            IsMilePost = false;
            flags = isWarning ? flags & 1u : flags & ~1u;   //isWarning
            flags |= 0b0000_0010;              //isLimit
            flags |= 0b1110_0000;           //isFreight, IsPassenger
            IsResume = !isStart;
            if (routeFile.MilepostUnitsMetric)
            {
                flags &= ~(1u << 8);
                Distance = (int)(Speed.MeterPerSecond.ToKpH(routeFile.TempRestrictedSpeed) + 0.1f);
            }
            else
            {
                flags |= (1u << 8);
                Distance = (int)(Speed.MeterPerSecond.ToMpH(routeFile.TempRestrictedSpeed) + 0.1f);
            }

            Angle = 0;
        }

        public void Update(float y, float angle, in WorldPosition position)
        {
            location = location.SetElevation(y);
            Angle = angle;
            this.position = position;
        }

        /// <summary>
        /// Flip restricted speedpost 
        /// </summary>
        public void Flip()
        {
            Angle += (float)Math.PI;
            position = new WorldPosition(position.TileX, position.TileZ, new Matrix(
                -position.XNAMatrix.M11, position.XNAMatrix.M12, -position.XNAMatrix.M13, position.XNAMatrix.M14,
                position.XNAMatrix.M21, position.XNAMatrix.M22, position.XNAMatrix.M23, position.XNAMatrix.M24,
                -position.XNAMatrix.M31, position.XNAMatrix.M32, -position.XNAMatrix.M33, position.XNAMatrix.M34,
                position.XNAMatrix.M41, position.XNAMatrix.M42, position.XNAMatrix.M43, position.XNAMatrix.M44));
        }

        /// <summary>
        /// Compute position of restricted speedpost table
        /// </summary>
        public void ComputeTablePosition()
        {
            Vector3 speedPostTablePosition = new Vector3(2.2f, 0, 0);
            speedPostTablePosition = Vector3.Transform(speedPostTablePosition, position.XNAMatrix);

            //looks suspicios to change location back to MSTS coordinates just to Normalize, and rest the location even after Normalize
            //position = position.SetMstsTranslation(speedPostTablePosition).Normalize();
            //position = position.SetMstsTranslation(position.XNAMatrix.Translation);
            position = position.SetTranslation(speedPostTablePosition).Normalize();
        }
    }

    /// <summary>
    /// Represents a region where a sound can be played.
    /// </summary>
    public class SoundRegionItem : TrackItem
    {
        /// <summary>Sound region data 1</summary>
        public uint SoundRegionData1 { get; private set; }
        /// <summary>Sound region data 2</summary>
        public uint SoundRegionData2 { get; private set; }
        /// <summary>Sound region data 3</summary>
        public float SoundRegionData3 { get; private set; }

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        /// <param name="index">The index of this TrItem in the list of TrItems</param>
        public SoundRegionItem(STFReader stf, int index)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("tritemid", ()=>{ ParseTrackItemId(stf, index); }),
                new STFReader.TokenProcessor("tritemrdata", ()=>{ ParseTrackItemRData(stf); }),
                new STFReader.TokenProcessor("tritemsdata", ()=>{ ParseTrackItemSData(stf); }),

                new STFReader.TokenProcessor("tritemsrdata", ()=>{
                    stf.MustMatchBlockStart();
                    SoundRegionData1 = stf.ReadUInt(null);
                    SoundRegionData2 = stf.ReadUInt(null);
                    SoundRegionData3 = stf.ReadFloat(STFReader.Units.None, null);
                    stf.SkipRestOfBlock();
                }),
            });
        }
    }

    /// <summary>
    /// represent an empty item (which probably should only happen for badly defined routes?)
    /// </summary>
    public class EmptyItem : TrackItem
    {
        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        /// <param name="idx">The index of this TrItem in the list of TrItems</param>
        public EmptyItem(STFReader stf, int idx)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("tritemid", ()=>{ ParseTrackItemId(stf, idx); }),
            });
        }
    }

    /// <summary>
    /// Representa a level Crossing item (so track crossing road)
    /// </summary>
    public class LevelCrossingItem : TrackItem
    {
        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        /// <param name="index">The index of this TrItem in the list of TrItems</param>
        public LevelCrossingItem(STFReader stf, int index)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("tritemid", ()=>{ ParseTrackItemId(stf, index); }),
                new STFReader.TokenProcessor("tritemrdata", ()=>{ ParseTrackItemRData(stf); }),
                new STFReader.TokenProcessor("tritemsdata", ()=>{ ParseTrackItemSData(stf); }),
            });
        }
    }

    /// <summary>
    /// Represents either start or end of a siding.
    /// </summary>
    public class SidingItem : TrackItem
    {
        /// <summary>Flags 1 for a siding ???</summary>
        public string Flags1 { get; private set; }
        /// <summary>Flags 2 for a siding, probably the index of the other end of the siding.</summary>
        public uint LinkedSidingId { get; private set; }

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        /// <param name="index">The index of this TrItem in the list of TrItems</param>
        public SidingItem(STFReader stf, int index)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("tritemid", ()=>{ ParseTrackItemId(stf, index); }),
                new STFReader.TokenProcessor("tritemrdata", ()=>{ ParseTrackItemRData(stf); }),
                new STFReader.TokenProcessor("tritemsdata", ()=>{ ParseTrackItemSData(stf); }),

                new STFReader.TokenProcessor("sidingname", ()=>{ ItemName = stf.ReadStringBlock(""); }),
                new STFReader.TokenProcessor("sidingtritemdata", ()=> {
                    stf.MustMatchBlockStart();
                    Flags1 = stf.ReadString();
                    LinkedSidingId = stf.ReadUInt(null);
                    stf.SkipRestOfBlock();
                }),
            });
        }
    }

    /// <summary>
    /// Represents either start or end of a platform (a place where trains can stop).
    /// </summary>
    public class PlatformItem : TrackItem
    {

        /// <summary>Name of the station where the platform is</summary>
        public string Station { get; private set; }
        /// <summary>Flags 1 for a platform ???</summary>
        public string Flags1 { get; private set; }
        /// <summary>Minimum waiting time at the platform</summary>
        public uint PlatformMinWaitingTime { get; private set; }
        /// <summary>Number of passengers waiting at the platform</summary>
        public uint PlatformNumPassengersWaiting { get; private set; }
        /// <summary>TrItem Id of the other end of the platform</summary>
        public uint LinkedPlatformItemId { get; private set; }

        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        /// <param name="idx">The index of this TrItem in the list of TrItems</param>
        public PlatformItem(STFReader stf, int idx)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("tritemid", ()=>{ ParseTrackItemId(stf, idx); }),
                new STFReader.TokenProcessor("tritemrdata", ()=>{ ParseTrackItemRData(stf); }),
                new STFReader.TokenProcessor("tritemsdata", ()=>{ ParseTrackItemSData(stf); }),

                new STFReader.TokenProcessor("platformname", ()=>{ ItemName = stf.ReadStringBlock(""); }),
                new STFReader.TokenProcessor("station", ()=>{ Station = stf.ReadStringBlock(""); }),
                new STFReader.TokenProcessor("platformminwaitingtime", ()=>{ PlatformMinWaitingTime = stf.ReadUIntBlock(null); }),
                new STFReader.TokenProcessor("platformnumpassengerswaiting", ()=>{ PlatformNumPassengersWaiting = stf.ReadUIntBlock(null); }),
                new STFReader.TokenProcessor("platformtritemdata", ()=>{
                    stf.MustMatchBlockStart();
                    Flags1 = stf.ReadString();
                    LinkedPlatformItemId = stf.ReadUInt(null);
                    stf.SkipRestOfBlock();
                }),
            });
        }

        /// <summary>
        /// Constructor to create Platform Item out of Siding Item
        /// </summary>
        /// <param name="thisSiding">The siding to use for a platform creation</param>
        public PlatformItem(SidingItem thisSiding)
        {
            TrackItemId = thisSiding.TrackItemId;
            SData1 = thisSiding.SData1;
            SData2 = thisSiding.SData2;
            ItemName = thisSiding.ItemName;
            Flags1 = thisSiding.Flags1;
            LinkedPlatformItemId = thisSiding.LinkedSidingId;
            Station = ItemName;
        }
    }

    /// <summary>
    /// Represends a hazard, a place where something more or less dangerous happens
    /// </summary>
    public class HazardItem : TrackItem
    {
        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        /// <param name="index">The index of this TrItem in the list of TrItems</param>
        public HazardItem(STFReader stf, int index)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("tritemid", ()=>{ ParseTrackItemId(stf, index); }),
                new STFReader.TokenProcessor("tritemrdata", ()=>{ ParseTrackItemRData(stf); }),
                new STFReader.TokenProcessor("tritemsdata", ()=>{ ParseTrackItemSData(stf); }),
            });
        }
    }

    /// <summary>
    /// Represents a pickup, a place to pickup fuel, water, ...
    /// </summary>
    public class PickupItem : TrackItem
    {
        /// <summary>
        /// Default constructor used during file parsing.
        /// </summary>
        /// <param name="stf">The STFreader containing the file stream</param>
        /// <param name="index">The index of this TrItem in the list of TrItems</param>
        public PickupItem(STFReader stf, int index)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("tritemid", ()=>{ ParseTrackItemId(stf, index); }),
                new STFReader.TokenProcessor("tritemrdata", ()=>{ ParseTrackItemRData(stf); }),
                new STFReader.TokenProcessor("tritemsdata", ()=>{ ParseTrackItemSData(stf); }),
            });
        }
    }
    #endregion
}
