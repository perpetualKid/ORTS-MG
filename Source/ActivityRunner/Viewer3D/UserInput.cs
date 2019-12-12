// COPYRIGHT 2009, 2010, 2011, 2012, 2013 by the Open Rails project.
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

// This logs the raw changes in input state.
//#define DEBUG_RAW_INPUT

// This logs the changes in input state, taking into account any corrections made by the code (e.g. swapped mouse buttons).
//#define DEBUG_INPUT

// This logs every UserCommandInput change from pressed to released.
//#define DEBUG_USER_INPUT

using System.Runtime.InteropServices;
//using System.Linq;      //DEBUG_INPUT only
using Microsoft.Xna.Framework.Input;
using Orts.Common.Input;
using Orts.Settings;
using Game = Orts.ActivityRunner.Viewer3D.Processes.Game;

namespace Orts.ActivityRunner.Viewer3D
{
    public static class UserInput
    {
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(Keys key);

        private static KeyboardState keyboardState;
        private static MouseState mouseState;
        private static KeyboardState lastKeyboardState;
        private static MouseState lastMouseState;
        private static readonly KeyboardState emptyKeyboardState = new KeyboardState();

        public static bool ComposingMessage { get; set; }

        public static UserInputRailDriver Raildriver { get; private set; }

        private static InputSettings inputSettings;

        private static Game game;

        public static void Initialize(Game game)
        {
            UserInput.game = game;
            inputSettings = game.Settings.Input;
            Raildriver = new UserInputRailDriver(game);
        }

        public static void Update()
        {
            Raildriver.Update();
            if (Orts.MultiPlayer.MPManager.IsMultiPlayer() && Orts.MultiPlayer.MPManager.Instance().ComposingText)
                return;

            lastKeyboardState = keyboardState;
            lastMouseState = mouseState;
            // Make sure we have an "idle" (everything released) keyboard and mouse state if the window isn't active.
            keyboardState = game.IsActive ? Keyboard.GetState() : emptyKeyboardState;
            mouseState = game.IsActive ? Mouse.GetState(game.Window) : new MouseState(0, 0, lastMouseState.ScrollWheelValue, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

#if DEBUG_RAW_INPUT
            for (Keys key = 0; key <= Keys.OemClear; key++)
                if (lastKeyboardState[key] != keyboardState[key])
                    Console.WriteLine("Keyboard {0} changed to {1}", key, keyboardState[key]);
            if (lastMouseState.LeftButton != mouseState.LeftButton)
                Console.WriteLine("Mouse left button changed to {0}", mouseState.LeftButton);
            if (lastMouseState.MiddleButton != mouseState.MiddleButton)
                Console.WriteLine("Mouse middle button changed to {0}", mouseState.MiddleButton);
            if (lastMouseState.RightButton != mouseState.RightButton)
                Console.WriteLine("Mouse right button changed to {0}", mouseState.RightButton);
            if (lastMouseState.XButton1 != mouseState.XButton1)
                Console.WriteLine("Mouse X1 button changed to {0}", mouseState.XButton1);
            if (lastMouseState.XButton2 != mouseState.XButton2)
                Console.WriteLine("Mouse X2 button changed to {0}", mouseState.XButton2);
            if (lastMouseState.ScrollWheelValue != mouseState.ScrollWheelValue)
                Console.WriteLine("Mouse scrollwheel changed by {0}", mouseState.ScrollWheelValue - lastMouseState.ScrollWheelValue);
#endif
#if DEBUG_INPUT
            var newKeys = GetPressedKeys();
            var oldKeys = GetPreviousPressedKeys();
            foreach (var newKey in newKeys)
                if (!oldKeys.Contains(newKey))
                    Console.WriteLine("Keyboard {0} pressed", newKey);
            foreach (var oldKey in oldKeys)
                if (!newKeys.Contains(oldKey))
                    Console.WriteLine("Keyboard {0} released", oldKey);
            if (IsMouseLeftButtonPressed)
                Console.WriteLine("Mouse left button pressed");
            if (IsMouseLeftButtonReleased)
                Console.WriteLine("Mouse left button released");
            if (IsMouseMiddleButtonPressed)
                Console.WriteLine("Mouse middle button pressed");
            if (IsMouseMiddleButtonReleased)
                Console.WriteLine("Mouse middle button released");
            if (IsMouseRightButtonPressed)
                Console.WriteLine("Mouse right button pressed");
            if (IsMouseRightButtonReleased)
                Console.WriteLine("Mouse right button released");
            if (IsMouseWheelChanged)
                Console.WriteLine("Mouse scrollwheel changed by {0}", MouseWheelChange);
#endif
#if DEBUG_USER_INPUT
            foreach (UserCommand command in Enum.GetValues(typeof(UserCommand)))
            {
                if (UserInput.IsPressed(command))
                    Console.WriteLine("Pressed  {0} - {1}", command, InputSettings.Commands[(int)command]);
                if (UserInput.IsReleased(command))
                    Console.WriteLine("Released {0} - {1}", command, InputSettings.Commands[(int)command]);
            }
#endif
        }

        public static bool IsPressed(UserCommand command)
        {
            if (ComposingMessage == true) return false;
            if (Raildriver.IsPressed(command))
                return true;
            var setting = inputSettings.Commands[(int)command];
            return setting.IsKeyDown(keyboardState) && !setting.IsKeyDown(lastKeyboardState);
        }

        public static bool IsReleased(UserCommand command)
        {
            if (ComposingMessage == true) return false;
            if (Raildriver.IsReleased(command))
                return true;
            var setting = inputSettings.Commands[(int)command];
            return !setting.IsKeyDown(keyboardState) && setting.IsKeyDown(lastKeyboardState);
        }

        public static bool IsDown(UserCommand command)
        {
            if (ComposingMessage == true) return false;
            if (Raildriver.IsDown(command))
                return true;
            var setting = inputSettings.Commands[(int)command];
            return setting.IsKeyDown(keyboardState);
        }

        public static Keys[] GetPressedKeys() { return keyboardState.GetPressedKeys(); }
        public static Keys[] GetPreviousPressedKeys() { return lastKeyboardState.GetPressedKeys(); }

        public static bool IsMouseMoved { get { return mouseState.X != lastMouseState.X || mouseState.Y != lastMouseState.Y; } }
        public static int MouseMoveX { get { return mouseState.X - lastMouseState.X; } }
        public static int MouseMoveY { get { return mouseState.Y - lastMouseState.Y; } }
        public static int MouseX { get { return mouseState.X; } }
        public static int MouseY { get { return mouseState.Y; } }

        public static bool IsMouseWheelChanged { get { return mouseState.ScrollWheelValue != lastMouseState.ScrollWheelValue; } }
        public static int MouseWheelChange { get { return mouseState.ScrollWheelValue - lastMouseState.ScrollWheelValue; } }

        public static bool IsMouseLeftButtonDown { get { return mouseState.LeftButton == ButtonState.Pressed; } }
        public static bool IsMouseLeftButtonPressed { get { return mouseState.LeftButton == ButtonState.Pressed && lastMouseState.LeftButton == ButtonState.Released; } }
        public static bool IsMouseLeftButtonReleased { get { return mouseState.LeftButton == ButtonState.Released && lastMouseState.LeftButton == ButtonState.Pressed; } }

        public static bool IsMouseMiddleButtonDown { get { return mouseState.MiddleButton == ButtonState.Pressed; } }
        public static bool IsMouseMiddleButtonPressed { get { return mouseState.MiddleButton == ButtonState.Pressed && lastMouseState.MiddleButton == ButtonState.Released; } }
        public static bool IsMouseMiddleButtonReleased { get { return mouseState.MiddleButton == ButtonState.Released && lastMouseState.MiddleButton == ButtonState.Pressed; } }

        public static bool IsMouseRightButtonDown { get { return mouseState.RightButton == ButtonState.Pressed; } }
        public static bool IsMouseRightButtonPressed { get { return mouseState.RightButton == ButtonState.Pressed && lastMouseState.RightButton == ButtonState.Released; } }
        public static bool IsMouseRightButtonReleased { get { return mouseState.RightButton == ButtonState.Released && lastMouseState.RightButton == ButtonState.Pressed; } }
    }
}
