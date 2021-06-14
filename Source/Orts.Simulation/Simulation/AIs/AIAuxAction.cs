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

/* AI
 * 
 * Contains code to initialize and control AI trains.
 * 
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Orts.Common;
using Orts.Common.Position;
using Orts.Formats.Msts;
using Orts.Formats.OR;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.Signalling;
using Orts.Simulation.Track;

namespace Orts.Simulation.AIs
{
    #region AuxActionsContainer

    //================================================================================================//
    /// <summary>
    /// AuxActionsContainer
    /// Used to manage all the action ref object.
    /// </summary>

    internal class AuxActionsContainer
    {
        public List<AuxActionRef> SpecAuxActions = new List<AuxActionRef>();          // Actions To Do during activity, like WP with specific location
        protected List<KeyValuePair<System.Type, AuxActionRef>> GenFunctions = new List<KeyValuePair<Type, AuxActionRef>>();

        public DistanceTravelledActions genRequiredActions = new DistanceTravelledActions(); // distance travelled Generic action list for AITrain
        public DistanceTravelledActions specRequiredActions = new DistanceTravelledActions();
        private Train ThisTrain;

        public AuxActionsContainer(Train thisTrain)
        {

            if (thisTrain is AITrain)
            {
                SetGenAuxActions((AITrain)thisTrain);
            }
            ThisTrain = thisTrain;
        }

        public AuxActionsContainer(Train thisTrain, BinaryReader inf, string routePath)
        {
            ThisTrain = thisTrain;
            if (thisTrain is AITrain)
            {
                SetGenAuxActions((AITrain)thisTrain);
            }

            int cntAuxActionSpec = inf.ReadInt32();
            for (int idx = 0; idx < cntAuxActionSpec; idx++)
            {
                int cntAction = inf.ReadInt32();
                AuxActionRef action;
                string actionRef = inf.ReadString();
                AuxActionRef.AuxiliaryAction nextAction = (AuxActionRef.AuxiliaryAction)Enum.Parse(typeof(AuxActionRef.AuxiliaryAction), actionRef);
                switch (nextAction)
                {
                    case AuxActionRef.AuxiliaryAction.WaitingPoint:
                        action = new AIActionWPRef(thisTrain, inf);
                        SpecAuxActions.Add(action);
                        break;
                    case AuxActionRef.AuxiliaryAction.SoundHorn:
                        action = new AIActionHornRef(thisTrain, inf);
                        SpecAuxActions.Add(action);
                        break;
                    case AuxActionRef.AuxiliaryAction.SignalDelegate:
                        action = new AIActSigDelegateRef(thisTrain, inf);
                        var hasWPActionAssociated = inf.ReadBoolean();
                        if (hasWPActionAssociated && SpecAuxActions.Count > 0)
                        {
                            var candidateAssociate = SpecAuxActions[SpecAuxActions.Count - 1];
                            if (candidateAssociate is AIActionWPRef && (candidateAssociate as AIActionWPRef).TCSectionIndex == (action as AIActSigDelegateRef).TCSectionIndex)
                            {
                                (action as AIActSigDelegateRef).AssociatedWPAction = candidateAssociate as AIActionWPRef;
                            }
                        }
                        SpecAuxActions.Add(action);
                        break;
                    default:
                        break;
                }
            }
        }

        public void Save(BinaryWriter outf, int currentClock)
        {
            int cnt = 0;
            outf.Write(SpecAuxActions.Count);
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "SaveAuxContainer, count :" + SpecAuxActions.Count + 
                "Position in file: " + outf.BaseStream.Position + "\n");
#endif
            AITrain aiTrain = ThisTrain as AITrain;
            if (SpecAuxActions.Count > 0 && SpecAuxActions[0] != null &&
                    specRequiredActions.First != null && specRequiredActions.First.Value is AuxActSigDelegate)

                // SigDelegate WP is running

                if (((AuxActSigDelegate)specRequiredActions.First.Value).currentMvmtState == AiMovementState.HandleAction &&
                    !(((AuxActSigDelegate)specRequiredActions.First.Value).ActionRef as AIActSigDelegateRef).IsAbsolute)
                {
                    int remainingDelay = ((AuxActSigDelegate)specRequiredActions.First.Value).ActualDepart - currentClock;
                    AIActSigDelegateRef actionRef = ((AuxActSigDelegate)specRequiredActions.First.Value).ActionRef as AIActSigDelegateRef;
                    if (actionRef.AssociatedWPAction != null) actionRef.AssociatedWPAction.SetDelay(remainingDelay);
                    actionRef.Delay = remainingDelay;
                }
            if (!(ThisTrain == Simulator.Instance.OriginalPlayerTrain && (ThisTrain.TrainType == TrainType.AiPlayerDriven ||
                ThisTrain.TrainType == TrainType.AiPlayerHosting || ThisTrain.TrainType == TrainType.Player || ThisTrain.TrainType == TrainType.Ai)))
            {

                if (ThisTrain is AITrain && ((aiTrain.MovementState == AiMovementState.HandleAction && aiTrain.nextActionInfo != null &&
                aiTrain.nextActionInfo.NextAction == AIActionItem.AI_ACTION_TYPE.AUX_ACTION && aiTrain.nextActionInfo is AuxActionWPItem)
                || (aiTrain.AuxActionsContainer.SpecAuxActions.Count > 0 &&
                aiTrain.AuxActionsContainer.SpecAuxActions[0] is AIActionWPRef && (aiTrain.AuxActionsContainer.SpecAuxActions[0] as AIActionWPRef).keepIt != null &&
                (aiTrain.AuxActionsContainer.SpecAuxActions[0] as AIActionWPRef).keepIt.currentMvmtState == AiMovementState.HandleAction)))
                // WP is running
                {
                    // Do nothing if it is an absolute WP
                    if (!(aiTrain.AuxActionsContainer.SpecAuxActions.Count > 0 && aiTrain.AuxActionsContainer.SpecAuxActions[0] is AIActionWPRef &&
                        (aiTrain.AuxActionsContainer.SpecAuxActions[0] as AIActionWPRef).Delay >= 30000 && (aiTrain.AuxActionsContainer.SpecAuxActions[0] as AIActionWPRef).Delay < 40000))
                    {
                        int remainingDelay;
                        if (aiTrain.nextActionInfo != null && aiTrain.nextActionInfo is AuxActionWPItem) remainingDelay = ((AuxActionWPItem)aiTrain.nextActionInfo).ActualDepart - currentClock;
                        else remainingDelay = ((AIActionWPRef)SpecAuxActions[0]).keepIt.ActualDepart - currentClock;
                        ((AIActionWPRef)SpecAuxActions[0]).SetDelay(remainingDelay);
                    }
                }
            }
            foreach (var action in SpecAuxActions)
            {
                ((AIAuxActionsRef)action).save(outf, cnt);
                cnt++;
            }
        }
#if false
            int cnt = 0;
            outf.Write(AuxActions.Count);
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "SaveAIAuxActions, count :" + AuxActions.Count + 
                "Position in file: " + outf.BaseStream.Position + "\n");
#endif

            foreach (var action in AuxActions)
            {
                action.save(outf, cnt);
                cnt++;
            }

#endif
        protected void SetGenAuxActions(AITrain thisTrain)  //  Add here the new Generic Action
        {
            Formats.Msts.Files.ActivityFile activity = Simulator.Instance.Activity;
            if (activity != null && activity.Activity.AIBlowsHornAtLevelCrossings && SpecAuxActions.Count == 0)
            {
                AuxActionHorn auxActionHorn = new AuxActionHorn(true, 2, 0, activity.Activity.AILevelCrossingHornPattern);
                AIActionHornRef horn = new AIActionHornRef(thisTrain, auxActionHorn, 0);
                List<KeyValuePair<System.Type, AuxActionRef>> listInfo = horn.GetCallFunction();
                foreach (var function in listInfo)
                    GenFunctions.Add(function);
            }
        }

        //public bool CheckGenActions(System.Type typeSource, float rearDist, float frontDist, WorldLocation location, uint trackNodeIndex)
        public bool CheckGenActions(Type typeSource, in WorldLocation location, params object[] list)
        {
            if (ThisTrain is AITrain)
            {
                AITrain aiTrain = ThisTrain as AITrain;
                foreach (var fonction in GenFunctions)
                {
                    if (typeSource == fonction.Key)   //  Caller object is a LevelCrossing
                    {
#if WITH_PATH_DEBUG
                        File.AppendAllText(@"C:\temp\checkpath.txt", "GenFunctions registered for train " + ThisTrain.Number + "\n");
#endif
                        AIAuxActionsRef called = (AIAuxActionsRef)fonction.Value;
                        if (called.HasAction(ThisTrain.Number, location))
                            return false;
                        AIActionItem newAction = called.CheckGenActions(location, aiTrain, list);
                        if (newAction != null)
                        {
                            if (newAction is AuxActionWPItem)
                                specRequiredActions.InsertAction(newAction);
                            else
                                genRequiredActions.InsertAction(newAction);
                        }
                    }
                }
            }
            return false;
        }

        public void RemoveSpecReqAction(AuxActionItem thisAction)
        {
            if (thisAction.CanRemove(ThisTrain))
            {
                DistanceTravelledItem thisItem = thisAction;
                specRequiredActions.Remove(thisItem);
            }
        }

        public void ProcessGenAction(AITrain thisTrain, int presentTime, double elapsedClockSeconds, AiMovementState movementState)
        {
            if (genRequiredActions.Count <= 0 || !(ThisTrain is AITrain))
                return;
            AITrain aiTrain = ThisTrain as AITrain;
            List<DistanceTravelledItem> itemList = new List<DistanceTravelledItem>();
            foreach (var action in genRequiredActions)
            {
                AIActionItem actionItem = action as AIActionItem;
                if (actionItem.RequiredDistance <= ThisTrain.DistanceTravelledM)
                {
                    itemList.Add(actionItem);
                }
            }
            foreach (var action in itemList)
            {
                AIActionItem actionItem = action as AIActionItem;
                actionItem.ProcessAction(aiTrain, presentTime, elapsedClockSeconds, movementState);
            }
        }

        public AiMovementState ProcessSpecAction(AITrain thisTrain, int presentTime, double elapsedClockSeconds, AiMovementState movementState)
        {
            AiMovementState MvtState = movementState;
            if (specRequiredActions.Count <= 0 || !(ThisTrain is AITrain))
                return MvtState;
            AITrain aiTrain = ThisTrain as AITrain;
            List<DistanceTravelledItem> itemList = new List<DistanceTravelledItem>();
            foreach (var action in specRequiredActions)
            {
                AIActionItem actionItem = action as AIActionItem;
                if (actionItem.RequiredDistance >= ThisTrain.DistanceTravelledM)
                    continue;
                if (actionItem is AuxActSigDelegate)
                {
                    var actionRef = (actionItem as AuxActSigDelegate).ActionRef;
                    if ((actionRef as AIActSigDelegateRef).IsAbsolute)
                        continue;
                }
                itemList.Add(actionItem);
            }
            foreach (var action in itemList)
            {
                AiMovementState tmpMvt;
                AIActionItem actionItem = action as AIActionItem;
                tmpMvt = actionItem.ProcessAction(aiTrain, presentTime, elapsedClockSeconds, movementState);
                if (tmpMvt != movementState)
                    MvtState = tmpMvt;  //  Try to avoid override of changed state of previous action
            }
            return MvtState;
        }

        public void Remove(AuxActionItem action)
        {
            bool ret = false;
            bool remove = true;
            if (action.ActionRef.IsGeneric)
            {
                if (genRequiredActions.Count > 0)
                    ret = genRequiredActions.Remove(action);
                if (!ret && specRequiredActions.Count > 0)
                {
                    if (((AIAuxActionsRef)action.ActionRef).CallFreeAction(ThisTrain))
                        RemoveSpecReqAction(action);
                }
            }
            if (action.ActionRef.ActionType == AuxActionRef.AuxiliaryAction.SoundHorn)
            {
                if (specRequiredActions.Contains(action)) RemoveSpecReqAction(action);
                else remove = false;
            }
            if (CountSpec() > 0 && remove == true)
                SpecAuxActions.Remove(action.ActionRef);
            if (ThisTrain is AITrain)
                ((AITrain)ThisTrain).ResetActions(true);
        }

        public void RemoveAt(int posit)
        {
            SpecAuxActions.RemoveAt(posit);
        }

        public AuxActionRef this[int key]
        {
            get
            {
                if (key >= SpecAuxActions.Count)
                    return null;
                return SpecAuxActions[key];
            }
            set
            {

            }
        }

        public int Count()
        {
            return CountSpec();
        }

        public int CountSpec()
        {
            return SpecAuxActions.Count;
        }

        //================================================================================================//
        //  SPA:    Added for use with new AIActionItems
        /// <summary>
        /// Create Specific Auxiliary Action, like WP
        /// <\summary>
        public void SetAuxAction(Train thisTrain)
        {
            if (SpecAuxActions.Count <= 0)
                return;
            AIAuxActionsRef thisAction;
            int specAuxActionsIndex = 0;
            bool requiredActionsInserted = false;
            while (specAuxActionsIndex <= SpecAuxActions.Count - 1)
            {
                while (SpecAuxActions.Count > 0)
                {
                    thisAction = (AIAuxActionsRef)SpecAuxActions[specAuxActionsIndex];

                    if (thisAction.SubrouteIndex > thisTrain.TCRoute.ActiveSubPath)
                    {
                        return;
                    }
                    if (thisAction.SubrouteIndex == thisTrain.TCRoute.ActiveSubPath)
                        break;
                    else
                    {
                        SpecAuxActions.RemoveAt(0);
                        if (SpecAuxActions.Count <= 0) return;
                    }
                }

                thisAction = (AIAuxActionsRef)SpecAuxActions[specAuxActionsIndex];
                bool validAction = false;
                float[] distancesM;
                while (!validAction)
                {
                    if (thisTrain is AITrain && thisTrain.TrainType != TrainType.AiPlayerDriven)
                    {
                        AITrain aiTrain = thisTrain as AITrain;
                        distancesM = thisAction.CalculateDistancesToNextAction(aiTrain, ((AITrain)aiTrain).TrainMaxSpeedMpS, true);
                    }
                    else
                    {
                        distancesM = thisAction.CalculateDistancesToNextAction(thisTrain, thisTrain.SpeedMpS, true);
                    }
                    //<CSComment> Next block does not seem useful. distancesM[0] includes distanceTravelledM, so it practically can be 0 only at start of game
                    /*                if (distancesM[0]< 0f)
                                    {
                                        SpecAuxActions.RemoveAt(0);
                                        if (SpecAuxActions.Count == 0)
                                        {
                                            return;
                                        }

                                        thisAction = (AIAuxActionsRef)SpecAuxActions[0];
                                        if (thisAction.SubrouteIndex > thisTrain.TCRoute.activeSubpath) return;
                                    }
                                    else */
                    {
                        float requiredSpeedMpS = 0;
                        if (requiredActionsInserted && ((thisAction is AIActSigDelegateRef && !((AIActSigDelegateRef)thisAction).IsAbsolute))) return;
                        validAction = true;
                        AIActionItem newAction = ((AIAuxActionsRef)SpecAuxActions[specAuxActionsIndex]).Handler(distancesM[1], requiredSpeedMpS, distancesM[0], thisTrain.DistanceTravelledM);
                        if (newAction != null)
                        {
                            if (thisTrain is AITrain && newAction is AuxActionWPItem)   // Put only the WP for AI into the requiredAction, other are on the container
                            {
                                bool found = false;
                                requiredActionsInserted = true;
                                if ((thisTrain.TrainType == TrainType.AiPlayerDriven || thisTrain.TrainType == TrainType.AiPlayerHosting) && thisTrain.requiredActions.Count > 0)
                                {
                                    // check if action already inserted
                                    foreach (DistanceTravelledItem item in thisTrain.requiredActions)
                                    {
                                        if (item is AuxActionWPItem)
                                        {
                                            found = true;
                                            continue;
                                        }
                                    }
                                }
                                if (!found)
                                {
                                    thisTrain.requiredActions.InsertAction(newAction);
                                    continue;
                                    //                              ((AITrain)thisTrain).nextActionInfo = newAction; // action must be restored through required actions only
                                }
                            }
                            else
                            {
                                specRequiredActions.InsertAction(newAction);
                                if (newAction is AuxActionWPItem || newAction is AuxActSigDelegate)
                                    return;
                            }
                        }
                    }
                }
                specAuxActionsIndex++;
            }
        }

        //================================================================================================//
        //  
        /// <summary>
        /// Reset WP Aux Action, if any
        /// <\summary>

        public void ResetAuxAction(Train thisTrain)
        {
            if (SpecAuxActions.Count <= 0)
                return;
            AIAuxActionsRef thisAction;
            thisAction = (AIAuxActionsRef)SpecAuxActions[0];
            if (thisAction.SubrouteIndex != thisTrain.TCRoute.ActiveSubPath) return;
            thisAction.LinkedAuxAction = false;
            return;
        }

        //================================================================================================//
        //  
        /// <summary>
        /// Move next Aux Action, if in same section, under train in case of decoupling
        /// <\summary>
        public void MoveAuxAction(Train thisTrain)
        {
            AITrain thisAITrain = (AITrain)thisTrain;
            if (SpecAuxActions.Count <= 0)
                return;
            AIAuxActionsRef thisAction;
            thisAction = (AIAuxActionsRef)SpecAuxActions[0];
            if (thisAction is AIActionWPRef && thisAction.SubrouteIndex == thisTrain.TCRoute.ActiveSubPath && thisAction.TCSectionIndex == thisTrain.PresentPosition[Direction.Forward].TrackCircuitSectionIndex)
            // Waiting point is just in the same section where the train is; move it under the train
            {
                AuxActionWPItem thisWPItem;
                if (thisAITrain.nextActionInfo != null && thisAITrain.nextActionInfo is AuxActionWPItem)
                {
                    thisWPItem = (AuxActionWPItem)thisAITrain.nextActionInfo;
                    if (thisWPItem.ActionRef == thisAction)
                    {
                        thisWPItem.ActivateDistanceM = thisTrain.PresentPosition[Direction.Forward].DistanceTravelled - 5;
                        thisAction.LinkedAuxAction = true;
                    }
                }
                thisAction.RequiredDistance = thisTrain.PresentPosition[Direction.Forward].Offset - 5;
            }
        }

        //================================================================================================//
        //  
        /// <summary>
        /// Move next Aux Action, if in same section and in next subpath (reversal in between), under train in case of decoupling
        /// <\summary>
        public void MoveAuxActionAfterReversal(Train thisTrain)
        {
            if (SpecAuxActions.Count <= 0)
                return;
            AIAuxActionsRef thisAction;
            thisAction = (AIAuxActionsRef)SpecAuxActions[0];
            if (thisAction is AIActionWPRef && thisAction.SubrouteIndex == thisTrain.TCRoute.ActiveSubPath + 1 && thisAction.TCSectionIndex == thisTrain.PresentPosition[Direction.Backward].TrackCircuitSectionIndex)
            // Waiting point is just in the same section where the train is; move it under the train
            {
                int thisSectionIndex = thisTrain.PresentPosition[Direction.Backward].TrackCircuitSectionIndex;
                TrackCircuitSection thisSection = TrackCircuitSection.TrackCircuitList[thisSectionIndex];
                thisAction.RequiredDistance = thisSection.Length - thisTrain.PresentPosition[Direction.Backward].Offset - 5;
            }
        }

        public void Add(AuxActionRef action)
        {
            SpecAuxActions.Add(action);
        }
    }

    #endregion
    #region AuxActionRef

    ////================================================================================================//
    ///// <summary>
    ///// AuxActionRef
    ///// info used to figure out one auxiliary action along the route.  It's a reference data, not a run data.
    ///// </summary>

    public class AIAuxActionsRef : AuxActionRef
    {
        public int SubrouteIndex;
        public int RouteIndex;
        public int TCSectionIndex;
        public int Direction;
        protected int TriggerDistance;
        public bool LinkedAuxAction;
        protected List<KeyValuePair<int, WorldLocation>> AskingTrain;
        public Signal SignalReferenced;
        public float RequiredSpeedMpS;
        public float RequiredDistance;
        public int Delay;
        public int EndSignalIndex { get; protected set; }

        public AuxiliaryAction NextAction = AuxiliaryAction.None;

        //================================================================================================//
        /// <summary>
        /// AIAuxActionsRef: Generic Constructor
        /// The specific datas are used to fire the Action.
        /// </summary>

        public AIAuxActionsRef(float requiredSpeedMps, in WorldLocation location)
        {
        }

        public AIAuxActionsRef(Train thisTrain, float distance, float requiredSpeedMpS, int subrouteIdx, int routeIdx, int sectionIdx, int dir, AuxActionRef.AuxiliaryAction actionType = AuxActionRef.AuxiliaryAction.None) :
            base(actionType, false)                 //null, requiredSpeedMpS, , -1, )
        {
            RequiredDistance = distance;
            RequiredSpeedMpS = requiredSpeedMpS;
            SubrouteIndex = subrouteIdx;
            RouteIndex = routeIdx;
            TCSectionIndex = sectionIdx;
            Direction = dir;
            AskingTrain = new List<KeyValuePair<int, WorldLocation>>();
            IsGeneric = false;
            EndSignalIndex = -1;
        }

        public AIAuxActionsRef(Train thisTrain, BinaryReader inf, AuxActionRef.AuxiliaryAction actionType = AuxActionRef.AuxiliaryAction.None)
        {
            RequiredSpeedMpS = inf.ReadSingle();
            RequiredDistance = inf.ReadSingle();
            SubrouteIndex = inf.ReadInt32();
            RouteIndex = inf.ReadInt32();
            TCSectionIndex = inf.ReadInt32();
            Direction = inf.ReadInt32();
            TriggerDistance = inf.ReadInt32();
            IsGeneric = inf.ReadBoolean();
            AskingTrain = new List<KeyValuePair<int, WorldLocation>>();
            EndSignalIndex = inf.ReadInt32();
            if (EndSignalIndex >= 0)
                SetSignalObject(Simulator.Instance.SignalEnvironment.Signals[EndSignalIndex]);
            else
                SetSignalObject(null);
            ActionType = actionType;
        }

        public virtual List<KeyValuePair<System.Type, AuxActionRef>> GetCallFunction()
        {
            return default(List<KeyValuePair<System.Type, AuxActionRef>>);
        }

        //================================================================================================//
        /// <summary>
        /// Handler
        /// Like a fabric, if other informations are needed, please define specific function that can be called on the new object
        /// </summary>


        internal virtual AIActionItem Handler(params object[] list)
        {
            AIActionItem info = null;
            if (!LinkedAuxAction || IsGeneric)
            {
                info = new AuxActionItem(this, AIActionItem.AI_ACTION_TYPE.AUX_ACTION);
                info.SetParam((float)list[0], (float)list[1], (float)list[2], (float)list[3]);

                //info = new AuxActionItem(distance, speed, activateDistance, insertedDistance,
                //                this, AIActionItem.AI_ACTION_TYPE.AUX_ACTION);
            }
            return info;
        }

        //================================================================================================//
        /// <summary>
        /// CalculateDistancesToNextAction
        /// PLease, don't use the default function, redefine it.
        /// </summary>

        public virtual float[] CalculateDistancesToNextAction(Train thisTrain, float presentSpeedMpS, bool reschedule)
        {
            float[] distancesM = new float[2];
            distancesM[1] = 0.0f;
            distancesM[0] = thisTrain.PresentPosition[Orts.Common.Direction.Forward].DistanceTravelled;

            return (distancesM);
        }

        public virtual float[] GetActivationDistances(Train thisTrain)
        {
            float[] distancesM = new float[2];
            distancesM[1] = float.MaxValue;
            distancesM[0] = float.MaxValue;

            return (distancesM);
        }
        //================================================================================================//
        //
        // Save
        //

        public virtual void save(BinaryWriter outf, int cnt)
        {
            outf.Write(cnt);
            string info = NextAction.ToString();
            outf.Write(NextAction.ToString());
            outf.Write(RequiredSpeedMpS);
            outf.Write(RequiredDistance);
            outf.Write(SubrouteIndex);
            outf.Write(RouteIndex);
            outf.Write(TCSectionIndex);
            outf.Write(Direction);
            outf.Write(TriggerDistance);
            outf.Write(IsGeneric);
            outf.Write(EndSignalIndex);

            //if (LinkedAuxAction != null)
            //    outf.Write(LinkedAuxAction.NextAction.ToString());
            //else
            //    outf.Write(AI_AUX_ACTION.NONE.ToString());
        }

        //================================================================================================//
        //
        // Restore
        //

        public void Register(int trainNumber, in WorldLocation location)
        {
            AskingTrain.Add(new KeyValuePair<int, WorldLocation>(trainNumber, location));
        }

        //public bool CheckGenActions(System.Type typeSource, float rearDist, float frontDist, WorldLocation location, uint trackNodeIndex)
        internal virtual AIActionItem CheckGenActions(in WorldLocation location, AITrain thisTrain, params object[] list)
        {
            return null;
        }

        public bool HasAction(int trainNumber, in WorldLocation location)
        {
            foreach (var info in AskingTrain)
            {
                int number = (int)info.Key;
                if (number == trainNumber)
                {
                    WorldLocation locationRegistered = info.Value;
                    if (location == locationRegistered)
                        return true;
                }
            }
            return false;
        }

        public virtual bool CallFreeAction(Train ThisTrain)
        {
            return true;
        }

        public void SetSignalObject(Signal signal)
        {
            SignalReferenced = signal;
        }
    }

    //================================================================================================//
    /// <summary>
    /// AIActionWPRef
    /// info used to figure out a Waiting Point along the route.
    /// </summary>

    internal class AIActionWPRef : AIAuxActionsRef
    {
        public AuxActionWPItem keepIt;

        public AIActionWPRef(Train thisTrain, float distance, float requiredSpeedMpS, int subrouteIdx, int routeIdx, int sectionIdx, int dir)
            : base(thisTrain, distance, requiredSpeedMpS, subrouteIdx, routeIdx, sectionIdx, dir, AuxActionRef.AuxiliaryAction.WaitingPoint)
        {
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "New AIAuxActionRef (WP) for train " + thisTrain.Number +
                " Required Distance " + distance + ", speed " + requiredSpeedMpS + ", and dir " + dir + "\n");
            File.AppendAllText(@"C:\temp\checkpath.txt", "\t\tSection id: " + subrouteIdx + "." + routeIdx + "." + sectionIdx 
                + "\n"); 
#endif
            NextAction = AuxiliaryAction.WaitingPoint;
        }

        public AIActionWPRef(Train thisTrain, BinaryReader inf)
            : base(thisTrain, inf)
        {
            Delay = inf.ReadInt32();
            NextAction = AuxiliaryAction.WaitingPoint;
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "\tRestore one WPAuxAction" +
                "Position in file: " + inf.BaseStream.Position +
                " type Action: " + NextAction.ToString() +
                " Delay: " + Delay + "\n");
#endif
        }

        public override void save(BinaryWriter outf, int cnt)
        {
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "\tSave one WPAuxAction, count :" + cnt +
                "Position in file: " + outf.BaseStream.Position +
                " type Action: " + NextAction.ToString() +
                " Delay: " + Delay + "\n");
#endif
            base.save(outf, cnt);
            outf.Write(Delay);
        }

        internal override AIActionItem Handler(params object[] list)
        {
            AIActionItem info = null;
            if (!LinkedAuxAction || IsGeneric)
            {
                LinkedAuxAction = true;
                info = new AuxActionWPItem(this, AIActionItem.AI_ACTION_TYPE.AUX_ACTION);
                info.SetParam((float)list[0], (float)list[1], (float)list[2], (float)list[3]);
                ((AuxActionWPItem)info).SetDelay(Delay);
                keepIt = (AuxActionWPItem)info;
#if WITH_PATH_DEBUG
                File.AppendAllText(@"C:\temp\checkpath.txt", "New action item, type WP with distance " + distance + ", speed " + speed + ", activate distance  " + activateDistance +
                    " and inserted distance " + insertedDistance + " (delay " + Delay + ")\n");
#endif
            }
            else if (LinkedAuxAction)
            {
                info = keepIt;
            }
            return (AIActionItem)info;
        }

        internal override AIActionItem CheckGenActions(in WorldLocation location, AITrain thisTrain, params object[] list)
        {
            AIActionItem newAction = null;
            int SpeedMps = (int)thisTrain.SpeedMpS;
            TrainCar locomotive = thisTrain.FindLeadLocomotive();
            if (Math.Abs(SpeedMps) <= Simulator.MaxStoppedMpS)   //  We call the handler to generate an actionRef
            {
                newAction = Handler(0f, 0f, thisTrain.DistanceTravelledM, thisTrain.DistanceTravelledM);

                Register(thisTrain.Number, location);
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "Caller registered for\n");
#endif
            }
            return newAction;
        }


        //================================================================================================//
        /// <summary>
        /// SetDelay
        /// To fullfill the waiting delay.
        /// </summary>

        public void SetDelay(int delay)
        {
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "\tDelay set to: " + delay + "\n");
#endif
            Delay = delay;
        }

        public override float[] CalculateDistancesToNextAction(Train thisTrain, float presentSpeedMpS, bool reschedule)
        {
            float[] distancesM = new float[2];

            int thisSectionIndex = thisTrain.PresentPosition[Common.Direction.Forward].TrackCircuitSectionIndex;
            TrackCircuitSection thisSection = TrackCircuitSection.TrackCircuitList[thisSectionIndex];
            float leftInSectionM = thisSection.Length - thisTrain.PresentPosition[Orts.Common.Direction.Forward].Offset;

            // get action route index - if not found, return distances < 0

            int actionIndex0 = thisTrain.PresentPosition[Common.Direction.Forward].RouteListIndex;
            int actionRouteIndex = thisTrain.ValidRoute[0].GetRouteIndex(TCSectionIndex, actionIndex0);
            float activateDistanceTravelledM = thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled + thisTrain.ValidRoute[0].GetDistanceAlongRoute(actionIndex0, leftInSectionM, actionRouteIndex, this.RequiredDistance, thisTrain.AITrainDirectionForward);


            // if reschedule, use actual speed

            float triggerDistanceM = TriggerDistance;

            if (thisTrain.TrainType != TrainType.AiPlayerDriven)
            {

                if (thisTrain is AITrain)
                {
                    AITrain aiTrain = thisTrain as AITrain;
                    if (reschedule)
                    {
                        float firstPartTime = 0.0f;
                        float firstPartRangeM = 0.0f;
                        float secndPartRangeM = 0.0f;
                        float remainingRangeM = activateDistanceTravelledM - thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled;

                        firstPartTime = presentSpeedMpS / (0.25f * aiTrain.MaxDecelMpSS);
                        firstPartRangeM = 0.25f * aiTrain.MaxDecelMpSS * (firstPartTime * firstPartTime);

                        if (firstPartRangeM < remainingRangeM && thisTrain.SpeedMpS < thisTrain.TrainMaxSpeedMpS) // if distance left and not at max speed
                        // split remaining distance based on relation between acceleration and deceleration
                        {
                            secndPartRangeM = (remainingRangeM - firstPartRangeM) * (2.0f * aiTrain.MaxDecelMpSS) / (aiTrain.MaxDecelMpSS + aiTrain.MaxAccelMpSS);
                        }

                        triggerDistanceM = activateDistanceTravelledM - (firstPartRangeM + secndPartRangeM);
                    }
                    else

                    // use maximum speed
                    {
                        float deltaTime = thisTrain.TrainMaxSpeedMpS / aiTrain.MaxDecelMpSS;
                        float brakingDistanceM = (thisTrain.TrainMaxSpeedMpS * deltaTime) + (0.5f * aiTrain.MaxDecelMpSS * deltaTime * deltaTime);
                        triggerDistanceM = activateDistanceTravelledM - brakingDistanceM;
                    }
                }
                else
                {
                    activateDistanceTravelledM = thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled + thisTrain.ValidRoute[0].GetDistanceAlongRoute(actionIndex0, leftInSectionM, actionRouteIndex, this.RequiredDistance, true);
                    triggerDistanceM = activateDistanceTravelledM;
                }

                distancesM[1] = triggerDistanceM;
                if (activateDistanceTravelledM < thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled &&
                    thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled - activateDistanceTravelledM < thisTrain.Length)
                    activateDistanceTravelledM = thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled;
                distancesM[0] = activateDistanceTravelledM;

                return (distancesM);
            }
            else
            {
                activateDistanceTravelledM = thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled + thisTrain.ValidRoute[0].GetDistanceAlongRoute(actionIndex0, leftInSectionM, actionRouteIndex, this.RequiredDistance, true);
                triggerDistanceM = activateDistanceTravelledM - Math.Min(this.RequiredDistance, 300);

                if (activateDistanceTravelledM < thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled &&
                    thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled - activateDistanceTravelledM < thisTrain.Length)
                {
                    activateDistanceTravelledM = thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled;
                    triggerDistanceM = activateDistanceTravelledM;
                }
                distancesM[1] = triggerDistanceM;
                distancesM[0] = activateDistanceTravelledM;

                return (distancesM);
            }
        }


        public override List<KeyValuePair<System.Type, AuxActionRef>> GetCallFunction()
        {
            List<KeyValuePair<System.Type, AuxActionRef>> listInfo = new List<KeyValuePair<System.Type, AuxActionRef>>();

            System.Type managed = typeof(Signal);
            KeyValuePair<System.Type, AuxActionRef> info = new KeyValuePair<System.Type, AuxActionRef>(managed, this);
            listInfo.Add(info);
            return listInfo;
        }

    }
    //================================================================================================//
    /// <summary>
    /// AIActionHornRef
    /// Start and Stop the horn
    /// </summary>

    public class AIActionHornRef : AIAuxActionsRef
    {
        /// <summary>
        /// The duration of the horn blast, if specified by an activity event.
        /// </summary>
        private int? DurationS { get; }

        /// <summary>
        /// The horn pattern to use.
        /// </summary>
        private AILevelCrossingHornPattern HornPattern { get; set; }

        public AIActionHornRef(Train thisTrain, float distance, float requiredSpeedMpS, int subrouteIdx, int routeIdx, int sectionIdx, int dir, int? durationS, AILevelCrossingHornPattern hornPattern)
            : base(thisTrain, distance, requiredSpeedMpS, subrouteIdx, routeIdx, sectionIdx, dir, AuxiliaryAction.SoundHorn)
        {
            DurationS = durationS;
            HornPattern = hornPattern;
            NextAction = AuxiliaryAction.SoundHorn;
        }

        public AIActionHornRef(Train thisTrain, BinaryReader inf)
            : base(thisTrain, inf, AuxiliaryAction.SoundHorn)
        {
            if (inf.ReadBoolean())
                DurationS = inf.ReadInt32();
            HornPattern = AILevelCrossingHornPattern.Restore(inf);
            NextAction = AuxiliaryAction.SoundHorn;
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "\tRestore one WPAuxAction" +
                "Position in file: " + inf.BaseStream.Position +
                " type Action: " + NextAction.ToString() +
                " Delay: " + Delay + "\n");
#endif
        }

        public AIActionHornRef(Train thisTrain, AuxActionHorn myBase, int nop = 0)
            : base(thisTrain, 0f, 0f, 0, 0, 0, 0, myBase.ActionType)
        {
            DurationS = myBase.Delay;
            NextAction = AuxiliaryAction.SoundHorn;
            IsGeneric = myBase.IsGeneric;
            HornPattern = AILevelCrossingHornPattern.CreateInstance(myBase.Pattern);
        }

        public override void save(BinaryWriter outf, int cnt)
        {
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "\tSave one HornAuxAction, count :" + cnt +
                "Position in file: " + outf.BaseStream.Position +
                " type Action: " + NextAction.ToString() + 
                " Delay: " + Delay + "\n");
#endif
            base.save(outf, cnt);
            outf.Write(DurationS.HasValue);
            if (DurationS.HasValue)
                outf.Write(DurationS.Value);
            HornPattern.Save(outf);
        }


        internal override AIActionItem Handler(params object[] list)
        {
            AIActionItem info = null;
            if (!LinkedAuxAction || IsGeneric)
            {
                LinkedAuxAction = true;
                info = new AuxActionHornItem(this, AIActionItem.AI_ACTION_TYPE.AUX_ACTION, DurationS, HornPattern);
                info.SetParam((float)list[0], (float)list[1], (float)list[2], (float)list[3]);
            }
            return (AIActionItem)info;
        }

        //public bool CheckGenActions(System.Type typeSource, float rearDist, float frontDist, WorldLocation location, uint trackNodeIndex)
        internal override AIActionItem CheckGenActions(in WorldLocation location, AITrain thisTrain, params object[] list)
        {
            AIActionItem newAction = null;
            float rearDist = (float)list[0];
            float frontDist = (float)list[1];
            uint trackNodeIndex = (uint)list[2];
            float minDist = Math.Min(Math.Abs(rearDist), frontDist);

            float[] distances = GetActivationDistances(thisTrain);

#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "GenFunctions not yet defined for train:" + thisTrain.Number + 
                " Activation Distance: " + distances[0] + " & train distance: " + (-minDist) + "\n");
#endif
            if (distances[0] >= -minDist)   //  We call the handler to generate an actionRef
            {
                HornPattern = (AILevelCrossingHornPattern)list[3];
                newAction = Handler(distances[0] + thisTrain.DistanceTravelledM, thisTrain.SpeedMpS, distances[0] + thisTrain.DistanceTravelledM, thisTrain.DistanceTravelledM);
                Register(thisTrain.Number, location);
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "Caller registered for\n");
#endif
            }
            return newAction;
        }

        public override List<KeyValuePair<System.Type, AuxActionRef>> GetCallFunction()
        {
            System.Type managed = typeof(LevelCrossings);
            KeyValuePair<System.Type, AuxActionRef> info = new KeyValuePair<System.Type, AuxActionRef>(managed, this);
            List<KeyValuePair<System.Type, AuxActionRef>> listInfo = new List<KeyValuePair<System.Type, AuxActionRef>>();
            listInfo.Add(info);
            return listInfo;
        }

        //  Start horn whatever the speed.

        public override float[] CalculateDistancesToNextAction(Train thisTrain, float presentSpeedMpS, bool reschedule)
        {
            int thisSectionIndex = thisTrain.PresentPosition[Common.Direction.Forward].TrackCircuitSectionIndex;
            TrackCircuitSection thisSection = TrackCircuitSection.TrackCircuitList[thisSectionIndex];
            float leftInSectionM = thisSection.Length - thisTrain.PresentPosition[Common.Direction.Forward].Offset;

            // get action route index - if not found, return distances < 0

            int actionIndex0 = thisTrain.PresentPosition[Common.Direction.Forward].RouteListIndex;
            int actionRouteIndex = thisTrain.ValidRoute[0].GetRouteIndex(TCSectionIndex, actionIndex0);
            float activateDistanceTravelledM = thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled + thisTrain.ValidRoute[0].GetDistanceAlongRoute(actionIndex0, leftInSectionM, actionRouteIndex, this.RequiredDistance, thisTrain.AITrainDirectionForward);
            float[] distancesM = new float[2];
            distancesM[1] = activateDistanceTravelledM;
            distancesM[0] = activateDistanceTravelledM;

            return (distancesM);
        }

        //  SPA:    We use this fonction and not the one from Train in order to leave control to the AuxAction
        public override float[] GetActivationDistances(Train thisTrain)
        {
            float[] distancesM = new float[2];
            distancesM[0] = this.RequiredDistance;   // 
            distancesM[1] = this.RequiredDistance + thisTrain.Length;
            return (distancesM);
        }

    }

    //================================================================================================//
    /// <summary>
    /// AIActSigDelegateRef
    /// An action to delegate the Signal management from a WP
    /// </summary>

    internal class AIActSigDelegateRef : AIAuxActionsRef
    {
        public bool IsAbsolute;
        public AIActionWPRef AssociatedWPAction;
        public float brakeSection;
        protected AuxActSigDelegate AssociatedItem;  //  In order to Unlock the signal when removing Action Reference

        public AIActSigDelegateRef(Train thisTrain, float distance, float requiredSpeedMpS, int subrouteIdx, int routeIdx, int sectionIdx, int dir, AIActionWPRef associatedWPAction = null)
            : base(thisTrain, distance, requiredSpeedMpS, subrouteIdx, routeIdx, sectionIdx, dir, AuxiliaryAction.SignalDelegate)
        {
            AssociatedWPAction = associatedWPAction;
            NextAction = AuxiliaryAction.SignalDelegate;
            IsGeneric = true;

            brakeSection = distance; // Set to 1 later when applicable
        }

        public AIActSigDelegateRef(Train thisTrain, BinaryReader inf)
            : base(thisTrain, inf, AuxiliaryAction.SignalDelegate)
        {
            Delay = inf.ReadInt32();
            brakeSection = (float)inf.ReadSingle();
            IsAbsolute = inf.ReadBoolean();
            NextAction = AuxiliaryAction.SignalDelegate;
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "\tRestore one WPAuxAction" +
                "Position in file: " + inf.BaseStream.Position +
                " type Action: " + NextAction.ToString() +
                " Delay: " + Delay + "\n");
#endif
        }

        public override void save(BinaryWriter outf, int cnt)
        {
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "\tSave one SigDelegate, count :" + cnt +
                "Position in file: " + outf.BaseStream.Position +
                " type Action: " + NextAction.ToString() +
                " Delay: " + Delay + "\n");
#endif
            base.save(outf, cnt);
            outf.Write(Delay);
            outf.Write(brakeSection);
            outf.Write(IsAbsolute);
            var hasWPActionAssociated = false;
            if (AssociatedWPAction != null) hasWPActionAssociated = true;
            outf.Write(hasWPActionAssociated);
        }

        public override bool CallFreeAction(Train thisTrain)
        {
            if (AssociatedItem != null && AssociatedItem.SignalReferenced != null)
            {
                if (AssociatedItem.locked)
                    AssociatedItem.SignalReferenced.UnlockForTrain(thisTrain.Number, thisTrain.TCRoute.ActiveSubPath);
                AssociatedItem.SignalReferenced = null;
                AssociatedItem = null;
                return true;
            }
            else if (AssociatedItem == null)
                return true;
            return false;
        }

        internal override AIActionItem Handler(params object[] list)
        {
            if (AssociatedItem != null)
                return null;
            AuxActSigDelegate info = new AuxActSigDelegate(this, AIActionItem.AI_ACTION_TYPE.AUX_ACTION);
            info.SetParam((float)list[0], (float)list[1], (float)list[2], (float)list[3]);
            AssociatedItem = info;
            return (AIActionItem)info;
        }

        //  SigDelegateRef.

        public override float[] CalculateDistancesToNextAction(Train thisTrain, float presentSpeedMpS, bool reschedule)
        {
            int thisSectionIndex = thisTrain.PresentPosition[Common.Direction.Forward].TrackCircuitSectionIndex;
            TrackCircuitSection thisSection = TrackCircuitSection.TrackCircuitList[thisSectionIndex];
            float leftInSectionM = thisSection.Length - thisTrain.PresentPosition[Common.Direction.Forward].Offset;

            // get action route index - if not found, return distances < 0

            int actionIndex0 = thisTrain.PresentPosition[Common.Direction.Forward].RouteListIndex;
            int actionRouteIndex = thisTrain.ValidRoute[0].GetRouteIndex(TCSectionIndex, actionIndex0);
            float activateDistanceTravelledM = -1;

            if (actionIndex0 != -1 && actionRouteIndex != -1)
                activateDistanceTravelledM = thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled + thisTrain.ValidRoute[0].GetDistanceAlongRoute(actionIndex0, leftInSectionM, actionRouteIndex, this.RequiredDistance, true);

            var currBrakeSection = (thisTrain is AITrain && !(thisTrain.TrainType == TrainType.AiPlayerDriven)) ? 1 : brakeSection;
            float triggerDistanceM = activateDistanceTravelledM - Math.Min(this.RequiredDistance, 300);   //  TODO, add the size of train

            float[] distancesM = new float[2];
            distancesM[1] = triggerDistanceM;
            if (activateDistanceTravelledM < thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled &&
                thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled - activateDistanceTravelledM < thisTrain.Length)
                activateDistanceTravelledM = thisTrain.PresentPosition[Common.Direction.Forward].DistanceTravelled;
            distancesM[0] = activateDistanceTravelledM;

            return (distancesM);
        }

        public void SetEndSignalIndex(int idx)
        {
            EndSignalIndex = idx;
        }

        //================================================================================================//
        /// <summary>
        /// SetDelay
        /// To fullfill the waiting delay.
        /// </summary>

        public void SetDelay(int delay)
        {
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "\tDelay set to: " + delay + "\n");
#endif
            Delay = delay;
        }

    }



    #endregion

    #region AuxActionData

    //================================================================================================//
    /// <summary>
    /// AuxActionItem
    /// A specific AIActionItem used at run time to manage a specific Auxiliary Action
    /// </summary>

    internal class AuxActionItem : AIActionItem
    {
        public AuxActionRef ActionRef;
        public bool Triggered;
        public bool Processing;
        public AiMovementState currentMvmtState = AiMovementState.InitAction;
        public Signal SignalReferenced { get { return ((AIAuxActionsRef)ActionRef).SignalReferenced; } set { } }

        //================================================================================================//
        /// <summary>
        /// AuxActionItem
        /// The basic constructor
        /// </summary>

        public AuxActionItem(AuxActionRef thisItem, AI_ACTION_TYPE thisAction) :
            base(null, thisAction)
        {
            NextAction = AI_ACTION_TYPE.AUX_ACTION;
            ActionRef = thisItem;
        }

        //================================================================================================//
        //
        // Restore
        //

        public AuxActionItem(BinaryReader inf)
            : base(inf)
        {
        }

        public virtual bool CanActivate(Train thisTrain, float SpeedMpS, bool reschedule)
        {
            return false;
        }

        public virtual bool CanRemove(Train thisTrain)
        {
            return true;
        }

        public override AiMovementState ProcessAction(Train thisTrain, int presentTime, double elapsedClockSeconds, AiMovementState movementState)
        {
            AiMovementState mvtState = movementState;
            if (ActionRef.IsGeneric)
                mvtState = currentMvmtState;
            int correctedTime = presentTime;
            switch (mvtState)
            {
                case AiMovementState.InitAction:
                    movementState = InitAction(thisTrain, presentTime, elapsedClockSeconds, mvtState);
                    break;
                case AiMovementState.HandleAction:
                    movementState = HandleAction(thisTrain, presentTime, elapsedClockSeconds, mvtState);
                    break;
                case AiMovementState.Braking:
                    break;
                case AiMovementState.Stopped:
                    break;
                default:
                    break;
            }
            currentMvmtState = movementState;
            return movementState;
        }

        public override AiMovementState ProcessAction(Train thisTrain, int presentTime)
        {
            int correctedTime = presentTime;
            switch (currentMvmtState)
            {
                case AiMovementState.InitAction:
                case AiMovementState.Stopped:
                    currentMvmtState = InitAction(thisTrain, presentTime, 0f, currentMvmtState);
                    break;
                case AiMovementState.HandleAction:
                    currentMvmtState = HandleAction(thisTrain, presentTime, 0f, currentMvmtState);
                    break;
                default:
                    break;
            }
            return currentMvmtState;
        }



#if WITH_PATH_DEBUG
        public override string AsString(AITrain thisTrain)
        {
            return " AUX(";
        }
#endif
    }

    //================================================================================================//
    /// <summary>
    /// AuxActionWPItem
    /// A specific class used at run time to manage a Waiting Point Action
    /// </summary>

    internal class AuxActionWPItem : AuxActionItem
    {
        private int Delay;
        public int ActualDepart;

        //================================================================================================//
        /// <summary>
        /// AuxActionWPItem
        /// The specific constructor for WP action
        /// </summary>

        public AuxActionWPItem(AuxActionRef thisItem, AI_ACTION_TYPE thisAction) :
            base(thisItem, thisAction)
        {
            ActualDepart = 0;
        }

        //================================================================================================//
        /// <summary>
        /// AsString
        /// Used by debugging in HUDWindows.
        /// </summary>

        public override string AsString(AITrain thisTrain)
        {
            return " WP(";
        }

        public override bool CanActivate(Train thisTrain, float SpeedMpS, bool reschedule)
        {
            if (ActionRef == null || thisTrain.PresentPosition[Direction.Forward].RouteListIndex == -1)
                return false;
            float[] distancesM = ((AIAuxActionsRef)ActionRef).CalculateDistancesToNextAction(thisTrain, SpeedMpS, reschedule);
            if (thisTrain.TrainType != TrainType.AiPlayerDriven)
            {
                if (RequiredDistance < thisTrain.DistanceTravelledM) // trigger point
                {
                    return true;
                }

                RequiredDistance = distancesM[1];
                ActivateDistanceM = distancesM[0];
                return false;
            }
            else
            {
                if (Math.Abs(thisTrain.SpeedMpS) <= 0.1f && distancesM[1] <= thisTrain.DistanceTravelledM)
                {
                    return true;
                }
                return false;
            }
        }

        public void SetDelay(int delay)
        {
            Delay = delay;
        }

        public override bool ValidAction(Train thisTrain)
        {
            bool actionValid = false;

            actionValid = CanActivate(thisTrain, thisTrain.SpeedMpS, true);
            if (thisTrain is AITrain && thisTrain.TrainType != TrainType.AiPlayerDriven)
            {
                AITrain aiTrain = thisTrain as AITrain;
                if (!actionValid)
                {
                    aiTrain.requiredActions.InsertAction(this);
                }
                aiTrain.EndProcessAction(actionValid, this, false);
            }
            return actionValid;
        }

        public override AiMovementState InitAction(Train thisTrain, int presentTime, double elapsedClockSeconds, AiMovementState movementState)
        {
            if (thisTrain is AITrain)
            {
                AITrain aiTrain = thisTrain as AITrain;

                // repeat stopping of train, because it could have been moved by UpdateBrakingState after ProcessAction
                if (aiTrain.TrainType != TrainType.AiPlayerDriven)
                {
                    aiTrain.AdjustControlsBrakeMore(aiTrain.MaxDecelMpSS, elapsedClockSeconds, 100);
                    aiTrain.SpeedMpS = 0;
                }
                int correctedTime = presentTime;
                // If delay between 40000 and 60000 an uncoupling is performed and delay is returned with the two lowest digits of the original one
                aiTrain.TestUncouple(ref Delay);
                // If delay between 30000 and 40000 it is considered an absolute delay in the form 3HHMM, where HH and MM are hour and minute where the delay ends
                Delay = thisTrain.TestAbsDelay(Delay, correctedTime);
                // If delay equal to 60001 it is considered as a command to unconditionally attach to the nearby train;
                aiTrain.TestUncondAttach(ref Delay);
                // If delay equal to 60002 it is considered as a request for permission to pass signal;
                aiTrain.TestPermission(ref Delay);
                ActualDepart = correctedTime + Delay;
                aiTrain.AuxActionsContainer.CheckGenActions(this.GetType(), aiTrain.RearTDBTraveller.WorldLocation, Delay);

#if WITH_PATH_DEBUG
                File.AppendAllText(@"C:\temp\checkpath.txt", "WP, init action for train " + aiTrain.Number + " at " + correctedTime + " to " + ActualDepart + "(HANDLE_ACTION)\n");
#endif
            }

            return AiMovementState.HandleAction;
        }

        public override AiMovementState HandleAction(Train thisTrain, int presentTime, double elapsedClockSeconds, AiMovementState movementState)
        {
            if (thisTrain is AITrain)
            {
                if (thisTrain.TrainType != TrainType.AiPlayerDriven)
                {
                    thisTrain.SpeedMpS = 0;
                }
                AITrain aiTrain = thisTrain as AITrain;

                thisTrain.AuxActionsContainer.CheckGenActions(this.GetType(), aiTrain.RearTDBTraveller.WorldLocation, ActualDepart - presentTime);

                if (ActualDepart > presentTime)
                {
                    movementState = AiMovementState.HandleAction;
                }
                else
                {
#if WITH_PATH_DEBUG
                    File.AppendAllText(@"C:\temp\checkpath.txt", "WP, End Handle action for train " + aiTrain.Number + " at " + presentTime + "(END_ACTION)\n");
#endif
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "WP, Action ended for train " + thisTrain.Number + " at " + presentTime + "(STOPPED)\n");
#endif
                    if (thisTrain.AuxActionsContainer.CountSpec() > 0)
                        thisTrain.AuxActionsContainer.Remove(this);
                    return AiMovementState.Stopped;
                }
            }
            else
            {
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "WP, Action ended for train " + thisTrain.Number + " at " + presentTime + "(STOPPED)\n");
#endif
                if (thisTrain.AuxActionsContainer.CountSpec() > 0)
                    thisTrain.AuxActionsContainer.Remove(this);
                return AiMovementState.Stopped;
            }
            return movementState;
        }

        public override AiMovementState ProcessAction(Train thisTrain, int presentTime, double elapsedClockSeconds, AiMovementState movementState)
        {
            //int correctedTime = presentTime;
            //switch (movementState)
            //{
            AiMovementState mvtState = movementState;
            if (ActionRef.IsGeneric)
                mvtState = currentMvmtState;
            int correctedTime = presentTime;
            switch (mvtState)
            {
                case AiMovementState.InitAction:
                    movementState = InitAction(thisTrain, presentTime, elapsedClockSeconds, movementState);
                    break;
                case AiMovementState.HandleAction:
                    movementState = HandleAction(thisTrain, presentTime, elapsedClockSeconds, movementState);
                    break;
                case AiMovementState.Braking:
                    if (thisTrain is AITrain)
                    {
                        AITrain aiTrain = thisTrain as AITrain;
                        float distanceToGoM = thisTrain.ActivityClearingDistanceM;
                        distanceToGoM = ActivateDistanceM - aiTrain.PresentPosition[Direction.Forward].DistanceTravelled;
                        float NextStopDistanceM = distanceToGoM;
                        if (distanceToGoM <= 0f)
                        {
                            aiTrain.AdjustControlsBrakeMore(aiTrain.MaxDecelMpSS, elapsedClockSeconds, 100);
                            aiTrain.AITrainThrottlePercent = 0;

                            if (aiTrain.SpeedMpS < 0.001)
                            {
                                aiTrain.SpeedMpS = 0f;
                                movementState = AiMovementState.InitAction;
                            }
                        }
                        else if (distanceToGoM < AITrain.signalApproachDistanceM && Math.Abs(aiTrain.SpeedMpS) <= 0.1f)
                        {
                            aiTrain.AdjustControlsBrakeMore(aiTrain.MaxDecelMpSS, elapsedClockSeconds, 100);
                            movementState = AiMovementState.InitAction;
                        }
                    }
                    else
                    {
                        if (thisTrain.AuxActionsContainer.CountSpec() > 0)
                            thisTrain.AuxActionsContainer.RemoveAt(0);
                    }
                    break;
                case AiMovementState.Stopped:
                    if (!(thisTrain is AITrain))
                        if (thisTrain.AuxActionsContainer.CountSpec() > 0)
                            thisTrain.AuxActionsContainer.Remove(this);


#if WITH_PATH_DEBUG
                    else
                    {
                        File.AppendAllText(@"C:\temp\checkpath.txt", "AITRain " + thisTrain.Number + "!  No more AuxActions...\n");
                    }
#endif
                    if (thisTrain is AITrain)
                    {
                        AITrain aiTrain = thisTrain as AITrain;

                        //movementState = thisTrain.UpdateStoppedState();   // Don't call UpdateStoppedState(), WP can't touch Signal
                        movementState = AiMovementState.Braking;
                        aiTrain.ResetActions(true);
#if WITH_PATH_DEBUG
                        File.AppendAllText(@"C:\temp\checkpath.txt", "AITRain " + aiTrain.Number + " is " + movementState.ToString() + " at " + presentTime + "\n");
#endif
                    }
                    break;
                default:
                    break;
            }
            if (ActionRef.IsGeneric)
                currentMvmtState = movementState;
            return movementState;
        }

    }




    //================================================================================================//
    /// <summary>
    /// AuxActionHornItem
    /// A specific class used at run time to manage a Horn Action
    /// </summary>

    internal class AuxActionHornItem : AuxActionItem
    {
        private int? DurationS { get; }
        private AILevelCrossingHornPattern HornPattern { get; }
        private int NextStepTimeS { get; set; }
        private IEnumerator<int> Execution { get; set; }

        //================================================================================================//
        /// <summary>
        /// AuxActionhornItem
        /// The specific constructor for horn action
        /// </summary>

        public AuxActionHornItem(AuxActionRef thisItem, AI_ACTION_TYPE thisAction, int? durationS, AILevelCrossingHornPattern hornPattern) :
            base(thisItem, thisAction)
        {
            DurationS = durationS;
            HornPattern = hornPattern;
        }

        //================================================================================================//
        /// <summary>
        /// AsString
        /// Used by debugging in HUDWindows.
        /// </summary>

        public override string AsString(AITrain thisTrain)
        {
            return " Horn(";
        }

        public override bool CanActivate(Train thisTrain, float SpeedMpS, bool reschedule)
        {
            if (ActionRef == null)
                return false;
            float[] distancesM = ((AIAuxActionsRef)ActionRef).CalculateDistancesToNextAction(thisTrain, SpeedMpS, reschedule);


            if (RequiredDistance < thisTrain.DistanceTravelledM) // trigger point
            {
                return true;
            }

            RequiredDistance = distancesM[1];
            ActivateDistanceM = distancesM[0];
            return false;
        }

        public override bool ValidAction(Train thisTrain)
        {
            bool actionValid = CanActivate(thisTrain, thisTrain.SpeedMpS, true);
            if (!(thisTrain is AITrain))
            {
                AITrain aiTrain = thisTrain as AITrain;

                if (!actionValid)
                {
                    aiTrain.requiredActions.InsertAction(this);
                }
                aiTrain.EndProcessAction(actionValid, this, false);
            }
            return actionValid;
        }

        public override AiMovementState InitAction(Train thisTrain, int presentTime, double elapsedClockSeconds, AiMovementState movementState)
        {
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "AITRain " + thisTrain.Number + " is " + movementState.ToString() + " at " + presentTime + "\n");
#endif
            Processing = true;
            int correctedTime = presentTime;
            if (!Triggered)
            {
                NextStepTimeS = correctedTime;
                var locomotive = (MSTSLocomotive)thisTrain.FindLeadLocomotive();
                Execution = HornPattern.Execute(locomotive, DurationS);
#if WITH_PATH_DEBUG
                    File.AppendAllText(@"C:\temp\checkpath.txt", "Do Horn for AITRain " + thisTrain.Number + " , mvt state " + movementState.ToString() + " at " + presentTime + "\n");
#endif
                Triggered = true;
            }
            return AiMovementState.HandleAction;
        }

        public override AiMovementState HandleAction(Train thisTrain, int presentTime, double elapsedClockSeconds, AiMovementState movementState)
        {
            if (Triggered && presentTime > NextStepTimeS)
            {
                // Advance to the next step.
                if (Execution.MoveNext())
                {
                    NextStepTimeS = presentTime + Execution.Current;
                    return AiMovementState.HandleAction;
                }
                else
                {
                    thisTrain.AuxActionsContainer.Remove(this);
                    Triggered = false;
                    return currentMvmtState;    //  Restore previous MovementState
                }
            }
            else
            {
                return movementState;
            }
        }

        public override AiMovementState ProcessAction(Train thisTrain, int presentTime, double elapsedClockSeconds, AiMovementState movementState)
        {
            AiMovementState mvtState = movementState;
            if (ActionRef.IsGeneric)
                mvtState = currentMvmtState;
            int correctedTime = presentTime;
            switch (mvtState)
            {
                case AiMovementState.InitAction:
                    movementState = InitAction(thisTrain, presentTime, elapsedClockSeconds, mvtState);
                    break;
                case AiMovementState.HandleAction:
                    movementState = HandleAction(thisTrain, presentTime, elapsedClockSeconds, mvtState);
                    break;
                case AiMovementState.Braking:
                    if (this.ActionRef.ActionType != AuxActionRef.AuxiliaryAction.SoundHorn)
                    {
                        float distanceToGoM = thisTrain.ActivityClearingDistanceM;
                        distanceToGoM = ActivateDistanceM - thisTrain.PresentPosition[Direction.Forward].DistanceTravelled;
                        float NextStopDistanceM = distanceToGoM;
                        if (distanceToGoM < 0f)
                        {
                            currentMvmtState = movementState;
                            movementState = AiMovementState.InitAction;
                        }
                    }
                    break;
                case AiMovementState.Stopped:
                    if (thisTrain is AITrain)
                        movementState = ((AITrain)thisTrain).UpdateStoppedState(elapsedClockSeconds);
                    break;
                default:
                    break;
            }
            if (ActionRef.IsGeneric)
                currentMvmtState = movementState;

            return movementState;
        }

    }

    //================================================================================================//
    /// <summary>
    /// AuxActSigDelegate
    /// Used to postpone the signal clear after WP
    /// </summary>

    internal class AuxActSigDelegate : AuxActionItem
    {
        public int ActualDepart;
        public bool locked = true;

        //================================================================================================//
        /// <summary>
        /// AuxActSigDelegate Item
        /// The specific constructor for AuxActSigDelegate action
        /// </summary>

        public AuxActSigDelegate(AuxActionRef thisItem, AI_ACTION_TYPE thisAction) :
            base(thisItem, thisAction)
        {
            ActualDepart = 0;
        }

        //================================================================================================//
        /// <summary>
        /// AsString
        /// Used by debugging in HUDWindows.
        /// </summary>

        public override string AsString(AITrain thisTrain)
        {
            return " SigDlgt(";
        }

        public bool ClearSignal(Train thisTrain)
        {
            if (SignalReferenced != null)
            {
                bool ret = SignalReferenced.RequestClearSignal(thisTrain.ValidRoute[0], thisTrain.RoutedForward, 0, false, null);
                return ret;
            }
            return true;
        }

        public override bool CanActivate(Train thisTrain, float SpeedMpS, bool reschedule)
        {
            if (ActionRef == null || SignalReferenced == null)
            {
                thisTrain.AuxActionsContainer.RemoveSpecReqAction(this);
                return false;
            }
            if (ActionRef != null && ((AIAuxActionsRef)ActionRef).LinkedAuxAction)
                return false;
            float[] distancesM = ((AIAuxActionsRef)ActionRef).CalculateDistancesToNextAction(thisTrain, SpeedMpS, reschedule);
            if (distancesM[0] < thisTrain.DistanceTravelledM && !((AIActSigDelegateRef)ActionRef).IsAbsolute) // trigger point
            {
                if (thisTrain.SpeedMpS > 0f)
                {
                    if (thisTrain is AITrain && thisTrain.TrainType != TrainType.AiPlayerDriven)
                    {
                        thisTrain.SetTrainOutOfControl(OutOfControlReason.OutOfPath);
                        return true;
                    }
                    else return false;
                }
            }

            if (!reschedule && distancesM[1] < thisTrain.DistanceTravelledM && (Math.Abs(thisTrain.SpeedMpS) <= 0.1f ||
                (thisTrain.IsPlayerDriven && currentMvmtState == AiMovementState.HandleAction)))
            {
                return true;
            }

            if (!reschedule && ((AIActSigDelegateRef)ActionRef).IsAbsolute)
            {
                TrackCircuitSection thisSection = TrackCircuitSection.TrackCircuitList[((AIActSigDelegateRef)ActionRef).TCSectionIndex];
                if (((thisSection.CircuitState.TrainReserved != null && thisSection.CircuitState.TrainReserved.Train == thisTrain) || thisSection.CircuitState.OccupiedByThisTrain(thisTrain)) &&
                    ((AIActSigDelegateRef)ActionRef).EndSignalIndex != -1)
                    return true;
            }

            //RequiredDistance = distancesM[1];
            //ActivateDistanceM = distancesM[0];
            return false;
        }

        public override bool CanRemove(Train thisTrain)
        {
            if (Processing && (currentMvmtState == AiMovementState.Stopped || currentMvmtState == AiMovementState.HandleAction))
                return true;
            if (SignalReferenced == null)
                return true;
            return false;
        }


        public override bool ValidAction(Train thisTrain)
        {
            bool actionValid = CanActivate(thisTrain, thisTrain.SpeedMpS, true);
            if (!actionValid)
            {
                //thisTrain.requiredActions.InsertAction(this);
            }
            return actionValid;
        }

        public override AiMovementState InitAction(Train thisTrain, int presentTime, double elapsedClockSeconds, AiMovementState movementState)
        {
            int delay = ((AIActSigDelegateRef)ActionRef).Delay;
            // If delay between 30000 and 40000 it is considered an absolute delay in the form 3HHMM, where HH and MM are hour and minute where the delay ends
            delay = thisTrain.TestAbsDelay(delay, presentTime);
            ActualDepart = presentTime + delay;
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "SigDelegate, init action for train " + thisTrain.Number + " at " + 
                presentTime + "(HANDLE_ACTION)\n");
#endif
            Processing = true;
            return AiMovementState.HandleAction;
        }

        public override AiMovementState HandleAction(Train thisTrain, int presentTime, double elapsedClockSeconds, AiMovementState movementState)
        {
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "WP, End Handle action for train " + thisTrain.Number + 
                " at " + presentTime + "(END_ACTION)\n");
#endif
            if (ActualDepart >= presentTime)
            {

                return movementState;
            }
            if (locked && SignalReferenced != null)
            {
                locked = false;
                if (SignalReferenced.HasLockForTrain(thisTrain.Number, thisTrain.TCRoute.ActiveSubPath))
                    SignalReferenced.UnlockForTrain(thisTrain.Number, thisTrain.TCRoute.ActiveSubPath);
                else
                {
                    //                    locked = true;
                    Trace.TraceWarning("SignalObject trItem={0}, trackNode={1}, wasn't locked for train {2}.",
                        SignalReferenced.TrackItemIndex, SignalReferenced.TrackNode, thisTrain.Number);
                }
            }
            if (ClearSignal(thisTrain) || (thisTrain.NextSignalObject[0] != null && (thisTrain.NextSignalObject[0].SignalLR(SignalFunction.Normal) > SignalAspectState.Stop)) ||
                thisTrain.NextSignalObject[0] == null || SignalReferenced != thisTrain.NextSignalObject[0] ||
                thisTrain.PresentPosition[Direction.Forward].TrackCircuitSectionIndex == thisTrain.ValidRoute[0][thisTrain.ValidRoute[0].Count - 1].TrackCircuitSection.Index)
            {
#if WITH_PATH_DEBUG
            File.AppendAllText(@"C:\temp\checkpath.txt", "WP, Action ended for train " + thisTrain.Number + " at " + presentTime + "(STOPPED)\n");
#endif
                if (((AIActSigDelegateRef)ActionRef).AssociatedWPAction != null)
                {
                    var WPAction = ((AIActSigDelegateRef)ActionRef).AssociatedWPAction.keepIt;
                    if (thisTrain.requiredActions.Contains(WPAction))
                    {
                        thisTrain.requiredActions.Remove(WPAction);
                    }
                    if (thisTrain.AuxActionsContainer.specRequiredActions.Contains(WPAction))
                        thisTrain.AuxActionsContainer.specRequiredActions.Remove(WPAction);
                    if (thisTrain.AuxActionsContainer.SpecAuxActions.Contains(((AIActSigDelegateRef)ActionRef).AssociatedWPAction))
                        thisTrain.AuxActionsContainer.SpecAuxActions.Remove(((AIActSigDelegateRef)ActionRef).AssociatedWPAction);
                }
                if (thisTrain.AuxActionsContainer.CountSpec() > 0)
                {
                    thisTrain.AuxActionsContainer.Remove(this);
                }
#if WITH_PATH_DEBUG
                else
                {
                    File.AppendAllText(@"C:\temp\checkpath.txt", "AITRain " + thisTrain.Number + "!  No more AuxActions...\n");
                }
#endif
                return (thisTrain is AITrain && (thisTrain as AITrain).MovementState == AiMovementState.StationStop ? AiMovementState.StationStop : AiMovementState.Stopped);
            }
            return movementState;
        }

        public override AiMovementState ProcessAction(Train thisTrain, int presentTime, double elapsedClockSeconds, AiMovementState movementState)
        {
            movementState = base.ProcessAction(thisTrain, presentTime, elapsedClockSeconds, movementState);
            return movementState;
        }
    }

    #endregion
}