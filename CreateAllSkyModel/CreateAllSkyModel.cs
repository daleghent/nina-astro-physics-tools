#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using DaleGhent.NINA.AstroPhysicsTools.Interfaces;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DaleGhent.NINA.AstroPhysicsTools.CreateAllSkyModel {

    [ExportMetadata("Name", "Create All-Sky Model")]
    [ExportMetadata("Description", "Runs Astro-Physics Point Mapper (APPM) in automatic mode for unattended all-sky model creation. A point map file must be configured under this plugin's Options > All-Sky Parameters")]
    [ExportMetadata("Icon", "AllSky_SVG")]
    [ExportMetadata("Category", "Astro-Physics Tools")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CreateAllSkyModel : SequenceItem, IValidatable, INotifyPropertyChanged {
        private bool manualMode = false;
        private bool doNotExit = false;
        private int totalPoints = 0;
        private int currentPoint = 0;
        private string mappingRunState = "Unknown";
        private AppmApi.AppmApi appm = null;
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly IAstroPhysicsToolsOptions options;

        [ImportingConstructor]
        public CreateAllSkyModel(IProfileService profileService, ICameraMediator cameraMediator, IFilterWheelMediator filterWheelMediator, IGuiderMediator guiderMediator) : this(profileService, cameraMediator, filterWheelMediator, guiderMediator, AstroPhysicsTools.AstroPhysicsToolsOptions) {
        }

        public CreateAllSkyModel(IProfileService profileService, ICameraMediator cameraMediator, IFilterWheelMediator filterWheelMediator, IGuiderMediator guiderMediator, IAstroPhysicsToolsOptions options) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.guiderMediator = guiderMediator;
            this.options = options;

            if (File.Exists(options.APPMExePath)) {
                AppmFileVersion = Version.Parse(FileVersionInfo.GetVersionInfo(options.APPMExePath).ProductVersion);
            }
        }

        public CreateAllSkyModel(CreateAllSkyModel copyMe) : this(copyMe.profileService, copyMe.cameraMediator, copyMe.filterWheelMediator, copyMe.guiderMediator, copyMe.options) {
            CopyMetaData(copyMe);
        }

        [JsonProperty]
        public bool ManualMode {
            get => manualMode;
            set {
                manualMode = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public bool DoNotExit {
            get => doNotExit;
            set {
                doNotExit = value;
                RaisePropertyChanged();
            }
        }

        public int TotalPoints {
            get => totalPoints;
            set {
                totalPoints = value;
                Logger.Debug($"TotalPoints set to {totalPoints}");
                RaisePropertyChanged();
            }
        }

        public int CurrentPoint {
            get => currentPoint;
            set {
                currentPoint = value;
                Logger.Debug($"CurrentPoint set to {currentPoint}");
                RaisePropertyChanged();
            }
        }

        public string MappingRunState {
            get => mappingRunState;
            set {
                mappingRunState = value;
                Logger.Debug($"MappingStatus set to {mappingRunState}");
                RaisePropertyChanged();
            }
        }

        private Version AppmFileVersion { get; set; }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken ct) {
            var updateStatusTaskCts = new CancellationTokenSource();
            CancellationToken updateStatusTaskCt = updateStatusTaskCts.Token;
            Task updateStatusTask = null;
            Process proc = null;
            FilterInfo originalFilter = null;
            bool stoppedGuiding = false;
            bool ownsProcess = false;
            appm = new AppmApi.AppmApi();

            var config = new AppmApi.AppmMeasurementConfiguration {
                SetSlewRate = options.AppmSetSlewRate,
                SlewRate = options.AppmSlewRate,
                SlewSettleTime = options.AppmSlewSettleTime,
                LongSlewExtraSettleTime = options.AppmLongSlewExtraSettleTime,
                ZenithSafetyDistance = options.AppmZenithSafetyDistance,
                ZenithSyncDistance = options.AppmZenithSyncDistance,
                UseMinAltitude = options.AppmUseMinAltitude,
                MinAltitude = options.AppmMinAltitude,

                CreateEastPoints = options.AllSkyCreateEastPoints,
                CreateWestPoints = options.AllSkyCreateWestPoints,
                UseMeridianLimits = options.AppmUseMeridianLimits,
                UseHorizonLimits = options.AppmUseHorizonLimits,
                DeclinationSpacing = options.AllSkyDeclinationSpacing,
                DeclinationOffset = options.AllSkyDeclinationOffset,
                UseMinDeclination = options.AllSkyUseMinDeclination,
                UseMaxDeclination = options.AllSkyUseMaxDeclination,
                RightAscensionSpacing = options.AllSkyRightAscensionSpacing,
                RightAscensionOffset = options.AllSkyRightAscensionOffset,
                UseMinHourAngleEast = options.AllSkyUseMinHourAngleEast,
                UseMaxHourAngleWest = options.AllSkyUseMaxHourAngleWest,
                PointOrderingStrategy = options.AllSkyPointOrderingStrategy,
            };

            var request = new AppmApi.AppmMeasurementConfigurationRequest() {
                Configuration = config,
            };

            if (guiderMediator.GetInfo().Connected) {
                stoppedGuiding = await guiderMediator.StopGuiding(ct);
            }

            try {
                if (filterWheelMediator.GetInfo().Connected) {
                    originalFilter = filterWheelMediator.GetInfo().SelectedFilter;
                    await filterWheelMediator.ChangeFilter(profileService.ActiveProfile.PlateSolveSettings.Filter, ct, progress);
                }

                proc = RunAPPM(out ownsProcess);

                MappingRunState = (await appm.WaitForApiInit(ct)).Status.MappingRunState;
                updateStatusTask = UpdateStatus(updateStatusTaskCt);

                var response = await appm.SetConfiguration(request, ct);

                if (!response.Success) {
                    throw new SequenceEntityFailedException("Could not set APPM configuration");
                }

                TotalPoints = response.PointCount;

                if (TotalPoints == 0) {
                    Logger.Warning($"Total point count is {TotalPoints}. Exiting without running model");
                    Notification.ShowWarning($"The point count for this mapping run is {TotalPoints}. The mapping run will not start. This is not an error, but it's perhaps not what you intended.");
                    throw new SequenceEntityFailedException("Not enough points to model");
                }

                if (!ManualMode) {
                    if (MappingRunState.Equals("Idle", StringComparison.OrdinalIgnoreCase)) {
                        await appm.Start(ct);

                        while (!MappingRunState.Equals("Running", StringComparison.OrdinalIgnoreCase)) {
                            Logger.Info($"Waiting for MappingRunState=Running");
                            progress?.Report(new ApplicationStatus { Status = "Waiting for APPM mapping to start" });

                            await Task.Delay(TimeSpan.FromSeconds(2), ct);
                            ThrowIfStatusTaskFaulted(updateStatusTask);
                        }

                        while (MappingRunState.Equals("Running", StringComparison.OrdinalIgnoreCase)) {
                            Logger.Info($"Mapping points progress: {CurrentPoint} / {TotalPoints}");
                            progress?.Report(new ApplicationStatus { Status = $"Mapping point {CurrentPoint} / {TotalPoints}" });

                            await Task.Delay(TimeSpan.FromSeconds(2), ct);
                            ThrowIfStatusTaskFaulted(updateStatusTask);
                        }

                        progress?.Report(new ApplicationStatus { Status = $"Mapping run completed" });
                        Logger.Info($"APPM mapping run has finished. MappingRunState={MappingRunState}");
                    }

                    if (!DoNotExit) {
                        await appm.Close(ct);
                    }
                } else if (proc != null) {
                    await proc.WaitForExitAsync(ct);
                }
            } catch (OperationCanceledException) {
                Logger.Info($"Cancellation requested");
                await appm.Stop(CancellationToken.None);

                MappingRunState = "Cancelled";

                if (!DoNotExit) {
                    await appm.Close(CancellationToken.None);
                }

                throw new SequenceEntityFailedException($"{Name} was cancelled.");
            } catch (SequenceEntityFailedException ex) {
                Logger.Info($"{ex.Message}");
                await appm.Close(CancellationToken.None);
                MappingRunState = "Failed";
                throw;
            } finally {
                updateStatusTaskCts.Cancel();

                if (updateStatusTask != null) {
                    try {
                        await updateStatusTask;
                    } catch (Exception ex) {
                        Logger.Debug($"Status update task ended with {ex.GetType()}: {ex.Message}");
                    }
                }

                updateStatusTaskCts.Dispose();

                if (ownsProcess) {
                    proc?.Dispose();
                }

                // The user's token may already be cancelled at this point, so cleanup must not use it.
                if (originalFilter != null && filterWheelMediator.GetInfo().Connected) {
                    await filterWheelMediator.ChangeFilter(originalFilter, CancellationToken.None, progress);
                }

                if (stoppedGuiding && guiderMediator.GetInfo().Connected) {
                    await guiderMediator.StartGuiding(false, progress, CancellationToken.None);
                }
            }

            MappingRunState = "Completed";
            progress?.Report(new ApplicationStatus { Status = string.Empty });

            return;
        }

        public override object Clone() {
            return new CreateAllSkyModel(this) {
                ManualMode = ManualMode,
                DoNotExit = DoNotExit,
            };
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {Name}, ManualMode: {ManualMode}, DotNotExit: {DoNotExit}, Exe Path: {options.APPMExePath}, Settings: {options.APPMSettingsPath}";
        }

        public IList<string> Issues { get; set; } = new ObservableCollection<string>();

        public bool Validate() {
            var i = new List<string>();

            if (!cameraMediator.GetInfo().Connected) {
                i.Add($"Camera is not connected");
            }

            if (string.IsNullOrEmpty(options.APPMExePath) || !File.Exists(options.APPMExePath)) {
                i.Add("Invalid location for ApPointMapper.exe");
            }

            if (!string.IsNullOrEmpty(options.APPMSettingsPath) && !File.Exists(options.APPMSettingsPath)) {
                i.Add("Invalid location for APPM settings file");
            }

            if (AppmFileVersion < AstroPhysicsTools.MinAppmVersion) {
                i.Add($"APPM version {AppmFileVersion} is too old. This instruction requires {AstroPhysicsTools.MinAppmVersion} or higher");
            }

            if (!i.SequenceEqual(Issues)) {
                Issues = i;
                RaisePropertyChanged(nameof(Issues));
            }

            return i.Count == 0;
        }

        private async Task UpdateStatus(CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                try {
                    Logger.Debug("Updating APPM stats...");

                    var runStatus = await appm.Status(ct);
                    CurrentPoint = runStatus.Status.MeasurementPointsCount;
                    MappingRunState = runStatus.Status.MappingRunState;

                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                } catch (OperationCanceledException) {
                    Logger.Debug("Cancellation requested. Update task is exiting");
                    return;
                } catch (Exception ex) {
                    Logger.Debug($"Update task failed: {ex.GetType()}, {ex.Message}");
                    throw;
                }
            }
        }

        // Without this the Execute loops would spin until the user's token trips if the status
        // poller died, because MappingRunState would never change again.
        private static void ThrowIfStatusTaskFaulted(Task updateStatusTask) {
            if (updateStatusTask.IsFaulted) {
                throw new SequenceEntityFailedException($"Lost contact with the APPM API: {updateStatusTask.Exception?.GetBaseException().Message}");
            }

            if (updateStatusTask.IsCompleted) {
                throw new SequenceEntityFailedException("The APPM status update task stopped unexpectedly");
            }
        }

        private Process RunAPPM(out bool ownsProcess) {
            Process[] procs = Process.GetProcessesByName("ApPointMapper");

            try {
                if (procs.Length > 0) {
                    Logger.Info($"ApPointMapper.exe is already running as PID {procs[0].Id}");
                    ownsProcess = false;
                    return procs[0];
                }
            } finally {
                for (int i = 1; i < procs.Length; i++) {
                    procs[i].Dispose();
                }
            }

            var startInfo = new ProcessStartInfo(options.APPMExePath);

            if (DoNotExit) {
                startInfo.ArgumentList.Add("-dontexit");
            }

            if (File.Exists(options.APPMSettingsPath)) {
                startInfo.ArgumentList.Add($"-s{options.APPMSettingsPath}");
            }

            Logger.Info($"Executing: {startInfo.FileName} {string.Join(" ", startInfo.ArgumentList)}");

            ownsProcess = true;
            return Process.Start(startInfo);
        }
    }
}