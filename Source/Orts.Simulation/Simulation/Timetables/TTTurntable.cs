// COPYRIGHT 2014 by the Open Rails project.
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

// This code processes the Timetable definition and converts it into playable train information
//
// #define DEBUG_POOLINFO
// #define DEBUG_TURNTABLEINFO
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

using Microsoft.Xna.Framework;

using Orts.Common;
using Orts.Common.Calc;
using Orts.Common.Position;
using Orts.Formats.Msts.Models;
using Orts.Formats.OR.Parsers;
using Orts.Simulation.AIs;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.Signalling;
using Orts.Simulation.Track;
using Orts.Simulation.World;

namespace Orts.Simulation.Timetables
{

    //================================================================================================//
    //================================================================================================//
    /// <summary>
    /// Class TimetableTurntablePool
    /// Class holding all details for turntables in timetable mode
    /// Child of TimetablePool
    /// </summary>
    public class TimetableTurntablePool : TimetablePool
    {

        public struct AccessPathDetails
        {
            public TrackCircuitPartialPathRoute AccessPath;           // actual access path
            public Traveller AccessTraveller;                 // traveler based on access path
            public string AccessPathName;                     // access path name
            public int TableVectorIndex;                      // index in VectorList of tracknode which is the table
            public int TableExitIndex;                        // index in table exit list for this exit
            public float TableApproachOffset;                 // offset of holding point in front of turntable (in Inward direction)
            public float TableMiddleEntry;                    // offset of middle of table when approaching table
            public float TableMiddleExit;                     // offset of middle of table when exiting
        }

        public struct TurntableDetails
        {
            public List<AccessPathDetails> AccessPaths;       // access paths details defined for turntable location
            public int TurntableIndex;                        // index for turntable in list of moving tables
            public float TurntableApproachClearanceM;         // required clearance from front of turntable on approach
            public float TurntableReleaseClearanceM;          // required clearance from front of turntabe for release
            public float? TurntableSpeedMpS;                  // set speed for turntable access
            public int? FrameRate;                            // frame rate for turntable movement
        }

        public TurntableDetails AdditionalTurntableDetails;
        private static float defaultTurntableApproachClearanceM = 10.0f;  // default approach clearance
        private static float defaultTurntableReleaseClearanceM = 5.0f;    // default release clearance

        public Simulator Simulatorref { get; protected set; }

        //================================================================================================//
        /// <summary>
        /// constructor for new TimetableTurntablePool
        /// creates TimetableTurntablePool from files .turntable-or
        /// </summary>
        /// <param name="fileContents"></param>
        /// <param name="lineindex"></param>
        /// <param name="simulatorref"></param>
        public TimetableTurntablePool(TimetableReader fileContents, ref int lineindex, Simulator simulatorref)
        {

            bool validpool = true;
            bool newName = false;
            bool firstName = false;
            TurnTable thisTurntable = null;

            string Worldfile = string.Empty;
            int UiD = -1;

            Simulatorref = simulatorref;
            ForceCreation = Simulatorref.Settings.TTCreateTrainOnPoolUnderflow;

            AdditionalTurntableDetails.TurntableApproachClearanceM = defaultTurntableApproachClearanceM;
            AdditionalTurntableDetails.TurntableReleaseClearanceM = defaultTurntableReleaseClearanceM;
            AdditionalTurntableDetails.TurntableSpeedMpS = null;
            AdditionalTurntableDetails.FrameRate = null;

            // loop through definitions
            while (lineindex < fileContents.Strings.Count && !newName)
            {
                string[] inputLine = fileContents.Strings[lineindex];

                // switch through definitions
                switch (inputLine[0].ToLower().Trim())
                {
                    // comment : do not process
                    case "#comment":
                        lineindex++;
                        break;

                    // name : set as name
                    case "#name":
                        newName = firstName;
                        if (!firstName)
                        {
                            lineindex++;
                            firstName = true;
                            PoolName = inputLine[1].ToLower().Trim();
                        }
                        break;

                    // worldfile : read worldfile details
                    case "#worldfile":
                        if (String.IsNullOrEmpty(PoolName))
                        {
                            Trace.TraceInformation("Pool : " + fileContents.FilePath + " : missing pool name \n");
                            validpool = false;
                            lineindex++;
                        }
                        else
                        {
                            Worldfile = inputLine[1].ToLower().Trim();
                            lineindex++;
                        }
                        break;

                    // UiD reference
                    case "#uid":
                        if (String.IsNullOrEmpty(PoolName))
                        {
                            Trace.TraceInformation("Pool : " + fileContents.FilePath + " : missing pool name \n");
                            validpool = false;
                            lineindex++;
                        }
                        else
                        {
                            try
                            {
                                UiD = Convert.ToInt32(inputLine[1].Trim());
                            }
                            catch
                            {
                                Trace.TraceInformation("Pool : " + fileContents.FilePath + " : invalid value for UiD \n");
                            }
                            lineindex++;
                        }
                        break;

                    // access path
                    case "#access":
                        if (String.IsNullOrEmpty(PoolName))
                        {
                            Trace.TraceInformation("Pool : " + fileContents.FilePath + " : missing pool name \n");
                            validpool = false;
                            lineindex++;
                        }
                        else
                        {
                            string accessPath = inputLine[1].ToLower().Trim();
                            bool pathValid = true;
                            TimetableInfo TTInfo = new TimetableInfo(Simulatorref);
                            AIPath newPath = TTInfo.LoadPath(accessPath, out pathValid);

                            if (pathValid)
                            {
                                TrackCircuitRoutePath fullRoute = new TrackCircuitRoutePath(newPath, (TrackDirection)(-2), 1, -1);
                                // if last element is end of track, remove it from path
                                TrackCircuitPartialPathRoute usedRoute = fullRoute.TCRouteSubpaths[0];
                                int lastIndex = usedRoute.Count - 1;
                                if (usedRoute[lastIndex].TrackCircuitSection.CircuitType == TrackCircuitType.EndOfTrack)
                                {
                                    lastIndex = usedRoute.Count - 2;
                                }

                                if (AdditionalTurntableDetails.AccessPaths == null)
                                {
                                    AdditionalTurntableDetails.AccessPaths = new List<AccessPathDetails>();
                                }

                                AccessPathDetails thisAccess = new AccessPathDetails();
                                thisAccess.AccessPath = new TrackCircuitPartialPathRoute(usedRoute);
                                thisAccess.AccessTraveller = new Traveller(Simulatorref.TSectionDat, Simulatorref.TrackDatabase.TrackDB.TrackNodes, newPath);
                                thisAccess.AccessPathName = accessPath;
                                AdditionalTurntableDetails.AccessPaths.Add(thisAccess);
                            }
                            else
                            {
                                Trace.TraceInformation("Pool : " + fileContents.FilePath + " : access path not found : " + accessPath);
                                validpool = false;
                            }
                            lineindex++;
                        }
                        break;

                    // case approach clearance : read clearance value
                    case "#approachclearance":
                        try
                        {
                            AdditionalTurntableDetails.TurntableApproachClearanceM = Convert.ToSingle(inputLine[1].Trim());
                        }
                        catch
                        {
                            Trace.TraceInformation("Pool : " + fileContents.FilePath + " : invalid value for approach clearance : " + inputLine[1].Trim());
                        }
                        lineindex++;
                        break;

                    // case release clearance : read clearance value
                    case "#releaseclearance":
                        try
                        {
                            AdditionalTurntableDetails.TurntableReleaseClearanceM = Convert.ToSingle(inputLine[1].Trim());
                        }
                        catch
                        {
                            Trace.TraceInformation("Pool : " + fileContents.FilePath + " : invalid value for release clearance : " + inputLine[1].Trim());
                        }
                        lineindex++;
                        break;

                    // case turntable speed : read speed (either speedmph or speedkph)
                    case "#speedmph":
                        try
                        {
                            AdditionalTurntableDetails.TurntableSpeedMpS = (float)Speed.MeterPerSecond.FromMpH(Convert.ToSingle(inputLine[1].Trim()));
                        }
                        catch
                        {
                            Trace.TraceInformation("Pool : " + fileContents.FilePath + " : invalid value for speed (mph) : " + inputLine[1].Trim());
                        }
                        lineindex++;
                        break;

                    case "#speedkph":
                        try
                        {
                            AdditionalTurntableDetails.TurntableSpeedMpS = (float)Speed.MeterPerSecond.FromKpH(Convert.ToSingle(inputLine[1].Trim()));
                        }
                        catch
                        {
                            Trace.TraceInformation("Pool : " + fileContents.FilePath + " : invalid value for speed (kph) : " + inputLine[1].Trim());
                        }
                        lineindex++;
                        break;

                    // table frame rate
                    case "#framerate":
                        try
                        {
                            AdditionalTurntableDetails.FrameRate = Convert.ToInt32(inputLine[1].Trim());
                        }
                        catch
                        {
                            Trace.TraceInformation("Pool : " + fileContents.FilePath + " : invalid value for frame rate : " + inputLine[1].Trim());
                        }
                        lineindex++;
                        break;

                    // storage : read path, add to path list
                    case "#storage":
                        if (String.IsNullOrEmpty(PoolName))
                        {
                            Trace.TraceInformation("Pool : " + fileContents.FilePath + " : missing pool name \n");
                            validpool = false;
                            lineindex++;
                        }
                        else
                        {
                            bool validStorage = true;
                            PoolDetails thisPool = ExtractStorage(fileContents, Simulatorref, ref lineindex, out validStorage, false);
                            if (validStorage)
                            {
                                StoragePool.Add(thisPool);
                            }
                            else
                            {
                                validpool = false;
                            }
                        }
                        break;

                    default:
                        Trace.TraceInformation("Pool : " + fileContents.FilePath + " : line : " + (lineindex - 1) + " : unexpected line defitinion : " + inputLine[0] + "\n");
                        lineindex++;
                        break;
                }
            }

            // find turntable reference
            AdditionalTurntableDetails.TurntableIndex = FindTurntable(Worldfile, UiD);

            if (AdditionalTurntableDetails.TurntableIndex < 0)
            {
                validpool = false;
                Trace.TraceInformation("Pool : " + fileContents.FilePath + " not included due to errors in pool definition");
            }

            // check validity of access and storage paths
            // paths must start at turntable
            if (validpool)
            {
                thisTurntable = Simulator.Instance.MovingTables[AdditionalTurntableDetails.TurntableIndex] as TurnTable;

                // check validity for all access paths
                for (int iPath = 0; iPath < AdditionalTurntableDetails.AccessPaths.Count; iPath++)
                {
                    int vectorIndex = CheckTurntablePath(AdditionalTurntableDetails.AccessPaths[iPath].AccessPath, thisTurntable.TrackShapeIndex);

                    if (vectorIndex < 0)
                    {
                        Trace.TraceInformation("Pool : " + PoolName + " : access path " + AdditionalTurntableDetails.AccessPaths[iPath].AccessPathName + " does not link to turntable");
                        validpool = false;
                    }
                    else
                    {
                        AccessPathDetails thisPath = AdditionalTurntableDetails.AccessPaths[iPath];
                        thisPath.TableVectorIndex = vectorIndex;
                        AdditionalTurntableDetails.AccessPaths[iPath] = thisPath;
                    }
                }

                // check validity for all storage paths
                for (int iPath = 0; iPath < StoragePool.Count; iPath++)
                {
                    int vectorIndex = CheckTurntablePath(StoragePool[iPath].StoragePath, thisTurntable.TrackShapeIndex);

                    if (vectorIndex < 0)
                    {
                        Trace.TraceInformation("Pool : " + PoolName + " : storage path " + StoragePool[iPath].StorageName + " does not link to turntable");
                        validpool = false;
                    }
                    else
                    {
                        PoolDetails thisPool = StoragePool[iPath];
                        thisPool.TableVectorIndex = vectorIndex;
                        StoragePool[iPath] = thisPool;
                    }
                }
            }

            // calculate offsets for paths

            if (validpool)
            {
                // access path : hold offset, offset for start of turntable, offset for middle of turntable
                for (int ipath = 0; ipath < AdditionalTurntableDetails.AccessPaths.Count; ipath++)
                {
                    CalculateAccessOffsets(ipath, thisTurntable);
                }

                // storage path : actual length, offset for start of turntable, offset for middle of turntable
                for (int ipath = 0; ipath < StoragePool.Count; ipath++)
                {
                    CalculateStorageOffsets(ipath, thisTurntable);
                }
            }

            // reset poolname if not valid
            if (!validpool)
            {
                PoolName = String.Empty;
            }
        }

        //================================================================================================//
        /// <summary>
        /// constructor for restore
        /// </summary>
        /// <param name="inf"></param>
        /// <param name="simulatorref"></param>
        public TimetableTurntablePool(BinaryReader inf, Simulator simulatorref)
        {
            Simulatorref = simulatorref;

            PoolName = inf.ReadString();
            ForceCreation = inf.ReadBoolean();

            AdditionalTurntableDetails.AccessPaths = new List<AccessPathDetails>();

            int noAccessPaths = inf.ReadInt32();
            for (int iAccessPath = 0; iAccessPath < noAccessPaths; iAccessPath++)
            {
                AccessPathDetails thisAccess = new AccessPathDetails();
                thisAccess.AccessPath = new TrackCircuitPartialPathRoute(inf);
                thisAccess.AccessTraveller = new Traveller(Simulatorref.TSectionDat, Simulatorref.TrackDatabase.TrackDB.TrackNodes, inf);
                thisAccess.AccessPathName = inf.ReadString();
                thisAccess.TableExitIndex = inf.ReadInt32();
                thisAccess.TableVectorIndex = inf.ReadInt32();
                thisAccess.TableApproachOffset = inf.ReadSingle();
                thisAccess.TableMiddleEntry = inf.ReadSingle();
                thisAccess.TableMiddleExit = inf.ReadSingle();

                AdditionalTurntableDetails.AccessPaths.Add(thisAccess);
            }

            AdditionalTurntableDetails.TurntableIndex = inf.ReadInt32();
            AdditionalTurntableDetails.TurntableApproachClearanceM = inf.ReadSingle();
            AdditionalTurntableDetails.TurntableReleaseClearanceM = inf.ReadSingle();
            AdditionalTurntableDetails.TurntableSpeedMpS = null;
            if (inf.ReadBoolean())
            {
                AdditionalTurntableDetails.TurntableSpeedMpS = inf.ReadSingle();
            }
            AdditionalTurntableDetails.FrameRate = null;
            if (inf.ReadBoolean())
            {
                AdditionalTurntableDetails.FrameRate = inf.ReadInt32();
            }

            int noPools = inf.ReadInt32();
            for (int iPool = 0; iPool < noPools; iPool++)
            {
                int maxStorage = 0;

                PoolDetails newPool = new PoolDetails();
                newPool.StoragePath = new TrackCircuitPartialPathRoute(inf);
                newPool.StoragePathTraveller = new Traveller(Simulatorref.TSectionDat, Simulatorref.TrackDatabase.TrackDB.TrackNodes, inf);
                newPool.StorageName = inf.ReadString();

                newPool.AccessPaths = null;

                newPool.StoredUnits = new List<int>();
                int noStoredUnits = inf.ReadInt32();

                for (int iUnits = 0; iUnits < noStoredUnits; iUnits++)
                {
                    newPool.StoredUnits.Add(inf.ReadInt32());
                }

                newPool.ClaimUnits = new List<int>();
                int noClaimUnits = inf.ReadInt32();

                for (int iUnits = 0; iUnits < noClaimUnits; iUnits++)
                {
                    newPool.ClaimUnits.Add(inf.ReadInt32());
                }

                newPool.StorageLength = inf.ReadSingle();
                newPool.StorageCorrection = inf.ReadSingle();

                newPool.TableExitIndex = inf.ReadInt32();
                newPool.TableVectorIndex = inf.ReadInt32();
                newPool.TableMiddleEntry = inf.ReadSingle();
                newPool.TableMiddleExit = inf.ReadSingle();

                newPool.RemLength = inf.ReadSingle();

                maxStorage = inf.ReadInt32();
                if (maxStorage <= 0)
                {
                    newPool.maxStoredUnits = null;
                }
                else
                {
                    newPool.maxStoredUnits = maxStorage;
                }

                StoragePool.Add(newPool);
            }
        }

        //================================================================================================//
        /// <summary>
        /// Method to save pool
        /// </summary>
        /// <param name="outf"></param>
        public override void Save(BinaryWriter outf)
        {
            outf.Write(PoolName);
            outf.Write(ForceCreation);

            // save access path information
            outf.Write(AdditionalTurntableDetails.AccessPaths.Count);
            foreach (AccessPathDetails thisPath in AdditionalTurntableDetails.AccessPaths)
            {
                thisPath.AccessPath.Save(outf);
                thisPath.AccessTraveller.Save(outf);
                outf.Write(thisPath.AccessPathName);
                outf.Write(thisPath.TableExitIndex);
                outf.Write(thisPath.TableVectorIndex);
                outf.Write(thisPath.TableApproachOffset);
                outf.Write(thisPath.TableMiddleEntry);
                outf.Write(thisPath.TableMiddleExit);
            }

            outf.Write(AdditionalTurntableDetails.TurntableIndex);
            outf.Write(AdditionalTurntableDetails.TurntableApproachClearanceM);
            outf.Write(AdditionalTurntableDetails.TurntableReleaseClearanceM);
            outf.Write(AdditionalTurntableDetails.TurntableSpeedMpS.HasValue);
            if (AdditionalTurntableDetails.TurntableSpeedMpS.HasValue)
            {
                outf.Write(AdditionalTurntableDetails.TurntableSpeedMpS.Value);
            }
            outf.Write(AdditionalTurntableDetails.FrameRate.HasValue);
            if (AdditionalTurntableDetails.FrameRate.HasValue)
            {
                outf.Write(AdditionalTurntableDetails.FrameRate.Value);
            }

            // save storage path information
            outf.Write(StoragePool.Count);

            foreach (PoolDetails thisStorage in StoragePool)
            {
                thisStorage.StoragePath.Save(outf);
                thisStorage.StoragePathTraveller.Save(outf);
                outf.Write(thisStorage.StorageName);

                outf.Write(thisStorage.StoredUnits.Count);
                foreach (int storedUnit in thisStorage.StoredUnits)
                {
                    outf.Write(storedUnit);
                }

                outf.Write(thisStorage.ClaimUnits.Count);
                foreach (int claimUnit in thisStorage.ClaimUnits)
                {
                    outf.Write(claimUnit);
                }

                outf.Write(thisStorage.StorageLength);
                outf.Write(thisStorage.StorageCorrection);

                outf.Write(thisStorage.TableExitIndex);
                outf.Write(thisStorage.TableVectorIndex);
                outf.Write(thisStorage.TableMiddleEntry);
                outf.Write(thisStorage.TableMiddleExit);

                outf.Write(thisStorage.RemLength);

                if (thisStorage.maxStoredUnits.HasValue)
                {
                    outf.Write(thisStorage.maxStoredUnits.Value);
                }
                else
                {
                    outf.Write(-1);
                }
            }
        }

        //================================================================================================//
        /// <summary>
        /// Check if path starts at turntable
        /// </summary>

        private int CheckTurntablePath(TrackCircuitPartialPathRoute thisPath, int turntableTrackShape)
        {
            // check if turntable track section is in path - must be in first element (path must start at turntable end)
            int vectorIndex = -1;
            TrackCircuitSection thisSection = thisPath[0].TrackCircuitSection;
            TrackVectorNode thisTDBsection = Simulatorref.TrackDatabase.TrackDB.TrackNodes[thisSection.OriginalIndex] as TrackVectorNode;

            for (int iVector = 0; iVector < thisTDBsection.TrackVectorSections.Length; iVector++)
            {
                TrackVectorSection thisVector = thisTDBsection.TrackVectorSections[iVector];
                if (thisVector.ShapeIndex == turntableTrackShape)
                {
                    vectorIndex = iVector;
                    break;
                }
            }

            return (vectorIndex);
        }

        //================================================================================================//
        /// <summary>
        /// FindTurntable : find reference to turntable as defined in turntable.dat using worldfile and uid references
        /// </summary>
        private int FindTurntable(string worldfile, int uid)
        {
            if (string.IsNullOrEmpty(worldfile))
                return -1;
            // search through all moving tables
            for (int i = 0; i < Simulator.Instance.MovingTables.Count; i++)
            {
                if (worldfile.Equals(Simulator.Instance.MovingTables[i].WFile, StringComparison.OrdinalIgnoreCase) && Simulator.Instance.MovingTables[i].UID == uid)
                {
                    return i;
                }
            }
            return -1;
        }

        //================================================================================================//
        /// <summary>
        /// Calculate offset of timetable position in access path
        /// </summary>

        private void CalculateAccessOffsets(int ipath, TurnTable thisTurntable)
        {
            AccessPathDetails thisPath = AdditionalTurntableDetails.AccessPaths[ipath];

            float baseLength = 0;

            // calculate total length of path sections except first section
            for (int isection = 1; isection < thisPath.AccessPath.Count; isection++)
            {
                baseLength += thisPath.AccessPath[isection].TrackCircuitSection.Length;
            }

            // calculate total length of track sections in first section backward upto turntable section
            TrackCircuitSection thisSection = thisPath.AccessPath[0].TrackCircuitSection;
            int trackNodeIndex = thisSection.OriginalIndex;

            TrackVectorSection[] trackVectors = (Simulatorref.TrackDatabase.TrackDB.TrackNodes[trackNodeIndex] as TrackVectorNode).TrackVectorSections;

            // check if path is in front or behind turntable

            int lastVectorIndex = 0;
            float entrySectionLength = 0.0f;
            float exitSectionLength = 0.0f;

            if (thisPath.TableVectorIndex < thisPath.AccessTraveller.TrackVectorSectionIndex)
            {
                // path is behind turntable - calculate length from turntable to end
                // also set direction forward (path is outbound)
                lastVectorIndex = thisPath.TableVectorIndex + 1;
                entrySectionLength = CalculateVectorLength(lastVectorIndex, trackVectors.Length - 1, lastVectorIndex, trackVectors);
                exitSectionLength = CalculateVectorLength(0, lastVectorIndex - 2, lastVectorIndex, trackVectors);
                thisPath.AccessPath[0].Direction = TrackDirection.Reverse;
                thisPath.AccessTraveller.Direction = Traveller.TravellerDirection.Forward;
            }
            else
            {
                // path is in front of turntable - calculate length from start to turntable
                // also set direction backward (path is outbound)
                lastVectorIndex = thisPath.TableVectorIndex - 1;
                entrySectionLength = CalculateVectorLength(0, lastVectorIndex, lastVectorIndex, trackVectors);
                exitSectionLength = CalculateVectorLength(lastVectorIndex + 2, trackVectors.Length - 1, lastVectorIndex, trackVectors);
                thisPath.AccessPath[0].Direction = TrackDirection.Ahead;
                thisPath.AccessTraveller.Direction = Traveller.TravellerDirection.Backward;
            }

            float totalLength = baseLength + entrySectionLength;

            // deduct clearance for turntable
            // if no explicit clearance defined, use length of last vector before turntable

            thisPath.TableApproachOffset = totalLength - AdditionalTurntableDetails.TurntableApproachClearanceM;
            thisPath.TableMiddleEntry = totalLength + (thisTurntable.Length / 2.0f);
            thisPath.TableMiddleExit = exitSectionLength + (thisTurntable.Length / 2.0f);

#if DEBUG_TURNTABLEINFO
            Trace.TraceInformation("Pool : {0} , calculate access positions for path : {1} :", PoolName, thisPath.AccessPathName);
            Trace.TraceInformation("\n     start offset {0}\n     middle offset [exit] : {1}\n     middle offset [entry] : {2}\n     turntable length : {3}\n     section length : {4}",
                                     thisPath.TableApproachOffset, thisPath.TableMiddleEntry, thisPath.TableMiddleExit, thisTurntable.Length, thisSection.Length);
#endif

            // get turntable exit index
            thisPath.TableExitIndex = thisTurntable.FindExitNode(trackNodeIndex);

            // store updated path
            AdditionalTurntableDetails.AccessPaths[ipath] = thisPath;
        }

        //================================================================================================//
        /// <summary>
        /// Calculate length and turntable offset for storage paths
        /// </summary>

        private void CalculateStorageOffsets(int ipath, TurnTable thisTurntable)
        {
            PoolDetails thisPath = StoragePool[ipath];

            float baseLength = 0;

            // calculate total length of path sections except first section
            for (int isection = 1; isection < thisPath.StoragePath.Count; isection++)
            {
                baseLength += thisPath.StoragePath[isection].TrackCircuitSection.Length;
            }

            // calculate total length of track sections in first section backward upto turntable section
            TrackCircuitSection thisSection = thisPath.StoragePath[0].TrackCircuitSection;
            int trackNodeIndex = thisSection.OriginalIndex;

            TrackVectorSection[] trackVectors = (Simulatorref.TrackDatabase.TrackDB.TrackNodes[trackNodeIndex] as TrackVectorNode).TrackVectorSections;

            // check if path is in front or behind turntable

            int lastVectorIndex = 0;
            float entrySectionLength = 0.0f;
            float exitSectionLength = 0.0f;

            if (thisPath.TableVectorIndex < thisPath.StoragePathTraveller.TrackVectorSectionIndex)
            {
                // path is behind turntable - calculate length from turntable to end
                // also set direction forward (path is outbound)
                lastVectorIndex = thisPath.TableVectorIndex + 1;
                entrySectionLength = CalculateVectorLength(lastVectorIndex, trackVectors.Length - 1, lastVectorIndex, trackVectors);
                exitSectionLength = CalculateVectorLength(0, lastVectorIndex - 2, lastVectorIndex, trackVectors);
                thisPath.StoragePath[0].Direction = TrackDirection.Reverse;
                thisPath.StoragePathTraveller.Direction = Traveller.TravellerDirection.Forward;
            }
            else
            {
                // path is in front of turntable - calculate length from start to turntable
                // also set direction backward (path is outbound)
                lastVectorIndex = thisPath.TableVectorIndex - 1;
                entrySectionLength = CalculateVectorLength(0, lastVectorIndex, lastVectorIndex, trackVectors);
                exitSectionLength = CalculateVectorLength(lastVectorIndex + 2, trackVectors.Length - 1, lastVectorIndex, trackVectors);
                thisPath.StoragePath[0].Direction = TrackDirection.Ahead;
                thisPath.StoragePathTraveller.Direction = Traveller.TravellerDirection.Backward;
            }

            float totalLength = baseLength + entrySectionLength;

            // deduct clearance for turntable

            thisPath.TableMiddleEntry = totalLength + (thisTurntable.Length / 2.0f);
            thisPath.TableMiddleExit = exitSectionLength + (thisTurntable.Length / 2.0f);

#if DEBUG_TURNTABLEINFO
            Trace.TraceInformation("Pool : {0} , calculate access positions for path {1}:", PoolName, thisPath.StorageName);
            Trace.TraceInformation("\n     middle offset [exit] : {0}\n     middle offset [entry] : {1}\n     turntable length : {2}\n     section length : {3}",
                                     thisPath.TableMiddleEntry, thisPath.TableMiddleExit, thisTurntable.Length, thisSection.Length);
#endif

            // get turntable exit index
            thisPath.TableExitIndex = thisTurntable.FindExitNode(trackNodeIndex);

            // store updated path
            StoragePool[ipath] = thisPath;
        }

        //================================================================================================//
        /// <summary>
        /// Calculate length of section connected to turntable
        /// </summary>

        private float CalculateVectorLength(int firstIndex, int LastIndex, int connectIndex, TrackVectorSection[] vectors)
        {
            float returnLength = 0.0f;

            for (int iVector = firstIndex; iVector <= LastIndex; iVector++)
            {
                float thisLength = 0.0f;

                TrackVectorSection thisVector = vectors[iVector];

                if (Simulatorref.TSectionDat.TrackSections.ContainsKey(thisVector.SectionIndex))
                {
                    TrackSection TS = Simulatorref.TSectionDat.TrackSections[thisVector.SectionIndex];

                    if (TS.Curved)
                    {
                        thisLength = MathHelper.ToRadians(Math.Abs(TS.Angle)) * TS.Radius;
                    }
                    else
                    {
                        thisLength = TS.Length;
                    }
                }

                returnLength += thisLength;
            }

            return (returnLength);
        }

        //================================================================================================//
        /// <summary>
        /// Create in pool : create train in pool, for this type of pool train is created directly on storage path
        /// </summary>

        public override TrackCircuitPartialPathRoute CreateInPool(TTTrain train, out int poolStorageIndex, bool checkAccessPath)
        {
            TrackCircuitPartialPathRoute newRoute = null;
            int storageIndex = -1;

            // check if train fits on turntable - if not, reject
            TurnTable thisTurntable = Simulator.Instance.MovingTables[AdditionalTurntableDetails.TurntableIndex] as TurnTable;

            if (train.Length > thisTurntable.Length)
            {
                Trace.TraceWarning($"Train : {train.Name} too long ({train.Length}) for turntable (length {thisTurntable.Length}) in pool {PoolName}\n");
            }
            else
            {
                storageIndex = GetPoolExitIndex(train);
                if (storageIndex < 0)
                {
                    Trace.TraceWarning("Pool : " + PoolName + " : overflow : cannot place train : " + train.Name + "\n");
                }
                else
                {
                    newRoute = new TrackCircuitPartialPathRoute(StoragePool[storageIndex].StoragePath);
                }
            }

            poolStorageIndex = storageIndex;
            return (newRoute);
        }

        //================================================================================================//
        /// <summary>
        /// TestPoolExit : find access path linked to trains path
        /// </summary>

        public override bool TestPoolExit(TTTrain train)
        {
            int testAccess;
            bool validPath = TestPoolAccess(train, out testAccess);
            return (validPath);
        }

        public bool TestPoolAccess(TTTrain train, out int accessIndex)
            { 
            bool validPool = false;
            int reqPath = -1;
            int reqPathIndex = -1;

            // set dispose states
            train.FormsStatic = true;
            train.Closeup = true;

            // find relevant access path

            int lastValidSectionIndex = train.TCRoute.TCRouteSubpaths.Last().Count - 1;
            for (int iSection = train.TCRoute.TCRouteSubpaths.Last().Count - 1; iSection >= 0 && reqPath == -1; iSection--)
            {
                int lastSectionIndex = train.TCRoute.TCRouteSubpaths.Last()[iSection].TrackCircuitSection.Index;
                TrackDirection lastSectionDirection = train.TCRoute.TCRouteSubpaths.Last().Last().Direction;

                for (int iPath = 0; iPath < AdditionalTurntableDetails.AccessPaths.Count && reqPath < 0; iPath++)
                {
                    TrackCircuitPartialPathRoute accessPath = AdditionalTurntableDetails.AccessPaths[iPath].AccessPath;
                    reqPathIndex = accessPath.GetRouteIndex(lastSectionIndex, 0);

                    // path is defined outbound, so directions must be opposite
                    if (reqPathIndex >= 0 && accessPath[reqPathIndex].Direction != lastSectionDirection)
                    {
                        reqPath = iPath;
                        lastValidSectionIndex = iSection;
                    }
                }
            }

            // remove sections from train route if required
            if (lastValidSectionIndex < train.TCRoute.TCRouteSubpaths.Last().Count - 1)
            {
                for (int iSection = train.TCRoute.TCRouteSubpaths.Last().Count - 1; iSection > lastValidSectionIndex; iSection--)
                {
                    train.TCRoute.TCRouteSubpaths.Last().RemoveAt(iSection);
                }
            }

            // none found
            if (reqPath < 0)
            {
                Trace.TraceWarning("Train : " + train.Name + " : no valid path found to access pool storage " + PoolName + "\n");
                train.FormsStatic = false;
                train.Closeup = false;
            }
            // path found
            else
            {
                validPool = true;
            }

            accessIndex = reqPath;
            return (validPool);
        }

        //================================================================================================//
        /// <summary>
        /// Extract train from pool
        /// </summary>

        public override TrainFromPool ExtractTrain(ref TTTrain train, int presentTime)
        {
#if DEBUG_POOLINFO
            var sob = new StringBuilder();
            sob.AppendFormat("Pool {0} : request for train {1} ({2})", PoolName, train.Number, train.Name);
            File.AppendAllText(@"C:\temp\PoolAnal.csv", sob.ToString() + "\n");
#endif
            // check if any engines available
            int selectedTrainNumber = -1;
            int selectedStorage = -1;
            bool claimActive = false;

            for (int iStorage = 0; iStorage < StoragePool.Count; iStorage++)
            {
                PoolDetails thisStorage = StoragePool[iStorage];
                // engine has claimed access - this storage cannot be used for exit right now
                if (thisStorage.ClaimUnits.Count > 0)
                {
                    claimActive = true;
                }
                else if (thisStorage.StoredUnits.Count > 0)
                {
                    selectedTrainNumber = thisStorage.StoredUnits.Last();
                    selectedStorage = iStorage;
                    break;
                }
            }

            // pool underflow : create engine from scratch
            if (selectedTrainNumber < 0)
            {
                // no train found but claim is active - create engine is delayed
                if (claimActive)
                {
                    return (TrainFromPool.Delayed);
                }

                DateTime baseDTA = new DateTime();
                DateTime moveTimeA = baseDTA.AddSeconds(train.AI.clockTime);

                if (ForceCreation)
                {
                    Trace.TraceInformation("Train request : " + train.Name + " from pool " + PoolName +
                        " : no engines available in pool, engine is created, at " + moveTimeA.ToString("HH:mm:ss") + "\n");
#if DEBUG_POOLINFO
                    sob = new StringBuilder();
                    sob.AppendFormat("Pool {0} : train {1} ({2}) : no units available, engine force created", PoolName, train.Number, train.Name);
                    File.AppendAllText(@"C:\temp\PoolAnal.csv", sob.ToString() + "\n");
#endif
                    return (TrainFromPool.ForceCreated);
                }
                else
                {
                    Trace.TraceInformation("Train request : " + train.Name + " from pool " + PoolName +
                        " : no engines available in pool, engine is not created , at " + moveTimeA.ToString("HH:mm:ss") + "\n");
#if DEBUG_POOLINFO
                    sob = new StringBuilder();
                    sob.AppendFormat("Pool {0} : train {1} ({2}) : no units available, enigne not created", PoolName, train.Number, train.Name);
                    File.AppendAllText(@"C:\temp\PoolAnal.csv", sob.ToString() + "\n");
#endif
                    return (TrainFromPool.NotCreated);
                }
            }

            // find required access path
            int firstSectionIndex = train.TCRoute.TCRouteSubpaths[0][0].TrackCircuitSection.Index;

            int reqAccessPath = -1;
            for (int iPath = 0; iPath < AdditionalTurntableDetails.AccessPaths.Count; iPath++)
            {
                TrackCircuitPartialPathRoute thisPath = AdditionalTurntableDetails.AccessPaths[iPath].AccessPath;
                int pathSectionIndex = thisPath.GetRouteIndex(firstSectionIndex, 0);
                if (pathSectionIndex >= 0 && AdditionalTurntableDetails.AccessPaths[iPath].AccessPath[pathSectionIndex].Direction == train.TCRoute.TCRouteSubpaths[0][0].Direction)
                {
                    reqAccessPath = iPath;
                    break;
                }
            }

            // no valid path found
            if (reqAccessPath < 0)
            {
                Trace.TraceInformation("Train request : " + train.Name + " from pool " + PoolName + " : no valid access path found \n");
                return (TrainFromPool.Failed);
            }

            // valid train found - start train
            TTTrain selectedTrain = train.GetOtherTTTrainByNumber(selectedTrainNumber);
            if (selectedTrain == null)
            {
#if DEBUG_POOLINFO
                sob = new StringBuilder();
                sob.AppendFormat("Pool {0} : cannot find train {1} for {2} ({3}) \n", PoolName, selectedTrainNumber, train.Number, train.Name);
                sob.AppendFormat("           stored units : {0}", reqStorage.StoredUnits.Count);
                File.AppendAllText(@"C:\temp\PoolAnal.csv", sob.ToString() + "\n");
#endif
                return (TrainFromPool.Delayed);
            }

            TrackCircuitSection[] occupiedSections = new TrackCircuitSection[selectedTrain.OccupiedTrack.Count];
            selectedTrain.OccupiedTrack.CopyTo(occupiedSections);

            selectedTrain.Forms = -1;
            selectedTrain.RemoveTrain();
            train.FormedOfType = TTTrain.FormCommand.TerminationFormed;

#if DEBUG_POOLINFO
            sob = new StringBuilder();
            sob.AppendFormat("Pool {0} : train {1} ({2}) extracted as {3} ({4}) \n", PoolName, selectedTrain.Number, selectedTrain.Name, train.Number, train.Name);
            sob.AppendFormat("           stored units : {0}", reqStorage.StoredUnits.Count);
            File.AppendAllText(@"C:\temp\PoolAnal.csv", sob.ToString() + "\n");
#endif
            // add access path from turntable to train path (path is defined outbound)
            AccessPathDetails reqPath = AdditionalTurntableDetails.AccessPaths[reqAccessPath];
            train.TCRoute.AddSectionsAtStart(reqPath.AccessPath, train, false);

            // set path of train upto turntable (reverse of storage path)
            PoolDetails reqStorage = StoragePool[selectedStorage];
            TrackCircuitPartialPathRoute reversePath = reqStorage.StoragePath.ReversePath();
            train.TCRoute.AddSubrouteAtStart(reversePath, train);
            train.ValidRoute[0] = new TrackCircuitPartialPathRoute(train.TCRoute.TCRouteSubpaths[0]);

            // set details for new train from existing train
            bool validFormed = train.StartFromAITrain(selectedTrain, presentTime, occupiedSections);

            if (validFormed)
            {
                train.InitializeSignals(true);

                // start new train
                if (Simulatorref.StartReference.Contains(train.Number))
                {
                    Simulatorref.StartReference.Remove(train.Number);
                }

                // existing train is player, so continue as player
                if (selectedTrain.TrainType == TrainType.Player)
                {
                    train.AI.TrainsToRemoveFromAI.Add(train);

                    // set proper details for new formed train
                    train.OrgAINumber = train.Number;
                    train.Number = 0;
                    train.LeadLocomotiveIndex = selectedTrain.LeadLocomotiveIndex;
                    for (int carid = 0; carid < train.Cars.Count; carid++)
                    {
                        train.Cars[carid].CarID = selectedTrain.Cars[carid].CarID;
                    }
                    train.AI.TrainsToAdd.Add(train);
                    Simulator.Instance.Trains.Add(train);

                    train.SetFormedOccupied();
                    train.TrainType = TrainType.Player;
                    train.ControlMode = TrainControlMode.Inactive;
                    train.MovementState = AiMovementState.Static;

                    // inform viewer about player train switch
                    Simulator.Instance.PlayerLocomotive = train.LeadLocomotive;
                    Simulator.Instance.OnPlayerLocomotiveChanged();

                    Simulator.Instance.OnPlayerTrainChanged(selectedTrain, train);
                    Simulator.Instance.PlayerLocomotive.Train = train;

                    train.SetupStationStopHandling();

                    // clear replay commands
                    Simulator.Instance.Log.CommandList.Clear();

                    // display messages
                    Simulator.Instance.Confirmer?.Information("Player switched to train : " + train.Name); // As Confirmer may not be created until after a restore.
                }

                // new train is intended as player
                else if (train.TrainType == TrainType.Player || train.TrainType == TrainType.PlayerIntended)
                {
                    train.TrainType = TrainType.Player;
                    train.ControlMode = TrainControlMode.Inactive;
                    train.MovementState = AiMovementState.Static;

                    train.AI.TrainsToAdd.Add(train);

                    // set player locomotive
                    // first test first and last cars - if either is drivable, use it as player locomotive
                    if (train.Cars.First().IsDriveable)
                    {
                        Simulatorref.PlayerLocomotive = train.LeadLocomotive = train.Cars.First();
                    }
                    else if (train.Cars.Last().IsDriveable)
                    {
                        Simulatorref.PlayerLocomotive = train.LeadLocomotive = train.Cars.Last();
                    }
                    else
                    {
                        foreach (TrainCar car in train.Cars)
                        {
                            if (car.IsDriveable)  // first loco is the one the player drives
                            {
                                Simulatorref.PlayerLocomotive = train.LeadLocomotive = car;
                                break;
                            }
                        }
                    }

                    train.InitializeBrakes();

                    if (Simulatorref.PlayerLocomotive == null)
                    {
                        throw new InvalidDataException("Can't find player locomotive in " + train.Name);
                    }
                    else
                    {
                        foreach (TrainCar car in train.Cars)
                        {
                            if (car.WagonType == TrainCar.WagonTypes.Engine)
                            {
                                MSTSLocomotive loco = car as MSTSLocomotive;
                                loco.AntiSlip = train.leadLocoAntiSlip;
                            }
                        }
                    }
                }

                // normal AI train
                else
                {
                    // set delay
                    float randDelay = RandomNumberGenerator.GetInt32(train.DelayedStartSettings.newStart.randomPartS * 10);
                    train.RestdelayS = train.DelayedStartSettings.newStart.fixedPartS + (randDelay / 10f);
                    train.DelayedStart = true;
                    train.DelayedStartState = AiStartMovement.NewTrain;

                    train.TrainType = TrainType.Ai;
                    train.AI.TrainsToAdd.Add(train);
                }

                train.SetFormedOccupied();

                // update any outstanding required actions adding the added length
                train.ResetActions(true);

                // set forced consist name if required
                if (!String.IsNullOrEmpty(train.ForcedConsistName))
                {
                    foreach (var car in train.Cars)
                    {
                        car.OrgConsist = train.ForcedConsistName;
                    }
                }

                // set timetable turntable class
                train.ActiveTurntable = new TimetableTurntableControl(this, PoolName, AdditionalTurntableDetails.TurntableIndex, Simulatorref, train);
                train.ActiveTurntable.MovingTableState = TimetableTurntableControl.MovingTableStateEnum.WaitingMovingTableAvailability;
                train.ActiveTurntable.MovingTableAction = TimetableTurntableControl.MovingTableActionEnum.FromStorage;
                train.ActiveTurntable.AccessPathIndex = reqAccessPath;
                train.ActiveTurntable.StoragePathIndex = selectedStorage;
                train.MovementState = AiMovementState.Static;
                train.ActivateTime = presentTime - 1; // train is immediately activated
            }
            else
            {
                return (TrainFromPool.Failed);
            }

            // update pool data
            reqStorage.StoredUnits.Remove(selectedTrainNumber);

            // get last train in storage
            TTTrain storedTrain = null;

            if (reqStorage.StoredUnits.Count > 0)
            {
                int trainNumber = reqStorage.StoredUnits.Last();
                storedTrain = train.GetOtherTTTrainByNumber(trainNumber);

                if (storedTrain != null)
                {
                    reqStorage.RemLength = CalculateStorageLength(reqStorage, storedTrain);
                }
                else
                {
                    Trace.TraceWarning("Error in pool {0} : stored units : {1} : train no. {2} not found\n", PoolName, reqStorage.StoredUnits.Count, trainNumber);
                    reqStorage.StoredUnits.RemoveAt(reqStorage.StoredUnits.Count - 1);

                    trainNumber = reqStorage.StoredUnits.Last();
                    storedTrain = train.GetOtherTTTrainByNumber(trainNumber);

                    if (storedTrain != null)
                    {
                        reqStorage.RemLength = CalculateStorageLength(reqStorage, storedTrain);
                    }
                }
            }
            else
            {
                reqStorage.RemLength = reqStorage.StorageLength;
            }

            StoragePool[selectedStorage] = reqStorage;

            return (TrainFromPool.Formed);
        }

        //================================================================================================//
        /// <summary>
        /// SetPoolExit : adjust train dispose details and path to required pool exit
        /// Returned poolStorageState : <0 : state (enum TTTrain.PoolAccessState); >0 : poolIndex
        /// </summary>

        public override TrackCircuitPartialPathRoute SetPoolExit(TTTrain train, out int poolStorageState, bool checkAccessPath)
        {
            // new route
            TrackCircuitPartialPathRoute newRoute = null;
            poolStorageState = -1;

            // set dispose states
            train.FormsStatic = true;
            train.Closeup = true;

            // find storage path with enough space to store train

            int reqPool = GetPoolExitIndex(train);
            int reqPath = 0;

            // no storage space found : pool overflow
            if (reqPool == (int)TTTrain.PoolAccessState.PoolOverflow)
            {
                Trace.TraceWarning("Pool : " + PoolName + " : overflow : cannot place train : " + train.Name + "\n");

                // train will be abandoned when reaching end of path
                train.FormsStatic = false;
                train.Closeup = false;
            }

            // no valid pool found
            else if (reqPool == (int)TTTrain.PoolAccessState.PoolInvalid)
            {
                // pool invalid
                Trace.TraceWarning("Pool : " + PoolName + " : no valid pool found : " + train.Name + "\n");

                // train will be abandoned when reaching end of path
                train.FormsStatic = false;
                train.Closeup = false;
            }

            // no action if state is poolClaimed - state will resolve as train ahead is stabled in pool

            // valid pool
            else if (reqPool > 0)
            {
                if (checkAccessPath)
                {
                    bool validPath = TestPoolAccess(train, out reqPath);

                    // none found
                    if (!validPath)
                    {
                        Trace.TraceWarning("Train : " + train.Name + " : no valid path found to access pool storage " + PoolName + "\n");
                        train.FormsStatic = false;
                        train.Closeup = false;
                        poolStorageState = -1;
                    }
                    // path found : extend train path with access paths
                    else
                    {
                        TrackCircuitPartialPathRoute accessPath = AdditionalTurntableDetails.AccessPaths[reqPath].AccessPath;
                        newRoute = new TrackCircuitPartialPathRoute(train.TCRoute.TCRouteSubpaths.Last());

                        // add elements from access route except those allready on the path
                        // add in reverse order and reverse direction as path is defined outbound
                        for (int iElement = accessPath.Count - 1; iElement >= 0; iElement--)
                        {
                            if (newRoute.GetRouteIndex(accessPath[iElement].TrackCircuitSection.Index, 0) < 0)
                            {
                                TrackCircuitRouteElement newElement = new TrackCircuitRouteElement(accessPath[iElement]);
                                newElement.Direction = newElement.Direction.Reverse();
                                newRoute.Add(newElement);
                            }
                        }

                        poolStorageState = reqPool;
                    }
                }
                // create new route from access track only
                // use first defined access track
                // reverse path as path is outbound
                else
                {
                    newRoute = new TrackCircuitPartialPathRoute(AdditionalTurntableDetails.AccessPaths[0].AccessPath.ReversePath());
                    poolStorageState = reqPool;
                }
            }

            // if route is valid, set state for last section to approach moving table
            // also add unit to storage as claim
            if (newRoute != null)
            {
                newRoute.Last().MovingTableApproachPath = reqPath;
                AddUnit(train, true);
                StoragePool[poolStorageState].ClaimUnits.Add(train.Number);
            }
            return (newRoute);
        }

        //================================================================================================//
        /// <summary>
        /// Get end of route distance on approach to turntable
        /// </summary>

        internal override float GetEndOfRouteDistance(TrackCircuitPartialPathRoute thisRoute, TrackCircuitPosition frontPosition, int pathIndex)
        {
            // get distance to approach point from present position of train
            int turntableSectionIndex = thisRoute.GetRouteIndex(AdditionalTurntableDetails.AccessPaths[pathIndex].AccessPath[0].TrackCircuitSection.Index, 0);
            float startoffset = TrackCircuitSection.TrackCircuitList[frontPosition.TrackCircuitSectionIndex].Length - frontPosition.Offset;
            float distanceToTurntable = thisRoute.GetDistanceAlongRoute(frontPosition.RouteListIndex, startoffset,
                turntableSectionIndex, AdditionalTurntableDetails.AccessPaths[pathIndex].TableApproachOffset, true);

            return (distanceToTurntable);
        }
    }

    //================================================================================================//
    //================================================================================================//
    /// <summary>
    /// Class to hold additional info and methods for use of turntable in timetable mode
    /// </summary>

    public class TimetableTurntableControl
    {
        private TurnTable parentTurntable;                          // parent turntable
        private int parentIndex;                                    // index of parent turntable in moving table list

        private TimetableTurntablePool parentPool;                  // parent pool
        private string poolName;                                    // parent pool name

        private TTTrain parentTrain;                         // train linked to turntable actions

        public enum MovingTableStateEnum
        {
            WaitingMovingTableAvailability,
            WaitingAccessToMovingTable,
            AccessToMovingTable,
            AccessOnMovingTable,
            WaitingStorageToMovingTable,
            StorageToMovingTable,
            StorageOnMovingTable,
            Completed,
            Inactive,
        }

        public MovingTableStateEnum MovingTableState = MovingTableStateEnum.Inactive;     // state of this turntable

        public enum MovingTableActionEnum
        {
            FromAccess,
            FromStorage,
            Turning,
            Undefined,
        }

        public MovingTableActionEnum MovingTableAction = MovingTableActionEnum.Undefined; // type of action 

        public int StoragePathIndex;                       // index of selected storage path
        public int AccessPathIndex;                        // index of selected access path

        private TrainOnMovingTable trainOnTable;            // class for train on table information
        private int reqTurntableExit;                              // index of required exit
        private bool reqReverseFormation;                          // train exits table in reverse formation
        private float clearingDistanceM;                           // distance for train to move to clear turntable
        private float originalTrainMaxSpeedMpS;                    // original allowed train max speed
        private float originalSpeedSignalMpS;                      // original signal speed limit
        private float originalSpeedLimitMpS;                       // original speedpost speed limit
        private float stopPositionOnTurntableM;                    // actual stop position on turntable

        //================================================================================================//
        // constructor from new
        public TimetableTurntableControl(TimetableTurntablePool thisPool, string thisPoolName, int turntableIndex, Simulator simulatorref, TTTrain train)
        {
            parentPool = thisPool;
            poolName = thisPoolName;
            parentIndex = turntableIndex;
            parentTrain = train;
            parentTurntable = simulatorref.MovingTables[parentIndex] as TurnTable;

            // set defined framerate if defined and not yet set for turntable
            if (parentPool.AdditionalTurntableDetails.FrameRate.HasValue && !parentTurntable.TurntableFrameRate.HasValue)
            {
                parentTurntable.TurntableFrameRate = parentPool.AdditionalTurntableDetails.FrameRate.Value;
            }
        }

        //================================================================================================//
        // constructor for restore
        public TimetableTurntableControl(BinaryReader inf, Simulator simulatorref, TTTrain train)
        {
            parentIndex = inf.ReadInt32();
            parentTurntable = null;  // cannot be restored as turntables are restored later

            poolName = inf.ReadString();
            parentPool = simulatorref.PoolHolder.Pools[poolName] as TimetableTurntablePool;

            parentTrain = train;

            MovingTableState = (MovingTableStateEnum)inf.ReadInt32();
            MovingTableAction = (MovingTableActionEnum)inf.ReadInt32();
            StoragePathIndex = inf.ReadInt32();
            AccessPathIndex = inf.ReadInt32();
            reqReverseFormation = inf.ReadBoolean();
            reqTurntableExit = inf.ReadInt32();

            clearingDistanceM = inf.ReadSingle();
            originalTrainMaxSpeedMpS = inf.ReadSingle();
            originalSpeedSignalMpS = inf.ReadSingle();
            originalSpeedLimitMpS = inf.ReadSingle();
            stopPositionOnTurntableM = inf.ReadSingle();

            trainOnTable = null;
            if (inf.ReadBoolean())
            {
                trainOnTable = new TrainOnMovingTable(parentTrain);
                trainOnTable.Restore(inf, parentTrain); // must be explicitly restored as train is not yet available in train dictionary
            }
        }

        //================================================================================================//
        /// <summary>
        /// method to save class
        /// </summary>

        public void Save(BinaryWriter outf)
        {
            outf.Write(parentIndex);
            outf.Write(poolName);

            outf.Write((int)MovingTableState);
            outf.Write((int)MovingTableAction);
            outf.Write(StoragePathIndex);
            outf.Write(AccessPathIndex);
            outf.Write(reqReverseFormation);
            outf.Write(reqTurntableExit);
            outf.Write(clearingDistanceM);
            outf.Write(originalTrainMaxSpeedMpS);
            outf.Write(originalSpeedSignalMpS);
            outf.Write(originalSpeedLimitMpS);
            outf.Write(stopPositionOnTurntableM);

            if (trainOnTable != null)
            {
                outf.Write(true);
                trainOnTable.Save(outf);
            }
            else
            {
                outf.Write(false);
            }
        }

        //================================================================================================//
        /// <summary>
        /// Check if turntable is available for this train
        /// </summary>

        public bool CheckTurntableAvailable()
        {
            if (parentTurntable == null) 
                parentTurntable = Simulator.Instance.MovingTables[parentIndex] as TurnTable;

            bool available = true;
            // check if waiting for turntable availability
            if (MovingTableState == MovingTableStateEnum.WaitingMovingTableAvailability)
            {
                available = false;

                // turntable is not available
                if (parentTurntable.InUse)
                {
                    if (!parentTurntable.WaitingTrains.Contains(parentTrain.Number))
                    {
                        parentTurntable.WaitingTrains.Enqueue(parentTrain.Number);
                    }
                }
                else
                {
                    if (parentTurntable.WaitingTrains.Count < 1 || parentTurntable.WaitingTrains.Peek() == parentTrain.Number)
                    {
                        if (parentTurntable.WaitingTrains.Count >= 1) parentTurntable.WaitingTrains.Dequeue();
                        available = true;
                        parentTurntable.InUse = true;
                        switch (MovingTableAction)
                        {
                            case TimetableTurntableControl.MovingTableActionEnum.FromAccess:
                                MovingTableState = TimetableTurntableControl.MovingTableStateEnum.WaitingAccessToMovingTable;
                                AccessPathIndex = GetAccessPathIndex();
                                StoragePathIndex = parentTrain.PoolStorageIndex;
                                break;

                            case TimetableTurntableControl.MovingTableActionEnum.FromStorage:
                                MovingTableState = TimetableTurntableControl.MovingTableStateEnum.WaitingStorageToMovingTable;
                                break;

                            default:
                                MovingTableState = TimetableTurntableControl.MovingTableStateEnum.Inactive;
                                break;
                        }
                    }
                }
            }
            return (available);
        }

        //================================================================================================//
        /// <summary>
        /// Perform update for train and turntable depending on action state (for AI trains)
        /// </summary>
        /// return : true if turntable actions for this train have terminated
        /// Instance of class will then be removed by train

        public void UpdateTurntableStateAI(double elapsedClockSeconds, int presentTime)
        {
            if (parentTurntable == null) 
                parentTurntable = Simulator.Instance.MovingTables[parentIndex] as TurnTable;

            int reqTurntableExit = -1;
            int reqTurntableEntry = -1;

            Traveller.TravellerDirection reqEntryDirection = Traveller.TravellerDirection.Forward;
            Traveller.TravellerDirection reqExitDirection = Traveller.TravellerDirection.Forward;

            // switch on turntable action state

            switch (MovingTableState)
            {

                // state : waiting for table (from Access)
                case TimetableTurntableControl.MovingTableStateEnum.WaitingAccessToMovingTable:

                    reqTurntableEntry = parentPool.StoragePool[StoragePathIndex].TableExitIndex;
                    reqEntryDirection = parentPool.StoragePool[StoragePathIndex].StoragePathTraveller.Direction;

                    reqTurntableExit = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableExitIndex;
                    reqExitDirection = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].AccessTraveller.Direction;

                    if (AutoRequestExit(reqTurntableExit, reqEntryDirection, reqExitDirection, elapsedClockSeconds))
                    {
                        parentTrain.DelayedStartMoving(AiStartMovement.Turntable);
                        MovingTableState = TimetableTurntableControl.MovingTableStateEnum.AccessToMovingTable;
                        parentTrain.EndAuthorityTypes[0] = EndAuthorityType.EndOfPath;

                        // calculate end position
                        parentTrain.DistanceToEndNodeAuthorityM[0] = CalculateDistanceToTurntable();

                        // set reduced speed
                        float reqTrainSpeed = parentPool.AdditionalTurntableDetails.TurntableSpeedMpS.HasValue ?
                            Math.Min(parentPool.AdditionalTurntableDetails.TurntableSpeedMpS.Value, parentTrain.SpeedSettings.movingtableSpeedMpS.Value) :
                            parentTrain.SpeedSettings.movingtableSpeedMpS.Value;

                        originalTrainMaxSpeedMpS = parentTrain.TrainMaxSpeedMpS;
                        originalSpeedSignalMpS = parentTrain.AllowedMaxSpeedSignalMpS;
                        originalSpeedLimitMpS = parentTrain.AllowedMaxSpeedLimitMpS;
                        parentTrain.TrainMaxSpeedMpS = reqTrainSpeed;
                        parentTrain.AllowedMaxSpeedMpS = Math.Min(parentTrain.AllowedMaxSpeedMpS, parentTrain.TrainMaxSpeedMpS);

                        // add storage path
                        parentTrain.TCRoute.AddSubrouteAtEnd(parentPool.StoragePool[parentTrain.PoolStorageIndex].StoragePath);
                    }
                    break;

                // state : waiting for table (from storage)
                case TimetableTurntableControl.MovingTableStateEnum.WaitingStorageToMovingTable:

                    reqTurntableEntry = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableExitIndex;
                    reqEntryDirection = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].AccessTraveller.Direction;

                    reqTurntableExit = parentPool.StoragePool[StoragePathIndex].TableExitIndex;
                    reqExitDirection = parentPool.StoragePool[StoragePathIndex].StoragePathTraveller.Direction;

                    if (AutoRequestExit(reqTurntableExit, reqEntryDirection, reqExitDirection, elapsedClockSeconds))
                    {
                        parentTrain.DelayedStartMoving(AiStartMovement.Turntable);
                        MovingTableState = TimetableTurntableControl.MovingTableStateEnum.StorageToMovingTable;
                        parentTrain.EndAuthorityTypes[0] = EndAuthorityType.EndOfPath;

                        // calculate end position
                        parentTrain.DistanceToEndNodeAuthorityM[0] = CalculateDistanceToTurntable();

                        // set reduced speed
                        float reqTrainSpeed = parentPool.AdditionalTurntableDetails.TurntableSpeedMpS.HasValue ?
                            Math.Min(parentPool.AdditionalTurntableDetails.TurntableSpeedMpS.Value, parentTrain.SpeedSettings.movingtableSpeedMpS.Value) :
                            parentTrain.SpeedSettings.movingtableSpeedMpS.Value;

                        originalTrainMaxSpeedMpS = parentTrain.TrainMaxSpeedMpS;
                        originalSpeedSignalMpS = parentTrain.AllowedMaxSpeedSignalMpS;
                        originalSpeedLimitMpS = parentTrain.AllowedMaxSpeedLimitMpS;
                        parentTrain.TrainMaxSpeedMpS = reqTrainSpeed;
                        parentTrain.AllowedMaxSpeedMpS = Math.Min(parentTrain.AllowedMaxSpeedMpS, parentTrain.TrainMaxSpeedMpS);
                    }
                    break;

                // state : moving onto turntable (from Access)
                // exit from this state is through UpdateBrakingState and SetNextStageOnStopped
                case TimetableTurntableControl.MovingTableStateEnum.AccessToMovingTable:

                    parentTrain.DistanceToEndNodeAuthorityM[0] = CalculateDistanceToTurntable();
                    parentTrain.UpdateBrakingState(elapsedClockSeconds, presentTime);
                    break;


                // state : moving onto turntable (from Storage)
                // exit from this state is through UpdateBrakingState and SetNextStageOnStopped
                case TimetableTurntableControl.MovingTableStateEnum.StorageToMovingTable:

                    parentTrain.DistanceToEndNodeAuthorityM[0] = CalculateDistanceToTurntable();
                    parentTrain.UpdateBrakingState(elapsedClockSeconds, presentTime);
                    break;

                // state : turning on turntable (from Access)
                // get required exit
                // exit from this state is through PrepareMoveOffTable and SetNextStageOnStopped 
                case TimetableTurntableControl.MovingTableStateEnum.AccessOnMovingTable:

                    reqTurntableEntry = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableExitIndex;
                    reqEntryDirection = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].AccessTraveller.Direction;

                    reqTurntableExit = parentPool.StoragePool[StoragePathIndex].TableExitIndex;
                    reqExitDirection = parentPool.StoragePool[StoragePathIndex].StoragePathTraveller.Direction;

                    AutoRequestExit(reqTurntableExit, reqEntryDirection, reqExitDirection, elapsedClockSeconds);
                    break;

                // state : turning on turntable (from Storage)
                // get required exit
                // exit from this state is through PrepareMoveOffTable and SetNextStageOnStopped 
                case TimetableTurntableControl.MovingTableStateEnum.StorageOnMovingTable:

                    reqTurntableEntry = parentPool.StoragePool[StoragePathIndex].TableExitIndex;
                    reqEntryDirection = parentPool.StoragePool[StoragePathIndex].StoragePathTraveller.Direction;

                    reqTurntableExit = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableExitIndex;
                    reqExitDirection = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].AccessTraveller.Direction;

                    AutoRequestExit(reqTurntableExit, reqEntryDirection, reqExitDirection, elapsedClockSeconds);
                    break;

                default:
                    break;
            }

            return;
        }

        //================================================================================================//
        /// <summary>
        /// Perform update for train and turntable depending on action state (for player train)
        /// </summary>
        /// return : true if turntable actions for this train have terminated
        /// Instance of class will then be removed by train

        public bool UpdateTurntableStatePlayer(double elapsedClockSeconds)
        {
            if (parentTurntable == null) 
                parentTurntable = Simulator.Instance.MovingTables[parentIndex] as TurnTable;

            bool terminated = false;
            int reqTurntableExit = -1;
            int reqTurntableEntry = -1;

            Traveller.TravellerDirection reqEntryDirection = Traveller.TravellerDirection.Forward;
            Traveller.TravellerDirection reqExitDirection = Traveller.TravellerDirection.Forward;

            // switch on turntable action state

            switch (MovingTableState)
            {
                // state : waiting for table to become available
                case MovingTableStateEnum.WaitingMovingTableAvailability:
                    if (parentTurntable.InUse)
                    {
                        if (!parentTurntable.WaitingTrains.Contains(parentTrain.Number))
                        {
                            parentTurntable.WaitingTrains.Enqueue(parentTrain.Number);
                        }
                    }
                    else
                    {
                        if (parentTurntable.WaitingTrains.Count < 1 || parentTurntable.WaitingTrains.Peek() == parentTrain.Number)
                        {
                            if (parentTurntable.WaitingTrains.Count >= 1) parentTurntable.WaitingTrains.Dequeue();
                            parentTurntable.InUse = true;

                            if (MovingTableAction == MovingTableActionEnum.FromAccess)
                            {
                                MovingTableState = TimetableTurntableControl.MovingTableStateEnum.WaitingAccessToMovingTable;
                                AccessPathIndex = GetAccessPathIndex();
                                StoragePathIndex = parentTrain.PoolStorageIndex;
                            }
                            else
                            {
                                MovingTableState = TimetableTurntableControl.MovingTableStateEnum.WaitingStorageToMovingTable;
                            }
                        }
                    }
                    break;

                // state : waiting for table (from Access)
                case TimetableTurntableControl.MovingTableStateEnum.WaitingAccessToMovingTable:

                    reqTurntableEntry = parentPool.StoragePool[StoragePathIndex].TableExitIndex;
                    reqEntryDirection = parentPool.StoragePool[StoragePathIndex].StoragePathTraveller.Direction;

                    reqTurntableExit = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableExitIndex;
                    reqExitDirection = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].AccessTraveller.Direction;

                    if (AutoRequestExit(reqTurntableExit, reqEntryDirection, reqExitDirection, elapsedClockSeconds))
                    {
                        MovingTableState = TimetableTurntableControl.MovingTableStateEnum.AccessToMovingTable;

                        // calculate end position - place in front of timetable incl. clearance
                        parentTrain.EndAuthorityTypes[0] = EndAuthorityType.EndOfPath;
                        parentTrain.DistanceToEndNodeAuthorityM[0] =
                            CalculateDistanceToTurntable() - (parentTurntable.Length / 2.0f) - parentPool.AdditionalTurntableDetails.TurntableApproachClearanceM;

                        // set reduced speed
                        float reqTrainSpeed = parentPool.AdditionalTurntableDetails.TurntableSpeedMpS.HasValue ?
                            Math.Min(parentPool.AdditionalTurntableDetails.TurntableSpeedMpS.Value, parentTrain.SpeedSettings.movingtableSpeedMpS.Value) :
                            parentTrain.SpeedSettings.movingtableSpeedMpS.Value;

                        originalTrainMaxSpeedMpS = parentTrain.TrainMaxSpeedMpS;
                        originalSpeedSignalMpS = parentTrain.AllowedMaxSpeedSignalMpS;
                        originalSpeedLimitMpS = parentTrain.AllowedMaxSpeedLimitMpS;
                        parentTrain.TrainMaxSpeedMpS = reqTrainSpeed;
                        parentTrain.AllowedMaxSpeedMpS = Math.Min(parentTrain.AllowedMaxSpeedMpS, parentTrain.TrainMaxSpeedMpS);

                        // add storage path
                        parentTrain.TCRoute.AddSubrouteAtEnd(parentPool.StoragePool[parentTrain.PoolStorageIndex].StoragePath);

                        // send message
                        var message = Simulator.Catalog.GetString("Turntable is ready for access - allowed speed set to {0}", FormatStrings.FormatSpeedDisplay(parentTrain.AllowedMaxSpeedMpS, Simulator.Instance.MilepostUnitsMetric));
                        Simulator.Instance.Confirmer.Information(message);

                        // create train-on-table class
                        trainOnTable = new TrainOnMovingTable(parentTrain);
                        trainOnTable.SetFrontState(false);
                        trainOnTable.SetBackState(false);
                    }
                    break;

                // state : waiting for table (from storage)
                case TimetableTurntableControl.MovingTableStateEnum.WaitingStorageToMovingTable:

                    reqTurntableEntry = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableExitIndex;
                    reqEntryDirection = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].AccessTraveller.Direction;

                    reqTurntableExit = parentPool.StoragePool[StoragePathIndex].TableExitIndex;
                    reqExitDirection = parentPool.StoragePool[StoragePathIndex].StoragePathTraveller.Direction;

                    if (AutoRequestExit(reqTurntableExit, reqEntryDirection, reqExitDirection, elapsedClockSeconds))
                    {
                        MovingTableState = TimetableTurntableControl.MovingTableStateEnum.StorageToMovingTable;

                        // calculate end position - place in front of timetable incl. clearance
                        parentTrain.EndAuthorityTypes[0] = EndAuthorityType.EndOfPath;
                        parentTrain.DistanceToEndNodeAuthorityM[0] =
                            CalculateDistanceToTurntable() - (parentTurntable.Length / 2.0f) - parentPool.AdditionalTurntableDetails.TurntableApproachClearanceM;

                        // set reduced speed
                        float reqTrainSpeed = parentPool.AdditionalTurntableDetails.TurntableSpeedMpS.HasValue ?
                            Math.Min(parentPool.AdditionalTurntableDetails.TurntableSpeedMpS.Value, parentTrain.SpeedSettings.movingtableSpeedMpS.Value) :
                            parentTrain.SpeedSettings.movingtableSpeedMpS.Value;

                        originalTrainMaxSpeedMpS = parentTrain.TrainMaxSpeedMpS;
                        originalSpeedSignalMpS = parentTrain.AllowedMaxSpeedSignalMpS;
                        originalSpeedLimitMpS = parentTrain.AllowedMaxSpeedLimitMpS;
                        parentTrain.TrainMaxSpeedMpS = reqTrainSpeed;
                        parentTrain.AllowedMaxSpeedMpS = Math.Min(parentTrain.AllowedMaxSpeedMpS, parentTrain.TrainMaxSpeedMpS);

                        // send message
                        var message = Simulator.Catalog.GetString("Turntable is ready for access - allowed speed set to {0}", FormatStrings.FormatSpeedDisplay(parentTrain.AllowedMaxSpeedMpS, Simulator.Instance.MilepostUnitsMetric));
                        Simulator.Instance.Confirmer.Information(message);

                        // create train-on-table class
                        trainOnTable = new TrainOnMovingTable(parentTrain);
                        trainOnTable.SetFrontState(false);
                        trainOnTable.SetBackState(false);
                    }
                    break;

                // state : moving onto turntable (from Access)
                case TimetableTurntableControl.MovingTableStateEnum.AccessToMovingTable:

                    // set end of authority beyond turntable
                    parentTrain.EndAuthorityTypes[0] = EndAuthorityType.EndOfPath;
                    parentTrain.DistanceToEndNodeAuthorityM[0] = CalculateDistanceToTurntable() + (parentTurntable.Length / 2.0f);

                    // check if train position on turntable
                    if (!trainOnTable.FrontOnBoard)
                    {
                        if (WorldLocation.Within(parentTrain.FrontTDBTraveller.WorldLocation, parentTurntable.WorldPosition.WorldLocation, parentTurntable.Length / 2))
                        {
                            trainOnTable.SetFrontState(true);
                            Simulator.Instance.Confirmer.Information("Front of train is on table");

                        }
                    }
                    else if (!trainOnTable.BackOnBoard)
                    {
                        if (WorldLocation.Within(parentTrain.RearTDBTraveller.WorldLocation, parentTurntable.WorldPosition.WorldLocation, parentTurntable.Length / 2))
                        {
                            trainOnTable.SetBackState(true);
                            Simulator.Instance.Confirmer.Information("Rear of train is on table");
                            Simulator.Instance.Confirmer.Information("Stop train, set throttle to zero, set reverser to neutral");
                        }
                    }
                    else if (parentTrain.SpeedMpS < 0.05f)
                    {
                        parentTrain.ControlMode = TrainControlMode.TurnTable;

                        var loco = parentTrain.LeadLocomotive;
                        if (loco.ThrottlePercent < 1 && Math.Abs(loco.SpeedMpS) < 0.05 && (loco.Direction == MidpointDirection.N || Math.Abs(parentTrain.MUReverserPercent) <= 1))
                        {
                            // check if train still on turntable
                            if (!WorldLocation.Within(parentTrain.FrontTDBTraveller.WorldLocation, parentTurntable.WorldPosition.WorldLocation, parentTurntable.Length / 2))
                            {
                                trainOnTable.SetFrontState(false);
                                Simulator.Instance.Confirmer.Information("Front of train slipped off table");
                            }
                            if (!WorldLocation.Within(parentTrain.RearTDBTraveller.WorldLocation, parentTurntable.WorldPosition.WorldLocation, parentTurntable.Length / 2))
                            {
                                trainOnTable.SetBackState(false);
                                Simulator.Instance.Confirmer.Information("Rear of train slipped off table");
                            }

                            if (trainOnTable.FrontOnBoard && trainOnTable.BackOnBoard)
                            {
                                parentTrain.ClearActiveSectionItems();   // release all track sections
                                MovingTableState = MovingTableStateEnum.AccessOnMovingTable;
                                parentTurntable.TrainsOnMovingTable.Add(trainOnTable);
                                parentTurntable.ComputeTrainPosition(parentTrain);
                            }
                        }
                    }

                    break;

                // state : moving onto turntable (from Storage)
                case TimetableTurntableControl.MovingTableStateEnum.StorageToMovingTable:

                    // set end of authority beyond turntable
                    parentTrain.EndAuthorityTypes[0] = EndAuthorityType.EndOfPath;
                    parentTrain.DistanceToEndNodeAuthorityM[0] = CalculateDistanceToTurntable() + (parentTurntable.Length / 2.0f);

                    // check if train position on turntable
                    if (!trainOnTable.FrontOnBoard)
                    {
                        if (WorldLocation.Within(parentTrain.FrontTDBTraveller.WorldLocation, parentTurntable.WorldPosition.WorldLocation, parentTurntable.Length / 2))
                        {
                            trainOnTable.SetFrontState(true);
                            Simulator.Instance.Confirmer.Information("Front of train is on table");

                        }
                    }
                    else if (!trainOnTable.BackOnBoard)
                    {
                        if (WorldLocation.Within(parentTrain.RearTDBTraveller.WorldLocation, parentTurntable.WorldPosition.WorldLocation, parentTurntable.Length / 2))
                        {
                            trainOnTable.SetBackState(true);
                            Simulator.Instance.Confirmer.Information("Rear of train is on table");
                            Simulator.Instance.Confirmer.Information("Stop train, set throttle to zero, set reverser to neutral");
                        }
                    }
                    else if (parentTrain.SpeedMpS < 0.05f)
                    {
                        parentTrain.ControlMode = TrainControlMode.TurnTable;

                        var loco = parentTrain.LeadLocomotive;
                        if (loco.ThrottlePercent < 1 && Math.Abs(loco.SpeedMpS) < 0.05 && (loco.Direction == MidpointDirection.N || Math.Abs(parentTrain.MUReverserPercent) <= 1))
                        {
                            // check if train still on turntable
                            if (!WorldLocation.Within(parentTrain.FrontTDBTraveller.WorldLocation, parentTurntable.WorldPosition.WorldLocation, parentTurntable.Length / 2))
                            {
                                trainOnTable.SetFrontState(false);
                                Simulator.Instance.Confirmer.Information("Front of train slipped off table");
                            }
                            if (!WorldLocation.Within(parentTrain.RearTDBTraveller.WorldLocation, parentTurntable.WorldPosition.WorldLocation, parentTurntable.Length / 2))
                            {
                                trainOnTable.SetBackState(false);
                                Simulator.Instance.Confirmer.Information("Rear of train slipped off table");
                            }

                            if (trainOnTable.FrontOnBoard && trainOnTable.BackOnBoard)
                            {
                                parentTrain.ClearActiveSectionItems();   // release all track sections
                                MovingTableState = MovingTableStateEnum.StorageOnMovingTable;
                                parentTurntable.TrainsOnMovingTable.Add(trainOnTable);
                                parentTurntable.ComputeTrainPosition(parentTrain);
                            }
                        }
                    }

                    break;

                // state : turning on turntable (from Access)
                // get required exit
                // exit from this state is through PrepareMoveOffTable 
                case TimetableTurntableControl.MovingTableStateEnum.AccessOnMovingTable:

                    reqTurntableEntry = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableExitIndex;
                    reqEntryDirection = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].AccessTraveller.Direction;

                    reqTurntableExit = parentPool.StoragePool[StoragePathIndex].TableExitIndex;
                    reqExitDirection = parentPool.StoragePool[StoragePathIndex].StoragePathTraveller.Direction;

                    AutoRequestExit(reqTurntableExit, reqEntryDirection, reqExitDirection, elapsedClockSeconds);
                    break;

                // state : turning on turntable (from Storage)
                // get required exit
                // exit from this state is through PrepareMoveOffTable
                case TimetableTurntableControl.MovingTableStateEnum.StorageOnMovingTable:

                    reqTurntableEntry = parentPool.StoragePool[StoragePathIndex].TableExitIndex;
                    reqEntryDirection = parentPool.StoragePool[StoragePathIndex].StoragePathTraveller.Direction;

                    reqTurntableExit = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableExitIndex;
                    reqExitDirection = parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].AccessTraveller.Direction;

                    AutoRequestExit(reqTurntableExit, reqEntryDirection, reqExitDirection, elapsedClockSeconds);
                    break;

                default:
                    break;
            }

            return (terminated);
        }

        //================================================================================================//
        /// <summary>
        /// Get access path index from present position of train
        /// </summary>
        /// <returns></returns>

        public int GetAccessPathIndex()
        {
            int presentSection = parentTrain.PresentPosition[Direction.Forward].TrackCircuitSectionIndex;
            TrackDirection presentDirection = parentTrain.PresentPosition[Direction.Forward].Direction;

            // search if section in access path
            // direction must be reverse as access path is defined outbound
            int reqPath = -1;
            for (int iPath=0; iPath <= parentPool.AdditionalTurntableDetails.AccessPaths.Count - 1 && reqPath < 0; iPath++)
            {
                int routeIndex = parentPool.AdditionalTurntableDetails.AccessPaths[iPath].AccessPath.GetRouteIndex(presentSection, 0);
                if (routeIndex >= 0 && parentPool.AdditionalTurntableDetails.AccessPaths[iPath].AccessPath[routeIndex].Direction != presentDirection)
                {
                    reqPath = iPath;
                }
            }

            return (reqPath);
        }

        //================================================================================================//
        /// <summary>
        /// Turn turntable to required exit position
        /// </summary>

        public bool AutoRequestExit(int reqExit, Traveller.TravellerDirection entryPathDirection, Traveller.TravellerDirection exitPathDirection,
                        double elapsedClockSeconds)
        {
            // if turntable is moving, always return false
            if (parentTurntable.AutoRotationDirection != Rotation.None)
            {
                parentTurntable.AutoRotateTable(elapsedClockSeconds);

                // in prerun, also perform turntable update as simulation is not yet running
                // also perform turntable update if this is not the active moving table
                bool performUpdate = parentTrain.AI.PreUpdate;
                if (!performUpdate)
                {
                    if (parentPool.Simulatorref.ActiveMovingTable == null)
                    {
                        performUpdate = true;
                    }
                    else
                    {
                        performUpdate = parentPool.Simulatorref.ActiveMovingTable.WFile != parentTurntable.WFile ||
                                        parentPool.Simulatorref.ActiveMovingTable.UID != parentTurntable.UID;
                    }
                }

                if (performUpdate)
                {
                    parentTurntable.Update();
                }
                return (false);
            }

            // if connected and connection is required exit, return true
            if ((parentTurntable.ForwardConnected || parentTurntable.RearConnected) && parentTurntable.ConnectedTrackEnd == reqExit)
            {
                // if train on table, prepare to move off table
                if (parentTurntable.TrainsOnMovingTable.Count > 0)
                {
                    PrepareMoveOffTable();
                }                
                return (true);
            }

            // if not moving and not connected, start movement
#if DEBUG_TURNTABLEINFO
            Trace.TraceInformation("Pool {0} - Train {1} [{2}]",parentPool.PoolName, parentTrain.Name, parentTrain.Number.ToString());
#endif

            parentTurntable.SendNotifications = false;
            reqTurntableExit = reqExit;

            // find out direction to move
            (float startAngle, float endAngle) = parentTurntable.FindConnectingDirections(reqExit);
            float angleToMove = (endAngle - startAngle) % (float)Math.PI;
            float halfPi = MathHelper.PiOver2;
            bool exitForward = parentTurntable.TrackNodeOrientation(reqExit);
            bool entryForward = parentTurntable.TrackNodeOrientation(parentTurntable.ConnectedTrackEnd);

            bool reqChangeEnd = false;

#if DEBUG_TURNTABLEINFO
            Trace.TraceInformation("   Exit information : start Exit - Angle {0} - {1} (front : {2} ), end Exit - angle {3} - {4}, angle to move : {5}",
                parentTurntable.ConnectedTrackEnd, startAngle, parentTurntable.ForwardConnected.ToString(), reqExit, endAngle, angleToMove.ToString());
            Trace.TraceInformation("             entry orientation forward : {0} , exit orientation forward : {1}", entryForward.ToString(), exitForward.ToString());
#endif

            // empty turntable : rotate required exit over smallest angle
            if (parentTurntable.TrainsOnMovingTable.Count <= 0)
            {
#if DEBUG_TURNTABLEINFO
                Trace.TraceInformation("   Empty turntable");
#endif

                if (angleToMove <= -halfPi)
                {
                    parentTurntable.AutoRotationDirection = Rotation.CounterClockwise;
                    reqChangeEnd = true;
#if DEBUG_TURNTABLEINFO
                    Trace.TraceInformation("   angle <= - halfPi");
#endif
                }
                else if (angleToMove < 0)
                {
                    parentTurntable.AutoRotationDirection = Rotation.CounterClockwise;
#if DEBUG_TURNTABLEINFO
                    Trace.TraceInformation("   -halfPi < angle < 0");
#endif
                }

                else if (angleToMove < halfPi)
                {
                    parentTurntable.AutoRotationDirection = Rotation.Clockwise;
#if DEBUG_TURNTABLEINFO
                    Trace.TraceInformation("   0 < angle < halfPi");
#endif
                }
                else
                {
                    parentTurntable.AutoRotationDirection = Rotation.Clockwise;
                    reqChangeEnd = true;
#if DEBUG_TURNTABLEINFO
                    Trace.TraceInformation("   halfPi <= angle");
#endif
                }

#if DEBUG_TURNTABLEINFO
                Trace.TraceInformation("   Angle calculation : Rotation Clockwise : {0}, Counterclockwise : {1}, Change end required : {2}",
                    parentTurntable.AutoClockwise.ToString(), parentTurntable.AutoCounterclockwise.ToString(), reqChangeEnd.ToString());
#endif

#if DEBUG_TURNTABLEINFO
                Trace.TraceInformation("   Exit orientation and path direction : exit forward : {0} , path direction : {1}",
                                  exitForward.ToString(), exitPathDirection.ToString());
#endif

                // reverse rotation if required
                if (reqChangeEnd)
                {
                    parentTurntable.AutoRotationDirection = parentTurntable.AutoRotationDirection.Reverse();
                }

                // set required end
                if ((parentTurntable.ForwardConnected && !reqChangeEnd) || (parentTurntable.RearConnected && reqChangeEnd))
                {
                    parentTurntable.ForwardConnectedTarget = reqExit;
                    parentTurntable.RearConnectedTarget = -1;
                }
                else
                {
                    parentTurntable.RearConnectedTarget = reqExit;
                    parentTurntable.ForwardConnectedTarget = -1;
                }

#if DEBUG_TURNTABLEINFO
                Trace.TraceInformation("   Final setting empty table : Forward connected : {0}, Rear connected {1} , Clockwise : {2}, Counterclockwise : {3}\n",
                            parentTurntable.ForwardConnectedTarget, parentTurntable.RearConnectedTarget, 
                            parentTurntable.AutoClockwise.ToString(), parentTurntable.AutoCounterclockwise.ToString());
#endif
                return (false);
            }

            // turntable has train
#if DEBUG_TURNTABLEINFO
            Trace.TraceInformation("   Train on turntable");
#endif

            // find out if train needs to reverse
            reqReverseFormation = TestTrainFormation(parentTrain);

            // if reverse formation not required, reverse connection
            if (!reqReverseFormation)
            {
                reqChangeEnd = !reqChangeEnd;
#if DEBUG_TURNTABLEINFO
                Trace.TraceInformation("    reverse formation not required, Change end required {0}", reqChangeEnd.ToString());
#endif
            }

            // rotate clockwise or counterclockwise depending on angle
            if ( angleToMove < 0)
            {
                parentTurntable.AutoRotationDirection = Rotation.CounterClockwise;
#if DEBUG_TURNTABLEINFO
                Trace.TraceInformation("   angle < 0");
#endif
            }
            else if (angleToMove > 0)
            {
                parentTurntable.AutoRotationDirection = Rotation.Clockwise;
#if DEBUG_TURNTABLEINFO
                Trace.TraceInformation("   angle > 0");
#endif
            }

#if DEBUG_TURNTABLEINFO
            Trace.TraceInformation("   Angle calculation : Rotation Clockwise : {0}, Counterclockwise : {1}, Change end required : {2}",
                parentTurntable.AutoClockwise.ToString(), parentTurntable.AutoCounterclockwise.ToString(), reqChangeEnd.ToString());
#endif

#if DEBUG_TURNTABLEINFO
            Trace.TraceInformation("   Entry orientation and path direction : entry forward : {0} , path direction : {1}",
                              entryForward.ToString(), entryPathDirection.ToString());
#endif

            // if entry orientation does not match tracknode direction, entry direction must be reversed
            // orientation is true : tracknode direction is away from turntable so traveller direction must be forward
            // orientation is false : tracknode direction is toward turntable so traveller direction must be backward
            if ((!entryForward && entryPathDirection == Traveller.TravellerDirection.Backward) || (entryForward && entryPathDirection == Traveller.TravellerDirection.Forward))
            {
                reqChangeEnd = !reqChangeEnd;
#if DEBUG_TURNTABLEINFO
                Trace.TraceInformation("   Entry has reversed orientation : change end required : {0}", reqChangeEnd.ToString());
#endif
            }

#if DEBUG_TURNTABLEINFO
            Trace.TraceInformation("   Exit orientation and path direction : exit forward : {0} , path direction : {1}",
                              exitForward.ToString(), exitPathDirection.ToString());
#endif

            // if exit orientation does not match tracknode direction, exit direction must be reversed
            // orientation is true : tracknode direction is away from turntable so traveller direction must be forward
            // orientation is false : tracknode direction is toward turntable so traveller direction must be backward
            if ((!exitForward && exitPathDirection == Traveller.TravellerDirection.Backward) || (exitForward && exitPathDirection == Traveller.TravellerDirection.Forward))
            {
                reqChangeEnd = !reqChangeEnd;
#if DEBUG_TURNTABLEINFO
                Trace.TraceInformation("   Exit has reversed orientation : change end required : {0}", reqChangeEnd.ToString());
#endif
            }

            // reverse rotation if required
            if (reqChangeEnd)
            {
                parentTurntable.AutoRotationDirection = parentTurntable.AutoRotationDirection.Reverse();
            }

            // set required end
            if ((parentTurntable.ForwardConnected && !reqChangeEnd) || (parentTurntable.RearConnected && reqChangeEnd))
            {
                parentTurntable.ForwardConnectedTarget = reqExit;
                parentTurntable.RearConnectedTarget = -1;
            }
            else
            {
                parentTurntable.RearConnectedTarget = reqExit;
                parentTurntable.ForwardConnectedTarget = -1;
            }

#if DEBUG_TURNTABLEINFO
            Trace.TraceInformation("   Final setting loaded table : Forward connected : {0}, Rear connected {1} , Clockwise : {2}, Counterclockwise : {3}\n",
                        parentTurntable.ForwardConnectedTarget, parentTurntable.RearConnectedTarget,
                        parentTurntable.AutoClockwise.ToString(), parentTurntable.AutoCounterclockwise.ToString());
#endif

            return (false);
        }

        //================================================================================================//
        /// <summary>
        /// Calculate distance to position in middle of turntable
        /// </summary>

        public float CalculateDistanceToTurntable()
        {
            float remDistance = 0.0f;

            // check present position is in last section of route
            if (parentTrain.PresentPosition[Direction.Forward].RouteListIndex < parentTrain.ValidRoute[0].Count - 1)
            {
                TrackCircuitSection thisSection = TrackCircuitSection.TrackCircuitList[parentTrain.PresentPosition[Direction.Forward].TrackCircuitSectionIndex];
                remDistance = thisSection.Length - parentTrain.PresentPosition[Direction.Forward].Offset;

                for (int iIndex = parentTrain.PresentPosition[Direction.Forward].RouteListIndex + 1; iIndex < parentTrain.ValidRoute[0].Count - 1; iIndex++)
                {
                    remDistance += parentTrain.ValidRoute[0][iIndex].TrackCircuitSection.Length;
                }

                if (MovingTableState == MovingTableStateEnum.StorageToMovingTable)
                {
                    remDistance += parentPool.StoragePool[StoragePathIndex].TableMiddleEntry + parentTrain.Length / 2.0f;
                }
                else if (MovingTableState == MovingTableStateEnum.AccessToMovingTable)
                {
                    remDistance += parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableMiddleEntry + parentTrain.Length / 2.0f;
                }
            }
            // train in same section as turntable
            else
            {
                if (MovingTableState == MovingTableStateEnum.StorageToMovingTable)
                {
                    remDistance += parentPool.StoragePool[StoragePathIndex].TableMiddleEntry - parentTrain.PresentPosition[Direction.Forward].Offset + parentTrain.Length / 2.0f;
                }
                else if (MovingTableState == MovingTableStateEnum.AccessToMovingTable)
                {
                    remDistance += parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableMiddleEntry - parentTrain.PresentPosition[Direction.Forward].Offset + parentTrain.Length / 2.0f;
                }
            }

            return (remDistance);
        }

        //================================================================================================//
        /// <summary>
        /// Set next stage in process when train is stopped after moving
        /// </summary>

        public void SetNextStageOnStopped()
        {
            bool trainOnTable = false;

            switch (MovingTableState)
            {
                case MovingTableStateEnum.AccessToMovingTable:
                    MovingTableState = MovingTableStateEnum.AccessOnMovingTable;
                    trainOnTable = true;
                    break;

                case MovingTableStateEnum.StorageToMovingTable:
                    MovingTableState = MovingTableStateEnum.StorageOnMovingTable;
                    trainOnTable = true;
                    break;

                default:
                    break;
            }

            if (trainOnTable)
            {
                SetTrainOnTable();
            }
        }

        //================================================================================================//
        /// <summary>
        /// Place train on turntable
        /// </summary>

        public void SetTrainOnTable()
        {
            // ensure train is not moving
            if (parentTrain.SpeedMpS > 0 || parentTrain.SpeedMpS < 0)   // if train still running force it to stop
            {
                parentTrain.SpeedMpS = 0;
                foreach (var Car in parentTrain.Cars)
                {
                    Car.SpeedMpS = 0;
                }

                parentTrain.Update(0);   // stop the wheels from moving etc
                parentTrain.AITrainThrottlePercent = 0;
                parentTrain.AITrainBrakePercent = 100;
            }

            // get actual stop position
            if (MovingTableAction == MovingTableActionEnum.FromAccess)
            {
                float trainOffset = parentTrain.RearTDBTraveller.TrackNodeOffset;
                stopPositionOnTurntableM = trainOffset + (parentTrain.Length / 2.0f) - parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableMiddleEntry;
            }
            else if (MovingTableAction == MovingTableActionEnum.FromStorage)
            {
                float trainOffset = parentTrain.RearTDBTraveller.TrackNodeOffset;
                stopPositionOnTurntableM = trainOffset + (parentTrain.Length / 2.0f) - parentPool.StoragePool[StoragePathIndex].TableMiddleEntry;
            }

            // clear approach route
            parentTrain.RemoveFromTrack();
            foreach (DistanceTravelledItem thisAction in parentTrain.requiredActions)
            {
                if (thisAction is ClearSectionItem)
                {
                    ClearSectionItem thisItem = thisAction as ClearSectionItem;
                    TrackCircuitSection thisSection = TrackCircuitSection.TrackCircuitList[thisItem.TrackSectionIndex];
                    thisSection.ClearOccupied(parentTrain, true);
                }
            }

            parentTrain.ResetActions(false);
            parentTrain.DelayedStartMoving(AiStartMovement.Turntable);

            // set train on table
            parentTrain.ControlMode = TrainControlMode.TurnTable;

            TrainOnMovingTable trainOnTable = new TrainOnMovingTable(parentTrain);
            trainOnTable.SetFrontState(true);
            trainOnTable.SetBackState(true);

            parentTurntable.TrainsOnMovingTable.Add(trainOnTable);
            parentTurntable.ComputeTrainPosition(parentTrain);
        }

        //================================================================================================//
        /// <summary>
        /// Prepare train to move off turntable
        /// </summary>

        public void PrepareMoveOffTable()
        {

            // set next active path for train
            parentTrain.TCRoute.ActiveSubPath++;
            parentTrain.ValidRoute[0] = new TrackCircuitPartialPathRoute(parentTrain.TCRoute.TCRouteSubpaths[parentTrain.TCRoute.ActiveSubPath]);

            // check if formation reverse is required
            bool reverseFormation = reqReverseFormation;

            // position reverse is required if path off turntable is backward
            bool reversePosition = false;
            if (parentTrain.ValidRoute[0][0].Direction == 0)
            {
                reversePosition = true;
                reverseFormation = !reverseFormation;  // inverse reverse formation as this is also done in reverseposition
            }

            // reverse formation if required
            if (reverseFormation) parentTrain.ReverseCars();

            // get traveller at start of path tracknode
            TrackCircuitSection thisSection = parentTrain.ValidRoute[0][0].TrackCircuitSection;
            Traveller middlePosition = new Traveller(parentPool.Simulatorref.TSectionDat, parentPool.Simulatorref.TrackDatabase.TrackDB.TrackNodes, parentPool.Simulatorref.TrackDatabase.TrackDB.TrackNodes[thisSection.OriginalIndex] as TrackVectorNode);

#if DEBUG_TURNTABLEINFO
            Trace.TraceInformation("Pool {0} - Train {1} [{2}] : calculating middle position for state : {3} , orientation : {4}",
                parentPool.PoolName, parentTrain.Name, parentTrain.Number, MovingTableState.ToString(),
                parentTurntable.MyTrackNodesOrientation[reqTurntableExit].ToString());
#endif

            // get position of front and rear of train in present tracknode
            if (MovingTableState == TimetableTurntableControl.MovingTableStateEnum.StorageOnMovingTable)
            {
                if (parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].AccessTraveller.Direction == Traveller.TravellerDirection.Forward)
                {
                    middlePosition.Move(parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableMiddleExit);
#if DEBUG_TURNTABLEINFO
                    Trace.TraceInformation("    used correction : {0} , resulting middle offset : TN : {1} , offset : {2} (off length : {3} ), Vector : {4}",
                        parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableMiddleExit,
                        middlePosition.TrackNodeIndex, middlePosition.TrackNodeOffset, middlePosition.TrackNodeLength, middlePosition.TrackVectorSectionIndex);
#endif
                }
                else
                {
                    // if tracknode direction through turntable is backward, use opposite position
                    middlePosition.Move(parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableMiddleEntry);
#if DEBUG_TURNTABLEINFO
                    Trace.TraceInformation("    used correction : {0} , resulting middle offset : TN : {1} , offset : {2} (off length : {3} ), Vector : {4}",
                        parentPool.AdditionalTurntableDetails.AccessPaths[AccessPathIndex].TableMiddleExit,
                        middlePosition.TrackNodeIndex, middlePosition.TrackNodeOffset, middlePosition.TrackNodeLength, middlePosition.TrackVectorSectionIndex);
#endif
                }
            }
            else if (MovingTableState == TimetableTurntableControl.MovingTableStateEnum.AccessOnMovingTable)
            {
                if (parentPool.StoragePool[StoragePathIndex].StoragePathTraveller.Direction == Traveller.TravellerDirection.Forward)
                {
                    middlePosition.Move(parentPool.StoragePool[StoragePathIndex].TableMiddleExit);
#if DEBUG_TURNTABLEINFO
                    Trace.TraceInformation("    used correction : {0} , resulting middle offset : TN : {1} , offset : {2} (off length : {3} ), Vector : {4}",
                        parentPool.StoragePool[StoragePathIndex].TableMiddleExit,
                        middlePosition.TrackNodeIndex, middlePosition.TrackNodeOffset, middlePosition.TrackNodeLength, middlePosition.TrackVectorSectionIndex);
#endif
                }
                else
                {
                    // if tracknode direction through turntable is backward, use opposite position
                    middlePosition.Move(parentPool.StoragePool[StoragePathIndex].TableMiddleEntry);
#if DEBUG_TURNTABLEINFO
                    Trace.TraceInformation("    used correction : {0} , resulting middle offset : TN : {1} , offset : {2} (off length : {3} ), Vector : {4}",
                        parentPool.StoragePool[StoragePathIndex].TableMiddleEntry,
                        middlePosition.TrackNodeIndex, middlePosition.TrackNodeOffset, middlePosition.TrackNodeLength, middlePosition.TrackVectorSectionIndex);
#endif
                }
            }

            parentTrain.RearTDBTraveller = new Traveller(middlePosition);
            float offsetPosition = reverseFormation ? (-parentTrain.Length / 2.0f) - stopPositionOnTurntableM : (-parentTrain.Length / 2.0f) + stopPositionOnTurntableM;
            parentTrain.RearTDBTraveller.MoveInSection(offsetPosition);
            parentTrain.FrontTDBTraveller = new Traveller(parentTrain.RearTDBTraveller);
            parentTrain.FrontTDBTraveller.MoveInSection(parentTrain.Length);

            // place train
            parentTrain.InitialTrainPlacement();
            if (reversePosition)
            {
                parentTrain.ReverseFormation(false);
            }

            // reinitiate train
            MovingTableState = MovingTableStateEnum.Completed;
            parentTrain.MovementState = AiMovementState.Static;
            parentTrain.ControlMode = TrainControlMode.AutoNode;
            parentTrain.DistanceTravelledM = 0;
            parentTrain.DelayedStartMoving(AiStartMovement.PathAction);

            // actions for mode access (train going into storage)
            if (MovingTableAction == MovingTableActionEnum.FromAccess)
            {
                // set terminate
                parentTrain.Closeup = true;
                parentTrain.FormsStatic = true;

                // calculate stop position
                float endOffset = parentPool.StoragePool[StoragePathIndex].StoragePathTraveller.TrackNodeOffset + parentTrain.Length;
                if (endOffset < parentTrain.DistanceToEndNodeAuthorityM[0])
                {
                    parentTrain.DistanceToEndNodeAuthorityM[0] = endOffset;
                }
            }

            // actions for mode storage (train going out of storage)
            else
            {
                // create end of route action
                parentTrain.SetEndOfRouteAction();
            }

            // set distance for train to clear table
            clearingDistanceM = (parentTrain.Length / 2f) + (parentTurntable.Length / 2f) + parentPool.AdditionalTurntableDetails.TurntableReleaseClearanceM;

            // create action for clearing turntable
            ClearMovingTableAction newAction = new ClearMovingTableAction(clearingDistanceM, originalTrainMaxSpeedMpS);
            parentTrain.requiredActions.InsertAction(newAction);
        }

        //================================================================================================//
        /// <summary>
        /// Remove train from turntable, return train to normal state
        /// </summary>

        public void RemoveTrainFromTurntable()
        {
            // clear table
            parentTurntable.TrainsOnMovingTable.Clear();
            parentTurntable.InUse = false;
            parentTurntable.GoToAutoTarget = false;
            trainOnTable = null;

            // reset train speed
            parentTrain.TrainMaxSpeedMpS = originalTrainMaxSpeedMpS;
            ActivateSpeedLimit activeSpeeds = new ActivateSpeedLimit(0.0f, originalSpeedLimitMpS, originalSpeedSignalMpS);

            if (parentTrain.TrainType == TrainType.Player)
            {
                parentTrain.SetPendingSpeedLimit(activeSpeeds);
            }
            else
            {
                parentTrain.SetAIPendingSpeedLimit(activeSpeeds);
            }
        }

        //================================================================================================//

        public bool TestTrainFormation (TTTrain parentTrain)
        {
            bool reqReverse = true;

            // get present train direction
            bool nowBackward = parentTrain.Cars[0].Flipped;

            if (MovingTableAction == MovingTableActionEnum.FromAccess)
            {
                if (nowBackward && parentTrain.PoolExitDirection == TimetablePool.PoolExitDirectionEnum.Backward)
                {
                    reqReverse = false;
                }
                else if (!nowBackward && parentTrain.PoolExitDirection == TimetablePool.PoolExitDirectionEnum.Forward)
                {
                    reqReverse = false;
                }
            }
            else
            {
                if (nowBackward && parentTrain.CreatePoolDirection == TimetablePool.PoolExitDirectionEnum.Backward)
                {
                    reqReverse = false;
                }
                else if (!nowBackward && parentTrain.CreatePoolDirection == TimetablePool.PoolExitDirectionEnum.Forward)
                {
                    reqReverse = false;
                }
            }

            return (reqReverse);
        }
    }
}