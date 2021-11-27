#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Astrometry;
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

namespace DaleGhent.NINA.AstroPhysicsTools.CreateDecArcModel {

    [ExportMetadata("Name", "Create Dec Arc Model")]
    [ExportMetadata("Description", "Runs Astro-Physics Point Mapper (APPM) in automatic mode for unattended dec arc model creation")]
    [ExportMetadata("Icon", "DecArc_SVG")]
    [ExportMetadata("Category", "Astro-Physics Tools (Beta)")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CreateDecArcModel : SequenceItem, IValidatable, INotifyPropertyChanged {
        private bool manualMode = false;
        private bool doNotExit = false;
        private bool doFullArc = false;
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
        public CreateDecArcModel(IProfileService profileService, ICameraMediator cameraMediator, IFilterWheelMediator filterWheelMediator, IGuiderMediator guiderMediator) {
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

            DecArcRaSpacing = Properties.Settings.Default.DecArcRaSpacing;
            DecArcDecSpacing = Properties.Settings.Default.DecArcDecSpacing;
            DecArcHourAngleLeadIn = Properties.Settings.Default.DecArcHourAngleLeadIn;
            DecArcQuantity = Properties.Settings.Default.DecArcQuantity;
            DecArcPointOrderingStrategy = Properties.Settings.Default.DecArcPointOrderingStrategy;
            DecArcPolarPointOrderingStrategy = Properties.Settings.Default.DecArcPolarPointOrderingStrategy;
            DecArcPolarProximityLimit = Properties.Settings.Default.DecArcPolarProximityLimit;

            Properties.Settings.Default.PropertyChanged += SettingsChanged;

            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.guiderMediator = guiderMediator;

            AppmFileVersion = Version.Parse(FileVersionInfo.GetVersionInfo(APPMExePath).ProductVersion);
        }

        public CreateDecArcModel(CreateDecArcModel copyMe) : this(copyMe.profileService, copyMe.cameraMediator, copyMe.filterWheelMediator, copyMe.guiderMediator) {
            CopyMetaData(copyMe);
        }

        [JsonProperty]
        public bool DoFullArc {
            get => doFullArc;
            set {
                doFullArc = value;
                RaisePropertyChanged();
            }
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

            var target = Utility.Utility.FindDsoInfo(this.Parent);

            if (target == null) {
                throw new SequenceEntityFailedException("No DSO has been defined");
            }

            target.Coordinates = target.Coordinates.Transform(Epoch.JNOW);

            if (target.Coordinates.Dec > 85d || target.Coordinates.Dec < -85d) {
                Logger.Info($"The target's declination of {target.Coordinates.DecString} is too close to the pole to create a meaningful model. Skipping model creation.");
                return;
            }

            var decArcParams = CalculateDecArcParameters(target);

            Logger.Info($"RA: HourAngleStart={decArcParams.EastHaLimit:0.00}, HourAngleEnd={decArcParams.WestHaLimit:0.00}, Hours={(decArcParams.WestHaLimit - Math.Abs(decArcParams.EastHaLimit)):0.00}");
            Logger.Info($"Dec: T={decArcParams.TargetDec:0.00}, N={decArcParams.NorthDecLimit:0.00}, S={decArcParams.SouthDecLimit:0.00}, Spread={decArcParams.NorthDecLimit - decArcParams.SouthDecLimit}, Spacing={DecArcDecSpacing}, Offset={decArcParams.DecOffset}");

            var config = new AppmApi.AppmMeasurementConfiguration() {
                SetSlewRate = AppmSetSlewRate,
                SlewRate = AppmSlewRate,
                SlewSettleTime = AppmSlewSettleTime,
                ZenithSafetyDistance = AppmZenithSafetyDistance,
                ZenithSyncDistance = AppmZenithSyncDistance,
                UseMinAltitude = AppmUseMinAltitude,
                MinAltitude = AppmMinAltitude,
                UseMaxDeclination = true,
                UseMinDeclination = true,
                UseMaxHourAngleWest = true,
                UseMinHourAngleEast = true,
                CreateEastPoints = true,
                CreateWestPoints = true,
                UseMeridianLimits = AppmUseMeridianLimits,
                UseHorizonLimits = AppmUseHorizonLimits,
                RightAscensionOffset = 0,
                DeclinationSpacing = decArcParams.DecSpacing,
                MaxDeclination = decArcParams.NorthDecLimit,
                MinDeclination = decArcParams.SouthDecLimit,
                DeclinationOffset = decArcParams.DecOffset,
                RightAscensionSpacing = decArcParams.RaSpacing,
                MinHourAngleEast = decArcParams.EastHaLimit,
                MaxHourAngleWest = decArcParams.WestHaLimit,
                PointOrderingStrategy = (90 - Math.Abs(decArcParams.TargetDec)) <= decArcParams.PolarProximityLimit
                    ? decArcParams.PolarPointOrderingStrategy : DecArcPointOrderingStrategy,
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
            return new CreateDecArcModel(this) {
                DoFullArc = DoFullArc,
                ManualMode = ManualMode,
                DoNotExit = DoNotExit,
            };
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(CreateDecArcModel)}, DoFullArc={DoFullArc}, ManualMode={ManualMode}, DotNotExit={DoNotExit}, ExePath={APPMExePath}, Settings={APPMSettingsPath}";
        }

        public IList<string> Issues { get; set; } = new ObservableCollection<string>();

        public bool Validate() {
            var i = new List<string>();

            if (AppmFileVersion < minVersion) {
                i.Add($"APCC Pro/APPM version {AppmFileVersion} is too old. This instruction requires {minVersion} or higher");
            }

            if (!cameraMediator.GetInfo().Connected) {
                i.Add($"Camera is not connected");
            }

            if (Utility.Utility.FindDsoInfo(this.Parent) == null) {
                i.Add("No DSO has been defined");
            }

            if (string.IsNullOrEmpty(APPMExePath) || !File.Exists(APPMExePath)) {
                i.Add("Invalid location for ApPointMapper.exe");
            }

            if (!string.IsNullOrEmpty(APPMSettingsPath) && !File.Exists(APPMSettingsPath)) {
                i.Add("Invalid location for APPM settings file");
            }

            if (i != Issues) {
                Issues = i;
                RaisePropertyChanged("Issues");
            }

            return i.Count == 0;
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

            if (File.Exists(APPMSettingsPath)) {
                args.Add($"-s{APPMSettingsPath}");
            }

            var appm = new ProcessStartInfo(APPMExePath) {
                Arguments = string.Join(" ", args.ToArray())
            };

            Logger.Info($"Executing: {appm.FileName} {appm.Arguments}");
            return Process.Start(appm);
        }

        private double CurrentHourAngle(DeepSkyObject target) {
            // We want HA in terms of -12..12, not 0..24
            return ((AstroUtil.GetHourAngle(AstroUtil.GetLocalSiderealTimeNow(profileService.ActiveProfile.AstrometrySettings.Longitude), target.Coordinates.RA) + 36) % 24) - 12;
        }

        private DecArcParameters CalculateDecArcParameters(DeepSkyObject target) {
            var decArcParams = new DecArcParameters() {
                ArcQuantity = DecArcQuantity,
                DecSpacing = DecArcDecSpacing,
                RaSpacing = DecArcRaSpacing,
                PointOrderingStrategy = DecArcPointOrderingStrategy,
                PolarPointOrderingStrategy = DecArcPolarPointOrderingStrategy,
                PolarProximityLimit = DecArcPolarProximityLimit,
            };

            decArcParams.TargetHa = CurrentHourAngle(target);
            decArcParams.EastHaLimit = DoFullArc ? -12d : Math.Round(Math.Max(decArcParams.TargetHa - decArcParams.HaLeadIn, -12), 2);

            decArcParams.TargetDec = (int)Math.Round(target.Coordinates.Dec);

            if (decArcParams.ArcQuantity == 1) {
                decArcParams.DecSpacing = 1;
                decArcParams.NorthDecLimit = decArcParams.SouthDecLimit = decArcParams.TargetDec;
            } else {
                var totalSpan = (decArcParams.ArcQuantity - 1) * decArcParams.DecSpacing;
                decArcParams.SouthDecLimit = Math.Max(-85, (int)Math.Round(target.Coordinates.Dec - (totalSpan / 2)));
                decArcParams.NorthDecLimit = Math.Min(85, decArcParams.SouthDecLimit + totalSpan);
                decArcParams.DecOffset = decArcParams.SouthDecLimit % decArcParams.DecSpacing;
            }

            return decArcParams;
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

        private bool AppmSetSlewRate { get; set; }
        private int AppmSlewRate { get; set; }
        private int AppmSlewSettleTime { get; set; }
        private double AppmZenithSafetyDistance { get; set; }
        private double AppmZenithSyncDistance { get; set; }
        private bool AppmUseMinAltitude { get; set; }
        private int AppmMinAltitude { get; set; }
        private bool AppmUseMeridianLimits { get; set; }
        private bool AppmUseHorizonLimits { get; set; }

        private int DecArcRaSpacing { get; set; }
        private int DecArcDecSpacing { get; set; }
        private double DecArcHourAngleLeadIn { get; set; }
        private int DecArcQuantity { get; set; }
        private int DecArcPointOrderingStrategy { get; set; }
        private int DecArcPolarPointOrderingStrategy { get; set; }
        private int DecArcPolarProximityLimit { get; set; }

        private Version AppmFileVersion { get; set; }

        private void SettingsChanged(object sender, PropertyChangedEventArgs e) {
            switch (e.PropertyName) {
                case "APPMExePath":
                    APPMExePath = Properties.Settings.Default.APPMExePath;
                    break;

                case "APPMSettingsPath":
                    APPMSettingsPath = Properties.Settings.Default.APPMSettingsPath;
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

                case "DecArcRaSpacing":
                    DecArcRaSpacing = Properties.Settings.Default.DecArcRaSpacing;
                    break;

                case "DecArcDecSpacing":
                    DecArcDecSpacing = Properties.Settings.Default.DecArcDecSpacing;
                    break;

                case "DecArcQuantity":
                    DecArcQuantity = Properties.Settings.Default.DecArcQuantity;
                    break;

                case "DecArcHourAngleLeadIn":
                    DecArcHourAngleLeadIn = Properties.Settings.Default.DecArcHourAngleLeadIn;
                    break;

                case "DecArcPointOrderingStrategy":
                    DecArcPointOrderingStrategy = Properties.Settings.Default.DecArcPointOrderingStrategy;
                    break;

                case "DecArcPolarPointOrderingStrategy":
                    DecArcPolarPointOrderingStrategy = Properties.Settings.Default.DecArcPolarPointOrderingStrategy;
                    break;

                case "DecArcPolarProximityLimit":
                    DecArcPolarProximityLimit = Properties.Settings.Default.DecArcPolarProximityLimit;
                    break;
            }
        }

        private class DecArcParameters {
            public int TargetDec { get; set; } = 0;
            public int NorthDecLimit { get; set; } = 0;
            public int SouthDecLimit { get; set; } = 0;
            public int DecOffset { get; set; } = 0;
            public int ArcQuantity { get; set; } = 0;
            public int DecSpacing { get; set; } = 0;
            public int RaSpacing { get; set; } = 0;
            public double TargetHa { get; set; } = 0;
            public double EastHaLimit { get; set; } = -12;
            public double WestHaLimit { get; set; } = 12;
            public double HaLeadIn { get; set; } = 0;
            public int PointOrderingStrategy { get; set; } = 0;
            public int PolarPointOrderingStrategy { get; set; } = 0;
            public int PolarProximityLimit { get; set; } = 0;
        }
    }
}