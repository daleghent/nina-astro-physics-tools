#region "copyright"

/*
    Copyright 2021-2024 Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using DaleGhent.NINA.AstroPhysicsTools.Interfaces;
using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
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
    [ExportMetadata("Category", "Astro-Physics Tools")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CreateDecArcModel : SequenceItem, IValidatable, INotifyPropertyChanged {
        private double hourAngleLeadIn;
        private double hourAngleTail;
        private bool manualMode = false;
        private bool doNotExit = false;
        private bool doFullArc = false;
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
        public CreateDecArcModel(IProfileService profileService, ICameraMediator cameraMediator, IFilterWheelMediator filterWheelMediator, IGuiderMediator guiderMediator) : this(profileService, cameraMediator, filterWheelMediator, guiderMediator, AstroPhysicsTools.AstroPhysicsToolsOptions) {
        }

        public CreateDecArcModel(IProfileService profileService, ICameraMediator cameraMediator, IFilterWheelMediator filterWheelMediator, IGuiderMediator guiderMediator, IAstroPhysicsToolsOptions options) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.guiderMediator = guiderMediator;
            this.options = options;

            if (File.Exists(options.APPMExePath)) {
                AppmFileVersion = Version.Parse(FileVersionInfo.GetVersionInfo(options.APPMExePath).ProductVersion);
            }

            hourAngleLeadIn = options.DecArcHourAngleLeadIn;
            hourAngleTail = options.DecArcHourAngleTail;
        }

        public CreateDecArcModel(CreateDecArcModel copyMe) : this(copyMe.profileService, copyMe.cameraMediator, copyMe.filterWheelMediator, copyMe.guiderMediator, copyMe.options) {
            CopyMetaData(copyMe);
        }

        [JsonProperty]
        public double HourAngleLeadIn {
            get => hourAngleLeadIn;
            set {
                hourAngleLeadIn = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public double HourAngleTail {
            get => hourAngleTail;
            set {
                hourAngleTail = value;
                RaisePropertyChanged();
            }
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
            var updateStatusTaskCts = new CancellationTokenSource();
            CancellationToken updateStatusTaskCt = updateStatusTaskCts.Token;
            Task updateStatusTask = null;
            FilterInfo originalFilter = null;
            bool stoppedGuiding = false;
            appm = new AppmApi.AppmApi();

            var target = Utility.Utility.FindDsoInfo(this.Parent) ?? throw new SequenceEntityFailedException("No DSO has been defined");
            target.Coordinates = target.Coordinates.Transform(Epoch.JNOW);

            if (target.Coordinates.Dec > 85d || target.Coordinates.Dec < -85d) {
                string message = $"The target's declination of {target.Coordinates.DecString} is too close to the pole to create a meaningful model. Skipping model creation.";
                Logger.Warning(message);
                Notification.ShowWarning(message);

                return;
            }

            var decArcParams = CalculateDecArcParameters(target);

            var config = new AppmApi.AppmMeasurementConfiguration() {
                SetSlewRate = options.AppmSetSlewRate,
                SlewRate = options.AppmSlewRate,
                SlewSettleTime = options.AppmSlewSettleTime,
                ZenithSafetyDistance = options.AppmZenithSafetyDistance,
                ZenithSyncDistance = options.AppmZenithSyncDistance,
                UseMinAltitude = options.AppmUseMinAltitude,
                MinAltitude = options.AppmMinAltitude,
                UseMaxDeclination = true,
                UseMinDeclination = true,
                UseMaxHourAngleWest = true,
                UseMinHourAngleEast = true,
                CreateEastPoints = true,
                CreateWestPoints = true,
                UseMeridianLimits = options.AppmUseMeridianLimits,
                UseHorizonLimits = options.AppmUseHorizonLimits,
                RightAscensionOffset = 0,
                DeclinationSpacing = decArcParams.DecSpacing,
                MaxDeclination = decArcParams.NorthDecLimit,
                MinDeclination = decArcParams.SouthDecLimit,
                DeclinationOffset = decArcParams.DecOffset,
                RightAscensionSpacing = decArcParams.RaSpacing,
                MinHourAngleEast = DoFullArc ? -12d : decArcParams.EastHaLimit,
                MaxHourAngleWest = DoFullArc ? 12d : decArcParams.WestHaLimit,
                PointOrderingStrategy = (90 - Math.Abs(decArcParams.TargetDec)) <= decArcParams.PolarProximityLimit
                    ? decArcParams.PolarPointOrderingStrategy : options.DecArcPointOrderingStrategy,
            };

            Logger.Info($"Dec: T={decArcParams.TargetDec:0.00}, N={config.MaxDeclination:0.00}, S={config.MinDeclination:0.00}, Spread={config.MaxDeclination - config.MinDeclination}, Spacing={options.DecArcDecSpacing}, Offset={config.DeclinationOffset}");

            var request = new AppmApi.AppmMeasurementConfigurationRequest() {
                Configuration = config,
            };

            if (guiderMediator.GetInfo().Connected) {
                stoppedGuiding = await guiderMediator.StopGuiding(ct);
            }

            if (filterWheelMediator.GetInfo().Connected) {
                originalFilter = filterWheelMediator.GetInfo().SelectedFilter;
                await filterWheelMediator.ChangeFilter(profileService.ActiveProfile.PlateSolveSettings.Filter, ct, progress);
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
                    if (MappingRunState.Equals("Idle", StringComparison.InvariantCultureIgnoreCase)) {
                        await appm.Start(ct);

                        while (!MappingRunState.Equals("Running", StringComparison.InvariantCultureIgnoreCase)) {
                            Logger.Info($"Waiting for MappingRunState=Running");
                            progress?.Report(new ApplicationStatus { Status = "Waiting for APPM mapping to start" });

                            await Task.Delay(TimeSpan.FromSeconds(2), ct);
                        }

                        while (MappingRunState.Equals("Running", StringComparison.InvariantCultureIgnoreCase)) {
                            Logger.Info($"Mapping points progress: {CurrentPoint} / {TotalPoints}");
                            progress?.Report(new ApplicationStatus { Status = $"Mapping point {CurrentPoint} / {TotalPoints}" });

                            await Task.Delay(TimeSpan.FromSeconds(2), ct);
                        }

                        progress?.Report(new ApplicationStatus { Status = $"Mapping run completed" });
                        Logger.Info($"APPM mapping run has finished. MappingRunState={MappingRunState}");
                        updateStatusTaskCts.Cancel();
                        updateStatusTask.Wait(ct);
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

                throw new SequenceEntityFailedException($"{Name} for {target.Name} was cancelled.");
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
                    await filterWheelMediator.ChangeFilter(originalFilter, ct, progress);
                }

                if (guiderMediator.GetInfo().Connected && stoppedGuiding) {
                    await guiderMediator.StartGuiding(false, progress, ct);
                }
            }

            MappingRunState = "Completed";
            progress?.Report(new ApplicationStatus { Status = string.Empty });

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
            return $"Category: {Category}, Item: {Name}, HATail={hourAngleTail:0.00}, DoFullArc={DoFullArc}, ManualMode={ManualMode}, DotNotExit={DoNotExit}, ExePath={options.APPMExePath}, Settings={options.APPMSettingsPath}";
        }

        public IList<string> Issues { get; set; } = new ObservableCollection<string>();

        public bool Validate() {
            var i = new List<string>();

            if (AppmFileVersion < AstroPhysicsTools.MinAppmVersion) {
                i.Add($"APCC Pro/APPM version {AppmFileVersion} is too old. This instruction requires {AstroPhysicsTools.MinAppmVersion} or higher");
            }

            if (!cameraMediator.GetInfo().Connected) {
                i.Add($"Camera is not connected");
            }

            if (Utility.Utility.FindDsoInfo(this.Parent) == null) {
                i.Add("No DSO has been defined");
            }

            if (string.IsNullOrEmpty(options.APPMExePath) || !File.Exists(options.APPMExePath)) {
                i.Add("Invalid location for ApPointMapper.exe");
            }

            if (!string.IsNullOrEmpty(options.APPMSettingsPath) && !File.Exists(options.APPMSettingsPath)) {
                i.Add("Invalid location for APPM settings file");
            }

            if (i != Issues) {
                Issues = i;
                RaisePropertyChanged(nameof(Issues));
            }

            return i.Count == 0;
        }

        private Process RunAPPM() {
            Process[] proc = Process.GetProcessesByName("ApPointMapper");

            if (proc.Length > 0) {
                Logger.Info($"ApPointMapper.exe is already running as PID {proc[0].Id}");
                return proc[0];
            }

            var args = new List<string>();

            if (DoNotExit) {
                args.Add("-dontexit");
            }

            if (File.Exists(options.APPMSettingsPath)) {
                args.Add($"-s{options.APPMSettingsPath}");
            }

            var appm = new ProcessStartInfo(options.APPMExePath) {
                Arguments = string.Join(" ", [.. args])
            };

            Logger.Info($"Executing: {appm.FileName} {appm.Arguments}");
            return Process.Start(appm);
        }

        private DecArcParameters CalculateDecArcParameters(IDeepSkyObject target) {
            var latitude = profileService.ActiveProfile.AstrometrySettings.Latitude;
            var longitude = profileService.ActiveProfile.AstrometrySettings.Longitude;

            var timeNow = DateTime.UtcNow;
            var sunRiseTime = AstroUtil.GetSunRiseAndSet(timeNow, latitude, longitude).Rise.Value;

            if (timeNow > sunRiseTime) {
                sunRiseTime = AstroUtil.GetSunRiseAndSet(timeNow.AddDays(1), latitude, longitude).Rise.Value;
            }

            var targetHaNow = HourAngle24to12(AstroUtil.GetHourAngle(AstroUtil.GetLocalSiderealTimeNow(longitude), target.Coordinates.RA));
            var targetHaAtSunrise = targetHaNow + (sunRiseTime - timeNow).TotalHours;

            var decArcStart = targetHaNow - hourAngleLeadIn;
            var decArcEnd = targetHaAtSunrise + hourAngleTail;

            decArcStart = Math.Max(decArcStart, -12d);
            decArcStart = Math.Min(decArcStart, 12d);

            decArcEnd = Math.Max(decArcEnd, -12d);
            decArcEnd = Math.Min(decArcEnd, 12d);

            // Create paramters object with calculations.
            var decArcParams = new DecArcParameters {
                ArcQuantity = options.DecArcQuantity,
                DecSpacing = options.DecArcDecSpacing,
                RaSpacing = options.DecArcRaSpacing,
                PointOrderingStrategy = options.DecArcPointOrderingStrategy,
                PolarPointOrderingStrategy = options.DecArcPolarPointOrderingStrategy,
                PolarProximityLimit = options.DecArcPolarProximityLimit,
                TargetDec = (int)Math.Round(target.Coordinates.Dec),
                EastHaLimit = decArcStart,
                WestHaLimit = decArcEnd,
            };

            if (decArcParams.ArcQuantity == 1) {
                decArcParams.DecSpacing = 1;
                decArcParams.NorthDecLimit = decArcParams.SouthDecLimit = decArcParams.TargetDec;
            } else {
                var totalSpan = (decArcParams.ArcQuantity - 1) * decArcParams.DecSpacing;
                decArcParams.SouthDecLimit = Math.Max(-85, (int)Math.Floor(target.Coordinates.Dec - (totalSpan / 2)));
                decArcParams.NorthDecLimit = Math.Min(85, decArcParams.SouthDecLimit + totalSpan);
                decArcParams.DecOffset = decArcParams.SouthDecLimit % decArcParams.DecSpacing;
            }

            Logger.Info($"Target RA: {target.Coordinates.RAString}, Target Current HA: {targetHaNow:0.00}, Target HA at sunrise: {targetHaAtSunrise:0.00}, Sunrise Time: {sunRiseTime}");
            Logger.Info($"DecArc HA start: {decArcStart:0.00}, DecArc HA end: {decArcEnd:0.00}, Total DecArc length: {(decArcEnd - decArcStart):0.00} hours");

            return decArcParams;
        }

        // Converts 24h format hour angle to 12h format
        private static double HourAngle24to12(double ha) {
            ha %= 24d;

            if (ha < -12d) {
                ha += 24d;
            } else if (ha > 12d) {
                ha -= 24d;
            }

            return ha;
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
                    MappingRunState = "Failed";
                    throw;
                }
            }
        }

        private Version AppmFileVersion { get; set; }

        private class DecArcParameters {
            public int TargetDec { get; set; } = 0;
            public int NorthDecLimit { get; set; } = 0;
            public int SouthDecLimit { get; set; } = 0;
            public int DecOffset { get; set; } = 0;
            public int ArcQuantity { get; set; } = 0;
            public int DecSpacing { get; set; } = 0;
            public int RaSpacing { get; set; } = 0;
            public double EastHaLimit { get; set; } = -12;
            public double WestHaLimit { get; set; } = 12;
            public double HaLeadIn { get; set; } = 0;
            public double HaTail { get; set; } = 0;
            public int PointOrderingStrategy { get; set; } = 0;
            public int PolarPointOrderingStrategy { get; set; } = 0;
            public int PolarProximityLimit { get; set; } = 0;
        }
    }
}