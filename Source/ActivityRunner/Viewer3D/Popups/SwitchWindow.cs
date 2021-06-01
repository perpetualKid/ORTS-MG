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
using System.Diagnostics;
using System.Linq;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Orts.Common;
using Orts.Formats.Msts.Models;
using Orts.Simulation;
using Orts.Simulation.Physics;
using Orts.Simulation.Signalling;
using Orts.Simulation.Track;

namespace Orts.ActivityRunner.Viewer3D.Popups
{
    public class SwitchWindow : Window
    {
        private const int SwitchImageSize = 32;
        private Image SwitchForwards;
        private Image SwitchBackwards;
        private Image TrainDirection;
        private Image ForwardEye;
        private Image BackwardEye;
        private static Texture2D SwitchStates;

        public SwitchWindow(WindowManager owner)
            : base(owner, Window.DecorationSize.X + (int)2.5 * SwitchImageSize, Window.DecorationSize.Y + 2 * SwitchImageSize, Viewer.Catalog.GetString("Switch"))
        {
        }

        internal protected override void Initialize()
        {
            base.Initialize();
            if (SwitchStates == null)
                // TODO: This should happen on the loader thread.
                SwitchStates = SharedTextureManager.Get(Owner.Viewer.RenderProcess.GraphicsDevice, System.IO.Path.Combine(Owner.Viewer.ContentPath, "SwitchStates.png"));
        }

        protected override ControlLayout Layout(ControlLayout layout)
        {
            var hbox = base.Layout(layout).AddLayoutHorizontal();
            {
                var vbox1 = hbox.AddLayoutVertical(SwitchImageSize);
                vbox1.Add(ForwardEye = new Image(0, 0, SwitchImageSize, SwitchImageSize / 2));
                vbox1.Add(TrainDirection = new Image(0, 0, SwitchImageSize, SwitchImageSize));
                vbox1.Add(BackwardEye = new Image(0, 0, SwitchImageSize, SwitchImageSize / 2));

                var vbox2 = hbox.AddLayoutVertical(hbox.RemainingWidth);
                vbox2.Add(SwitchForwards = new Image(0, 0, SwitchImageSize, SwitchImageSize));
                vbox2.Add(SwitchBackwards = new Image(0, 0, SwitchImageSize, SwitchImageSize));
                SwitchForwards.Texture = SwitchBackwards.Texture = SwitchStates;
                SwitchForwards.Click += new Action<Control, Point>(SwitchForwards_Click);
                SwitchBackwards.Click += new Action<Control, Point>(SwitchBackwards_Click);
                TrainDirection.Texture = ForwardEye.Texture = BackwardEye.Texture = SwitchStates;
            }
            return hbox;
        }

        private void SwitchForwards_Click(Control arg1, Point arg2)
        {
            new ToggleSwitchAheadCommand(Owner.Viewer.Log);
        }

        private void SwitchBackwards_Click(Control arg1, Point arg2)
        {
            new ToggleSwitchBehindCommand(Owner.Viewer.Log);
        }

        public override void PrepareFrame(in ElapsedTime elapsedTime, bool updateFull)
        {
            base.PrepareFrame(elapsedTime, updateFull);

            if (updateFull)
            {
                var train = Owner.Viewer.PlayerTrain;
                try
                {
                    UpdateSwitch(SwitchForwards, train, true);
                    UpdateSwitch(SwitchBackwards, train, false);
                }
                catch (Exception) { }

                UpdateDirection(TrainDirection, train);
                UpdateEye(ForwardEye, train, true);
                UpdateEye(BackwardEye, train, false);
            }
        }

        private void UpdateSwitch(Image image, Train train, bool front)
        {
            image.Source = new Rectangle(0, 0, SwitchImageSize, SwitchImageSize);

            var traveller = front ? new Traveller(train.FrontTDBTraveller) : new Traveller(train.RearTDBTraveller, Traveller.TravellerDirection.Backward);

            TrackNode SwitchPreviousNode = traveller.TN;
            TrackJunctionNode SwitchNode = null;
            while (traveller.NextSection())
            {
                if (traveller.IsJunction)
                {
                    SwitchNode = traveller.TN as TrackJunctionNode;
                    break;
                }
                SwitchPreviousNode = traveller.TN;
            }
            if (SwitchNode == null)
                return;

            Debug.Assert(SwitchPreviousNode != null);
            Debug.Assert(SwitchNode.InPins == 1);
            Debug.Assert(SwitchNode.OutPins == 2 || SwitchNode.OutPins == 3);  // allow for 3-way switch
            Debug.Assert(SwitchNode.TrackPins.Count() == 3 || SwitchNode.TrackPins.Count() == 4);  // allow for 3-way switch

            var switchPreviousNodeID = SwitchPreviousNode.Index;
            var switchBranchesAwayFromUs = SwitchNode.TrackPins[0].Link == switchPreviousNodeID;
            var switchTrackSection = Owner.Viewer.Simulator.TSectionDat.TrackShapes[SwitchNode.ShapeIndex];  // TSECTION.DAT tells us which is the main route
            var switchMainRouteIsLeft = SwitchNode.GetAngle(Owner.Viewer.Simulator.TSectionDat) > 0;  // align the switch

            image.Source.X = ((switchBranchesAwayFromUs == front ? 1 : 3) + (switchMainRouteIsLeft ? 1 : 0)) * SwitchImageSize;
            image.Source.Y = SwitchNode.SelectedRoute * SwitchImageSize;

            TrackCircuitSection switchSection = TrackCircuitSection.TrackCircuitList[SwitchNode.TrackCircuitCrossReferences[0].Index];
            if (switchSection.CircuitState.Occupied() || switchSection.CircuitState.SignalReserved >= 0 ||
                (switchSection.CircuitState.TrainReserved != null && switchSection.CircuitState.TrainReserved.Train.ControlMode != TrainControlMode.Manual))
                image.Source.Y += 2 * SwitchImageSize;
        }

        private static void UpdateDirection(Image image, Train train)
        {
            image.Source = new Rectangle(0, 0, SwitchImageSize, SwitchImageSize);
            image.Source.Y = 4 * SwitchImageSize;
            image.Source.X = train.MUDirection == MidpointDirection.Forward ? 2 * SwitchImageSize :
                (train.MUDirection == MidpointDirection.Reverse ? 1 * SwitchImageSize : 0);
        }

        private static void UpdateEye(Image image, Train train, bool front)
        {
            image.Source = new Rectangle(0, 0, SwitchImageSize, SwitchImageSize / 2);
            image.Source.Y = (int)(4.25 * SwitchImageSize);
            bool flipped = Simulator.Instance.PlayerLocomotive.Flipped ^ Simulator.Instance.PlayerLocomotive.GetCabFlipped();
            image.Source.X = (front ^ !flipped) ? 0 : 3 * SwitchImageSize;
        }

    }
}
