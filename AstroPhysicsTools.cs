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
using NINA.Profile;
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
using Settings = DaleGhent.NINA.AstroPhysicsTools.Properties.Settings;

namespace DaleGhent.NINA.AstroPhysicsTools {

    [Export(typeof(IPluginManifest))]
    public class AstroPhysicsTools : PluginBase, INotifyPropertyChanged {
        private IPluginOptionsAccessor pluginSettings;
        private IProfileService profileService;

        [ImportingConstructor]
        public AstroPhysicsTools(IProfileService profileService) {
            if (Settings.Default.UpgradeSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpgradeSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }

            this.pluginSettings = new PluginOptionsAccessor(profileService, Guid.Parse(this.Identifier));
            this.profileService = profileService;

            profileService.ProfileChanged += ProfileService_ProfileChanged;

            if (!Settings.Default.ApToolsMigratedProfiles.Contains(profileService.ActiveProfile.Id.ToString()) && !ApToolsProfileMigrated) {
                Logger.Info($"Migrating app settings to NINA profile {profileService.ActiveProfile.Name} ({profileService.ActiveProfile.Id})");
                MigrateSettingsToProfile();

                ApToolsProfileMigrated = true;
                Settings.Default.ApToolsMigratedProfiles.Add(profileService.ActiveProfile.Id.ToString());
                CoreUtil.SaveSettings(Settings.Default);
            }

            APPMExePathDialogCommand = new RelayCommand(OpenAPPMExePathDialog);
            APPMSettingsPathDialogCommand = new RelayCommand(OpenAPPMSettingsPathDialog);
            APPMMapPathDialoggCommand = new RelayCommand(OpenAPPMMapPathDialog);
            ApccExePathDialogCommand = new RelayCommand(OpenApccExePathDialog);
            ImportAppmMeasurementConfigCommand = new AsyncCommand<bool>(() => Task.Run(ImportAppmMeasurementConfig));
        }

        public override Task Teardown() {
            profileService.ProfileChanged -= ProfileService_ProfileChanged;
            return base.Teardown();
        }

        public bool ApToolsProfileMigrated {
            get => pluginSettings.GetValueBoolean(nameof(ApToolsProfileMigrated), false);
            set {
                pluginSettings.SetValueBoolean(nameof(ApToolsProfileMigrated), value);
                RaisePropertyChanged();
            }
        }

        public bool AppmSetSlewRate {
            get => pluginSettings.GetValueBoolean(nameof(AppmSetSlewRate), Settings.Default.AppmSetSlewRate);
            set {
                pluginSettings.SetValueBoolean(nameof(AppmSetSlewRate), value);
                RaisePropertyChanged();
            }
        }

        public int AppmSlewRate {
            get => pluginSettings.GetValueInt32(nameof(AppmSlewRate), Settings.Default.AppmSlewRate);
            set {
                pluginSettings.SetValueInt32(nameof(AppmSlewRate), value);
                RaisePropertyChanged();
            }
        }

        public int AppmSlewSettleTime {
            get => pluginSettings.GetValueInt32(nameof(AppmSlewSettleTime), Settings.Default.AppmSlewSettleTime);
            set {
                pluginSettings.SetValueInt32(nameof(AppmSlewSettleTime), value);
                RaisePropertyChanged();
            }
        }

        public double AppmZenithSafetyDistance {
            get => pluginSettings.GetValueDouble(nameof(AppmZenithSafetyDistance), Settings.Default.AppmZenithSafetyDistance);
            set {
                pluginSettings.SetValueDouble(nameof(AppmZenithSafetyDistance), value);
                RaisePropertyChanged();
            }
        }

        public double AppmZenithSyncDistance {
            get => pluginSettings.GetValueDouble(nameof(AppmZenithSyncDistance), Settings.Default.AppmZenithSyncDistance);
            set {
                pluginSettings.SetValueDouble(nameof(AppmZenithSyncDistance), value);
                RaisePropertyChanged();
            }
        }

        public bool AppmUseMinAltitude {
            get => pluginSettings.GetValueBoolean(nameof(AppmUseMinAltitude), Settings.Default.AppmUseMinAltitude);
            set {
                pluginSettings.SetValueBoolean(nameof(AppmUseMinAltitude), value);
                RaisePropertyChanged();
            }
        }

        public int AppmMinAltitude {
            get => pluginSettings.GetValueInt32(nameof(AppmMinAltitude), Settings.Default.AppmMinAltitude);
            set {
                pluginSettings.SetValueInt32(nameof(AppmMinAltitude), value);
                RaisePropertyChanged();
            }
        }

        public bool AppmUseMeridianLimits {
            get => pluginSettings.GetValueBoolean(nameof(AppmUseMeridianLimits), Settings.Default.AppmUseMeridianLimits);
            set {
                pluginSettings.SetValueBoolean(nameof(AppmUseMeridianLimits), value);
                RaisePropertyChanged();
            }
        }

        public bool AppmUseHorizonLimits {
            get => pluginSettings.GetValueBoolean(nameof(AppmUseHorizonLimits), Settings.Default.AppmUseHorizonLimits);
            set {
                pluginSettings.SetValueBoolean(nameof(AppmUseHorizonLimits), value);
                RaisePropertyChanged();
            }
        }

        public string APPMExePath {
            get => pluginSettings.GetValueString(nameof(APPMExePath), Settings.Default.APPMExePath);
            set {
                pluginSettings.SetValueString(nameof(APPMExePath), value);
                RaisePropertyChanged();
            }
        }

        public string APPMSettingsPath {
            get => pluginSettings.GetValueString(nameof(APPMSettingsPath), Settings.Default.APPMSettingsPath);
            set {
                pluginSettings.SetValueString(nameof(APPMSettingsPath), value);
                RaisePropertyChanged();
            }
        }

        public string APPMMapPath {
            get => pluginSettings.GetValueString(nameof(APPMMapPath), Settings.Default.APPMMapPath);
            set {
                pluginSettings.SetValueString(nameof(APPMMapPath), value);
                RaisePropertyChanged();
            }
        }

        public string ApccExePath {
            get => pluginSettings.GetValueString(nameof(ApccExePath), Settings.Default.ApccDefaultProPath);
            set {
                pluginSettings.SetValueString(nameof(ApccExePath), value);
                RaisePropertyChanged();
            }
        }

        public uint ApccStartupTimeout {
            get => pluginSettings.GetValueUInt32(nameof(ApccStartupTimeout), Settings.Default.ApccStartupTimeout);
            set {
                pluginSettings.SetValueUInt32(nameof(ApccStartupTimeout), value);
                RaisePropertyChanged();
            }
        }

        public uint ApccDriverConnectTimeout {
            get => pluginSettings.GetValueUInt32(nameof(ApccDriverConnectTimeout), Settings.Default.ApccDriverConnectTimeout);
            set {
                pluginSettings.SetValueUInt32(nameof(ApccDriverConnectTimeout), value);
                RaisePropertyChanged();
            }
        }

        public IList<string> PointOrderingStrategyList => Utility.Utility.PointOrderingStrategyList;

        /*
         * Create Dec Arcy Model properties
         *
         */

        public int DecArcRaSpacing {
            get => pluginSettings.GetValueInt32(nameof(DecArcRaSpacing), Settings.Default.DecArcRaSpacing);
            set {
                pluginSettings.SetValueInt32(nameof(DecArcRaSpacing), value);
                RaisePropertyChanged();
            }
        }

        public int DecArcDecSpacing {
            get => pluginSettings.GetValueInt32(nameof(DecArcDecSpacing), Settings.Default.DecArcDecSpacing);
            set {
                pluginSettings.SetValueInt32(nameof(DecArcDecSpacing), value);
                RaisePropertyChanged();
            }
        }

        public int DecArcQuantity {
            get => pluginSettings.GetValueInt32(nameof(DecArcQuantity), Settings.Default.DecArcQuantity);
            set {
                pluginSettings.SetValueInt32(nameof(DecArcQuantity), value);
                RaisePropertyChanged();
            }
        }

        public double DecArcHourAngleLeadIn {
            get {
                var decArcHourAngleLeadIn = pluginSettings.GetValueDouble(nameof(DecArcHourAngleLeadIn), Settings.Default.DecArcHourAngleLeadIn);
                HoursToDegrees = 15 * decArcHourAngleLeadIn;
                return decArcHourAngleLeadIn;
            }
            set {
                pluginSettings.SetValueDouble(nameof(DecArcHourAngleLeadIn), value);
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
            get => pluginSettings.GetValueInt32(nameof(DecArcPointOrderingStrategy), Settings.Default.DecArcPointOrderingStrategy);
            set {
                pluginSettings.SetValueInt32(nameof(DecArcPointOrderingStrategy), value);
                RaisePropertyChanged();
            }
        }

        public int DecArcPolarPointOrderingStrategy {
            get => pluginSettings.GetValueInt32(nameof(DecArcPolarPointOrderingStrategy), Settings.Default.DecArcPolarPointOrderingStrategy);
            set {
                pluginSettings.SetValueInt32(nameof(DecArcPolarPointOrderingStrategy), value);
                RaisePropertyChanged();
            }
        }

        public int DecArcPolarProximityLimit {
            get => pluginSettings.GetValueInt32(nameof(DecArcPolarProximityLimit), Settings.Default.DecArcPolarProximityLimit);
            set {
                pluginSettings.SetValueInt32(nameof(DecArcPolarProximityLimit), value);
                RaisePropertyChanged();
            }
        }

        /*
         * Create All Sky Model properties
         *
         */

        public bool AllSkyCreateWestPoints {
            get => pluginSettings.GetValueBoolean(nameof(AllSkyCreateWestPoints), Settings.Default.AllSkyCreateWestPoints);
            set {
                pluginSettings.SetValueBoolean(nameof(AllSkyCreateWestPoints), value);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyCreateEastPoints {
            get => pluginSettings.GetValueBoolean(nameof(AllSkyCreateEastPoints), Settings.Default.AllSkyCreateEastPoints);
            set {
                pluginSettings.SetValueBoolean(nameof(AllSkyCreateEastPoints), value);
                RaisePropertyChanged();
            }
        }

        public int AllSkyPointOrderingStrategy {
            get => pluginSettings.GetValueInt32(nameof(AllSkyPointOrderingStrategy), Settings.Default.AllSkyPointOrderingStrategy);
            set {
                pluginSettings.SetValueInt32(nameof(AllSkyPointOrderingStrategy), value);
                RaisePropertyChanged();
            }
        }

        public int AllSkyDeclinationSpacing {
            get => pluginSettings.GetValueInt32(nameof(AllSkyDeclinationSpacing), Settings.Default.AllSkyDeclinationSpacing);
            set {
                pluginSettings.SetValueInt32(nameof(AllSkyDeclinationSpacing), value);
                RaisePropertyChanged();
            }
        }

        public int AllSkyDeclinationOffset {
            get => pluginSettings.GetValueInt32(nameof(AllSkyDeclinationOffset), Settings.Default.AllSkyDeclinationOffset);
            set {
                pluginSettings.SetValueInt32(nameof(AllSkyDeclinationOffset), value);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyUseMinDeclination {
            get => pluginSettings.GetValueBoolean(nameof(AllSkyUseMinDeclination), Settings.Default.AllSkyUseMinDeclination);
            set {
                pluginSettings.SetValueBoolean(nameof(AllSkyUseMinDeclination), value);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyUseMaxDeclination {
            get => pluginSettings.GetValueBoolean(nameof(AllSkyUseMaxDeclination), Settings.Default.AllSkyUseMaxDeclination);
            set {
                pluginSettings.SetValueBoolean(nameof(AllSkyUseMaxDeclination), value);
                RaisePropertyChanged();
            }
        }

        public int AllSkyMinDeclination {
            get => pluginSettings.GetValueInt32(nameof(AllSkyMinDeclination), Settings.Default.AllSkyMinDeclination);
            set {
                pluginSettings.SetValueInt32(nameof(AllSkyMinDeclination), value);
                RaisePropertyChanged();
            }
        }

        public int AllSkyMaxDeclination {
            get => pluginSettings.GetValueInt32(nameof(AllSkyMaxDeclination), Settings.Default.AllSkyMaxDeclination);
            set {
                pluginSettings.SetValueInt32(nameof(AllSkyMaxDeclination), value);
                RaisePropertyChanged();
            }
        }

        public int AllSkyRightAscensionSpacing {
            get => pluginSettings.GetValueInt32(nameof(AllSkyRightAscensionSpacing), Settings.Default.AllSkyRightAscensionSpacing);
            set {
                pluginSettings.SetValueInt32(nameof(AllSkyRightAscensionSpacing), value);
                RaisePropertyChanged();
            }
        }

        public int AllSkyRightAscensionOffset {
            get => pluginSettings.GetValueInt32(nameof(AllSkyRightAscensionOffset), Settings.Default.AllSkyRightAscensionOffset);
            set {
                pluginSettings.SetValueInt32(nameof(AllSkyRightAscensionOffset), value);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyUseMinHourAngleEast {
            get => pluginSettings.GetValueBoolean(nameof(AllSkyUseMinHourAngleEast), Settings.Default.AllSkyUseMinHourAngleEast);
            set {
                pluginSettings.SetValueBoolean(nameof(AllSkyUseMinHourAngleEast), value);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyUseMaxHourAngleWest {
            get => pluginSettings.GetValueBoolean(nameof(AllSkyUseMaxHourAngleWest), Settings.Default.AllSkyUseMaxHourAngleWest);
            set {
                pluginSettings.SetValueBoolean(nameof(AllSkyUseMaxHourAngleWest), value);
                RaisePropertyChanged();
            }
        }

        public double AllSkyMinHourAngleEast {
            get => pluginSettings.GetValueDouble(nameof(AllSkyMinHourAngleEast), Settings.Default.AllSkyMinHourAngleEast);
            set {
                pluginSettings.SetValueDouble(nameof(AllSkyMinHourAngleEast), value);
                RaisePropertyChanged();
            }
        }

        public double AllSkyMaxHourAngleWest {
            get => pluginSettings.GetValueDouble(nameof(AllSkyMaxHourAngleWest), Settings.Default.AllSkyMaxHourAngleWest);
            set {
                pluginSettings.SetValueDouble(nameof(AllSkyMaxHourAngleWest), value);
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

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            RaiseAllPropertiesChanged();
        }

        private void MigrateSettingsToProfile() {
            AppmSetSlewRate = Settings.Default.AppmSetSlewRate;
            AppmSlewRate = Settings.Default.AppmSlewRate;
            AppmSlewSettleTime = Settings.Default.AppmSlewSettleTime;
            AppmZenithSafetyDistance = Settings.Default.AppmZenithSafetyDistance;
            AppmUseMinAltitude = Settings.Default.AppmUseMinAltitude;
            AppmMinAltitude = Settings.Default.AppmMinAltitude;
            AppmUseMeridianLimits = Settings.Default.AppmUseMeridianLimits;
            AppmUseHorizonLimits = Settings.Default.AppmUseHorizonLimits;

            AllSkyCreateEastPoints = Settings.Default.AllSkyCreateEastPoints;
            AllSkyCreateWestPoints = Settings.Default.AllSkyCreateWestPoints;
            AllSkyDeclinationSpacing = Settings.Default.AllSkyDeclinationSpacing;
            AllSkyDeclinationOffset = Settings.Default.AllSkyDeclinationOffset;
            AllSkyUseMinDeclination = Settings.Default.AllSkyUseMinDeclination;
            AllSkyUseMaxDeclination = Settings.Default.AllSkyUseMaxDeclination;
            AllSkyMinDeclination = Settings.Default.AllSkyMinDeclination;
            AllSkyMaxDeclination = Settings.Default.AllSkyMaxDeclination;
            AllSkyRightAscensionSpacing = Settings.Default.AllSkyRightAscensionSpacing;
            AllSkyRightAscensionOffset = Settings.Default.AllSkyRightAscensionOffset;
            AllSkyUseMinHourAngleEast = Settings.Default.AllSkyUseMinHourAngleEast;
            AllSkyUseMaxHourAngleWest = Settings.Default.AllSkyUseMaxHourAngleWest;
            AllSkyPointOrderingStrategy = Settings.Default.AllSkyPointOrderingStrategy;

            DecArcRaSpacing = Settings.Default.DecArcRaSpacing;
            DecArcDecSpacing = Settings.Default.DecArcDecSpacing;
            DecArcQuantity = Settings.Default.DecArcQuantity;
            DecArcHourAngleLeadIn = Settings.Default.DecArcHourAngleLeadIn;
            DecArcPointOrderingStrategy = Settings.Default.DecArcPointOrderingStrategy;
            DecArcPolarPointOrderingStrategy = Settings.Default.DecArcPolarPointOrderingStrategy;
            DecArcPolarProximityLimit = Settings.Default.DecArcPolarProximityLimit;

            APPMExePath = Settings.Default.APPMExePath;
            APPMSettingsPath = Settings.Default.APPMSettingsPath;
            APPMMapPath = Settings.Default.APPMMapPath;
            ApccExePath = Settings.Default.ApccExePath;
            ApccStartupTimeout = Settings.Default.ApccStartupTimeout;
            ApccDriverConnectTimeout = Settings.Default.ApccDriverConnectTimeout;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaiseAllPropertiesChanged() {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}