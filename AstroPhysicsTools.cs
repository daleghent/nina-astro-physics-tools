#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using Settings = DaleGhent.NINA.AstroPhysicsTools.Properties.Settings;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Windows.Input;
using System;

namespace DaleGhent.NINA.AstroPhysicsTools {

    [Export(typeof(IPluginManifest))]
    public class AstroPhysicsTools : PluginBase {

        [ImportingConstructor]
        public AstroPhysicsTools(IProfileService profileService) {
            if (Settings.Default.UpgradeSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpgradeSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }

            if (AstroPhysicsToolsOptions == null) {
                AstroPhysicsToolsOptions = new AstroPhysicsToolsOptions(profileService);
            }

            APPMExePathDialogCommand = new RelayCommand(AstroPhysicsToolsOptions.OpenAPPMExePathDialog);
            APPMSettingsPathDialogCommand = new RelayCommand(AstroPhysicsToolsOptions.OpenAPPMSettingsPathDialog);
            APPMMapPathDialoggCommand = new RelayCommand(AstroPhysicsToolsOptions.OpenAPPMMapPathDialog);
            ApccExePathDialogCommand = new RelayCommand(AstroPhysicsToolsOptions.OpenApccExePathDialog);
            ImportAppmMeasurementConfigCommand = new AsyncCommand<bool>(() => Task.Run(AstroPhysicsToolsOptions.ImportAppmMeasurementConfig));
        }

        public override Task Teardown() {
            AstroPhysicsToolsOptions.RemoveProfileHandler();
            return base.Teardown();
        }

        public static Version MinAppmVersion = new Version(1, 9, 2, 3);

        public static AstroPhysicsToolsOptions AstroPhysicsToolsOptions { get; private set; }
        public ICommand APPMExePathDialogCommand { get; private set; }
        public ICommand APPMSettingsPathDialogCommand { get; private set; }
        public ICommand APPMMapPathDialoggCommand { get; private set; }
        public ICommand ApccExePathDialogCommand { get; private set; }
        public ICommand ImportAppmMeasurementConfigCommand { get; private set; }
    }
}