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
        }

        public override Task Teardown() {
            AstroPhysicsToolsOptions.RemoveProfileHandler();
            return base.Teardown();
        }

        public static Version MinAppmVersion = new Version(1, 9, 2, 3);

        public static AstroPhysicsToolsOptions AstroPhysicsToolsOptions { get; private set; }
    }
}