using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Orts.Common;
using Orts.Common.Input;

namespace Orts.Settings.Util
{
    /// <summary>
    /// Extension class for Input settings to dump keyboard mappings to screen or file
    /// </summary>
    public static class KeyboardMap
    {
        // Keyboard scancodes are basically constant; some keyboards have extra buttons (e.g. UK ones tend to have an
        // extra button next to Left Shift) or move one or two around (e.g. UK ones tend to move 0x2B down one row)
        // but generally this layout is right. Numeric keypad omitted as most keys are just duplicates of the main
        // keys (in two sets, based on Num Lock) and we don't use them. Scancodes are in hex.
        //
        // Break/Pause (0x11D) is handled specially and doesn't use the expect 0x45 scancode.
        //
        public static readonly string[] KeyboardLayout = new[] {
            "[01 ]   [3B ][3C ][3D ][3E ]   [3F ][40 ][41 ][42 ]   [43 ][44 ][57 ][58 ]   [37 ][46 ][11D]",
            "                                                                                            ",
            "[29 ][02 ][03 ][04 ][05 ][06 ][07 ][08 ][09 ][0A ][0B ][0C ][0D ][0E     ]   [52 ][47 ][49 ]",
            "[0F   ][10 ][11 ][12 ][13 ][14 ][15 ][16 ][17 ][18 ][19 ][1A ][1B ][2B   ]   [53 ][4F ][51 ]",
            "[3A     ][1E ][1F ][20 ][21 ][22 ][23 ][24 ][25 ][26 ][27 ][28 ][1C      ]                  ",
            "[2A       ][2C ][2D ][2E ][2F ][30 ][31 ][32 ][33 ][34 ][35 ][36         ]        [48 ]     ",
            "[1D   ][    ][38  ][39                          ][    ][    ][    ][1D   ]   [4B ][50 ][4D ]",
        };

        public static int MapWidth => KeyboardLayout[0].Length;

        public static void DrawKeyboardMap(Action<Rectangle> drawRow, Action<Rectangle, int, string> drawKey)
        {
            for (var y = 0; y < KeyboardLayout.Length; y++)
            {
                var keyboardLine = KeyboardLayout[y];
                drawRow?.Invoke(new Rectangle(0, y, keyboardLine.Length, 1));

                var x = keyboardLine.IndexOf('[');
                while (x != -1)
                {
                    var x2 = keyboardLine.IndexOf(']', x);

                    var scanCodeString = keyboardLine.Substring(x + 1, 3).Trim();
                    var keyScanCode = scanCodeString.Length > 0 ? int.Parse(scanCodeString, NumberStyles.HexNumber) : 0;

                    var keyName = ScanCodeKeyUtils.GetScanCodeKeyName(keyScanCode);
                    // Only allow F-keys to show >1 character names. The rest we'll remove for now.
                    if ((keyName.Length > 1) && !new[] { 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x57, 0x58 }.Contains(keyScanCode))
                        keyName = "";

                    drawKey?.Invoke(new Rectangle(x, y, x2 - x + 1, 1), keyScanCode, keyName);

                    x = keyboardLine.IndexOf('[', x2);
                }
            }
        }

        public static Color GetScanCodeColor(this InputSettings input, int scanCode)
        {
            // These should be placed in order of priority - the first found match is used.
            var prefixesToColors = new List<KeyValuePair<string, Color>>()
            {
                new KeyValuePair<string, Color>("ControlReverser", Color.DarkGreen),
                new KeyValuePair<string, Color>("ControlThrottle", Color.DarkGreen),
                new KeyValuePair<string, Color>("ControlTrainBrake", Color.DarkRed),
                new KeyValuePair<string, Color>("ControlEngineBrake", Color.DarkRed),
                new KeyValuePair<string, Color>("ControlDynamicBrake", Color.DarkRed),
                new KeyValuePair<string, Color>("ControlBrakeHose", Color.DarkRed),
                new KeyValuePair<string, Color>("ControlEmergency", Color.DarkRed),
                new KeyValuePair<string, Color>("ControlBailOff", Color.DarkRed),
                new KeyValuePair<string, Color>("ControlInitializeBrakes", Color.DarkRed),
                new KeyValuePair<string, Color>("Control", Color.DarkBlue),
                new KeyValuePair<string, Color>("Camera", Color.Orange),
                new KeyValuePair<string, Color>("Display", Color.DarkGoldenrod),
                //new KeyValuePair<string, Color>("Game", Color.Blue),
                new KeyValuePair<string, Color>("", Color.Gray),
            };

            foreach (var prefixToColor in prefixesToColors)
                foreach (var command in GetScanCodeCommands(scanCode, input.Commands))
                    if (command.ToString().StartsWith(prefixToColor.Key))
                        return prefixToColor.Value;

            return Color.Transparent;
        }

        public static void DumpToText(this InputSettings input, string filePath)
        {
            using (var writer = new StreamWriter(File.OpenWrite(filePath)))
            {
                writer.WriteLine("{0,-40}{1,-40}{2}", "Command", "Key", "Unique Inputs");
                writer.WriteLine(new String('=', 40 * 3));
                foreach (var command in EnumExtension.GetValues<UserCommand>())
                    writer.WriteLine("{0,-40}{1,-40}{2}", GetPrettyCommandName(command), input.Commands[(int)command], String.Join(", ", input.Commands[(int)command].GetUniqueInputs().OrderBy(s => s).ToArray()));
            }
        }

        public static void DumpToGraphic(this InputSettings input, string filePath)
        {
            var keyWidth = 50;
            var keyHeight = 4 * keyWidth;
            var keySpacing = 5;
            var keyFontLabel = new System.Drawing.Font(System.Drawing.SystemFonts.MessageBoxFont.FontFamily, keyHeight * 0.33f, System.Drawing.GraphicsUnit.Pixel);
            var keyFontCommand = new System.Drawing.Font(System.Drawing.SystemFonts.MessageBoxFont.FontFamily, keyHeight * 0.22f, System.Drawing.GraphicsUnit.Pixel);
            var keyboardLayoutBitmap = new System.Drawing.Bitmap(KeyboardLayout[0].Length * keyWidth, KeyboardLayout.Length * keyHeight);
            using (var g = System.Drawing.Graphics.FromImage(keyboardLayoutBitmap))
            {
                DrawKeyboardMap(null, (keyBox, keyScanCode, keyName) =>
                {
                    var keyCommands = GetScanCodeCommands(keyScanCode, input.Commands);
                    var keyCommandNames = String.Join("\n", keyCommands.Select(c => String.Join(" ", GetPrettyCommandName(c).Split(' ').Skip(1).ToArray())).ToArray());

                    var keyColor = input.GetScanCodeColor(keyScanCode);
                    var keyTextColor = System.Drawing.Brushes.Black;
                    if (keyColor == Color.Transparent)
                    {
                        keyColor = Color.White;
                    }
                    else
                    {
                        keyColor.R += (byte)((255 - keyColor.R) * 2 / 3);
                        keyColor.G += (byte)((255 - keyColor.G) * 2 / 3);
                        keyColor.B += (byte)((255 - keyColor.B) * 2 / 3);
                    }

                    Scale(ref keyBox, keyWidth, keyHeight);
                    keyBox.Inflate(-keySpacing, -keySpacing);

                    g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb((int)keyColor.PackedValue)), keyBox.Left, keyBox.Top, keyBox.Width, keyBox.Height);
                    g.DrawRectangle(System.Drawing.Pens.Black, keyBox.Left, keyBox.Top, keyBox.Width, keyBox.Height);
                    g.DrawString(keyName, keyFontLabel, keyTextColor, keyBox.Right - g.MeasureString(keyName, keyFontLabel).Width + keySpacing, keyBox.Top - 3 * keySpacing);
                    g.DrawString(keyCommandNames, keyFontCommand, keyTextColor, keyBox.Left, keyBox.Bottom - keyCommands.Count() * keyFontCommand.Height);
                });
            }
            keyboardLayoutBitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
        }

        public static void Scale(ref Rectangle rectangle, int scaleX, int scaleY)
        {
            rectangle.X *= scaleX;
            rectangle.Y *= scaleY;
            rectangle.Width *= scaleX;
            rectangle.Height *= scaleY;
        }

        public static string GetPrettyCommandName(UserCommand command)
        {
            var name = command.ToString();
            var nameU = name.ToUpperInvariant();
            var nameL = name.ToLowerInvariant();
            for (var i = name.Length - 1; i > 0; i--)
            {
                if (((name[i - 1] != nameU[i - 1]) && (name[i] == nameU[i])) ||
                    (name[i - 1] == nameL[i - 1]) && (name[i] != nameL[i]))
                {
                    name = name.Insert(i, " ");
                    nameL = nameL.Insert(i, " ");
                }
            }
            return name;
        }

        public static string GetPrettyUniqueInput(string uniqueInput)
        {
            var parts = uniqueInput.Split('+');
            if (parts[parts.Length - 1].StartsWith("0x"))
            {
                var key = int.Parse(parts[parts.Length - 1].Substring(2), NumberStyles.AllowHexSpecifier);
                parts[parts.Length - 1] = ScanCodeKeyUtils.GetScanCodeKeyName(key);
            }
            return string.Join(" + ", parts);
        }

        private static IEnumerable<UserCommand> GetScanCodeCommands(int scanCode, IList<UserCommandInput> commands)
        {
            return EnumExtension.GetValues<UserCommand>().Where(uc => ((commands[(int)uc] as UserCommandKeyInput)?.ScanCode == scanCode));
        }


    }
}
