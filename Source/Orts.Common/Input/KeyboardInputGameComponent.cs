﻿using System;
using System.Collections.Generic;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Orts.Common.Input
{
    public class KeyboardInputGameComponent : GameComponent
    {
        public delegate void KeyEvent(Keys key, KeyModifiers modifiers, GameTime gameTime);

        private const int KeyPressShift = 8;
        private const int KeyDownShift = 13;
        private const int KeyUpShift = 17;

        private KeyboardState currentKeyboardState;
        private KeyboardState previousKeyboardState;
        private KeyModifiers previousModifiers;
        private KeyModifiers currentModifiers;
        private Keys[] previousKeys = Array.Empty<Keys>();
        private readonly Dictionary<int, KeyEvent> keyEvents = new Dictionary<int, KeyEvent>();

        private readonly IInputCapture inputCapture;

        private Action<int, GameTime, KeyEventType, KeyModifiers> inputActionHandler;

        public KeyboardInputGameComponent(Game game) : base(game)
        {
            inputCapture = game as IInputCapture;
        }

        public ref readonly KeyboardState KeyboardState => ref currentKeyboardState;

        public KeyModifiers KeyModifiers => currentModifiers;

        public static int KeyEventCode(Keys key, KeyModifiers modifiers, KeyEventType keyEventType)
        {
            switch (keyEventType)
            {
                case KeyEventType.KeyDown:
                    return (int)key << KeyDownShift ^ (int)modifiers;
                case KeyEventType.KeyPressed:
                    return (int)key << KeyPressShift ^ (int)modifiers;
                case KeyEventType.KeyReleased:
                    return (int)key << KeyUpShift ^ (int)modifiers;
                default:
                    throw new NotSupportedException();
            }
        }

        public void AddInputHandler(Action<int, GameTime, KeyEventType, KeyModifiers> inputAction)
        {
            inputActionHandler += inputAction;
        }

        public void AddKeyEvent(Keys key, KeyModifiers modifiers, KeyEventType keyEventType, KeyEvent eventHandler)
        {
            int lookupCode = KeyEventCode(key, modifiers, keyEventType);
            if (keyEvents.ContainsKey(lookupCode))
                keyEvents[lookupCode] += eventHandler;
            else
                keyEvents[lookupCode] = eventHandler;
        }

        public void RemoveKeyEvent(Keys key, KeyModifiers modifiers, KeyEventType keyEventType, KeyEvent eventHandler)
        {
            int lookupCode = KeyEventCode(key, modifiers, keyEventType);
            if (keyEvents.ContainsKey(lookupCode))
                keyEvents[lookupCode] -= eventHandler;
        }

        public override void Update(GameTime gameTime)
        {
            if (!Game.IsActive || (inputCapture?.InputCaptured ?? false))
            {
                currentKeyboardState = default;
                return;
            }

            KeyboardState newState = Keyboard.GetState();

            #region keyboard update
            //if (currentKeyboardState != previousKeyboardState || currentKeyboardState.GetPressedKeyCount() != 0)
            if (currentKeyboardState != newState || newState.GetPressedKeyCount() != 0)
            {
                (currentKeyboardState, previousKeyboardState) = (previousKeyboardState, currentKeyboardState);
                (currentModifiers, previousModifiers) = (previousModifiers, currentModifiers);
                currentKeyboardState = newState;

                currentModifiers = KeyModifiers.None;
                if (currentKeyboardState.IsKeyDown(Keys.LeftShift) || currentKeyboardState.IsKeyDown(Keys.RightShift))
                    currentModifiers |= KeyModifiers.Shift;
                if (currentKeyboardState.IsKeyDown(Keys.LeftControl) || currentKeyboardState.IsKeyDown(Keys.RightControl))
                    currentModifiers |= KeyModifiers.Control;
                if (currentKeyboardState.IsKeyDown(Keys.LeftAlt) || currentKeyboardState.IsKeyDown(Keys.LeftAlt))
                    currentModifiers |= KeyModifiers.Alt;

                Keys[] currentKeys = currentKeyboardState.GetPressedKeys();
                foreach (Keys key in currentKeys)
                {
                    //if (key == Keys.LeftShift || key == Keys.RightShift || key == Keys.LeftControl || key == Keys.RightControl || key == Keys.LeftAlt || key == Keys.RightAlt)
                    if ((int)key > 159 && (int)key < 166)
                        continue;
                    if (previousKeyboardState.IsKeyDown(key) && (currentModifiers == previousModifiers))
                    {
                        // Key (still) down
                        int lookup = (int)key << KeyDownShift ^ (int)currentModifiers;
                        inputActionHandler?.Invoke(lookup, gameTime, KeyEventType.KeyDown, currentModifiers);
                    }
                    if (previousKeyboardState.IsKeyDown(key) && (currentModifiers != previousModifiers))
                    {
                        // Key Up, state may have changed due to a modifier changed
                        int lookup = (int)key << KeyUpShift ^ (int)previousModifiers;
                        inputActionHandler?.Invoke(lookup, gameTime, KeyEventType.KeyReleased, previousModifiers);
                    }
                    if (!previousKeyboardState.IsKeyDown(key) || (currentModifiers != previousModifiers))
                    {
                        //Key just pressed
                        int lookup = (int)key << KeyPressShift ^ (int)currentModifiers;
                        inputActionHandler?.Invoke(lookup, gameTime, KeyEventType.KeyPressed, currentModifiers);
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
                    int lookup = (int)key << KeyUpShift ^ (int)previousModifiers;
                    inputActionHandler?.Invoke(lookup, gameTime, KeyEventType.KeyReleased, previousModifiers);
                }
                previousKeys = currentKeys;
            }
            #endregion

            base.Update(gameTime);
        }

        public bool KeyState(Keys key, KeyModifiers modifiers, KeyEventType keyEventType)
        {
            switch (keyEventType)
            {
                case KeyEventType.KeyDown:
                    if (currentKeyboardState.IsKeyDown(key) && modifiers == currentModifiers && previousModifiers == currentModifiers == previousKeyboardState.IsKeyDown(key))
                        return true;
                    break;
                case KeyEventType.KeyPressed:
                    if (currentKeyboardState.IsKeyDown(key) && modifiers == currentModifiers && (previousModifiers != currentModifiers || !previousKeyboardState.IsKeyDown(key)))
                        return true;
                    break;
                case KeyEventType.KeyReleased:
                    if ((!currentKeyboardState.IsKeyDown(key) || previousModifiers != currentModifiers) && (previousModifiers == modifiers && previousKeyboardState.IsKeyDown(key)))
                        return true;
                    break;
                default:
                    throw new NotSupportedException();
            }
            return false;
        }
    }
}
