#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using DaleGhent.NINA.AstroPhysicsTools.Utility;
using System.ComponentModel;

namespace DaleGhent.NINA.AstroPhysicsTools.ApMeridianLimitTrigger {

    /// <summary>
    /// Determines when the flip is performed relative to the target's meridian transit.
    /// </summary>
    [TypeConverter(typeof(EnumDescriptionTypeConverter))]
    public enum ApFlipStrategy {

        /// <summary>
        /// Track up to APCC's western meridian limit and flip there. If the limits form a meridian
        /// hole, tracking stops at the start of the hole and the flip occurs once the target has
        /// cleared the eastern limit.
        /// </summary>
        [Description("Delayed")]
        Delayed,

        /// <summary>
        /// Flip before the target transits the meridian and resume tracking counterweight-up within
        /// APCC's eastern limit. Requires eastern counterweight-up slews to be enabled in APCC.
        /// If the limits form a meridian hole the western limit still forbids tracking through it, so
        /// this strategy defers to the same pause-then-flip behavior as <see cref="Delayed"/>.
        /// </summary>
        [Description("Early")]
        Early,
    }
}