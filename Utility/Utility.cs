#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Sequencer.Container;
using System.Collections.Generic;

namespace DaleGhent.NINA.AstroPhysicsTools.Utility {

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
    }
}