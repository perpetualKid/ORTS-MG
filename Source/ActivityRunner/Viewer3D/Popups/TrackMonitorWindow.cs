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

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orts.Simulation.Physics;
using Orts.Common;
using System;
using System.Collections.Generic;
using Orts.Common.Calc;
using Orts.Simulation;

namespace Orts.ActivityRunner.Viewer3D.Popups
{
    public class TrackMonitorWindow : Window
    {
        public const int MaximumDistance = 5000;
        public const int TrackMonitorLabelHeight = 130; // Height of labels above the main display.
        public const int TrackMonitorOffsetY = 25/*Window.DecorationOffset.Y*/ + TrackMonitorLabelHeight;
        private const int TrackMonitorHeightInLinesOfText = 16;
        private Label SpeedCurrent;
        private Label SpeedProjected;
        private Label SpeedAllowed;
        private Label ControlMode;
        private Label Gradient;
        private TrackMonitor Monitor;

        public TrackMonitorWindow(WindowManager owner)
            : base(owner, Window.DecorationSize.X + owner.TextFontDefault.Height * 10, Window.DecorationSize.Y + owner.TextFontDefault.Height * (5 + TrackMonitorHeightInLinesOfText) + ControlLayout.SeparatorSize * 3, Viewer.Catalog.GetString("Track Monitor"))
        {
        }

        public override void TabAction() => Monitor.CycleMode();

        protected override ControlLayout Layout(ControlLayout layout)
        {
            var vbox = base.Layout(layout).AddLayoutVertical();
            {
                var hbox = vbox.AddLayoutHorizontalLineOfText();
                hbox.Add(new Label(hbox.RemainingWidth / 2, hbox.RemainingHeight, Viewer.Catalog.GetString("Speed:")));
                hbox.Add(SpeedCurrent = new Label(hbox.RemainingWidth, hbox.RemainingHeight, "", LabelAlignment.Right));
            }
            {
                var hbox = vbox.AddLayoutHorizontalLineOfText();
                hbox.Add(new Label(hbox.RemainingWidth / 2, hbox.RemainingHeight, Viewer.Catalog.GetString("Projected:")));
                hbox.Add(SpeedProjected = new Label(hbox.RemainingWidth, hbox.RemainingHeight, "", LabelAlignment.Right));
            }
            {
                var hbox = vbox.AddLayoutHorizontalLineOfText();
                hbox.Add(new Label(hbox.RemainingWidth / 2, hbox.RemainingHeight, Viewer.Catalog.GetString("Limit:")));
                hbox.Add(SpeedAllowed = new Label(hbox.RemainingWidth, hbox.RemainingHeight, "", LabelAlignment.Right));
            }
            vbox.AddHorizontalSeparator();
            {
                var hbox = vbox.AddLayoutHorizontalLineOfText();
                hbox.Add(ControlMode = new Label(hbox.RemainingWidth - 18, hbox.RemainingHeight, "", LabelAlignment.Left));
                hbox.Add(Gradient = new Label(hbox.RemainingWidth, hbox.RemainingHeight, "", LabelAlignment.Right));

            }
            vbox.AddHorizontalSeparator();
            {
                var hbox = vbox.AddLayoutHorizontalLineOfText();
                hbox.Add(new Label(hbox.RemainingWidth, hbox.RemainingHeight, Viewer.Catalog.GetString(" Milepost   Limit     Dist")));
            }
            vbox.AddHorizontalSeparator();
            vbox.Add(Monitor = new TrackMonitor(vbox.RemainingWidth, vbox.RemainingHeight, Owner));

            return vbox;
        }

        public override void PrepareFrame(in ElapsedTime elapsedTime, bool updateFull)
        {
            base.PrepareFrame(elapsedTime, updateFull);

            // Always get train details to pass on to TrackMonitor.
            var thisInfo = Owner.Viewer.PlayerTrain.GetTrainInfo();
            Monitor.StoreInfo(thisInfo);

            // Update text fields on full update only.
            if (updateFull)
            {
                SpeedCurrent.Text = FormatStrings.FormatSpeedDisplay(Math.Abs(thisInfo.Speed), Owner.Viewer.MilepostUnitsMetric);
                SpeedProjected.Text = FormatStrings.FormatSpeedDisplay(Math.Abs(thisInfo.ProjectedSpeed), Owner.Viewer.MilepostUnitsMetric);
                SpeedAllowed.Text = FormatStrings.FormatSpeedLimit(thisInfo.AllowedSpeed, Owner.Viewer.MilepostUnitsMetric);

                var ControlText = thisInfo.ControlMode.GetLocalizedDescription();
                if (thisInfo.ControlMode == TrainControlMode.AutoNode)
                {
                    ControlText = FindAuthorityInfo(thisInfo.ObjectInfoForward, ControlText);
                }
                else if (thisInfo.ControlMode == TrainControlMode.OutOfControl)
                {
                    ControlText += thisInfo.ObjectInfoForward[0].OutOfControlReason.GetLocalizedDescription();
                }
                ControlMode.Text = ControlText;
                if (-thisInfo.Gradient < -0.00015)
                {
                    var c = '\u2198';
                    Gradient.Text = $"|  {-thisInfo.Gradient:F1}%{c} ";
                    Gradient.Color = Color.LightSkyBlue;
                }
                else if (-thisInfo.Gradient > 0.00015)
                {
                    var c = '\u2197';
                    Gradient.Text = $"|  {-thisInfo.Gradient:F1}%{c} ";
                    Gradient.Color = Color.Yellow;
                }
                else Gradient.Text = "";
            }
        }

        private static string FindAuthorityInfo(List<TrainPathItem> ObjectInfo, string ControlText)
        {
            foreach (var thisInfo in ObjectInfo)
            {
                if (thisInfo.ItemType == TrainPathItemType.Authority)
                {
                    return ControlText + " : " + thisInfo.AuthorityType.GetLocalizedDescription();
                }
            }

            return ControlText;
        }
    }

    public class TrackMonitor : Control
    {
        private static Texture2D SignalAspects;
        private static Texture2D TrackMonitorImages;
        private static Texture2D MonitorTexture;
        private WindowTextFont Font;
        private readonly Viewer viewer;
        private bool metric;
        private DisplayMode mode = DisplayMode.All;

        /// <summary>
        /// Different information views for the Track Monitor.
        /// </summary>
        public enum DisplayMode
        {
            /// <summary>
            /// Display all track and routing features.
            /// </summary>
            All,
            /// <summary>
            /// Show only the static features that a train driver would know by memory.
            /// </summary>
            StaticOnly,
        }

        public static int DbfEvalOverSpeed;//Debrief eval
        private bool istrackColorRed = false;//Debrief eval
        public static Double DbfEvalOverSpeedTimeS = 0;//Debrief eval
        public static double DbfEvalIniOverSpeedTimeS = 0;//Debrief eval

        private TrainInfo validInfo;
        private const int DesignWidth = 150; // All Width/X values are relative to this width.

        // position constants
        private readonly int additionalInfoHeight = 16; // vertical offset on window for additional out-of-range info at top and bottom
        private readonly int[] mainOffset = new int[2] { 12, 12 }; // offset for items, cell 0 is upward, 1 is downward
        private readonly int textSpacing = 10; // minimum vertical distance between two labels

        // The track is 24 wide = 6 + 2 + 8 + 2 + 6.
        private readonly int trackRail1Offset = 6;
        private readonly int trackRail2Offset = 6 + 2 + 8;
        private readonly int trackRailWidth = 2;

        // Vertical offset for text for forwards ([0]) and backwards ([1]).
        private readonly int[] textOffset = new int[2] { -11, -3 };

        // Horizontal offsets for various elements.
        private readonly int distanceTextOffset = 117;
        private readonly int trackOffset = 42;
        private readonly int speedTextOffset = 70;
        private readonly int milepostTextOffset = 0;

        // position definition arrays
        // contents :
        // cell 0 : X offset
        // cell 1 : Y offset down from top (absolute)/item location (relative)
        // cell 2 : Y offset down from bottom (absolute)/item location (relative)
        // cell 3 : X size
        // cell 4 : Y size

        private int[] eyePosition = new int[5] { 42, -4, -20, 24, 24 };
        private int[] trainPosition = new int[5] { 42, -12, -12, 24, 24 }; // Relative positioning
        private int[] otherTrainPosition = new int[5] { 42, -24, 0, 24, 24 }; // Relative positioning
        private int[] stationPosition = new int[5] { 42, 0, -24, 24, 12 }; // Relative positioning
        private int[] reversalPosition = new int[5] { 42, -21, -3, 24, 24 }; // Relative positioning
        private int[] waitingPointPosition = new int[5] { 42, -21, -3, 24, 24 }; // Relative positioning
        private int[] endAuthorityPosition = new int[5] { 42, -14, -10, 24, 24 }; // Relative positioning
        private int[] signalPosition = new int[5] { 95, -16, 0, 16, 16 }; // Relative positioning
        private int[] arrowPosition = new int[5] { 22, -12, -12, 24, 24 };
        private int[] invalidReversalPosition = new int[5] { 42, -14, -10, 24, 24 }; // Relative positioning
        private int[] leftSwitchPosition = new int[5] { 37, -14, -10, 24, 24 }; // Relative positioning
        private int[] rightSwitchPosition = new int[5] { 47, -14, -10, 24, 24 }; // Relative positioning

        // texture rectangles : X-offset, Y-offset, width, height
        private Rectangle eyeSprite = new Rectangle(0, 144, 24, 24);
        private Rectangle trainPositionAutoForwardsSprite = new Rectangle(0, 72, 24, 24);
        private Rectangle trainPositionAutoBackwardsSprite = new Rectangle(24, 72, 24, 24);
        private Rectangle trainPositionManualOnRouteSprite = new Rectangle(24, 96, 24, 24);
        private Rectangle trainPositionManualOffRouteSprite = new Rectangle(0, 96, 24, 24);
        private Rectangle endAuthoritySprite = new Rectangle(0, 0, 24, 24);
        private Rectangle oppositeTrainForwardSprite = new Rectangle(24, 120, 24, 24);
        private Rectangle oppositeTrainBackwardSprite = new Rectangle(0, 120, 24, 24);
        private Rectangle stationSprite = new Rectangle(24, 0, 24, 24);
        private Rectangle reversalSprite = new Rectangle(0, 24, 24, 24);
        private Rectangle waitingPointSprite = new Rectangle(24, 24, 24, 24);
        private Rectangle forwardArrowSprite = new Rectangle(24, 48, 24, 24);
        private Rectangle backwardArrowSprite = new Rectangle(0, 48, 24, 24);
        private Rectangle invalidReversalSprite = new Rectangle(24, 144, 24, 24);
        private Rectangle leftArrowSprite = new Rectangle(0, 168, 24, 24);
        private Rectangle rightArrowSprite = new Rectangle(24, 168, 24, 24);
        private Dictionary<TrackMonitorSignalAspect, Rectangle> SignalMarkers = new Dictionary<TrackMonitorSignalAspect, Rectangle>
        {
            { TrackMonitorSignalAspect.Clear2, new Rectangle(0, 0, 16, 16) },
            { TrackMonitorSignalAspect.Clear1, new Rectangle(16, 0, 16, 16) },
            { TrackMonitorSignalAspect.Approach3, new Rectangle(0, 16, 16, 16) },
            { TrackMonitorSignalAspect.Approach2, new Rectangle(16, 16, 16, 16) },
            { TrackMonitorSignalAspect.Approach1, new Rectangle(0, 32, 16, 16) },
            { TrackMonitorSignalAspect.Restricted, new Rectangle(16, 32, 16, 16) },
            { TrackMonitorSignalAspect.StopAndProceed, new Rectangle(0, 48, 16, 16) },
            { TrackMonitorSignalAspect.Stop, new Rectangle(16, 48, 16, 16) },
            { TrackMonitorSignalAspect.Permission, new Rectangle(0, 64, 16, 16) },
            { TrackMonitorSignalAspect.None, new Rectangle(16, 64, 16, 16) }
        };

        // fixed distance rounding values as function of maximum distance
        private Dictionary<float, float> roundingValues = new Dictionary<float, float>
        {
            { 0.0f, 0.5f },
            { 5.0f, 1.0f },
            { 10.0f, 2.0f }
        };

        public TrackMonitor(int width, int height, WindowManager owner)
            : base(0, 0, width, height)
        {
            if (SignalAspects == null)
                // TODO: This should happen on the loader thread.
                SignalAspects = SharedTextureManager.Get(owner.Viewer.RenderProcess.GraphicsDevice, System.IO.Path.Combine(owner.Viewer.ContentPath, "SignalAspects.png"));
            if (TrackMonitorImages == null)
                // TODO: This should happen on the loader thread.
                TrackMonitorImages = SharedTextureManager.Get(owner.Viewer.RenderProcess.GraphicsDevice, System.IO.Path.Combine(owner.Viewer.ContentPath, "TrackMonitorImages.png"));

            viewer = owner.Viewer;
            Font = owner.TextFontSmall;
            metric = viewer.MilepostUnitsMetric;

            ScaleDesign(ref additionalInfoHeight);
            ScaleDesign(ref mainOffset);
            ScaleDesign(ref textSpacing);

            ScaleDesign(ref trackRail1Offset);
            ScaleDesign(ref trackRail2Offset);
            ScaleDesign(ref trackRailWidth);

            ScaleDesign(ref textOffset);

            ScaleDesign(ref distanceTextOffset);
            ScaleDesign(ref trackOffset);
            ScaleDesign(ref speedTextOffset);

            ScaleDesign(ref eyePosition);
            ScaleDesign(ref trainPosition);
            ScaleDesign(ref otherTrainPosition);
            ScaleDesign(ref stationPosition);
            ScaleDesign(ref reversalPosition);
            ScaleDesign(ref waitingPointPosition);
            ScaleDesign(ref endAuthorityPosition);
            ScaleDesign(ref signalPosition);
            ScaleDesign(ref arrowPosition);
            ScaleDesign(ref leftSwitchPosition);
            ScaleDesign(ref rightSwitchPosition);
            ScaleDesign(ref invalidReversalPosition);
        }

        /// <summary>
        /// Change the Track Monitor display mode.
        /// </summary>
        public void CycleMode()
        {
            mode = mode.Next();
        }

        private void ScaleDesign(ref int variable)
        {
            variable = variable * Position.Width / DesignWidth;
        }

        private void ScaleDesign(ref int[] variable)
        {
            for (var i = 0; i < variable.Length; i++)
                ScaleDesign(ref variable[i]);
        }

        internal override void Draw(SpriteBatch spriteBatch, Point offset)
        {
            if (MonitorTexture == null)
            {
                MonitorTexture = new Texture2D(spriteBatch.GraphicsDevice, 1, 1, false, SurfaceFormat.Color);
                MonitorTexture.SetData(new[] { Color.White });
            }

            // Adjust offset to point at the control's position so we can keep code below simple.
            offset.X += Position.X;
            offset.Y += Position.Y;

            if (validInfo == null)
            {
                drawTrack(spriteBatch, offset, 0f, 1f);
                return;
            }

            drawTrack(spriteBatch, offset, validInfo.Speed, validInfo.AllowedSpeed);

            if (Orts.MultiPlayer.MPManager.IsMultiPlayer())
            {
                drawMPInfo(spriteBatch, offset);
            }
            else if (validInfo.ControlMode == TrainControlMode.AutoNode || validInfo.ControlMode == TrainControlMode.AutoSignal)
            {
                drawAutoInfo(spriteBatch, offset);
            }
            else if (validInfo.ControlMode == TrainControlMode.TurnTable) return;
            else
            {
                drawManualInfo(spriteBatch, offset);
            }
        }

        public void StoreInfo(TrainInfo thisInfo)
        {
            validInfo = thisInfo;
        }

        private void drawTrack(SpriteBatch spriteBatch, Point offset, float speedMpS, float allowedSpeedMpS)
        {
            var train = Program.Viewer.PlayerLocomotive.Train;
            var absoluteSpeedMpS = Math.Abs(speedMpS);
            var trackColor =
                absoluteSpeedMpS < allowedSpeedMpS - 1.0f ? Color.Green :
                absoluteSpeedMpS < allowedSpeedMpS + 0.0f ? Color.PaleGreen :
                absoluteSpeedMpS < allowedSpeedMpS + 5.0f ? Color.Orange : Color.Red;

            spriteBatch.Draw(MonitorTexture, new Rectangle(offset.X + trackOffset + trackRail1Offset, offset.Y, trackRailWidth, Position.Height), trackColor);
            spriteBatch.Draw(MonitorTexture, new Rectangle(offset.X + trackOffset + trackRail2Offset, offset.Y, trackRailWidth, Position.Height), trackColor);

            if (trackColor == Color.Red && !istrackColorRed)//Debrief Eval
            {
                istrackColorRed = true;
                DbfEvalIniOverSpeedTimeS = Orts.MultiPlayer.MPManager.Simulator.ClockTime;
            }            

            if (istrackColorRed && trackColor != Color.Red)//Debrief Eval
            {
                istrackColorRed = false;
                DbfEvalOverSpeed++;
            }

            if (istrackColorRed && (Orts.MultiPlayer.MPManager.Simulator.ClockTime - DbfEvalIniOverSpeedTimeS) > 1.0000)//Debrief Eval
            {
                DbfEvalOverSpeedTimeS = DbfEvalOverSpeedTimeS + (Orts.MultiPlayer.MPManager.Simulator.ClockTime - DbfEvalIniOverSpeedTimeS);
                train.DbfEvalValueChanged = true;
                DbfEvalIniOverSpeedTimeS = Orts.MultiPlayer.MPManager.Simulator.ClockTime;
            }
        }

        private void drawAutoInfo(SpriteBatch spriteBatch, Point offset)
        {
            // set area details
            var startObjectArea = additionalInfoHeight;
            var endObjectArea = Position.Height - additionalInfoHeight - trainPosition[4];
            var zeroObjectPointTop = endObjectArea;
            var zeroObjectPointMiddle = zeroObjectPointTop - trainPosition[1];
            var zeroObjectPointBottom = zeroObjectPointMiddle - trainPosition[2];
            var distanceFactor = (float)(endObjectArea - startObjectArea) / TrackMonitorWindow.MaximumDistance;

            // draw train position line
            // use red if no info for reverse move available
            var lineColor = Color.DarkGray;
            if (validInfo.ObjectInfoBackward != null && validInfo.ObjectInfoBackward.Count > 0 &&
                validInfo.ObjectInfoBackward[0].ItemType == TrainPathItemType.Authority &&
                validInfo.ObjectInfoBackward[0].AuthorityType == EndAuthorityType.NoPathReserved)
            {
                lineColor = Color.Red;
            }
            spriteBatch.Draw(MonitorTexture, new Rectangle(offset.X, offset.Y + endObjectArea, Position.Width, 1), lineColor);

            // draw direction arrow
            if (validInfo.Direction == Direction.Forward)
            {
                drawArrow(spriteBatch, offset, forwardArrowSprite, zeroObjectPointMiddle + arrowPosition[1]);
            }
            else if (validInfo.Direction == Direction.Backward)
            {
                drawArrow(spriteBatch, offset, backwardArrowSprite, zeroObjectPointMiddle + arrowPosition[2]);
            }

            // draw eye
            drawEye(spriteBatch, offset, 0, Position.Height);

            // draw fixed distance indications
            var firstMarkerDistance = drawDistanceMarkers(spriteBatch, offset, TrackMonitorWindow.MaximumDistance, distanceFactor, zeroObjectPointTop, 4, true);
            var firstLabelPosition = Convert.ToInt32(firstMarkerDistance * distanceFactor) - textSpacing;

            // draw forward items
            drawItems(spriteBatch, offset, startObjectArea, endObjectArea, zeroObjectPointTop, zeroObjectPointBottom, TrackMonitorWindow.MaximumDistance, distanceFactor, firstLabelPosition, validInfo.ObjectInfoForward, true);

            // draw own train marker
            drawOwnTrain(spriteBatch, offset, trainPositionAutoForwardsSprite, zeroObjectPointTop);
        }

        // draw Multiplayer info
        // all details accessed through class variables

        private void drawMPInfo(SpriteBatch spriteBatch, Point offset)
        {
            // set area details
            var startObjectArea = additionalInfoHeight;
            var endObjectArea = Position.Height - additionalInfoHeight;
            var zeroObjectPointTop = 0;
            var zeroObjectPointMiddle = 0;
            var zeroObjectPointBottom = 0;
            if (validInfo.Direction == Direction.Forward)
            {
                zeroObjectPointTop = endObjectArea - trainPosition[4];
                zeroObjectPointMiddle = zeroObjectPointTop - trainPosition[1];
                zeroObjectPointBottom = zeroObjectPointMiddle - trainPosition[2];
            }
            else if (validInfo.Direction == Direction.Backward)
            {
                zeroObjectPointTop = startObjectArea;
                zeroObjectPointMiddle = zeroObjectPointTop - trainPosition[1];
                zeroObjectPointBottom = zeroObjectPointMiddle - trainPosition[2];
            }
            else
            {
                zeroObjectPointMiddle = startObjectArea + (endObjectArea - startObjectArea) / 2;
                zeroObjectPointTop = zeroObjectPointMiddle + trainPosition[1];
                zeroObjectPointBottom = zeroObjectPointMiddle - trainPosition[2];
            }
            var distanceFactor = (float)(endObjectArea - startObjectArea - trainPosition[4]) / TrackMonitorWindow.MaximumDistance;
            if (validInfo.Direction == (Direction)(-1))
                distanceFactor /= 2;

            if (validInfo.Direction == Direction.Forward)
            {
                // draw direction arrow
                drawArrow(spriteBatch, offset, forwardArrowSprite, zeroObjectPointMiddle + arrowPosition[1]);
            }
            else if (validInfo.Direction == Direction.Backward)
            {
                // draw direction arrow
                drawArrow(spriteBatch, offset, backwardArrowSprite, zeroObjectPointMiddle + arrowPosition[2]);
            }

            if (validInfo.Direction != Direction.Backward)
            {
                // draw fixed distance indications
                var firstMarkerDistance = drawDistanceMarkers(spriteBatch, offset, TrackMonitorWindow.MaximumDistance, distanceFactor, zeroObjectPointTop, 4, true);
                var firstLabelPosition = Convert.ToInt32(firstMarkerDistance * distanceFactor) - textSpacing;

                // draw forward items
                drawItems(spriteBatch, offset, startObjectArea, endObjectArea, zeroObjectPointTop, zeroObjectPointBottom, TrackMonitorWindow.MaximumDistance, distanceFactor, firstLabelPosition, validInfo.ObjectInfoForward, true);
            }

            if (validInfo.Direction != 0)
            {
                // draw fixed distance indications
                var firstMarkerDistance = drawDistanceMarkers(spriteBatch, offset, TrackMonitorWindow.MaximumDistance, distanceFactor, zeroObjectPointBottom, 4, false);
                var firstLabelPosition = Convert.ToInt32(firstMarkerDistance * distanceFactor) - textSpacing;

                // draw backward items
                drawItems(spriteBatch, offset, startObjectArea, endObjectArea, zeroObjectPointBottom, zeroObjectPointTop, TrackMonitorWindow.MaximumDistance, distanceFactor, firstLabelPosition, validInfo.ObjectInfoBackward, false);
            }

            // draw own train marker
            drawOwnTrain(spriteBatch, offset, validInfo.Direction == (Direction)(-1) ? trainPositionManualOnRouteSprite : validInfo.Direction == Direction.Forward ? trainPositionAutoForwardsSprite : trainPositionAutoBackwardsSprite, zeroObjectPointTop);
        }

        // draw manual info
        // all details accessed through class variables

        private void drawManualInfo(SpriteBatch spriteBatch, Point offset)
        {
            // set area details
            var startObjectArea = additionalInfoHeight;
            var endObjectArea = Position.Height - additionalInfoHeight;
            var zeroObjectPointMiddle = startObjectArea + (endObjectArea - startObjectArea) / 2;
            var zeroObjectPointTop = zeroObjectPointMiddle + trainPosition[1];
            var zeroObjectPointBottom = zeroObjectPointMiddle - trainPosition[2];
            var distanceFactor = (float)(zeroObjectPointTop - startObjectArea) / TrackMonitorWindow.MaximumDistance;

            // draw lines through own train
            spriteBatch.Draw(MonitorTexture, new Rectangle(offset.X, offset.Y + zeroObjectPointTop, Position.Width, 1), Color.DarkGray);
            spriteBatch.Draw(MonitorTexture, new Rectangle(offset.X, offset.Y + zeroObjectPointBottom - 1, Position.Width, 1), Color.DarkGray);

            // draw direction arrow
            if (validInfo.Direction == Direction.Forward)
            {
                drawArrow(spriteBatch, offset, forwardArrowSprite, zeroObjectPointMiddle + arrowPosition[1]);
            }
            else if (validInfo.Direction == Direction.Backward)
            {
                drawArrow(spriteBatch, offset, backwardArrowSprite, zeroObjectPointMiddle + arrowPosition[2]);
            }

            // draw eye
            drawEye(spriteBatch, offset, 0, Position.Height);

            // draw fixed distance indications
            var firstMarkerDistance = drawDistanceMarkers(spriteBatch, offset, TrackMonitorWindow.MaximumDistance, distanceFactor, zeroObjectPointTop, 3, true);
            drawDistanceMarkers(spriteBatch, offset, TrackMonitorWindow.MaximumDistance, distanceFactor, zeroObjectPointBottom, 3, false);  // no return required
            var firstLabelPosition = Convert.ToInt32(firstMarkerDistance * distanceFactor) - textSpacing;

            // draw forward items
            drawItems(spriteBatch, offset, startObjectArea, endObjectArea, zeroObjectPointTop, zeroObjectPointBottom, TrackMonitorWindow.MaximumDistance, distanceFactor, firstLabelPosition, validInfo.ObjectInfoForward, true);

            // draw backward items
            drawItems(spriteBatch, offset, startObjectArea, endObjectArea, zeroObjectPointBottom, zeroObjectPointTop, TrackMonitorWindow.MaximumDistance, distanceFactor, firstLabelPosition, validInfo.ObjectInfoBackward, false);

            // draw own train marker
            var ownTrainSprite = validInfo.PathDefined ? trainPositionManualOnRouteSprite : trainPositionManualOffRouteSprite;
            drawOwnTrain(spriteBatch, offset, ownTrainSprite, zeroObjectPointTop);
        }

        // draw own train marker at required position
        private void drawOwnTrain(SpriteBatch spriteBatch, Point offset, Rectangle sprite, int position)
        {
            spriteBatch.Draw(TrackMonitorImages, new Rectangle(offset.X + trainPosition[0], offset.Y + position, trainPosition[3], trainPosition[4]), sprite, Color.White);
        }

        // draw arrow at required position
        private void drawArrow(SpriteBatch spriteBatch, Point offset, Rectangle sprite, int position)
        {
            spriteBatch.Draw(TrackMonitorImages, new Rectangle(offset.X + arrowPosition[0], offset.Y + position, arrowPosition[3], arrowPosition[4]), sprite, Color.White);
        }

        // draw eye at required position
        private void drawEye(SpriteBatch spriteBatch, Point offset, int forwardsY, int backwardsY)
        {
            // draw eye
            if (validInfo.CabOrientation == Direction.Forward)
            {
                spriteBatch.Draw(TrackMonitorImages, new Rectangle(offset.X + eyePosition[0], offset.Y + forwardsY + eyePosition[1], eyePosition[3], eyePosition[4]), eyeSprite, Color.White);
            }
            else
            {
                spriteBatch.Draw(TrackMonitorImages, new Rectangle(offset.X + eyePosition[0], offset.Y + backwardsY + eyePosition[2], eyePosition[3], eyePosition[4]), eyeSprite, Color.White);
            }
        }

        // draw fixed distance markers
        private float drawDistanceMarkers(SpriteBatch spriteBatch, Point offset, float maxDistance, float distanceFactor, int zeroPoint, int numberOfMarkers, bool forward)
        {
            var maxDistanceD = Size.Length.FromM(maxDistance, metric); // in displayed units
            var markerIntervalD = maxDistanceD / numberOfMarkers;

            var roundingValue = roundingValues[0];
            foreach (var thisValue in roundingValues)
            {
                if (markerIntervalD > thisValue.Key)
                {
                    roundingValue = thisValue.Value;
                }
            }

            markerIntervalD = Convert.ToInt32(markerIntervalD / roundingValue) * roundingValue;
            var markerIntervalM = Size.Length.ToM(markerIntervalD, metric);  // from display back to metre

            for (var ipos = 1; ipos <= numberOfMarkers; ipos++)
            {
                var actDistanceM = markerIntervalM * ipos;
                if (actDistanceM < maxDistance)
                {
                    var itemOffset = Convert.ToInt32(actDistanceM * distanceFactor);
                    var itemLocation = forward ? zeroPoint - itemOffset : zeroPoint + itemOffset;
                    var distanceString = FormatStrings.FormatDistanceDisplay(actDistanceM, metric);
                    Font.Draw(spriteBatch, new Point(offset.X + distanceTextOffset, offset.Y + itemLocation + textOffset[forward ? 0 : 1]), distanceString, Color.White);
                }
            }

            return (float)markerIntervalM;
        }

        // draw signal, speed and authority items
        // items are sorted in order of increasing distance

        private void drawItems(SpriteBatch spriteBatch, Point offset, int startObjectArea, int endObjectArea, int zeroPoint, int lastLabelPosition, float maxDistance, float distanceFactor, int firstLabelPosition, List<TrainPathItem> itemList, bool forward)
        {
            var signalShown = false;
            var firstLabelShown = false;
            var borderSignalShown = false;

            foreach (var thisItem in itemList)
            {
                switch (thisItem.ItemType)
                {
                    case TrainPathItemType.Authority:
                        drawAuthority(spriteBatch, offset, startObjectArea, endObjectArea, zeroPoint, maxDistance, distanceFactor, firstLabelPosition, forward, lastLabelPosition, thisItem, ref firstLabelShown);
                        break;

                    case TrainPathItemType.Signal:
                        lastLabelPosition = drawSignalForward(spriteBatch, offset, startObjectArea, endObjectArea, zeroPoint, maxDistance, distanceFactor, firstLabelPosition, forward, lastLabelPosition, thisItem, ref signalShown, ref borderSignalShown, ref firstLabelShown);
                        break;

                    case TrainPathItemType.Speedpost:
                        lastLabelPosition = drawSpeedpost(spriteBatch, offset, startObjectArea, endObjectArea, zeroPoint, maxDistance, distanceFactor, firstLabelPosition, forward, lastLabelPosition, thisItem, ref firstLabelShown);
                        break;

                    case TrainPathItemType.Station:
                        drawStation(spriteBatch, offset, startObjectArea, endObjectArea, zeroPoint, maxDistance, distanceFactor, firstLabelPosition, forward, lastLabelPosition, thisItem);
                        break;

                    case TrainPathItemType.WaitingPoint:
                        drawWaitingPoint(spriteBatch, offset, startObjectArea, endObjectArea, zeroPoint, maxDistance, distanceFactor, firstLabelPosition, forward, lastLabelPosition, thisItem, ref firstLabelShown);
                        break;

                    case TrainPathItemType.Milepost:
                        lastLabelPosition = drawMilePost(spriteBatch, offset, startObjectArea, endObjectArea, zeroPoint, maxDistance, distanceFactor, firstLabelPosition, forward, lastLabelPosition, thisItem, ref firstLabelShown);
                        break;

                    case TrainPathItemType.FacingSwitch:
                        drawSwitch(spriteBatch, offset, startObjectArea, endObjectArea, zeroPoint, maxDistance, distanceFactor, firstLabelPosition, forward, lastLabelPosition, thisItem, ref firstLabelShown);
                        break;

                    case TrainPathItemType.Reversal:
                        drawReversal(spriteBatch, offset, startObjectArea, endObjectArea, zeroPoint, maxDistance, distanceFactor, firstLabelPosition, forward, lastLabelPosition, thisItem, ref firstLabelShown);
                        break;

                    default:     // capture unkown item
                        break;
                }
            }
            //drawReversal and drawSwitch icons on top.
            foreach (var thisItem in itemList)
            {
                switch (thisItem.ItemType)
                {
                    case TrainPathItemType.FacingSwitch:
                        drawSwitch(spriteBatch, offset, startObjectArea, endObjectArea, zeroPoint, maxDistance, distanceFactor, firstLabelPosition, forward, lastLabelPosition, thisItem, ref firstLabelShown);
                        break;

                    case TrainPathItemType.Reversal:
                        drawReversal(spriteBatch, offset, startObjectArea, endObjectArea, zeroPoint, maxDistance, distanceFactor, firstLabelPosition, forward, lastLabelPosition, thisItem, ref firstLabelShown);
                        break;

                    default:
                        break;
                }
            }
            // reverse display of signals to have correct superposition
            for (int iItems = itemList.Count-1 ; iItems >=0; iItems--)
            {
                var thisItem = itemList[iItems];
                switch (thisItem.ItemType)
                {
                    case TrainPathItemType.Signal:
                        drawSignalBackward(spriteBatch, offset, startObjectArea, endObjectArea, zeroPoint, maxDistance, distanceFactor, forward, thisItem, signalShown);
                        break;

                    default:
                        break;
                }
            }
        }

        // draw authority information
        private void drawAuthority(SpriteBatch spriteBatch, Point offset, int startObjectArea, int endObjectArea, int zeroPoint, float maxDistance, float distanceFactor, int firstLabelPosition, bool forward, int lastLabelPosition, TrainPathItem thisItem, ref bool firstLabelShown)
        {
            var displayItem = new Rectangle(0, 0, 0, 0);
            var displayRequired = false;
            var offsetArray = new int[0];

            if (thisItem.AuthorityType == EndAuthorityType.EndOfAuthority ||
                thisItem.AuthorityType == EndAuthorityType.EndOfPath ||
                thisItem.AuthorityType == EndAuthorityType.EndOfTrack||
                thisItem.AuthorityType == EndAuthorityType.ReservedSwitch ||
                thisItem.AuthorityType == EndAuthorityType.Loop)
            {
                displayItem = endAuthoritySprite;
                offsetArray = endAuthorityPosition;
                displayRequired = true;
            }
            else if (thisItem.AuthorityType == EndAuthorityType.TrainAhead)
            {
                displayItem = forward ? oppositeTrainForwardSprite : oppositeTrainBackwardSprite;
                offsetArray = otherTrainPosition;
                displayRequired = true;
            }

            if (thisItem.DistanceToTrainM < (maxDistance - textSpacing / distanceFactor) && displayRequired)
            {
                var itemOffset = Convert.ToInt32(thisItem.DistanceToTrainM * distanceFactor);
                var itemLocation = forward ? zeroPoint - itemOffset : zeroPoint + itemOffset;
                spriteBatch.Draw(TrackMonitorImages, new Rectangle(offset.X + offsetArray[0], offset.Y + itemLocation + offsetArray[forward ? 1 : 2], offsetArray[3], offsetArray[4]), displayItem, Color.White);

                if (itemOffset < firstLabelPosition && !firstLabelShown)
                {
                    var labelPoint = new Point(offset.X + distanceTextOffset, offset.Y + itemLocation + textOffset[forward ? 0 : 1]);
                    var distanceString = FormatStrings.FormatDistanceDisplay(thisItem.DistanceToTrainM, metric);
                    Font.Draw(spriteBatch, labelPoint, distanceString, Color.White);
                    firstLabelShown = true;
                }
            }
        }

        // check signal information for reverse display
        private int drawSignalForward(SpriteBatch spriteBatch, Point offset, int startObjectArea, int endObjectArea, int zeroPoint, float maxDistance, float distanceFactor, int firstLabelPosition, bool forward, int lastLabelPosition, TrainPathItem thisItem, ref bool signalShown, ref bool borderSignalShown, ref bool firstLabelShown)
        {
            var displayItem = SignalMarkers[thisItem.SignalState];
            var newLabelPosition = lastLabelPosition;

            var displayRequired = false;
            var itemLocation = 0;
            var itemOffset = 0;
            var maxDisplayDistance = maxDistance - (textSpacing / 2) / distanceFactor;

            if (thisItem.DistanceToTrainM < maxDisplayDistance)
            {
                itemOffset = Convert.ToInt32(thisItem.DistanceToTrainM * distanceFactor);
                itemLocation = forward ? zeroPoint - itemOffset : zeroPoint + itemOffset;
                displayRequired = true;
                signalShown = true;
            }
            else if (!borderSignalShown && !signalShown)
            {
                itemOffset = 2 * startObjectArea;
                itemLocation = forward ? startObjectArea : endObjectArea;
                displayRequired = true;
                borderSignalShown = true;
            }

            bool showSpeeds;
            switch (mode)
            {
                case DisplayMode.All:
                default:
                    showSpeeds = true;
                    break;
                case DisplayMode.StaticOnly:
                    showSpeeds = false;
                    break;
            }

            if (displayRequired)
            {
                if (showSpeeds && thisItem.SignalState != TrackMonitorSignalAspect.Stop && thisItem.AllowedSpeedMpS > 0)
                {
                    var labelPoint = new Point(offset.X + speedTextOffset, offset.Y + itemLocation + textOffset[forward ? 0 : 1]);
                    var speedString = FormatStrings.FormatSpeedLimitNoUoM(thisItem.AllowedSpeedMpS, metric);
                    Font.Draw(spriteBatch, labelPoint, speedString, Color.White);
                }

                if ((itemOffset < firstLabelPosition && !firstLabelShown) || thisItem.DistanceToTrainM > maxDisplayDistance)
                {
                    var labelPoint = new Point(offset.X + distanceTextOffset, offset.Y + itemLocation + textOffset[forward ? 0 : 1]);
                    var distanceString = FormatStrings.FormatDistanceDisplay(thisItem.DistanceToTrainM, metric);
                    Font.Draw(spriteBatch, labelPoint, distanceString, Color.White);
                    firstLabelShown = true;
                }
            }

            return newLabelPosition;
        }

        // draw signal information
        private void drawSignalBackward(SpriteBatch spriteBatch, Point offset, int startObjectArea, int endObjectArea, int zeroPoint, float maxDistance, float distanceFactor, bool forward, TrainPathItem thisItem, bool signalShown)
        {
            TrackMonitorSignalAspect aspect;
            switch (mode)
            {
                case DisplayMode.All:
                default:
                    aspect = thisItem.SignalState;
                    break;
                case DisplayMode.StaticOnly:
                    aspect = TrackMonitorSignalAspect.None;
                    break;
            }
            var displayItem = SignalMarkers[aspect];
 
            var displayRequired = false;
            var itemLocation = 0;
            var itemOffset = 0;
            var maxDisplayDistance = maxDistance - (textSpacing / 2) / distanceFactor;

            if (thisItem.DistanceToTrainM < maxDisplayDistance)
            {
                itemOffset = Convert.ToInt32(thisItem.DistanceToTrainM * distanceFactor);
                itemLocation = forward ? zeroPoint - itemOffset : zeroPoint + itemOffset;
                displayRequired = true;
            }
            else if (!signalShown)
            {
                itemOffset = 2 * startObjectArea;
                itemLocation = forward ? startObjectArea : endObjectArea;
                displayRequired = true;
            }

            if (displayRequired)
            {
                spriteBatch.Draw(SignalAspects, new Rectangle(offset.X + signalPosition[0], offset.Y + itemLocation + signalPosition[forward ? 1 : 2], signalPosition[3], signalPosition[4]), displayItem, Color.White);
            }

        }

        // draw speedpost information
        private int drawSpeedpost(SpriteBatch spriteBatch, Point offset, int startObjectArea, int endObjectArea, int zeroPoint, float maxDistance, float distanceFactor, int firstLabelPosition, bool forward, int lastLabelPosition, TrainPathItem thisItem, ref bool firstLabelShown)
        {
            var newLabelPosition = lastLabelPosition;

            if (thisItem.DistanceToTrainM < (maxDistance - textSpacing / distanceFactor))
            {
                var itemOffset = Convert.ToInt32(thisItem.DistanceToTrainM * distanceFactor);
                var itemLocation = forward ? zeroPoint - itemOffset : zeroPoint + itemOffset;
                newLabelPosition = forward ? Math.Min(itemLocation, lastLabelPosition - textSpacing) : Math.Max(itemLocation, lastLabelPosition + textSpacing);

                var allowedSpeed = thisItem.AllowedSpeedMpS;
                if (allowedSpeed > 998)
                {
                    if (!Simulator.Instance.TimetableMode)
                    {
                        allowedSpeed = (float)Simulator.Instance.TRK.Route.SpeedLimit;
                    }
                }

                var labelPoint = new Point(offset.X + speedTextOffset, offset.Y + newLabelPosition + textOffset[forward ? 0 : 1]);
                var speedString = FormatStrings.FormatSpeedLimitNoUoM(allowedSpeed, metric);
                Font.Draw(spriteBatch, labelPoint, speedString, thisItem.SpeedObjectType == SpeedItemType.Standard ? Color.White :
                    (thisItem.SpeedObjectType == SpeedItemType.TemporaryRestrictionStart ? Color.Red : Color.LightGreen));

                if (itemOffset < firstLabelPosition && !firstLabelShown)
                {
                    labelPoint = new Point(offset.X + distanceTextOffset, offset.Y + newLabelPosition + textOffset[forward ? 0 : 1]);
                    var distanceString = FormatStrings.FormatDistanceDisplay(thisItem.DistanceToTrainM, metric);
                    Font.Draw(spriteBatch, labelPoint, distanceString, Color.White);
                    firstLabelShown = true;
                }
            }

            return newLabelPosition;
        }


        // draw station stop information
        private int drawStation(SpriteBatch spriteBatch, Point offset, int startObjectArea, int endObjectArea, int zeroPoint, float maxDistance, float distanceFactor, float firstLabelDistance, bool forward, int lastLabelPosition, TrainPathItem thisItem)
        {
            var displayItem = stationSprite;
            var newLabelPosition = lastLabelPosition;

            if (thisItem.DistanceToTrainM < (maxDistance - textSpacing / distanceFactor))
            {
                var itemOffset = Convert.ToInt32(thisItem.DistanceToTrainM * distanceFactor);
                var itemLocation = forward ? zeroPoint - itemOffset : zeroPoint + itemOffset;
                var startOfPlatform = (int)Math.Max(stationPosition[4], thisItem.StationPlatformLength * distanceFactor);
                var markerPlacement = new Rectangle(offset.X + stationPosition[0], offset.Y + itemLocation + stationPosition[forward ? 1 : 2], stationPosition[3], startOfPlatform);
                spriteBatch.Draw(TrackMonitorImages, markerPlacement, displayItem, Color.White);
            }

            return newLabelPosition;
        }

        // draw reversal information
        private int drawReversal(SpriteBatch spriteBatch, Point offset, int startObjectArea, int endObjectArea, int zeroPoint, float maxDistance, float distanceFactor, float firstLabelDistance, bool forward, int lastLabelPosition, TrainPathItem thisItem, ref bool firstLabelShown)
        {
            var displayItem = thisItem.Valid ? reversalSprite : invalidReversalSprite;
            var newLabelPosition = lastLabelPosition;

            if (thisItem.DistanceToTrainM < (maxDistance - textSpacing / distanceFactor))
            {
                var itemOffset = Convert.ToInt32(thisItem.DistanceToTrainM * distanceFactor);
                var itemLocation = forward ? zeroPoint - itemOffset : zeroPoint + itemOffset;
                newLabelPosition = forward ? Math.Min(itemLocation, lastLabelPosition - textSpacing) : Math.Max(itemLocation, lastLabelPosition + textSpacing);

                // What was this offset all about? Shouldn't we draw the icons in the correct location ALL the time? -- James Ross
                // var correctingOffset = Program.Simulator.TimetableMode || !Program.Simulator.Settings.EnhancedActCompatibility ? 0 : 7;

                if (thisItem.Valid)
                {
                    var markerPlacement = new Rectangle(offset.X + reversalPosition[0], offset.Y + itemLocation + reversalPosition[forward ? 1 : 2], reversalPosition[3], reversalPosition[4]);
                    spriteBatch.Draw(TrackMonitorImages, markerPlacement, displayItem, thisItem.Enabled ? Color.LightGreen : Color.White);
                }
                else
                {
                    var markerPlacement = new Rectangle(offset.X + invalidReversalPosition[0], offset.Y + itemLocation + invalidReversalPosition[forward ? 1 : 2], invalidReversalPosition[3], invalidReversalPosition[4]);
                    spriteBatch.Draw(TrackMonitorImages, markerPlacement, displayItem, Color.White);
                }

                // Only show distance for enhanced MSTS compatibility (this is the only time the position is controlled by the author).
                if (itemOffset < firstLabelDistance && !firstLabelShown && !Simulator.Instance.TimetableMode)
                {
                    var labelPoint = new Point(offset.X + distanceTextOffset, offset.Y + newLabelPosition + textOffset[forward ? 0 : 1]);
                    var distanceString = FormatStrings.FormatDistanceDisplay(thisItem.DistanceToTrainM, metric);
                    Font.Draw(spriteBatch, labelPoint, distanceString, Color.White);
                    firstLabelShown = true;
                }
            }

            return newLabelPosition;
        }

        // draw waiting point information
        private int drawWaitingPoint(SpriteBatch spriteBatch, Point offset, int startObjectArea, int endObjectArea, int zeroPoint, float maxDistance, float distanceFactor, float firstLabelDistance, bool forward, int lastLabelPosition, TrainPathItem thisItem, ref bool firstLabelShown)
        {
            var displayItem = waitingPointSprite;
            var newLabelPosition = lastLabelPosition;

            if (thisItem.DistanceToTrainM < (maxDistance - textSpacing / distanceFactor))
            {
                var itemOffset = Convert.ToInt32(thisItem.DistanceToTrainM * distanceFactor);
                var itemLocation = forward ? zeroPoint - itemOffset : zeroPoint + itemOffset;
                newLabelPosition = forward ? Math.Min(itemLocation, lastLabelPosition - textSpacing) : Math.Max(itemLocation, lastLabelPosition + textSpacing);

                var markerPlacement = new Rectangle(offset.X + waitingPointPosition[0], offset.Y + itemLocation + waitingPointPosition[forward ? 1 : 2], waitingPointPosition[3], waitingPointPosition[4]);
                spriteBatch.Draw(TrackMonitorImages, markerPlacement, displayItem, thisItem.Enabled ? Color.Yellow : Color.Red);

                if (itemOffset < firstLabelDistance && !firstLabelShown)
                {
                    var labelPoint = new Point(offset.X + distanceTextOffset, offset.Y + newLabelPosition + textOffset[forward ? 0 : 1]);
                    var distanceString = FormatStrings.FormatDistanceDisplay(thisItem.DistanceToTrainM, metric);
                    Font.Draw(spriteBatch, labelPoint, distanceString, Color.White);
                    firstLabelShown = true;
                }
            }

            return newLabelPosition;
        }

        // draw milepost information
        private int drawMilePost(SpriteBatch spriteBatch, Point offset, int startObjectArea, int endObjectArea, int zeroPoint, float maxDistance, float distanceFactor, int firstLabelPosition, bool forward, int lastLabelPosition, TrainPathItem thisItem, ref bool firstLabelShown)
        {
            var newLabelPosition = lastLabelPosition;

            if (thisItem.DistanceToTrainM < (maxDistance - textSpacing / distanceFactor))
            {
                var itemOffset = Convert.ToInt32(thisItem.DistanceToTrainM * distanceFactor);
                var itemLocation = forward ? zeroPoint - itemOffset : zeroPoint + itemOffset;
                newLabelPosition = forward ? Math.Min(itemLocation, lastLabelPosition - textSpacing) : Math.Max(itemLocation, lastLabelPosition + textSpacing);
                var labelPoint = new Point(offset.X + milepostTextOffset, offset.Y + newLabelPosition + textOffset[forward ? 0 : 1]);
                var milepostString = $"{thisItem.Miles}";
                Font.Draw(spriteBatch, labelPoint, milepostString, Color.White);

            }

            return newLabelPosition;
        }

        // draw switch information
        private int drawSwitch(SpriteBatch spriteBatch, Point offset, int startObjectArea, int endObjectArea, int zeroPoint, float maxDistance, float distanceFactor, float firstLabelDistance, bool forward, int lastLabelPosition, TrainPathItem thisItem, ref bool firstLabelShown)
        {
            var displayItem = thisItem.SwitchDivertsRight ? rightArrowSprite : leftArrowSprite;
            var newLabelPosition = lastLabelPosition;

            bool showSwitches;
            switch (mode)
            {
                case DisplayMode.All:
                default:
                    showSwitches = true;
                    break;
                case DisplayMode.StaticOnly:
                    showSwitches = false;
                    break;
            }

            if (showSwitches && thisItem.DistanceToTrainM < (maxDistance - textSpacing / distanceFactor))
            {
                var itemOffset = Convert.ToInt32(thisItem.DistanceToTrainM * distanceFactor);
                var itemLocation = forward ? zeroPoint - itemOffset : zeroPoint + itemOffset;
                newLabelPosition = forward ? Math.Min(itemLocation, lastLabelPosition - textSpacing) : Math.Max(itemLocation, lastLabelPosition + textSpacing);

                var markerPlacement = thisItem.SwitchDivertsRight ?
                    new Rectangle(offset.X + rightSwitchPosition[0], offset.Y + itemLocation + rightSwitchPosition[forward ? 1 : 2], rightSwitchPosition[3], rightSwitchPosition[4]) :
                    new Rectangle(offset.X + leftSwitchPosition[0], offset.Y + itemLocation + leftSwitchPosition[forward ? 1 : 2], leftSwitchPosition[3], leftSwitchPosition[4]);
                spriteBatch.Draw(TrackMonitorImages, markerPlacement, displayItem, Color.White);

                // Only show distance for enhanced MSTS compatibility (this is the only time the position is controlled by the author).
                if (itemOffset < firstLabelDistance && !firstLabelShown && !Simulator.Instance.TimetableMode)
                {
                    var labelPoint = new Point(offset.X + distanceTextOffset, offset.Y + newLabelPosition + textOffset[forward ? 0 : 1]);
                    var distanceString = FormatStrings.FormatDistanceDisplay(thisItem.DistanceToTrainM, metric);
                    Font.Draw(spriteBatch, labelPoint, distanceString, Color.White);
                    firstLabelShown = true;
                }
            }

            return newLabelPosition;
        }


    }
}
