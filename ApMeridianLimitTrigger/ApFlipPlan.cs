#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using DaleGhent.NINA.AstroPhysicsTools.ApccApi;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Utility;
using System;

namespace DaleGhent.NINA.AstroPhysicsTools.ApMeridianLimitTrigger {

    internal enum FlipPlanKind {
        Early,
        Delayed,
        MeridianHole,
    }

    /// <summary>
    /// The hour angles a flip is scheduled around, derived purely from APCC's meridian limits and the
    /// selected strategy. Holding them in one shape lets every strategy share the same time keeping,
    /// trigger decision, and logging, so the times the UI displays and the times the trigger decides
    /// on can never diverge.
    /// </summary>
    internal sealed class FlipPlan {
        public FlipPlanKind Kind { get; init; }

        /// <summary>The hour angle at which the mount actually crosses the pier.</summary>
        public double FlipHourAngle { get; init; }

        /// <summary>
        /// The hour angle at which tracking must stop, when that precedes the flip. Only a meridian
        /// hole produces one; null for every strategy that flips at the limit it stops at.
        /// </summary>
        public double? StopHourAngle { get; init; }

        /// <summary>Whether the limits sit exactly on the meridian and form no hole at all.</summary>
        public bool IsZeroLimit { get; init; }

        /// <summary>
        /// Whether the scheduled flip time is really the moment APCC's meridian limit is reached,
        /// with the flip committed to some amount of time before it. True for the strategies that
        /// track up to a limit; false for a meridian hole, where the limit is reported separately as
        /// <see cref="StopHourAngle"/> and the flip time is the flip itself.
        /// </summary>
        public bool FlipTimeIsLimit { get; init; }

        public string DebugDetail { get; init; } = string.Empty;
    }

    /// <summary>
    /// The hour angle arithmetic a meridian flip is planned with.
    ///
    /// Everything here is a pure function of APCC's reported limits and the selected strategy, with no
    /// mount state and no side effects beyond logging. Keeping it apart from the trigger lets the
    /// geometry be reasoned about, and tested, without a sequencer or a connected mount.
    /// </summary>
    internal static class ApFlipPlanner {

        // Sidereal hours elapse per solar hour
        public const double SiderealRate = 1.0027379d;

        // Half-width, in hours, of the hour angle window centred on upper culmination within which a
        // meridian flip is evaluated. Six hours covers any usable meridian limit while excluding the
        // region around lower culmination, which only a circumpolar target can occupy while tracking.
        public const double CulminationWindowHours = 6d;

        /// <summary>
        /// Chooses the flip strategy that APCC's current limits permit and reduces it to the hour angles
        /// it is scheduled around. Returns null when no flip can be scheduled at all.
        /// </summary>
        public static FlipPlan ResolveFlipPlan(ApccMeridianLimits meridianLimits, PierSide sideOfPier, ApFlipStrategy strategy) {
            if (sideOfPier != PierSide.pierWest) {
                return null;
            }

            // Early flip: rather than tracking up to the western limit and flipping there, the mount is
            // flipped before the target transits the meridian and resumes tracking counterweight-up on
            // the far side. APCC's eastern limit defines how far east of the meridian the mount may be
            // driven in that orientation, and therefore how early the flip may occur.
            //
            // A meridian hole (MeridianDelay >= 0) is excluded here. The hole is imposed by the western
            // limit, which an early flip does nothing to relieve: the mount still cannot track through
            // the forbidden region, so it must stop at the start of the hole and flip once the target
            // has cleared the eastern limit. That case therefore falls through to the meridian hole
            // handling below regardless of the selected strategy.
            if (strategy == ApFlipStrategy.Early && meridianLimits.MeridianDelay < 0d && CanFlipEarly(meridianLimits)) {
                // The eastern limit is reported as a magnitude, so it is negated to place the eastern
                // extent of the limit on the hour angle clock. The flip occurs the flip offset westward
                // of that extent, i.e. back toward the meridian, which on this clock means a less
                // negative hour angle. Subtracting the offset inside the negation yields that.
                //
                // For example, a 2.0h eastern limit with a 15 minute offset gives an eastern extent of
                // -2.000 and a flip hour angle of -1.750.
                var flipHourAngle = -(Math.Abs(meridianLimits.MeridianLimitsEastHours) - (meridianLimits.MeridianLimitsFlipOffsetMinutes / 60d));

                return new FlipPlan {
                    Kind = FlipPlanKind.Early,
                    FlipHourAngle = flipHourAngle,
                    // The eastern limit is a single, definite moment, so both flip bounds are that moment.
                    // The trigger decision applies a lookahead to it, however, so the flip is committed to
                    // before the limit is actually reached. The displayed time is therefore the limit.
                    FlipTimeIsLimit = true,
                    DebugDetail = $"FlipHA = {flipHourAngle:F3} (East limit = {meridianLimits.MeridianLimitsEastHours:F3}, Flip offset = {meridianLimits.MeridianLimitsFlipOffsetMinutes:F1}m)",
                };
            }

            // Standard case: the west limit permits tracking past the meridian (MeridianDelay < 0),
            // so the mount tracks up to the limit and flips there. No meridian hole is involved.
            if (meridianLimits.MeridianDelay < 0d) {
                // The hour angle at which APCC's meridian limit requires the mount to stop tracking.
                // MeridianDelay is signed opposite the hour angle clock: a negative delay means the
                // mount may track that far past (west of) the meridian, so negating it yields the
                // western hour angle of the limit. MeridianDelay already accounts for the flip offset
                // minutes, so it is used directly.
                var flipHourAngle = -meridianLimits.MeridianDelay;

                return new FlipPlan {
                    Kind = FlipPlanKind.Delayed,
                    FlipHourAngle = flipHourAngle,
                    // The mount tracks up to the western limit and flips there, so the scheduled time is
                    // that limit. The trigger commits to the flip up to one instruction duration earlier.
                    FlipTimeIsLimit = true,
                    DebugDetail = $"FlipHA = {flipHourAngle:F3} (Flip delay = {meridianLimits.MeridianDelay:F3}, Flip offset = {meridianLimits.MeridianLimitsFlipOffsetMinutes:F1}m)",
                };
            }

            // Meridian hole: a non-negative delay/western limit means the limit does not permit tracking
            // past the meridian, so the mount must stop tracking at or before it. It cannot flip and resume
            // until the target has transited the meridian and cleared any eastern limit.
            //
            // A delay of exactly zero is the degenerate form of this: the limit sits on the meridian so the
            // mount pauses just long enough to keep the next instruction from running into the meridian,
            // and flips as soon as the target has transited it.
            {
                // The true zero-limit case: the limits sit exactly on the meridian and form no hole at
                // all. APCC's flip offset describes how far into a hole the mount may be driven, so it
                // has nothing to apply to here. The mount simply pauses at the meridian and flips as soon
                // as the target has transited it.
                var isZeroLimit = meridianLimits.MeridianDelay == 0d && Math.Abs(meridianLimits.MeridianLimitsEastHours) == 0d;

                var flipOffsetHours = isZeroLimit ? 0d : meridianLimits.MeridianLimitsFlipOffsetMinutes / 60d;

                // The hour angle of the limit itself. With a zero delay the limit sits on the meridian.
                var limitHourAngle = meridianLimits.MeridianDelay > 0d
                    ? -meridianLimits.MeridianDelay
                    : 0d;

                // The hour angle at which tracking must stop. The flip offset describes how far short of
                // the limit APCC requires the mount to be held, so that is the limit in practice.
                var stopHourAngle = limitHourAngle - flipOffsetHours;

                // The western hour angle at which the target has cleared the eastern limit and the mount
                // may flip. Unlike MeridianDelay, MeridianLimitsEastHours does not account for the flip
                // offset, so it is added here. In the zero-limit case both are zero and the flip may occur
                // as soon as the target has transited the meridian.
                var exitHourAngle = Math.Abs(meridianLimits.MeridianLimitsEastHours) + flipOffsetHours;

                return new FlipPlan {
                    Kind = FlipPlanKind.MeridianHole,
                    FlipHourAngle = exitHourAngle,
                    StopHourAngle = stopHourAngle,
                    IsZeroLimit = isZeroLimit,
                    DebugDetail = $"LimitHA = {limitHourAngle:F3}, StopHA = {stopHourAngle:F3} (Flip offset = {flipOffsetHours:F3}), ExitHA = {exitHourAngle:F3} (Flip delay = {meridianLimits.MeridianDelay:F3}, East limit = {meridianLimits.MeridianLimitsEastHours:F3})",
                };
            }
        }

        /// <summary>
        /// Normalizes an hour angle onto the -12..+12 hour clock used by APCC's meridian limits,
        /// where 0 is the meridian, negative values are east of it, and positive values are west of it.
        /// </summary>
        public static double NormalizeHourAngle(double hourAngle) {
            var ha = AstroUtil.EuclidianModulus(hourAngle, 24);
            return ha > 12d ? ha - 24d : ha;
        }

        /// <summary>
        /// The clock hours until a target at <paramref name="currentHourAngle"/> reaches
        /// <paramref name="targetHourAngle"/>.
        ///
        /// Hour angles advance at the sidereal rate, so the difference between two of them is a count of
        /// sidereal hours and has to be divided by that rate to become the wall clock time a countdown or
        /// a wait is measured in. The result is negative when the target hour angle is already behind.
        ///
        /// Every conversion from hour angles to elapsed time goes through here so that the time the
        /// trigger schedules a flip for and the time the flip actually waits out cannot drift apart.
        /// </summary>
        public static double HoursUntilHourAngle(double currentHourAngle, double targetHourAngle) {
            return (targetHourAngle - currentHourAngle) / SiderealRate;
        }

        /// <summary>
        /// Whether APCC's configuration permits flipping before the target transits the meridian. This
        /// requires eastern counterweight-up slews to be enabled and an eastern limit that actually
        /// extends east of the meridian; without both the mount cannot track after an early flip.
        /// </summary>
        public static bool CanFlipEarly(ApccMeridianLimits meridianLimits) {
            if (!meridianLimits.MeridianLimitsEastCwUpSlewsEnabled) {
                Logger.Warning("Early meridian flip is selected but eastern counterweight-up slews are not enabled in APCC. Falling back to a delayed flip.");
                return false;
            }

            if (Math.Abs(meridianLimits.MeridianLimitsEastHours) <= (meridianLimits.MeridianLimitsFlipOffsetMinutes / 60d)) {
                Logger.Warning($"Early meridian flip is selected but APCC's eastern limit ({meridianLimits.MeridianLimitsEastHours:F3}h) does not exceed the flip offset ({meridianLimits.MeridianLimitsFlipOffsetMinutes:F1}m). Falling back to a delayed flip.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether the given declination is circumpolar (never sets) for the given site latitude.
        /// Works for both northern and southern hemispheres.
        /// </summary>
        /// <param name="declination">Target declination in degrees.</param>
        /// <param name="siteLatitude">Site latitude in degrees. Positive = north, negative = south.</param>
        /// <returns>True if the declination lies within the circumpolar region of the local sky.</returns>
        public static bool IsCircumpolar(double declination, double siteLatitude) {
            if (double.IsNaN(declination) || double.IsNaN(siteLatitude)) {
                return false;
            }

            // |dec| must exceed 90 - |latitude| and have the same sign as the latitude. At the equator
            // this limit is 90 degrees, so nothing qualifies and no separate guard is needed.
            var limit = 90d - Math.Abs(siteLatitude);

            return siteLatitude > 0d
                ? declination >= limit
                : declination <= -limit;
        }
    }
}
