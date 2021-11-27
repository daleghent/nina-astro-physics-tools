#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DaleGhent.NINA.AstroPhysicsTools {

    [Export(typeof(IPluginManifest))]
    public class AstroPhysicsTools : PluginBase, ISettings, INotifyPropertyChanged {

        [ImportingConstructor]
        public AstroPhysicsTools() {
            if (Properties.Settings.Default.UpgradeSettings) {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.UpgradeSettings = false;
                CoreUtil.SaveSettings(Properties.Settings.Default);
            }

            APPMExePathDialogCommand = new RelayCommand(OpenAPPMExePathDialog);
            APPMSettingsPathDialogCommand = new RelayCommand(OpenAPPMSettingsPathDialog);
            APPMMapPathDialoggCommand = new RelayCommand(OpenAPPMMapPathDialog);
            ApccExePathDialogCommand = new RelayCommand(OpenApccExePathDialog);
            ImportAppmMeasurementConfigCommand = new AsyncCommand<bool>(() => Task.Run(ImportAppmMeasurementConfig));
        }

        public bool AppmSetSlewRate {
            get => Properties.Settings.Default.AppmSetSlewRate;
            set {
                Properties.Settings.Default.AppmSetSlewRate = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int AppmSlewRate {
            get => Properties.Settings.Default.AppmSlewRate;
            set {
                Properties.Settings.Default.AppmSlewRate = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int AppmSlewSettleTime {
            get => Properties.Settings.Default.AppmSlewSettleTime;
            set {
                Properties.Settings.Default.AppmSlewSettleTime = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AppmZenithSafetyDistance {
            get => Properties.Settings.Default.AppmZenithSafetyDistance;
            set {
                Properties.Settings.Default.AppmZenithSafetyDistance = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AppmZenithSyncDistance {
            get => Properties.Settings.Default.AppmZenithSyncDistance;
            set {
                Properties.Settings.Default.AppmZenithSyncDistance = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool AppmUseMinAltitude {
            get => Properties.Settings.Default.AppmUseMinAltitude;
            set {
                Properties.Settings.Default.AppmUseMinAltitude = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int AppmMinAltitude {
            get => Properties.Settings.Default.AppmMinAltitude;
            set {
                Properties.Settings.Default.AppmMinAltitude = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool AppmUseMeridianLimits {
            get => Properties.Settings.Default.AppmUseMeridianLimits;
            set {
                Properties.Settings.Default.AppmUseMeridianLimits = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool AppmUseHorizonLimits {
            get => Properties.Settings.Default.AppmUseHorizonLimits;
            set {
                Properties.Settings.Default.AppmUseHorizonLimits = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public string APPMExePath {
            get => Properties.Settings.Default.APPMExePath;
            set {
                Properties.Settings.Default.APPMExePath = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public string APPMSettingsPath {
            get => Properties.Settings.Default.APPMSettingsPath;
            set {
                Properties.Settings.Default.APPMSettingsPath = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public string APPMMapPath {
            get => Properties.Settings.Default.APPMMapPath;
            set {
                Properties.Settings.Default.APPMMapPath = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public string ApccExePath {
            get {
                if (string.IsNullOrEmpty(Properties.Settings.Default.ApccExePath)) {
                    // Find APCC Pro first, then look for Standard
                    if (File.Exists(Properties.Settings.Default.ApccDefaultProPath)) {
                        ApccExePath = Properties.Settings.Default.ApccDefaultProPath;
                    } else if (File.Exists(Properties.Settings.Default.ApccDefaultStandardPath)) {
                        ApccExePath = Properties.Settings.Default.ApccDefaultStandardPath;
                    }
                }

                return Properties.Settings.Default.ApccExePath;
            }
            set {
                Properties.Settings.Default.ApccExePath = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public uint ApccStartupTimeout {
            get => Properties.Settings.Default.ApccStartupTimeout;
            set {
                Properties.Settings.Default.ApccStartupTimeout = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public uint ApccDriverConnectTimeout {
            get => Properties.Settings.Default.ApccDriverConnectTimeout;
            set {
                Properties.Settings.Default.ApccDriverConnectTimeout = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public IList<string> PointOrderingStrategyList => Utility.Utility.PointOrderingStrategyList;

        /*
         * Create Dec Arcy Model properties
         *
         */

        public int DecArcRaSpacing {
            get => Properties.Settings.Default.DecArcRaSpacing;
            set {
                Properties.Settings.Default.DecArcRaSpacing = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int DecArcDecSpacing {
            get => Properties.Settings.Default.DecArcDecSpacing;
            set {
                Properties.Settings.Default.DecArcDecSpacing = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int DecArcQuantity {
            get => Properties.Settings.Default.DecArcQuantity;
            set {
                Properties.Settings.Default.DecArcQuantity = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double DecArcHourAngleLeadIn {
            get {
                HoursToDegrees = 15 * Properties.Settings.Default.DecArcHourAngleLeadIn;
                return Properties.Settings.Default.DecArcHourAngleLeadIn;
            }
            set {
                Properties.Settings.Default.DecArcHourAngleLeadIn = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        private double hoursToDegrees = double.NaN;

        public double HoursToDegrees {
            get => hoursToDegrees;
            private set {
                hoursToDegrees = value;
                RaisePropertyChanged();
            }
        }

        public int DecArcPointOrderingStrategy {
            get => Properties.Settings.Default.DecArcPointOrderingStrategy;
            set {
                Properties.Settings.Default.DecArcPointOrderingStrategy = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int DecArcPolarPointOrderingStrategy {
            get => Properties.Settings.Default.DecArcPolarPointOrderingStrategy;
            set {
                Properties.Settings.Default.DecArcPolarPointOrderingStrategy = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int DecArcPolarProximityLimit {
            get => Properties.Settings.Default.DecArcPolarProximityLimit;
            set {
                Properties.Settings.Default.DecArcPolarProximityLimit = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        /*
         * Create All Sky Model properties
         *
         */

        public bool AllSkyCreateWestPoints {
            get => Properties.Settings.Default.AllSkyCreateWestPoints;
            set {
                Properties.Settings.Default.AllSkyCreateWestPoints = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyCreateEastPoints {
            get => Properties.Settings.Default.AllSkyCreateEastPoints;
            set {
                Properties.Settings.Default.AllSkyCreateEastPoints = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int AllSkyPointOrderingStrategy {
            get => Properties.Settings.Default.AllSkyPointOrderingStrategy;
            set {
                Properties.Settings.Default.AllSkyPointOrderingStrategy = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int AllSkyDeclinationSpacing {
            get => Properties.Settings.Default.AllSkyDeclinationSpacing;
            set {
                Properties.Settings.Default.AllSkyDeclinationSpacing = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int AllSkyDeclinationOffset {
            get => Properties.Settings.Default.AllSkyDeclinationOffset;
            set {
                Properties.Settings.Default.AllSkyDeclinationOffset = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyUseMinDeclination {
            get => Properties.Settings.Default.AllSkyUseMinDeclination;
            set {
                Properties.Settings.Default.AllSkyUseMinDeclination = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyUseMaxDeclination {
            get => Properties.Settings.Default.AllSkyUseMaxDeclination;
            set {
                Properties.Settings.Default.AllSkyUseMaxDeclination = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int AllSkyMinDeclination {
            get => Properties.Settings.Default.AllSkyMinDeclination;
            set {
                Properties.Settings.Default.AllSkyMinDeclination = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int AllSkyMaxDeclination {
            get => Properties.Settings.Default.AllSkyMaxDeclination;
            set {
                Properties.Settings.Default.AllSkyMaxDeclination = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int AllSkyRightAscensionSpacing {
            get => Properties.Settings.Default.AllSkyRightAscensionSpacing;
            set {
                Properties.Settings.Default.AllSkyRightAscensionSpacing = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int AllSkyRightAscensionOffset {
            get => Properties.Settings.Default.AllSkyRightAscensionOffset;
            set {
                Properties.Settings.Default.AllSkyRightAscensionOffset = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyUseMinHourAngleEast {
            get => Properties.Settings.Default.AllSkyUseMinHourAngleEast;
            set {
                Properties.Settings.Default.AllSkyUseMinHourAngleEast = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyUseMaxHourAngleWest {
            get => Properties.Settings.Default.AllSkyUseMaxHourAngleWest;
            set {
                Properties.Settings.Default.AllSkyUseMaxHourAngleWest = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AllSkyMinHourAngleEast {
            get => Properties.Settings.Default.AllSkyMinHourAngleEast;
            set {
                Properties.Settings.Default.AllSkyMinHourAngleEast = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AllSkyMaxHourAngleWest {
            get => Properties.Settings.Default.AllSkyMaxHourAngleWest;
            set {
                Properties.Settings.Default.AllSkyMaxHourAngleWest = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        /*
         * Button controls
         */

        private void OpenAPPMExePathDialog(object obj) {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog {
                FileName = string.Empty,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Filter = "Any Program|*.exe"
            };

            if (dialog.ShowDialog() == true) {
                APPMExePath = dialog.FileName;
            }
        }

        private void OpenAPPMSettingsPathDialog(object obj) {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog {
                FileName = string.Empty,
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"Astro-Physics\APPM"),
                Filter = "APPM Settings File|*.appm"
            };

            if (dialog.ShowDialog() == true) {
                APPMSettingsPath = dialog.FileName;
            }
        }

        private void OpenAPPMMapPathDialog(object obj) {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog {
                FileName = string.Empty,
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"Astro-Physics\APPM"),
                Filter = "APPM Point Map File|*.csv"
            };

            if (dialog.ShowDialog() == true) {
                APPMMapPath = dialog.FileName;
            }
        }

        private void OpenApccExePathDialog(object obj) {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog {
                FileName = string.Empty,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Filter = "Any Program|*.exe"
            };

            if (dialog.ShowDialog() == true) {
                ApccExePath = dialog.FileName;
            }
        }

        private bool ImportAppmMeasurementConfig() {
            var appm = new AppmApi.AppmApi();

            try {
                var config = appm.GetConfiguration(CancellationToken.None).Result.Configuration;

                AppmSetSlewRate = config.SetSlewRate;
                AppmSlewRate = config.SlewRate;
                AppmSlewSettleTime = config.SlewSettleTime;
                AppmZenithSafetyDistance = config.ZenithSafetyDistance;
                AppmZenithSyncDistance = config.ZenithSyncDistance;
                AppmUseMinAltitude = config.UseMinAltitude;
                AppmMinAltitude = config.MinAltitude;
                AppmUseMeridianLimits = config.UseMeridianLimits;
                AppmUseHorizonLimits = config.UseHorizonLimits;

                AllSkyCreateEastPoints = config.CreateEastPoints;
                AllSkyCreateWestPoints = config.CreateWestPoints;
                AllSkyDeclinationSpacing = config.DeclinationSpacing;
                AllSkyDeclinationOffset = config.DeclinationOffset;
                AllSkyUseMinDeclination = config.UseMinDeclination;
                AllSkyUseMaxDeclination = config.UseMaxDeclination;
                AllSkyMinDeclination = config.MinDeclination;
                AllSkyMaxDeclination = config.MaxDeclination;
                AllSkyRightAscensionSpacing = config.RightAscensionSpacing;
                AllSkyRightAscensionOffset = config.RightAscensionOffset;
                AllSkyUseMinHourAngleEast = config.UseMinHourAngleEast;
                AllSkyUseMaxHourAngleWest = config.UseMaxHourAngleWest;
                AllSkyPointOrderingStrategy = config.PointOrderingStrategy;

                Logger.Info("Imported APPM measurement settings");
            } catch (Exception ex) {
                Logger.Error($"Failed to import configuration from APPM: {ex.GetType()}: {ex.Message}");
                Notification.ShowError("Failed to import APPM measurement settings. Is APPM running?");
                return false;
            }

            return true;
        }

        public ICommand APPMExePathDialogCommand { get; private set; }
        public ICommand APPMSettingsPathDialogCommand { get; private set; }
        public ICommand APPMMapPathDialoggCommand { get; private set; }
        public ICommand ApccExePathDialogCommand { get; private set; }
        public ICommand ImportAppmMeasurementConfigCommand { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}