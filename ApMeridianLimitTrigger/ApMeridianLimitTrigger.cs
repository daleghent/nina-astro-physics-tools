#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using DaleGhent.NINA.AstroPhysicsTools.ApccApi;
using DaleGhent.NINA.AstroPhysicsTools.Interfaces;
using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Utility;
using NINA.Sequencer.Validations;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static DaleGhent.NINA.AstroPhysicsTools.ApMeridianLimitTrigger.ApFlipPlanner;

namespace DaleGhent.NINA.AstroPhysicsTools.ApMeridianLimitTrigger {

    [ExportMetadata("Name", "Astro-Physics Meridian Flip")]
    [ExportMetadata("Description", "Triggers a meridian flip for Astro-Physics mounts using APCC's meridian limit settings")]
    [ExportMetadata("Icon", "MeridianFlipSVG")]
    [ExportMetadata("Category", "Astro-Physics Tools")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public class ApMeridianLimitTrigger : SequenceTrigger, IMeridianFlipTrigger, IValidatable, IDisposable {

        // The trigger only needs the telescope mediator itself. The rest of the bundle is forwarded to
        // the flip engine, so it is kept whole rather than unpacked into fields.
        private readonly ApMeridianFlipDependencies deps;

        private readonly IAstroPhysicsToolsOptions options;

        private ITelescopeMediator telescopeMediator => deps.TelescopeMediator;
        private ISafetyMonitorMediator safetyMonitorMediator => deps.SafetyMonitorMediator;
        private ISequenceMediator sequenceMediator => deps.SequenceMediator;

        [ImportingConstructor]
        public ApMeridianLimitTrigger(IProfileService profileService,
                                      ICameraMediator cameraMediator,
                                      ITelescopeMediator telescopeMediator,
                                      IFocuserMediator focuserMediator,
                                      ISafetyMonitorMediator safetyMonitorMediator,
                                      IFilterWheelMediator filterWheelMediator,
                                      IGuiderMediator guiderMediator,
                                      IImagingMediator imagingMediator,
                                      IDomeMediator domeMediator,
                                      IDomeFollower domeFollower,
                                      IImageHistoryVM history,
                                      IAutoFocusVMFactory autoFocusVMFactory,
                                      IPlateSolverFactory plateSolverFactory,
                                      IWindowServiceFactory windowServiceFactory,
                                      ISequenceMediator sequenceMediator)
            : this(new ApMeridianFlipDependencies(profileService,
                                                  cameraMediator,
                                                  telescopeMediator,
                                                  focuserMediator,
                                                  safetyMonitorMediator,
                                                  filterWheelMediator,
                                                  guiderMediator,
                                                  imagingMediator,
                                                  domeMediator,
                                                  domeFollower,
                                                  history,
                                                  autoFocusVMFactory,
                                                  plateSolverFactory,
                                                  windowServiceFactory,
                                                  sequenceMediator),
                   AstroPhysicsTools.AstroPhysicsToolsOptions) {
        }

        internal ApMeridianLimitTrigger(ApMeridianFlipDependencies deps, IAstroPhysicsToolsOptions options) {
            this.deps = deps;
            this.options = options;
        }

        /// <summary>
        /// Runs the entire meridian flip using the plugin's own flip engine. NINA's <c>MeridianFlipVM</c>
        /// is deliberately not used: it drives the flip from a modal window and derives its timing from
        /// the profile's meridian flip settings, neither of which can express the strategies that APCC's
        /// meridian limits make possible.
        /// </summary>
        public override async Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var target = GetSequenceTarget(context, out var unusableReason);

            if (target == null) {
                target = telescopeMediator.GetCurrentPosition();
                Logger.Warning($"{Name} - {unusableReason} Taking current mount coordinates instead for the flip.");
            }

            var timeUntilFlip = TimeSpan.Zero;

            if (meridianHoleFlipAt.HasValue) {
                // The mount has reached the start of the meridian hole and cannot flip until the target
                // has transited the meridian and cleared the eastern limit. The engine stops tracking,
                // waits out the hole, and flips once the target has cleared it.
                //
                // The moment to flip at was fixed when the hole was armed, so it is used as recorded
                // rather than recomputed here. Recomputing would derive it from a fresh mount reading
                // taken after the instruction that was in progress has finished, which is a different
                // basis than the one the trigger decided and the countdown was displayed on.
                var flipAt = meridianHoleFlipAt.Value;
                timeUntilFlip = RemainingUntil(flipAt);

                // The flip time is assigned directly rather than through SetFlipTimes, which would turn
                // the remaining span back into an instant and reproduce it with rounding drift. There is
                // no trigger lead time here, so the earliest and latest flip times are that instant.
                LatestFlipTime = flipAt;
                EarliestFlipTime = flipAt;
                StartCountdownTicker();

                Logger.Info($"{Name} - Meridian hole start reached. Flip deferred {timeUntilFlip.TotalSeconds:F0}s until {flipAt:HH:mm:ss}. Target RA = {target.RA:F3}");
            }

            meridianHoleFlipAt = null;

            var isEarlyFlip = earlyFlipPending;
            earlyFlipPending = false;

            var engine = new ApMeridianFlipEngine(Name, deps);

            engine.StageChanged += OnFlipStageChanged;

            try {
                await engine.FlipAsync(target, timeUntilFlip, context ?? Parent, isEarlyFlip, progress, token);
            } finally {
                // The engine already clears the stage and the status area in its own finally block. Only
                // the trigger's copy of the stage has to be reset here, because the handler that would
                // have carried the engine's reset across is detached first.
                engine.StageChanged -= OnFlipStageChanged;
                ActiveStage = string.Empty;
                ClearFlipTimes();
            }
        }

        private void OnFlipStageChanged(string stage) {
            ActiveStage = stage;
        }

        private string activeStage = string.Empty;

        /// <summary>
        /// The flip stage currently being executed by the engine, or an empty string when no flip is
        /// in progress. Surfaced in the trigger's own UI in place of NINA's modal flip window.
        /// </summary>
        public string ActiveStage {
            get => activeStage;
            private set {
                activeStage = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsFlipping));
            }
        }

        public bool IsFlipping => !string.IsNullOrEmpty(ActiveStage);

        private ApFlipStrategy flipStrategy = ApFlipStrategy.Delayed;

        /// <summary>
        /// Whether the mount tracks up to APCC's western meridian limit before flipping, or flips early
        /// and resumes tracking counterweight-up within the eastern limit.
        /// </summary>
        [JsonProperty]
        public ApFlipStrategy FlipStrategy {
            get => flipStrategy;
            set {
                if (flipStrategy == value) { return; }

                flipStrategy = value;
                RaisePropertyChanged();

                // Both passes are needed and neither subsumes the other: Validate refreshes the issue
                // list, the recalculation refreshes the displayed times. They are cheap because both
                // read APCC's limits through the same short-lived cache, so only the first of them can
                // reach the network. The guard above keeps a setter that changes nothing from doing
                // either.
                Validate();
                RecalculateFlipTimes();
            }
        }

        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            return EvaluateFlip(nextItem, commitTriggerState: true);
        }

        /// <summary>
        /// Recalculates the limit and flip times without arming a flip. Used when something other than
        /// the passage of time invalidates them, such as the user changing the flip strategy, so the
        /// displayed countdowns reflect the new strategy immediately rather than at the next evaluation.
        /// </summary>
        private void RecalculateFlipTimes() {
            try {
                // Called from the FlipStrategy setter on the UI thread. A recalculation is only ever for
                // display, so it is skipped rather than waited on when an evaluation is already running.
                // The evaluation in progress refreshes the same times moments later anyway.
                EvaluateFlip(nextItem: null, commitTriggerState: false, skipIfBusy: true);
            } catch (Exception ex) {
                // A recalculation is only ever for display. Unlike a real evaluation it must not
                // interrupt anything, so a failure to reach APCC is logged and otherwise ignored.
                Logger.Debug($"{Name} - Could not recalculate flip times: {ex.Message}");
            }
        }

        /// <summary>
        /// Determines whether a flip is due and, as a side effect, refreshes the limit and flip times.
        /// </summary>
        /// <param name="commitTriggerState">
        /// When true, arms the flip that <see cref="Execute"/> will perform. When false the evaluation is
        /// for display only and leaves the trigger's armed state untouched.
        /// </param>
        /// <param name="skipIfBusy">
        /// When true the evaluation is abandoned rather than queued if another is already running. Only a
        /// display-only recalculation does this; it has nothing to report and the evaluation in progress
        /// refreshes the same times moments later anyway.
        /// </param>
        /// <remarks>
        /// Evaluations arrive from the sequencer thread and, as display-only recalculations, from the UI
        /// thread. Both write the same limit and flip times, so they are serialized here to keep those
        /// times mutually consistent.
        /// </remarks>
        private bool EvaluateFlip(ISequenceItem nextItem, bool commitTriggerState, bool skipIfBusy = false) {
            // A display-only recalculation abandons the attempt rather than queueing behind an evaluation
            // that is already refreshing the same times; every other caller waits.
            if (!Monitor.TryEnter(evaluationLock, skipIfBusy ? 0 : Timeout.Infinite)) {
                Logger.Debug($"{Name} - Flip evaluation already in progress, skipping recalculation");
                return false;
            }

            // Set once a plan has been produced and its times established. Every other way out of the
            // evaluation leaves it false, so no exit path can leave the UI showing a countdown for a
            // flip that is not scheduled.
            var scheduled = false;

            try {
                return EvaluateFlipCore(nextItem, commitTriggerState, ref scheduled);
            } finally {
                if (!scheduled) {
                    ClearFlipTimes();
                }

                Monitor.Exit(evaluationLock);
            }
        }

        // Serializes flip evaluations and every piece of trigger state they write.
        private readonly object evaluationLock = new();

        private bool EvaluateFlipCore(ISequenceItem nextItem, bool commitTriggerState, ref bool scheduled) {
            var telescopeInfo = telescopeMediator.GetInfo();

            if (commitTriggerState) {
                earlyFlipPending = false;
            }

            if (!telescopeInfo.Connected || double.IsNaN(telescopeInfo.TimeToMeridianFlip)) {
                Logger.Error($"{Name} - Mount is not connected. Skipping flip evaluation");
                return false;
            }

            var blockedBecause = GetEvaluationBlocker(telescopeInfo);

            if (blockedBecause != null) {
                Logger.Info($"{Name} - {blockedBecause}. Skipping flip evaluation");
                return false;
            }

            // Checks have passed.
            // Now collect the meridian limits and mount position to determine if a meridian flip is required.

            ApccMeridianLimits meridianLimits;

            try {
                meridianLimits = GetMeridianLimits();
                Logger.Debug($"{Name} - Current meridian limits: East = {meridianLimits.MeridianLimitsEastHours:F3}, West = {meridianLimits.MeridianLimitsWestHours:F3}");
            } catch (Exception ex) {
                // The limits cannot be read, so there is nothing to plan a flip from. Failing the
                // sequence over it would end the run on an APCC outage that may well be transient, so
                // this is reported the same way as in Validate and the recalculation path: no flip is
                // scheduled and the next evaluation tries again.
                Logger.Error($"{Name} - Failed to get meridian limits from APCC, no flip will be scheduled: {ex.Message}");
                return false;
            }

            var nextInstructionTime = GetReservedExecutionDuration(nextItem);
            Logger.Debug($"{Name} - Next instruction duration: {nextInstructionTime.TotalSeconds:F2}s");

            var currentRa = telescopeInfo.RightAscension;
            var currentLst = telescopeInfo.SiderealTime;

            // The hour angle of the target. 0 is the meridian, negative values are east of it,
            // positive values are west of it. Computed once here so that the culmination guard below
            // and every strategy branch decide on exactly the same value.
            var hourAngle = NormalizeHourAngle(currentLst - currentRa);

            // Guard against evaluating a flip when the target is nowhere near upper culmination.
            //
            // Every flip condition below is a one-sided comparison against a small hour angle near the
            // meridian, so a target far west of it satisfies them trivially. For a target that rises and
            // sets this is harmless because it is below the horizon by then, but a circumpolar target
            // stays up through lower culmination and would otherwise be commanded to flip while nowhere
            // near the meridian. Restricting evaluation to the half of the hour angle clock centred on
            // upper culmination excludes that region without affecting any real flip.
            if (Math.Abs(hourAngle) > CulminationWindowHours) {
                Logger.Info($"{Name} - Target hour angle {hourAngle:F3} is outside the +/-{CulminationWindowHours:F0}h upper culmination window. No meridian flip to schedule.");
                return false;
            }

            var plan = ResolveFlipPlan(meridianLimits, telescopeInfo.SideOfPier, FlipStrategy);

            if (plan == null) {
                // The mount is counterweight-down and east of the pier, tracking toward upper culmination.
                // APCC's western limit is what would eventually bound this, but acting on it is not yet
                // implemented, so no flip is scheduled here. Circumpolar targets are noted because they are
                // the ones that can remain in this state long enough to reach that limit.
                //
                // The target is resolved only on this branch. It is wanted for the log line alone, and
                // resolving it up front walked the sequence context on every evaluation to build a string
                // that is usually not produced.
                if (telescopeInfo.SideOfPier == PierSide.pierEast) {
                    var targetDeclination = GetSequenceTarget(Parent, out _)?.Dec ?? telescopeInfo.Declination;

                    if (IsCircumpolar(targetDeclination, telescopeInfo.SiteLatitude)) {
                        Logger.Info($"{Name} - Mount is on the east side of the pier with a circumpolar target (Dec = {targetDeclination:F3}, Latitude = {telescopeInfo.SiteLatitude:F3}). No meridian flip to schedule.");
                        return false;
                    }
                }

                Logger.Info($"{Name} - Mount is on the {telescopeInfo.SideOfPier} side of the pier. No meridian flip to schedule.");

                return false;
            }

            // How far the target will have moved (in sidereal hours) by the time the next instruction
            // completes. This is purely a property of the sequence, so it is applied only to the trigger
            // decision and never to the scheduled times themselves.
            var lookaheadHours = nextInstructionTime.TotalHours * SiderealRate;

            // Remaining sidereal hours until each hour angle is reached, converted back to clock time.
            var hoursUntilFlip = HoursUntilHourAngle(hourAngle, plan.FlipHourAngle);

            // The hour angle the trigger decision is made against. For a meridian hole this is where
            // tracking must stop, which precedes the flip; otherwise it is the flip itself.
            var decisionHourAngle = plan.StopHourAngle ?? plan.FlipHourAngle;

            var leadTime = plan.Kind == FlipPlanKind.Delayed ? nextInstructionTime : TimeSpan.Zero;

            SetFlipTimes(hoursUntilFlip, leadTime);

            // Whether the scheduled time is the limit or the flip is a property of the strategy, so it is
            // taken from the plan rather than inferred from the lead time. A display-only recalculation
            // has no next instruction and therefore no lead time, and inferring it there would drop the
            // label whenever the user merely changed strategy.
            FlipTimeIsLimit = plan.FlipTimeIsLimit;

            // Only the meridian hole case produces a limit stop, so the limit is cleared for every other
            // plan rather than reset up front. Setting it unconditionally at the head of the evaluation
            // fired the whole notification cascade, and restarted the countdown ticker, on every pass.
            if (plan.StopHourAngle.HasValue) {
                var hoursUntilStop = HoursUntilHourAngle(hourAngle, plan.StopHourAngle.Value);
                LimitTime = DateTime.Now.AddHours(Math.Max(hoursUntilStop, 0d));
                LimitTimeIsMeridian = plan.IsZeroLimit;
            } else {
                LimitTime = null;
                LimitTimeIsMeridian = false;
            }

            // For an early flip this is negative, since the flip occurs before the target transits the
            // meridian. It is reported as such rather than clamped so the value stays a truthful hour angle.
            //
            // These are minutes of hour angle, not clock minutes, so the conversion is a plain x60 and
            // deliberately does not divide by SiderealRate the way every scheduled time does. The
            // interface this satisfies describes a position relative to the meridian, not a duration.
            minutesAfterMeridian = plan.FlipHourAngle * 60d;
            RaiseMinutesAfterMeridianChanged();

            // The scheduled times are now established and must survive this evaluation regardless of
            // whether the flip turns out to be due yet.
            scheduled = true;

            // The schedule is complete, so the ticker is started once here rather than from each setter
            // that contributed to it.
            StartCountdownTicker();

            Logger.Debug($"{Name} - {plan.Kind}: PierSide = {telescopeInfo.SideOfPier}, CurrentLst = {currentLst:F3}, RA = {currentRa:F3}, HA = {hourAngle:F3}, {plan.DebugDetail}, DecisionHA = {decisionHourAngle:F3} (lookahead = {lookaheadHours:F3}), Flip at {LatestFlipTime:HH:mm:ss} (TTF = {hoursUntilFlip * 3600d:F0}s)");

            // The next instruction must not be allowed to run past the decision hour angle. Firing as soon
            // as it would carry the target beyond it commits to the flip before that happens, rather than
            // letting the instruction start and the limit fall due partway through it.
            //
            // An early flip approaches its hour angle from the east, so the target is *above* it until the
            // flip is due and the lookahead is subtracted rather than added.
            var reached = plan.Kind == FlipPlanKind.Early
                ? hourAngle - lookaheadHours >= decisionHourAngle
                : hourAngle + lookaheadHours >= decisionHourAngle;

            if (!reached) { return false; }
            if (!commitTriggerState) { return false; }

            // The flip is due, but a flip slews the mount and resumes imaging, so it must not be started
            // while conditions are unsafe. Tracking is stopped instead of flipping, which both halts the
            // mount before it can be driven into APCC's limit and leaves it where a safety-driven park or
            // an unsafe condition trigger can act on it. This mirrors what NINA's own meridian flip does.
            //
            // Only a connected monitor is honoured: an unconnected one reports IsSafe false, which would
            // otherwise block every flip for anyone without a safety monitor.
            if (safetyMonitorMediator.GetInfo() is { Connected: true, IsSafe: false }) {
                Logger.Warning($"{Name} - Meridian flip is due but the safety monitor reports unsafe conditions. Stopping tracking instead of flipping.");
                telescopeMediator.SetTrackingEnabled(false);
                return false;
            }

            switch (plan.Kind) {
                case FlipPlanKind.Early:
                    Logger.Info($"{Name} - Target hour angle {hourAngle:F3} (-{lookaheadHours:F3} lookahead) has reached the early flip hour angle {decisionHourAngle:F3}. Triggering early meridian flip.");
                    earlyFlipPending = true;
                    break;

                case FlipPlanKind.Delayed:
                    Logger.Info($"{Name} - Target hour angle {hourAngle:F3} (+{lookaheadHours:F3} lookahead) has reached the flip hour angle {decisionHourAngle:F3}. Triggering meridian flip.");
                    break;

                case FlipPlanKind.MeridianHole:
                    Logger.Info($"{Name} - Target hour angle {hourAngle:F3} (+{lookaheadHours:F3} lookahead) has reached the meridian hole stop hour angle {decisionHourAngle:F3}. Pausing until the target clears hour angle {plan.FlipHourAngle:F3} at {LatestFlipTime:HH:mm:ss}.");

                    meridianHoleFlipAt = LatestFlipTime;

                    break;
            }

            return true;
        }

        /// <summary>
        /// The reason the mount's current state rules out evaluating a flip, or null when it does not.
        /// </summary>
        private static string GetEvaluationBlocker(TelescopeInfo telescopeInfo) {
            if (telescopeInfo.Slewing) { return "Mount is slewing"; }
            if (telescopeInfo.AtPark) { return "Mount is parked"; }
            if (telescopeInfo.AtHome) { return "Mount is at home position"; }
            if (!telescopeInfo.TrackingEnabled) { return "Mount is not tracking"; }

            return null;
        }

        /// <summary>
        /// The coordinates the sequence context is targeting, or null when it has none that can be used.
        ///
        /// This is the single definition of a usable target, so the coordinates a flip is planned around
        /// and the declination it is reasoned about can never be resolved differently. All-zero
        /// coordinates are rejected because they are the placeholder an unconfigured target carries
        /// rather than a real position.
        /// </summary>
        /// <param name="unusableReason">
        /// Why no target could be resolved, phrased for a log message, or null when one was.
        /// </param>
        private static Coordinates GetSequenceTarget(ISequenceContainer context, out string unusableReason) {
            var target = ItemUtility.RetrieveContextCoordinates(context)?.Coordinates;

            if (target == null) {
                unusableReason = "No target information available for flip.";
                return null;
            }

            if (target.RA == 0 && target.Dec == 0) {
                unusableReason = "Target coordinates are all zero. Most likely not intended.";
                return null;
            }

            unusableReason = null;
            return target;
        }

        /// <summary>
        /// Clears the scheduled flip and limit times. Used whenever no flip can be scheduled, so the UI
        /// hides the countdowns rather than continuing to show stale ones.
        /// </summary>
        private void ClearFlipTimes() {
            EarliestFlipTime = DateTime.MinValue;
            LatestFlipTime = DateTime.MinValue;
            FlipTimeIsLimit = false;
            LimitTime = null;
            LimitTimeIsMeridian = false;
            minutesAfterMeridian = 0d;
            RaiseMinutesAfterMeridianChanged();

            // Nothing is left to count down, so the ticker is stopped rather than left notifying the UI
            // of values pinned at zero.
            StopCountdownTicker();
        }

        /// <summary>
        /// The single point at which the scheduled flip window is established. All strategies funnel
        /// through here so that the times the UI displays and the times the trigger decides on can never
        /// diverge.
        /// </summary>
        /// <param name="hoursUntilFlip">
        /// Clock hours from now until the flip actually occurs. This is the moment the mount is expected
        /// to cross the pier, not the moment the trigger fires.
        /// </param>
        /// <param name="triggerLeadTime">
        /// How far ahead of the flip the trigger may fire. The trigger fires as soon as the next
        /// instruction would carry the target past the limit, so the flip is committed to up to one
        /// instruction duration before it happens. Zero when the flip occurs at a definite moment.
        /// </param>
        private void SetFlipTimes(double hoursUntilFlip, TimeSpan triggerLeadTime) {
            var now = DateTime.Now;
            var flipIn = Math.Max(hoursUntilFlip, 0d);

            // LatestFlipTime is when the flip actually happens and is what the countdown is derived from.
            // EarliestFlipTime is only the start of the window in which the trigger may commit to it.
            LatestFlipTime = now.AddHours(flipIn);
            EarliestFlipTime = now.AddHours(Math.Max(flipIn - triggerLeadTime.TotalHours, 0d));
        }

        /// <summary>
        /// When the target reaches the hour angle at which it has cleared the eastern meridian limit and
        /// the mount may flip, as calculated at the moment the trigger committed to the flip. Null when
        /// no meridian hole is armed.
        ///
        /// Carried rather than recomputed so the wait the engine performs is exactly the one the trigger
        /// decided on and the UI counted down to.
        /// </summary>
        private DateTime? meridianHoleFlipAt = null;

        /// <summary>
        /// Set when <see cref="ShouldTrigger"/> fires an early flip, so <see cref="Execute"/> knows the
        /// mount must be commanded across the pier rather than flipped by the mount's own logic.
        /// </summary>
        private bool earlyFlipPending = false;

        private DateTime? limitTime = null;

        /// <summary>
        /// The time at which APCC's meridian limit is reached in a meridian hole, or null when the
        /// current limits do not produce one. The pause itself occurs some amount of time before this,
        /// by however long the instruction in progress runs, which cannot be known in advance. The limit
        /// is the only well defined moment, so it is what the UI shows.
        /// </summary>
        public DateTime? LimitTime {
            get => limitTime;
            protected set {
                if (limitTime == value) { return; }

                limitTime = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasLimitTime));
                RaisePropertyChanged(nameof(TimeUntilLimit));
                RaisePropertyChanged(nameof(LimitTimeDisplay));
                RaisePropertyChanged(nameof(TimeUntilLimitDisplay));
            }
        }

        public bool HasLimitTime => LimitTime.HasValue;

        private bool limitTimeIsMeridian = false;

        /// <summary>
        /// Whether <see cref="LimitTime"/> is the meridian itself rather than a configured limit. True
        /// only when both the east and west limits are zero, in which case there are no limits to speak
        /// of and the mount simply stops at the meridian.
        /// </summary>
        public bool LimitTimeIsMeridian {
            get => limitTimeIsMeridian;
            protected set {
                if (limitTimeIsMeridian == value) { return; }

                limitTimeIsMeridian = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(LimitTimeLabel));
            }
        }

        /// <summary>
        /// The label shown alongside <see cref="LimitTimeDisplay"/>.
        /// </summary>
        public string LimitTimeLabel => LimitTimeIsMeridian ? "Meridian At" : "Limits At";

        /// <summary>
        /// The wall clock time remaining until <see cref="LimitTime"/>. Zero when no limit is pending
        /// or the limit has already been reached.
        /// </summary>
        public TimeSpan TimeUntilLimit => LimitTime.HasValue ? RemainingUntil(LimitTime.Value) : TimeSpan.Zero;

        /// <summary>
        /// The wall clock time remaining until <paramref name="when"/>, floored at zero. Every countdown
        /// and the flip wait itself are derived through here so none of them can clamp differently.
        /// </summary>
        private static TimeSpan RemainingUntil(DateTime when) {
            var remaining = when - DateTime.Now;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>
        /// The wall clock time remaining until <see cref="LatestFlipTime"/>. Zero when the flip time is
        /// unknown or has already passed.
        ///
        /// This is the single subtraction the flip countdown is derived from. <see cref="TimeToMeridianFlip"/>
        /// and <see cref="TimeUntilFlipDisplay"/> both project it rather than reading the clock again, so
        /// the three cannot report different moments within one refresh.
        /// </summary>
        public TimeSpan TimeUntilFlip => HasFlipTime ? RemainingUntil(LatestFlipTime) : TimeSpan.Zero;

        // Formatted here rather than via XAML StringFormat: a markup extension consumes the backslashes
        // that TimeSpan's custom format strings require to escape the ":" separator.
        public string LimitTimeDisplay => LimitTime.HasValue ? LimitTime.Value.ToString("HH:mm:ss") : string.Empty;

        public string TimeUntilLimitDisplay => ApFlipFormat.Countdown(TimeUntilLimit);

        public string FlipTimeDisplay => LatestFlipTime > DateTime.MinValue ? LatestFlipTime.ToString("HH:mm:ss") : string.Empty;

        private bool flipTimeIsLimit = false;

        /// <summary>
        /// Whether <see cref="LatestFlipTime"/> is the moment APCC's meridian limit is reached rather
        /// than the moment of the flip itself. True for the delayed case, where the trigger commits to
        /// the flip up to one instruction duration before the limit is reached.
        /// </summary>
        public bool FlipTimeIsLimit {
            get => flipTimeIsLimit;
            protected set {
                if (flipTimeIsLimit == value) { return; }

                flipTimeIsLimit = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(FlipTimeLabel));
            }
        }

        /// <summary>
        /// The label shown alongside <see cref="FlipTimeDisplay"/>.
        /// </summary>
        public string FlipTimeLabel => FlipTimeIsLimit ? "Limits At" : "Flip Time";

        /// <summary>
        /// Whether a flip time has actually been calculated. The evaluation resets
        /// <see cref="LatestFlipTime"/> to <see cref="DateTime.MinValue"/> whenever no flip can be
        /// scheduled (mount disconnected, parked, at home, slewing, not tracking, or already east of
        /// the pier), which is also its initial value. The UI uses this to hide the flip time and
        /// countdown rather than display placeholders.
        /// </summary>
        public bool HasFlipTime => LatestFlipTime > DateTime.MinValue;

        public string TimeUntilFlipDisplay => ApFlipFormat.Countdown(TimeUntilFlip);

        protected DateTime latestFlipTime;

        public virtual DateTime LatestFlipTime {
            get => latestFlipTime;
            protected set {
                if (latestFlipTime == value) { return; }

                latestFlipTime = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(TimeUntilFlip));
                RaisePropertyChanged(nameof(TimeToMeridianFlip));
                RaisePropertyChanged(nameof(HasFlipTime));
                RaisePropertyChanged(nameof(FlipTimeDisplay));
                RaisePropertyChanged(nameof(TimeUntilFlipDisplay));
            }
        }

        protected DateTime earliestFlipTime;

        public virtual DateTime EarliestFlipTime {
            get => earliestFlipTime;
            protected set {
                if (earliestFlipTime == value) { return; }

                earliestFlipTime = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Hours remaining until the flip occurs. Derived from APCC's meridian limits rather than
        /// the mount driver's own estimate, which has no knowledge of the configured limits.
        /// <see cref="LatestFlipTime"/> is used because that is when the mount actually crosses the pier.
        /// <see cref="EarliestFlipTime"/> is merely when the trigger may commit to the flip, and counting
        /// down to it would reach zero while the instruction in progress still had to complete.
        /// </summary>
        public virtual double TimeToMeridianFlip {
            get => TimeUntilFlip.TotalHours;
            set { }
        }

        /// <summary>
        /// Minutes past the meridian at which the flip occurs. A meridian hole yields a positive value
        /// measured from the meridian to the eastern limit's exit point; a west limit that permits
        /// tracking past the meridian yields the extent of that limit.
        ///
        /// This is an angle expressed in minutes of hour angle, not an elapsed clock time. It is the one
        /// value in this class that is not converted through the sidereal rate, because it describes
        /// where the flip happens rather than when.
        /// </summary>
        public virtual double MinutesAfterMeridian {
            get => minutesAfterMeridian;
            set { }
        }

        private double minutesAfterMeridian = 0d;

        /// <summary>
        /// APCC's limits are absolute, so the earliest and latest flip times are identical and this
        /// matches <see cref="MinutesAfterMeridian"/>.
        /// </summary>
        public virtual double MaxMinutesAfterMeridian {
            get => MinutesAfterMeridian;
            set { }
        }

        /// <summary>
        /// Flip decisions are driven entirely by APCC's reported hour angle limits, never by the
        /// mount's side of pier reporting.
        /// </summary>
        public virtual bool UseSideOfPier {
            get => false;
            set { }
        }

        /// <summary>
        /// Releases the trigger's runtime state when the sequencer detaches it.
        ///
        /// The armed flip and the countdowns belong to the parent the trigger was attached to, so they
        /// are dropped rather than carried into a new one. Clearing the times also stops the ticker,
        /// which is why it is not stopped separately here.
        ///
        /// Attachment is also where the sequence end subscription is maintained, because it is the only
        /// notification the trigger gets that it has entered or left a sequence.
        /// </summary>
        public override void AfterParentChanged() {
            ResetState();

            if (Parent == null) {
                UnsubscribeSequenceFinished();
            } else {
                SubscribeSequenceFinished();
            }
        }

        /// <summary>
        /// Drops everything the trigger has decided about the current sequence: the armed meridian hole,
        /// the pending early flip, and the displayed times and countdowns.
        /// </summary>
        private void ResetState() {
            meridianHoleFlipAt = null;
            earlyFlipPending = false;
            ClearFlipTimes();
        }

        private bool sequenceFinishedSubscribed = false;

        /// <summary>
        /// Starts listening for the end of the sequence so the trigger's state does not outlive the run
        /// that produced it. The sequencer does not otherwise tell a trigger that the sequence ended, so
        /// without this an armed flip or a stale countdown would persist until the trigger happened to be
        /// evaluated again.
        ///
        /// NINA's <see cref="ISequenceMediator"/> forwards the subscription straight to the sequencer
        /// view models, which do not exist until the sequencer has been built. Failing to subscribe is
        /// therefore logged rather than thrown: the trigger still works, it just falls back to resetting
        /// on detachment.
        /// </summary>
        private void SubscribeSequenceFinished() {
            lock (sequenceFinishedLock) {
                if (sequenceFinishedSubscribed || disposed) { return; }

                try {
                    sequenceMediator.SequenceFinished += OnSequenceFinished;
                    sequenceFinishedSubscribed = true;
                } catch (Exception ex) {
                    Logger.Debug($"{Name} - Could not subscribe to SequenceFinished: {ex.Message}");
                }
            }
        }

        private void UnsubscribeSequenceFinished() {
            lock (sequenceFinishedLock) {
                if (!sequenceFinishedSubscribed) { return; }

                try {
                    sequenceMediator.SequenceFinished -= OnSequenceFinished;
                } catch (Exception ex) {
                    Logger.Debug($"{Name} - Could not unsubscribe from SequenceFinished: {ex.Message}");
                }

                sequenceFinishedSubscribed = false;
            }
        }

        private readonly object sequenceFinishedLock = new();

        /// <summary>
        /// Clears the trigger's state when the sequence ends, so a new run starts from a clean slate
        /// rather than inheriting an armed flip or a countdown from the previous one. This matters most
        /// when the sequence ended by parking the mount, where the flip the trigger was holding can no
        /// longer be performed at all.
        /// </summary>
        private Task OnSequenceFinished(object sender, EventArgs args) {
            var parked = telescopeMediator.GetInfo()?.AtPark == true;

            Logger.Info($"{Name} - Sequence finished{(parked ? " with the mount parked" : string.Empty)}. Clearing flip state.");

            ResetState();
            ActiveStage = string.Empty;

            return Task.CompletedTask;
        }

        private readonly object countdownLock = new();
        private CancellationTokenSource countdownCts = null;
        private Task countdownTask = null;
        private bool disposed = false;

        /// <summary>
        /// Stops the countdown ticker for good and prevents it from being restarted.
        ///
        /// As of writing, NINA's sequencer detaches triggers via <see cref="AfterParentChanged"/> rather
        /// than disposing them, so routine teardown happens there and this is not called. It is
        /// implemented so that the ticker is released deterministically by any host that does own the
        /// trigger's lifetime, including a future sequencer that disposes what it removes.
        ///
        /// This differs from detachment in being permanent. <see cref="AfterParentChanged"/> merely
        /// clears the schedule, which stops the ticker but leaves the trigger able to run again if it is
        /// attached to a new parent; disposal latches <see cref="disposed"/> so no later evaluation can
        /// start one.
        /// </summary>
        public void Dispose() {
            lock (countdownLock) {
                if (disposed) { return; }
                disposed = true;
            }

            StopCountdownTicker();
            UnsubscribeSequenceFinished();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Ticks the limit and flip countdowns on a background task. <see cref="Execute"/> blocks for the
        /// duration of the meridian flip, so the countdowns cannot rely on the trigger's own evaluation
        /// cycle to refresh them.
        ///
        /// The ticker only runs while there is a countdown to show. Both countdowns are cleared whenever
        /// no flip can be scheduled, and ticking then would raise change notifications every second for
        /// values the UI is not displaying.
        /// </summary>
        private void StartCountdownTicker() {
            if (!HasFlipTime && !HasLimitTime) {
                StopCountdownTicker();
                return;
            }

            CancellationTokenSource finished = null;

            lock (countdownLock) {
                if (disposed) { return; }
                if (countdownTask?.IsCompleted == false) { return; }

                // Any source still held here belongs to a ticker that has already run to completion,
                // either because it was cancelled or because its countdowns elapsed. Nothing can be
                // waiting on it, so it is safe to release once the lock is dropped.
                finished = countdownCts;

                var cts = new CancellationTokenSource();
                var token = cts.Token;

                countdownCts = cts;

                countdownTask = Task.Run(async () => {
                    try {
                        while (!token.IsCancellationRequested) {
                            await Task.Delay(TimeSpan.FromSeconds(1), token);

                            RaiseCountdownsChanged();

                            // Both countdowns have run out, so every value this ticker refreshes is now
                            // pinned at zero. It stops rather than going on notifying the UI of values
                            // that can no longer change, and the next evaluation that schedules a flip
                            // starts a new one.
                            if (CountdownsElapsed) { break; }
                        }
                    } catch (OperationCanceledException) {
                    } catch (Exception ex) {
                        Logger.Error($"{Name} - Countdown ticker stopped: {ex.Message}");
                    }
                }, token);
            }

            finished?.Dispose();
        }

        /// <summary>
        /// Raises the change notifications for the two countdowns and everything projected from them.
        ///
        /// The five values are three readings of two clock subtractions, so they are always notified
        /// together: refreshing any one of them without the others would let the UI show a countdown and
        /// its formatted form disagreeing by a second.
        /// </summary>
        private void RaiseCountdownsChanged() {
            RaisePropertyChanged(nameof(TimeUntilLimit));
            RaisePropertyChanged(nameof(TimeUntilLimitDisplay));
            RaisePropertyChanged(nameof(TimeUntilFlip));
            RaisePropertyChanged(nameof(TimeToMeridianFlip));
            RaisePropertyChanged(nameof(TimeUntilFlipDisplay));
        }

        /// <summary>
        /// Whether both countdowns have reached zero, so there is nothing left for the ticker to update.
        /// </summary>
        private bool CountdownsElapsed => TimeUntilFlip <= TimeSpan.Zero && TimeUntilLimit <= TimeSpan.Zero;

        /// <summary>
        /// Cancels the running ticker, if any.
        ///
        /// The token source is deliberately not disposed here. The ticker is sitting in a
        /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> registered against that source, and
        /// disposing it before the delay has observed the cancellation makes the delay throw
        /// <see cref="ObjectDisposedException"/> instead of cancelling cleanly. Disposal is therefore
        /// deferred until the ticker has actually finished.
        /// </summary>
        private void StopCountdownTicker() {
            CancellationTokenSource cts;
            Task task;

            lock (countdownLock) {
                cts = countdownCts;
                task = countdownTask;

                countdownCts = null;
                countdownTask = null;
            }

            if (cts == null) { return; }

            cts.Cancel();

            if (task == null) {
                cts.Dispose();
                return;
            }

            task.ContinueWith(_ => cts.Dispose(),
                              CancellationToken.None,
                              TaskContinuationOptions.ExecuteSynchronously,
                              TaskScheduler.Default);
        }

        /// <summary>
        /// Raises the change notifications for the derived minutes-after-meridian values.
        ///
        /// Only these two are raised here. Every other value the UI derives from the schedule hangs off
        /// <see cref="LatestFlipTime"/> or <see cref="LimitTime"/>, whose setters already notify their own
        /// dependents, and both are always assigned alongside <see cref="minutesAfterMeridian"/>. Raising
        /// them again here would notify the UI two and three times over for a single change.
        /// </summary>
        private void RaiseMinutesAfterMeridianChanged() {
            RaisePropertyChanged(nameof(MinutesAfterMeridian));
            RaisePropertyChanged(nameof(MaxMinutesAfterMeridian));
        }

        protected virtual TimeSpan GetReservedExecutionDuration(ISequenceItem nextItem) {
            return nextItem?.GetEstimatedDuration() ?? TimeSpan.Zero;
        }

        public virtual bool Validate() {
            var i = CollectIssues();

            // The Issues setter raises its own change notification, so none is raised here.
            if (!i.SequenceEqual(Issues)) {
                Issues = i;
            }

            return i.Count == 0;
        }

        private List<string> CollectIssues() {
            var i = new List<string>();
            var telescopeInfo = telescopeMediator.GetInfo();
            var apDriverName = "AstroPhysicsV2";

            // The version cannot be read, so there is nothing to compare against the minimum. Reporting
            // that comparison as well would surface the retained (or initial, all-zero) version as a
            // second issue for what is really one root cause.
            if (!TryGetApccFileVersion(options.ApccExePath, out var apccFileVersion)) {
                i.Add($"APCC executable path is not set or is invalid. Correct it in this plugin's Options");
                return i;
            }

            ApccFileVersion = apccFileVersion;

            if (ApccFileVersion < AstroPhysicsTools.MinApccVersion) {
                i.Add($"Installed APCC version {ApccFileVersion} is lower than the minimum required version {AstroPhysicsTools.MinApccVersion}");
                return i;
            }

            if (!IsApccRunning()) {
                i.Add($"APCC is not running");
                return i;
            }

            ApccMeridianLimits meridianLimits;

            // Validation runs on the UI's cadence and on the UI thread, so it takes whatever the cache
            // can serve and reports the absence of a value as an issue rather than waiting on APCC. The
            // flip evaluation, which genuinely cannot proceed without limits, is the only caller that
            // blocks. A refresh is still kicked off here, so the next pass a second later has a value.
            try {
                if (!TryGetMeridianLimits(out meridianLimits)) {
                    i.Add($"Waiting for meridian limits from APCC");
                    return i;
                }
            } catch (Exception ex) {
                i.Add($"Failed to get meridian limits from APCC: {ex.Message}");
                return i;
            }

            if (!meridianLimits.MeridianLimitsEnabled) {
                i.Add($"Meridian limits are not enabled in APCC");
            }

            if (FlipStrategy == ApFlipStrategy.Early && !meridianLimits.MeridianLimitsEastCwUpSlewsEnabled) {
                i.Add($"An early meridian flip requires eastern counterweight-up slews to be enabled in APCC");
            }

            if (!telescopeInfo.Connected) {
                i.Add($"Mount is not connected");
                return i;
            }

            if (!string.Equals(telescopeInfo.Name, apDriverName)) {
                i.Add($"The connected mount driver is not the AstroPhysics V2 GTO Mount driver");
                return i;
            }

            return i;
        }

        public ApMeridianLimitTrigger(ApMeridianLimitTrigger copyMe) : this(copyMe.deps, copyMe.options) {
            CopyMetaData(copyMe);

            // The backing field is assigned rather than the property: the setter validates and
            // recalculates the flip times, which would run a full APCC probe and a flip evaluation
            // against a half-constructed trigger every time one is cloned. The sequencer validates the
            // clone on its own cadence once it is attached.
            flipStrategy = copyMe.FlipStrategy;
        }

        private IList<string> issues = [];

        /// <summary>
        /// The validation issues currently outstanding. The only writer is <see cref="Validate"/>, which
        /// hands over a freshly built list it does not retain, so the value is stored as given rather
        /// than copied on every assignment.
        /// </summary>
        public IList<string> Issues {
            get => issues;
            set {
                issues = value ?? [];
                RaisePropertyChanged();
            }
        }

        public override object Clone() {
            return new ApMeridianLimitTrigger(this);
        }

        public override string ToString() {
            return $"Trigger: {Name}";
        }

        private static readonly ApccApi.ApccApi apccApi = new();

        // How long anything read from APCC is served without contacting it again. The limits, whether
        // APCC is running, and the version of the executable on disk all change on human timescales
        // while being asked for on the UI's cadence, so they share one interval.
        private static readonly TimeSpan ApccCacheTtl = TimeSpan.FromSeconds(2);

        // How long a set of limits may go on being served once it is stale, while refreshes are failing.
        // Beyond this the value is too old to make a flip decision on and callers are made to wait for a
        // fresh one, so a persistent APCC failure surfaces as an error rather than as silently frozen limits.
        private static readonly TimeSpan MeridianLimitsMaxStale = TimeSpan.FromMinutes(1);

        // The cache's own lock also guards the in-flight refresh below. No blocking or I/O is performed
        // while it is held, so a slow or hung APCC can never stall an unrelated caller.
        private static readonly TtlCache<ApccMeridianLimits> meridianLimitsCache = new(ApccCacheTtl);

        private static Task<ApccMeridianLimits> meridianLimitsRefresh = null;

        /// <summary>
        /// Fetches APCC's meridian limits, reusing a recently retrieved result.
        ///
        /// Both the flip evaluation and validation need the limits, and each runs far more often than
        /// the limits can realistically change: the evaluation at every sequence item boundary and the
        /// validation on the UI's cadence. A short cache keeps them consistent with each other and
        /// removes nearly all of the HTTP traffic.
        ///
        /// <see cref="ShouldTrigger"/> and <see cref="Validate"/> are both synchronous by contract, so a
        /// refresh that no cached value can cover has to be waited on. Only that case blocks: a merely
        /// stale value is returned immediately and refreshed in the background, so the UI never stalls on
        /// the request while a usable value is on hand. The request itself is always issued outside the
        /// lock, and only one is ever in flight no matter how many callers arrive.
        /// </summary>
        private static ApccMeridianLimits GetMeridianLimits() {
            if (meridianLimitsCache.TryGet(out var fresh)) {
                return fresh;
            }

            Task<ApccMeridianLimits> refresh;

            lock (meridianLimitsCache.SyncRoot) {
                refresh = StartMeridianLimitsRefresh();
            }

            // Serve a stale value rather than waiting on the refresh just started. A value inside the
            // stale window still describes APCC's limits closely enough to decide on, and the limits move
            // slowly enough that the refresh will land long before it matters.
            if (meridianLimitsCache.TryGet(MeridianLimitsMaxStale, out var stale)) {
                return stale;
            }

            // Nothing usable is cached, so the caller has to wait. Awaiting synchronously is safe here
            // because the request runs on the thread pool via Task.Run and captures no context: it does
            // not need the thread that is blocked waiting for it.
            return refresh.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Returns the in-flight limits refresh, starting one if none is running. Must be called with
        /// the limits cache's lock held.
        /// </summary>
        private static Task<ApccMeridianLimits> StartMeridianLimitsRefresh() {
            if (meridianLimitsRefresh?.IsCompleted == false) {
                return meridianLimitsRefresh;
            }

            meridianLimitsRefresh = Task.Run(async () => {
                var limits = await apccApi.GetMeridianLimits(CancellationToken.None).ConfigureAwait(false)
                    ?? throw new SequenceEntityFailedException("APCC returned no meridian limits");

                meridianLimitsCache.Set(limits);

                return limits;
            });

            // A background refresh has no caller to surface a failure to, so it is logged here. This also
            // observes the exception, which is still propagated to any caller waiting on the task itself.
            _ = meridianLimitsRefresh.ContinueWith(
                t => Logger.Warning($"Failed to refresh APCC meridian limits: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return meridianLimitsRefresh;
        }

        /// <summary>
        /// Returns APCC's meridian limits to the same accuracy as <see cref="GetMeridianLimits"/> but
        /// never waits on APCC, reporting false when nothing usable is cached.
        ///
        /// This exists for <see cref="Validate"/>, which runs on the UI thread roughly once a second per
        /// visible trigger. Blocking there on an HTTP request, as the first call after startup would
        /// otherwise do, stalls the interface. A refresh is still started so a later pass has a value;
        /// a failed one surfaces through the same exception a blocking caller would see.
        /// </summary>
        private static bool TryGetMeridianLimits(out ApccMeridianLimits limits) {
            if (meridianLimitsCache.TryGet(out limits)) {
                return true;
            }

            Task<ApccMeridianLimits> refresh;

            lock (meridianLimitsCache.SyncRoot) {
                refresh = StartMeridianLimitsRefresh();
            }

            if (meridianLimitsCache.TryGet(MeridianLimitsMaxStale, out limits)) {
                return true;
            }

            // A refresh that has already failed is reported as the failure it is, rather than as a value
            // that has merely not arrived yet. One still running is the latter, so it is left to land.
            if (refresh.IsFaulted) {
                throw refresh.Exception.GetBaseException();
            }

            limits = default;
            return false;
        }

        private Version ApccFileVersion { get; set; } = new Version(0, 0, 0, 0);

        // Validation runs on the UI's cadence, roughly once a second per visible trigger, but it probes
        // things that change on human timescales: whether APCC is running, and the version of the
        // executable on disk. Both are cached for the same interval as the meridian limits so a visible
        // trigger does not enumerate the process table and stat a file once a second.
        private static readonly TtlCache<bool> apccRunningCache = new(ApccCacheTtl);

        private static readonly TtlCache<Version> apccVersionCache = new(ApccCacheTtl);
        private static string cachedApccVersionPath = null;

        /// <summary>
        /// Whether APCC is currently running.
        ///
        /// <see cref="Process.GetProcessesByName(string)"/> enumerates every process on the machine and
        /// returns objects that each hold a native handle, so the result is cached and the handles are
        /// disposed rather than left to the finalizer.
        /// </summary>
        private static bool IsApccRunning() {
            if (apccRunningCache.TryGet(out var running)) {
                return running;
            }

            var processes = Process.GetProcessesByName("AstroPhysicsCommandCenter");

            try {
                running = processes.Length > 0;
            } finally {
                foreach (var process in processes) {
                    process.Dispose();
                }
            }

            apccRunningCache.Set(running);

            return running;
        }

        /// <summary>
        /// The product version of the APCC executable at the given path, or false when the path is not
        /// set or does not exist. The result is cached because reading it stats and opens the file, and
        /// invalidated whenever the configured path changes so a corrected path takes effect at once.
        /// </summary>
        private static bool TryGetApccFileVersion(string apccExePath, out Version version) {
            // The probe is performed with the lock held so that the path a version was read for and the
            // path the cache is keyed to cannot diverge. Releasing it around the file access would let a
            // Set for the old path land after another caller had invalidated for a new one, caching a
            // version for the wrong executable. The work is a stat and a header read of a local file,
            // and only runs once per cache interval, so holding the lock across it costs nothing.
            lock (apccVersionCache.SyncRoot) {
                if (apccExePath != cachedApccVersionPath) {
                    cachedApccVersionPath = apccExePath;
                    apccVersionCache.Invalidate();
                } else if (apccVersionCache.TryGet(out version)) {
                    return version != null;
                }

                version = null;

                if (!string.IsNullOrEmpty(apccExePath) && File.Exists(apccExePath)) {
                    try {
                        version = Version.Parse(FileVersionInfo.GetVersionInfo(apccExePath).ProductVersion);
                    } catch (Exception ex) {
                        // A file that exists but carries no parseable product version is indistinguishable
                        // from a wrong path as far as validation is concerned, and previously threw out of
                        // Validate entirely.
                        Logger.Warning($"Could not read the APCC product version from {apccExePath}: {ex.Message}");
                    }
                }

                apccVersionCache.Set(version);

                return version != null;
            }
        }
    }
}