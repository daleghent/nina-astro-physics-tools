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
using NINA.Core.Utility;
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

namespace DaleGhent.NINA.AstroPhysics.CreateDecArcModel {

    [ExportMetadata("Name", "Create Dec Arc Model")]
    [ExportMetadata("Description", "Runs Astro-Physics Point Mapper (APPM) in automatic mode for unattended dec arc model creation")]
    [ExportMetadata("Icon", "APPM_SVG")]
    [ExportMetadata("Category", "Astro-Physics Tools")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CreateDecArcModel : SequenceItem, IValidatable, INotifyPropertyChanged {
        private bool manualMode = false;
        private bool doNotExit = false;
        private bool doFullArc = false;
        private int totalPoints = 0;
        private int completedPoints = 0;
        private string modelStatus = string.Empty;
        private IProfileService profileService;

        [ImportingConstructor]
        public CreateDecArcModel(IProfileService profileService) {
            APPMExePath = Properties.Settings.Default.APPMExePath;
            APPMSettingsPath = Properties.Settings.Default.APPMSettingsPath;
            DecArcRaSpacing = Properties.Settings.Default.DecArcRaSpacing;
            DecArcDecSpacing = Properties.Settings.Default.DecArcDecSpacing;
            HourAngleLeadIn = Properties.Settings.Default.HourAngleLeadIn;
            DecArcQuantity = Properties.Settings.Default.DecArcQuantity;
            PointOrderingStrategy = Properties.Settings.Default.PointOrderingStrategy;
            PolarPointOrderingStrategy = Properties.Settings.Default.PolarPointOrderingStrategy;
            PolarProximityLimit = Properties.Settings.Default.PolarProximityLimit;

            Properties.Settings.Default.PropertyChanged += SettingsChanged;

            this.profileService = profileService;
        }

        public CreateDecArcModel(CreateDecArcModel copyMe) : this(copyMe.profileService) {
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
                Logger.Debug($"TotalPoints={totalPoints}");
                RaisePropertyChanged();
            }
        }

        public int CompletedPoints {
            get => completedPoints;
            set {
                completedPoints = value;
                Logger.Debug($"CompletedPoints={completedPoints}");
                RaisePropertyChanged();
            }
        }

        public string ModelStatus {
            get => modelStatus;
            set {
                modelStatus = value;
                Logger.Debug($"ModelStatus={modelStatus}");
                RaisePropertyChanged();
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken ct) {
            var appm = new AppmApi.AppmApi();
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

            var proc = RunAPPM();
            await appm.WaitForApiInit(ct);

            var config = await appm.GetConfiguration(ct);
            var newConfig = new AppmApi.AppmMeasurementConfigurationRequest() {
                Configuration = config.Configuration
            };

            newConfig.Configuration.UseMaxDeclination = true;
            newConfig.Configuration.UseMinDeclination = true;
            newConfig.Configuration.UseMaxHourAngleWest = true;
            newConfig.Configuration.UseMinHourAngleEast = true;
            newConfig.Configuration.CreateEastPoints = true;
            newConfig.Configuration.CreateWestPoints = true;
            newConfig.Configuration.UseMeridianLimits = true;
            newConfig.Configuration.UseHorizonLimits = true;
            newConfig.Configuration.UseMinAltitude = true;
            newConfig.Configuration.RightAscensionOffset = 0;
            newConfig.Configuration.DeclinationSpacing = decArcParams.DecSpacing;
            newConfig.Configuration.MaxDeclination = decArcParams.NorthDecLimit;
            newConfig.Configuration.MinDeclination = decArcParams.SouthDecLimit;
            newConfig.Configuration.DeclinationOffset = decArcParams.DecOffset;
            newConfig.Configuration.RightAscensionSpacing = decArcParams.RaSpacing;
            newConfig.Configuration.MinHourAngleEast = decArcParams.EastHaLimit;
            newConfig.Configuration.MaxHourAngleWest = decArcParams.WestHaLimit;
            newConfig.Configuration.PointOrderingStrategy = (90 - Math.Abs(decArcParams.TargetDec)) <= decArcParams.PolarProximityLimit ? decArcParams.PolarPointOrderingStrategy : PointOrderingStrategy;

            var pointCountResult = await appm.SetConfiguration(newConfig, ct);
            TotalPoints = pointCountResult.PointCount;

            if (!ManualMode) {
                await appm.Start(ct);

                var runStatus = await appm.WaitForMappingState("Running", ct);
                ModelStatus = runStatus.Status.MappingRunState;

                while (!runStatus.Status.MappingRunState.Equals("Idle")) {
                    if (ct.IsCancellationRequested) {
                        await appm.Stop(CancellationToken.None);
                        return;
                    }

                    runStatus = await appm.Status(ct);
                    CompletedPoints = runStatus.Status.MeasurementPointsCount;
                    ModelStatus = runStatus.Status.MappingRunState;

                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            } else {
                proc.WaitForExit();
            }

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
                PointOrderingStrategy = PointOrderingStrategy,
                PolarPointOrderingStrategy = PolarPointOrderingStrategy,
                PolarProximityLimit = PolarProximityLimit,
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

        private string APPMExePath { get; set; }
        private string APPMSettingsPath { get; set; }
        private int DecArcRaSpacing { get; set; }
        private int DecArcDecSpacing { get; set; }
        private double HourAngleLeadIn { get; set; }
        private int DecArcQuantity { get; set; }
        private int PointOrderingStrategy { get; set; }
        private int PolarPointOrderingStrategy { get; set; }
        private int PolarProximityLimit { get; set; }

        private void SettingsChanged(object sender, PropertyChangedEventArgs e) {
            switch (e.PropertyName) {
                case "APPMExePath":
                    APPMExePath = Properties.Settings.Default.APPMExePath;
                    break;

                case "APPMSettingsPath":
                    APPMSettingsPath = Properties.Settings.Default.APPMSettingsPath;
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

                case "HourAngleLeadIn":
                    HourAngleLeadIn = Properties.Settings.Default.HourAngleLeadIn;
                    break;

                case "PointOrderingStrategy":
                    PointOrderingStrategy = Properties.Settings.Default.PointOrderingStrategy;
                    break;

                case "PolarPointOrderingStrategy":
                    PolarPointOrderingStrategy = Properties.Settings.Default.PolarPointOrderingStrategy;
                    break;

                case "PolarProximityLimit":
                    PolarProximityLimit = Properties.Settings.Default.PolarProximityLimit;
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