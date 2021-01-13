﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Text;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

using Orts.Common;
using Orts.Common.Input;

namespace Orts.View.Xna
{
    public class InputGameComponent : GameComponent
    {
        public enum KeyEventType
        {
            /// <summary>
            /// Key just pressed down
            /// </summary>
            KeyPressed = keyPressShift,
            /// <summary>
            /// Key held down
            /// </summary>
            KeyDown = keyDownShift,
            /// <summary>
            /// Key released
            /// </summary>
            KeyReleased = keyUpShift,
        }

        public enum MouseMovedEventType
        {
            MouseMoved,
            MouseMovedLeftButtonDown,
            MouseMovedRightButtonDown,
        }

        public enum MouseWheelEventType
        {
            MouseWheelChanged,
            MouseHorizontalWheelChanged,
        }

        public enum MouseButtonEventType
        { 
            LeftButtonPressed,
            LeftButtonDown,
            LeftButtonReleased,
            RightButtonPressed,
            RightButtonDown,
            RightButtonReleased,
            MiddleButtonPressed,
            MiddleButtonDown,
            MiddleButtonReleased,
            XButton1Pressed,
            XButton1Down,
            XButton1Released,
            XButton2Pressed,
            XButton2Down,
            XButton2Released,
        }

        private const int keyPressShift = 8;
        private const int keyDownShift = 13;
        private const int keyUpShift = 17;

        private KeyboardState currentKeyboardState;
        private KeyboardState previousKeyboardState;
        private KeyModifiers previousModifiers;
        private Keys[] previousKeys = Array.Empty<Keys>();
        private readonly Dictionary<int, Action> keyEvents = new Dictionary<int, Action>();

        private MouseState currentMouseState;
        private MouseState previousMouseState;
        private readonly EnumArray<Action<Point, Vector2>, MouseMovedEventType> mouseMoveEvents = new EnumArray<Action<Point, Vector2>, MouseMovedEventType>();
        private readonly EnumArray<Action<Point>, MouseButtonEventType> mouseButtonEvents = new EnumArray<Action<Point>, MouseButtonEventType>();
        private readonly EnumArray<Action<Point, int>, MouseWheelEventType> mouseWheelEvents = new EnumArray<Action<Point, int>, MouseWheelEventType>();

        private readonly IInputCapture inputCapture;

        public InputGameComponent(Game game) : base(game)
        {
            inputCapture = game as IInputCapture;
        }

        public ref readonly KeyboardState KeyboardState => ref currentKeyboardState;
        public ref readonly MouseState MouseState => ref currentMouseState;

        public void AddKeyEvent(Keys key, KeyModifiers modifiers, KeyEventType keyEventType, Action action)
        {
            int lookupCode;
            switch (keyEventType)
            {
                case KeyEventType.KeyDown:
                    lookupCode = (int)key << keyDownShift ^ (int)modifiers;
                    break;
                case KeyEventType.KeyPressed:
                    lookupCode = (int)key << keyPressShift ^ (int)modifiers;
                    break;
                case KeyEventType.KeyReleased:
                    lookupCode = (int)key << keyUpShift ^ (int)modifiers;
                    break;
                default:
                    throw new NotSupportedException();
            }
            keyEvents.Add(lookupCode, action);
        }

        public void RemoveKeyEvent(Keys key, KeyModifiers modifiers, KeyEventType keyEventType)
        {
            int lookupCode;
            switch (keyEventType)
            {
                case KeyEventType.KeyDown:
                    lookupCode = (int)key << keyDownShift ^ (int)modifiers;
                    break;
                case KeyEventType.KeyPressed:
                    lookupCode = (int)key << keyPressShift ^ (int)modifiers;
                    break;
                case KeyEventType.KeyReleased:
                    lookupCode = (int)key << keyUpShift ^ (int)modifiers;
                    break;
                default:
                    throw new NotSupportedException();
            }
            keyEvents.Remove(lookupCode);
        }

        public void AddMouseEvent(MouseMovedEventType mouseEventType, Action<Point, Vector2> action)
        {
            mouseMoveEvents[mouseEventType] = action;
        }

        public void RemoveMouseEvent(MouseMovedEventType mouseEventType)
        {
            mouseMoveEvents[mouseEventType] = null;
        }

        public void AddMouseEvent(MouseButtonEventType mouseEventType, Action<Point> action)
        {
            mouseButtonEvents[mouseEventType] = action;
        }

        public void RemoveMouseEvent(MouseButtonEventType mouseEventType)
        {
            mouseButtonEvents[mouseEventType] = null;
        }

        public void AddMouseEvent(MouseWheelEventType mouseEventType, Action<Point, int> action)
        {
            mouseWheelEvents[mouseEventType] = action;
        }

        public void RemoveMouseEvent(MouseWheelEventType mouseEventType)
        {
            mouseWheelEvents[mouseEventType] = null;
        }

        public override void Update(GameTime gameTime)
        {
            if (!Game.IsActive || (inputCapture?.InputCaptured ?? false))
            {
                currentKeyboardState = default;
                currentMouseState = default;
                return;
            }
            (currentKeyboardState, previousKeyboardState) = (previousKeyboardState, currentKeyboardState);
            (currentMouseState, previousMouseState) = (previousMouseState, currentMouseState);
            currentKeyboardState = Keyboard.GetState();
            currentMouseState = Mouse.GetState(Game.Window);


            #region keyboard update
            if (currentKeyboardState != previousKeyboardState || currentKeyboardState.GetPressedKeyCount() != 0)
            {
                KeyModifiers modifiers = KeyModifiers.None;
                if (currentKeyboardState.IsKeyDown(Keys.LeftShift) || currentKeyboardState.IsKeyDown(Keys.RightShift))
                    modifiers |= KeyModifiers.Shift;
                if (currentKeyboardState.IsKeyDown(Keys.LeftControl) || currentKeyboardState.IsKeyDown(Keys.RightControl))
                    modifiers |= KeyModifiers.Control;
                if (currentKeyboardState.IsKeyDown(Keys.LeftAlt) || currentKeyboardState.IsKeyDown(Keys.LeftAlt))
                    modifiers |= KeyModifiers.Alt;

                Keys[] currentKeys = currentKeyboardState.GetPressedKeys();
                foreach (Keys key in currentKeys)
                {
                    //if (key == Keys.LeftShift || key == Keys.RightShift || key == Keys.LeftControl || key == Keys.RightControl || key == Keys.LeftAlt || key == Keys.RightAlt)
                    if ((int)key > 159 && (int)key < 166)
                        continue;
                    Debug.WriteLine($"{key} - {modifiers}", Game.Window.Title);
                    if (previousKeyboardState.IsKeyDown(key) && (modifiers == previousModifiers))
                    {
                        // Key (still) down
                        int lookup = (int)key << keyDownShift ^ (int)modifiers;
                        if (keyEvents.TryGetValue(lookup, out Action action))
                        {
                            action.Invoke();
                        }
                    }
                    if (previousKeyboardState.IsKeyDown(key) && (modifiers != previousModifiers))
                    {
                        // Key Up, state may have changed due to a modifier changed
                        int lookup = (int)key << keyUpShift ^ (int)modifiers;
                        if (keyEvents.TryGetValue(lookup, out Action action))
                        {
                            action.Invoke();
                        }
                    }
                    if (!previousKeyboardState.IsKeyDown(key) || (modifiers != previousModifiers))
                    {
                        //Key just pressed
                        int lookup = (int)key << keyPressShift ^ (int)modifiers;
                        if (keyEvents.TryGetValue(lookup, out Action action))
                        {
                            action.Invoke();
                        }
                    }
                    int previousIndex = Array.IndexOf(previousKeys, key);//not  great, but considering this is mostly very few (<5) acceptable
                    if (previousIndex > -1)
                        previousKeys[previousIndex] = Keys.None;
                }
                foreach (Keys key in previousKeys)
                {
                    if (key == Keys.None)
                        continue;
                    //if (key == Keys.LeftShift || key == Keys.RightShift || key == Keys.LeftControl || key == Keys.RightControl || key == Keys.LeftAlt || key == Keys.RightAlt)
                    if ((int)key > 159 && (int)key < 166)
                        continue;
                    // Key Up, not in current set of Keys Downs
                    int lookup = (int)key << keyUpShift ^ (int)modifiers;
                    if (keyEvents.TryGetValue(lookup, out Action action))
                    {
                        action.Invoke();
                    }
                }
                previousModifiers = modifiers;
                previousKeys = currentKeys;
            }
            #endregion

            #region mouse updates
            if (currentMouseState != previousMouseState && previousMouseState != default)
            {
                if (currentMouseState.Position != previousMouseState.Position)
                {
                    if (currentMouseState.LeftButton == ButtonState.Pressed)
                        mouseMoveEvents[MouseMovedEventType.MouseMovedLeftButtonDown]?.Invoke(currentMouseState.Position, (currentMouseState.Position - previousMouseState.Position).ToVector2());
                    else if (currentMouseState.LeftButton == ButtonState.Pressed)
                        mouseMoveEvents[MouseMovedEventType.MouseMovedRightButtonDown]?.Invoke(currentMouseState.Position, (currentMouseState.Position - previousMouseState.Position).ToVector2());
                    else
                        mouseMoveEvents[MouseMovedEventType.MouseMoved]?.Invoke(currentMouseState.Position, (currentMouseState.Position - previousMouseState.Position).ToVector2());
                }

                int mouseWheelDelta;
                if ((mouseWheelDelta = currentMouseState.ScrollWheelValue - previousMouseState.ScrollWheelValue) != 0)
                    mouseWheelEvents[MouseWheelEventType.MouseWheelChanged]?.Invoke(currentMouseState.Position, mouseWheelDelta);
                if ((mouseWheelDelta = currentMouseState.HorizontalScrollWheelValue - previousMouseState.HorizontalScrollWheelValue) != 0)
                    mouseWheelEvents[MouseWheelEventType.MouseHorizontalWheelChanged]?.Invoke(currentMouseState.Position, mouseWheelDelta);

                void MouseButtonEvent(ButtonState currentButton, ButtonState previousButton, MouseButtonEventType down, MouseButtonEventType pressed, MouseButtonEventType released)
                {
                    if (currentButton == ButtonState.Pressed)
                    {
                        if (previousButton == ButtonState.Pressed)
                            mouseButtonEvents[down]?.Invoke(currentMouseState.Position);
                        else
                            mouseButtonEvents[pressed]?.Invoke(currentMouseState.Position);
                    }
                    else if (previousButton == ButtonState.Pressed)
                        mouseButtonEvents[released]?.Invoke(currentMouseState.Position);
                }

                MouseButtonEvent(currentMouseState.LeftButton, previousMouseState.LeftButton, MouseButtonEventType.LeftButtonDown, MouseButtonEventType.LeftButtonPressed, MouseButtonEventType.LeftButtonReleased);
                MouseButtonEvent(currentMouseState.RightButton, previousMouseState.RightButton, MouseButtonEventType.RightButtonDown, MouseButtonEventType.RightButtonPressed, MouseButtonEventType.RightButtonReleased);
                MouseButtonEvent(currentMouseState.MiddleButton, previousMouseState.MiddleButton, MouseButtonEventType.MiddleButtonDown, MouseButtonEventType.MiddleButtonPressed, MouseButtonEventType.MiddleButtonReleased);
                MouseButtonEvent(currentMouseState.XButton1, previousMouseState.XButton1, MouseButtonEventType.XButton1Down, MouseButtonEventType.XButton1Pressed, MouseButtonEventType.XButton1Released);
                MouseButtonEvent(currentMouseState.XButton2, previousMouseState.XButton2, MouseButtonEventType.XButton2Down, MouseButtonEventType.XButton2Pressed, MouseButtonEventType.XButton2Released);
            }
            else
            {
                if (currentMouseState.LeftButton == ButtonState.Pressed)
                    mouseButtonEvents[MouseButtonEventType.LeftButtonDown]?.Invoke(currentMouseState.Position);
                if (currentMouseState.RightButton == ButtonState.Pressed)
                    mouseButtonEvents[MouseButtonEventType.RightButtonDown]?.Invoke(currentMouseState.Position);
                if (currentMouseState.MiddleButton == ButtonState.Pressed)
                    mouseButtonEvents[MouseButtonEventType.MiddleButtonDown]?.Invoke(currentMouseState.Position);
                if (currentMouseState.XButton1 == ButtonState.Pressed)
                    mouseButtonEvents[MouseButtonEventType.XButton1Down]?.Invoke(currentMouseState.Position);
                if (currentMouseState.XButton2 == ButtonState.Pressed)
                    mouseButtonEvents[MouseButtonEventType.XButton2Down]?.Invoke(currentMouseState.Position);
            }
            #endregion

            base.Update(gameTime);
        }

    }
}
