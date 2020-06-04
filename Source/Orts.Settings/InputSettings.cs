// COPYRIGHT 2012, 2013, 2014 by the Open Rails project.
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

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GNU.Gettext;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Orts.Common;
using Orts.Common.Input;
using Orts.Settings.Store;
using Orts.Settings.Util;

namespace Orts.Settings
{
    /// <summary>
    /// Loads, stores and manages keyboard input settings for all available <see cref="UserCommands"/>.
    /// </summary>
    /// <remarks>
    /// <para>Keyboard input is processed by associating specific combinations of keys (either scan codes or virtual keys) and modifiers with each <see cref="UserCommands"/>.</para>
    /// <para>There are three kinds of <see cref="UserCommand"/>, each using a different <see cref="UserCommandInput"/>:</para>
    /// <list type="bullet">
    /// <item><description><see cref="UserCommandModifierInput"/> represents a specific combination of keyboard modifiers (Shift, Control and Alt). E.g. Shift.</description></item>
    /// <item><description><see cref="UserCommandKeyInput"/> represents a key (scan code or virtual key) and a specific combination of keyboard modifiers. E.g. Alt-F4.</description></item>
    /// <item><description><see cref="UserCommandModifiableKeyInput"/> represents a key (scan code or virtual key), a specific combination of keyboard modifiers and a set of keyboard modifiers to ignore. E.g. Up Arrow (+ Shift) (+ Control).</description></item>
    /// </list>
    /// <para>Keyboard input is identified in two distinct ways:</para>
    /// <list>
    /// <item><term>Scan code</term><description>A scan code represents a specific location on the physical keyboard, irrespective of the user's locale, keyboard layout and other enviromental settings. For this reason, this is the preferred way to refer to the "main" area of the keyboard - this area varies significantly by locale and usually it is the physical location that matters.</description></item>
    /// <item><term>Virtual key</term><description>A virtual key represents a logical key on the keyboard, irrespective of where it might be located. For keys outside the "main" area, this is much the same as scan codes and is preferred when refering to logical keys like "Up Arrow".</description></item>
    /// </list>
    /// </remarks>
    public class InputSettings : SettingsBase
    {
        static GettextResourceManager commonCatalog = new GettextResourceManager("ORTS.Common");
        static GettextResourceManager settingsCatalog = new GettextResourceManager("ORTS.Settings");

        public static readonly UserCommandInput[] DefaultCommands = new UserCommandInput[Enum.GetNames(typeof(UserCommand)).Length];
        public readonly UserCommandInput[] Commands = new UserCommandInput[Enum.GetNames(typeof(UserCommand)).Length];

        static InputSettings()
        {
            InitializeCommands(DefaultCommands);
        }

        public InputSettings(IEnumerable<string> options, SettingsStore store) : 
            base(SettingsStore.GetSettingsStore(store.StoreType, store.Location, "Keyboard"))
        {
            InitializeCommands(Commands);
            LoadSettings(options);
        }

        UserCommand GetCommand(string name)
        {
            if (!EnumExtension.GetValue(name, out UserCommand result))
                throw new ArgumentOutOfRangeException();
            return result;
        }

        UserCommand[] GetCommands()
        {
            return EnumExtension.GetValues<UserCommand>().ToArray();
        }

        public override object GetDefaultValue(string name)
        {
            return DefaultCommands[(int)GetCommand(name)].UniqueDescriptor;
        }

        protected override object GetValue(string name)
        {
            return Commands[(int)GetCommand(name)].UniqueDescriptor;
        }

        protected override void SetValue(string name, object value)
        {
            Commands[(int)GetCommand(name)].UniqueDescriptor = (int)value;
        }

        protected override void Load(bool allowUserSettings, NameValueCollection optionalValues)
        {
            foreach (UserCommand command in EnumExtension.GetValues<UserCommand>())
                LoadSetting(allowUserSettings, optionalValues, command.ToString());
            properties = null;
        }

        public override void Save()
        {
            foreach (UserCommand command in EnumExtension.GetValues<UserCommand>())
                SaveSetting(command.ToString());
            properties = null;
        }

        public override void Reset()
        {
            foreach (UserCommand command in EnumExtension.GetValues<UserCommand>())
                Reset(command.ToString());
        }

        #region Default Input Settings
        static void InitializeCommands(UserCommandInput[] Commands)
        {
            // All UserCommandModifierInput commands go here.
            Commands[(int)UserCommand.GameSwitchWithMouse] = new UserCommandModifierInput(KeyModifiers.Alt);
            Commands[(int)UserCommand.DisplayNextWindowTab] = new UserCommandModifierInput(KeyModifiers.Shift);
            Commands[(int)UserCommand.CameraMoveFast] = new UserCommandModifierInput(KeyModifiers.Shift);
            Commands[(int)UserCommand.GameSuspendOldPlayer] = new UserCommandModifierInput(KeyModifiers.Shift);
            Commands[(int)UserCommand.CameraMoveSlow] = new UserCommandModifierInput(KeyModifiers.Control);

            // Everything else goes here, sorted alphabetically please (and grouped by first word of name).
            Commands[(int)UserCommand.CameraBrakeman] = new UserCommandKeyInput(0x07);
            Commands[(int)UserCommand.CameraBrowseBackwards] = new UserCommandKeyInput(0x4F, KeyModifiers.Shift | KeyModifiers.Alt);
            Commands[(int)UserCommand.CameraBrowseForwards] = new UserCommandKeyInput(0x47, KeyModifiers.Shift | KeyModifiers.Alt);
            Commands[(int)UserCommand.CameraCab] = new UserCommandKeyInput(0x02);
            Commands[(int)UserCommand.CameraThreeDimensionalCab] = new UserCommandKeyInput(0x02, KeyModifiers.Alt);
            Commands[(int)UserCommand.CameraCarFirst] = new UserCommandKeyInput(0x47, KeyModifiers.Alt);
            Commands[(int)UserCommand.CameraCarLast] = new UserCommandKeyInput(0x4F, KeyModifiers.Alt);
            Commands[(int)UserCommand.CameraCarNext] = new UserCommandKeyInput(0x49, KeyModifiers.Alt);
            Commands[(int)UserCommand.CameraCarPrevious] = new UserCommandKeyInput(0x51, KeyModifiers.Alt);
            Commands[(int)UserCommand.CameraFree] = new UserCommandKeyInput(0x09);
            Commands[(int)UserCommand.CameraHeadOutBackward] = new UserCommandKeyInput(0x4F);
            Commands[(int)UserCommand.CameraHeadOutForward] = new UserCommandKeyInput(0x47);
            Commands[(int)UserCommand.CameraJumpBackPlayer] = new UserCommandKeyInput(0x0A);
            Commands[(int)UserCommand.CameraJumpingTrains] = new UserCommandKeyInput(0x0A, KeyModifiers.Alt);
            Commands[(int)UserCommand.CameraJumpSeeSwitch] = new UserCommandKeyInput(0x22, KeyModifiers.Control | KeyModifiers.Alt);
            Commands[(int)UserCommand.CameraOutsideFront] = new UserCommandKeyInput(0x03);
            Commands[(int)UserCommand.CameraOutsideRear] = new UserCommandKeyInput(0x04);
            Commands[(int)UserCommand.CameraPanDown] = new UserCommandModifiableKeyInput(0x50, Commands[(int)UserCommand.CameraMoveFast], Commands[(int)UserCommand.CameraMoveSlow]);
            Commands[(int)UserCommand.CameraPanLeft] = new UserCommandModifiableKeyInput(0x4B, Commands[(int)UserCommand.CameraMoveFast], Commands[(int)UserCommand.CameraMoveSlow]);
            Commands[(int)UserCommand.CameraPanRight] = new UserCommandModifiableKeyInput(0x4D, Commands[(int)UserCommand.CameraMoveFast], Commands[(int)UserCommand.CameraMoveSlow]);
            Commands[(int)UserCommand.CameraPanUp] = new UserCommandModifiableKeyInput(0x48, Commands[(int)UserCommand.CameraMoveFast], Commands[(int)UserCommand.CameraMoveSlow]);
            Commands[(int)UserCommand.CameraPassenger] = new UserCommandKeyInput(0x06);
            Commands[(int)UserCommand.CameraPreviousFree] = new UserCommandKeyInput(0x09, KeyModifiers.Shift);
            Commands[(int)UserCommand.CameraReset] = new UserCommandKeyInput(0x09, KeyModifiers.Control);
            Commands[(int)UserCommand.CameraRotateDown] = new UserCommandModifiableKeyInput(0x50, KeyModifiers.Alt, Commands[(int)UserCommand.CameraMoveFast], Commands[(int)UserCommand.CameraMoveSlow]);
            Commands[(int)UserCommand.CameraRotateLeft] = new UserCommandModifiableKeyInput(0x4B, KeyModifiers.Alt, Commands[(int)UserCommand.CameraMoveFast], Commands[(int)UserCommand.CameraMoveSlow]);
            Commands[(int)UserCommand.CameraRotateRight] = new UserCommandModifiableKeyInput(0x4D, KeyModifiers.Alt, Commands[(int)UserCommand.CameraMoveFast], Commands[(int)UserCommand.CameraMoveSlow]);
            Commands[(int)UserCommand.CameraRotateUp] = new UserCommandModifiableKeyInput(0x48, KeyModifiers.Alt, Commands[(int)UserCommand.CameraMoveFast], Commands[(int)UserCommand.CameraMoveSlow]);
            Commands[(int)UserCommand.CameraScrollLeft] = new UserCommandModifiableKeyInput(0x4B, KeyModifiers.Alt);
            Commands[(int)UserCommand.CameraScrollRight] = new UserCommandModifiableKeyInput(0x4D, KeyModifiers.Alt);
            Commands[(int)UserCommand.CameraChangePassengerViewPoint] = new UserCommandKeyInput(0x06, KeyModifiers.Shift);
            Commands[(int)UserCommand.CameraToggleShowCab] = new UserCommandKeyInput(0x02, KeyModifiers.Shift);
            Commands[(int)UserCommand.CameraTrackside] = new UserCommandKeyInput(0x05); ;
            Commands[(int)UserCommand.CameraSpecialTracksidePoint] = new UserCommandKeyInput(0x05, KeyModifiers.Shift);
            Commands[(int)UserCommand.CameraVibrate] = new UserCommandKeyInput(0x2F, KeyModifiers.Control);
            Commands[(int)UserCommand.CameraZoomIn] = new UserCommandModifiableKeyInput(0x49, Commands[(int)UserCommand.CameraMoveFast], Commands[(int)UserCommand.CameraMoveSlow]);
            Commands[(int)UserCommand.CameraZoomOut] = new UserCommandModifiableKeyInput(0x51, Commands[(int)UserCommand.CameraMoveFast], Commands[(int)UserCommand.CameraMoveSlow]);

            Commands[(int)UserCommand.ControlAIFireOn] = new UserCommandKeyInput(0x23, KeyModifiers.Alt);
            Commands[(int)UserCommand.ControlAIFireOff] = new UserCommandKeyInput(0x23, KeyModifiers.Control);
            Commands[(int)UserCommand.ControlAIFireReset] = new UserCommandKeyInput(0x23, KeyModifiers.Control | KeyModifiers.Alt);
            Commands[(int)UserCommand.ControlAlerter] = new UserCommandKeyInput(0x2C);
            Commands[(int)UserCommand.ControlBackwards] = new UserCommandKeyInput(0x1F);
            Commands[(int)UserCommand.ControlBailOff] = new UserCommandKeyInput(0x35);
            Commands[(int)UserCommand.ControlBell] = new UserCommandKeyInput(0x30);
            Commands[(int)UserCommand.ControlBellToggle] = new UserCommandKeyInput(0x30, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlBlowerDecrease] = new UserCommandKeyInput(0x31, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlBlowerIncrease] = new UserCommandKeyInput(0x31);
            Commands[(int)UserCommand.ControlSteamHeatDecrease] = new UserCommandKeyInput(0x20, KeyModifiers.Alt);
            Commands[(int)UserCommand.ControlSteamHeatIncrease] = new UserCommandKeyInput(0x16, KeyModifiers.Alt);
            Commands[(int)UserCommand.ControlBrakeHoseConnect] = new UserCommandKeyInput(0x2B);
            Commands[(int)UserCommand.ControlBrakeHoseDisconnect] = new UserCommandKeyInput(0x2B, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlCabRadio] = new UserCommandKeyInput(0x13, KeyModifiers.Alt);
            Commands[(int)UserCommand.ControlCircuitBreakerClosingOrder] = new UserCommandKeyInput(0x18);
            Commands[(int)UserCommand.ControlCircuitBreakerOpeningOrder] = new UserCommandKeyInput(0x17);
            Commands[(int)UserCommand.ControlCircuitBreakerClosingAuthorization] = new UserCommandKeyInput(0x18, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlCylinderCocks] = new UserCommandKeyInput(0x2E);
            Commands[(int)UserCommand.ControlLargeEjectorIncrease] = new UserCommandKeyInput(0x24, KeyModifiers.Control);
            Commands[(int)UserCommand.ControlLargeEjectorDecrease] = new UserCommandKeyInput(0x24, KeyModifiers.Alt);
            Commands[(int)UserCommand.ControlSmallEjectorIncrease] = new UserCommandKeyInput(0x24);
            Commands[(int)UserCommand.ControlSmallEjectorDecrease] = new UserCommandKeyInput(0x24, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlCylinderCompound] = new UserCommandKeyInput(0x19);
            Commands[(int)UserCommand.ControlDamperDecrease] = new UserCommandKeyInput(0x32, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlDamperIncrease] = new UserCommandKeyInput(0x32);
            Commands[(int)UserCommand.ControlDieselHelper] = new UserCommandKeyInput(0x15, KeyModifiers.Control);
            Commands[(int)UserCommand.ControlDieselPlayer] = new UserCommandKeyInput(0x15, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlDoorLeft] = new UserCommandKeyInput(0x10);
            Commands[(int)UserCommand.ControlDoorRight] = new UserCommandKeyInput(0x10, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlDynamicBrakeDecrease] = new UserCommandKeyInput(0x33);
            Commands[(int)UserCommand.ControlDynamicBrakeIncrease] = new UserCommandKeyInput(0x34);
            Commands[(int)UserCommand.ControlEmergencyPushButton] = new UserCommandKeyInput(0x0E);
            Commands[(int)UserCommand.ControlEngineBrakeDecrease] = new UserCommandKeyInput(0x1A);
            Commands[(int)UserCommand.ControlEngineBrakeIncrease] = new UserCommandKeyInput(0x1B);
            Commands[(int)UserCommand.ControlFireboxClose] = new UserCommandKeyInput(0x21, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlFireboxOpen] = new UserCommandKeyInput(0x21);
            Commands[(int)UserCommand.ControlFireShovelFull] = new UserCommandKeyInput(0x13, KeyModifiers.Control);
            Commands[(int)UserCommand.ControlFiring] = new UserCommandKeyInput(0x21, KeyModifiers.Control);
            Commands[(int)UserCommand.ControlFiringRateDecrease] = new UserCommandKeyInput(0x13, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlFiringRateIncrease] = new UserCommandKeyInput(0x13);
            Commands[(int)UserCommand.ControlForwards] = new UserCommandKeyInput(0x11);
            Commands[(int)UserCommand.ControlGearDown] = new UserCommandKeyInput(0x12, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlGearUp] = new UserCommandKeyInput(0x12);
            Commands[(int)UserCommand.ControlHandbrakeFull] = new UserCommandKeyInput(0x28, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlHandbrakeNone] = new UserCommandKeyInput(0x27, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlHeadlightDecrease] = new UserCommandKeyInput(0x23, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlHeadlightIncrease] = new UserCommandKeyInput(0x23);
            Commands[(int)UserCommand.ControlHorn] = new UserCommandKeyInput(0x39);
            Commands[(int)UserCommand.ControlImmediateRefill] = new UserCommandKeyInput(0x14, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlInitializeBrakes] = new UserCommandKeyInput(0x35, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlInjector1] = new UserCommandKeyInput(0x17);
            Commands[(int)UserCommand.ControlInjector1Decrease] = new UserCommandKeyInput(0x25, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlInjector1Increase] = new UserCommandKeyInput(0x25);
            Commands[(int)UserCommand.ControlInjector2] = new UserCommandKeyInput(0x18);
            Commands[(int)UserCommand.ControlInjector2Decrease] = new UserCommandKeyInput(0x26, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlInjector2Increase] = new UserCommandKeyInput(0x26);
            Commands[(int)UserCommand.ControlLight] = new UserCommandKeyInput(0x26);
            Commands[(int)UserCommand.ControlMirror] = new UserCommandKeyInput(0x2F, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlPantograph1] = new UserCommandKeyInput(0x19);
            Commands[(int)UserCommand.ControlPantograph2] = new UserCommandKeyInput(0x19, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlPantograph3] = new UserCommandKeyInput(0x19, KeyModifiers.Control);
            Commands[(int)UserCommand.ControlPantograph4] = new UserCommandKeyInput(0x19, KeyModifiers.Shift | KeyModifiers.Control);
            Commands[(int)UserCommand.ControlOdoMeterShowHide] = new UserCommandKeyInput(0x2C, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlOdoMeterReset] = new UserCommandKeyInput(0x2C, KeyModifiers.Control);
            Commands[(int)UserCommand.ControlOdoMeterDirection] = new UserCommandKeyInput(0x2C, KeyModifiers.Control | KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlRefill] = new UserCommandKeyInput(0x14);
            Commands[(int)UserCommand.ControlRetainersOff] = new UserCommandKeyInput(0x1A, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlRetainersOn] = new UserCommandKeyInput(0x1B, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlSander] = new UserCommandKeyInput(0x2D);
            Commands[(int)UserCommand.ControlSanderToggle] = new UserCommandKeyInput(0x2D, KeyModifiers.Shift);
            Commands[(int)UserCommand.ControlThrottleDecrease] = new UserCommandKeyInput(0x1E);
            Commands[(int)UserCommand.ControlThrottleIncrease] = new UserCommandKeyInput(0x20);
            Commands[(int)UserCommand.ControlThrottleZero] = new UserCommandKeyInput(0x1E, KeyModifiers.Control);
            Commands[(int)UserCommand.ControlTrainBrakeDecrease] = new UserCommandKeyInput(0x27);
            Commands[(int)UserCommand.ControlTrainBrakeIncrease] = new UserCommandKeyInput(0x28);
            Commands[(int)UserCommand.ControlTrainBrakeZero] = new UserCommandKeyInput(0x27, KeyModifiers.Control);
            Commands[(int)UserCommand.ControlTurntableClockwise] = new UserCommandKeyInput(0x2E, KeyModifiers.Alt);
            Commands[(int)UserCommand.ControlTurntableCounterclockwise] = new UserCommandKeyInput(0x2E, KeyModifiers.Control);
            Commands[(int)UserCommand.ControlWaterScoop] = new UserCommandKeyInput(0x15);
            Commands[(int)UserCommand.ControlWiper] = new UserCommandKeyInput(0x2F);

            Commands[(int)UserCommand.DebugClockBackwards] = new UserCommandKeyInput(0x0C);
            Commands[(int)UserCommand.DebugClockForwards] = new UserCommandKeyInput(0x0D);
            Commands[(int)UserCommand.DebugDumpKeymap] = new UserCommandKeyInput(0x3B, KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugFogDecrease] = new UserCommandKeyInput(0x0C, KeyModifiers.Shift);
            Commands[(int)UserCommand.DebugFogIncrease] = new UserCommandKeyInput(0x0D, KeyModifiers.Shift);
            Commands[(int)UserCommand.DebugLockShadows] = new UserCommandKeyInput(0x1F, KeyModifiers.Control | KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugLogger] = new UserCommandKeyInput(0x58);
            Commands[(int)UserCommand.DebugLogRenderFrame] = new UserCommandKeyInput(0x58, KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugOvercastDecrease] = new UserCommandKeyInput(0x0C, KeyModifiers.Control);
            Commands[(int)UserCommand.DebugOvercastIncrease] = new UserCommandKeyInput(0x0D, KeyModifiers.Control);
            Commands[(int)UserCommand.DebugPhysicsForm] = new UserCommandKeyInput(0x3D, KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugPrecipitationDecrease] = new UserCommandKeyInput(0x0C, KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugPrecipitationIncrease] = new UserCommandKeyInput(0x0D, KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugPrecipitationLiquidityDecrease] = new UserCommandKeyInput(0x0C, KeyModifiers.Control | KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugPrecipitationLiquidityIncrease] = new UserCommandKeyInput(0x0D, KeyModifiers.Control | KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugResetWheelSlip] = new UserCommandKeyInput(0x2D, KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugSignalling] = new UserCommandKeyInput(0x57, KeyModifiers.Control | KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugSoundForm] = new UserCommandKeyInput(0x1F, KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugSpeedDown] = new UserCommandKeyInput(0x51, KeyModifiers.Control | KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugSpeedReset] = new UserCommandKeyInput(0x47, KeyModifiers.Control | KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugSpeedUp] = new UserCommandKeyInput(0x49, KeyModifiers.Control | KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugToggleAdvancedAdhesion] = new UserCommandKeyInput(0x2D, KeyModifiers.Control | KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugTracks] = new UserCommandKeyInput(0x40, KeyModifiers.Control | KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugWeatherChange] = new UserCommandKeyInput(0x19, KeyModifiers.Alt);
            Commands[(int)UserCommand.DebugToggleConfirmations] = new UserCommandKeyInput(0x44, KeyModifiers.Control | KeyModifiers.Alt);

            Commands[(int)UserCommand.DisplayTrainListWindow] = new UserCommandKeyInput(0x43, KeyModifiers.Alt);
            Commands[(int)UserCommand.DisplayBasicHUDToggle] = new UserCommandKeyInput(0x3F, KeyModifiers.Alt);
            Commands[(int)UserCommand.DisplayCarLabels] = new UserCommandModifiableKeyInput(0x41, Commands[(int)UserCommand.DisplayNextWindowTab]);
            Commands[(int)UserCommand.DisplayCompassWindow] = new UserCommandKeyInput(0x0B);
            Commands[(int)UserCommand.DisplayHelpWindow] = new UserCommandModifiableKeyInput(0x3B, Commands[(int)UserCommand.DisplayNextWindowTab]);
            Commands[(int)UserCommand.DisplayHUD] = new UserCommandModifiableKeyInput(0x3F, Commands[(int)UserCommand.DisplayNextWindowTab]);
            Commands[(int)UserCommand.DisplayTrainDrivingWindow] = new UserCommandModifiableKeyInput(0x3F, KeyModifiers.Control);
            Commands[(int)UserCommand.DisplayNextStationWindow] = new UserCommandKeyInput(0x44);
            Commands[(int)UserCommand.DisplayStationLabels] = new UserCommandModifiableKeyInput(0x40, Commands[(int)UserCommand.DisplayNextWindowTab]);
            Commands[(int)UserCommand.DisplaySwitchWindow] = new UserCommandKeyInput(0x42);
            Commands[(int)UserCommand.DisplayTrackMonitorWindow] = new UserCommandKeyInput(0x3E);
            Commands[(int)UserCommand.DisplayTrainOperationsWindow] = new UserCommandKeyInput(0x43);

            Commands[(int)UserCommand.GameAutopilotMode] = new UserCommandKeyInput(0x1E, KeyModifiers.Alt);
            Commands[(int)UserCommand.GameChangeCab] = new UserCommandKeyInput(0x12, KeyModifiers.Control);
            Commands[(int)UserCommand.GameClearSignalBackward] = new UserCommandKeyInput(0x0F, KeyModifiers.Shift);
            Commands[(int)UserCommand.GameClearSignalForward] = new UserCommandKeyInput(0x0F);
            Commands[(int)UserCommand.GameExternalCabController] = new UserCommandKeyInput(0x29);
            Commands[(int)UserCommand.GameFullscreen] = new UserCommandKeyInput(0x1C, KeyModifiers.Alt);
            Commands[(int)UserCommand.GameMultiPlayerDispatcher] = new UserCommandKeyInput(0x0A, KeyModifiers.Control);
            Commands[(int)UserCommand.GameMultiPlayerTexting] = new UserCommandKeyInput(0x14, KeyModifiers.Control);
            Commands[(int)UserCommand.GamePause] = new UserCommandKeyInput(Keys.Pause);
            Commands[(int)UserCommand.GamePauseMenu] = new UserCommandKeyInput(0x01);
            Commands[(int)UserCommand.GameQuit] = new UserCommandKeyInput(0x3E, KeyModifiers.Alt);
            Commands[(int)UserCommand.GameRequestControl] = new UserCommandKeyInput(0x12, KeyModifiers.Alt);
            Commands[(int)UserCommand.GameResetSignalBackward] = new UserCommandKeyInput(0x0F, KeyModifiers.Control | KeyModifiers.Shift);
            Commands[(int)UserCommand.GameResetSignalForward] = new UserCommandKeyInput(0x0F, KeyModifiers.Control);
            Commands[(int)UserCommand.GameSave] = new UserCommandKeyInput(0x3C);
            Commands[(int)UserCommand.GameScreenshot] = new UserCommandKeyInput(Keys.PrintScreen);
            Commands[(int)UserCommand.GameSignalPicked] = new UserCommandKeyInput(0x22, KeyModifiers.Control);
            Commands[(int)UserCommand.GameSwitchAhead] = new UserCommandKeyInput(0x22);
            Commands[(int)UserCommand.GameSwitchBehind] = new UserCommandKeyInput(0x22, KeyModifiers.Shift);
            Commands[(int)UserCommand.GameSwitchManualMode] = new UserCommandKeyInput(0x32, KeyModifiers.Control);
            Commands[(int)UserCommand.GameSwitchPicked] = new UserCommandKeyInput(0x22, KeyModifiers.Alt);
            Commands[(int)UserCommand.GameUncoupleWithMouse] = new UserCommandKeyInput(0x16);
        }
        #endregion

        public string CheckForErrors()
        {
            // Make sure all modifiable input commands are synchronized first.
            foreach (var command in Commands)
                (command as UserCommandModifiableKeyInput)?.SynchronizeCombine();

            StringBuilder errors = new StringBuilder();

            // Check for commands which both require a particular modifier, and ignore it.
            foreach (var command in EnumExtension.GetValues<UserCommand>())
            {
                if (Commands[(int)command] is UserCommandModifiableKeyInput modInput)
                {
                    if (modInput.Shift && modInput.IgnoreShift)
                        errors.AppendLine(settingsCatalog.GetStringFmt("{0} requires and is modified by Shift", commonCatalog.GetString(command.GetDescription())));
                    if (modInput.Control && modInput.IgnoreControl)
                        errors.AppendLine(settingsCatalog.GetStringFmt("{0} requires and is modified by Control", commonCatalog.GetString(command.GetDescription())));
                    if (modInput.Alt && modInput.IgnoreAlt)
                        errors.AppendLine(settingsCatalog.GetStringFmt("{0} requires and is modified by Alt", commonCatalog.GetString(command.GetDescription())));
                }
            }

            // Check for two commands assigned to the same key
            UserCommand firstCommand = EnumExtension.GetValues<UserCommand>().Min();
            UserCommand lastCommand = EnumExtension.GetValues<UserCommand>().Max();
            for (UserCommand command1 = firstCommand; command1 <= lastCommand; command1++)
            {
                var input1 = Commands[(int)command1];

                // Modifier inputs don't matter as they don't represent any key.
                if (input1 is UserCommandModifierInput)
                    continue;

                for (var command2 = command1 + 1; command2 <= lastCommand; command2++)
                {
                    var input2 = Commands[(int)command2];

                    // Modifier inputs don't matter as they don't represent any key.
                    if (input2 is UserCommandModifierInput)
                        continue;

                    // Ignore problems when both inputs are on defaults. (This protects the user somewhat but leaves developers in the dark.)
                    //if (input1.PersistentDescriptor == InputSettings.DefaultCommands[(int)command1].PersistentDescriptor && input2.PersistentDescriptor == InputSettings.DefaultCommands[(int)command2].PersistentDescriptor)
                    if (input1.UniqueDescriptor == DefaultCommands[(int)command1].UniqueDescriptor &&
                    input2.UniqueDescriptor == DefaultCommands[(int)command2].UniqueDescriptor)
                        continue;

                    var unique1 = input1.GetUniqueInputs();
                    var unique2 = input2.GetUniqueInputs();
                    var sharedUnique = unique1.Where(id => unique2.Contains(id));
                    foreach (var uniqueInput in sharedUnique)
                        errors.AppendLine(settingsCatalog.GetStringFmt("{0} and {1} both match {2}", commonCatalog.GetString(command1.GetDescription()), 
                            commonCatalog.GetString(command2.GetDescription()), KeyboardMap.GetPrettyUniqueInput(uniqueInput)));
                }
            }

            return errors.ToString();
        }
    }

}
