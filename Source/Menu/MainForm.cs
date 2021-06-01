// COPYRIGHT 2009, 2010, 2011, 2012, 2013, 2014, 2015 by the Open Rails project.
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
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using GetText;
using GetText.WindowsForms;

using Orts.Common;
using Orts.Common.Info;
using Orts.Formats.OR.Files;
using Orts.Formats.OR.Models;
using Orts.Models.Simplified;
using Orts.Settings;
using Orts.Updater;

using Activity = Orts.Models.Simplified.Activity;
using Path = Orts.Models.Simplified.Path;

namespace Orts.Menu
{
    public partial class MainForm : Form
    {
        public enum UserAction
        {
            SingleplayerNewGame,
            SingleplayerResumeSave,
            SingleplayerReplaySave,
            SingleplayerReplaySaveFromSave,
            MultiplayerServer,
            MultiplayerClient,
            SinglePlayerTimetableGame,
            SinglePlayerResumeTimetableGame,
            MultiplayerServerResumeSave,
            MultiplayerClientResumeSave
        }

        private static readonly string[] coreExecutables = new[] {
                    "OpenRails.exe",
                    "Menu.exe",
                    "ActivityRunner.exe",
                    "Updater.exe",
                };

        private static readonly string[] documentFiles = new[]
        {
            ".pdf", ".doc", ".docx", ".pptx", ".txt"
        };

        private bool initialized;
        private UserSettings settings;
        private IEnumerable<Folder> folders = Array.Empty<Folder>();
        private IEnumerable<Route> routes = Array.Empty<Route>();
        private IEnumerable<Activity> activities = Array.Empty<Activity>();
        private IEnumerable<Consist> consists = Array.Empty<Consist>();
        private IEnumerable<Path> paths = Array.Empty<Path>();
        private IEnumerable<TimetableInfo> timetableSets = Array.Empty<TimetableInfo>();
        private IEnumerable<WeatherFileInfo> timetableWeatherFileSet = Array.Empty<WeatherFileInfo>();
        private CancellationTokenSource ctsRouteLoading;
        private CancellationTokenSource ctsActivityLoading;
        private CancellationTokenSource ctsConsistLoading;
        private CancellationTokenSource ctsPathLoading;
        private CancellationTokenSource ctsTimeTableLoading;

        private readonly ResourceManager resources = new ResourceManager("Orts.Menu.Properties.Resources", typeof(MainForm).Assembly);
        private UpdateManager updateManager;
        private readonly Image elevationIcon;
        private int detailUpdater;

        #region current selection to be passed a startup parameters
        // Base items
        internal Folder SelectedFolder => (Folder)comboBoxFolder.SelectedItem;
        internal Route SelectedRoute => (Route)comboBoxRoute.SelectedItem;

        // Activity mode items
        internal Activity SelectedActivity => (Activity)comboBoxActivity.SelectedItem;
        internal Consist SelectedConsist => (Consist)comboBoxConsist.SelectedItem;
        internal Path SelectedPath => (Path)comboBoxHeadTo.SelectedItem;
        internal string SelectedStartTime => comboBoxStartTime.Text;

        // Timetable mode items
        internal TimetableInfo SelectedTimetableSet => (TimetableInfo)comboBoxTimetableSet.SelectedItem;
        internal TimetableFile SelectedTimetable => (TimetableFile)comboBoxTimetable.SelectedItem;
        internal TrainInformation SelectedTimetableTrain => (TrainInformation)comboBoxTimetableTrain.SelectedItem;
        internal int SelectedTimetableDay => initialized ? (comboBoxTimetableDay.SelectedItem as ComboBoxItem<int>).Key : 0;
        internal WeatherFileInfo SelectedWeatherFile => (WeatherFileInfo)comboBoxTimetableWeatherFile.SelectedItem;
        internal Consist SelectedTimetableConsist { get; private set; }
        internal Path SelectedTimetablePath { get; private set; }

        // Shared items
        internal SeasonType SelectedStartSeason => initialized ? (radioButtonModeActivity.Checked ? (comboBoxStartSeason.SelectedItem as ComboBoxItem<SeasonType>).Key : (comboBoxTimetableSeason.SelectedItem as ComboBoxItem<SeasonType>).Key) : SeasonType.Spring;
        internal WeatherType SelectedStartWeather => initialized ? (radioButtonModeActivity.Checked ? (comboBoxStartWeather.SelectedItem as ComboBoxItem<WeatherType>).Key : (comboBoxTimetableWeather.SelectedItem as ComboBoxItem<WeatherType>).Key) : WeatherType.Clear;

        internal string SelectedSaveFile { get; private set; }
        internal UserAction SelectedAction { get; private set; }
        #endregion

        private ICatalog catalog;
        private readonly ObjectPropertiesStore store = new ObjectPropertiesStore();

        #region Main Form
        public MainForm()
        {
            InitializeComponent();

            // Set title to show revision or build info.
            Text = $"{RuntimeInfo.ProductName} {VersionInfo.Version}";
#if DEBUG
            Text += " (debug)";
#endif
            panelModeTimetable.Location = panelModeActivity.Location;
            UpdateEnabled();
            using (Icon icon = new Icon(SystemIcons.Shield, SystemInformation.SmallIconSize))
                elevationIcon = icon.ToBitmap();

            CatalogManager.SetCatalogDomainPattern(CatalogDomainPattern.AssemblyName, null, RuntimeInfo.LocalesFolder);
        }

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();

                ctsRouteLoading?.Dispose();
                ctsActivityLoading?.Dispose();
                ctsConsistLoading?.Dispose();
                ctsPathLoading?.Dispose(); ;
                ctsTimeTableLoading?.Dispose();
                elevationIcon?.Dispose();
                updateManager?.Dispose();
            }
            base.Dispose(disposing);
        }

        private async void MainForm_Shown(object sender, EventArgs e)
        {
            IEnumerable<string> options = Environment.GetCommandLineArgs().
                Where(a => a.StartsWith("-", StringComparison.OrdinalIgnoreCase) || a.StartsWith("/", StringComparison.OrdinalIgnoreCase)).Select(a => a.Substring(1));
            settings = new UserSettings(options);

            updateManager = new UpdateManager(settings);

            List<Task> initTasks = new List<Task>
            {
                LoadFolderListAsync()
            };

            linkLabelUpdate.Visible = false;
            LoadOptions();
            LoadLanguage();

            if (!initialized)
            {
                initTasks.Add(CheckForUpdateAsync());
                LoadToolsAndDocuments();

                comboBoxStartSeason.DataSourceFromEnum<SeasonType>();
                comboBoxStartWeather.DataSourceFromEnum<WeatherType>();
                comboBoxDifficulty.DataSourceFromEnum<Difficulty>();
                comboBoxTimetableSeason.DataSourceFromEnum<SeasonType>();
                comboBoxTimetableWeather.DataSourceFromEnum<WeatherType>();
                comboBoxTimetableDay.DataSourceFromList<int>(Enumerable.Range(0, 7), (day) => CultureInfo.CurrentUICulture.DateTimeFormat.DayNames[day]);
            }

            ShowEnvironment();
            ShowTimetableEnvironment();

            await Task.WhenAll(initTasks).ConfigureAwait(true);
            initialized = true;

        }

        private static IEnumerable<ToolStripItem> LoadTools()
        {
            return Directory.EnumerateFiles(System.IO.Path.GetDirectoryName(RuntimeInfo.ApplicationFolder), "*.exe").
                Where(fileName => (!coreExecutables.Contains(System.IO.Path.GetFileName(fileName), StringComparer.InvariantCultureIgnoreCase))).
                Select(fileName =>
                {
                    FileVersionInfo toolInfo = FileVersionInfo.GetVersionInfo(fileName);
                    // Skip any executable that isn't part of this product (e.g. Visual Studio hosting files).
                    if (toolInfo.ProductName != RuntimeInfo.ProductName)
                        return null;
                    // Remove the product name from the tool's name
                    string toolName = string.Join(" ", toolInfo.Comments.Split(' ').Except(RuntimeInfo.ProductName.Split(' ')));
                    return new ToolStripMenuItem(toolName, null, (object sender2, EventArgs e2) =>
                    {
                        string toolPath = (sender2 as ToolStripItem).Tag as string;
                        bool toolIsConsole = false;
                        using (BinaryReader reader = new BinaryReader(File.OpenRead(toolPath)))
                        {
                            toolIsConsole = GetImageSubsystem(reader) == ImageSubsystem.WindowsConsole;
                        }
                        if (toolIsConsole)
                            Process.Start("cmd", $"/k \"{toolPath}\"");
                        else
                            Process.Start(toolPath);
                    }
                    )
                    { Tag = fileName };
                }).Where(t => t != null);
        }

        private static IEnumerable<ToolStripItem> LoadDocuments()
        {
            if (Directory.Exists(RuntimeInfo.DocumentationFolder))
            {
                return Directory.EnumerateFiles(RuntimeInfo.DocumentationFolder).
                    Union(Directory.Exists(System.IO.Path.Combine(RuntimeInfo.DocumentationFolder, CultureInfo.CurrentUICulture.Name)) ?
                        Directory.EnumerateFiles(System.IO.Path.Combine(RuntimeInfo.DocumentationFolder, CultureInfo.CurrentUICulture.Name)) : Array.Empty<string>()).
                    Union(Directory.Exists(System.IO.Path.Combine(RuntimeInfo.DocumentationFolder, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)) ?
                        Directory.EnumerateFiles(System.IO.Path.Combine(RuntimeInfo.DocumentationFolder, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)) : Array.Empty<string>()).
                    Where(fileName => documentFiles.Contains(System.IO.Path.GetExtension(fileName), StringComparer.InvariantCultureIgnoreCase)).
                    Select(fileName =>
                    {
                        return new ToolStripMenuItem(System.IO.Path.GetFileName(fileName), null, (object sender2, EventArgs e2) =>
                        {
                            string docPath = (sender2 as ToolStripItem).Tag as string;
                            Process.Start(new ProcessStartInfo { FileName = docPath, UseShellExecute = true });
                        })
                        { Tag = fileName };
                    }).Where(d => d != null);
            }
            else
                return Array.Empty<ToolStripItem>().AsEnumerable();
        }

        private void LoadToolsAndDocuments()
        {
            contextMenuStripTools.Items.Clear();
            contextMenuStripDocuments.Items.Clear();
            contextMenuStripTools.Items.Add(testingToolStripMenuItem);
            contextMenuStripTools.Items.AddRange(LoadTools().OrderBy(tool => tool.Text).ToArray());
            contextMenuStripDocuments.Items.AddRange(LoadDocuments().OrderBy(doc => doc.Text).ToArray());
            // Documents button will be disabled if Documentation folder is not present.
            buttonDocuments.Enabled = contextMenuStripDocuments.Items.Count > 0;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveOptions();
            if (null != ctsRouteLoading && !ctsRouteLoading.IsCancellationRequested)
                ctsRouteLoading.Cancel();
            if (null != ctsActivityLoading && !ctsActivityLoading.IsCancellationRequested)
                ctsActivityLoading.Cancel();
            if (null != ctsConsistLoading && !ctsConsistLoading.IsCancellationRequested)
                ctsConsistLoading.Cancel();
            if (null != ctsPathLoading && !ctsPathLoading.IsCancellationRequested)
                ctsPathLoading.Cancel();
            if (null != ctsTimeTableLoading && !ctsPathLoading.IsCancellationRequested)
                ctsTimeTableLoading.Cancel();

            // Remove any deleted saves
            if (Directory.Exists(UserSettings.DeletedSaveFolder))
                Directory.Delete(UserSettings.DeletedSaveFolder, true);   // true removes all contents as well as folder
        }

        private async Task CheckForUpdateAsync()
        {
            string availableVersion = await updateManager.GetBestAvailableVersionString(false).ConfigureAwait(true);
            if (updateManager.LastCheckError != null)
            {
                linkLabelUpdate.Text = catalog.GetString("Update check failed");
                linkLabelUpdate.Visible = true;
                linkLabelUpdate.Tag = null;
            }
            else
            {
                if (!string.IsNullOrEmpty(availableVersion))
                {
                    linkLabelUpdate.Text = catalog.GetString($"Update to {UpdateManager.NormalizedPackageVersion(availableVersion)}");
                    linkLabelUpdate.Tag = availableVersion;
                    linkLabelUpdate.Visible = true;
                    linkLabelUpdate.Image = updateManager.UpdaterNeedsElevation ? elevationIcon : null;
                    linkLabelUpdate.AutoSize = true;
                    linkLabelUpdate.Left = panelDetails.Right - linkLabelUpdate.Width - elevationIcon.Width;
                    linkLabelUpdate.AutoSize = false;
                    linkLabelUpdate.Width = panelDetails.Right - linkLabelUpdate.Left;
                }
                else
                {
                    linkLabelUpdate.Visible = false;
                }
            }
        }

        private void LoadLanguage()
        {
            Localizer.Revert(this, store);
            CatalogManager.Reset();

            if (!string.IsNullOrEmpty(settings.Language))
            {
                try
                {
                    CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(settings.Language);
                }
                catch (CultureNotFoundException exception)
                {
                    Trace.WriteLine(exception.Message);
                }
            }
            else 
            {
                CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InstalledUICulture;
            }
            catalog = CatalogManager.Catalog;
            Localizer.Localize(this, catalog, store);
        }
        #endregion

        #region Folders
        private async void ComboBoxFolder_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                await Task.WhenAll(LoadRouteListAsync(), LoadLocomotiveListAsync()).ConfigureAwait(true);
            }
            catch (TaskCanceledException) { }
        }
        #endregion

        #region Routes
        private async void ComboBoxRoute_SelectedIndexChanged(object sender, EventArgs e)
        {
            int updater = Interlocked.CompareExchange(ref detailUpdater, 1, 0);
            try
            {
                await Task.WhenAll(
                    LoadActivityListAsync(),
                    LoadStartAtListAsync(),
                    LoadTimetableSetListAsync()).ConfigureAwait(true);
            }
            catch (TaskCanceledException) { }
            if (updater == 0)
            {
                ShowDetails();
                detailUpdater = 0;
            }
        }
        #endregion

        #region Mode
        private void RadioButtonMode_CheckedChanged(object sender, EventArgs e)
        {
            int updater = Interlocked.CompareExchange(ref detailUpdater, 1, 0);
            panelModeActivity.Visible = radioButtonModeActivity.Checked;
            panelModeTimetable.Visible = radioButtonModeTimetable.Checked;
            UpdateEnabled();
            if (updater == 0)
            {
                ShowDetails();
                detailUpdater = 0;
            }
        }
        #endregion

        #region Activities
        private void ComboBoxActivity_SelectedIndexChanged(object sender, EventArgs e)
        {
            int updater = Interlocked.CompareExchange(ref detailUpdater, 1, 0);
            ShowLocomotiveList();
            ShowConsistList();
            ShowStartAtList();
            ShowEnvironment();
            if (updater == 0)
            {
                ShowDetails();
                detailUpdater = 0;
            }
            //Debrief Activity Eval
            //0 = "- Explore route -"
            //1 = "+ Explore in Activity mode +"
            if (comboBoxActivity.SelectedIndex < 2)
            {
                checkDebriefActivityEval.Checked = false;
                checkDebriefActivityEval.Enabled = false;
            }
            else
            {
                checkDebriefActivityEval.Enabled = true;
            }
        }
        #endregion

        #region Locomotives
        private void ComboBoxLocomotive_SelectedIndexChanged(object sender, EventArgs e)
        {
            ShowConsistList();
        }
        #endregion

        #region Consists
        private void ComboBoxConsist_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateExploreActivity(true);
        }
        #endregion

        #region Starting from
        private void ComboBoxStartAt_SelectedIndexChanged(object sender, EventArgs e)
        {
            ShowHeadToList();
        }
        #endregion

        #region Heading to
        private void ComboBoxHeadTo_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateExploreActivity(true);
        }
        #endregion

        #region Environment
        private void ComboBoxStartTime_TextChanged(object sender, EventArgs e)
        {
            UpdateExploreActivity(false);
        }

        private void ComboBoxStartSeason_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateExploreActivity(false);
        }

        private void ComboBoxStartWeather_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateExploreActivity(false);
        }
        #endregion

        #region Timetable Sets
        private void ComboBoxTimetableSet_SelectedIndexChanged(object sender, EventArgs e)
        {
            int updater = Interlocked.CompareExchange(ref detailUpdater, 1, 0);
            UpdateTimetableSet();
            ShowTimetableList();
            if (updater == 0)
            {
                ShowDetails();
                detailUpdater = 0;
            }
        }
        #endregion

        #region Timetables
        private void ComboBoxTimetable_selectedIndexChanged(object sender, EventArgs e)
        {
            int updater = Interlocked.CompareExchange(ref detailUpdater, 1, 0);
            ShowTimetableTrainList();
            if (updater == 0)
            {
                ShowDetails();
                detailUpdater = 0;
            }
        }
        #endregion

        #region Timetable Trains
        private void ComboBoxTimetableTrain_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBoxTimetableTrain.SelectedItem is TrainInformation selectedTrain)
            {
                int updater = Interlocked.CompareExchange(ref detailUpdater, 1, 0);
                SelectedTimetableConsist = Consist.GetConsist(SelectedFolder, selectedTrain.LeadingConsist, selectedTrain.ReverseConsist);
                Path path = Path.GetPath(SelectedRoute, selectedTrain.Path);
                SelectedTimetablePath = path.IsPlayerPath ? path : null;

                if (updater == 0)
                {
                    ShowDetails();
                    detailUpdater = 0;
                }
            }
        }
        #endregion

        #region Timetable environment
        private void ComboBoxTimetableDay_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateTimetableSet();
        }

        private void ComboBoxTimetableSeason_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateTimetableSet();
        }

        private void ComboBoxTimetableWeather_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateTimetableSet();
        }

        private void ComboBoxTimetableWeatherFile_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateTimetableWeatherSet();
        }
        #endregion

        #region Multiplayer
        private void TextBoxMPUser_TextChanged(object sender, EventArgs e)
        {
            UpdateEnabled();
        }

        private bool CheckUserName(string text)
        {
            Match match = Regex.Match(text, @"^[a-zA-Z_][a-zA-Z0-9_]{3,9}$");//4-10 characters, digits or _, not starting with a digit
            if (!match.Success)
            {
                MessageBox.Show(catalog.GetString("User name must be 4-10 characters (chars, digits, _) long, cannot contain space, ', \" or - and must not start with a digit."), RuntimeInfo.ProductName);
                return false;
            }
            return true;
        }

        #endregion

        #region Misc. buttons and options
        private async void LinkLabelUpdate_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (updateManager.LastCheckError != null)
            {
                MessageBox.Show(catalog.GetString($"The update check failed due to an error:\n\n{updateManager.LastCheckError.Message} {updateManager.LastCheckError.InnerException?.Message}"), RuntimeInfo.ProductName);
                return;
            }

            try
            {
                await updateManager.RunUpdateProcess(linkLabelUpdate.Tag as string).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                MessageBox.Show(catalog.GetString($"The update failed due to an error:\n\n{exception.Message} {exception.InnerException?.Message}"), RuntimeInfo.ProductName);
                return;
                throw;
            }
        }

        private void ButtonTools_Click(object sender, EventArgs e)
        {
            contextMenuStripTools.Show(buttonTools, new Point(0, buttonTools.ClientSize.Height), ToolStripDropDownDirection.Default);
        }

        private void ButtonDocuments_Click(object sender, EventArgs e)
        {
            contextMenuStripDocuments.Show(buttonDocuments, new Point(0, buttonDocuments.ClientSize.Height), ToolStripDropDownDirection.Default);
        }

        private void TestingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (TestingForm form = new TestingForm(settings, RuntimeInfo.ActivityRunnerExecutable))
            {
                form.ShowDialog(this);
            }
        }

        private async void ButtonOptions_Click(object sender, EventArgs e)
        {
            SaveOptions();

            using (OptionsForm form = new OptionsForm(settings, updateManager, false))
            {
                switch (form.ShowDialog(this))
                {
                    case DialogResult.OK:
                        await Task.WhenAll(LoadFolderListAsync(), CheckForUpdateAsync()).ConfigureAwait(true);
                        break;
                    case DialogResult.Retry: //Language has changed
                        LoadLanguage();
                        LoadToolsAndDocuments();
                        break;
                }
            }
        }

        private void ButtonStart_Click(object sender, EventArgs e)
        {
            SaveOptions();

            if (radioButtonModeActivity.Checked)
            {
                SelectedAction = UserAction.SingleplayerNewGame;
                if (SelectedActivity != null)
                    DialogResult = DialogResult.OK;
            }
            else
            {
                SelectedAction = UserAction.SinglePlayerTimetableGame;
                if (SelectedTimetableTrain != null)
                    DialogResult = DialogResult.OK;
            }
        }

        private void ButtonResume_Click(object sender, EventArgs e)
        {
            OpenResumeForm(false);
        }

        private void ButtonResumeMP_Click(object sender, EventArgs e)
        {
            OpenResumeForm(true);
        }

        private void OpenResumeForm(bool multiplayer)
        {
            if (radioButtonModeTimetable.Checked)
            {
                SelectedAction = UserAction.SinglePlayerTimetableGame;
            }
            else if (!multiplayer)
            {
                SelectedAction = UserAction.SingleplayerNewGame;
            }
            else if (radioButtonMPClient.Checked)
            {
                SelectedAction = UserAction.MultiplayerClient;
            }
            else
                SelectedAction = UserAction.MultiplayerServer;

            // if timetable mode but no timetable selected - no action
            if (SelectedAction == UserAction.SinglePlayerTimetableGame && (SelectedTimetableSet == null || multiplayer))
            {
                return;
            }

            using (ResumeForm form = new ResumeForm(settings, SelectedRoute, SelectedAction, SelectedActivity, SelectedTimetableSet, routes))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    SaveOptions();
                    SelectedSaveFile = form.SelectedSaveFile;
                    SelectedAction = form.SelectedAction;
                    DialogResult = DialogResult.OK;
                }
            }
        }

        private void ButtonStartMP_Click(object sender, EventArgs e)
        {
            if (!CheckUserName(textBoxMPUser.Text))
                return;
            SaveOptions();
            SelectedAction = radioButtonMPClient.Checked ? UserAction.MultiplayerClient : UserAction.MultiplayerServer;
            DialogResult = DialogResult.OK;
        }

        #endregion

        #region Options
        private void LoadOptions()
        {
            checkBoxWarnings.Checked = settings.Logging;
            //Debrief activity evaluation
            checkDebriefActivityEval.Checked = settings.DebriefActivityEval;
            //TO DO: Debrief TTactivity evaluation
            //checkDebriefTTActivityEval.Checked = Settings.DebriefTTActivityEval;

            textBoxMPUser.Text = settings.Multiplayer_User;
            textBoxMPHost.Text = settings.Multiplayer_Host + ":" + settings.Multiplayer_Port;
        }

        private void SaveOptions()
        {
            settings.Logging = checkBoxWarnings.Checked;
            settings.Multiplayer_User = textBoxMPUser.Text;
            //Debrief activity evaluation
            settings.DebriefActivityEval = checkDebriefActivityEval.Checked;
            //TO DO: Debrief TTactivity evaluation
            //Settings.DebriefTTActivityEval = checkDebriefTTActivityEval.Checked;

            string[] mpHost = textBoxMPHost.Text.Split(':');
            settings.Multiplayer_Host = mpHost[0];
            if (mpHost.Length > 1 && int.TryParse(mpHost[1], out int port))
            {
                settings.Multiplayer_Port = port;
            }
            else
            {
                settings.Multiplayer_Port = (int)settings.GetDefaultValue("Multiplayer_Port");
            }
            settings.Menu_Selection = new[] {
                // Base items
                SelectedFolder?.Path ?? string.Empty,
                SelectedRoute?.Path ?? string.Empty,
                // Activity mode items / Explore mode items
                radioButtonModeActivity.Checked ? SelectedActivity?.FilePath ?? string.Empty : SelectedTimetableSet?.FileName ?? string.Empty,
                radioButtonModeActivity.Checked ?
                    SelectedActivity is ExploreActivity && (comboBoxLocomotive.SelectedItem as Locomotive)?.FilePath != null ? (comboBoxLocomotive.SelectedItem as Locomotive).FilePath : string.Empty :
                    SelectedTimetable?.Description ?? string.Empty,
                radioButtonModeActivity.Checked ?
                    SelectedActivity is ExploreActivity && SelectedConsist != null ? SelectedConsist.FilePath : string.Empty :
                    SelectedTimetableTrain?.Column.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                radioButtonModeActivity.Checked ?
                    SelectedActivity is ExploreActivity && SelectedPath != null ? SelectedPath.FilePath : string.Empty : SelectedTimetableDay.ToString(CultureInfo.InvariantCulture),
                radioButtonModeActivity.Checked ?
                    SelectedActivity is ExploreActivity ? SelectedStartTime : string.Empty : string.Empty,
                // Shared items
                radioButtonModeActivity.Checked ?
                    SelectedActivity is ExploreActivity ? SelectedStartSeason.ToString() : string.Empty : SelectedStartSeason.ToString(),
                radioButtonModeActivity.Checked ?
                    SelectedActivity is ExploreActivity ? SelectedStartWeather.ToString() : string.Empty : SelectedStartWeather.ToString(),
            };
            settings.Save();
        }
        #endregion

        #region Enabled state
        private void UpdateEnabled()
        {
            comboBoxFolder.Enabled = comboBoxFolder.Items.Count > 0;
            comboBoxRoute.Enabled = comboBoxRoute.Items.Count > 0;
            comboBoxActivity.Enabled = comboBoxActivity.Items.Count > 0;
            comboBoxLocomotive.Enabled = comboBoxLocomotive.Items.Count > 0 && SelectedActivity is ExploreActivity;
            comboBoxConsist.Enabled = comboBoxConsist.Items.Count > 0 && SelectedActivity is ExploreActivity;
            comboBoxStartAt.Enabled = comboBoxStartAt.Items.Count > 0 && SelectedActivity is ExploreActivity;
            comboBoxHeadTo.Enabled = comboBoxHeadTo.Items.Count > 0 && SelectedActivity is ExploreActivity;
            comboBoxStartTime.Enabled = comboBoxStartSeason.Enabled = comboBoxStartWeather.Enabled = SelectedActivity is ExploreActivity;
            comboBoxStartTime.DropDownStyle = SelectedActivity is ExploreActivity ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
            comboBoxTimetable.Enabled = comboBoxTimetableSet.Items.Count > 0;
            comboBoxTimetableTrain.Enabled = comboBoxTimetable.Items.Count > 0;
            comboBoxTimetableWeatherFile.Enabled = comboBoxTimetableWeatherFile.Items.Count > 0;
            //Avoid to Start with a non valid Activity/Locomotive/Consist.
            buttonResume.Enabled = buttonStart.Enabled = radioButtonModeActivity.Checked &&
                comboBoxActivity.Text.Length > 0 && comboBoxActivity.Text[0] != '<' && comboBoxLocomotive.Text.Length > 0 && comboBoxLocomotive.Text[0] != '<' ?
                SelectedActivity != null && (!(SelectedActivity is ExploreActivity) || (comboBoxConsist.Items.Count > 0 && comboBoxHeadTo.Items.Count > 0)) :
                SelectedTimetableTrain != null;
            buttonResumeMP.Enabled = buttonStartMP.Enabled = buttonStart.Enabled && !string.IsNullOrEmpty(textBoxMPUser.Text) && !string.IsNullOrEmpty(textBoxMPHost.Text);
        }
        #endregion

        #region Folder list
        private async Task LoadFolderListAsync()
        {
            try
            {
                folders = (await Folder.GetFolders(settings.FolderSettings.Folders).ConfigureAwait(true)).OrderBy(f => f.Name);
            }
            catch (TaskCanceledException)
            {
                folders = Array.Empty<Folder>();
            }

            ShowFolderList();
            if (folders.Any())
                comboBoxFolder.Focus();

            if (!initialized && !folders.Any())
            {
                using (OptionsForm form = new OptionsForm(settings, updateManager, true))
                {
                    switch (form.ShowDialog(this))
                    {
                        case DialogResult.OK:
                            await LoadFolderListAsync().ConfigureAwait(true);
                            break;
                        case DialogResult.Retry:
                            LoadLanguage();
                            LoadToolsAndDocuments();
                            break;
                    }
                }
            }
        }

        private void ShowFolderList()
        {
            try
            {
                comboBoxFolder.BeginUpdate();
                comboBoxFolder.Items.Clear();
                comboBoxFolder.Items.AddRange(folders.ToArray());
            }
            finally
            {
                comboBoxFolder.EndUpdate();
            }
            UpdateFromMenuSelection<Folder>(comboBoxFolder, MenuSelectionIndex.Folder, f => f.Path);
            UpdateEnabled();
        }
        #endregion

        #region Route list
        private async Task LoadRouteListAsync()
        {
            lock (routes)
            {
                if (ctsRouteLoading != null && !ctsRouteLoading.IsCancellationRequested)
                    ctsRouteLoading.Cancel();
                ctsRouteLoading = ResetCancellationTokenSource(ctsRouteLoading);
            }
            paths = Array.Empty<Path>();
            activities = Array.Empty<Activity>();

            Folder selectedFolder = SelectedFolder;
            try
            {
                routes = (await Route.GetRoutes(selectedFolder, ctsRouteLoading.Token).ConfigureAwait(true)).OrderBy(r => r.Name);
            }
            catch (TaskCanceledException)
            {
                routes = Array.Empty<Route>();
            }
            //cleanout existing data
            ShowRouteList();
            ShowActivityList();
            ShowStartAtList();
            ShowHeadToList();

        }

        private void ShowRouteList()
        {
            try
            {
                comboBoxRoute.BeginUpdate();
                comboBoxRoute.Items.Clear();
                comboBoxRoute.Items.AddRange(routes.ToArray());
            }
            finally
            {
                comboBoxRoute.EndUpdate();
            }
            UpdateFromMenuSelection<Route>(comboBoxRoute, MenuSelectionIndex.Route, r => r.Path);
            if (settings.Menu_Selection.Length > (int)MenuSelectionIndex.Activity)
            {
                string path = settings.Menu_Selection[(int)MenuSelectionIndex.Activity]; // Activity or Timetable
                string extension = System.IO.Path.GetExtension(path);
                if (".act".Equals(extension, StringComparison.OrdinalIgnoreCase))
                    radioButtonModeActivity.Checked = true;
                else if (".timetable_or".Equals(extension, StringComparison.OrdinalIgnoreCase))
                    radioButtonModeTimetable.Checked = true;
            }
            UpdateEnabled();
        }
        #endregion

        #region Activity list
        private async Task LoadActivityListAsync()
        {
            lock (activities)
            {
                if (ctsActivityLoading != null && !ctsActivityLoading.IsCancellationRequested)
                    ctsActivityLoading.Cancel();
                ctsActivityLoading = ResetCancellationTokenSource(ctsActivityLoading);
            }
            //            ShowActivityList();

            Folder selectedFolder = SelectedFolder;
            Route selectedRoute = SelectedRoute;
            try
            {
                activities = (await Activity.GetActivities(selectedFolder, selectedRoute, ctsActivityLoading.Token).ConfigureAwait(true)).OrderBy(a => a.Name);
            }
            catch (TaskCanceledException)
            {
                activities = Array.Empty<Activity>();
            }
            ShowActivityList();
        }

        private void ShowActivityList()
        {
            try
            {
                comboBoxActivity.BeginUpdate();
                comboBoxActivity.Items.Clear();
                comboBoxActivity.Items.AddRange(activities.ToArray());
            }
            finally
            {
                comboBoxActivity.EndUpdate();
            }
            UpdateFromMenuSelection<Activity>(comboBoxActivity, MenuSelectionIndex.Activity, a => a.FilePath);
            UpdateEnabled();
        }

        private void UpdateExploreActivity(bool updateDetails)
        {
            int updater = Interlocked.CompareExchange(ref detailUpdater, 1, 0);
            (SelectedActivity as ExploreActivity)?.UpdateActivity(SelectedStartTime, (SeasonType)SelectedStartSeason, (WeatherType)SelectedStartWeather, SelectedConsist, SelectedPath);
            if (updater == 0)
            {
                if (updateDetails)
                    ShowDetails();
                detailUpdater = 0;
            }
        }
        #endregion

        #region Consist lists
        private async Task LoadLocomotiveListAsync()
        {
            lock (consists)
            {
                if (ctsConsistLoading != null && !ctsConsistLoading.IsCancellationRequested)
                    ctsConsistLoading.Cancel();
                ctsConsistLoading = ResetCancellationTokenSource(ctsConsistLoading);
            }

            Folder selectedFolder = SelectedFolder;
            try
            {
                consists = (await Consist.GetConsists(selectedFolder, ctsConsistLoading.Token).ConfigureAwait(true)).OrderBy(c => c.Name);
            }
            catch (TaskCanceledException)
            {
                consists = Array.Empty<Consist>();
            }
            ShowLocomotiveList();
        }

        private void ShowLocomotiveList()
        {
            if (SelectedActivity == null || SelectedActivity is ExploreActivity)
            {
                try
                {
                    comboBoxLocomotive.BeginUpdate();
                    comboBoxLocomotive.Items.Clear();
                    comboBoxLocomotive.Items.Add(Locomotive.Any);
                    comboBoxLocomotive.Items.AddRange(consists.Where(c => c.Locomotive != null).Select(c => c.Locomotive).Distinct().OrderBy(l => l.Name).ToArray());
                    if (comboBoxLocomotive.Items.Count == 1)
                        comboBoxLocomotive.Items.Clear();
                }
                finally
                {
                    comboBoxLocomotive.EndUpdate();
                }
                UpdateFromMenuSelection<Locomotive>(comboBoxLocomotive, MenuSelectionIndex.Locomotive, l => l.FilePath);
            }
            else
            {
                try
                {
                    comboBoxLocomotive.BeginUpdate();
                    comboBoxConsist.BeginUpdate();
                    Consist consist = SelectedActivity.Consist;
                    comboBoxLocomotive.Items.Clear();
                    comboBoxLocomotive.Items.Add(consist.Locomotive);
                    comboBoxLocomotive.SelectedIndex = 0;
                    comboBoxConsist.Items.Clear();
                    comboBoxConsist.Items.Add(consist);
                    comboBoxConsist.SelectedIndex = 0;
                }
                finally
                {
                    comboBoxLocomotive.EndUpdate();
                    comboBoxConsist.EndUpdate();
                }
            }
            UpdateEnabled();
        }

        private void ShowConsistList()
        {
            if (SelectedActivity == null || SelectedActivity is ExploreActivity)
            {
                try
                {
                    comboBoxConsist.BeginUpdate();
                    comboBoxConsist.Items.Clear();
                    comboBoxConsist.Items.AddRange(consists.Where(c => comboBoxLocomotive.SelectedItem.Equals(c.Locomotive)).OrderBy(c => c.Name).ToArray());
                }
                finally
                {
                    comboBoxConsist.EndUpdate();
                }
                UpdateFromMenuSelection<Consist>(comboBoxConsist, MenuSelectionIndex.Consist, c => c.FilePath);
            }
            UpdateEnabled();
        }
        #endregion

        #region Path lists
        private async Task LoadStartAtListAsync()
        {
            lock (paths)
            {
                if (ctsPathLoading != null && !ctsPathLoading.IsCancellationRequested)
                    ctsPathLoading.Cancel();
                ctsPathLoading = ResetCancellationTokenSource(ctsPathLoading);
            }

            ShowStartAtList();
            ShowHeadToList();

            Route selectedRoute = SelectedRoute;
            try
            {
                paths = (await Path.GetPaths(selectedRoute, false, ctsPathLoading.Token).ConfigureAwait(true)).OrderBy(a => a.ToString());
            }
            catch (TaskCanceledException)
            {
                paths = Array.Empty<Path>();
            }
            if (SelectedActivity == null || SelectedActivity is ExploreActivity)
                ShowStartAtList();
        }

        private void ShowStartAtList()
        {
            if (SelectedActivity == null || SelectedActivity is ExploreActivity)
            {
                try
                {
                    comboBoxStartAt.BeginUpdate();
                    comboBoxStartAt.Items.Clear();
                    comboBoxStartAt.Items.AddRange(paths.Select(p => p.Start).Distinct().OrderBy(s => s.ToString()).ToArray());
                }
                finally
                {
                    comboBoxStartAt.EndUpdate();
                }
                // Because this list is unique names, we have to do some extra work to select it.
                if (settings.Menu_Selection.Length >= (int)MenuSelectionIndex.Path)
                {
                    string pathFilePath = settings.Menu_Selection[(int)MenuSelectionIndex.Path];
                    Path path = paths.FirstOrDefault(p => p.FilePath == pathFilePath);
                    if (path != null)
                        SetComboBoxItem<string>(comboBoxStartAt, s => s == path.Start);
                    else if (comboBoxStartAt.Items.Count > 0)
                        comboBoxStartAt.SelectedIndex = 0;
                }
            }
            else
            {
                try
                {
                    comboBoxStartAt.BeginUpdate();
                    comboBoxHeadTo.BeginUpdate();
                    Path path = SelectedActivity.Path;
                    comboBoxStartAt.Items.Clear();
                    comboBoxStartAt.Items.Add(path.Start);
                    comboBoxHeadTo.Items.Clear();
                    comboBoxHeadTo.Items.Add(path);
                }
                finally
                {
                    comboBoxStartAt.EndUpdate();
                    comboBoxHeadTo.EndUpdate();
                }
                comboBoxStartAt.SelectedIndex = 0;
                comboBoxHeadTo.SelectedIndex = 0;
            }
            UpdateEnabled();
        }

        private void ShowHeadToList()
        {
            if (SelectedActivity == null || SelectedActivity is ExploreActivity)
            {
                try
                {
                    comboBoxHeadTo.BeginUpdate();
                    comboBoxHeadTo.Items.Clear();
                    comboBoxHeadTo.Items.AddRange(paths.Where(p => p.Start == (string)comboBoxStartAt.SelectedItem).ToArray());
                }
                finally
                {
                    comboBoxHeadTo.EndUpdate();
                }
                UpdateFromMenuSelection<Path>(comboBoxHeadTo, MenuSelectionIndex.Path, c => c.FilePath);
            }
            UpdateEnabled();
        }
        #endregion

        #region Environment
        private void ShowEnvironment()
        {
            if (SelectedActivity == null || SelectedActivity is ExploreActivity)
            {
                try
                {
                    comboBoxStartTime.BeginUpdate();
                    comboBoxDuration.BeginUpdate();
                    comboBoxStartTime.Items.Clear();
                    foreach (int hour in Enumerable.Range(0, 24))
                        comboBoxStartTime.Items.Add($"{hour}:00");
                    comboBoxDuration.Items.Clear();
                    comboBoxDuration.Items.Add("");
                }
                finally
                {
                    comboBoxStartTime.EndUpdate();
                    comboBoxDuration.EndUpdate();
                }

                UpdateFromMenuSelection(comboBoxStartTime, MenuSelectionIndex.Time, "12:00");
                UpdateFromMenuSelectionComboBoxItem(comboBoxStartSeason, MenuSelectionIndex.Season, SeasonType.Summer);
                UpdateFromMenuSelectionComboBoxItem(comboBoxStartWeather, MenuSelectionIndex.Weather, WeatherType.Clear);
                comboBoxDifficulty.SelectedIndex = -1;
                comboBoxDuration.SelectedIndex = 0;
            }
            else
            {
                try
                {
                    comboBoxStartTime.BeginUpdate();
                    comboBoxDuration.BeginUpdate();

                    comboBoxStartTime.Items.Clear();
                    comboBoxStartTime.Items.Add(SelectedActivity.StartTime.ToString());
                    comboBoxDuration.Items.Clear();
                    comboBoxDuration.Items.Add(SelectedActivity.Duration.ToString(@"hh\:mm", CultureInfo.InvariantCulture));

                }
                finally
                {
                    comboBoxStartTime.EndUpdate();
                    comboBoxDuration.EndUpdate();
                }
                comboBoxStartTime.SelectedIndex = 0;
                comboBoxStartSeason.SelectedValue = SelectedActivity.Season;
                comboBoxStartWeather.SelectedValue = SelectedActivity.Weather;
                comboBoxDifficulty.SelectedValue = SelectedActivity.Difficulty;
                comboBoxDuration.SelectedIndex = 0;
            }
        }
        #endregion

        #region Timetable Set list
        private async Task LoadTimetableSetListAsync()
        {
            lock (timetableSets)
            {
                if (ctsTimeTableLoading != null && !ctsTimeTableLoading.IsCancellationRequested)
                    ctsTimeTableLoading.Cancel();
                ctsTimeTableLoading = ResetCancellationTokenSource(ctsTimeTableLoading);
            }

            ShowTimetableSetList();

            Folder selectedFolder = SelectedFolder;
            Route selectedRoute = SelectedRoute;
            try
            {
                timetableSets = (await TimetableInfo.GetTimetableInfo(selectedRoute, ctsTimeTableLoading.Token).ConfigureAwait(true)).OrderBy(tt => tt.Description);
                timetableWeatherFileSet = (await WeatherFileInfo.GetTimetableWeatherFiles(selectedRoute, ctsTimeTableLoading.Token).ConfigureAwait(true)).OrderBy(a => a.ToString());
            }
            catch (TaskCanceledException)
            {
                timetableSets = Array.Empty<TimetableInfo>();
                timetableWeatherFileSet = Array.Empty<WeatherFileInfo>();
            }
            ShowTimetableSetList();
            ShowTimetableWeatherSet();
        }

        private void ShowTimetableSetList()
        {
            try
            {
                comboBoxTimetableSet.BeginUpdate();
                comboBoxTimetableSet.Items.Clear();
                comboBoxTimetableSet.Items.AddRange(timetableSets.ToArray());
            }
            finally
            {
                comboBoxTimetableSet.EndUpdate();
            }
            UpdateFromMenuSelection<TimetableInfo>(comboBoxTimetableSet, MenuSelectionIndex.TimetableSet, t => t.FileName);
            UpdateEnabled();
        }

        private void UpdateTimetableSet()
        {
            if (SelectedTimetableSet != null)
            {
                SelectedTimetableSet.Day = SelectedTimetableDay;
                SelectedTimetableSet.Season = SelectedStartSeason;
                SelectedTimetableSet.Weather = SelectedStartWeather;
            }
        }

        private void ShowTimetableWeatherSet()
        {
            comboBoxTimetableWeatherFile.Items.Clear();
            foreach (WeatherFileInfo weatherFile in timetableWeatherFileSet)
            {
                comboBoxTimetableWeatherFile.Items.Add(weatherFile);
                UpdateEnabled();
            }
        }

        private void UpdateTimetableWeatherSet()
        {
            SelectedTimetableSet.WeatherFile = SelectedWeatherFile.GetFullName();
        }

        #endregion

        #region Timetable list
        private void ShowTimetableList()
        {
            if (null != SelectedTimetableSet)
            {
                try
                {
                    comboBoxTimetable.BeginUpdate();
                    comboBoxTimetable.Items.Clear();
                    comboBoxTimetable.Items.AddRange(SelectedTimetableSet.TimeTables.ToArray());
                }
                finally
                {
                    comboBoxTimetable.EndUpdate();
                }
                UpdateFromMenuSelection<TimetableFile>(comboBoxTimetable, MenuSelectionIndex.Timetable, t => t.Description);
            }
            else
                comboBoxTimetable.Items.Clear();

            UpdateEnabled();
        }
        #endregion

        #region Timetable Train list
        private void ShowTimetableTrainList()
        {
            if (null != SelectedTimetableSet)
            {
                try
                {
                    comboBoxTimetableTrain.BeginUpdate();
                    comboBoxTimetableTrain.Items.Clear();

                    List<TrainInformation> trains = SelectedTimetableSet.TimeTables[comboBoxTimetable.SelectedIndex].Trains;
                    trains.Sort();
                    comboBoxTimetableTrain.Items.AddRange(trains.ToArray());
                }
                finally
                {
                    comboBoxTimetableTrain.EndUpdate();
                }
                UpdateFromMenuSelection<TrainInformation>(comboBoxTimetableTrain, MenuSelectionIndex.Train, t => t.Column.ToString(CultureInfo.InvariantCulture));
            }
            else
                comboBoxTimetableTrain.Items.Clear();

            UpdateEnabled();
        }
        #endregion

        #region Timetable environment
        private void ShowTimetableEnvironment()
        {
            UpdateFromMenuSelectionComboBoxItem(comboBoxTimetableDay, MenuSelectionIndex.Day, 0);
            UpdateFromMenuSelectionComboBoxItem(comboBoxTimetableSeason, MenuSelectionIndex.Season, 1);
            UpdateFromMenuSelectionComboBoxItem(comboBoxTimetableWeather, MenuSelectionIndex.Weather, 0);
        }
        #endregion

        #region Details
        private void ShowDetails()
        {
            ClearDetails();
            if (SelectedRoute != null && SelectedRoute.Description != null)
                AddDetailToShow(catalog.GetString("Route: {0}", SelectedRoute.Name), SelectedRoute.Description);

            if (radioButtonModeActivity.Checked)
            {
                if (SelectedConsist?.Locomotive?.Description != null)
                {
                    AddDetailToShow(catalog.GetString("Locomotive: {0}", SelectedConsist.Locomotive.Name), SelectedConsist.Locomotive.Description);
                }
                if (SelectedActivity?.Description != null)
                {
                    AddDetailToShow(catalog.GetString("Activity: {0}", SelectedActivity.Name), SelectedActivity.Description);
                    AddDetailToShow(catalog.GetString("Activity Briefing"), SelectedActivity.Briefing);
                }
                else if (SelectedPath != null)
                {
                    AddDetailToShow(catalog.GetString("Path: {0}", SelectedPath.Name),
                        string.Join("\n", catalog.GetString("Starting at: {0}", SelectedPath.Start),
                    catalog.GetString("Heading to: {0}", SelectedPath.End)));
                }
            }
            if (radioButtonModeTimetable.Checked)
            {
                if (SelectedTimetableSet != null)
                {
                    AddDetailToShow(catalog.GetString("Timetable set: {0}", SelectedTimetableSet), string.Empty);
                    // Description not shown as no description is available for a timetable set.
                }

                if (SelectedTimetable != null)
                {
                    AddDetailToShow(catalog.GetString("Timetable: {0}", SelectedTimetable), SelectedTimetable.Briefing);
                }
                if (SelectedTimetableTrain != null)
                {
                    if (string.IsNullOrEmpty(SelectedTimetableTrain.Briefing))
                    AddDetailToShow(catalog.GetString("Train: {0}", SelectedTimetableTrain), catalog.GetString("Start time: {0}", SelectedTimetableTrain.StartTimeCleaned));
                    else
                        AddDetailToShow(catalog.GetString("Train: {0}", SelectedTimetableTrain), catalog.GetString("Start time: {0}", SelectedTimetableTrain.StartTimeCleaned) + $"\n{SelectedTimetableTrain.Briefing}");

                    if (SelectedTimetableConsist != null)
                    {
                        AddDetailToShow(catalog.GetString("Consist: {0}", SelectedTimetableConsist.Name), string.Empty);
                        if (SelectedTimetableConsist.Locomotive != null && SelectedTimetableConsist.Locomotive.Description != null)
                        {
                            AddDetailToShow(catalog.GetString("Locomotive: {0}", SelectedTimetableConsist.Locomotive.Name), SelectedTimetableConsist.Locomotive.Description);
                        }
                    }
                    if (SelectedTimetablePath != null)
                    {
                        AddDetailToShow(catalog.GetString("Path: {0}", SelectedTimetablePath.Name), SelectedTimetablePath.ToInfo());
                    }
                }
            }

            FlowDetails();
        }

        private readonly List<Detail> details = new List<Detail>();

        private class Detail
        {
            public readonly Control Title;
            public readonly Control Expander;
            public readonly Control Summary;
            public readonly Control Description;
            public bool Expanded;

            public Detail(Control title, Control expander, Control summary, Control lines)
            {
                Title = title;
                Expander = expander;
                Summary = summary;
                Description = lines;
                Expanded = false;
            }
        }

        private void ClearDetails()
        {
            details.Clear();
            while (panelDetails.Controls.Count > 0)
                panelDetails.Controls.RemoveAt(0);
        }

        private void AddDetailToShow(string title, string text)
        {
            panelDetails.SuspendLayout();
            Label titleControl = new Label { Margin = new Padding(2), Text = title, UseMnemonic = false, Font = new Font(panelDetails.Font, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft };
            titleControl.Left = titleControl.Margin.Left;
            titleControl.Width = panelDetails.ClientSize.Width - titleControl.Margin.Horizontal - titleControl.PreferredHeight;
            titleControl.Height = titleControl.PreferredHeight;
            titleControl.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            panelDetails.Controls.Add(titleControl);

            Button expanderControl = new Button { Margin = new Padding(0), Text = "", FlatStyle = FlatStyle.Flat };
            expanderControl.Left = panelDetails.ClientSize.Width - titleControl.Height - titleControl.Margin.Right;
            expanderControl.Width = expanderControl.Height = titleControl.Height;
            expanderControl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            expanderControl.FlatAppearance.BorderSize = 0;
            expanderControl.BackgroundImageLayout = ImageLayout.Center;
            panelDetails.Controls.Add(expanderControl);

            Label summaryControl = new Label { Margin = new Padding(2), Text = text, AutoSize = false, UseMnemonic = false, UseCompatibleTextRendering = false };
            summaryControl.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            summaryControl.Left = summaryControl.Margin.Left;
            summaryControl.Width = panelDetails.ClientSize.Width - summaryControl.Margin.Horizontal;
            summaryControl.Height = MeasureTextHeigth("1\n2\n3\n4\n5", panelDetails.Font, summaryControl.ClientSize);
            panelDetails.Controls.Add(summaryControl);

            // Find out where we need to cut the text to make the summary 5 lines long. Uses a binary search to find the cut point.
            int size = MeasureTextHeigth(text, panelDetails.Font, summaryControl.ClientSize);
            int height = size;
            if (size > summaryControl.Height)
            {
                StringBuilder builder = new StringBuilder(text);
                float index = summaryControl.Text.Length;
                float indexChunk = index;
                while (indexChunk > 0.5f || size > summaryControl.Height)
                {
                    if (indexChunk > 0.5f)
                        indexChunk /= 2;
                    if (size > summaryControl.Height)
                        index -= indexChunk;
                    else
                        index += indexChunk;
                    size = MeasureTextHeigth(builder.ToString(0, (int)index) + "...", panelDetails.Font, summaryControl.ClientSize);
                }
                for (int i = 0; i < 3; i++)
                    builder[(int)index++] = '.';
                summaryControl.Text = builder.ToString(0, (int)index);
            }

            Label descriptionControl = new Label { Margin = new Padding(2), Text = text, AutoSize = false, UseMnemonic = false, UseCompatibleTextRendering = false };
            descriptionControl.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            descriptionControl.Left = descriptionControl.Margin.Left;
            descriptionControl.Width = panelDetails.ClientSize.Width - descriptionControl.Margin.Horizontal;
            descriptionControl.Height = height;
            panelDetails.Controls.Add(descriptionControl);

            // Enable the expander only if the full description is longer than the summary. Otherwise, disable the expander.
            expanderControl.Enabled = descriptionControl.Height > summaryControl.Height;
            if (expanderControl.Enabled)
            {
                expanderControl.BackgroundImage = (Image)resources.GetObject("ExpanderClosed", CultureInfo.InvariantCulture);
                expanderControl.Tag = details.Count;
                expanderControl.Click += new EventHandler(ExpanderControl_Click);
            }
            else
            {
                expanderControl.BackgroundImage = (Image)resources.GetObject("ExpanderClosedDisabled", CultureInfo.InvariantCulture);
            }
            details.Add(new Detail(titleControl, expanderControl, summaryControl, descriptionControl));
            panelDetails.ResumeLayout();

        }

        private static int MeasureTextHeigth(string text, Font font, Size clientSize)
        {
            return TextRenderer.MeasureText(text, font, clientSize, TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        }

        private void ExpanderControl_Click(object sender, EventArgs e)
        {
            int index = (int)(sender as Control).Tag;
            details[index].Expanded = !details[index].Expanded;
            details[index].Expander.BackgroundImage = (Image)resources.GetObject(details[index].Expanded ? "ExpanderOpen" : "ExpanderClosed", CultureInfo.InvariantCulture);
            FlowDetails();
        }

        private void FlowDetails()
        {
            int scrollPosition = panelDetails.AutoScrollPosition.Y;
            panelDetails.AutoScrollPosition = Point.Empty;
            panelDetails.AutoScrollMinSize = new Size(0, panelDetails.ClientSize.Height + 1);

            int top = 0;
            foreach (Detail detail in details)
            {
                top += detail.Title.Margin.Top;
                detail.Title.Top = detail.Expander.Top = top;
                top += detail.Title.Height + detail.Title.Margin.Bottom + detail.Description.Margin.Top;
                detail.Summary.Top = detail.Description.Top = top;
                detail.Summary.Visible = !detail.Expanded && detail.Expander.Enabled;
                detail.Description.Visible = !detail.Summary.Visible;
                if (detail.Description.Visible)
                    top += detail.Description.Height + detail.Description.Margin.Bottom;
                else
                    top += detail.Summary.Height + detail.Summary.Margin.Bottom;
            }

            if (panelDetails.AutoScrollMinSize.Height < top)
                panelDetails.AutoScrollMinSize = new Size(0, top);
            panelDetails.AutoScrollPosition = new Point(0, -scrollPosition);
        }
        #endregion

        #region Utility functions
        private void UpdateFromMenuSelection<T>(ComboBox comboBox, MenuSelectionIndex index, T defaultValue)
        {
            UpdateFromMenuSelection(comboBox, index, _ => _.ToString(), defaultValue);
        }

        private void UpdateFromMenuSelection<T>(ComboBox comboBox, MenuSelectionIndex index, Func<T, string> map)
        {
            UpdateFromMenuSelection(comboBox, index, map, default);
        }

        private void UpdateFromMenuSelectionComboBoxItem<T>(ComboBox comboBox, MenuSelectionIndex index, T defaultValue)
        {
            UpdateFromMenuSelection(comboBox, index, (item => item.Key.ToString()), new ComboBoxItem<T>(defaultValue, string.Empty));
        }


        private void UpdateFromMenuSelection<T>(ComboBox comboBox, MenuSelectionIndex index, Func<T, string> map, T defaultValue)
        {
            if (settings.Menu_Selection.Length > (int)index && !string.IsNullOrEmpty(settings.Menu_Selection[(int)index]))
            {
                if (comboBox.DropDownStyle == ComboBoxStyle.DropDown)
                    comboBox.Text = settings.Menu_Selection[(int)index];
                else
                    SetComboBoxItem<T>(comboBox, item => map(item) == settings.Menu_Selection[(int)index]);
            }
            else
            {
                if (comboBox.DropDownStyle == ComboBoxStyle.DropDown)
                    comboBox.Text = map(defaultValue);
                else if (defaultValue != null)
                    SetComboBoxItem<T>(comboBox, item => map(item) == map(defaultValue));
                else if (comboBox.Items.Count > 0)
                    comboBox.SelectedIndex = 0;
            }
        }

        private static void SetComboBoxItem<T>(ComboBox comboBox, Func<T, bool> predicate)
        {
            if (comboBox.Items.Count == 0)
                return;

            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is T t && predicate(t))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }
            comboBox.SelectedIndex = 0;
        }
        #endregion

        #region Executable utils
        private enum ImageSubsystem
        {
            Unknown = 0,
            Native = 1,
            WindowsGui = 2,
            WindowsConsole = 3,
        }

        private static ImageSubsystem GetImageSubsystem(BinaryReader stream)
        {
            try
            {
                long baseOffset = stream.BaseStream.Position;

                // WORD IMAGE_DOS_HEADER.e_magic = 0x4D5A (MZ)
                stream.BaseStream.Seek(baseOffset + 0, SeekOrigin.Begin);
                ushort dosMagic = stream.ReadUInt16();
                if (dosMagic != 0x5A4D)
                    return ImageSubsystem.Unknown;

                // LONG IMAGE_DOS_HEADER.e_lfanew
                stream.BaseStream.Seek(baseOffset + 60, SeekOrigin.Begin);
                uint ntHeaderOffset = stream.ReadUInt32();
                if (ntHeaderOffset == 0)
                    return ImageSubsystem.Unknown;

                // DWORD IMAGE_NT_HEADERS.Signature = 0x00004550 (PE..)
                stream.BaseStream.Seek(baseOffset + ntHeaderOffset, SeekOrigin.Begin);
                uint ntMagic = stream.ReadUInt32();
                if (ntMagic != 0x00004550)
                    return ImageSubsystem.Unknown;

                // WORD IMAGE_OPTIONAL_HEADER.Magic = 0x010A (32bit header) or 0x020B (64bit header)
                stream.BaseStream.Seek(baseOffset + ntHeaderOffset + 24, SeekOrigin.Begin);
                ushort optionalMagic = stream.ReadUInt16();
                if (optionalMagic != 0x010B && optionalMagic != 0x020B)
                    return ImageSubsystem.Unknown;

                // WORD IMAGE_OPTIONAL_HEADER.Subsystem
                // Note: There might need to be an adjustment for ImageBase being ULONGLONG in the 64bit header though this doesn't actually seem to be true.
                stream.BaseStream.Seek(baseOffset + ntHeaderOffset + 92, SeekOrigin.Begin);
                ushort peSubsystem = stream.ReadUInt16();

                return (ImageSubsystem)peSubsystem;
            }
            catch (EndOfStreamException)
            {
                return ImageSubsystem.Unknown;
            }
        }
        #endregion

        private static CancellationTokenSource ResetCancellationTokenSource(CancellationTokenSource cts)
        {
            if (cts != null)
            {
                cts.Dispose();
            }
            // Create a new cancellation token source so that can cancel all the tokens again 
            return new CancellationTokenSource();
        }

        private void ComboBoxTimetable_EnabledChanged(object sender, EventArgs e)
        {
            //Debrief Eval TTActivity.
            if (!comboBoxTimetable.Enabled)
            {
                //comboBoxTimetable.Enabled == false then we erase comboBoxTimetable and comboBoxTimetableTrain data.
                if (comboBoxTimetable.Items.Count > 0)
                {
                    comboBoxTimetable.Items.Clear();
                    comboBoxTimetableTrain.Items.Clear();
                    buttonStart.Enabled = false;
                }
            }
            //TO DO: Debrief Eval TTActivity
        }
    }
}
