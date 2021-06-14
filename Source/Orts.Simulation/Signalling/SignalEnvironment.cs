// COPYRIGHT 2013 by the Open Rails project.
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

// This module covers all classes and code for signal, speed post, track occupation and track reservation control

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Xna.Framework;

using Orts.Common;
using Orts.Common.Position;
using Orts.Formats.Msts;
using Orts.Formats.Msts.Files;
using Orts.Formats.Msts.Models;
using Orts.Formats.Msts.Parsers;
using Orts.MultiPlayer;
using Orts.Simulation.Physics;
using Orts.Simulation.Track;

namespace Orts.Simulation.Signalling
{

    //================================================================================================//
    //================================================================================================//
    /// <summary>
    /// Class Signals
    /// </summary>
    public class SignalEnvironment
    {

        //================================================================================================//
        // local data
        //================================================================================================//

        private Simulator simulator;

        /// Gets an array of all the SignalObjects.
        public List<Signal> Signals { get; private set; }

        private readonly TrackDB trackDB;
        private TrackSectionsFile tsectiondat;

        public int OrtsSignalTypeCount { get; private set; }

        private int updateStart;
        private int updateStep;

        public List<PlatformDetails> PlatformDetailsList { get; } = new List<PlatformDetails>();
        public Dictionary<int, int> PlatformXRefList { get; } = new Dictionary<int, int>();
        public Dictionary<string, List<int>> StationXRefList { get; } = new Dictionary<string, List<int>>();

        public bool UseLocationPassingPaths { get; private set; }   // Use location-based style processing of passing paths (set by Simulator)
        internal Dictionary<int, DeadlockInfo> DeadlockInfoList;    // each deadlock info has unique reference
        internal int deadlockIndex;                                 // last used reference index
        internal Dictionary<int, int> DeadlockReference;            // cross-reference between trackcircuitsection (key) and deadlockinforeference (value)

        private List<Milepost> milepostList = new List<Milepost>();                     // list of mileposts
        private int foundMileposts;

        //================================================================================================//
        /// <summary>
        /// Constructor
        /// </summary>

        public SignalEnvironment(SignalConfigurationFile sigcfg, bool locationPassingPaths, CancellationToken token)
        {
            UseLocationPassingPaths = locationPassingPaths;
            Dictionary<int, int> platformList = new Dictionary<int, int>();

            OrtsSignalTypeCount = OrSignalTypes.Instance.FunctionTypes.Count;

            trackDB = Simulator.Instance.TDB.TrackDB;
            tsectiondat = Simulator.Instance.TSectionDat;

            // read SIGSCR files

            Trace.Write(" SIGSCR ");
            SignalScriptProcessing.Initialize(new SignalScripts(sigcfg.ScriptPath, sigcfg.ScriptFiles, sigcfg.SignalTypes));

            ConcurrentBag<SignalWorldInfo> signalWorldList = new ConcurrentBag<SignalWorldInfo>();
            ConcurrentDictionary<uint, SignalWorldInfo> signalWorldLookup = new ConcurrentDictionary<uint, SignalWorldInfo>();
            ConcurrentDictionary<int, uint> platformSidesList = new ConcurrentDictionary<int, uint>();

            // build list of signal world file information
            BuildSignalWorld(Simulator.Instance.RouteFolder.WorldFolder, sigcfg, signalWorldList, signalWorldLookup, platformSidesList, token);

            // build list of signals in TDB file
            BuildSignalList(trackDB.TrackItems, trackDB.TrackNodes, tsectiondat, Simulator.Instance.TDB, platformList, signalWorldList);

            if (Signals.Count > 0)
            {
                // Add CFG info

                AddSignalConfiguration(sigcfg);

                // Add World info

                AddWorldInfo(signalWorldLookup);

                // check for any backfacing heads in signals
                // if found, split signal
                SplitBackfacing(trackDB.TrackItems, trackDB.TrackNodes);
                Signals.RemoveAll(signal => signal == null);
            }

            SetNumSignalHeads();

            //
            // Create trackcircuit database
            //
            CreateTrackCircuits(trackDB.TrackItems, trackDB.TrackNodes, tsectiondat);

            //
            // Process platform information
            //

            ProcessPlatforms(platformList, trackDB.TrackItems, trackDB.TrackNodes, platformSidesList);

            //
            // Process tunnel information
            //

            ProcessTunnels();

            //
            // Process trough information
            //

            ProcessTroughs();

            Signals.RemoveAll(signal => !signal.ValidateSignal());
            //re-index the elements
            for (int i = 0; i < Signals.Count; i++)
            {
                Signals[i].ResetIndex(i);
            }

            updateStep = (Signals.Count / 20) + 1;

            DeadlockInfoList = new Dictionary<int, DeadlockInfo>();
            deadlockIndex = 1;
            DeadlockReference = new Dictionary<int, int>();
        }

        //================================================================================================//
        /// <summary>
        /// Overlay constructor for restore after saved game
        /// </summary>
        public void Restore(BinaryReader inf)
        {
            if (null == inf)
                throw new ArgumentNullException(nameof(inf));

            int signalIndex = inf.ReadInt32();
            while (signalIndex >= 0)
            {
                Signal signal = Signals[signalIndex];
                signal.Restore(inf);
                signalIndex = inf.ReadInt32();
            }

            int tcListCount = inf.ReadInt32();

            if (tcListCount != TrackCircuitSection.TrackCircuitList.Count)
            {
                Trace.TraceError("Mismatch between saved : {0} and existing : {1} TrackCircuits", tcListCount, TrackCircuitSection.TrackCircuitList.Count);
                throw new InvalidDataException("Cannot resume route due to altered data");
            }
            else
            {
                foreach (TrackCircuitSection section in TrackCircuitSection.TrackCircuitList)
                {
                    section.Restore(inf);
                }
            }

            UseLocationPassingPaths = inf.ReadBoolean();

            DeadlockInfoList = new Dictionary<int, DeadlockInfo>();
            int totalDeadlocks = inf.ReadInt32();
            for (int iDeadlock = 0; iDeadlock <= totalDeadlocks - 1; iDeadlock++)
            {
                int thisDeadlockIndex = inf.ReadInt32();
                DeadlockInfo thisInfo = new DeadlockInfo(inf);
                DeadlockInfoList.Add(thisDeadlockIndex, thisInfo);
            }

            deadlockIndex = inf.ReadInt32();

            DeadlockReference = new Dictionary<int, int>();
            int totalReferences = inf.ReadInt32();
            for (int iReference = 0; iReference <= totalReferences - 1; iReference++)
            {
                int thisSectionIndex = inf.ReadInt32();
                int thisDeadlockIndex = inf.ReadInt32();
                DeadlockReference.Add(thisSectionIndex, thisDeadlockIndex);
            }
        }

        //================================================================================================//
        /// <summary>
        /// Restore Train links
        /// Train links must be restored separately as Trains is restored later as Signals
        /// </summary>
        public void RestoreTrains(List<Train> trains)
        {
            if (null == trains)
                throw new ArgumentNullException(nameof(trains));

            foreach (TrackCircuitSection section in TrackCircuitSection.TrackCircuitList)
            {
                section.CircuitState.RestoreTrains(trains, section.Index);
            }

            //TODO 20201103 could those loops be combined? need to check for dependencies/cross references
            // restore train information       
            if (Signals != null)
            {
                foreach (Signal signal in Signals)
                {
                    signal.RestoreTrains(trains);
                }

                // restore correct aspects
                foreach (Signal signal in Signals)
                {
                    signal.RestoreAspect();
                }
            }
        }

        //================================================================================================//
        /// <summary>
        /// Save game
        /// </summary>
        public void Save(BinaryWriter outf)
        {
            if (null == outf)
                throw new ArgumentNullException(nameof(outf));

            if (Signals != null)
            {
                foreach (Signal signal in Signals)
                {
                    outf.Write(signal.Index);
                    signal.Save(outf);
                }
            }
            outf.Write(-1);

            outf.Write(TrackCircuitSection.TrackCircuitList.Count);
            foreach (TrackCircuitSection section in TrackCircuitSection.TrackCircuitList)
            {
                section.Save(outf);
            }

            outf.Write(UseLocationPassingPaths);

            outf.Write(DeadlockInfoList.Count);
            foreach (KeyValuePair<int, DeadlockInfo> deadlockDetails in DeadlockInfoList)
            {
                outf.Write(deadlockDetails.Key);
                deadlockDetails.Value.Save(outf);
            }

            outf.Write(deadlockIndex);

            outf.Write(DeadlockReference.Count);
            foreach (KeyValuePair<int, int> referenceDetails in DeadlockReference)
            {
                outf.Write(referenceDetails.Key);
                outf.Write(referenceDetails.Value);
            }

        }

        //================================================================================================//
        /// <summary>
        /// Read all world files to get signal flags
        /// </summary>
        private void BuildSignalWorld(string worldPath, SignalConfigurationFile sigcfg, ConcurrentBag<SignalWorldInfo> signalWorldList,
            ConcurrentDictionary<uint, SignalWorldInfo> signalWorldLookup, ConcurrentDictionary<int, uint> platformSidesList, CancellationToken token)
        {
            List<TokenID> Tokens = new List<TokenID>
            {
                TokenID.Signal,
                TokenID.Platform
            };

            Parallel.ForEach(Directory.EnumerateFiles(worldPath, "w-??????+??????.w"), new ParallelOptions() { MaxDegreeOfParallelism = System.Environment.ProcessorCount, CancellationToken = token },
                (fileName) =>
                {
                    try
                    {

                        WorldFile worldFile = new WorldFile(fileName, Tokens);
                        // loop through all signals
                        foreach (WorldObject worldObject in worldFile.Objects)
                        {
                            if (worldObject is SignalObject signalObject && signalObject.SignalUnits != null)//SignalUnits may be null if this has no unit, will ignore it and treat it as static in scenary.cs
                            {
                                //check if signalheads are on same or adjacent tile as signal itself - otherwise there is an invalid match
                                bool invalid = false;
                                foreach (SignalUnit signalUnit in signalObject.SignalUnits)
                                {
                                    if (signalUnit.TrackItem >= trackDB.TrackItems.Length ||
                                        Math.Abs(trackDB.TrackItems[signalUnit.TrackItem].Location.TileX - worldObject.WorldPosition.TileX) > 1 ||
                                        Math.Abs(trackDB.TrackItems[signalUnit.TrackItem].Location.TileZ - worldObject.WorldPosition.TileZ) > 1)
                                    {
                                        Trace.TraceWarning($"Signal referenced in .w file {worldObject.WorldPosition.TileX} {worldObject.WorldPosition.TileZ} as TrItem {signalUnit.TrackItem} not present in .tdb file ");
                                        invalid = true;
                                        break;
                                    }
                                }
                                if (invalid)
                                    continue;

                                // if valid, add signal
                                SignalWorldInfo signalWorldInfo = new SignalWorldInfo(signalObject, sigcfg);
                                signalWorldList.Add(signalWorldInfo);
                                foreach (KeyValuePair<uint, uint> reference in signalWorldInfo.HeadReference)
                                {
                                    if (!signalWorldLookup.TryAdd(reference.Key, signalWorldInfo))
                                        Trace.TraceWarning($"Key {reference.Key} already exists for SignalWorldInfo");
                                }
                            }
                            else if (worldObject is PlatformObject platformObject)
                            {
                                platformSidesList.TryAdd(platformObject.TrackItemIds.TrackDbItems[0], platformObject.PlatformData);
                                platformSidesList.TryAdd(platformObject.TrackItemIds.TrackDbItems[1], platformObject.PlatformData);
                            }
                        }
                    }
                    catch (FileLoadException error)
                    {
                        Trace.WriteLine(error);
                    }
                });
            Trace.TraceInformation("Loading WorldFiles for Signal World");
        }

        //================================================================================================//
        /// <summary>
        /// Update : perform signal updates
        /// </summary>
        public void Update(bool fullUpdate)
        {
            //TODO 20201125 re-enable Parallelism 
            if (fullUpdate)
            {
                for(int i = 0; i < Signals.Count; i++)
                {
                    Signals[i].Update();
                }
                //Parallel.ForEach(Signals, new ParallelOptions() { MaxDegreeOfParallelism = System.Environment.ProcessorCount }, (signal) =>
                //{
                //    signal.Update();
                //});
            }
            else
            {
                for (int i = updateStart; i < Math.Min(updateStart + updateStep, Signals.Count); i++)
                {
                    Signals[i].Update();
                }
                ////only a fraction (1/20) of all signals is processed each update loop to limit load
                //Parallel.For(updateStart, Math.Min(updateStart + updateStep, Signals.Count), new ParallelOptions() { MaxDegreeOfParallelism = System.Environment.ProcessorCount }, (i) =>
                //{
                //    Signals[i].Update();
                //});
            }
            updateStart += updateStep;
            if (updateStart >= Signals.Count)
                updateStart = 0;
        }

        //================================================================================================//
        /// <summary></summary>
        /// Build signal list from TDB
        /// </summary>

        private void BuildSignalList(TrackItem[] trackItems, TrackNode[] trackNodes, TrackSectionsFile tsectiondat,
                TrackDatabaseFile tdbfile, Dictionary<int, int> platformList, ConcurrentBag<SignalWorldInfo> signalWorldList)
        {

            //  Determine the number of signals in the track Objects list
            int signalCount = (trackItems ?? Enumerable.Empty<TrackItem>()).Where(item => item is SignalItem || (item is SpeedPostItem speedPost && speedPost.IsLimit)).Count();

            // set general items and create sections
            Signals = new List<Signal>(signalCount);

            Signal.Initialize(this, trackNodes, trackItems);

            Dictionary<uint, Signal> signalHeadList = new Dictionary<uint, Signal>();

            for (int i = 1; i < trackNodes.Length; i++)
            {
                ScanSection(trackItems, trackNodes, i, tsectiondat, tdbfile, platformList, signalHeadList);
            }

            //  Only continue if one or more signals in route.
            if (Signals.Count > 0)
            {
                // using world cross-reference list, merge heads to single signal

                MergeHeads(signalWorldList, signalHeadList);

                Signals.RemoveAll(item => item == null);
                //re-index the elements
                for (int i = 0; i < Signals.Count; i++)
                {
                    Signals[i].ResetIndex(i);
                }
            }
            else
            {
                Signals = new List<Signal>();
            }

        } //BuildSignalList


        //================================================================================================//
        /// <summary>
        /// Split backfacing signals
        /// </summary>
        private void SplitBackfacing(TrackItem[] trackItems, TrackNode[] trackNodes)
        {
            List<Signal> backfacingSignals = new List<Signal>();
            // Loop through all signals to check on Backfacing heads
            foreach (Signal signal in Signals)
            {
                if (signal.IsSignal && signal.WorldObject?.Backfacing.Count > 0)
                {
                    // create new signal - copy of existing signal
                    // use Backfacing flags and reset head indication
                    Signal newSignal = new Signal(Signals.Count + backfacingSignals.Count, signal)
                    {
                        TrackItemRefIndex = 0
                    };

                    newSignal.WorldObject.UpdateFlags(signal.WorldObject.FlagsSetBackfacing);
                    newSignal.WorldObject.HeadsSet.SetAll(false);

                    // loop through the list with headreferences, check this agains the list with backfacing heads
                    // use the TDBreference to find the actual head
                    bool backfacingHeads = false;

                    foreach (KeyValuePair<uint, uint> headRef in signal.WorldObject.HeadReference)
                    {
                        foreach (int headIndex in signal.WorldObject.Backfacing)
                        {
                            if (headRef.Value == headIndex)
                            {
                                for (int k = signal.SignalHeads.Count -1; k >= 0; k--)
                                {
                                    SignalHead head = signal.SignalHeads[k];

                                    // backfacing head found - add to new signal, set to remove from exising signal
                                    if (head.TDBIndex == headRef.Key)
                                    {
                                        signal.SignalHeads.RemoveAt(k);
                                        backfacingHeads = true;
                                        head.ResetMain(newSignal);
                                        newSignal.SignalHeads.Add(head);
                                    }
                                }
                            }

                            // update flags for available heads
                            newSignal.WorldObject.HeadsSet[headIndex] = true;
                            signal.WorldObject.HeadsSet[headIndex] = false;
                        }
                    }

                    // check if there were actually any backfacing signal heads
                    if (backfacingHeads)
                    {
                        // Check direction of heads to set correct direction for signal
                        if (signal.SignalHeads.Count > 0)
                        {
                            SignalItem oldItem = trackItems[signal.SignalHeads[0].TDBIndex] as SignalItem;
                            if (signal.TrackDirection != oldItem.Direction)
                            {
                                signal.TrackDirection = oldItem.Direction;
                                signal.TdbTraveller.ReverseDirection();                           // reverse //
                            }
                        }

                        SignalItem newItem = trackItems[newSignal.SignalHeads[0].TDBIndex] as SignalItem;
                        if (newSignal.TrackDirection != newItem.Direction)
                        {
                            newSignal.TrackDirection = newItem.Direction;
                            newSignal.TdbTraveller.ReverseDirection();                           // reverse //
                        }

                        // set correct trRefIndex for this signal, and set cross-reference for all backfacing trRef items
                        TrackVectorNode tvn = trackNodes[newSignal.TrackNode] as TrackVectorNode;
                        for (int i = 0; i < tvn.TrackItemIndices.Length; i++)
                        {
                            int tdbRef = tvn.TrackItemIndices[i];
                            if (trackItems[tdbRef] is SignalItem item)
                            {
                                foreach (SignalHead head in newSignal.SignalHeads)
                                {
                                    if (tdbRef == head.TDBIndex)
                                    {
                                        SignalItem sigItem = item;
                                        sigItem.SignalObject = newSignal.Index;
                                        newSignal.TrackItemRefIndex = i;

                                        // remove this key from the original signal //
                                        signal.WorldObject.HeadReference.Remove((uint)tdbRef);
                                    }
                                }
                            }
                        }

                        // reset cross-references for original signal (it may have been set for a backfacing head)
                        tvn = trackNodes[newSignal.TrackNode] as TrackVectorNode;
                        for (int i = 0; i < tvn.TrackItemIndices.Length; i++)
                        {
                            int tdbRef = tvn.TrackItemIndices[i];
                            if (trackItems[tdbRef] is SignalItem item)
                            {
                                foreach (SignalHead head in signal.SignalHeads)
                                {
                                    if (tdbRef == head.TDBIndex)
                                    {
                                        SignalItem sigItem = item;
                                        sigItem.SignalObject = signal.Index;
                                        signal.TrackItemRefIndex = i;

                                        // remove this key from the new signal //
                                        newSignal.WorldObject.HeadReference.Remove((uint)tdbRef);
                                    }
                                }
                            }
                        }

                        // add new signal to interim list
                        backfacingSignals.Add(newSignal);
                    }
                }
            }
            // remove existings signals heads remain
            Signals.RemoveAll(signal => signal.SignalHeads.Count <= 0);
            Signals.AddRange(backfacingSignals);
        }

        //================================================================================================//
        /// <summary>
        /// ScanSection : This method checks a section in the TDB for signals or speedposts
        /// </summary>
        private void ScanSection(TrackItem[] trackItems, TrackNode[] trackNodes, int index, TrackSectionsFile tsectiondat, TrackDatabaseFile tdbfile, Dictionary<int, int> platformList, Dictionary<uint, Signal> signalHeadList)
        {
            if (trackNodes[index] is TrackEndNode)
                return;

            //  Is it a vector node then it may contain objects.
            if (trackNodes[index] is TrackVectorNode tvn)
            {
                // Any objects ?
                for (int i = 0; i < tvn.TrackItemIndices.Length; i++)
                {
                    if (trackItems[tvn.TrackItemIndices[i]] != null)
                    {
                        int tdbRef = tvn.TrackItemIndices[i];

                        // Track Item is signal
                        if (trackItems[tdbRef] is SignalItem sigItem)
                        {
                            if (AddSignal(index, i, sigItem, tdbRef, tsectiondat, tdbfile, signalHeadList))
                            {
                                sigItem.SignalObject = Signals.Count - 1;
                            }
                            else
                            {
                                sigItem.SignalObject = -1;
                            }
                        }
                        // Track Item is speedpost - check if really limit
                        else if (trackItems[tdbRef] is SpeedPostItem speedItem)
                        {
                            if (speedItem.IsLimit)
                            {
                                AddSpeed(index, i, speedItem, tdbRef, tsectiondat, tdbfile);
                                speedItem.SignalObject = Signals.Count - 1;

                            }
                            else if (speedItem.IsMilePost)
                            {
                                speedItem.SignalObject = AddMilepost(speedItem, tdbRef);
                            }
                        }
                        else if (trackItems[tdbRef] is PlatformItem)
                        {
                            if (platformList.ContainsKey(tdbRef))
                            {
                                Trace.TraceInformation("Double reference to platform ID {0} in nodes {1} and {2}\n", tdbRef, platformList[tdbRef], index);
                            }
                            else
                            {
                                platformList.Add(tdbRef, index);
                            }
                        }
                        else if (trackItems[tdbRef] is SidingItem)
                        {
                            if (platformList.ContainsKey(tdbRef))
                            {
                                Trace.TraceInformation("Double reference to siding ID {0} in nodes {1} and {2}\n", tdbRef, platformList[tdbRef], index);
                            }
                            else
                            {
                                platformList.Add(tdbRef, index);
                            }
                        }
                    }
                }
            }
        }   //ScanSection 

        //================================================================================================//
        /// <summary>
        /// Merge Heads
        /// </summary>

        private void MergeHeads(ConcurrentBag<SignalWorldInfo> signalWorldList, Dictionary<uint, Signal> signalHeadList)
        {
            foreach (SignalWorldInfo signalWorldInfo in signalWorldList)
            {
                Signal MainSignal = null;

                if (signalWorldInfo.HeadReference.Count > 1)
                {

                    foreach (KeyValuePair<uint, uint> thisReference in signalWorldInfo.HeadReference)
                    {
                        if (signalHeadList.ContainsKey(thisReference.Key))
                        {
                            if (MainSignal == null)
                            {
                                MainSignal = signalHeadList[thisReference.Key];
                            }
                            else
                            {
                                Signal AddSignal = signalHeadList[thisReference.Key];
                                if (MainSignal.TrackNode != AddSignal.TrackNode)
                                {
                                    Trace.TraceWarning("Signal head {0} in different track node than signal head {1} of same signal", MainSignal.TrackItemIndex, thisReference.Key);
                                    MainSignal = null;
                                    break;
                                }
                                foreach (SignalHead thisHead in AddSignal.SignalHeads)
                                {
                                    MainSignal.SignalHeads.Add(thisHead);
                                    Signals[AddSignal.Index] = null;
                                }
                            }
                        }
                        else
                        {
                            Trace.TraceInformation("Signal found in Worldfile but not in TDB - TDB Index : {0}", thisReference.Key);
                            MainSignal = null;
                        }
                    }
                }
            }
        }

        //================================================================================================//
        /// <summary>
        /// This method adds a new Signal to the list
        /// </summary>
        private bool AddSignal(int trackNode, int nodeIndex, SignalItem sigItem, int tdbRef, TrackSectionsFile tsectiondat, TrackDatabaseFile tdbfile, Dictionary<uint, Signal> signalHeadList)
        {
            if (!(tdbfile.TrackDB.TrackNodes[trackNode] is TrackVectorNode tvn))
            {
                Trace.TraceInformation("Reference to invalid track node {0} for Signal {1}\n", trackNode, tdbRef);
                return false;
            }

            Traveller traveller = new Traveller(tsectiondat, tdbfile.TrackDB.TrackNodes, tvn, sigItem.Location, (Traveller.TravellerDirection)(1 - sigItem.Direction));

            Signal signal = new Signal(Signals.Count, traveller)
            {
                IsSignal = true, IsSpeedSignal = false, TrackDirection = sigItem.Direction, TrackNode = trackNode, TrackItemRefIndex = nodeIndex

            };
            signal.AddHead(nodeIndex, tdbRef, sigItem);

            if (signalHeadList.ContainsKey((uint)tdbRef))
            {
                Trace.TraceInformation("Invalid double TDBRef {0} in node {1}\n", tdbRef, trackNode);
                return false;
            }
            else
            {
                Signals.Add(signal);
                signalHeadList.Add((uint)tdbRef, signal);
            }
            return true;
        } // AddSignal


        //================================================================================================//
        /// <summary>
        /// This method adds a new Speedpost to the list
        /// </summary>
        private void AddSpeed(int trackNode, int nodeIndex, SpeedPostItem speedItem, int tdbRef, TrackSectionsFile tsectiondat, TrackDatabaseFile tdbfile)
        {
            Traveller traveller = new Traveller(tsectiondat, tdbfile.TrackDB.TrackNodes, tdbfile.TrackDB.TrackNodes[trackNode] as TrackVectorNode, speedItem.Location, Traveller.TravellerDirection.Backward);

            Signal signal = new Signal(Signals.Count, traveller)
            {
                IsSignal = false, IsSpeedSignal = false, TrackDirection = TrackDirection.Ahead, TrackNode = trackNode, TrackItemRefIndex = nodeIndex,
            };
            signal.AddHead(nodeIndex, tdbRef, speedItem);

            double delta_angle = signal.TdbTraveller.RotY - ((Math.PI / 2) - speedItem.Angle);
            float delta_float = MathHelper.WrapAngle((float)delta_angle);
            if (Math.Abs(delta_float) < (Math.PI / 2))
            {
                signal.TrackDirection = ((TrackDirection)(int)signal.TdbTraveller.Direction).Reverse();
            }
            else
            {
                signal.TrackDirection = (TrackDirection)(int)signal.TdbTraveller.Direction;
                signal.TdbTraveller.ReverseDirection();
            }
            Signals.Add(signal);
        } // AddSpeed

        //================================================================================================//
        /// <summary>
        /// This method adds a new Milepost to the list
        /// </summary>

        private int AddMilepost(SpeedPostItem speedItem, int tdbRef)
        {
            Milepost milepost = new Milepost((uint)tdbRef, speedItem.Distance);
            milepostList.Add(milepost);

            foundMileposts = milepostList.Count;
            return foundMileposts - 1;
        } // AddMilepost

        //================================================================================================//
        /// <summary>
        /// Add the sigcfg reference to each signal object.
        /// </summary>
        private void AddSignalConfiguration(SignalConfigurationFile signalConfig)
        {
            foreach (Signal signal in Signals)
            {
                if (signal.IsSignal)
                {
                    signal.SetSignalType(signalConfig);
                }
            }
        }//AddCFG

        //================================================================================================//
        /// <summary>
        /// Add info from signal world objects to signal
        /// </summary>
        private void AddWorldInfo(ConcurrentDictionary<uint, SignalWorldInfo> signalWorldLookup)
        {
            // loop through all signal and all heads
            foreach (Signal signal in Signals)
            {
                foreach (SignalHead head in signal.SignalHeads)
                {
                    // get reference using TDB index from head
                    if (signalWorldLookup.TryGetValue(Convert.ToUInt32(head.TDBIndex), out SignalWorldInfo result))
                        signal.WorldObject = result;
                }
            }

        }//AddWorldInfo

        //================================================================================================//
        /// <summary>
        /// FindByTrackItem : find required signalObj + signalHead
        /// </summary>
        public KeyValuePair<Signal, SignalHead>? FindByTrackItem(uint trackItem)
        {
            foreach (Signal signal in Signals)
                foreach (SignalHead head in signal.SignalHeads)
                    if ((trackDB.TrackNodes[signal.TrackNode] as TrackVectorNode).TrackItemIndices[head.TrackItemIndex] == (int)trackItem)
                        return new KeyValuePair<Signal, SignalHead>(signal, head);
            return null;
        }//FindByTrItem

        //================================================================================================//
        /// <summary>
        /// Count number of normal signal heads
        /// </summary>
        private void SetNumSignalHeads()
        {
            foreach (Signal signal in Signals)
            {
                signal.SetNumberSignalHeads();
            }
        }

        //================================================================================================//
        /// <summary>
        /// Find_Next_Object_InRoute : find next item along path of train - using Route List (only forward)
        /// Objects to search for : SpeedPost, Signal
        ///
        /// Usage :
        ///   always set : RouteList, RouteNodeIndex, distance along RouteNode, fnType
        ///
        ///   from train :
        ///     optional : maxdistance
        ///
        /// returned :
        ///   >= 0 : signal object reference
        ///   -1  : end of track 
        ///   -3  : no item within required distance
        ///   -5  : end of authority
        ///   -6  : end of (sub)route
        /// </summary>

        internal TrackCircuitSignalItem FindNextObjectInRoute(TrackCircuitPartialPathRoute routePath, int routeIndex, float routePosition, float maxDistance, SignalFunction signalType, Train.TrainRouted train)
        {
            if (null == routePath)
                throw new ArgumentNullException(nameof(routePath));

            SignalItemFindState locstate = SignalItemFindState.None;
            // local processing state     //

            int actRouteIndex = routeIndex;      // present node               //
            TrackCircuitRouteElement routeElement = routePath[actRouteIndex];
            TrackDirection actDirection = routeElement.Direction;
            TrackCircuitSection section = routeElement.TrackCircuitSection;
            float totalLength = 0;
            float lengthOffset = routePosition;

            Signal foundObject = null;

            //
            // loop through trackcircuits until :
            //  - end of track or route is found
            //  - end of authorization is found
            //  - required item is found
            //  - max distance is covered
            //

            while (locstate == SignalItemFindState.None)
            {

                // normal signal
                if (signalType == SignalFunction.Normal)
                {
                    if (section.EndSignals[actDirection] != null)
                    {
                        foundObject = section.EndSignals[actDirection];
                        totalLength += (section.Length - lengthOffset);
                        locstate = SignalItemFindState.Item;
                    }
                }
                // speedpost
                else if (signalType == SignalFunction.Speed)
                {
                    TrackCircuitSignalList speedpostList = section.CircuitItems.TrackCircuitSpeedPosts[actDirection];
                    locstate = SignalItemFindState.None;

                    for (int i = 0; i < speedpostList.Count && locstate == SignalItemFindState.None;
                             i++)
                    {
                        TrackCircuitSignalItem speedpost = speedpostList[i];
                        if (speedpost.SignalLocation > lengthOffset)
                        {
                            SpeedInfo speedInfo = speedpost.Signal.SignalSpeed(SignalFunction.Speed);

                            // set signal in list if there is no train or if signal has active speed
                            if (train == null || (speedInfo != null && (speedInfo.Flag || speedInfo.Reset ||
                                (train.Train.IsFreight && speedInfo.FreightSpeed != -1) || (!train.Train.IsFreight && speedInfo.PassengerSpeed != -1))))
                            {
                                locstate = SignalItemFindState.Item;
                                foundObject = speedpost.Signal;
                                totalLength += (speedpost.SignalLocation - lengthOffset);
                            }
                            // also set signal in list if it is a speed signal as state of speed signal may change
                            else if (speedpost.Signal.IsSpeedSignal)
                            {
                                locstate = SignalItemFindState.Item;
                                foundObject = speedpost.Signal;
                                totalLength += (speedpost.SignalLocation - lengthOffset);
                            }
                        }
                    }
                }
                // other fn_types
                else
                {
                    TrackCircuitSignalList signalList = section.CircuitItems.TrackCircuitSignals[actDirection][(int)signalType];
                    locstate = SignalItemFindState.None;

                    foreach (TrackCircuitSignalItem signal in signalList)
                    {
                        if (signal.SignalLocation > lengthOffset)
                        {
                            locstate = SignalItemFindState.Item;
                            foundObject = signal.Signal;
                            totalLength += (signal.SignalLocation - lengthOffset);
                            break;
                        }
                    }
                }

                // next section accessed via next route element
                if (locstate == SignalItemFindState.None)
                {
                    totalLength += (section.Length - lengthOffset);
                    lengthOffset = 0;

                    int setSection = section.ActivePins[(TrackDirection)routeElement.OutPin[Location.NearEnd], (Location)routeElement.OutPin[Location.FarEnd]].Link;
                    actRouteIndex++;

                    if (setSection < 0)
                    {
                        locstate = SignalItemFindState.EndOfAuthority;
                    }
                    else if (actRouteIndex >= routePath.Count)
                    {
                        locstate = SignalItemFindState.EndOfPath;
                    }
                    else if (maxDistance > 0 && totalLength > maxDistance)
                    {
                        locstate = SignalItemFindState.PassedMaximumDistance;
                    }
                    else
                    {
                        routeElement = routePath[actRouteIndex];
                        actDirection = routeElement.Direction;
                        section = routeElement.TrackCircuitSection;
                    }
                }
            }

            return foundObject != null ? new TrackCircuitSignalItem(foundObject, totalLength) : new TrackCircuitSignalItem(locstate);
        }

        //================================================================================================//
        /// <summary>
        /// GetNextObject_InRoute : find next item along path of train - using Route List (only forward)
        ///
        /// Usage :
        ///   always set : Train (may be null), RouteList, RouteNodeIndex, distance along RouteNode, fn_type
        ///
        ///   from train :
        ///     optional : maxdistance
        ///
        /// returned :
        ///   >= 0 : signal object reference
        ///   -1  : end of track 
        ///   -2  : passed signal at danger
        ///   -3  : no item within required distance
        ///   -5  : end of authority
        ///   -6  : end of (sub)route
        /// </summary>


        // call without position
        internal SignalItemInfo GetNextObjectInRoute(Train.TrainRouted train, TrackCircuitPartialPathRoute routePath,
                    int routeIndex, float routePosition, float maxDistance, SignalItemType requiredType)
        {
            if (train == null)
                throw new ArgumentNullException(nameof(train));

            return GetNextObjectInRoute(train, routePath, routeIndex, routePosition, maxDistance, requiredType, train.Train.PresentPosition[train.Direction]);
        }

        // call with position
        internal SignalItemInfo GetNextObjectInRoute(Train.TrainRouted train, TrackCircuitPartialPathRoute routePath,
                    int routeIndex, float routePosition, float maxDistance, SignalItemType requiredType, TrackCircuitPosition position)
        {
            if (position == null)
                throw new ArgumentNullException(nameof(position));

            TrackCircuitSignalItem foundItem = null;

            float signalDistance = -1f;
            float speedpostDistance = -1f;

            bool findSignal = requiredType == SignalItemType.Any || requiredType == SignalItemType.Signal;

            TrackCircuitPartialPathRoute usedRoute = routePath;

            // if routeIndex is not valid, build temp route from present position to first node or signal

            if (routeIndex < 0)
            {
                List<int> Sections = ScanRoute(train.Train, position.TrackCircuitSectionIndex, position.Offset, position.Direction,
                    true, 200f, false, true, true, false, true, false, false, true, false, train?.Train.IsFreight ?? false);

                TrackCircuitPartialPathRoute route = new TrackCircuitPartialPathRoute();
                int prevSection = -2;

                foreach (int sectionIndex in Sections)
                {
                    TrackCircuitRouteElement routeElement = new TrackCircuitRouteElement(TrackCircuitSection.TrackCircuitList[Math.Abs(sectionIndex)], 
                        sectionIndex > 0 ? TrackDirection.Ahead : TrackDirection.Reverse, prevSection);
                    route.Add(routeElement);
                    prevSection = Math.Abs(sectionIndex);
                }
                usedRoute = route;
                routeIndex = 0;
            }

            TrackCircuitSignalItem nextSignal = FindNextObjectInRoute(usedRoute, routeIndex, routePosition, maxDistance, SignalFunction.Normal, train);

            // always find signal to check for signal at danger
            SignalItemFindState signalState = nextSignal.SignalState;
            if (nextSignal.SignalState == SignalItemFindState.Item)
            {
                signalDistance = nextSignal.SignalLocation;
                Signal foundSignal = nextSignal.Signal;
                if (foundSignal.SignalLR(SignalFunction.Normal) == SignalAspectState.Stop)
                {
                    signalState = SignalItemFindState.PassedDanger;
                }
                else if (train != null && foundSignal.EnabledTrain != train)
                {
                    signalState = SignalItemFindState.PassedDanger;
                    nextSignal.SignalState = signalState;  // do not return OBJECT_FOUND - signal is not valid
                }
            }

            // look for speedpost only if required
            if (requiredType == SignalItemType.Any || requiredType == SignalItemType.SpeedLimit)
            {
                TrackCircuitSignalItem nextSpeedpost = FindNextObjectInRoute(usedRoute, routeIndex, routePosition, maxDistance, SignalFunction.Speed, train);

                if (nextSpeedpost.SignalState == SignalItemFindState.Item)
                {
                    speedpostDistance = nextSpeedpost.SignalLocation;
                }

                if (signalDistance > 0 && speedpostDistance > 0)
                {
                    if (signalDistance < speedpostDistance)
                    {
                        if (findSignal)
                        {
                            foundItem = nextSignal;
                        }
                        else
                        {
                            foundItem = nextSpeedpost;
                            if (signalState == SignalItemFindState.PassedDanger)
                            {
                                foundItem.SignalState = signalState;
                            }
                        }
                    }
                    else
                    {
                        foundItem = nextSpeedpost;
                    }
                }
                else if (signalDistance > 0)
                {
                    foundItem = nextSignal;
                }
                else if (speedpostDistance > 0)
                {
                    foundItem = nextSpeedpost;
                }
            }
            else if (findSignal)
            {
                foundItem = nextSignal;
            }

            SignalItemInfo returnItem;
            if (foundItem == null)
            {
                returnItem = new SignalItemInfo(SignalItemFindState.None);
            }
            else if (foundItem.SignalState != SignalItemFindState.Item)
            {
                returnItem = new SignalItemInfo(foundItem.SignalState);
            }
            else
            {
                returnItem = new SignalItemInfo(foundItem.Signal, foundItem.SignalLocation);
            }

            return returnItem;
        }

        public (Signal Signal, float Distance) GetSignalItemInfo(TrackCircuitCrossReferences trackCircuitXRefList, float offset, int direction, float routeLength)
        {
            if (null == trackCircuitXRefList)
                throw new ArgumentNullException(nameof(trackCircuitXRefList));

            TrackCircuitPosition position = new TrackCircuitPosition();
            position.SetPosition(trackCircuitXRefList, offset, (TrackDirection)direction);
            TrackCircuitPartialPathRoute route = BuildTempRoute(null, position.TrackCircuitSectionIndex, position.Offset, position.Direction, routeLength, true, false, false);
            SignalItemInfo signalInfo = GetNextObjectInRoute(null, route, 0, position.Offset, -1, SignalItemType.Signal, position);

            return (signalInfo?.SignalDetails, signalInfo.DistanceFound);
        }

        //================================================================================================//
        /// <summary>
        /// Gets the Track Monitor Aspect from the MSTS aspect (for the TCS) 
        /// </summary>
        public static TrackMonitorSignalAspect TranslateToTCSAspect(SignalAspectState state)
        {
            switch (state)
            {
                case SignalAspectState.Stop:
                    return TrackMonitorSignalAspect.Stop;
                case SignalAspectState.Stop_And_Proceed:
                    return TrackMonitorSignalAspect.StopAndProceed;
                case SignalAspectState.Restricting:
                    return TrackMonitorSignalAspect.Restricted;
                case SignalAspectState.Approach_1:
                    return TrackMonitorSignalAspect.Approach1;
                case SignalAspectState.Approach_2:
                    return TrackMonitorSignalAspect.Approach2;
                case SignalAspectState.Approach_3:
                    return TrackMonitorSignalAspect.Approach3;
                case SignalAspectState.Clear_1:
                    return TrackMonitorSignalAspect.Clear1;
                case SignalAspectState.Clear_2:
                    return TrackMonitorSignalAspect.Clear2;
                default:
                    return TrackMonitorSignalAspect.None;
            }
        } // GetMonitorAspect

        //================================================================================================//
        /// <summary>
        /// Create Track Circuits
        /// <summary>
        private void CreateTrackCircuits(TrackItem[] trackItems, TrackNode[] trackNodes, TrackSectionsFile tsectiondat)
        {

            // Create dummy element as first to keep indexes equal
            TrackCircuitSection.TrackCircuitList.Add(new TrackCircuitSection(this));

            // Create new default elements from existing base
            for (int i = 1; i < trackNodes.Length; i++)
            {
                TrackNode trackNode = trackNodes[i];
                TrackCircuitSection defaultSection =
                    new TrackCircuitSection(trackNode, i, tsectiondat);
                TrackCircuitSection.TrackCircuitList.Add(defaultSection);
            }

            Dictionary<int, CrossOverInfo> crossoverList = new Dictionary<int, CrossOverInfo>();


            // loop through original default elements
            // collect track items
            int originalNodes = TrackCircuitSection.TrackCircuitList.Count;
            for (int i = 1; i < originalNodes; i++)
            {
                ProcessNodes(i, trackItems, trackNodes, tsectiondat, crossoverList);
            }

            // Delete MilepostList as it is no more needed
            foundMileposts = -1;
            milepostList = null;

            // loop through original default elements
            // split on crossover items
            originalNodes = TrackCircuitSection.TrackCircuitList.Count;
            int nextNode = originalNodes;
            foreach (KeyValuePair<int, CrossOverInfo> crossOver in crossoverList)
            {
                nextNode = SplitNodesCrossover(crossOver.Value, tsectiondat, nextNode);
            }

            // loop through original default elements
            // split on normal signals
            originalNodes = TrackCircuitSection.TrackCircuitList.Count;
            nextNode = originalNodes;

            for (int i = 1; i < originalNodes; i++)
            {
                nextNode = SplitNodesSignals(i, nextNode);
            }

            // loop through all items
            // perform link test
            originalNodes = TrackCircuitSection.TrackCircuitList.Count;
            nextNode = originalNodes;
            for (int i = 1; i < originalNodes; i++)
            {
                nextNode = PerformLinkTest(i, nextNode);
            }

            // loop through all items
            // reset active links
            // set fixed active links for none-junction links
            // set trailing junction flags
            originalNodes = TrackCircuitSection.TrackCircuitList.Count;
            for (int i = 1; i < originalNodes; i++)
            {
                SetActivePins(i);
            }

            // Set cross-reference
            for (int i = 1; i < originalNodes; i++)
            {
                SetCrossReference(i, trackNodes);
            }
            for (int i = 1; i < originalNodes; i++)
            {
                SetCrossReferenceCrossOver(i, trackNodes);
            }

            // Set cross-reference for signals
            foreach (TrackCircuitSection section in TrackCircuitSection.TrackCircuitList)
            {
                Signal.SetSignalCrossReference(section);
            }

            // Set default next signal and fixed route information
            foreach (Signal signal in Signals)
            {
                signal.SetSignalDefaultNextSignal();
            }
        }

        //================================================================================================//
        /// <summary>
        /// ProcessNodes
        /// </summary>
        private void ProcessNodes(int nodeIndex, TrackItem[] trackItems, TrackNode[] trackNodes, TrackSectionsFile tsectiondat, Dictionary<int, CrossOverInfo> crossoverList)
        {

            // Check if original tracknode had trackitems
            TrackCircuitSection circuit = TrackCircuitSection.TrackCircuitList[nodeIndex];

            if (trackNodes[circuit.OriginalIndex] is TrackVectorNode tvn && tvn.TrackItemIndices.Length > 0)
            {
                // Create TDBtraveller at start of section to calculate distances
                TrackVectorSection firstSection = tvn.TrackVectorSections[0];
                Traveller traveller = new Traveller(tsectiondat, trackNodes, tvn, firstSection.Location, Traveller.TravellerDirection.Forward);


                // Process all items (do not split yet)
                float[] lastDistance = new float[2] { -1.0f, -1.0f };
                for (int i = 0; i < tvn.TrackItemIndices.Length; i++)
                {
                    int tdbRef = tvn.TrackItemIndices[i];
                    if (trackItems[tdbRef] != null)
                    {
                        lastDistance = InsertNode(circuit, trackItems[tdbRef], traveller, trackNodes, lastDistance, crossoverList);
                    }
                }
            }
        }

        //================================================================================================//
        /// <summary>
        /// InsertNode
        /// </summary>
        private float[] InsertNode(TrackCircuitSection circuit, TrackItem trackItem, Traveller traveller, TrackNode[] trackNodes, float[] lastDistance, Dictionary<int, CrossOverInfo> crossoverList)
        {

            float[] newLastDistance = new float[2] { lastDistance[0], lastDistance[1] };

            // Insert signal
            if (trackItem is SignalItem signalItem)
            {
                if (signalItem.SignalObject >= 0)
                {
                    Signal signal = Signals[signalItem.SignalObject];

                    float signalDistance = signal.DistanceTo(traveller);
                    if (signal.TrackDirection == TrackDirection.Reverse)
                    {
                        signalDistance = circuit.Length - signalDistance;
                    }

                    for (int i = 0; i < OrtsSignalTypeCount; i++)
                    {
                        if (signal.OrtsSignalType(i))
                        {
                            TrackCircuitSignalItem trackCircuitItem = new TrackCircuitSignalItem(signal, signalDistance);

                            TrackDirection direction = signal.TrackDirection.Reverse();
                            TrackCircuitSignalList signalList = circuit.CircuitItems.TrackCircuitSignals[direction][i];

                            // if signal is SPEED type, insert in speedpost list
                            if (i == (int)SignalFunction.Speed)
                            {
                                signalList = circuit.CircuitItems.TrackCircuitSpeedPosts[direction];
                            }

                            if (!signalList.Where(item => item.Signal == signal).Any())
                            {
                                if (direction == TrackDirection.Ahead)
                                {
                                    signalList.Insert(0, trackCircuitItem);
                                }
                                else
                                {
                                    signalList.Add(trackCircuitItem);
                                }
                            }
                        }
                    }
                    newLastDistance[(int)signal.TrackDirection] = signalDistance;
                }
            }
            // Insert speedpost
            else if (trackItem is SpeedPostItem speedItem)
            {
                if (speedItem.SignalObject >= 0)
                {
                    if (!speedItem.IsMilePost)
                    {
                        Signal speedpost = Signals[speedItem.SignalObject];
                        float speedpostDistance = speedpost.DistanceTo(traveller);
                        if (speedpost.TrackDirection == TrackDirection.Reverse)
                        {
                            speedpostDistance = circuit.Length - speedpostDistance;
                        }

                        if (speedpostDistance == lastDistance[(int)speedpost.TrackDirection]) // if at same position as last item
                        {
                            speedpostDistance += 0.001f;  // shift 1 mm so it will be found
                        }

                        TrackCircuitSignalItem trackCircuitItem = new TrackCircuitSignalItem(speedpost, speedpostDistance);

                        TrackDirection direction = speedpost.TrackDirection.Reverse();
                        TrackCircuitSignalList signalList = circuit.CircuitItems.TrackCircuitSpeedPosts[direction];

                        if (direction == TrackDirection.Ahead)
                        {
                            signalList.Insert(0, trackCircuitItem);
                        }
                        else
                        {
                            signalList.Add(trackCircuitItem);
                        }

                        newLastDistance[(int)speedpost.TrackDirection] = speedpostDistance;
                    }
                    // Milepost
                    else if (speedItem.IsMilePost)
                    {
                        Milepost milepost = milepostList[speedItem.SignalObject];
                        TrackItem milepostTrItem = Simulator.Instance.TDB.TrackDB.TrackItems[milepost.TrackItemId];
                        float milepostDistance = traveller.DistanceTo(milepostTrItem.Location);

                        TrackCircuitMilepost trackCircuitItem = new TrackCircuitMilepost(milepost, milepostDistance, circuit.Length - milepostDistance);

                        circuit.CircuitItems.TrackCircuitMileposts.Add(trackCircuitItem);
                    }
                }
            }
            // Insert crossover in special crossover list
            else if (trackItem is CrossoverItem crossOver)
            {
                float cdist = traveller.DistanceTo(trackNodes[circuit.OriginalIndex], crossOver.Location);

                int crossOverId = (int)crossOver.TrackItemId;
                int crossId = (int)crossOver.TrackNode;

                // search in Dictionary for combined item //

                if (crossoverList.ContainsKey(crossId))
                {
                    crossoverList[crossId].Update(cdist, circuit.Index);
                }
                else
                {
                    crossoverList.Add(crossOverId, new CrossOverInfo(cdist, 0f, circuit.Index, -1, crossOverId, crossId, crossOver.ShapeId));
                }
            }

            return newLastDistance;
        }

        //================================================================================================//
        /// <summary>
        /// Split on Signals
        /// </summary>
        private int SplitNodesSignals(int node, int nextNode)
        {
            int index = node;
            List<int> addIndex = new List<int>();

            // in direction 0, check original item only
            // keep list of added items
            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[index];

            int newIndex;
            if (section.CircuitType == TrackCircuitType.Normal)
            {
                addIndex.Add(node);

                List<TrackCircuitSignalItem> sectionSignals = section.CircuitItems.TrackCircuitSignals[0][(int)SignalFunction.Normal];

                while (sectionSignals.Count > 0)
                {
                    TrackCircuitSignalItem signal = sectionSignals[0];
                    sectionSignals.RemoveAt(0);

                    newIndex = nextNode;
                    nextNode++;

                    TrackCircuitSection.SplitSection(index, newIndex, section.Length - signal.SignalLocation);
                    TrackCircuitSection newSection = TrackCircuitSection.TrackCircuitList[newIndex];
                    newSection.EndSignals[TrackDirection.Ahead] = signal.Signal;
                    section = TrackCircuitSection.TrackCircuitList[index];
                    addIndex.Add(newIndex);

                    // restore list (link is lost as item is replaced)
                    sectionSignals = section.CircuitItems.TrackCircuitSignals[0][(int)SignalFunction.Normal];
                }
            }

            //
            // in direction Heading.Reverse, check original item and all added items
            //

            foreach (int actIndex in addIndex)
            {
                index = actIndex;

                while (index > 0)
                {
                    section = TrackCircuitSection.TrackCircuitList[index];

                    newIndex = -1;
                    if (section.CircuitType == TrackCircuitType.Normal)
                    {

                        List<TrackCircuitSignalItem> sectionSignals = section.CircuitItems.TrackCircuitSignals[TrackDirection.Reverse][(int)SignalFunction.Normal];

                        if (sectionSignals.Count > 0)
                        {
                            TrackCircuitSignalItem signal = sectionSignals[0];
                            sectionSignals.RemoveAt(0);

                            newIndex = nextNode;
                            nextNode++;

                            TrackCircuitSection.SplitSection(index, newIndex, signal.SignalLocation);
                            TrackCircuitSection newSection = TrackCircuitSection.TrackCircuitList[newIndex];
                            newSection.EndSignals[TrackDirection.Ahead] = null;
                            section = TrackCircuitSection.TrackCircuitList[index];
                            section.EndSignals[TrackDirection.Reverse] = signal.Signal;

                            // restore list (link is lost as item is replaced)
                            sectionSignals = section.CircuitItems.TrackCircuitSignals[TrackDirection.Reverse][(int)SignalFunction.Normal];
                        }
                    }
                    index = section.CircuitItems.TrackCircuitSignals[TrackDirection.Reverse][(int)SignalFunction.Normal].Count > 0 ? index : newIndex;
                }
            }

            return nextNode;
        }

        //================================================================================================//
        /// <summary>
        /// Split CrossOvers
        /// </summary>
        private int SplitNodesCrossover(CrossOverInfo crossOver, TrackSectionsFile tsectiondat, int nextNode)
        {
            bool processCrossOver = true;
            int sectionIndex0 = 0;
            int sectionIndex1 = 0;

            if (crossOver.Details[Location.NearEnd].SectionIndex < 0 || crossOver.Details[Location.FarEnd].SectionIndex < 0)
            {
                Trace.TraceWarning($"Incomplete crossover : indices {crossOver.Details[Location.NearEnd].ItemIndex} and {crossOver.Details[Location.FarEnd].ItemIndex}");
                processCrossOver = false;
            }
            if (crossOver.Details[Location.NearEnd].SectionIndex == crossOver.Details[Location.FarEnd].SectionIndex)
            {
                Trace.TraceWarning($"Invalid crossover : indices {crossOver.Details[Location.NearEnd].ItemIndex} and {crossOver.Details[Location.FarEnd].ItemIndex} : equal section : {crossOver.Details[Location.NearEnd].SectionIndex}");
                processCrossOver = false;
            }

            if (processCrossOver)
            {
                sectionIndex0 = GetCrossOverSectionIndex(crossOver.Details[Location.NearEnd]);
                sectionIndex1 = GetCrossOverSectionIndex(crossOver.Details[Location.FarEnd]);

                if (sectionIndex0 < 0 || sectionIndex1 < 0)
                {
                    processCrossOver = false;
                }
            }

            if (processCrossOver)
            {
                int newSection0 = nextNode;
                nextNode++;
                int newSection1 = nextNode;
                nextNode++;
                int jnSection = nextNode;
                nextNode++;

                TrackCircuitSection.SplitSection(sectionIndex0, newSection0, crossOver.Details[Location.NearEnd].Position);
                TrackCircuitSection.SplitSection(sectionIndex1, newSection1, crossOver.Details[Location.FarEnd].Position);

                TrackCircuitSection.AddCrossoverJunction(sectionIndex0, newSection0, sectionIndex1, newSection1, jnSection, crossOver, tsectiondat);
            }

            return nextNode;
        }

        //================================================================================================//
        /// <summary>
        /// Get cross-over section index
        /// </summary>
        private int GetCrossOverSectionIndex(CrossOverInfo.Detail crossOver)
        {
            int sectionIndex = crossOver.SectionIndex;
            float position = crossOver.Position;
            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[sectionIndex];

            while (position > 0 && position > section.Length)
            // while (position > 0 && position > section.Length && section.OriginalIndex == firstSectionOriginalIndex)
            {
                int prevSection = sectionIndex;
                position -= section.Length;
                crossOver.Position = position;
                sectionIndex = section.Pins[TrackDirection.Reverse, Location.NearEnd].Link;

                if (sectionIndex > 0)
                {
                    section = TrackCircuitSection.TrackCircuitList[sectionIndex];
                    if (section.CircuitType == TrackCircuitType.Crossover)
                    {
                        if (section.Pins[TrackDirection.Ahead, Location.NearEnd].Link == prevSection)
                        {
                            sectionIndex = section.Pins[TrackDirection.Reverse, Location.NearEnd].Link;
                        }
                        else
                        {
                            sectionIndex = section.Pins[TrackDirection.Reverse, Location.FarEnd].Link;
                        }
                        section = TrackCircuitSection.TrackCircuitList[sectionIndex];
                    }
                }
                else
                {
                    position = -1;  // no position found //
                }
            }

            if (position < 0)
            {
                Trace.TraceWarning($"Cannot locate CrossOver {crossOver.ItemIndex} in Section {crossOver.SectionIndex}");
                sectionIndex = -1;
            }

            return sectionIndex;
        }

        //================================================================================================//
        /// <summary>
        /// Check pin links
        /// </summary>
        private int PerformLinkTest(int node, int nextNode)
        {

            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[node];

            foreach (TrackDirection direction in EnumExtension.GetValues<TrackDirection>())
            {
                foreach (Location pinLocation in EnumExtension.GetValues<Location>())
                {
                    int linkedNode = section.Pins[direction, pinLocation].Link;
                    TrackDirection linkedDirection = section.Pins[direction, pinLocation].Direction.Reverse();

                    if (linkedNode > 0)
                    {
                        TrackCircuitSection linkedSection = TrackCircuitSection.TrackCircuitList[linkedNode];

                        bool linkfound = false;
                        bool doublelink = false;
                        int doublenode = -1;

                        foreach (Location linkedPin in EnumExtension.GetValues<Location>())
                        {
                            if (linkedSection.Pins[linkedDirection, linkedPin].Link == node)
                            {
                                linkfound = true;
                                if (linkedSection.ActivePins[linkedDirection, linkedPin].Link == -1)
                                {
                                    linkedSection.ActivePins[linkedDirection, linkedPin] = linkedSection.ActivePins[linkedDirection, linkedPin].FromLink(node);
                                }
                                else
                                {
                                    doublelink = true;
                                    doublenode = linkedSection.ActivePins[linkedDirection, linkedPin].Link;
                                }
                            }
                        }

                        if (!linkfound)
                        {
                            Trace.TraceWarning("Ignored invalid track node {0} pin [{1},{2}] link to track node {3}", node, direction, pinLocation, linkedNode);
                            int endNode = nextNode;
                            nextNode++;
                            TrackCircuitSection.InsertEndNode(node, direction, pinLocation, endNode);
                        }

                        if (doublelink)
                        {
                            Trace.TraceWarning("Ignored invalid track node {0} pin [{1},{2}] link to track node {3}; already linked to track node {4}", node, direction, pinLocation, linkedNode, doublenode);
                            int endNode = nextNode;
                            nextNode++;
                            TrackCircuitSection.InsertEndNode(node, direction, pinLocation, endNode);
                        }
                    }
                    else if (linkedNode == 0)
                    {
                        Trace.TraceWarning("Ignored invalid track node {0} pin [{1},{2}] link to track node {3}", node, direction, pinLocation, linkedNode);
                        int endNode = nextNode;
                        nextNode++;
                        TrackCircuitSection.InsertEndNode(node, direction, pinLocation, endNode);
                    }
                }
            }

            return nextNode;
        }

        //================================================================================================//
        /// <summary>
        /// set active pins for non-junction links
        /// </summary>
        private void SetActivePins(int node)
        {
            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[node];

            foreach (TrackDirection direction in EnumExtension.GetValues<TrackDirection>())
            {
                foreach (Location pinLocation in EnumExtension.GetValues<Location>())
                {
                    if (section.Pins[direction, pinLocation].Link > 0)
                    {
                        TrackCircuitSection nextSection;
                        if (section.CircuitType == TrackCircuitType.Junction)
                        {
                            nextSection = TrackCircuitSection.TrackCircuitList[section.Pins[direction, pinLocation].Link];

                            if (section.Pins[direction, Location.FarEnd].Link > 0)    // Junction end
                            {
                                section.ActivePins[direction, pinLocation] = section.Pins[direction, pinLocation].FromLink(-1);
                            }
                            else
                            {
                                section.ActivePins[direction, pinLocation] = section.Pins[direction, pinLocation];
                            }
                        }
                        else if (section.CircuitType == TrackCircuitType.Crossover)
                        {
                            nextSection = TrackCircuitSection.TrackCircuitList[section.Pins[direction, pinLocation].Link];

                            section.ActivePins[direction, pinLocation] = section.Pins[direction, pinLocation].FromLink(-1);
                        }
                        else
                        {
                            nextSection = TrackCircuitSection.TrackCircuitList[section.Pins[direction, pinLocation].Link];

                            section.ActivePins[direction, pinLocation] = section.Pins[direction, pinLocation];
                        }

                        if (nextSection?.CircuitType == TrackCircuitType.Crossover)
                        {
                            section.ActivePins[direction, pinLocation] = section.ActivePins[direction, pinLocation].FromLink(-1);
                        }
                        else if (nextSection?.CircuitType == TrackCircuitType.Junction)
                        {
                            TrackDirection nextDirection = section.Pins[direction, pinLocation].Direction.Reverse();
                            if (nextSection.Pins[nextDirection, Location.FarEnd].Link > 0)
                            {
                                section.ActivePins[direction, pinLocation] = section.ActivePins[direction, pinLocation].FromLink(-1);
                            }
                        }
                    }
                }
            }
        }

        //================================================================================================//
        /// <summary>
        /// set cross-reference to tracknodes
        /// </summary>
        private void SetCrossReference(int node, TrackNode[] trackNodes)
        {
            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[node];
            if (section.OriginalIndex > 0 && section.CircuitType != TrackCircuitType.Crossover)
            {
                TrackNode trackNode = trackNodes[section.OriginalIndex];
                float offset0 = section.OffsetLength[Location.NearEnd];
                float offset1 = section.OffsetLength[Location.FarEnd];

                TrackCircuitSectionCrossReference newReference = new TrackCircuitSectionCrossReference(section.Index, section.Length, section.OffsetLength.ToArray());

                bool inserted = false;

                TrackCircuitCrossReferences crossReference = trackNode.TrackCircuitCrossReferences;
                for (int i = 0; i < crossReference.Count && !inserted; i++)
                {
                    TrackCircuitSectionCrossReference reference = crossReference[i];
                    if (offset0 < reference.OffsetLength[0])
                    {
                        crossReference.Insert(i, newReference);
                        inserted = true;
                    }
                    else if (offset1 > reference.OffsetLength[1])
                    {
                        crossReference.Insert(i, newReference);
                        inserted = true;
                    }
                }

                if (!inserted)
                {
                    trackNode.TrackCircuitCrossReferences.Add(newReference);
                }
            }
        }

        //================================================================================================//
        /// <summary>
        /// set cross-reference to tracknodes for CrossOver items
        /// </summary>
        private void SetCrossReferenceCrossOver(int node, TrackNode[] trackNodes)
        {
            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[node];
            if (section.OriginalIndex > 0 && section.CircuitType == TrackCircuitType.Crossover)
            {
                foreach (Location pinLocation in EnumExtension.GetValues<Location>())
                {
                    int prevIndex = section.Pins[TrackDirection.Ahead, pinLocation].Link;
                    TrackCircuitSection prevSection = TrackCircuitSection.TrackCircuitList[prevIndex];

                    TrackCircuitSectionCrossReference newReference = new TrackCircuitSectionCrossReference(section.Index, section.Length, section.OffsetLength.ToArray());
                    TrackNode trackNode = trackNodes[prevSection.OriginalIndex];
                    TrackCircuitCrossReferences crossReference = trackNode.TrackCircuitCrossReferences;

                    bool inserted = false;
                    for (int i = 0; i < crossReference.Count && !inserted; i++)
                    {
                        TrackCircuitSectionCrossReference reference = crossReference[i];
                        if (reference.Index == prevIndex)
                        {
                            newReference.OffsetLength[0] = reference.OffsetLength[0];
                            newReference.OffsetLength[1] = reference.OffsetLength[1] + reference.Length;
                            crossReference.Insert(i, newReference);
                            inserted = true;
                        }
                    }

                    if (!inserted)
                    {
                        Trace.TraceWarning($"ERROR : cannot find XRef for leading track to crossover {node}");
                    }
                }
            }
        }

        //================================================================================================//
        /// <summary>
        /// Set physical switch
        /// </summary>
        public void SetSwitch(int nodeIndex, int switchPos, TrackCircuitSection section)
        {
            if (null == section)
                throw new ArgumentNullException(nameof(section));

            if (MPManager.NoAutoSwitch())
                return;
            TrackJunctionNode node = trackDB.TrackNodes[nodeIndex] as TrackJunctionNode;
            node.SelectedRoute = switchPos;
            section.JunctionLastRoute = switchPos;

            // update any linked signals
            foreach (int i in section.LinkedSignals ?? Enumerable.Empty<int>())
            {
                Signals[i].Update();
            }
        }

        //================================================================================================//
        /// <summary>
        /// Node control track clearance update request
        /// </summary>
        public void RequestClearNode(Train.TrainRouted train, TrackCircuitPartialPathRoute routePart)
        {
            if (null == train)
                throw new ArgumentNullException(nameof(train));
            if (null == routePart)
                throw new ArgumentNullException(nameof(routePart));

            TrackCircuitRouteElement routeElement = null;
            List<int> sectionsInRoute = new List<int>();

            float clearedDistanceM = 0.0f;
            EndAuthorityType endAuthority = EndAuthorityType.NoPathReserved;
            float maxDistance = train.Train.MaxDistanceCheckedAhead;

            int lastReserved = train.Train.LastReservedSection[train.TrainRouteDirectionIndex];
            int endListIndex = -1;

            bool furthestRouteCleared = false;

            TrackCircuitPartialPathRoute subPathRoute = new TrackCircuitPartialPathRoute(train.Train.ValidRoute[train.TrainRouteDirectionIndex]);
            TrackCircuitPosition position = new TrackCircuitPosition(train.Train.PresentPosition[train.Direction]);

            // for loop detection, set occupied sections in sectionsInRoute list - but remove present position
            foreach (TrackCircuitSection occSection in train.Train.OccupiedTrack)
            {
                sectionsInRoute.Add(occSection.Index);
            }

            // correct for invalid combination of present position and occupied sections
            if (sectionsInRoute.Count > 0 && position.TrackCircuitSectionIndex != sectionsInRoute.First() && position.TrackCircuitSectionIndex != sectionsInRoute.Last())
            {
                if (train.Train.PresentPosition[Direction.Backward].TrackCircuitSectionIndex == sectionsInRoute.First())
                {
                    for (int i = sectionsInRoute.Count - 1; i >= 0; i--)
                    {
                        if (sectionsInRoute[i] == position.TrackCircuitSectionIndex)
                        {
                            break;
                        }
                        else
                        {
                            sectionsInRoute.RemoveAt(i);
                        }
                    }
                }
                else if (train.Train.PresentPosition[Direction.Backward].TrackCircuitSectionIndex == sectionsInRoute.Last())
                {
                    for (int i = 0; i < sectionsInRoute.Count; i++)
                    {
                        if (sectionsInRoute[i] == position.TrackCircuitSectionIndex)
                        {
                            break;
                        }
                        else
                        {
                            sectionsInRoute.RemoveAt(i);
                        }
                    }
                }
            }

            sectionsInRoute.Remove(position.TrackCircuitSectionIndex);

            // check if last reserved on present route
            if (lastReserved > 0)
            {
                endListIndex = subPathRoute.GetRouteIndex(lastReserved, position.RouteListIndex);

                // check if backward in route - if so, route is valid and obstacle is in present section
                if (endListIndex < 0)
                {
                    int prevListIndex = -1;
                    for (int i = position.RouteListIndex; i >= 0 && prevListIndex < 0; i--)
                    {
                        routeElement = subPathRoute[i];
                        if (routeElement.TrackCircuitSection.Index == lastReserved)
                        {
                            prevListIndex = i;
                        }
                    }

                    if (prevListIndex < 0)     // section is really off route - perform request from present position
                    {
                        BreakDownRoute(position.TrackCircuitSectionIndex, train);
                    }
                }
            }

            int routeIndex;
            // if section is (still) set, check if this is at maximum distance
            if (endListIndex >= 0)
            {
                routeIndex = endListIndex;
                clearedDistanceM = train.Train.GetDistanceToTrain(lastReserved, 0.0f);

                if (clearedDistanceM > maxDistance)
                {
                    endAuthority = EndAuthorityType.MaxDistance;
                    furthestRouteCleared = true;
                }
                else
                {
                    for (int i = position.RouteListIndex + 1; i < routeIndex; i++)
                    {
                        sectionsInRoute.Add(subPathRoute[i].TrackCircuitSection.Index);
                    }
                }
            }
            else
            {
                routeIndex = position.RouteListIndex;   // obstacle is in present section
            }

            if (routeIndex < 0)
                return;//by JTang

            int lastRouteIndex = routeIndex;
            float offset = 0.0f;
            if (routeIndex == position.RouteListIndex)
            {
                offset = position.Offset;
            }

            TrackCircuitSection section;
            // if authority type is loop and loop section is still occupied by train, no need for any checks
            if (train.Train.LoopSection >= 0)
            {
                section = TrackCircuitSection.TrackCircuitList[train.Train.LoopSection];

                // test if train is really occupying this section
                TrackCircuitPartialPathRoute tempRoute = BuildTempRoute(train.Train, train.Train.PresentPosition[Direction.Backward].TrackCircuitSectionIndex, train.Train.PresentPosition[Direction.Backward].Offset,
                    train.Train.PresentPosition[Direction.Backward].Direction, train.Train.Length, true, true, false);

                if (tempRoute.GetRouteIndex(section.Index, 0) < 0)
                {
                    train.Train.OccupiedTrack.Clear();
                    foreach (TrackCircuitRouteElement element in tempRoute)
                    {
                        train.Train.OccupiedTrack.Add(element.TrackCircuitSection);
                    }
                }

                if (section.CircuitState.OccupiedByThisTrain(train.Train) ||
                    (section.CircuitState.TrainReserved != null && section.CircuitState.TrainReserved.Train == train.Train))
                {
                    furthestRouteCleared = true;
                    endAuthority = EndAuthorityType.Loop;
                }
                else
                {
                    // update trains ValidRoute to avoid continuation at wrong entry
                    int rearIndex = train.Train.PresentPosition[Direction.Backward].RouteListIndex;
                    int nextIndex = routePart.GetRouteIndex(train.Train.LoopSection, rearIndex);
                    int firstIndex = routePart.GetRouteIndex(train.Train.LoopSection, 0);

                    if (firstIndex != nextIndex)
                    {
                        for (int i = 0; i < rearIndex; i++)
                        {
                            train.Train.ValidRoute[train.TrainRouteDirectionIndex][i].Invalidate(); // invalidate route upto loop point
                        }
                        routePart = train.Train.ValidRoute[train.TrainRouteDirectionIndex];
                    }

                    train.Train.LoopSection = -1;
                }
            }

            // check if present clearance is beyond required maximum distance
            // try to clear further ahead if required

            if (!furthestRouteCleared)
            {

                // check if train ahead still in last available section
                bool routeAvailable = true;
                section = routePart[routeIndex].TrackCircuitSection;

                float posOffset = position.Offset;
                TrackDirection posDirection = position.Direction;

                if (routeIndex > position.RouteListIndex)
                {
                    posOffset = 0;
                    posDirection = routePart[routeIndex].Direction;
                }

                Dictionary<Train, float> trainAhead = section.TestTrainAhead(train.Train, posOffset, posDirection);

                if (trainAhead.Count > 0)
                {
                    routeAvailable = false;

                    // if section is junction or crossover, use next section as last, otherwise use this section as last
                    if (section.CircuitType != TrackCircuitType.Junction && section.CircuitType != TrackCircuitType.Crossover)
                    {
                        lastRouteIndex = routeIndex - 1;
                    }
                }

                // train ahead has moved on, check next sections
                int startRouteIndex = routeIndex;

                while (routeIndex < routePart.Count && routeAvailable && !furthestRouteCleared)
                {
                    routeElement = routePart[routeIndex];
                    section = routeElement.TrackCircuitSection;

                    // check if section is in loop
                    if (sectionsInRoute.Contains(section.Index) ||
                        (routeIndex > startRouteIndex && section.Index == train.Train.PresentPosition[train.Direction].TrackCircuitSectionIndex))
                    {
                        endAuthority = EndAuthorityType.Loop;
                        train.Train.LoopSection = section.Index;
                        routeAvailable = false;

                        Trace.TraceInformation("Train {0} ({1}) : Looped at {2}", train.Train.Name, train.Train.Number, section.Index);
                    }
                    // check if section is access to pool
                    else if (train.Train.CheckPoolAccess(section.Index))
                    {
                        routeAvailable = false;
                        furthestRouteCleared = true;
                    }
                    // check if section is available
                    else if (section.GetSectionStateClearNode(train, (int)routeElement.Direction, routePart))
                    {
                        lastReserved = section.Index;
                        lastRouteIndex = routeIndex;
                        sectionsInRoute.Add(section.Index);
                        clearedDistanceM += section.Length - offset;

                        if (section.CircuitState.OccupiedByOtherTrains(train))
                        {
                            bool trainIsAhead = false;

                            // section is still ahead
                            if (section.Index != position.TrackCircuitSectionIndex)
                            {
                                trainIsAhead = true;
                            }
                            // same section
                            else
                            {
                                trainAhead = section.TestTrainAhead(train.Train, position.Offset, position.Direction);
                                if (trainAhead.Count > 0 && section.CircuitType == TrackCircuitType.Normal) // do not end path on junction
                                {
                                    trainIsAhead = true;
                                }
                            }

                            if (trainIsAhead)
                            {
                                lastRouteIndex = routeIndex - 1;
                                lastReserved = lastRouteIndex >= 0 ? routePart[lastRouteIndex].TrackCircuitSection.Index : -1;
                                routeAvailable = false;
                                clearedDistanceM -= section.Length + offset; // correct length as this section was already added to total length
                            }
                        }

                        if (routeAvailable)
                        {
                            routeIndex++;
                            offset = 0.0f;

                            if (!section.CircuitState.OccupiedByThisTrain(train) &&
                                section.CircuitState.TrainReserved == null)
                            {
                                section.Reserve(train, routePart);
                            }

                            if (!furthestRouteCleared && section.EndSignals[routeElement.Direction] != null)
                            {
                                Signal endSignal = section.EndSignals[routeElement.Direction];
                                // check if signal enabled for other train - if so, keep in node control
                                if (endSignal.EnabledTrain == null || endSignal.EnabledTrain == train)
                                {
                                    if (routeIndex < routePart.Count)
                                    {
                                        train.Train.SwitchToSignalControl(section.EndSignals[routeElement.Direction]);
                                    }
                                }
                                furthestRouteCleared = true;
                            }

                            if (clearedDistanceM > (train.Train.MaxDistanceCheckedAhead))
                            {
                                endAuthority = EndAuthorityType.MaxDistance;
                                furthestRouteCleared = true;
                            }
                        }
                    }
                    // section is not available
                    else
                    {
                        lastRouteIndex = routeIndex - 1;
                        lastReserved = lastRouteIndex >= 0 ? routePart[lastRouteIndex].TrackCircuitSection.Index : -1;
                        routeAvailable = false;
                    }
                }
            }

            // if not cleared to max distance or looped, determine reason

            if (!furthestRouteCleared && lastRouteIndex > 0 && routePart[lastRouteIndex].TrackCircuitSection.Index >= 0 && endAuthority != EndAuthorityType.Loop)
            {

                routeElement = routePart[lastRouteIndex];
                section = routeElement.TrackCircuitSection;

                // end of track reached
                if (section.CircuitType == TrackCircuitType.EndOfTrack)
                {
                    endAuthority = EndAuthorityType.EndOfTrack;
                    furthestRouteCleared = true;
                }

                // end of path reached

                if (!furthestRouteCleared && lastRouteIndex > (routePart.Count - 1))
                {
                    endAuthority = EndAuthorityType.EndOfPath;
                    furthestRouteCleared = true;
                }
            }

            // check if next section is switch held against train
            if (!furthestRouteCleared && lastRouteIndex < (routePart.Count - 1))
            {
                TrackCircuitRouteElement nextElement = routePart[lastRouteIndex + 1];
                section = nextElement.TrackCircuitSection;
                if (section.CircuitType == TrackCircuitType.Junction ||
                    section.CircuitType == TrackCircuitType.Crossover)
                {
                    if (!section.IsAvailable(train))
                    {
                        // check if switch is set to required path - if so, do not classify as reserved switch even if it is reserved by another train

                        int jnIndex = routePart.GetRouteIndex(section.Index, 0);
                        bool jnAligned = false;
                        if (jnIndex < routePart.Count - 1)
                        {
                            if (routePart[jnIndex + 1].TrackCircuitSection.Index == section.ActivePins[TrackDirection.Ahead, Location.NearEnd].Link ||
                                routePart[jnIndex + 1].TrackCircuitSection.Index == section.ActivePins[TrackDirection.Ahead, Location.FarEnd].Link)
                            {
                                if (routePart[jnIndex - 1].TrackCircuitSection.Index == section.ActivePins[TrackDirection.Reverse, Location.NearEnd].Link ||
                                    routePart[jnIndex - 1].TrackCircuitSection.Index == section.ActivePins[TrackDirection.Reverse, Location.FarEnd].Link)
                                {
                                    jnAligned = true;
                                }
                            }
                            else if (routePart[jnIndex + 1].TrackCircuitSection.Index == section.ActivePins[TrackDirection.Reverse, Location.NearEnd].Link ||
                                routePart[jnIndex + 1].TrackCircuitSection.Index == section.ActivePins[TrackDirection.Reverse, Location.FarEnd].Link)
                            {
                                if (routePart[jnIndex - 1].TrackCircuitSection.Index == section.ActivePins[TrackDirection.Ahead, Location.NearEnd].Link ||
                                    routePart[jnIndex - 1].TrackCircuitSection.Index == section.ActivePins[TrackDirection.Ahead, Location.FarEnd].Link)
                                {
                                    jnAligned = true;
                                }
                            }
                        }

                        // switch is not properly set, so it blocks the path
                        if (!jnAligned)
                        {
                            endAuthority = EndAuthorityType.ReservedSwitch;
                            furthestRouteCleared = true;
                        }
                    }
                }
            }

            // check if next section is occupied by stationary train or train moving in similar direction
            // if so calculate distance to end of train
            // only allowed for NORMAL sections and if not looped
            if (!furthestRouteCleared && lastRouteIndex < (routePart.Count - 1) && endAuthority != EndAuthorityType.Loop)
            {
                TrackCircuitRouteElement nextElement = routePart[lastRouteIndex + 1];
                TrackDirection reqDirection = nextElement.Direction;
                TrackDirection revDirection = nextElement.Direction.Reverse();

                section = nextElement.TrackCircuitSection;

                if (section.CircuitType == TrackCircuitType.Normal &&
                           section.CircuitState.OccupiedByOtherTrains(train))
                {
                    if (section.CircuitState.OccupiedByOtherTrains((int)revDirection, false, train))
                    {
                        endAuthority = EndAuthorityType.TrainAhead;
                    }
                    // check for train further ahead and determine distance to train
                    Dictionary<Train, float> trainAhead = section.TestTrainAhead(train.Train, offset, reqDirection);

                    if (trainAhead.Count > 0)
                    {
                        foreach (KeyValuePair<Train, float> thisTrainAhead in trainAhead)  // there is only one value
                        {
                            endAuthority = EndAuthorityType.TrainAhead;
                            clearedDistanceM += thisTrainAhead.Value;
                            furthestRouteCleared = true;
                        }
                    }
                }
                else if (section.GetSectionStateClearNode(train, (int)routeElement.Direction, routePart))
                {
                    endAuthority = EndAuthorityType.EndOfAuthority;
                }
                else if (section.CircuitType == TrackCircuitType.Crossover || section.CircuitType == TrackCircuitType.Junction)
                {
                    // first not-available section is crossover or junction - treat as reserved switch
                    endAuthority = EndAuthorityType.ReservedSwitch;
                }
            }
            else if (routeIndex >= routePart.Count)
            {
                endAuthority = EndAuthorityType.EndOfAuthority;
            }

            // update train details
            train.Train.EndAuthorityTypes[train.TrainRouteDirectionIndex] = endAuthority;
            train.Train.LastReservedSection[train.TrainRouteDirectionIndex] = lastReserved;
            train.Train.DistanceToEndNodeAuthorityM[train.TrainRouteDirectionIndex] = clearedDistanceM;
        }

        //================================================================================================//
        /// <summary>
        /// Break down reserved route
        /// </summary>
        public void BreakDownRoute(int firstSectionIndex, Train.TrainRouted requiredTrain)
        {
            if (null == requiredTrain)
                throw new ArgumentNullException(nameof(requiredTrain));

            if (firstSectionIndex < 0)
                return; // no route to break down

            TrackCircuitSection firstSection = TrackCircuitSection.TrackCircuitList[firstSectionIndex];
            Train.TrainRouted train = firstSection.CircuitState.TrainReserved;

            // if not reserved - no further route ahead
            if (train == null || train != requiredTrain)
            {
                return;   // section reserved for other train - stop action
            }

            // if occupied by train - skip actions and proceed to next section
            if (!firstSection.CircuitState.OccupiedByThisTrain(requiredTrain))
            {
                // unreserve first section
                firstSection.UnreserveTrain(train, true);
            }

            // check which direction to go
            TrackCircuitSection nextSection = null;
            TrackDirection nextDirection = TrackDirection.Ahead;

            foreach (TrackDirection direction in EnumExtension.GetValues<TrackDirection>())
            {
                foreach (Location pinLocation in EnumExtension.GetValues<Location>())
                {
                    int sectionIndex = firstSection.Pins[direction, pinLocation].Link;
                    if (sectionIndex > 0)
                    {
                        TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[sectionIndex];
                        if (section.CircuitState.TrainReserved != null && section.CircuitState.TrainReserved.Train == requiredTrain.Train)
                        {
                            nextSection = section;
                            nextDirection = firstSection.Pins[direction, pinLocation].Direction;
                        }
                    }
                }
            }

            // run back through all reserved sections
            while (nextSection != null)
            {
                nextSection.UnreserveTrain(requiredTrain, true);
                TrackCircuitSection section = nextSection;
                nextSection = null;
                TrackDirection currentDirection = nextDirection;

                // try to find next section using active links

                TrackCircuitSection trySection;
                foreach (Location pinLocation in EnumExtension.GetValues<Location>())
                {
                    int sectionIndex = section.ActivePins[currentDirection, pinLocation].Link;
                    if (sectionIndex > 0)
                    {
                        trySection = TrackCircuitSection.TrackCircuitList[sectionIndex];
                        if (trySection.CircuitState.TrainReserved != null && trySection.CircuitState.TrainReserved.Train == requiredTrain.Train)
                        {
                            nextSection = trySection;
                            nextDirection = section.ActivePins[currentDirection, pinLocation].Direction;
                        }
                    }
                }

                // not found, then try possible links
                foreach (Location pinLocation in EnumExtension.GetValues<Location>())
                {
                    int trySectionIndex = section.Pins[currentDirection, pinLocation].Link;
                    if (trySectionIndex > 0)
                    {
                        trySection = TrackCircuitSection.TrackCircuitList[trySectionIndex];
                        if (trySection.CircuitState.TrainReserved != null && trySection.CircuitState.TrainReserved.Train == requiredTrain.Train)
                        {
                            nextSection = trySection;
                            nextDirection = section.Pins[currentDirection, pinLocation].Direction;
                        }
                    }
                }
            }
        }

        //================================================================================================//
        /// <summary>
        /// Break down reserved route using route list
        /// </summary>
        public void BreakDownRouteList(TrackCircuitPartialPathRoute requiredRoute, int firstRouteIndex, Train.TrainRouted requiredTrain)
        {
            if (null == requiredTrain)
                throw new ArgumentNullException(nameof(requiredTrain));
            if (null == requiredRoute)
                throw new ArgumentNullException(nameof(requiredRoute));

            for (int i = requiredRoute.Count - 1; i >= 0 && i >= firstRouteIndex; i--)
            {
                TrackCircuitSection section = requiredRoute[i].TrackCircuitSection;
                if (!section.CircuitState.OccupiedByThisTrain(requiredTrain.Train))
                {
                    section.RemoveTrain(requiredTrain.Train, true);
                }
                else
                {
                    section.EndSignals[requiredRoute[i].Direction]?.ResetSignal(false);
                }
            }
        }

        //================================================================================================//
        /// Build temp route for train
        /// <summary>
        /// Used for trains without path (eg stationary constists), manual operation
        /// </summary>
        public static TrackCircuitPartialPathRoute BuildTempRoute(Train train, int firstSectionIndex, float firstOffset, TrackDirection firstDirection,
                float routeLength, bool overrideManualSwitchState, bool autoAlign, bool stopAtFacingSignal)
        {
            bool honourManualSwitchState = !overrideManualSwitchState;
            List<int> sectionList = ScanRoute(train, firstSectionIndex, firstOffset, firstDirection,
                    true, routeLength, honourManualSwitchState, autoAlign, stopAtFacingSignal, false, true, false, false, false, false, false);
            TrackCircuitPartialPathRoute tempRoute = new TrackCircuitPartialPathRoute();
            int lastIndex = -1;

            foreach (int i in sectionList)
            {
                TrackDirection curDirection = i < 0 ? TrackDirection.Reverse : TrackDirection.Ahead;
                int sectionIndex = i < 0 ? -i : i;
                TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[sectionIndex];

                TrackCircuitRouteElement thisElement = new TrackCircuitRouteElement(section, curDirection, lastIndex);
                tempRoute.Add(thisElement);
                lastIndex = sectionIndex;
            }

            // set pin references for junction sections
            for (int i = 0; i < tempRoute.Count - 1; i++) // do not process last element as next element is required
            {
                TrackCircuitRouteElement routeElement = tempRoute[i];
                TrackCircuitSection section = routeElement.TrackCircuitSection;

                if (section.CircuitType == TrackCircuitType.Junction)
                {
                    if (routeElement.OutPin[Location.NearEnd] == TrackDirection.Reverse) // facing switch
                    {
                        routeElement.OutPin[Location.FarEnd] = section.Pins[TrackDirection.Reverse, Location.NearEnd].Link == tempRoute[i + 1].TrackCircuitSection.Index ? TrackDirection.Ahead : TrackDirection.Reverse;
                    }
                }
            }

            return tempRoute;
        }

        //================================================================================================//
        /// <summary>
        /// Follow default route for train
        /// Use for :
        ///   - build temp list for trains without route (eg stat objects)
        ///   - build list for train under Manual control
        ///   - build list of sections when train slip backward
        ///   - search signal or speedpost ahead or at the rear of the train (either in facing or backward direction)
        ///
        /// Search ends :
        ///   - if required object is found
        ///   - if required length is covered
        ///   - if valid path only is requested and unreserved section is found (variable thisTrain required)
        ///   - end of track
        ///   - looped track
        ///   - re-enter in original route (for manual re-routing)
        ///
        /// Returned is list of sections, with positive no. indicating direction 0 and negative no. indicating direction 1
        /// If signal or speedpost is required, list will contain index of required item (>0 facing direction, <0 backing direction)
        /// </summary>
        public static List<int> ScanRoute(Train train, int firstSectionIndex, float firstOffset, TrackDirection firstDirection, bool forward,
                float routeLength, bool honourManualSwitch, bool autoAlign, bool stopAtFacingSignal, bool reservedOnly, bool returnSections,
                bool searchFacingSignal, bool searchBackwardSignal, bool searchFacingSpeedpost, bool searchBackwardSpeedpost,
                bool isFreight, bool considerSpeedReset = false, bool checkReenterOriginalRoute = false)
        {
            int sectionIndex = firstSectionIndex;

            int lastIndex = -2;   // set to values not encountered for pin links
            int thisIndex = sectionIndex;

            float offset = firstOffset;
            TrackDirection curDirection = firstDirection;
            TrackDirection nextDirection = curDirection;

            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[sectionIndex];

            float coveredLength = firstOffset;
            if (forward || (firstDirection == TrackDirection.Reverse && !forward))
            {
                coveredLength = section.Length - firstOffset;
            }

            bool endOfRoute = false;
            List<int> foundItems = new List<int>();
            List<int> foundObject = new List<int>();

            while (!endOfRoute)
            {

                // check looped

                int routedIndex = curDirection == 0 ? thisIndex : -thisIndex;
                if (foundItems.Contains(thisIndex) || foundItems.Contains(-thisIndex))
                {
                    break;
                }

                // add section
                foundItems.Add(routedIndex);

                // set length, pin index and opp direction

                TrackDirection oppDirection = curDirection.Reverse();

                TrackDirection outPinDirection = forward ? curDirection : oppDirection;
                TrackDirection inPinDirection = outPinDirection.Reverse();

                // check all conditions and objects as required

                if (stopAtFacingSignal && section.EndSignals[curDirection] != null)           // stop at facing signal
                {
                    endOfRoute = true;
                }

                // search facing speedpost
                if (searchFacingSpeedpost && section.CircuitItems.TrackCircuitSpeedPosts[curDirection].Count > 0)
                {
                    List<TrackCircuitSignalItem> itemList = section.CircuitItems.TrackCircuitSpeedPosts[curDirection];

                    if (forward)
                    {
                        for (int i = 0; i < itemList.Count && !endOfRoute; i++)
                        {
                            TrackCircuitSignalItem signalItem = itemList[i];

                            Signal thisSpeedpost = signalItem.Signal;
                            SpeedInfo speed_info = thisSpeedpost.SpeedLimit(SignalFunction.Speed);

                            if ((isFreight && speed_info.FreightSpeed > 0) || (!isFreight && speed_info.PassengerSpeed > 0))
                            {
                                if (signalItem.SignalLocation > offset)
                                {
                                    foundObject.Add(signalItem.Signal.Index);
                                    endOfRoute = true;
                                }
                            }
                        }
                    }
                    else
                    {
                        for (int i = itemList.Count - 1; i >= 0 && !endOfRoute; i--)
                        {
                            TrackCircuitSignalItem signalItem = itemList[i];

                            Signal speedpost = signalItem.Signal;
                            SpeedInfo speed_info = speedpost.SpeedLimit(SignalFunction.Speed);

                            if ((isFreight && speed_info.FreightSpeed > 0) || (!isFreight && speed_info.PassengerSpeed > 0))
                            {
                                if (offset == 0 || signalItem.SignalLocation < offset)
                                {
                                    foundObject.Add(signalItem.Signal.Index);
                                    endOfRoute = true;
                                }
                            }
                        }
                    }
                }

                if (searchFacingSignal && section.EndSignals[curDirection] != null)           // search facing signal
                {
                    foundObject.Add(section.EndSignals[curDirection].Index);
                    endOfRoute = true;
                }

                // search backward speedpost
                if (searchBackwardSpeedpost && section.CircuitItems.TrackCircuitSpeedPosts[oppDirection].Count > 0)
                {
                    List<TrackCircuitSignalItem> itemList = section.CircuitItems.TrackCircuitSpeedPosts[oppDirection];

                    if (forward)
                    {
                        for (int iObject = itemList.Count - 1; iObject >= 0 && !endOfRoute; iObject--)
                        {
                            TrackCircuitSignalItem signalItem = itemList[iObject];

                            Signal speedpost = signalItem.Signal;
                            SpeedInfo speed_info = speedpost.SpeedLimit(SignalFunction.Speed);
                            if (considerSpeedReset)
                            {
                                speed_info.Reset = speedpost.SignalSpeed(SignalFunction.Speed).Reset;
                            }
                            if ((isFreight && speed_info.FreightSpeed > 0) || (!isFreight && speed_info.PassengerSpeed > 0) || speed_info.Reset)
                            {
                                if (signalItem.SignalLocation < section.Length - offset)
                                {
                                    endOfRoute = true;
                                    foundObject.Add(-(signalItem.Signal.Index));
                                }
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < itemList.Count - 1 && !endOfRoute; i++)
                        {
                            TrackCircuitSignalItem signalItem = itemList[i];

                            Signal speedpost = signalItem.Signal;
                            SpeedInfo speed_info = speedpost.SpeedLimit(SignalFunction.Speed);

                            if ((isFreight && speed_info.FreightSpeed > 0) || (!isFreight && speed_info.PassengerSpeed > 0))
                            {
                                if (offset == 0 || signalItem.SignalLocation > section.Length - offset)
                                {
                                    endOfRoute = true;
                                    foundObject.Add(-(signalItem.Signal.Index));
                                }
                            }
                        }
                    }
                }

                // move to next section
                // follow active links if set, otherwise default links (=0)
                int nextIndex = -1;
                switch (section.CircuitType)
                {
                    case TrackCircuitType.Crossover:
                        if (section.Pins[inPinDirection, Location.NearEnd].Link == lastIndex)
                        {
                            nextIndex = section.Pins[outPinDirection, Location.NearEnd].Link;
                            nextDirection = section.Pins[outPinDirection, Location.NearEnd].Direction;
                        }
                        else if (section.Pins[inPinDirection, Location.FarEnd].Link == lastIndex)
                        {
                            nextIndex = section.Pins[outPinDirection, Location.FarEnd].Link;
                            nextDirection = section.Pins[outPinDirection, Location.FarEnd].Direction;
                        }
                        break;
                    case TrackCircuitType.Junction:
                        if (checkReenterOriginalRoute)
                        {
#pragma warning disable CA1062 // Validate arguments of public methods
                            TrackCircuitPartialPathRoute originalSubpath = train.TCRoute.TCRouteSubpaths[train.TCRoute.OriginalSubpath];
#pragma warning restore CA1062 // Validate arguments of public methods
                            if (outPinDirection == 0)
                            {
                                // loop on original route to check if we are re-entering it
                                for (int routeIndex = 0; routeIndex < originalSubpath.Count; routeIndex++)
                                {
                                    if (thisIndex == originalSubpath[routeIndex].TrackCircuitSection.Index)
                                    // nice, we are returning into the original route
                                    {
                                        endOfRoute = true;
                                        break;
                                    }
                                }
                            }
                        }

                        if (section.ActivePins[outPinDirection, Location.NearEnd].Link > 0)
                        {
                            nextIndex = section.ActivePins[outPinDirection, Location.NearEnd].Link;
                            nextDirection = section.ActivePins[outPinDirection, Location.NearEnd].Direction;
                        }
                        else if (section.ActivePins[outPinDirection, Location.FarEnd].Link > 0)
                        {
                            nextIndex = section.ActivePins[outPinDirection, Location.FarEnd].Link;
                            nextDirection = section.ActivePins[outPinDirection, Location.FarEnd].Direction;
                        }
                        else if (honourManualSwitch && section.JunctionSetManual >= 0)
                        {
                            nextIndex = section.Pins[outPinDirection, (Location)section.JunctionSetManual].Link;
                            nextDirection = section.Pins[outPinDirection, (Location)section.JunctionSetManual].Direction;
                        }
                        else if (!reservedOnly)
                        {
                            nextIndex = section.Pins[outPinDirection, (Location)section.JunctionLastRoute].Link;
                            nextDirection = section.Pins[outPinDirection, (Location)section.JunctionLastRoute].Direction;
                        }
                        break;
                    case TrackCircuitType.EndOfTrack:
                        break;
                    default:
                        nextIndex = section.Pins[outPinDirection, Location.NearEnd].Link;
                        nextDirection = section.Pins[outPinDirection, Location.NearEnd].Direction;

                        TrackCircuitSection nextSection = TrackCircuitSection.TrackCircuitList[nextIndex];

                        // if next section is junction : check if locked against AI and if auto-alignment allowed
                        // switchable end of switch is always pin direction 1
                        if (nextSection.CircuitType == TrackCircuitType.Junction)
                        {
                            TrackDirection nextPinDirection = nextDirection.Reverse();
                            int nextPinIndex = nextSection.Pins[nextPinDirection, Location.NearEnd].Link == thisIndex ? 0 : 1;
                            if (nextPinDirection == TrackDirection.Reverse && nextSection.JunctionLastRoute != nextPinIndex)
                            {
                                //TODO 20201027 to be verified, nextSection.AILock had never been set, thus removed
                                //if (nextSection.AILock && thisTrain != null && (thisTrain.TrainType == Train.TRAINTYPE.AI
                                //    || thisTrain.TrainType == Train.TRAINTYPE.AI_PLAYERHOSTING))
                                //{
                                //    endOfRoute = true;
                                //}

                                if (!autoAlign)
                                {
                                    endOfRoute = true;
                                }
                            }
                        }
                        break;
                }

                if (nextIndex < 0)
                {
                    endOfRoute = true;
                }
                else
                {
                    lastIndex = thisIndex;
                    thisIndex = nextIndex;
                    section = TrackCircuitSection.TrackCircuitList[thisIndex];
                    curDirection = forward ? nextDirection : nextDirection.Reverse();
                    oppDirection = curDirection.Reverse();

                    if (searchBackwardSignal && section.EndSignals[oppDirection] != null)
                    {
                        endOfRoute = true;
                        foundObject.Add(-(section.EndSignals[oppDirection].Index));
                    }
                }

                if (!endOfRoute)
                {
                    offset = 0.0f;

                    if (train != null && reservedOnly)
                    {
                        TrackCircuitState state = section.CircuitState;

                        if (!state.OccupationState.ContainsTrain(train) &&
                            (state.TrainReserved != null && state.TrainReserved.Train != train))
                        {
                            endOfRoute = true;
                        }
                    }
                }

                if (!endOfRoute && routeLength > 0)
                {
                    endOfRoute = (coveredLength > routeLength);
                    coveredLength += section.Length;
                }

            }

            return returnSections ? foundItems : foundObject;
        }

        //================================================================================================//
        /// <summary>
        /// Process Platforms
        /// </summary>
        private void ProcessPlatforms(Dictionary<int, int> platformList, TrackItem[] trackItems, TrackNode[] trackNodes, ConcurrentDictionary<int, uint> platformSidesList)
        {
            foreach (KeyValuePair<int, int> platformIndex in platformList)
            {
                int platformDetailsIndex;

                // get platform item

                int index = platformIndex.Key;

                PlatformItem platform = trackItems[index] is PlatformItem item ? item : new PlatformItem((SidingItem)trackItems[index]);

                TrackNode trackNode = trackNodes[platformIndex.Value];

                // check if entry already created for related entry
                int relatedIndex = (int)platform.LinkedPlatformItemId;

                PlatformDetails platformDetails;
                Location refIndex;
                bool splitPlatform = false;

                // get related platform details
                if (PlatformXRefList.ContainsKey(relatedIndex))
                {
                    platformDetailsIndex = PlatformXRefList[relatedIndex];
                    platformDetails = PlatformDetailsList[platformDetailsIndex];
                    PlatformXRefList.Add(index, platformDetailsIndex);
                    refIndex = Location.FarEnd;
                }
                // create new platform details
                else
                {
                    platformDetails = new PlatformDetails(index);
                    PlatformDetailsList.Add(platformDetails);
                    platformDetailsIndex = PlatformDetailsList.Count - 1;
                    PlatformXRefList.Add(index, platformDetailsIndex);
                    refIndex = Location.NearEnd;
                }

                // set station reference
                if (StationXRefList.ContainsKey(platform.Station))
                {
                    List<int> crossRefList = StationXRefList[platform.Station];
                    crossRefList.Add(platformDetailsIndex);
                }
                else
                {
                    List<int> crossRefList = new List<int>
                    {
                        platformDetailsIndex
                    };
                    StationXRefList.Add(platform.Station, crossRefList);
                }

                // get tracksection

                int sectionIndex = -1;
                int crossrefIndex = -1;

                for (int iXRef = trackNode.TrackCircuitCrossReferences.Count - 1; iXRef >= 0 && sectionIndex < 0; iXRef--)
                {
                    if (platform.SData1 <
                     (trackNode.TrackCircuitCrossReferences[iXRef].OffsetLength[1] + trackNode.TrackCircuitCrossReferences[iXRef].Length))
                    {
                        sectionIndex = trackNode.TrackCircuitCrossReferences[iXRef].Index;
                        crossrefIndex = iXRef;
                    }
                }

                if (sectionIndex < 0)
                {
                    Trace.TraceInformation("Cannot locate TCSection for platform {0}", index);
                    sectionIndex = trackNode.TrackCircuitCrossReferences[0].Index;
                    crossrefIndex = 0;
                }

                // if first entry, set tracksection

                if (refIndex == Location.NearEnd)
                {
                    platformDetails.TCSectionIndex.Add(sectionIndex);
                }
                // if second entry, test if equal - if not, build list
                else
                {
                    if (sectionIndex != platformDetails.TCSectionIndex[0])
                    {
                        int firstXRef = -1;
                        for (int i = trackNode.TrackCircuitCrossReferences.Count - 1; i >= 0 && firstXRef < 0; i--)
                        {
                            if (trackNode.TrackCircuitCrossReferences[i].Index == platformDetails.TCSectionIndex[0])
                            {
                                firstXRef = i;
                            }
                        }

                        if (firstXRef < 0)  // platform is split by junction !!!
                        {
                            ResolveSplitPlatform(platformDetails, sectionIndex, platform, trackNode as TrackVectorNode, trackItems, trackNodes);
                            splitPlatform = true;
                            Trace.TraceInformation("Platform split by junction at " + platformDetails.Name);
                        }
                        else if (crossrefIndex < firstXRef)
                        {
                            platformDetails.TCSectionIndex.Clear();
                            for (int iXRef = crossrefIndex; iXRef <= firstXRef; iXRef++)
                            {
                                platformDetails.TCSectionIndex.Add(trackNode.TrackCircuitCrossReferences[iXRef].Index);
                            }
                        }
                        else
                        {
                            platformDetails.TCSectionIndex.Clear();
                            for (int iXRef = firstXRef; iXRef <= crossrefIndex; iXRef++)
                            {
                                platformDetails.TCSectionIndex.Add(trackNode.TrackCircuitCrossReferences[iXRef].Index);
                            }
                        }
                    }
                }

                // set details (if not split platform)
                if (!splitPlatform)
                {
                    TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[sectionIndex];

                    platformDetails.PlatformReference[refIndex] = index;
                    platformDetails.NodeOffset[refIndex] = platform.SData1;
                    platformDetails.TrackCircuitOffset[refIndex, TrackDirection.Reverse] = platform.SData1 - section.OffsetLength[Location.FarEnd];
                    platformDetails.TrackCircuitOffset[refIndex.Reverse(), TrackDirection.Ahead] = section.Length - platformDetails.TrackCircuitOffset[refIndex, TrackDirection.Reverse];
                    if (platform.Flags1.Equals("ffff0000", StringComparison.OrdinalIgnoreCase))
                        platformDetails.PlatformFrontUiD = index;        // used to define 
                }

                if (refIndex == 0)
                {
                    platformDetails.Name = platform.Station;
                    platformDetails.MinWaitingTime = platform.PlatformMinWaitingTime;
                    platformDetails.NumPassengersWaiting = (int)platform.PlatformNumPassengersWaiting;
                }
                else if (!splitPlatform)
                {
                    platformDetails.Length = Math.Abs(platformDetails.NodeOffset[Location.FarEnd] - platformDetails.NodeOffset[Location.NearEnd]);
                }

                if (platformSidesList.TryGetValue(index, out uint platformData))
                {
                    if (((uint)Formats.Msts.PlatformData.PlatformLeft & platformData) == (uint)Formats.Msts.PlatformData.PlatformLeft)
                        platformDetails.PlatformSide |= PlatformDetails.PlatformSides.Left;
                    if (((uint)Formats.Msts.PlatformData.PlatformRight & platformData) == (uint)Formats.Msts.PlatformData.PlatformRight)
                        platformDetails.PlatformSide |= PlatformDetails.PlatformSides.Right;
                }

                // check if direction correct, else swap 0 - 1 entries for offsets etc.
                if (refIndex == Location.FarEnd && platformDetails.NodeOffset[Location.FarEnd] < platformDetails.NodeOffset[Location.NearEnd] && !splitPlatform)
                {
                    float tf = platformDetails.NodeOffset[0];
                    platformDetails.NodeOffset[Location.NearEnd] = platformDetails.NodeOffset[Location.FarEnd];
                    platformDetails.NodeOffset[Location.FarEnd] = tf;

                    foreach (Location location in EnumExtension.GetValues<Location>())
                    {
                        tf = platformDetails.TrackCircuitOffset[location, TrackDirection.Ahead];
                        platformDetails.TrackCircuitOffset[location, TrackDirection.Ahead] = platformDetails.TrackCircuitOffset[location, TrackDirection.Reverse];
                        platformDetails.TrackCircuitOffset[location, TrackDirection.Reverse] = tf;
                    }
                }

                // search for end signals
                trackNode = trackNodes[TrackCircuitSection.TrackCircuitList[platformDetails.TCSectionIndex[0]].OriginalIndex];

                if (refIndex == Location.FarEnd)
                {
                    float distToSignal = 0.0f;
                    float offset = platformDetails.TrackCircuitOffset[Location.FarEnd, TrackDirection.Ahead];
                    int lastSection = platformDetails.TCSectionIndex[platformDetails.TCSectionIndex.Count - 1];
                    int lastSectionXRef = -1;

                    for (int i = 0; i < trackNode.TrackCircuitCrossReferences.Count; i++)
                    {
                        if (lastSection == trackNode.TrackCircuitCrossReferences[i].Index)
                        {
                            lastSectionXRef = i;
                            break;
                        }
                    }

                    for (int i = lastSectionXRef; i < trackNode.TrackCircuitCrossReferences.Count; i++)
                    {
                        TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[trackNode.TrackCircuitCrossReferences[i].Index];

                        distToSignal += section.Length - offset;
                        offset = 0.0f;

                        if (section.EndSignals[TrackDirection.Ahead] != null)
                        {
                            // end signal is always valid in timetable mode
                            if (Simulator.Instance.TimetableMode || distToSignal <= 150)
                            {
                                platformDetails.EndSignals[TrackDirection.Ahead] = section.EndSignals[TrackDirection.Ahead].Index;
                                platformDetails.DistanceToSignals[TrackDirection.Ahead] = distToSignal;
                            }
                            // end signal is only valid if it has no fixed route in activity mode
                            else
                            {
                                float? approachControlLimitPositionM = null;
                                if (distToSignal > 150)
                                {
                                    foreach (SignalHead signalHead in section.EndSignals[TrackDirection.Ahead].SignalHeads)
                                    {
                                        if (signalHead.ApproachControlLimitPositionM != null) approachControlLimitPositionM = signalHead.ApproachControlLimitPositionM;
                                    }
                                }
                                if (!section.EndSignals[TrackDirection.Ahead].FixedRoute && !(approachControlLimitPositionM != null && (float)approachControlLimitPositionM < distToSignal + 100))
                                {
                                    platformDetails.EndSignals[TrackDirection.Ahead] = section.EndSignals[TrackDirection.Ahead].Index;
                                    platformDetails.DistanceToSignals[TrackDirection.Ahead] = distToSignal;
                                }
                            }
                            break;
                        }
                    }

                    distToSignal = 0.0f;
                    offset = platformDetails.TrackCircuitOffset[Location.FarEnd, TrackDirection.Reverse];
                    int firstSection = platformDetails.TCSectionIndex[0];
                    int firstSectionXRef = lastSectionXRef;

                    if (lastSection != firstSection)
                    {
                        for (int iXRef = 0; iXRef < trackNode.TrackCircuitCrossReferences.Count; iXRef++)
                        {
                            if (firstSection == trackNode.TrackCircuitCrossReferences[iXRef].Index)
                            {
                                firstSectionXRef = iXRef;
                                break;
                            }
                        }
                    }

                    for (int iXRef = firstSectionXRef; iXRef >= 0; iXRef--)
                    {
                        TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[trackNode.TrackCircuitCrossReferences[iXRef].Index];

                        distToSignal += section.Length - offset;
                        offset = 0.0f;

                        if (section.EndSignals[TrackDirection.Reverse] != null)
                        {
                            if (Simulator.Instance.TimetableMode || distToSignal <= 150)
                            {
                                platformDetails.EndSignals[TrackDirection.Reverse] = section.EndSignals[TrackDirection.Reverse].Index;
                                platformDetails.DistanceToSignals[TrackDirection.Reverse] = distToSignal;
                            }
                            else
                            {
                                float? approachControlLimitPositionM = null;
                                if (distToSignal > 150)
                                {
                                    foreach (SignalHead signalHead in section.EndSignals[TrackDirection.Reverse].SignalHeads)
                                    {
                                        if (signalHead.ApproachControlLimitPositionM != null) approachControlLimitPositionM = signalHead.ApproachControlLimitPositionM;
                                    }
                                }
                                if (!section.EndSignals[TrackDirection.Reverse].FixedRoute && !(approachControlLimitPositionM != null && (float)approachControlLimitPositionM < distToSignal + 100))
                                {
                                    platformDetails.EndSignals[TrackDirection.Reverse] = section.EndSignals[TrackDirection.Reverse].Index;
                                    platformDetails.DistanceToSignals[TrackDirection.Reverse] = distToSignal;
                                }
                            }
                            break;
                        }

                    }
                }

                // set section crossreference
                if (refIndex == Location.FarEnd)
                {
                    foreach (int i in platformDetails.TCSectionIndex)
                    {
                        TrackCircuitSection thisSection = TrackCircuitSection.TrackCircuitList[i];
                        thisSection.PlatformIndices.Add(platformDetailsIndex);
                    }
                }
            }

            // Override .tdb NumPassengersWaiting info with .act NumPassengersWaiting info if any available
            int overriddenPlatformDetailsIndex;
            foreach (Formats.Msts.Models.PlatformData platformData in Simulator.Instance.Activity?.Activity.PlatformWaitingPassengers ?? Enumerable.Empty<Formats.Msts.Models.PlatformData>())
            {
                overriddenPlatformDetailsIndex = PlatformDetailsList.FindIndex(platformDetails => (platformDetails.PlatformReference[Location.NearEnd] == platformData.ID) || (platformDetails.PlatformReference[Location.FarEnd] == platformData.ID));
                if (overriddenPlatformDetailsIndex >= 0)
                    PlatformDetailsList[overriddenPlatformDetailsIndex].NumPassengersWaiting = platformData.PassengerCount;
                else
                    Trace.TraceWarning("Platform referenced in .act file with TrItemId {0} not present in .tdb file ", platformData.ID);
            }
        }// ProcessPlatforms

        //================================================================================================//
        /// <summary>
        /// Resolve split platforms
        /// </summary>
        private static void ResolveSplitPlatform(PlatformDetails platformDetails, int secondSectionIndex, PlatformItem secondPlatform, TrackVectorNode secondNode,
                    TrackItem[] trackItems, TrackNode[] trackNodes)
        {
            // get all positions related to tile of first platform item

            PlatformItem firstPlatform = (trackItems[platformDetails.PlatformReference[Location.NearEnd]] is PlatformItem item) ?
                    item : new PlatformItem((SidingItem)trackItems[platformDetails.PlatformReference[Location.NearEnd]]);

            int firstSectionIndex = platformDetails.TCSectionIndex[0];
            TrackCircuitSection thisSection = TrackCircuitSection.TrackCircuitList[firstSectionIndex];
            TrackVectorNode firstNode = trackNodes[thisSection.OriginalIndex] as TrackVectorNode;

            // first platform
            int TileX1 = firstPlatform.Location.TileX;
            int TileZ1 = firstPlatform.Location.TileZ;
            float X1 = firstPlatform.Location.Location.X;
            float Z1 = firstPlatform.Location.Location.Z;

            ref readonly WorldLocation location1 = ref firstNode.TrackVectorSections[0].Location;
            // start node position
            int TS1TileX = location1.TileX;
            int TS1TileZ = location1.TileZ;
            float TS1X = location1.Location.X;
            float TS1Z = location1.Location.Z;

            float TS1Xc = TS1X + (TS1TileX - TileX1) * 2048;
            float TS1Zc = TS1Z + (TS1TileZ - TileZ1) * 2048;

            // second platform
            int TileX2 = secondPlatform.Location.TileX;
            int TileZ2 = secondPlatform.Location.TileZ;
            float X2 = secondPlatform.Location.Location.X;
            float Z2 = secondPlatform.Location.Location.Z;

            float X2c = X2 + (TileX2 - TileX1) * 2048;
            float Z2c = Z2 + (TileZ2 - TileZ1) * 2048;

            ref readonly WorldLocation location2 = ref secondNode.TrackVectorSections[0].Location;
            int TS2TileX = location2.TileX;
            int TS2TileZ = location2.TileZ;
            float TS2X = location2.Location.X;
            float TS2Z = location2.Location.Z;

            float TS2Xc = TS2X + (TS2TileX - TileX1) * 2048;
            float TS2Zc = TS2Z + (TS2TileZ - TileZ1) * 2048;

            // determine if 2nd platform is towards end or begin of tracknode - use largest delta for check

            float dXplatform = X2c - X1;
            float dXnode = TS1Xc - X1;
            float dZplatform = Z2c - Z1;
            float dZnode = TS1Zc - Z1;

            float dplatform = Math.Abs(dXplatform) > Math.Abs(dZplatform) ? dXplatform : dZplatform;
            float dnode = Math.Abs(dXplatform) > Math.Abs(dXplatform) ? dXnode : dZnode;  // use same delta direction!

            // if towards begin : build list of sections from start

            List<int> platformSections = new List<int>();
            bool reqSectionFound = false;
            float totalLength1 = 0;
            int direction1 = 0;

            if (Math.Sign(dplatform) == Math.Sign(dnode))
            {
                for (int i = firstNode.TrackCircuitCrossReferences.Count - 1; i >= 0 && !reqSectionFound; i--)
                {
                    int crossrefIndex = firstNode.TrackCircuitCrossReferences[i].Index;
                    platformSections.Add(crossrefIndex);
                    totalLength1 += TrackCircuitSection.TrackCircuitList[crossrefIndex].Length;
                    reqSectionFound = (crossrefIndex == firstSectionIndex);
                }
                totalLength1 -= platformDetails.TrackCircuitOffset[Location.FarEnd, TrackDirection.Ahead];  // correct for offset
            }
            else
            {
                for (int i = 0; i < firstNode.TrackCircuitCrossReferences.Count && !reqSectionFound; i++)
                {
                    int crossrefIndex = firstNode.TrackCircuitCrossReferences[i].Index;
                    platformSections.Add(crossrefIndex);
                    totalLength1 += TrackCircuitSection.TrackCircuitList[crossrefIndex].Length;
                    reqSectionFound = (crossrefIndex == firstSectionIndex);
                    direction1 = 1;
                }
                totalLength1 -= platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Reverse];  // correct for offset
            }

            // determine if 1st platform is towards end or begin of tracknode - use largest delta for check
            dXplatform = X1 - X2c;
            dXnode = TS2Xc - X2c;
            dZplatform = Z1 - Z2c;
            dZnode = TS2Zc - Z2c;

            dplatform = Math.Abs(dXplatform) > Math.Abs(dZplatform) ? dXplatform : dZplatform;
            dnode = Math.Abs(dXplatform) > Math.Abs(dXplatform) ? dXnode : dZnode;  // use same delta direction!

            // if towards begin : build list of sections from start

            List<int> platformSectionsStart = new List<int>();
            reqSectionFound = false;
            float totalLength2 = 0;
            int direction2 = 0;

            if (Math.Sign(dplatform) == Math.Sign(dnode))
            {
                for (int i = secondNode.TrackCircuitCrossReferences.Count - 1; i >= 0 && !reqSectionFound; i--)
                {
                    int crossrefIndex = secondNode.TrackCircuitCrossReferences[i].Index;
                    platformSectionsStart.Add(crossrefIndex);
                    totalLength2 += TrackCircuitSection.TrackCircuitList[crossrefIndex].Length;
                    reqSectionFound = (crossrefIndex == secondSectionIndex);
                }
                totalLength2 -= (TrackCircuitSection.TrackCircuitList[secondSectionIndex].Length - secondPlatform.SData1);
            }
            else
            {
                for (int i = 0; i < secondNode.TrackCircuitCrossReferences.Count && !reqSectionFound; i++)
                {
                    int crossrefIndex = secondNode.TrackCircuitCrossReferences[i].Index;
                    platformSectionsStart.Add(crossrefIndex);
                    totalLength2 += TrackCircuitSection.TrackCircuitList[crossrefIndex].Length;
                    reqSectionFound = (crossrefIndex == secondSectionIndex);
                    direction2 = 1;
                }
                totalLength2 -= secondPlatform.SData1; // correct for offset
            }

            // use largest part

            platformDetails.TCSectionIndex.Clear();

            if (totalLength1 > totalLength2)
            {
                foreach (int thisIndex in platformSections)
                {
                    platformDetails.TCSectionIndex.Add(thisIndex);
                }

                platformDetails.Length = totalLength1;

                if (direction1 == 0)
                {
                    platformDetails.NodeOffset[Location.NearEnd] = 0.0f;
                    platformDetails.NodeOffset[Location.FarEnd] = firstPlatform.SData1;
                    platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Ahead] = TrackCircuitSection.TrackCircuitList[platformSections[platformSections.Count - 1]].Length - totalLength1;
                    for (int i = 0; i < platformSections.Count - 2; i++)
                    {
                        platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Ahead] += TrackCircuitSection.TrackCircuitList[platformSections[i]].Length;
                    }
                    platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Reverse] = 0.0f;
                    platformDetails.TrackCircuitOffset[Location.FarEnd, TrackDirection.Ahead] = TrackCircuitSection.TrackCircuitList[platformSections[0]].Length;
                    platformDetails.TrackCircuitOffset[Location.FarEnd, TrackDirection.Reverse] = firstPlatform.SData1;
                    for (int i = 0; i < platformSections.Count - 2; i++)
                    {
                        platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Ahead] -= TrackCircuitSection.TrackCircuitList[platformSections[i]].Length;
                    }
                }
                else
                {
                    platformDetails.NodeOffset[Location.NearEnd] = firstPlatform.SData1;
                    platformDetails.NodeOffset[Location.FarEnd] = platformDetails.NodeOffset[Location.NearEnd] + totalLength1;
                    platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Ahead] = 0.0f;
                    platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Reverse] = TrackCircuitSection.TrackCircuitList[platformSections[0]].Length - totalLength1;
                    for (int i = 1; i < platformSections.Count - 1; i++)
                    {
                        platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Ahead] += TrackCircuitSection.TrackCircuitList[platformSections[i]].Length;
                    }
                    platformDetails.TrackCircuitOffset[Location.FarEnd, TrackDirection.Ahead] = totalLength1;
                    for (int i = 1; i < platformSections.Count - 1; i++)
                    {
                        platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Ahead] -= TrackCircuitSection.TrackCircuitList[platformSections[i]].Length;
                    }
                    platformDetails.TrackCircuitOffset[Location.FarEnd, TrackDirection.Reverse] = TrackCircuitSection.TrackCircuitList[platformSections[platformSections.Count - 1]].Length;
                }
            }
            else
            {
                platformDetails.TCSectionIndex.AddRange(platformSectionsStart);
                platformDetails.Length = totalLength2;

                if (direction2 == 0)
                {
                    platformDetails.NodeOffset[Location.NearEnd] = 0.0f;
                    platformDetails.NodeOffset[Location.FarEnd] = secondPlatform.SData1;
                    platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Ahead] = TrackCircuitSection.TrackCircuitList[platformSectionsStart.Count - 1].Length - totalLength2;
                    for (int i = 0; i < platformSectionsStart.Count - 2; i++)
                    {
                        platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Ahead] += TrackCircuitSection.TrackCircuitList[platformSectionsStart[i]].Length;
                    }
                    platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Reverse] = 0.0f;
                    platformDetails.TrackCircuitOffset[Location.FarEnd, TrackDirection.Ahead] = TrackCircuitSection.TrackCircuitList[platformSectionsStart[0]].Length;
                    platformDetails.TrackCircuitOffset[Location.FarEnd, TrackDirection.Reverse] = secondPlatform.SData1;
                    for (int i = 0; i < platformSectionsStart.Count - 2; i++)
                    {
                        platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Ahead] -= TrackCircuitSection.TrackCircuitList[platformSectionsStart[i]].Length;
                    }
                }
                else
                {
                    platformDetails.NodeOffset[Location.NearEnd] = secondPlatform.SData1;
                    platformDetails.NodeOffset[Location.FarEnd] = platformDetails.NodeOffset[0] + totalLength2;
                    platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Ahead] = 0.0f;
                    platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Reverse] = TrackCircuitSection.TrackCircuitList[platformSectionsStart[0]].Length - totalLength2;
                    for (int i = 1; i < platformSectionsStart.Count - 1; i++)
                    {
                        platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Ahead] += TrackCircuitSection.TrackCircuitList[platformSectionsStart[i]].Length;
                    }
                    platformDetails.TrackCircuitOffset[Location.FarEnd, TrackDirection.Ahead] = totalLength2;
                    for (int i = 1; i < platformSectionsStart.Count - 1; i++)
                    {
                        platformDetails.TrackCircuitOffset[Location.NearEnd, TrackDirection.Ahead] -= TrackCircuitSection.TrackCircuitList[platformSectionsStart[i]].Length;
                    }
                    platformDetails.TrackCircuitOffset[Location.FarEnd, TrackDirection.Reverse] = TrackCircuitSection.TrackCircuitList[platformSectionsStart[platformSectionsStart.Count - 1]].Length;
                }
            }
        }

        //================================================================================================//
        /// <summary>
        /// Remove all deadlock path references for specified train
        /// </summary>
        public void RemoveDeadlockPathReferences(int trainNumber)
        {
            foreach (KeyValuePair<int, DeadlockInfo> deadlockElement in DeadlockInfoList)
            {
                DeadlockInfo deadlockInfo = deadlockElement.Value;
                if (deadlockInfo.TrainSubpathIndex.ContainsKey(trainNumber))
                {
                    Dictionary<int, int> subpathRef = deadlockInfo.TrainSubpathIndex[trainNumber];
                    foreach (KeyValuePair<int, int> pathRef in subpathRef)
                    {
                        int routeIndex = pathRef.Value;
                        List<int> pathReferences = deadlockInfo.TrainReferences[routeIndex];
                        foreach (int pathReference in pathReferences)
                        {
                            deadlockInfo.AvailablePathList[pathReference].AllowedTrains.Remove(trainNumber);
                        }
                        deadlockInfo.TrainReferences.Remove(routeIndex);
                        deadlockInfo.TrainOwnPath.Remove(routeIndex);
                        deadlockInfo.TrainLengthFit.Remove(routeIndex);
                    }
                    deadlockInfo.TrainSubpathIndex.Remove(trainNumber);
                }
            }
        }

        //================================================================================================//
        /// <summary>
        /// Reallocate all deadlock path references for specified train when train forms new train
        /// </summary>
        public void ReallocateDeadlockPathReferences(int oldnumber, int newnumber)
        {
            foreach (KeyValuePair<int, DeadlockInfo> deadlockElement in DeadlockInfoList)
            {
                DeadlockInfo deadlockInfo = deadlockElement.Value;
                if (deadlockInfo.TrainSubpathIndex.ContainsKey(oldnumber))
                {
                    Dictionary<int, int> subpathRef = deadlockInfo.TrainSubpathIndex[oldnumber];
                    foreach (KeyValuePair<int, int> pathRef in subpathRef)
                    {
                        int routeIndex = pathRef.Value;
                        List<int> pathReferences = deadlockInfo.TrainReferences[routeIndex];
                        foreach (int pathReference in pathReferences)
                        {
                            deadlockInfo.AvailablePathList[pathReference].AllowedTrains.Remove(oldnumber);
                            deadlockInfo.AvailablePathList[pathReference].AllowedTrains.Add(newnumber);
                        }
                    }
                    deadlockInfo.TrainSubpathIndex.Add(newnumber, subpathRef);
                    deadlockInfo.TrainSubpathIndex.Remove(oldnumber);
                }
            }
        }

        //================================================================================================//
        /// <summary>
        /// ProcessTunnels
        /// Process tunnel sections and add info to TrackCircuitSections
        /// </summary>
        private void ProcessTunnels()
        {
            // loop through tracknodes
            foreach (TrackNode node in trackDB.TrackNodes)
            {
                if (node is TrackVectorNode tvn)
                {
                    bool inTunnel = false;
                    List<float[]> tunnelInfo = new List<float[]>();
                    List<int> tunnelPaths = new List<int>();
                    float[] lastTunnel = null;
                    float totalLength = 0f;
                    int numPaths = -1;

                    // loop through all sections in node
                    foreach (TrackVectorSection section in tvn.TrackVectorSections)
                    {
                        if (!tsectiondat.TrackSections.ContainsKey(section.SectionIndex))
                        {
                            continue;  // missing track section
                        }

                        TrackSection TS = tsectiondat.TrackSections[section.SectionIndex];

                        // determine length
                        float length = TS.Curved ? MathHelper.ToRadians(Math.Abs(TS.Angle)) * TS.Radius : TS.Length;

                        // check tunnel shape

                        bool tunnelShape = false;
                        int shapePaths = 0;

                        if (tsectiondat.TrackShapes.ContainsKey(section.ShapeIndex))
                        {
                            TrackShape shape = tsectiondat.TrackShapes[section.ShapeIndex];
                            tunnelShape = shape.TunnelShape;
                            shapePaths = Convert.ToInt32(shape.PathsNumber);
                        }

                        if (tunnelShape)
                        {
                            numPaths = numPaths < 0 ? shapePaths : Math.Min(numPaths, shapePaths);
                            if (inTunnel)
                            {
                                lastTunnel[1] += length;
                            }
                            else
                            {
                                lastTunnel = new float[2];
                                lastTunnel[0] = totalLength;
                                lastTunnel[1] = length;
                                inTunnel = true;
                            }
                        }
                        else if (inTunnel)
                        {
                            tunnelInfo.Add(lastTunnel);
                            tunnelPaths.Add(numPaths);
                            inTunnel = false;
                            numPaths = -1;
                        }
                        totalLength += length;
                    }

                    // add last tunnel item
                    if (inTunnel)
                    {
                        tunnelInfo.Add(lastTunnel);
                        tunnelPaths.Add(numPaths);
                    }

                    // add tunnel info to TrackCircuitSections
                    if (tunnelInfo.Count > 0)
                    {
                        bool tcsInTunnel = false;
                        float[] tunnelData = tunnelInfo[0];
                        float processedLength = 0;

                        for (int i = node.TrackCircuitCrossReferences.Count - 1; i >= 0; i--)
                        {
                            TrackCircuitSectionCrossReference crossRefSection = node.TrackCircuitCrossReferences[i];
                            // forward direction
                            float sectionStartOffset = crossRefSection.OffsetLength[1];
                            float sectionLength = crossRefSection.Length;
                            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[crossRefSection.Index];
                            float startOffset;

                            // if tunnel starts in TCS
                            while (tunnelData != null && tunnelData[0] <= (sectionStartOffset + sectionLength))
                            {
                                float tunnelStart = 0;
                                float sectionTunnelStart;

                                // if in tunnel, set start in tunnel and check end
                                if (tcsInTunnel)
                                {
                                    sectionTunnelStart = -1;
                                    startOffset = processedLength;
                                }
                                else
                                // else start new tunnel
                                {
                                    sectionTunnelStart = tunnelData[0] - sectionStartOffset;
                                    tunnelStart = sectionTunnelStart;
                                    startOffset = -1;
                                }

                                if ((sectionStartOffset + sectionLength) >= (tunnelData[0] + tunnelData[1]))  // tunnel end is in this section
                                {
                                    tcsInTunnel = false;
                                    processedLength = 0;

                                    section.AddTunnelData(new TunnelInfoData(tunnelPaths[0], sectionTunnelStart, tunnelStart + tunnelData[1] - processedLength, tunnelData[1] - processedLength, tunnelData[1], section.Length, startOffset));

                                    if (tunnelInfo.Count >= 2)
                                    {
                                        tunnelInfo.RemoveAt(0);
                                        tunnelData = tunnelInfo[0];
                                        tunnelPaths.RemoveAt(0);
                                    }
                                    else
                                    {
                                        tunnelData = null;
                                        break;  // no more tunnels to process
                                    }
                                }
                                else
                                {
                                    tcsInTunnel = true;
                                    processedLength += (sectionLength - tunnelStart);

                                    section.AddTunnelData(new TunnelInfoData(tunnelPaths[0], sectionTunnelStart, -1, sectionLength - tunnelStart, tunnelData[1], section.Length, startOffset));
                                    break;  // cannot add more tunnels to section
                                }
                            }
                        }
                    }
                }
            }
        }

        //================================================================================================//
        /// <summary>
        /// ProcessTroughs
        /// Process trough sections and add info to TrackCircuitSections
        /// </summary>
        private void ProcessTroughs()
        {
            // loop through tracknodes
            foreach (TrackNode node in trackDB.TrackNodes)
            {
                if (node is TrackVectorNode tvn)
                {
                    bool overTrough = false;
                    List<float[]> troughInfo = new List<float[]>();
                    List<int> troughPaths = new List<int>();
                    float[] lastTrough = null;
                    float totalLength = 0f;
                    int numPaths = -1;

                    // loop through all sections in node
                    foreach (TrackVectorSection section in tvn.TrackVectorSections)
                    {
                        if (!tsectiondat.TrackSections.ContainsKey(section.SectionIndex))
                        {
                            continue;  // missing track section
                        }

                        TrackSection trackSection = tsectiondat.TrackSections[section.SectionIndex];

                        // determine length
                        float length = trackSection.Curved ? MathHelper.ToRadians(Math.Abs(trackSection.Angle)) * trackSection.Radius : trackSection.Length;

                        // check trough shape

                        bool troughShape = false;
                        int shapePaths = 0;

                        if (tsectiondat.TrackShapes.ContainsKey(section.ShapeIndex))
                        {
                            TrackShape shape = tsectiondat.TrackShapes[section.ShapeIndex];
                            if (shape.FileName != null)
                            {
                                troughShape = shape.FileName.EndsWith("wtr.s", StringComparison.OrdinalIgnoreCase);
                                shapePaths = Convert.ToInt32(shape.PathsNumber);
                            }
                        }

                        if (troughShape)
                        {
                            numPaths = numPaths < 0 ? shapePaths : Math.Min(numPaths, shapePaths);
                            if (overTrough)
                            {
                                lastTrough[1] += length;
                            }
                            else
                            {
                                lastTrough = new float[2];
                                lastTrough[0] = totalLength;
                                lastTrough[1] = length;
                                overTrough = true;
                            }
                        }
                        else if (overTrough)
                        {
                            troughInfo.Add(lastTrough);
                            troughPaths.Add(numPaths);
                            overTrough = false;
                            numPaths = -1;
                        }
                        totalLength += length;
                    }

                    // add last tunnel item
                    if (overTrough)
                    {
                        troughInfo.Add(lastTrough);
                        troughPaths.Add(numPaths);
                    }

                    // add tunnel info to TrackCircuitSections

                    if (troughInfo.Count > 0)
                    {
                        bool sectionOverTrough = false;
                        float[] troughData = troughInfo[0];
                        float processedLength = 0;

                        for (int i = node.TrackCircuitCrossReferences.Count - 1; i >= 0; i--)
                        {
                            TrackCircuitSectionCrossReference crossRefSection = node.TrackCircuitCrossReferences[i];
                            // forward direction
                            float tcsStartOffset = crossRefSection.OffsetLength[1];
                            float tcsLength = crossRefSection.Length;
                            TrackCircuitSection section = TrackCircuitSection.TrackCircuitList[crossRefSection.Index];

                            // if trough starts in TCS
                            while (troughData != null && troughData[0] <= (tcsStartOffset + tcsLength))
                            {
                                float troughStart = 0;
                                float sectionTroughStart;
                                float startOffset;

                                // if in trough, set start in trough and check end
                                if (sectionOverTrough)
                                {
                                    sectionTroughStart = -1;
                                    startOffset = processedLength;
                                }
                                else
                                // else start new trough
                                {
                                    sectionTroughStart = troughData[0] - tcsStartOffset;
                                    troughStart = sectionTroughStart;
                                    startOffset = -1;
                                }

                                if ((tcsStartOffset + tcsLength) >= (troughData[0] + troughData[1]))  // trough end is in this section
                                {
                                    sectionOverTrough = false;
                                    processedLength = 0;

                                    section.AddTroughData(new TroughInfoData(sectionTroughStart, troughStart + troughData[1] - processedLength, troughData[1] - processedLength, troughData[1], section.Length, startOffset));

                                    if (troughInfo.Count >= 2)
                                    {
                                        troughInfo.RemoveAt(0);
                                        troughData = troughInfo[0];
                                        troughPaths.RemoveAt(0);
                                    }
                                    else
                                    {
                                        troughData = null;
                                        break;  // no more troughs to process
                                    }
                                }
                                else
                                {
                                    sectionOverTrough = true;
                                    processedLength += (tcsLength - troughStart);

                                    section.AddTroughData(new TroughInfoData(sectionTroughStart, -1, tcsLength - troughStart, troughData[1], section.Length, startOffset));
                                    break;  // cannot add more troughs to section
                                }
                            }
                        }
                    }
                }
            }
        }

        //================================================================================================//
        /// <summary>
        /// Find Train
        /// Find train in list using number, to restore reference after restore
        /// </summary>
        public static Train FindTrain(int number, List<Train> trains)
        {
            return trains?.Where(train => train.Number == number).FirstOrDefault();
        }

        //================================================================================================//
        /// <summary>
        /// Request set switch
        /// Manual request to set switch, either from train or direct from node
        /// </summary>
        public static bool RequestSetSwitch(Train train, Direction direction)
        {
            if (null == train)
                throw new ArgumentNullException(nameof(train));

            switch (train.ControlMode)
            {
                case TrainControlMode.Manual: 
                    return train.ProcessRequestManualSetSwitch(direction);
                case TrainControlMode.Explorer: 
                    return train.ProcessRequestExplorerSetSwitch(direction);
                default: 
                    return false;
            }
        }

        public void RequestSetSwitch(int trackCircuitIndex)
        {
            TrackCircuitSection switchSection = TrackCircuitSection.TrackCircuitList[trackCircuitIndex];
            Train train = switchSection.CircuitState.TrainReserved?.Train;
            bool switchReserved = (switchSection.CircuitState.SignalReserved >= 0 || switchSection.CircuitState.TrainClaimed.Count > 0);

            // set physical state

            if (!switchSection.CircuitState.Occupied() && train == null)
            {
                switchSection.JunctionSetManual = switchSection.JunctionLastRoute == 0 ? 1 : 0;
                SetSwitch(switchSection.OriginalIndex, switchSection.JunctionSetManual, switchSection);
            }
            // if switch reserved by manual train then notify train
            else if (train != null && train.ControlMode == TrainControlMode.Manual)
            {
                switchSection.JunctionSetManual = switchSection.JunctionLastRoute == 0 ? 1 : 0;
                train.ProcessRequestManualSetSwitch(switchSection.Index);
            }
            else if (train != null && train.ControlMode == TrainControlMode.Explorer)
            {
                switchSection.JunctionSetManual = switchSection.JunctionLastRoute == 0 ? 1 : 0;
                train.ProcessRequestExplorerSetSwitch(switchSection.Index);
            }
        }

        //only used by MP to manually set a switch to a desired position
        public bool RequestSetSwitch(TrackJunctionNode switchNode, int desiredState)
        {
            if (null == switchNode)
                throw new ArgumentNullException(nameof(switchNode));

            TrackCircuitSection switchSection = TrackCircuitSection.TrackCircuitList[switchNode.TrackCircuitCrossReferences[0].Index];
            bool switchReserved = (switchSection.CircuitState.SignalReserved >= 0 || switchSection.CircuitState.TrainClaimed.Count > 0);
            bool switchSet = false;

            // It must be possible to force a switch also in its present state, not only in the opposite state
            if (!MPManager.IsServer() && switchReserved)
                return false;
            //this should not be enforced in MP, as a train may need to be allowed to go out of the station from the side line

            if (!switchSection.CircuitState.Occupied())
            {
                switchSection.JunctionSetManual = desiredState;
                (trackDB.TrackNodes[switchSection.OriginalIndex] as TrackJunctionNode).SelectedRoute = switchSection.JunctionSetManual;
                switchSection.JunctionLastRoute = switchSection.JunctionSetManual;
                switchSet = true;

                if (!Simulator.Instance.TimetableMode)
                    switchSection.CircuitState.Forced = true;

                foreach (int i in switchSection.LinkedSignals ?? Enumerable.Empty<int>())
                {
                    Signals[i].Update();
                }

                foreach (Train train in Simulator.Instance.Trains)
                {
                    if (train.TrainType != TrainType.Static)
                    {
                        if (train.ControlMode != TrainControlMode.AutoNode && train.ControlMode != TrainControlMode.AutoSignal)
                            train.ProcessRequestExplorerSetSwitch(switchSection.Index);
                        else
                            train.ProcessRequestAutoSetSwitch(switchSection.Index);
                    }
                }
            }
            return switchSet;
        }

        //================================================================================================//

    }// class Signals

}
