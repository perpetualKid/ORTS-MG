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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Xna.Framework;

using Orts.ActivityRunner.Viewer3D;
using Orts.ActivityRunner.Viewer3D.Popups;
using Orts.Common;
using Orts.Common.Input;
using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.RollingStocks.SubSystems.Brakes;

namespace Orts.Viewer3D.Popups
{
    public class TrainDrivingWindow : Window
    {
        bool DynBrakeSetup = false;
        bool ResizeWindow = false;
        bool UpdateDataEnded = false;
        double StartTime;
        int FirstColIndex = 0;//first string that does not fit
        int FirstColLenght = 0;
        int FirstColOverFlow = 0;
        int LastColLenght = 0;
        int LastColOverFlow = 0;
        int LinesCount = 0;

        public bool StandardHUD = true;// Standard text

        int WindowHeightMin = 0;
        int WindowHeightMax = 0;
        int WindowWidthMin = 0;
        int WindowWidthMax = 0;

        char expandWindow;
        string keyPressed;// display a symbol when a control key is pressed.
        string Gradient;
        public int OffSetX = 0;
        const int TextSize = 15;
        public int keyPresLenght;

        Label indicator;
        LabelMono indicatorMono;
        Label ExpandWindow;
        Label LabelFontToBold;
        public static bool MonoFont;
        public static bool FontToBold;

        public struct ListLabel
        {
            public string FirstCol { get; set; }
            public int FirstColWidth { get; set; }
            public string LastCol { get; set; }
            public int LastColWidth { get; set; }
            public string SymbolCol { get; set; }
            public bool ChangeColWidth { get; set; }
            public string keyPressed { get; set; }
        }
        List<ListLabel> ListToLabel = new List<ListLabel>();

        // Change text color
        readonly Dictionary<string, Color> ColorCode = new Dictionary<string, Color>
        {
            { "!!!", Color.OrangeRed },
            { "!!?", Color.Orange },
            { "!??", Color.White },
            { "?!?", Color.Black },
            { "???", Color.Yellow },
            { "??!", Color.Green },
            { "?!!", Color.PaleGreen },
            { "$$$", Color.LightSkyBlue},
            { "$??", Color.Cyan}
        };

        readonly Dictionary<string, string> FirstColToAbbreviated = new Dictionary<string, string>()
        {
            {Viewer.Catalog.GetString("Autopilot"), Viewer.Catalog.GetString("AUTO")},
            {Viewer.Catalog.GetString("Boiler pressure"), Viewer.Catalog.GetString("PRES")},
            {Viewer.Catalog.GetString("Boiler water glass"), Viewer.Catalog.GetString("WATR")},
            {Viewer.Catalog.GetString("Boiler water level"), Viewer.Catalog.GetString("LEVL")},
            {Viewer.Catalog.GetString("Circuit breaker"), Viewer.Catalog.GetString("CIRC")},
            {Viewer.Catalog.GetString("Cylinder cocks"), Viewer.Catalog.GetString("CCOK")},
            {Viewer.Catalog.GetString("Direction"), Viewer.Catalog.GetString("DIRC")},
            {Viewer.Catalog.GetString("Doors open"), Viewer.Catalog.GetString("DOOR")},
            {Viewer.Catalog.GetString("Dynamic brake"), Viewer.Catalog.GetString("BDYN")},
            {Viewer.Catalog.GetString("Engine brake"), Viewer.Catalog.GetString("BLOC")},
            {Viewer.Catalog.GetString("Engine"), Viewer.Catalog.GetString("ENGN")},
            {Viewer.Catalog.GetString("Fire mass"), Viewer.Catalog.GetString("FIRE")},
            {Viewer.Catalog.GetString("Fixed gear"), Viewer.Catalog.GetString("GEAR")},
            {Viewer.Catalog.GetString("Fuel levels"), Viewer.Catalog.GetString("FUEL")},
            {Viewer.Catalog.GetString("Gear"), Viewer.Catalog.GetString("GEAR")},
            {Viewer.Catalog.GetString("Gradient"), Viewer.Catalog.GetString("GRAD")},
            {Viewer.Catalog.GetString("Grate limit"), Viewer.Catalog.GetString("GRAT")},
            {Viewer.Catalog.GetString("Pantographs"), Viewer.Catalog.GetString("PANT")},
            {Viewer.Catalog.GetString("Power"), Viewer.Catalog.GetString("POWR")},
            {Viewer.Catalog.GetString("Regulator"), Viewer.Catalog.GetString("REGL")},
            {Viewer.Catalog.GetString("Replay"), Viewer.Catalog.GetString("RPLY")},
            {Viewer.Catalog.GetString("Retainers"), Viewer.Catalog.GetString("RETN")},
            {Viewer.Catalog.GetString("Reverser"), Viewer.Catalog.GetString("REVR")},
            {Viewer.Catalog.GetString("Sander"), Viewer.Catalog.GetString("SAND")},
            {Viewer.Catalog.GetString("Speed"), Viewer.Catalog.GetString("SPED")},
            {Viewer.Catalog.GetString("Steam usage"), Viewer.Catalog.GetString("STEM")},
            {Viewer.Catalog.GetString("Throttle"), Viewer.Catalog.GetString("THRO")},
            {Viewer.Catalog.GetString("Time"), Viewer.Catalog.GetString("TIME")},
            {Viewer.Catalog.GetString("Train brake"), Viewer.Catalog.GetString("BTRN")},
            {Viewer.Catalog.GetString("Wheel"), Viewer.Catalog.GetString("WHEL")},    
        };

        readonly Dictionary<string, string> LastColToAbbreviated = new Dictionary<string, string>()
        {
            { Viewer.Catalog.GetString("apply Service"), Viewer.Catalog.GetString("Apply")},
            {Viewer.Catalog.GetString("Apply Quick"), Viewer.Catalog.GetString("ApplQ")},
            {Viewer.Catalog.GetString("Apply Slow"), Viewer.Catalog.GetString("ApplS")},
            {Viewer.Catalog.GetString("coal"), Viewer.Catalog.GetString("c")},
            {Viewer.Catalog.GetString("Emergency Braking Push Button"), Viewer.Catalog.GetString("EmerBPB")},
            {Viewer.Catalog.GetString("Lap Self"), Viewer.Catalog.GetString("LapS")},
            {Viewer.Catalog.GetString("Minimum Reduction"), Viewer.Catalog.GetString("MRedc")},
            {Viewer.Catalog.GetString("safe range"), Viewer.Catalog.GetString("safe")},
            {Viewer.Catalog.GetString("skid"), Viewer.Catalog.GetString("Skid")},
            {Viewer.Catalog.GetString("slip warning"), Viewer.Catalog.GetString("Warning")},
            {Viewer.Catalog.GetString("slip"), Viewer.Catalog.GetString("Slip")},
            {Viewer.Catalog.GetString("water"), Viewer.Catalog.GetString("w")},
        };

        public TrainDrivingWindow(WindowManager owner)
            : base(owner, Window.DecorationSize.X + owner.TextFontDefault.Height * 10, Window.DecorationSize.Y + owner.TextFontDefault.Height * 10, Viewer.Catalog.GetString("Train Driving Info"))
        {
            WindowHeightMin = Location.Height;
            WindowHeightMax = Location.Height + owner.TextFontDefault.Height * 20;
            WindowWidthMin = Location.Width;
            WindowWidthMax = Location.Width + owner.TextFontDefault.Height * 20;
        }

        protected internal override void Save(BinaryWriter outf)
        {
            base.Save(outf);
            outf.Write(StandardHUD);
            outf.Write(Location.X);
            outf.Write(Location.Y);
            outf.Write(Location.Width);
            outf.Write(Location.Height);
        }

        protected internal override void Restore(BinaryReader inf)
        {
            base.Restore(inf);
            Rectangle LocationRestore;
            StandardHUD = inf.ReadBoolean();
            LocationRestore.X = inf.ReadInt32();
            LocationRestore.Y = inf.ReadInt32();
            LocationRestore.Width = inf.ReadInt32();
            LocationRestore.Height = inf.ReadInt32();

            // Display window
            SizeTo(LocationRestore.Width, LocationRestore.Height);
            MoveTo(LocationRestore.X, LocationRestore.Y);
        }

        protected internal override void Initialize()
        {
            base.Initialize();
            // Reset window size
            UpdateWindowSize();

        }

        protected override ControlLayout Layout(ControlLayout layout)
        {
            // Display main HUD data
            var vbox = base.Layout(layout).AddLayoutVertical();
            if (ListToLabel.Count > 0)
            {
                var colWidth = ListToLabel.Max(x => x.FirstColWidth) + (StandardHUD ? FontToBold ? 19 : 16 : 8);
                var TimeHboxPositionY = 0;
                foreach (var data in ListToLabel)
                {
                    if (data.FirstCol.Contains(Viewer.Catalog.GetString("NwLn")))
                    {
                        var hbox = vbox.AddLayoutHorizontalLineOfText();
                        hbox.Add(new Label(colWidth * 2, hbox.RemainingHeight, " "));
                    }
                    else if (data.FirstCol.Contains("Sprtr"))
                    {
                        vbox.AddHorizontalSeparator();
                    }
                    else
                    {
                        var hbox = vbox.AddLayoutHorizontalLineOfText();
                        var FirstCol = data.FirstCol;
                        var LastCol = data.LastCol;
                        var SymbolCol = data.SymbolCol;

                        if (ColorCode.Keys.Any(FirstCol.EndsWith) || ColorCode.Keys.Any(LastCol.EndsWith) || ColorCode.Keys.Any(data.keyPressed.EndsWith) || ColorCode.Keys.Any(data.SymbolCol.EndsWith))
                        {
                            var colorFirstColEndsWith = ColorCode.Keys.Any(FirstCol.EndsWith) ? ColorCode[FirstCol.Substring(FirstCol.Length - 3)] : Color.White;
                            var colorLastColEndsWith = ColorCode.Keys.Any(LastCol.EndsWith) ? ColorCode[LastCol.Substring(LastCol.Length - 3)] : Color.White;
                            var colorKeyPressed = ColorCode.Keys.Any(data.keyPressed.EndsWith) ? ColorCode[data.keyPressed.Substring(data.keyPressed.Length - 3)] : Color.White;
                            var colorSymbolCol = ColorCode.Keys.Any(data.SymbolCol.EndsWith) ? ColorCode[data.SymbolCol.Substring(data.SymbolCol.Length - 3)] : Color.White;

                            // Erase the color code at the string end
                            FirstCol = ColorCode.Keys.Any(FirstCol.EndsWith) ? FirstCol.Substring(0, FirstCol.Length - 3) : FirstCol;
                            LastCol = ColorCode.Keys.Any(LastCol.EndsWith) ? LastCol.Substring(0, LastCol.Length - 3) : LastCol;
                            keyPressed = ColorCode.Keys.Any(data.keyPressed.EndsWith) ? data.keyPressed.Substring(0, data.keyPressed.Length - 3) : data.keyPressed;
                            SymbolCol = ColorCode.Keys.Any(data.SymbolCol.EndsWith) ? data.SymbolCol.Substring(0, data.SymbolCol.Length - 3) : data.SymbolCol;

                            // Apply color to FirstCol
                            if (StandardHUD)
                            {   // Apply color to FirstCol
                                hbox.Add(indicator = new Label(TextSize, hbox.RemainingHeight, keyPressed, LabelAlignment.Center));
                                indicator.Color = colorKeyPressed;
                                hbox.Add(indicator = new Label(colWidth, hbox.RemainingHeight, FirstCol));
                                indicator.Color = colorFirstColEndsWith;
                            }
                            else
                            {   // Use constant width font
                                hbox.Add(indicator = new Label(TextSize, hbox.RemainingHeight, keyPressed, LabelAlignment.Center));
                                indicator.Color = colorKeyPressed;
                                hbox.Add(indicatorMono = new LabelMono(colWidth, hbox.RemainingHeight, FirstCol));
                                indicatorMono.Color = colorFirstColEndsWith;
                            }

                            if (data.keyPressed != null && data.keyPressed != "")
                            {
                                hbox.Add(indicator = new Label(-TextSize, 0, TextSize, hbox.RemainingHeight, keyPressed, LabelAlignment.Right));
                                indicator.Color = colorKeyPressed;
                            }

                            if (data.SymbolCol != null && data.SymbolCol != "")
                            {
                                hbox.Add(indicator = new Label(-(TextSize + 3), 0, TextSize, hbox.RemainingHeight, SymbolCol, LabelAlignment.Right));
                                indicator.Color = colorSymbolCol;
                            }

                            // Apply color to LastCol
                            hbox.Add(indicator = new Label(colWidth, hbox.RemainingHeight, LastCol));
                            indicator.Color = colorFirstColEndsWith == Color.White ? colorLastColEndsWith : colorFirstColEndsWith;
                        }
                        else
                        {   // blanck space
                            keyPressed = "";
                            hbox.Add(indicator = new Label(TextSize, hbox.RemainingHeight, keyPressed, LabelAlignment.Center));
                            indicator.Color = Color.White; // Default color

                            //Avoids troubles when the Main Scale (Windows DPI settings) is not set to 100%
                            if (LastCol.Contains(':')) TimeHboxPositionY = hbox.Position.Y;

                            if (StandardHUD)
                            {
                                hbox.Add(indicator = new Label(colWidth, hbox.RemainingHeight, FirstCol));
                                indicator.Color = Color.White; // Default color
                            }
                            else
                            {
                                hbox.Add(indicatorMono = new LabelMono(colWidth, hbox.RemainingHeight, FirstCol));
                                indicatorMono.Color = Color.White; // Default color
                            }

                            // Font to bold, clickable label
                            if (hbox.Position.Y == TimeHboxPositionY && LastCol.Contains(':')) // Time line.
                            {
                                hbox.Add(LabelFontToBold = new Label(Owner.TextFontDefault.MeasureString(LastCol) - (StandardHUD ? 5 : 3), hbox.RemainingHeight, LastCol));
                                LabelFontToBold.Color = Color.White;
                                LabelFontToBold.Click += new Action<Control, Point>(FontToBold_Click);
                            }
                            else
                            {
                                hbox.Add(indicator = new Label(colWidth, hbox.RemainingHeight, LastCol));
                                indicator.Color = Color.White; // Default color
                            }
                        }

                        // Clickable symbol
                        if (hbox.Position.Y == TimeHboxPositionY)
                        {
                            hbox.Add(ExpandWindow = new Label(hbox.RemainingWidth - TextSize, 0, TextSize, hbox.RemainingHeight, expandWindow.ToString(), LabelAlignment.Right));
                            ExpandWindow.Color = Color.Yellow;
                            ExpandWindow.Click += new Action<Control, Point>(ExpandWindow_Click);
                        }
                        // Separator line
                        if (data.FirstCol.Contains("Sprtr"))
                        {
                            hbox.AddHorizontalSeparator();
                        }
                    }
                }
            }// close
            return vbox;
        }

        void FontToBold_Click(Control arg1, Point arg2)
        {
            FontToBold = FontToBold ? false : true;
        }

        void ExpandWindow_Click(Control arg1, Point arg2)
        {
            StandardHUD = StandardHUD ? false : true;
            //UpdateData();
            UpdateWindowSize();
        }

        private void UpdateWindowSize()
        {
            UpdateData();
            ModifyWindowSize();
        }

        /// <summary>
        /// Modify window size
        /// </summary>
        private void ModifyWindowSize()
        {
            if (ListToLabel.Count > 0)
            {
                var textwidth = Owner.TextFontDefault.Height;
                FirstColLenght = ListToLabel.Max(x => x.FirstColWidth);
                LastColLenght = ListToLabel.Max(x => x.LastColWidth);

                var desiredHeight = FontToBold ? Owner.TextFontDefaultBold.Height * ListToLabel.Count(x => x.LastCol != null)
                    : Owner.TextFontDefault.Height * ListToLabel.Count(x => x.LastCol != null);

                var desiredWidth = FirstColLenght + LastColLenght + (StandardHUD ? FontToBold ? 43 : 41 : 31);

                var newHeight = (int)MathHelper.Clamp(desiredHeight, (StandardHUD ? WindowHeightMin : 100), WindowHeightMax);
                var newWidth = (int)MathHelper.Clamp(desiredWidth, (StandardHUD ? WindowWidthMin : 100), WindowWidthMax);

                // Move the dialog up if we're expanding it, or down if not; this keeps the center in the same place.
                var newTop = Location.Y + (Location.Height - newHeight) / 2;

                // Display window
                SizeTo(newWidth, newHeight);
                MoveTo(Location.X, newTop);
            }
        }

        /// <summary>
        /// Display info according to the full text window or the slim text window
        /// </summary>
        /// <param name="firstkeyactivated"></param>
        /// <param name="firstcol"></param>
        /// <param name="lastcol"></param>
        /// <param name="symbolcol"></param>
        /// <param name="changecolwidth"></param>
        /// <param name="lastkeyactivated"></param>
        private void InfoToLabel(string firstkeyactivated, string firstcol, string lastcol, string symbolcol, bool changecolwidth, string lastkeyactivated)
        {
            if (!UpdateDataEnded)
            {
                if (!StandardHUD)
                {
                    foreach (var code in FirstColToAbbreviated)
                    {
                        if (firstcol.Contains(code.Key))
                        {
                            firstcol = firstcol.Replace(code.Key, code.Value).TrimEnd();
                        }
                    }
                    foreach (var code in LastColToAbbreviated)
                    {
                        if (lastcol.Contains(code.Key))
                        {
                            lastcol = lastcol.Replace(code.Key, code.Value).TrimEnd();
                        }
                    }
                }

                var firstColWidth = 0;
                var lastColWidth = 0;

                if (!firstcol.Contains("Sprtr"))
                {

                    if (firstcol.Contains("?") || firstcol.Contains("!") || firstcol.Contains("$"))
                    {
                        firstColWidth = FontToBold ? Owner.TextFontDefaultBold.MeasureString(firstcol.Replace("?", "").Replace("!", "").Replace("$", "").TrimEnd())
                            : Owner.TextFontDefault.MeasureString(firstcol.Replace("?", "").Replace("!", "").Replace("$", "").TrimEnd());
                    }
                    else
                    {
                        firstColWidth = FontToBold ? Owner.TextFontDefaultBold.MeasureString(firstcol.TrimEnd())
                            : Owner.TextFontDefault.MeasureString(firstcol.TrimEnd());
                    }

                    if (lastcol.Contains("?") || lastcol.Contains("!") || lastcol.Contains("$"))
                    {
                        lastColWidth = FontToBold ? Owner.TextFontDefaultBold.MeasureString(lastcol.Replace("?", "").Replace("!", "").Replace("$", "").TrimEnd())
                            : Owner.TextFontDefault.MeasureString(lastcol.Replace("?", "").Replace("!", "").Replace("$", "").TrimEnd());
                    }
                    else
                    {
                        lastColWidth = FontToBold ? Owner.TextFontDefaultBold.MeasureString(lastcol.TrimEnd())
                            : Owner.TextFontDefault.MeasureString(lastcol.TrimEnd());
                    }

                    //Set a minimum value for LastColWidth to avoid overlap between time value and clickable symbol
                    if (ListToLabel.Count == 1)
                    {
                        lastColWidth = ListToLabel.First().LastColWidth + 15;// time value + clickable symbol
                    }
                    // Ajuste the text lenght because MeasureString was not accuracy
                    lastColWidth = lastColWidth > 180 ? lastColWidth + 10 : lastColWidth;
                }

                ListToLabel.Add(new ListLabel
                {
                    FirstCol = firstcol,
                    FirstColWidth = firstColWidth,
                    LastCol = lastcol,
                    LastColWidth = lastColWidth,
                    SymbolCol = symbolcol,
                    ChangeColWidth = changecolwidth,
                    keyPressed = keyPressed
                });
            }
            else
            {
                // Detect Autopilot is on to avoid flickering when slim window is displayed
                var AutopilotOn = Owner.Viewer.Settings.Autopilot && Owner.Viewer.PlayerLocomotive.Train.TrainType == Train.TRAINTYPE.AI_PLAYERHOSTING ? true : false;

                //ResizeWindow, when the string spans over the right boundary of the window
                var maxFirstColWidth = ListToLabel.Max(x => x.FirstColWidth);
                var maxLastColWidth = ListToLabel.Max(x => x.LastColWidth);

                if (!ResizeWindow & (FirstColOverFlow != maxFirstColWidth || (!AutopilotOn && LastColOverFlow != maxLastColWidth)))
                {
                    LastColOverFlow = maxLastColWidth;
                    FirstColOverFlow = maxFirstColWidth;
                    ResizeWindow = true;
                }
            }
        }

        private void UpdateData()
        {   //Update data
            var arrowUp = '\u25B2';  // ▲
            var smallArrowUp = '\u25B3';  // △
            var arrowDown = '\u25BC';// ▼
            var smallArrowDown = '\u25BD';// ▽
            var end = '\u2589';// block ▉
            var endLower = '\u2596';// block ▖
            var arrowToRight = '\u25BA'; // ►
            var smallDiamond = '\u25C6'; // ●

            var PlayerTrain = Owner.Viewer.PlayerLocomotive.Train;
            var BrakeStatus = Owner.Viewer.PlayerLocomotive.GetTrainBrakeStatus();
            var DynamicBrakePercent = Owner.Viewer.PlayerLocomotive.DynamicBrakePercent;
            var DynamicBrakeStatus = Owner.Viewer.PlayerLocomotive.GetDynamicBrakeStatus();
            var EngineBrakeStatus = Owner.Viewer.PlayerLocomotive.GetEngineBrakeStatus();
            var Locomotive = Owner.Viewer.PlayerLocomotive as MSTSLocomotive;
            var LocomotiveDebugStatus = Owner.Viewer.PlayerLocomotive.GetDebugStatus();
            var LocomotiveStatus = Owner.Viewer.PlayerLocomotive.GetStatus();
            var LocomotiveSteam = Owner.Viewer.PlayerLocomotive as MSTSSteamLocomotive;
            var CombinedCT = Locomotive.CombinedControlType == MSTSLocomotive.CombinedControl.ThrottleDynamic ? true : false;
            var ShowMUReverser = Math.Abs(PlayerTrain.MUReverserPercent) != 100;
            var ShowRetainers = PlayerTrain.RetainerSetting != RetainerSetting.Exhaust;
            var Stretched = PlayerTrain.Cars.Count > 1 && PlayerTrain.NPull == PlayerTrain.Cars.Count - 1;
            var Bunched = !Stretched && PlayerTrain.Cars.Count > 1 && PlayerTrain.NPush == PlayerTrain.Cars.Count - 1;
            var ThisInfo = Owner.Viewer.PlayerTrain.GetTrainInfo();
            expandWindow = '\u23FA';// ⏺ toggle window

            keyPressed = "";
            ListToLabel.Clear();
            UpdateDataEnded = false;

            if (!StandardHUD)
            {
                var newBrakeStatus = new StringBuilder(BrakeStatus);
                BrakeStatus = newBrakeStatus
                      .Replace(Viewer.Catalog.GetString("bar"), string.Empty)
                      .Replace(Viewer.Catalog.GetString("inHg"), string.Empty)
                      .Replace(Viewer.Catalog.GetString("kgf/cm²"), string.Empty)
                      .Replace(Viewer.Catalog.GetString("kPa"), string.Empty)
                      .Replace(Viewer.Catalog.GetString("psi"), string.Empty)
                      .Replace(Viewer.Catalog.GetString("lib./pal."), string.Empty)//cs locales
                      .Replace(Viewer.Catalog.GetString("pal.rtuti"), string.Empty)
                      .ToString();
            }

            //if (StandardHUD)
            //    InfoToLabel(Viewer.Catalog.GetString("Version"), VersionInfo.VersionOrBuild.ToString(), false);

            // First Block
            // Client and server may have a time difference.
            keyPressed = "";
            if (Orts.MultiPlayer.MPManager.IsClient())
                InfoToLabel(keyPressed, Viewer.Catalog.GetString("Time"), FormatStrings.FormatTime(Owner.Viewer.Simulator.ClockTime + Orts.MultiPlayer.MPManager.Instance().serverTimeDifference), "", false, keyPressed);
            else
            {
                InfoToLabel(keyPressed, Viewer.Catalog.GetString("Time"), FormatStrings.FormatTime(Owner.Viewer.Simulator.ClockTime), "", false, keyPressed);
            }
            if (Owner.Viewer.Simulator.IsReplaying)
            {
                InfoToLabel(keyPressed, Viewer.Catalog.GetString("Replay"), FormatStrings.FormatTime(Owner.Viewer.Log.ReplayEndsAt - Owner.Viewer.Simulator.ClockTime), "", false, keyPressed);
                keyPressed = "";
            }

            InfoToLabel(keyPressed, Viewer.Catalog.GetString("Speed"),
                FormatStrings.FormatSpeedDisplay(Owner.Viewer.PlayerLocomotive.SpeedMpS, Owner.Viewer.PlayerLocomotive.IsMetric) +
                (ThisInfo.speedMpS < ThisInfo.allowedSpeedMpS - 1.0f ? "!??" :        // White
                ThisInfo.speedMpS < ThisInfo.allowedSpeedMpS + 0.0f ? "?!!" :         // PaleGreen
                ThisInfo.speedMpS < ThisInfo.allowedSpeedMpS + 5.0f ? "!!?" : "!!!"), "", false, keyPressed);// Orange : Red
            keyPressed = "";

            // Gradient info
            if (StandardHUD)
            {
                if (-ThisInfo.currentElevationPercent < -0.00015)
                {
                    var c = '\u2198';
                    Gradient = String.Format("{0:F1}%{1}", -ThisInfo.currentElevationPercent, c) + "$$$";
                }
                else if (-ThisInfo.currentElevationPercent > 0.00015)
                {
                    var c = '\u2197';
                    Gradient = String.Format("{0:F1}%{1}", -ThisInfo.currentElevationPercent, c) + "???";
                }
                else Gradient = String.Format("{0:F1}%", -ThisInfo.currentElevationPercent);

                InfoToLabel(keyPressed, Viewer.Catalog.GetString("Gradient"), Gradient, "", false, keyPressed);
                keyPressed = "";
            }

            // Separator
            InfoToLabel(keyPressed, "Sprtr", "", "", false, keyPressed);
            keyPressed = "";

            // Second block
            // Direction
            if (UserInput.IsDown(UserCommand.ControlBackwards) || UserInput.IsDown(UserCommand.ControlForwards))
            {
                if ((Owner.Viewer.PlayerLocomotive.EngineType != TrainCar.EngineTypes.Steam &&
                    (Owner.Viewer.PlayerLocomotive.Direction != Direction.Forward
                    || Owner.Viewer.PlayerLocomotive.Direction != Direction.Reverse)
                    && (Owner.Viewer.PlayerLocomotive.ThrottlePercent >= 1
                    || Math.Abs(Owner.Viewer.PlayerLocomotive.SpeedMpS) > 1))
                    || (Owner.Viewer.PlayerLocomotive.EngineType == TrainCar.EngineTypes.Steam && LocomotiveSteam.CutoffController.MaximumValue == Math.Abs(PlayerTrain.MUReverserPercent / 100))
                    )
                {
                    keyPressed = end.ToString() + "???";
                }
                else
                {
                    keyPressed = (UserInput.IsDown(UserCommand.ControlBackwards) ? arrowDown.ToString() + "???" : UserInput.IsDown(UserCommand.ControlForwards) ? arrowUp.ToString() + "???" : " ");
                }
            }
            InfoToLabel(keyPressed, (Owner.Viewer.PlayerLocomotive.EngineType == TrainCar.EngineTypes.Steam ? Viewer.Catalog.GetString("Reverser") : Viewer.Catalog.GetString("Direction")),
                (ShowMUReverser ? Math.Abs(PlayerTrain.MUReverserPercent).ToString("0") + "% " : "") + FormatStrings.Catalog.GetParticularString("Reverser", Owner.Viewer.PlayerLocomotive.Direction.GetDescription()), "", false, keyPressed);
            keyPressed = "";

            // Throttle
            if (DynamicBrakePercent < 1 && ((UserInput.IsDown(UserCommand.ControlThrottleIncrease) && Locomotive.ThrottleController.MaximumValue == Owner.Viewer.PlayerLocomotive.ThrottlePercent / 100)
               || (UserInput.IsDown(UserCommand.ControlThrottleDecrease) && Owner.Viewer.PlayerLocomotive.ThrottlePercent == 0)))
            {
                keyPressed = end.ToString() + "???";
            }
            else if (DynamicBrakePercent > -1)
            {
                keyPressed = endLower.ToString() + "???";
            }
            else
            {
                keyPressed = UserInput.IsDown(UserCommand.ControlThrottleIncrease) ? arrowUp.ToString() + "???"
                    : UserInput.IsDown(UserCommand.ControlThrottleDecrease) ? arrowDown.ToString() + "???"
                    : "";
            }
            InfoToLabel(keyPressed, (Owner.Viewer.PlayerLocomotive is MSTSSteamLocomotive ? Viewer.Catalog.GetString("Regulator") : Viewer.Catalog.GetString("Throttle")), Owner.Viewer.PlayerLocomotive.ThrottlePercent.ToString("0") + "%", "", false, keyPressed);
            keyPressed = "";

            // Cylinder Cocks
            if (Owner.Viewer.PlayerLocomotive is MSTSSteamLocomotive)
            {
                keyPressed = (UserInput.IsDown(UserCommand.ControlCylinderCocks) || (Owner.Viewer.PlayerLocomotive as MSTSSteamLocomotive).CylinderCocksAreOpen) ? arrowToRight.ToString() + "???" : "";
                InfoToLabel(keyPressed, Viewer.Catalog.GetString("Cylinder cocks"), (Owner.Viewer.PlayerLocomotive as MSTSSteamLocomotive).CylinderCocksAreOpen ? Viewer.Catalog.GetString("Open") + "!!?" : Viewer.Catalog.GetString("Closed") + "!??", "", false, keyPressed);
            }

            // Sander
            keyPressed = UserInput.IsDown(UserCommand.ControlSander) || UserInput.IsDown(UserCommand.ControlSanderToggle) ? arrowDown.ToString() + "???" : " ";
            if (Owner.Viewer.PlayerLocomotive.GetSanderOn())
            {
                var sanderBlocked = Owner.Viewer.PlayerLocomotive is MSTSLocomotive && Math.Abs(PlayerTrain.SpeedMpS) > ((MSTSLocomotive)Owner.Viewer.PlayerLocomotive).SanderSpeedOfMpS;
                if (sanderBlocked)
                {
                    InfoToLabel(keyPressed, Viewer.Catalog.GetString("Sander"), Viewer.Catalog.GetString("Blocked") + "!!!", "", StandardHUD ? true : false, keyPressed);
                    keyPressed = "";
                }
                else
                {
                    keyPressed = arrowToRight.ToString() + "???";
                    InfoToLabel(keyPressed, Viewer.Catalog.GetString("Sander"), Viewer.Catalog.GetString("On") + "!!?", "", StandardHUD ? true : false, keyPressed);
                    keyPressed = "";
                }
            }
            else
            {
                keyPressed = "";
                InfoToLabel(keyPressed, Viewer.Catalog.GetString("Sander"), "Off", "", false, keyPressed);
            }

            InfoToLabel("", "Sprtr", "", "", false, keyPressed);

            // Train Brake multi-lines
            // TO DO: A better algorithm
            //var brakeStatus = Owner.Viewer.PlayerLocomotive.GetTrainBrakeStatus();
            //steam loco
            keyPressed = UserInput.IsDown(UserCommand.ControlTrainBrakeDecrease) ? arrowDown.ToString() + "???" : UserInput.IsDown(UserCommand.ControlTrainBrakeIncrease) ? arrowUp.ToString() + "???" : "";

            var brakeInfoValue = "";
            var index = 0;

            if (BrakeStatus.Contains(Viewer.Catalog.GetString("EQ")))
            {
                brakeInfoValue = BrakeStatus.Substring(0, BrakeStatus.IndexOf(Viewer.Catalog.GetString("EQ"))).TrimEnd();
                InfoToLabel(keyPressed, Viewer.Catalog.GetString("Train brake"), brakeInfoValue + "$??", "", false, keyPressed);
                keyPressed = "";
                index = BrakeStatus.IndexOf(Viewer.Catalog.GetString("EQ"));
                brakeInfoValue = BrakeStatus.Substring(index, BrakeStatus.IndexOf(Viewer.Catalog.GetString("BC")) - index).TrimEnd();

                InfoToLabel(keyPressed, "", brakeInfoValue, "", false, keyPressed);
                keyPressed = "";
                if (BrakeStatus.Contains(Viewer.Catalog.GetString("EOT")))
                {
                    var IndexOffset = Viewer.Catalog.GetString("EOT").Length + 1;
                    index = BrakeStatus.IndexOf(Viewer.Catalog.GetString("BC"));
                    brakeInfoValue = BrakeStatus.Substring(index, BrakeStatus.IndexOf(Viewer.Catalog.GetString("EOT")) - index).TrimEnd();
                    keyPressed = "";
                    InfoToLabel(keyPressed, "", brakeInfoValue, "", false, keyPressed);
                    keyPressed = "";
                    index = BrakeStatus.IndexOf(Viewer.Catalog.GetString("EOT")) + IndexOffset;
                    brakeInfoValue = BrakeStatus.Substring(index, BrakeStatus.Length - index).TrimStart();
                    keyPressed = "";
                    InfoToLabel(keyPressed, "", brakeInfoValue, "", false, keyPressed);
                    keyPressed = "";
                }
                else
                {
                    index = BrakeStatus.IndexOf(Viewer.Catalog.GetString("BC"));
                    brakeInfoValue = BrakeStatus.Substring(index, BrakeStatus.Length - index).TrimEnd();
                    keyPressed = "";
                    InfoToLabel(keyPressed, "", brakeInfoValue, "", false, keyPressed);
                    keyPressed = "";
                }
            }
            else if (BrakeStatus.Contains(Viewer.Catalog.GetString("Lead")))
            {
                var IndexOffset  = Viewer.Catalog.GetString("Lead").Length + 1;
                brakeInfoValue = BrakeStatus.Substring(0, BrakeStatus.IndexOf(Viewer.Catalog.GetString("Lead"))).TrimEnd();
                InfoToLabel(keyPressed, Viewer.Catalog.GetString("Train brake"), brakeInfoValue + "$??", "", false, keyPressed);

                keyPressed = "";
                index = BrakeStatus.IndexOf(Viewer.Catalog.GetString("Lead")) + IndexOffset;
                if (BrakeStatus.Contains(Viewer.Catalog.GetString("EOT")))
                {
                    brakeInfoValue = BrakeStatus.Substring(index, BrakeStatus.IndexOf(Viewer.Catalog.GetString("EOT")) - index).TrimEnd();
                    InfoToLabel(keyPressed, "", brakeInfoValue, "", false, keyPressed);

                    keyPressed = "";
                    index = BrakeStatus.IndexOf(Viewer.Catalog.GetString("EOT")) + IndexOffset;
                    brakeInfoValue = BrakeStatus.Substring(index, BrakeStatus.Length - index).TrimEnd();
                    InfoToLabel(keyPressed, "", brakeInfoValue, "", false, keyPressed);
                }
                else
                {
                    brakeInfoValue = BrakeStatus.Substring(index, BrakeStatus.Length - index).TrimEnd();
                    InfoToLabel(keyPressed, "", brakeInfoValue, "", false, keyPressed);
                }
            }
            else if (BrakeStatus.Contains(Viewer.Catalog.GetString("BC")))
            {
                brakeInfoValue = BrakeStatus.Substring(0, BrakeStatus.IndexOf(Viewer.Catalog.GetString("BC"))).TrimEnd();
                InfoToLabel(keyPressed, Viewer.Catalog.GetString("Train brake"), brakeInfoValue + "$??", "", false, keyPressed);

                keyPressed = "";
                index = BrakeStatus.IndexOf(Viewer.Catalog.GetString("BC"));
                brakeInfoValue = BrakeStatus.Substring(index, BrakeStatus.Length - index).TrimEnd();

                keyPressed = "";
                InfoToLabel(keyPressed, "", brakeInfoValue, "", false, keyPressed);
                keyPressed = "";
            }

            keyPressed = "";
            if (ShowRetainers)
                InfoToLabel(keyPressed, Viewer.Catalog.GetString("Retainers"), (PlayerTrain.RetainerPercent + " " + Viewer.Catalog.GetString(PlayerTrain.RetainerSetting.GetDescription())), "", false, keyPressed); keyPressed = "";
		keyPressed = "";
            if ((Owner.Viewer.PlayerLocomotive as MSTSLocomotive).EngineBrakeFitted) // ideally this test should be using "engineBrakeStatus != null", but this currently does not work, as a controller is defined by default
            {
            }
            keyPressed = UserInput.IsDown(UserCommand.ControlEngineBrakeDecrease) ? arrowDown.ToString() + "???" : UserInput.IsDown(UserCommand.ControlEngineBrakeIncrease) ? arrowUp.ToString() + "???" : "";
            if (EngineBrakeStatus.Contains(Viewer.Catalog.GetString("BC")))
            {
                InfoToLabel(keyPressed, Viewer.Catalog.GetString("Engine brake"), EngineBrakeStatus.Substring(0, EngineBrakeStatus.IndexOf("BC")) + "$??", "", false, keyPressed);
                keyPressed = "";
                index = EngineBrakeStatus.IndexOf(Viewer.Catalog.GetString("BC"));
                brakeInfoValue = EngineBrakeStatus.Substring(index, EngineBrakeStatus.Length - index).TrimEnd();
                InfoToLabel(keyPressed, Viewer.Catalog.GetString(""), brakeInfoValue + "!??", "", false, keyPressed);
            }
            else
                InfoToLabel(keyPressed, Viewer.Catalog.GetString("Engine brake"), EngineBrakeStatus + "$??", "", false, keyPressed);

            keyPressed = "";
            if (DynamicBrakeStatus != null && Locomotive.IsLeadLocomotive())
            {
                if (!DynBrakeSetup && ( UserInput.IsDown(UserCommand.ControlDynamicBrakeIncrease) && DynamicBrakePercent == 0)
                    || (CombinedCT && UserInput.IsDown(UserCommand.ControlThrottleDecrease) && Owner.Viewer.PlayerLocomotive.ThrottlePercent == 0 && DynamicBrakeStatus == "0%"))
                {
                    StartTime = Locomotive.DynamicBrakeCommandStartTime + Locomotive.DynamicBrakeDelayS;
                    DynBrakeSetup = true;
                    keyPressed = arrowToRight.ToString() + "???";
                    InfoToLabel(keyPressed, Viewer.Catalog.GetString("Dynamic brake"), Viewer.Catalog.GetString("Setup") + "$??", "", false, keyPressed);
                }
                else if (DynBrakeSetup && StartTime < Owner.Viewer.Simulator.ClockTime)
                {
                    DynBrakeSetup = false;
                    InfoToLabel(keyPressed, Viewer.Catalog.GetString("Dynamic brake"), DynamicBrakePercent + "% " + "$??", "", false, keyPressed);
                }
                else if (DynBrakeSetup && StartTime > Owner.Viewer.Simulator.ClockTime)
                {
                    keyPressed = arrowToRight.ToString() + "???";
                    InfoToLabel(keyPressed, Viewer.Catalog.GetString("Dynamic brake"), Viewer.Catalog.GetString("Setup") + "$??", "", false, keyPressed);
                }
                else if (!DynBrakeSetup && DynamicBrakePercent > -1)
                {
                    if (CombinedCT)
                    {
                        keyPressed = UserInput.IsDown(UserCommand.ControlThrottleIncrease) || UserInput.IsDown(UserCommand.ControlDynamicBrakeDecrease) ? arrowDown.ToString() + "???"
                           : UserInput.IsDown(UserCommand.ControlThrottleDecrease) || UserInput.IsDown(UserCommand.ControlDynamicBrakeIncrease) ? arrowUp.ToString() + "???"
                           : "";
                    }
                    else
                    {
                        keyPressed = UserInput.IsDown(UserCommand.ControlDynamicBrakeDecrease) ? arrowDown.ToString() + "???"
                            : UserInput.IsDown(UserCommand.ControlDynamicBrakeIncrease) ? arrowUp.ToString() + "???"
                            : "";
                    }
                    InfoToLabel(keyPressed, Viewer.Catalog.GetString("Dynamic brake"), DynamicBrakeStatus + "$??", "", false, keyPressed);
                }
                else if (DynamicBrakeStatus == "" && DynamicBrakePercent < 0)
                {
                    InfoToLabel(keyPressed, Viewer.Catalog.GetString("Dynamic brake"), Viewer.Catalog.GetString("Off"), "", false, keyPressed);
                }
            }
            keyPressed = "";
            InfoToLabel(keyPressed, "Sprtr", "", "", false, keyPressed);

            if (LocomotiveStatus != null)
            {
                var lines = LocomotiveStatus.Split('\n');
                foreach (var data in lines)
                {
                    if (data.Length > 0)
                    {
                        var parts = data.Split(new[] { " = " }, 2, StringSplitOptions.None);
                        var HeatColor = "!??"; // Color.White
                        keyPressed = "";
                        if (!StandardHUD && Viewer.Catalog.GetString(parts[0]).StartsWith(Viewer.Catalog.GetString("Steam usage")))
                        {
                        }
                        else if (Viewer.Catalog.GetString(parts[0]).StartsWith(Viewer.Catalog.GetString("Boiler pressure")))
                        {
                            MSTSSteamLocomotive steamloco = (MSTSSteamLocomotive)Owner.Viewer.PlayerLocomotive;
                            var bandUpper = steamloco.PreviousBoilerHeatOutBTUpS * 1.025f; // find upper bandwidth point
                            var bandLower = steamloco.PreviousBoilerHeatOutBTUpS * 0.975f; // find lower bandwidth point - gives a total 5% bandwidth

                            if (steamloco.BoilerHeatInBTUpS > bandLower && steamloco.BoilerHeatInBTUpS < bandUpper) HeatColor = smallDiamond.ToString() + "!??";
                            else if (steamloco.BoilerHeatInBTUpS < bandLower) HeatColor = smallArrowDown.ToString() + "$??"; // Color.Cyan
                            else if (steamloco.BoilerHeatInBTUpS > bandUpper) HeatColor = smallArrowUp.ToString() + "!!?"; // Color.Orange

                            keyPressed = "";
                            InfoToLabel(keyPressed, Viewer.Catalog.GetString("Boiler pressure"), Viewer.Catalog.GetString(parts[1]), HeatColor, false, keyPressed);
                        }
                        else if (!StandardHUD && Viewer.Catalog.GetString(parts[0]).StartsWith(Viewer.Catalog.GetString("Fuel levels")))
                        {
                            keyPressed = "";
                            InfoToLabel(keyPressed, parts[0].EndsWith("?") || parts[0].EndsWith("!") ? Viewer.Catalog.GetString(parts[0].Substring(0, parts[0].Length - 3)) : Viewer.Catalog.GetString(parts[0]), (parts.Length > 1 ? Viewer.Catalog.GetString(parts[1].Replace(" ", string.Empty)) : ""), "", false, keyPressed);
                        }
                        else if (parts[0].StartsWith(Viewer.Catalog.GetString("Gear")))
                        {
                            keyPressed = UserInput.IsDown(UserCommand.ControlGearDown) ? arrowDown.ToString() + "???" : UserInput.IsDown(UserCommand.ControlGearUp) ? arrowUp.ToString() + "???" : "";
                            InfoToLabel(keyPressed, Viewer.Catalog.GetString(parts[0]), (parts.Length > 1 ? Viewer.Catalog.GetString(parts[1]) : ""), "", false, keyPressed);
                            keyPressed = "";
                        }
                        else if (parts.Contains(Viewer.Catalog.GetString("Pantographs")))
                        {
                            keyPressed = UserInput.IsDown(UserCommand.ControlPantograph1) ? parts[1].StartsWith(Viewer.Catalog.GetString("Up")) ? arrowUp.ToString() + "???" : arrowDown.ToString() + "???" : "";
                            InfoToLabel(keyPressed, Viewer.Catalog.GetString(parts[0]), (parts.Length > 1 ? Viewer.Catalog.GetString(parts[1]) : ""), "", false, keyPressed);
                            keyPressed = "";
                        }
                        else if (parts.Contains(Viewer.Catalog.GetString("Engine")))
                        {
                            keyPressed = "";
                            InfoToLabel(keyPressed, Viewer.Catalog.GetString(parts[0]), (parts.Length > 1 ? Viewer.Catalog.GetString(parts[1]) + "!??" : ""), "", false, keyPressed);
                            keyPressed = "";
                        }
                        else
                        {
                            InfoToLabel("", parts[0].EndsWith("?") || parts[0].EndsWith("!") ? Viewer.Catalog.GetString( parts[0].Substring(0, parts[0].Length - 3)) : Viewer.Catalog.GetString(parts[0]), (parts.Length > 1 ? Viewer.Catalog.GetString(parts[1]) : ""), "", false, keyPressed);
                        }
                    }
                }
            }

            keyPressed = "";
            InfoToLabel(keyPressed, "Sprtr", "", "", true, keyPressed);

            keyPressed = "";
            if (StandardHUD)
                InfoToLabel(keyPressed, Viewer.Catalog.GetString("FPS"), Owner.Viewer.RenderProcess.FrameRate.SmoothedValue.ToString("F0"), "", false, keyPressed);

            // Messages
			// Autopilot
            keyPressed = "";
            if (Owner.Viewer.Settings.Autopilot)
            {
                if (Owner.Viewer.PlayerLocomotive.Train.TrainType == Train.TRAINTYPE.AI_PLAYERHOSTING)
                {
                    keyPressed = UserInput.IsDown(UserCommand.GameAutopilotMode) ? arrowUp.ToString() + "???" : "";
                    InfoToLabel(keyPressed, Viewer.Catalog.GetString("Autopilot"), Viewer.Catalog.GetString("On") + "???", "", false, keyPressed);
                }
                else if (Owner.Viewer.PlayerLocomotive.Train.TrainType != Train.TRAINTYPE.AI_PLAYERHOSTING)
                {
                    keyPressed = UserInput.IsDown(UserCommand.GameAutopilotMode) ? arrowDown.ToString() + "???" : "";
                    InfoToLabel(keyPressed, Viewer.Catalog.GetString("Autopilot"), Viewer.Catalog.GetString("Off"), "", false, keyPressed);
                }
                else
                    InfoToLabel("", Viewer.Catalog.GetString("Autopilot"), Viewer.Catalog.GetString("Off"), "", false, keyPressed);
            }
            else
                InfoToLabel("", Viewer.Catalog.GetString("Autopilot" + "?!?"), Viewer.Catalog.GetString("Off"), "", false, keyPressed);

            // Grate limit
            keyPressed = "";
            if (Owner.Viewer.PlayerLocomotive.GetType() == typeof(MSTSSteamLocomotive))
            {
                MSTSSteamLocomotive steamloco = (MSTSSteamLocomotive)Owner.Viewer.PlayerLocomotive;
                if (steamloco.GrateCombustionRateLBpFt2 > steamloco.GrateLimitLBpFt2)
                {
                    if (steamloco.IsGrateLimit)
                        InfoToLabel("", Viewer.Catalog.GetString("Grate limit"), Viewer.Catalog.GetString("Exceeded") + "!!!", "", false, keyPressed);
                }
                else
                    InfoToLabel("", Viewer.Catalog.GetString("Grate limit") + "?!?", Viewer.Catalog.GetString("Normal") + "?!?", "", false, keyPressed);
            }
            else
                InfoToLabel("", Viewer.Catalog.GetString("Grate limit") + "?!?", Viewer.Catalog.GetString("-") + "?!?", "", false, keyPressed);

			// Wheel
            keyPressed = "";
            if (Owner.Viewer.PlayerTrain.IsWheelSlip)
                InfoToLabel("", Viewer.Catalog.GetString("Wheel"), Viewer.Catalog.GetString("slip") + "!!!", "", false, keyPressed);
            else if (Owner.Viewer.PlayerTrain.IsWheelSlipWarninq)
                InfoToLabel("", Viewer.Catalog.GetString("Wheel"), Viewer.Catalog.GetString("slip warning") + "???", "", false, keyPressed);
            else if (Owner.Viewer.PlayerTrain.IsBrakeSkid)
                InfoToLabel("", Viewer.Catalog.GetString("Wheel"), Viewer.Catalog.GetString("skid") + "!!!", "", false, keyPressed);
            else
                InfoToLabel("", Viewer.Catalog.GetString("Wheel") + "?!?", Viewer.Catalog.GetString("Normal") + "?!?", "", false, keyPressed);

            // Doors
            keyPressed = "";
            if ((Owner.Viewer.PlayerLocomotive as MSTSWagon).DoorLeftOpen || (Owner.Viewer.PlayerLocomotive as MSTSWagon).DoorRightOpen)
            {
                var color = Math.Abs(Owner.Viewer.PlayerLocomotive.SpeedMpS) > 0.1f ? "!!!" : "???";
                var status = "";
                if ((Owner.Viewer.PlayerLocomotive as MSTSWagon).DoorLeftOpen)
                    status += Viewer.Catalog.GetString((Owner.Viewer.PlayerLocomotive as MSTSLocomotive).GetCabFlipped() ? Viewer.Catalog.GetString("Right") : Viewer.Catalog.GetString("Left"));
                if ((Owner.Viewer.PlayerLocomotive as MSTSWagon).DoorRightOpen)
                    status += string.Format(status == "" ? "{0}" : " {0}", Viewer.Catalog.GetString((Owner.Viewer.PlayerLocomotive as MSTSLocomotive).GetCabFlipped() ? Viewer.Catalog.GetString("Left") : Viewer.Catalog.GetString("Right")));
                status += color;

                InfoToLabel(" ", Viewer.Catalog.GetString("Doors open"), status, "", false, keyPressed);
            }
            else
                InfoToLabel(" ", Viewer.Catalog.GetString("Doors open") + "?!?", Viewer.Catalog.GetString("Closed"), "", false, keyPressed);

            // Ctrl + F Firing to manual
            if (UserInput.IsDown(UserCommand.ControlFiring))
            {
                ResizeWindow = true;
            }

            UpdateDataEnded = true;
            keyPressed = "";
            InfoToLabel(keyPressed, "", "", "", true, keyPressed);
        }

        public override void PrepareFrame(in ElapsedTime elapsedTime, bool updateFull)
        {
            base.PrepareFrame(elapsedTime, updateFull);

            var MovingCurrentWindow = UserInput.IsMouseLeftButtonDown &&
                   UserInput.MouseX >= Location.X && UserInput.MouseX <= Location.X + Location.Width &&
                   UserInput.MouseY >= Location.Y && UserInput.MouseY <= Location.Y + Location.Height ?
                   true : false;

            // Avoid to updateFull when the window is moving
            if (!MovingCurrentWindow & updateFull)
            {
                UpdateData();

                // Ctrl + F (FiringIsManual)
               if (ResizeWindow || LinesCount != ListToLabel.Count())
               {
                    ResizeWindow = false;
                    UpdateWindowSize();
                    LinesCount = ListToLabel.Count();
               }

               //Update Layout
               Layout();
            }
        }
    }
}