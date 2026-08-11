#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem.Autofocus;
using NINA.Sequencer.SequenceItem.Platesolving;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace DaleGhent.NINA.AstroPhysicsTools.ApMeridianLimitTrigger {

    /// <summary>
    /// The plugin's own meridian flip engine.
    ///
    /// NINA's <c>MeridianFlipVM</c> drives the flip from a modal window and derives its own timing from
    /// the profile's meridian flip settings, neither of which can express the strategies that APCC's
    /// meridian limits make possible. This engine performs the same equipment choreography without a
    /// window, reporting every stage through the application status area, while leaving the timing
    /// decision entirely to the caller.
    ///
    /// The non-timing meridian flip options of the active profile (settle time, dome synchronization,
    /// recentering, autofocus after flip, and image rotation) are still honored.
    /// </summary>
    internal class ApMeridianFlipEngine {
        private readonly string name;
        private readonly ApMeridianFlipDependencies deps;

        public ApMeridianFlipEngine(string name, ApMeridianFlipDependencies deps) {
            this.name = name;
            this.deps = deps;
        }

        private IProfileService profileService => deps.ProfileService;
        private ITelescopeMediator telescopeMediator => deps.TelescopeMediator;
        private ICameraMediator cameraMediator => deps.CameraMediator;
        private IFocuserMediator focuserMediator => deps.FocuserMediator;
        private IFilterWheelMediator filterWheelMediator => deps.FilterWheelMediator;
        private IGuiderMediator guiderMediator => deps.GuiderMediator;
        private IImagingMediator imagingMediator => deps.ImagingMediator;
        private IDomeMediator domeMediator => deps.DomeMediator;
        private IDomeFollower domeFollower => deps.DomeFollower;
        private IImageHistoryVM history => deps.History;
        private IAutoFocusVMFactory autoFocusVMFactory => deps.AutoFocusVMFactory;
        private IPlateSolverFactory plateSolverFactory => deps.PlateSolverFactory;
        private IWindowServiceFactory windowServiceFactory => deps.WindowServiceFactory;

        /// <summary>
        /// Raised whenever the engine advances to a new stage so the trigger can surface it in its UI.
        /// </summary>
        public event Action<string> StageChanged;

        /// <summary>
        /// Runs the complete meridian flip.
        /// </summary>
        /// <param name="target">The coordinates to return to after the flip.</param>
        /// <param name="waitUntilFlip">
        /// How long the mount must wait, with tracking stopped, before the flip may be performed. Zero
        /// when the flip may proceed immediately, which is always the case for an early flip.
        /// </param>
        /// <param name="parent">The sequence container used as the runtime parent for post-flip actions.</param>
        /// <param name="isEarlyFlip">
        /// True when the flip occurs before the target transits the meridian. The mount must then be
        /// commanded onto the far side of the pier explicitly, since a slew alone will not flip it.
        /// </param>
        public async Task FlipAsync(Coordinates target,
                                    TimeSpan waitUntilFlip,
                                    ISequenceContainer parent,
                                    bool isEarlyFlip,
                                    IProgress<ApplicationStatus> progress,
                                    CancellationToken token) {
            var settings = profileService.ActiveProfile.MeridianFlipSettings;
            var trackingStopped = false;
            var guidingStopped = false;
            var completed = false;

            Logger.Info($"{name} - Starting meridian flip. Target: {FormatCoordinates(target)}, Early flip: {isEarlyFlip}, Wait until flip: {waitUntilFlip}, Settle: {settings.SettleTime}s, Recenter: {settings.Recenter}, AutoFocus: {settings.AutoFocusAfterFlip}, RotateImage: {settings.RotateImageAfterFlip}");

            try {
                guidingStopped = await StopGuiding(progress, token);

                if (waitUntilFlip > TimeSpan.Zero) {
                    // The mount cannot be permitted to keep tracking into a region its limits forbid,
                    // so tracking is stopped for the duration of the wait.
                    ReportStage("Stopping tracking", progress);
                    Logger.Info($"{name} - Stopping tracking for {waitUntilFlip.TotalSeconds:F0}s until the flip window opens");
                    telescopeMediator.SetTrackingEnabled(false);
                    trackingStopped = true;

                    await CountdownAsync(waitUntilFlip, remaining => $"Waiting for flip window: {ApFlipFormat.Countdown(remaining)}", progress, token);

                    ReportStage("Resuming tracking", progress);
                    telescopeMediator.SetTrackingEnabled(true);
                    trackingStopped = false;
                }

                await ExecuteFlip(target, isEarlyFlip, progress, token);

                await Settle(settings.SettleTime, progress, token);

                await SynchronizeDome(progress, token);

                if (settings.Recenter) {
                    await Recenter(target, parent, progress, token);
                }

                if (settings.AutoFocusAfterFlip) {
                    await RunAutoFocus(parent, progress, token);
                }

                if (settings.RotateImageAfterFlip) {
                    RotateImageAfterFlip(progress);
                }

                completed = true;
            } finally {
                if (guidingStopped && completed) {
                    await StartGuiding(progress, CancellationToken.None);
                } else if (guidingStopped) {
                    // The flip did not finish, so the mount is not where guiding expects it to be and is
                    // very likely stopped at APCC's limit. Resuming would only have the guider chase a
                    // star that is not there; it is left to whatever handles the failure.
                    Logger.Warning($"{name} - Guiding is left stopped because the meridian flip did not complete");
                }

                if (!completed && trackingStopped) {
                    // Tracking is deliberately left off. It was stopped because the mount may not track
                    // into the region its limits forbid, and the flip that would have relieved that did
                    // not happen, so re-enabling it here would drive the mount straight at the limit.
                    Logger.Warning($"{name} - Tracking is left stopped because the meridian flip did not complete");
                }

                StageChanged?.Invoke(string.Empty);
                progress?.Report(new ApplicationStatus() { Source = name, Status = string.Empty });
                Logger.Info($"{name} - Meridian flip finished. Completed: {completed}");
            }
        }

        private async Task<bool> StopGuiding(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (!guiderMediator.GetInfo().Connected) { return false; }

            ReportStage("Stopping guiding", progress);
            Logger.Info($"{name} - Stopping guiding for the meridian flip");

            return await guiderMediator.StopGuiding(token);
        }

        private async Task StartGuiding(IProgress<ApplicationStatus> progress, CancellationToken token) {
            ReportStage("Resuming guiding", progress);
            Logger.Info($"{name} - Resuming guiding after the meridian flip");

            try {
                if (!await guiderMediator.StartGuiding(profileService.ActiveProfile.GuiderSettings.AutoRetryStartGuiding, progress, token)) {
                    var message = "Guiding could not be resumed after the meridian flip";
                    Logger.Warning($"{name} - {message}");
                    Notification.ShowWarning(message);
                }
            } catch (Exception ex) {
                Logger.Error($"{name} - Failed to resume guiding after the meridian flip", ex);
            }
        }

        /// <summary>
        /// Reports a once-per-second countdown of <paramref name="duration"/> through the application
        /// status area. The elapsed time is taken from the delay itself rather than assumed to be exactly
        /// one second, so an overrunning delay does not stretch the countdown.
        /// </summary>
        private async Task CountdownAsync(TimeSpan duration,
                                          Func<TimeSpan, string> status,
                                          IProgress<ApplicationStatus> progress,
                                          CancellationToken token) {
            var remaining = duration;

            while (remaining.TotalSeconds >= 1d) {
                ReportStage(status(remaining), progress);
                var delta = await CoreUtil.Delay(1000, token);
                remaining -= delta;
            }
        }

        private async Task ExecuteFlip(Coordinates target, bool isEarlyFlip, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var pierSideBefore = telescopeMediator.GetInfo().SideOfPier;
            var pierSideAfter = pierSideBefore;
            var flipSuccessful = false;

            await telescopeMediator.RaiseBeforeMeridianFlip(new BeforeMeridianFlipEventArgs(target));

            try {
                ReportStage("Flipping mount", progress);
                Logger.Info($"{name} - Flipping to {FormatCoordinates(target)}");

                if (isEarlyFlip) {
                    // The target has not transited the meridian yet, so a plain slew to the target
                    // coordinates will not flip the mount. The mount must be commanded onto the far
                    // side of the pier explicitly before the slew.
                    Logger.Info($"{name} - Early flip: requesting a flip to {PierSide.pierEast} before the target transits the meridian");

                    telescopeMediator.SendCommandString("*PIERFLIP");

                    await WaitForEarlyFlipToComplete(pierSideBefore, progress, token);

                    // An early flip reports no success of its own, so a change of reported side of pier
                    // is the only evidence the mount actually crossed.
                    pierSideAfter = telescopeMediator.GetInfo().SideOfPier;
                    flipSuccessful = pierSideAfter != pierSideBefore;
                } else {
                    flipSuccessful = await telescopeMediator.MeridianFlip(target, token);
                    pierSideAfter = telescopeMediator.GetInfo().SideOfPier;
                }

                if (!flipSuccessful) {
                    throw new SequenceEntityFailedException("Meridian flip failed");
                }

                Logger.Info($"{name} - Flip complete. Side of pier {pierSideBefore} -> {pierSideAfter}");

                if (pierSideAfter == pierSideBefore) {
                    // Only reachable for a driver-driven flip that reported success: the early path
                    // already treats an unchanged side of pier as a failure above.
                    Logger.Warning($"{name} - The mount is still reporting side of pier {pierSideAfter} after the flip. The flip may not have taken effect.");
                }
            } finally {
                await telescopeMediator.RaiseAfterMeridianFlip(new AfterMeridianFlipEventArgs(flipSuccessful, target));
            }
        }

        /// <summary>
        /// Blocks until an early flip commanded through APCC has actually finished.
        ///
        /// The mount does not begin moving the instant the command returns, so polling <c>Slewing</c>
        /// straight away reports false and the flip would appear to complete immediately while the mount
        /// is still swinging through the pier. The slew is therefore first waited *for*, and only then
        /// waited *out*. Some drivers never raise the slewing flag for a pier flip at all, so a change of
        /// reported side of pier is accepted as the flip having started as well, and the start wait is
        /// bounded so a driver that reports neither cannot hang the sequence indefinitely.
        /// </summary>
        private async Task WaitForEarlyFlipToComplete(PierSide pierSideBefore, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var startTimeout = TimeSpan.FromSeconds(30);
            var pollInterval = TimeSpan.FromSeconds(1);

            ReportStage("Waiting for the mount to begin the flip", progress);

            // Elapsed time is measured rather than counted in poll intervals. Task.Delay guarantees only
            // a minimum, so under load a counted loop would make these timeouts substantially longer than
            // the durations they are documented as.
            var startWait = Stopwatch.StartNew();
            var flipStarted = false;

            while (startWait.Elapsed < startTimeout) {
                // Sampled once per iteration so both conditions decide on the same snapshot.
                var info = telescopeMediator.GetInfo();

                if (info.Slewing || info.SideOfPier != pierSideBefore) {
                    flipStarted = true;
                    break;
                }

                await Task.Delay(pollInterval, token);
            }

            if (!flipStarted) {
                Logger.Warning($"{name} - The mount did not report slewing or a change of pier side within {startTimeout.TotalSeconds:F0}s of the flip command.");
            }

            ReportStage("Waiting for the mount to complete the flip", progress);

            // Bounded like the start wait above. A driver that leaves the slewing flag set would
            // otherwise hold the sequence here indefinitely, with no way out but cancellation. The
            // bound is generous: a pier flip swings the mount through its full range and can legitimately
            // take minutes. Reaching it is reported and the flip continues, where the side of pier check
            // in ExecuteFlip decides whether the mount actually crossed.
            var completionTimeout = TimeSpan.FromMinutes(10);
            var completionWait = Stopwatch.StartNew();

            while (telescopeMediator.GetInfo().Slewing) {
                if (completionWait.Elapsed >= completionTimeout) {
                    Logger.Warning($"{name} - The mount is still reporting slewing {completionTimeout.TotalMinutes:F0} minutes after the flip command. Moving on.");
                    break;
                }

                await Task.Delay(pollInterval, token);
            }
        }

        private Task Settle(double settleTimeSeconds, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var remaining = TimeSpan.FromSeconds(settleTimeSeconds);
            if (remaining <= TimeSpan.Zero) { return Task.CompletedTask; }

            Logger.Info($"{name} - Settling the mount for {settleTimeSeconds}s");

            return CountdownAsync(remaining, r => $"Settling: {ApFlipFormat.Countdown(r)}", progress, token);
        }

        private async Task SynchronizeDome(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var domeInfo = domeMediator.GetInfo();
            if (!domeInfo.Connected || !domeInfo.CanSetAzimuth) { return; }

            ReportStage("Synchronizing dome", progress);

            try {
                if (domeFollower.IsFollowing) {
                    Logger.Info($"{name} - Waiting for the dome to synchronize to the mount");
                    await domeFollower.WaitForDomeSynchronization(token);
                } else {
                    Logger.Info($"{name} - Synchronizing the dome to the mount since dome following is not enabled");

                    if (!await domeFollower.TriggerTelescopeSync()) {
                        Logger.Warning($"{name} - Dome synchronization did not complete successfully. Moving on");
                        Notification.ShowWarning("Dome synchronization after the meridian flip did not complete successfully");
                    }
                }
            } catch (Exception ex) {
                Logger.Error($"{name} - Dome synchronization did not complete successfully. Moving on", ex);
                Notification.ShowWarning("Dome synchronization after the meridian flip did not complete successfully");
            }
        }

        private async Task Recenter(Coordinates target, ISequenceContainer parent, IProgress<ApplicationStatus> progress, CancellationToken token) {
            ReportStage("Recentering after flip", progress);
            Logger.Info($"{name} - Recentering on {FormatCoordinates(target)} after the flip");

            var center = new Center(profileService,
                                    telescopeMediator,
                                    imagingMediator,
                                    filterWheelMediator,
                                    guiderMediator,
                                    domeMediator,
                                    domeFollower,
                                    plateSolverFactory,
                                    windowServiceFactory);

            center.Coordinates.Coordinates = target;
            center.AttachNewParent(parent);

            await center.Run(progress, token);
        }

        private async Task RunAutoFocus(ISequenceContainer parent, IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (!focuserMediator.GetInfo().Connected) {
                Logger.Warning($"{name} - Autofocus after flip is enabled but no focuser is connected. Skipping");
                return;
            }

            ReportStage("Running autofocus after flip", progress);
            Logger.Info($"{name} - Running autofocus after the flip");

            var autoFocus = new RunAutofocus(profileService,
                                             history,
                                             cameraMediator,
                                             filterWheelMediator,
                                             focuserMediator,
                                             autoFocusVMFactory);

            autoFocus.AttachNewParent(parent);

            await autoFocus.Run(progress, token);
        }

        private void RotateImageAfterFlip(IProgress<ApplicationStatus> progress) {
            ReportStage("Rotating image after flip", progress);
            Logger.Info($"{name} - Rotating the image by 180 degrees after the flip");

            imagingMediator.SetImageRotation(imagingMediator.GetImageRotation() + 180);
        }

        private void ReportStage(string status, IProgress<ApplicationStatus> progress) {
            StageChanged?.Invoke(status);
            progress?.Report(new ApplicationStatus() { Source = name, Status = status });
        }

        private static string FormatCoordinates(Coordinates coordinates) {
            return coordinates == null
                ? "unknown"
                : $"RA: {coordinates.RAString} Dec: {coordinates.DecString} Epoch: {coordinates.Epoch}";
        }
    }
}