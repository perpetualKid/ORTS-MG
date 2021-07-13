// COPYRIGHT 2010, 2011, 2012, 2013, 2014, 2015 by the Open Rails project.
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

// This file is the responsibility of the 3D & Environment Team. 

using System;
using System.Linq;

using Microsoft.Xna.Framework;

using Orts.Common;
using Orts.Common.Input;
using Orts.Simulation;
using Orts.Simulation.AIs;
using Orts.Simulation.Physics;

namespace Orts.ActivityRunner.Viewer3D.Popups
{
    public class TrainListWindow : Window
    {
        public TrainListWindow(WindowManager owner)
            : base(owner, DecorationSize.X + (owner?.TextFontDefault.Height ?? throw new ArgumentNullException(nameof(owner))) * 20, DecorationSize.Y + owner.TextFontDefault.Height * 30, Viewer.Catalog.GetString("Train List"))
        {
        }

        protected override ControlLayout Layout(ControlLayout layout)
        {
            ControlLayoutVertical vbox = base.Layout(layout).AddLayoutVertical();
            if (Owner.Viewer.Simulator.ActivityFile != null || Owner.Viewer.Simulator.TimetableMode)
            {
                int colWidth = (vbox.RemainingWidth - vbox.TextHeight * 2) / 5;

                ControlLayoutHorizontal line = vbox.AddLayoutHorizontalLineOfText();
                line.Add(new Label(colWidth, line.RemainingHeight, Viewer.Catalog.GetString("Number")));
                line.Add(new Label(colWidth * 3, line.RemainingHeight, Viewer.Catalog.GetString("Service Name"), LabelAlignment.Left));
                line.Add(new Label(colWidth, line.RemainingHeight, Viewer.Catalog.GetString("Viewed"), LabelAlignment.Right));

                vbox.AddHorizontalSeparator();
                ControlLayout scrollbox = vbox.AddLayoutScrollboxVertical(vbox.RemainingWidth);
                Train train0 = Owner.Viewer.Simulator.Trains.Find(item => item.IsActualPlayerTrain);
                if (train0 != null)
                {
                    TrainLabel number, name, viewed;
                    line = scrollbox.AddLayoutHorizontalLineOfText();
                    line.Add(number = new TrainLabel(colWidth, line.RemainingHeight, Owner.Viewer, train0, $"{train0.Number}", LabelAlignment.Left));
                    line.Add(name = new TrainLabel(colWidth * 4 - Owner.TextFontDefault.Height, line.RemainingHeight, Owner.Viewer, train0, train0.Name, LabelAlignment.Left));
                    if (train0 == Owner.Viewer.SelectedTrain)
                    {
                        line.Add(viewed = new TrainLabel(Owner.TextFontDefault.Height, line.RemainingHeight, Owner.Viewer, train0, "*", LabelAlignment.Right));
                        viewed.Color = Color.Red;
                    }
                    if (Owner.Viewer.Simulator.IsAutopilotMode)
                    {
                        number.Color = train0.IsPlayable ? Color.LightGreen : Color.White;
                        name.Color = number.Color;
                    }
                    if (train0 is AITrain && (train0 as AITrain).MovementState == AiMovementState.Suspended)
                    {
                        number.Color = Color.Orange;
                        name.Color = Color.Orange;
                    }
                    if (train0.IsActualPlayerTrain)
                    {
                        number.Color = Color.Red;
                        name.Color = Color.Red;
                    }

                }
                foreach (AITrain train in Owner.Viewer.Simulator.AI.AITrains)
                {
                    if (train.MovementState != AiMovementState.Static && train.TrainType != TrainType.Player
                        && !(train.TrainType == TrainType.AiIncorporated && !train.IncorporatingTrain.IsPathless))
                    {
                        line = scrollbox.AddLayoutHorizontalLineOfText();
                        TrainLabel number, name, viewed;
                        line.Add(number = new TrainLabel(colWidth, line.RemainingHeight, Owner.Viewer, train, $"{train.Number}", LabelAlignment.Left));
                        line.Add(name = new TrainLabel(colWidth * 4 - Owner.TextFontDefault.Height, line.RemainingHeight, Owner.Viewer, train, train.Name, LabelAlignment.Left));
                        if (train == Owner.Viewer.SelectedTrain)
                        {
                            line.Add(viewed = new TrainLabel(Owner.TextFontDefault.Height, line.RemainingHeight, Owner.Viewer, train, "*", LabelAlignment.Right));
                            viewed.Color = Color.Red;
                        }
                        if (Owner.Viewer.Simulator.IsAutopilotMode)
                        {
                            number.Color = train.IsPlayable ? Color.LightGreen : Color.White;
                            name.Color = number.Color;
                        }
                        if (train.MovementState == AiMovementState.Suspended)
                        {
                            number.Color = Color.Orange;
                            name.Color = Color.Orange;
                        }
                        if (train.IsActualPlayerTrain)
                        {
                            number.Color = Color.Red;
                            name.Color = Color.Red;
                        }
                    }
                }

                // Now list static trains with loco and cab
                if (Owner.Viewer.Simulator.IsAutopilotMode)
                {
                    foreach (Train train in Owner.Viewer.Simulator.Trains.Where(t => t.TrainType == TrainType.Static && t.IsPlayable))
                    {
                        line = scrollbox.AddLayoutHorizontalLineOfText();
                        TrainLabel number, name, viewed;
                        line.Add(number = new TrainLabel(colWidth, line.RemainingHeight, Owner.Viewer, train, $"{train.Number}", LabelAlignment.Left));
                        line.Add(name = new TrainLabel(colWidth * 4 - Owner.TextFontDefault.Height, line.RemainingHeight, Owner.Viewer, train, train.Name, LabelAlignment.Left));
                        if (train == Owner.Viewer.SelectedTrain)
                        {
                            line.Add(viewed = new TrainLabel(Owner.TextFontDefault.Height, line.RemainingHeight, Owner.Viewer, train, "*", LabelAlignment.Right));
                            viewed.Color = Color.Red;
                        }
                        number.Color = Color.Yellow;
                        name.Color = Color.Yellow;
                    }
                }
            }
            return vbox;
        }

        public override void PrepareFrame(in ElapsedTime elapsedTime, bool updateFull)
        {
            base.PrepareFrame(elapsedTime, updateFull);

            if (updateFull && (Owner.Viewer.Simulator.ActivityFile != null || Owner.Viewer.Simulator.TimetableMode) && Owner.Viewer.Simulator.AI.aiListChanged)
            {
                Owner.Viewer.Simulator.AI.aiListChanged = false;
                Layout();
            }
        }
    }

    internal class TrainLabel : Label
    {
        private readonly Viewer Viewer;
        private readonly Train PickedTrainFromList;

        public TrainLabel(int width, int height, Viewer viewer, Train train, string trainName, LabelAlignment alignment)
            : base(width, height, trainName, alignment)
        {
            Viewer = viewer;
            PickedTrainFromList = train;
            OnClick += TrainLabel_OnClick;
        }

        private void TrainLabel_OnClick(object sender, MouseClickEventArgs e)
        {
            if (PickedTrainFromList?.ControlMode == TrainControlMode.TurnTable)
            {
                Viewer.Simulator.Confirmer.Information(Viewer.Catalog.GetString("Train in turntable not aligned to a track can't be selected"));
                return;
            }
            if (PickedTrainFromList != null && Viewer?.PlayerLocomotive?.Train?.ControlMode == TrainControlMode.TurnTable)
            {
                Viewer.Simulator.Confirmer.Information(Viewer.Catalog.GetString("Player train can't be switched when in turntable not aligned to a track"));
                return;
            }
            Viewer.Simulator.TrainSwitcher.SuspendOldPlayer = false;
            if (PickedTrainFromList != null && PickedTrainFromList != Viewer.SelectedTrain)
            {
                //Ask for change of viewed train
                Viewer.Simulator.TrainSwitcher.PickedTrainFromList = PickedTrainFromList;
                Viewer.Simulator.TrainSwitcher.ClickedTrainFromList = true;

            }
            if (PickedTrainFromList != null && (PickedTrainFromList == Viewer.SelectedTrain || (PickedTrainFromList.TrainType == TrainType.AiIncorporated &&
                (PickedTrainFromList as AITrain).IncorporatingTrain.IsPathless && (PickedTrainFromList as AITrain).IncorporatingTrain == Viewer.SelectedTrain)) && !PickedTrainFromList.IsActualPlayerTrain &&
                Viewer.Simulator.IsAutopilotMode && PickedTrainFromList.IsPlayable)
            {
                if (e.KeyModifiers.HasFlag(Viewer.Settings.Input.GameSuspendOldPlayerModifier))
                    Viewer.Simulator.TrainSwitcher.SuspendOldPlayer = true;
                //Ask for change of driven train
                Viewer.Simulator.TrainSwitcher.SelectedAsPlayer = PickedTrainFromList;
                Viewer.Simulator.TrainSwitcher.ClickedSelectedAsPlayer = true;
            }
            else if (PickedTrainFromList != null && PickedTrainFromList != Viewer.SelectedTrain)
            {
                //Ask for change of viewed train
                Viewer.Simulator.TrainSwitcher.PickedTrainFromList = PickedTrainFromList;
                Viewer.Simulator.TrainSwitcher.ClickedTrainFromList = true;

            }
        }
    }
}
