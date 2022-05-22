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
            CancellationTokenSource updateStatusTaskCts = new CancellationTokenSource();
            CancellationToken updateStatusTaskCt = updateStatusTaskCts.Token;
            Task updateStatusTask = null;
            FilterInfo currentFilter = null;
            bool stoppedGuiding = false;
            appm = new AppmApi.AppmApi();

            var config = new AppmApi.AppmMeasurementConfiguration {
                SetSlewRate = options.AppmSetSlewRate,
                SlewRate = options.AppmSlewRate,
                SlewSettleTime = options.AppmSlewSettleTime,
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

            if (filterWheelMediator.GetInfo().Connected) {
                currentFilter = filterWheelMediator.GetInfo().SelectedFilter;
                await filterWheelMediator.ChangeFilter(profileService.ActiveProfile.PlateSolveSettings.Filter, ct);
            }

            var proc = RunAPPM();

            try {
                MappingRunState = appm.WaitForApiInit(ct).Result.Status.MappingRunState;
                updateStatusTask = UpdateStatus(updateStatusTaskCt);

                var response = appm.SetConfiguration(request, ct);

                if (!response.Result.Success) {
                    throw new SequenceEntityFailedException("Could not set APPM configuration");
                };

                TotalPoints = response.Result.PointCount;

                if (TotalPoints == 0) {
                    Logger.Warning($"Total point count is {TotalPoints}. Exiting without running model");
                    Notification.ShowWarning($"The point count for this mapping run is {TotalPoints}. The mapping run will not start. This is not an error, but it's perhaps not what you intended.");
                    throw new OperationCanceledException("Not enough points to model");
                }

                if (!ManualMode) {
                    if (MappingRunState.Equals("Idle")) {
                        await appm.Start(ct);

                        while (!MappingRunState.Equals("Running")) {
                            Logger.Info($"Waiting for MappingRunState=Running");
                            await Task.Delay(TimeSpan.FromSeconds(2), ct);
                        }

                        while (MappingRunState.Equals("Running")) {
                            Logger.Info($"Mapping points progress: {CurrentPoint} / {TotalPoints}");
                            await Task.Delay(TimeSpan.FromSeconds(2), ct);
                        }

                        Logger.Info($"APPM mapping run has finished. MappingRunState={MappingRunState}");
                    }

                    if (!DoNotExit) {
                        await appm.Close(ct);
                    }
                } else {
                    proc.WaitForExit();
                }
            } catch (OperationCanceledException) {
                Logger.Info($"Cancellation requested");
                await appm.Stop(CancellationToken.None);

                MappingRunState = "Cancelled";

                if (!DoNotExit) {
                    await appm.Close(CancellationToken.None);
                }
            } catch (SequenceEntityFailedException ex) {
                Logger.Info($"{ex.Message}");
                await appm.Close(CancellationToken.None);
                MappingRunState = "Failed";
                throw;
            } finally {
                if (updateStatusTask != null) {
                    updateStatusTaskCts.Cancel();
                    updateStatusTask.Dispose();
                }

                updateStatusTaskCts.Dispose();
                proc.Dispose();

                if (filterWheelMediator.GetInfo().Connected) {
                    await filterWheelMediator.ChangeFilter(currentFilter, ct);
                }

                if (guiderMediator.GetInfo().Connected && stoppedGuiding) {
                    await guiderMediator.StartGuiding(false, progress, ct);
                }
            }

            MappingRunState = "Completed";

            return;
        }

        public override object Clone() {
            return new CreateAllSkyModel(this) {
                DoNotExit = DoNotExit,
            };
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(CreateAllSkyModel)}, ManualMode: {ManualMode}, DotNotExit: {DoNotExit}, Exe Path: {options.APPMExePath}, Settings: {options.APPMSettingsPath}";
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
                i.Add($"APCC Pro/APPM version {AppmFileVersion} is too old. This instruction requires {AstroPhysicsTools.MinAppmVersion} or higher");
            }

            if (i != Issues) {
                Issues = i;
                RaisePropertyChanged("Issues");
            }

            return i.Count == 0;
        }

        private async Task<Task> UpdateStatus(CancellationToken ct) {
            while (true) {
                try {
                    Logger.Debug("Updating APPM stats...");

                    var runStatus = await appm.Status(ct);
                    CurrentPoint = runStatus.Status.MeasurementPointsCount;
                    MappingRunState = runStatus.Status.MappingRunState;

                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                } catch (Exception ex) {
                    Logger.Debug($"Update task is exiting: {ex.GetType()}, {ex.Message}");
                    return Task.CompletedTask;
                }
            }
        }

        private Process RunAPPM() {
            Process[] proc = Process.GetProcessesByName("ApPointMapper");

            if (proc.Length > 0) {
                Logger.Info($"ApPointMapper.exe is already running as PID {proc[0].Id}");
                return proc[0];
            }

            List<string> args = new List<string>();

            if (DoNotExit) {
                args.Add("-dontexit");
            }

            if (File.Exists(options.APPMSettingsPath)) {
                args.Add($"-s{options.APPMSettingsPath}");
            }

            var appm = new ProcessStartInfo(options.APPMExePath) {
                Arguments = string.Join(" ", args.ToArray())
            };

            Logger.Info($"Executing: {appm.FileName} {appm.Arguments}");
            return Process.Start(appm);
        }
    }
}