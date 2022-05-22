#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using DaleGhent.NINA.AstroPhysicsTools.Interfaces;
using Settings = DaleGhent.NINA.AstroPhysicsTools.Properties.Settings;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace DaleGhent.NINA.AstroPhysicsTools {
    public class AstroPhysicsToolsOptions : BaseINPC, IAstroPhysicsToolsOptions {
        private readonly IProfileService profileService;
        private readonly IPluginOptionsAccessor pluginOptionsAccessor;

        public AstroPhysicsToolsOptions(IProfileService profileService) {
            this.profileService = profileService;
            profileService.ProfileChanged += ProfileService_ProfileChanged;

            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(AstroPhysicsTools));
            if (guid == null) {
                throw new Exception($"GUID was not found in assembly metadata");
            }

            this.pluginOptionsAccessor = new PluginOptionsAccessor(this.profileService, guid.Value);

            if (!Settings.Default.ApToolsMigratedProfiles.Contains(this.profileService.ActiveProfile.Id.ToString()) && !ApToolsProfileMigrated) {
                Logger.Info($"Migrating app settings to NINA profile {this.profileService.ActiveProfile.Name} ({this.profileService.ActiveProfile.Id})");
                MigrateSettingsToProfile();

                ApToolsProfileMigrated = true;
                Settings.Default.ApToolsMigratedProfiles.Add(this.profileService.ActiveProfile.Id.ToString());
                CoreUtil.SaveSettings(Settings.Default);
            }
        }

        public bool ApToolsProfileMigrated {
            get => pluginOptionsAccessor.GetValueBoolean(nameof(ApToolsProfileMigrated), false);
            set {
                pluginOptionsAccessor.SetValueBoolean(nameof(ApToolsProfileMigrated), value);
                RaisePropertyChanged();
            }
        }

        public bool AppmSetSlewRate {
            get => pluginOptionsAccessor.GetValueBoolean(nameof(AppmSetSlewRate), true);
            set {
                pluginOptionsAccessor.SetValueBoolean(nameof(AppmSetSlewRate), value);
                RaisePropertyChanged();
            }
        }

        public int AppmSlewRate {
            get => pluginOptionsAccessor.GetValueInt32(nameof(AppmSlewRate), 900);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(AppmSlewRate), value);
                RaisePropertyChanged();
            }
        }

        public int AppmSlewSettleTime {
            get => pluginOptionsAccessor.GetValueInt32(nameof(AppmSlewSettleTime), 2);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(AppmSlewSettleTime), value);
                RaisePropertyChanged();
            }
        }

        public double AppmZenithSafetyDistance {
            get => pluginOptionsAccessor.GetValueDouble(nameof(AppmZenithSafetyDistance), 0d);
            set {
                pluginOptionsAccessor.SetValueDouble(nameof(AppmZenithSafetyDistance), value);
                RaisePropertyChanged();
            }
        }

        public double AppmZenithSyncDistance {
            get => pluginOptionsAccessor.GetValueDouble(nameof(AppmZenithSyncDistance), 3d);
            set {
                pluginOptionsAccessor.SetValueDouble(nameof(AppmZenithSyncDistance), value);
                RaisePropertyChanged();
            }
        }

        public bool AppmUseMinAltitude {
            get => pluginOptionsAccessor.GetValueBoolean(nameof(AppmUseMinAltitude), true);
            set {
                pluginOptionsAccessor.SetValueBoolean(nameof(AppmUseMinAltitude), value);
                RaisePropertyChanged();
            }
        }

        public int AppmMinAltitude {
            get => pluginOptionsAccessor.GetValueInt32(nameof(AppmMinAltitude), 30);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(AppmMinAltitude), value);
                RaisePropertyChanged();
            }
        }

        public bool AppmUseMeridianLimits {
            get => pluginOptionsAccessor.GetValueBoolean(nameof(AppmUseMeridianLimits), true);
            set {
                pluginOptionsAccessor.SetValueBoolean(nameof(AppmUseMeridianLimits), value);
                RaisePropertyChanged();
            }
        }

        public bool AppmUseHorizonLimits {
            get => pluginOptionsAccessor.GetValueBoolean(nameof(AppmUseHorizonLimits), true);
            set {
                pluginOptionsAccessor.SetValueBoolean(nameof(AppmUseHorizonLimits), value);
                RaisePropertyChanged();
            }
        }

        public string APPMExePath {
            get => pluginOptionsAccessor.GetValueString(nameof(APPMExePath), @"C:\Program Files (x86)\Astro-Physics\APCC Pro\ApPointMapper.exe");
            set {
                pluginOptionsAccessor.SetValueString(nameof(APPMExePath), value);
                RaisePropertyChanged();
            }
        }

        public string APPMSettingsPath {
            get => pluginOptionsAccessor.GetValueString(nameof(APPMSettingsPath), string.Empty);
            set {
                pluginOptionsAccessor.SetValueString(nameof(APPMSettingsPath), value);
                RaisePropertyChanged();
            }
        }

        public string APPMMapPath {
            get => pluginOptionsAccessor.GetValueString(nameof(APPMMapPath), string.Empty);
            set {
                pluginOptionsAccessor.SetValueString(nameof(APPMMapPath), value);
                RaisePropertyChanged();
            }
        }

        public string ApccExePath {
            get => pluginOptionsAccessor.GetValueString(nameof(ApccExePath), @"C:\Program Files (x86)\Astro-Physics\APCC Pro\AstroPhysicsCommandCenter.exe");
            set {
                pluginOptionsAccessor.SetValueString(nameof(ApccExePath), value);
                RaisePropertyChanged();
            }
        }

        public uint ApccStartupTimeout {
            get => pluginOptionsAccessor.GetValueUInt32(nameof(ApccStartupTimeout), 30);
            set {
                pluginOptionsAccessor.SetValueUInt32(nameof(ApccStartupTimeout), value);
                RaisePropertyChanged();
            }
        }

        public uint ApccDriverConnectTimeout {
            get => pluginOptionsAccessor.GetValueUInt32(nameof(ApccDriverConnectTimeout), 5);
            set {
                pluginOptionsAccessor.SetValueUInt32(nameof(ApccDriverConnectTimeout), value);
                RaisePropertyChanged();
            }
        }

        public IList<string> PointOrderingStrategyList => Utility.Utility.PointOrderingStrategyList;

        /*
         * Create Dec Arc Model properties
         */

        public int DecArcDecSpacing {
            get => pluginOptionsAccessor.GetValueInt32(nameof(DecArcDecSpacing), 1);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(DecArcDecSpacing), value);
                RaisePropertyChanged();
            }
        }

        public int DecArcRaSpacing {
            get => pluginOptionsAccessor.GetValueInt32(nameof(DecArcRaSpacing), 4);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(DecArcRaSpacing), value);
                RaisePropertyChanged();
            }
        }

        public int DecArcQuantity {
            get => pluginOptionsAccessor.GetValueInt32(nameof(DecArcQuantity), 2);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(DecArcQuantity), value);
                RaisePropertyChanged();
            }
        }

        public double DecArcHourAngleLeadIn {
            get {
                var decArcHourAngleLeadIn = pluginOptionsAccessor.GetValueDouble(nameof(DecArcHourAngleLeadIn), 0d);
                HoursToDegrees = 15 * decArcHourAngleLeadIn;
                return decArcHourAngleLeadIn;
            }
            set {
                pluginOptionsAccessor.SetValueDouble(nameof(DecArcHourAngleLeadIn), value);
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
            get => pluginOptionsAccessor.GetValueInt32(nameof(DecArcPointOrderingStrategy), 0);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(DecArcPointOrderingStrategy), value);
                RaisePropertyChanged();
            }
        }

        public int DecArcPolarPointOrderingStrategy {
            get => pluginOptionsAccessor.GetValueInt32(nameof(DecArcPolarPointOrderingStrategy), 1);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(DecArcPolarPointOrderingStrategy), value);
                RaisePropertyChanged();
            }
        }

        public int DecArcPolarProximityLimit {
            get => pluginOptionsAccessor.GetValueInt32(nameof(DecArcPolarProximityLimit), 35);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(DecArcPolarProximityLimit), value);
                RaisePropertyChanged();
            }
        }

        /*
         * Create All Sky Model properties
         */

        public bool AllSkyCreateWestPoints {
            get => pluginOptionsAccessor.GetValueBoolean(nameof(AllSkyCreateWestPoints), true);
            set {
                pluginOptionsAccessor.SetValueBoolean(nameof(AllSkyCreateWestPoints), value);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyCreateEastPoints {
            get => pluginOptionsAccessor.GetValueBoolean(nameof(AllSkyCreateEastPoints), true);
            set {
                pluginOptionsAccessor.SetValueBoolean(nameof(AllSkyCreateEastPoints), value);
                RaisePropertyChanged();
            }
        }

        public int AllSkyPointOrderingStrategy {
            get => pluginOptionsAccessor.GetValueInt32(nameof(AllSkyPointOrderingStrategy), 0);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(AllSkyPointOrderingStrategy), value);
                RaisePropertyChanged();
            }
        }

        public int AllSkyDeclinationSpacing {
            get => pluginOptionsAccessor.GetValueInt32(nameof(AllSkyDeclinationSpacing), 10);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(AllSkyDeclinationSpacing), value);
                RaisePropertyChanged();
            }
        }

        public int AllSkyDeclinationOffset {
            get => pluginOptionsAccessor.GetValueInt32(nameof(AllSkyDeclinationOffset), 0);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(AllSkyDeclinationOffset), value);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyUseMinDeclination {
            get => pluginOptionsAccessor.GetValueBoolean(nameof(AllSkyUseMinDeclination), true);
            set {
                pluginOptionsAccessor.SetValueBoolean(nameof(AllSkyUseMinDeclination), value);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyUseMaxDeclination {
            get => pluginOptionsAccessor.GetValueBoolean(nameof(AllSkyUseMaxDeclination), true);
            set {
                pluginOptionsAccessor.SetValueBoolean(nameof(AllSkyUseMaxDeclination), value);
                RaisePropertyChanged();
            }
        }

        public int AllSkyMinDeclination {
            get => pluginOptionsAccessor.GetValueInt32(nameof(AllSkyMinDeclination), -85);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(AllSkyMinDeclination), value);
                RaisePropertyChanged();
            }
        }

        public int AllSkyMaxDeclination {
            get => pluginOptionsAccessor.GetValueInt32(nameof(AllSkyMaxDeclination), 85);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(AllSkyMaxDeclination), value);
                RaisePropertyChanged();
            }
        }

        public int AllSkyRightAscensionSpacing {
            get => pluginOptionsAccessor.GetValueInt32(nameof(AllSkyRightAscensionSpacing), 10);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(AllSkyRightAscensionSpacing), value);
                RaisePropertyChanged();
            }
        }

        public int AllSkyRightAscensionOffset {
            get => pluginOptionsAccessor.GetValueInt32(nameof(AllSkyRightAscensionOffset), 0);
            set {
                pluginOptionsAccessor.SetValueInt32(nameof(AllSkyRightAscensionOffset), value);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyUseMinHourAngleEast {
            get => pluginOptionsAccessor.GetValueBoolean(nameof(AllSkyUseMinHourAngleEast), true);
            set {
                pluginOptionsAccessor.SetValueBoolean(nameof(AllSkyUseMinHourAngleEast), value);
                RaisePropertyChanged();
            }
        }

        public bool AllSkyUseMaxHourAngleWest {
            get => pluginOptionsAccessor.GetValueBoolean(nameof(AllSkyUseMaxHourAngleWest), true);
            set {
                pluginOptionsAccessor.SetValueBoolean(nameof(AllSkyUseMaxHourAngleWest), value);
                RaisePropertyChanged();
            }
        }

        public double AllSkyMinHourAngleEast {
            get => pluginOptionsAccessor.GetValueDouble(nameof(AllSkyMinHourAngleEast), -6);
            set {
                pluginOptionsAccessor.SetValueDouble(nameof(AllSkyMinHourAngleEast), value);
                RaisePropertyChanged();
            }
        }

        public double AllSkyMaxHourAngleWest {
            get => pluginOptionsAccessor.GetValueDouble(nameof(AllSkyMaxHourAngleWest), 6);
            set {
                pluginOptionsAccessor.SetValueDouble(nameof(AllSkyMaxHourAngleWest), value);
                RaisePropertyChanged();
            }
        }

        /*
         * Button controls
         */

        internal void OpenAPPMExePathDialog(object obj) {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog {
                FileName = string.Empty,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Filter = "Any Program|*.exe"
            };

            if (dialog.ShowDialog() == true) {
                APPMExePath = dialog.FileName;
            }
        }

        internal void OpenAPPMSettingsPathDialog(object obj) {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog {
                FileName = string.Empty,
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"Astro-Physics\APPM"),
                Filter = "APPM Settings File|*.appm"
            };

            if (dialog.ShowDialog() == true) {
                APPMSettingsPath = dialog.FileName;
            }
        }

        internal void OpenAPPMMapPathDialog(object obj) {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog {
                FileName = string.Empty,
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"Astro-Physics\APPM"),
                Filter = "APPM Point Map File|*.csv"
            };

            if (dialog.ShowDialog() == true) {
                APPMMapPath = dialog.FileName;
            }
        }

        internal void OpenApccExePathDialog(object obj) {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog {
                FileName = string.Empty,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Filter = "Any Program|*.exe"
            };

            if (dialog.ShowDialog() == true) {
                ApccExePath = dialog.FileName;
            }
        }

        internal bool ImportAppmMeasurementConfig() {
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

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            RaiseAllPropertiesChanged();
        }

        internal void RemoveProfileHandler() {
            profileService.ProfileChanged -= ProfileService_ProfileChanged;
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
    }
}
