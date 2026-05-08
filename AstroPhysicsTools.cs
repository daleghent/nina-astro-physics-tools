#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Settings = DaleGhent.NINA.AstroPhysicsTools.Properties.Settings;

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

            AstroPhysicsToolsOptions ??= new AstroPhysicsToolsOptions(profileService);
        }

        public override Task Teardown() {
            AstroPhysicsToolsOptions.RemoveProfileHandler();
            return base.Teardown();
        }

        public static Version MinAppmVersion = new("1.9.8.15");

        public static AstroPhysicsToolsOptions AstroPhysicsToolsOptions { get; private set; }
    }
}