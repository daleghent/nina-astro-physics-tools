#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

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
    [ExportMetadata("Category", "Astro-Physics Tools (Beta)")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CreateAllSkyModel : SequenceItem, IValidatable, INotifyPropertyChanged {
        private bool manualMode = false;
        private bool doNotExit = false;
        private int totalPoints = 0;
        private int currentPoint = 0;
        private string mappingRunState = "Unknown";
        private AppmApi.AppmApi appm = null;
        private IProfileService profileService;
        private ICameraMediator cameraMediator;
        private IFilterWheelMediator filterWheelMediator;
        private IGuiderMediator guiderMediator;

        private readonly Version minVersion = new Version(1, 9, 2, 3);

        [ImportingConstructor]
        public CreateAllSkyModel(IProfileService profileService, ICameraMediator cameraMediator, IFilterWheelMediator filterWheelMediator, IGuiderMediator guiderMediator) {
            APPMExePath = Properties.Settings.Default.APPMExePath;
            APPMSettingsPath = Properties.Settings.Default.APPMSettingsPath;

            AppmSetSlewRate = Properties.Settings.Default.AppmSetSlewRate;
            AppmSlewRate = Properties.Settings.Default.AppmSlewRate;
            AppmSlewSettleTime = Properties.Settings.Default.AppmSlewSettleTime;
            AppmZenithSafetyDistance = Properties.Settings.Default.AppmZenithSafetyDistance;
            AppmZenithSyncDistance = Properties.Settings.Default.AppmZenithSyncDistance;
            AppmUseMinAltitude = Properties.Settings.Default.AppmUseMinAltitude;
            AppmMinAltitude = Properties.Settings.Default.AppmMinAltitude;
            AppmUseMeridianLimits = Properties.Settings.Default.AppmUseMeridianLimits;
            AppmUseHorizonLimits = Properties.Settings.Default.AppmUseHorizonLimits;

            AllSkyCreateWestPoints = Properties.Settings.Default.AllSkyCreateWestPoints;
            AllSkyCreateEastPoints = Properties.Settings.Default.AllSkyCreateEastPoints;
            AllSkyPointOrderingStrategy = Properties.Settings.Default.AllSkyPointOrderingStrategy;
            AllSkyDeclinationSpacing = Properties.Settings.Default.AllSkyDeclinationSpacing;
            AllSkyDeclinationOffset = Properties.Settings.Default.AllSkyDeclinationOffset;
            AllSkyUseMinDeclination = Properties.Settings.Default.AllSkyUseMinDeclination;
            AllSkyUseMaxDeclination = Properties.Settings.Default.AllSkyUseMaxDeclination;
            AllSkyMinDeclination = Properties.Settings.Default.AllSkyMinDeclination;
            AllSkyMaxDeclination = Properties.Settings.Default.AllSkyMaxDeclination;
            AllSkyRightAscensionSpacing = Properties.Settings.Default.AllSkyRightAscensionSpacing;
            AllSkyRightAscensionOffset = Properties.Settings.Default.AllSkyRightAscensionOffset;
            AllSkyUseMinHourAngleEast = Properties.Settings.Default.AllSkyUseMinHourAngleEast;
            AllSkyUseMaxHourAngleWest = Properties.Settings.Default.AllSkyUseMaxHourAngleWest;
            AllSkyMinHourAngleEast = Properties.Settings.Default.AllSkyMinHourAngleEast;
            AllSkyMaxHourAngleWest = Properties.Settings.Default.AllSkyMaxHourAngleWest;

            Properties.Settings.Default.PropertyChanged += SettingsChanged;

            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.guiderMediator = guiderMediator;

            AppmFileVersion = Version.Parse(FileVersionInfo.GetVersionInfo(APPMExePath).ProductVersion);
        }

        public CreateAllSkyModel(CreateAllSkyModel copyMe) : this(copyMe.profileService, copyMe.cameraMediator, copyMe.filterWheelMediator, copyMe.guiderMediator) {
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

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken ct) {
            CancellationTokenSource updateStatusTaskCts = new CancellationTokenSource();
            CancellationToken updateStatusTaskCt = updateStatusTaskCts.Token;
            Task updateStatusTask = null;
            FilterInfo currentFilter = null;
            bool stoppedGuiding = false;
            appm = new AppmApi.AppmApi();

            var config = new AppmApi.AppmMeasurementConfiguration {
                SetSlewRate = AppmSetSlewRate,
                SlewRate = AppmSlewRate,
                SlewSettleTime = AppmSlewSettleTime,
                ZenithSafetyDistance = AppmZenithSafetyDistance,
                ZenithSyncDistance = AppmZenithSyncDistance,
                UseMinAltitude = AppmUseMinAltitude,
                MinAltitude = AppmMinAltitude,

                CreateEastPoints = AllSkyCreateEastPoints,
                CreateWestPoints = AllSkyCreateWestPoints,
                UseMeridianLimits = AppmUseMeridianLimits,
                UseHorizonLimits = AppmUseHorizonLimits,
                DeclinationSpacing = AllSkyDeclinationSpacing,
                DeclinationOffset = AllSkyDeclinationOffset,
                UseMinDeclination = AllSkyUseMinDeclination,
                UseMaxDeclination = AllSkyUseMaxDeclination,
                RightAscensionSpacing = AllSkyRightAscensionSpacing,
                RightAscensionOffset = AllSkyRightAscensionOffset,
                UseMinHourAngleEast = AllSkyUseMinHourAngleEast,
                UseMaxHourAngleWest = AllSkyUseMaxHourAngleWest,
                PointOrderingStrategy = AllSkyPointOrderingStrategy,
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
            return $"Category: {Category}, Item: {nameof(CreateAllSkyModel)}, ManualMode: {ManualMode}, DotNotExit: {DoNotExit}, Exe Path: {APPMExePath}, Settings: {APPMSettingsPath}";
        }

        public IList<string> Issues { get; set; } = new ObservableCollection<string>();

        public bool Validate() {
            var i = new List<string>();

            if (!cameraMediator.GetInfo().Connected) {
                i.Add($"Camera is not connected");
            }

            if (string.IsNullOrEmpty(APPMExePath) || !File.Exists(APPMExePath)) {
                i.Add("Invalid location for ApPointMapper.exe");
            }

            if (!string.IsNullOrEmpty(APPMSettingsPath) && !File.Exists(APPMSettingsPath)) {
                i.Add("Invalid location for APPM settings file");
            }

            if (AppmFileVersion < minVersion) {
                i.Add($"APCC Pro/APPM version {AppmFileVersion} is too old. This instruction requires {minVersion} or higher");
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

        private string APPMExePath { get; set; }
        private string APPMSettingsPath { get; set; }
        private Version AppmFileVersion { get; set; }

        private bool AppmSetSlewRate { get; set; }
        private int AppmSlewRate { get; set; }
        private int AppmSlewSettleTime { get; set; }
        private double AppmZenithSafetyDistance { get; set; }
        private double AppmZenithSyncDistance { get; set; }
        private bool AppmUseMinAltitude { get; set; }
        private int AppmMinAltitude { get; set; }
        private bool AppmUseMeridianLimits { get; set; }
        private bool AppmUseHorizonLimits { get; set; }

        private bool AllSkyCreateWestPoints { get; set; }
        private bool AllSkyCreateEastPoints { get; set; }
        private int AllSkyPointOrderingStrategy { get; set; }
        private int AllSkyDeclinationSpacing { get; set; }
        private int AllSkyDeclinationOffset { get; set; }
        private bool AllSkyUseMinDeclination { get; set; }
        private bool AllSkyUseMaxDeclination { get; set; }
        private int AllSkyMinDeclination { get; set; }
        private int AllSkyMaxDeclination { get; set; }
        private int AllSkyRightAscensionSpacing { get; set; }
        private int AllSkyRightAscensionOffset { get; set; }
        private bool AllSkyUseMinHourAngleEast { get; set; }
        private bool AllSkyUseMaxHourAngleWest { get; set; }
        private double AllSkyMinHourAngleEast { get; set; }
        private double AllSkyMaxHourAngleWest { get; set; }

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

            if (File.Exists(APPMSettingsPath)) {
                args.Add($"-s{APPMSettingsPath}");
            }

            var appm = new ProcessStartInfo(APPMExePath) {
                Arguments = string.Join(" ", args.ToArray())
            };

            Logger.Info($"Executing: {appm.FileName} {appm.Arguments}");
            return Process.Start(appm);
        }

        private void SettingsChanged(object sender, PropertyChangedEventArgs e) {
            switch (e.PropertyName) {
                case "APPMExePath":
                    APPMExePath = Properties.Settings.Default.APPMExePath;
                    break;

                case "APPMSettingsPath":
                    APPMSettingsPath = Properties.Settings.Default.APPMSettingsPath;
                    break;

                case "AppmSetSlewRate":
                    AppmSetSlewRate = Properties.Settings.Default.AppmSetSlewRate;
                    break;

                case "AppmSlewRate":
                    AppmSlewRate = Properties.Settings.Default.AppmSlewRate;
                    break;

                case "AppmSlewSettleTime":
                    AppmSlewSettleTime = Properties.Settings.Default.AppmSlewSettleTime;
                    break;

                case "AppmZenithSafetyDistance":
                    AppmZenithSafetyDistance = Properties.Settings.Default.AppmZenithSafetyDistance;
                    break;

                case "AppmZenithSyncDistance":
                    AppmZenithSyncDistance = Properties.Settings.Default.AppmZenithSyncDistance;
                    break;

                case "AppmUseMinAltitude":
                    AppmUseMinAltitude = Properties.Settings.Default.AppmUseMinAltitude;
                    break;

                case "AppmMinAltitude":
                    AppmMinAltitude = Properties.Settings.Default.AppmMinAltitude;
                    break;

                case "AppmUseMeridianLimits":
                    AppmUseMeridianLimits = Properties.Settings.Default.AppmUseMeridianLimits;
                    break;

                case "AppmUseHorizonLimits":
                    AppmUseHorizonLimits = Properties.Settings.Default.AppmUseHorizonLimits;
                    break;

                case "AllSkyCreateWestPoints":
                    AllSkyCreateWestPoints = Properties.Settings.Default.AllSkyCreateWestPoints;
                    break;

                case "AllSkyCreateEastPoints":
                    AllSkyCreateEastPoints = Properties.Settings.Default.AllSkyCreateEastPoints;
                    break;

                case "AllSkyPointOrderingStrategy":
                    AllSkyPointOrderingStrategy = Properties.Settings.Default.AllSkyPointOrderingStrategy;
                    break;

                case "AllSkyDeclinationSpacing":
                    AllSkyDeclinationSpacing = Properties.Settings.Default.AllSkyDeclinationSpacing;
                    break;

                case "AllSkyDeclinationOffset":
                    AllSkyDeclinationOffset = Properties.Settings.Default.AllSkyDeclinationOffset;
                    break;

                case "AllSkyUseMinDeclination":
                    AllSkyUseMinDeclination = Properties.Settings.Default.AllSkyUseMinDeclination;
                    break;

                case "AllSkyUseMaxDeclination":
                    AllSkyUseMaxDeclination = Properties.Settings.Default.AllSkyUseMaxDeclination;
                    break;

                case "AllSkyMinDeclination":
                    AllSkyMinDeclination = Properties.Settings.Default.AllSkyMinDeclination;
                    break;

                case "AllSkyMaxDeclination":
                    AllSkyMaxDeclination = Properties.Settings.Default.AllSkyMaxDeclination;
                    break;

                case "AllSkyRightAscensionSpacing":
                    AllSkyRightAscensionSpacing = Properties.Settings.Default.AllSkyRightAscensionSpacing;
                    break;

                case "AllSkyRightAscensionOffset":
                    AllSkyRightAscensionOffset = Properties.Settings.Default.AllSkyRightAscensionOffset;
                    break;

                case "AllSkyUseMinHourAngleEast":
                    AllSkyUseMinHourAngleEast = Properties.Settings.Default.AllSkyUseMinHourAngleEast;
                    break;

                case "AllSkyUseMaxHourAngleWest":
                    AllSkyUseMaxHourAngleWest = Properties.Settings.Default.AllSkyUseMaxHourAngleWest;
                    break;

                case "AllSkyMinHourAngleEast":
                    AllSkyMinHourAngleEast = Properties.Settings.Default.AllSkyMinHourAngleEast;
                    break;

                case "AllSkyMaxHourAngleWest":
                    AllSkyMaxHourAngleWest = Properties.Settings.Default.AllSkyMaxHourAngleWest;
                    break;
            }
        }
    }
}