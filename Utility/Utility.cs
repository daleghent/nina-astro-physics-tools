#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.Sequencer.Container;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace DaleGhent.NINA.AstroPhysics.Utility {

    public class Utility {

        public static DeepSkyObject FindDsoInfo(ISequenceContainer container) {
            DeepSkyObject target = null;
            ISequenceContainer acontainer = container;

            while (acontainer != null) {
                if (acontainer is IDeepSkyObjectContainer dsoContainer) {
                    target = dsoContainer.Target.DeepSkyObject;
                    break;
                }

                acontainer = acontainer.Parent;
            }

            return target;
        }

        public static readonly IList<string> PointOrderingStrategyList = new List<string> {
            "Declination",
            "Declination (Equal RA)",
            "Declination (Graduated RA)",
            "Hour Angle"
        };

        public static bool IsProcessRunning(string process) {
            bool isRunning = false;

            Process[] pname = Process.GetProcessesByName(process);

            if (pname.Length > 0) {
                Logger.Debug($"Process {process} is running. Count={pname.Length}");
                isRunning = true;
            } else {
                Logger.Debug($"Process {process} is not running.");
            }

            return isRunning;
        }
    }
}