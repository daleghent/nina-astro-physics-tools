#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using System;

namespace DaleGhent.NINA.AstroPhysicsTools.ApMeridianLimitTrigger {

    /// <summary>
    /// How a meridian flip's durations are rendered for display.
    ///
    /// The trigger's own countdowns and the engine's status messages describe the same waits, so they
    /// are formatted here rather than at each site. TimeSpan's "hh" specifier would otherwise be used
    /// in the engine, which rolls over at 24 hours and drops the day component entirely: a long
    /// meridian hole wait would be shown by the engine as a different duration than the trigger's own
    /// countdown to the same moment.
    /// </summary>
    internal static class ApFlipFormat {

        /// <summary>
        /// A countdown as hh:mm:ss, with the hours component accumulating past 24 rather than wrapping.
        /// </summary>
        public static string Countdown(TimeSpan span) {
            return $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
        }
    }
}
