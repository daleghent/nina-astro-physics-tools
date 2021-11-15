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
using System.Text;
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
        private bool manualStart = false;
        private bool doNotExit = false;
        private bool doFullArc = false;
        private IProfileService profileService;

        [ImportingConstructor]
        public CreateDecArcModel(IProfileService profileService) {
            APPMExePath = Properties.Settings.Default.APPMExePath;
            APPMSettingsPath = Properties.Settings.Default.APPMSettingsPath;
            DecArcRaSpacing = Properties.Settings.Default.DecArcRaSpacing;
            DecArcDecSpacing = Properties.Settings.Default.DecArcDecSpacing;
            HourAngleLeadIn = Properties.Settings.Default.HourAngleLeadIn;
            DecArcQuantity = Properties.Settings.Default.DecArcQuantity;

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
        public bool ManualStart {
            get => manualStart;
            set {
                manualStart = value;
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

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var measurementSettings = measurementConfig;

            var target = Utilities.FindDsoInfo(this.Parent);
            target.Coordinates = target.Coordinates.Transform(Epoch.JNOW);

            if (target == null) {
                throw new SequenceEntityFailedException("No DSO has been defined");
            }

            if (target.Coordinates.Dec > 85d || target.Coordinates.Dec < -85d) {
                Logger.Info($"The target's declination of {target.Coordinates.DecString} is too close to the pole to create a meaningful model. Skipping model creation.");
                return Task.CompletedTask;
            }

            var decArcParams = CalculateDecArcParameters(target);

            Logger.Info($"RA: HourAngleStart={decArcParams.EastHaLimit:0.00}, HourAngleEnd={decArcParams.WestHaLimit:0.00}, Hours={(decArcParams.WestHaLimit - Math.Abs(decArcParams.EastHaLimit)):0.00}");
            Logger.Info($"Dec: T={decArcParams.TargetDec:0.00}, N={decArcParams.NorthDecLimit:0.00}, S={decArcParams.SouthDecLimit:0.00}, Spread={decArcParams.NorthDecLimit - decArcParams.SouthDecLimit}, Spacing={DecArcDecSpacing}");

            measurementSettings["DeclinationSpacing"] = decArcParams.DecSpacing;
            measurementSettings["MaxDeclination"] = decArcParams.NorthDecLimit;
            measurementSettings["MinDeclination"] = decArcParams.SouthDecLimit;
            measurementSettings["RightAscensionSpacing"] = decArcParams.RaSpacing;
            measurementSettings["MinHourAngleEast"] = decArcParams.EastHaLimit;

            /*
             * Write out APPM measurement config and run APPM
             */

            var tmpFile = new Utilities.TemporaryFile();
            AppmMeasurementConfPath = tmpFile.FilePath;

            GenerateMesurementFile(measurementSettings, tmpFile, target);
            _ = RunAPPM();

            tmpFile.Dispose();

            return Task.CompletedTask;
        }

        public override object Clone() {
            return new CreateDecArcModel(this) {
                DoFullArc = DoFullArc,
                ManualStart = ManualStart,
                DoNotExit = DoNotExit,
            };
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(CreateDecArcModel)}, DoFullArc: {DoFullArc}, ManualStart: {ManualStart}, DotNotExit: {DoNotExit}, Exe Path: {APPMExePath}, Settings: {APPMSettingsPath}]";
        }

        public IList<string> Issues { get; set; } = new ObservableCollection<string>();

        public bool Validate() {
            var i = new List<string>();

            if (Utilities.FindDsoInfo(this.Parent) == null) {
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

        private string APPMExePath { get; set; }
        private string APPMSettingsPath { get; set; }
        private string AppmMeasurementConfPath { get; set; }
        private int DecArcRaSpacing { get; set; }
        private int DecArcDecSpacing { get; set; }
        private double HourAngleLeadIn { get; set; }
        private int DecArcQuantity { get; set; }

        private int RunAPPM() {
            List<string> args = new List<string>();

            if (!ManualStart) {
                args.Add("-auto");
            }

            if (DoNotExit) {
                args.Add("-dontexit");
            }

            if (File.Exists(APPMSettingsPath)) {
                args.Add($"-s{APPMSettingsPath}");
            }

            if (File.Exists(AppmMeasurementConfPath)) {
                args.Add($"-M{AppmMeasurementConfPath}");
            }

            var appm = new ProcessStartInfo(APPMExePath) {
                Arguments = string.Join(" ", args.ToArray())
            };

            Logger.Info($"Executing: {appm.FileName} {appm.Arguments}");

            var cmd = Process.Start(appm);
            cmd.WaitForExit();

            Logger.Debug($"APPM exited with exit code {cmd.ExitCode}");
            return cmd.ExitCode;
        }

        private Dictionary<string, object> measurementConfig = new Dictionary<string, object>() {
            { "CreateWestPoints", true },
            { "CreateEastPoints", true },
            { "PointOrderingStrategy", 0 },
            { "DeclinationSpacing", 0 },
            { "DeclinationOffset", 0 },
            { "UseMinAltitude", true },
            { "RightAscensionSpacing", 0},
            { "RightAscensionOffset", 0 },
            { "UseMinDeclination", true },
            { "UseMaxDeclination", true },
            { "MinDeclination", 0 },
            { "MaxDeclination", 0 },
            { "UseMinHourAngleEast", true },
            { "UseMaxHourAngleWest", true },
            { "MinHourAngleEast", -12d },
            { "MaxHourAngleWest", 12d },
        };

        private void GenerateMesurementFile(Dictionary<string, object> measurementSettings, Utilities.TemporaryFile tmpFile, DeepSkyObject target) {
            var fileStream = File.OpenWrite(tmpFile.FilePath);

            var header = $"# Dec Arc configuration generated at {DateTime.UtcNow:R}{Environment.NewLine}";
            header += $"# Target: {target.Name}{Environment.NewLine}";
            header += $"# Epoch: {target.Coordinates.Epoch}{Environment.NewLine}";
            header += $"# RA: {target.Coordinates.RAString} ({target.Coordinates.RADegrees:0.00}°){Environment.NewLine}";
            header += $"# Dec: {target.Coordinates.DecString} ({target.Coordinates.Dec:0.00}°){Environment.NewLine}";
            header += $"# Arcs: {DecArcQuantity}{Environment.NewLine}";
            header += $"# Dec arc spacing: {DecArcDecSpacing}°{Environment.NewLine}";
            header += $"# RA point spacing: {DecArcRaSpacing}°{Environment.NewLine}";
            header += $"# HA Lead-in: {HourAngleLeadIn}{Environment.NewLine}";

            var bytes = Encoding.UTF8.GetBytes(header);
            fileStream.Write(bytes, 0, bytes.Length);

            foreach (var configParam in measurementSettings) {
                bytes = Encoding.UTF8.GetBytes($"{configParam.Key}={configParam.Value}{Environment.NewLine}");
                fileStream.Write(bytes, 0, bytes.Length);
            }

            fileStream.Flush();
            fileStream.Close();

            Logger.Debug($"{Environment.NewLine + File.ReadAllText(tmpFile.FilePath)}");
        }

        private double CurrentHourAngle(DeepSkyObject target) {
            // We want HA in terms of -12..12, not 0..24
            return ((AstroUtil.GetHourAngle(AstroUtil.GetLocalSiderealTimeNow(profileService.ActiveProfile.AstrometrySettings.Longitude), target.Coordinates.RA) + 36) % 24) - 12;
        }

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
            }
        }

        private DecArcParameters CalculateDecArcParameters(DeepSkyObject target) {
            var decArcParams = new DecArcParameters() {
                ArcQuantity = DecArcQuantity,
                DecSpacing = DecArcDecSpacing,
                RaSpacing = DecArcRaSpacing,
            };

            decArcParams.TargetHa = CurrentHourAngle(target);

            decArcParams.EastHaLimit = DoFullArc ? -12d : Math.Round(Math.Max(decArcParams.TargetHa - decArcParams.HaLeadIn, -12), 2);

            if (decArcParams.ArcQuantity == 1) {
                decArcParams.DecSpacing = 1;
                decArcParams.TargetDec = (int)Math.Round(target.Coordinates.Dec);
                decArcParams.NorthDecLimit = decArcParams.SouthDecLimit = decArcParams.TargetDec;
            } else if (decArcParams.ArcQuantity == 2) {
                if (profileService.ActiveProfile.AstrometrySettings.Latitude >= 0) {
                    decArcParams.TargetDec = (int)Math.Ceiling(target.Coordinates.Dec);
                    decArcParams.NorthDecLimit = decArcParams.TargetDec;
                    decArcParams.SouthDecLimit = decArcParams.NorthDecLimit - decArcParams.DecSpacing;
                } else {
                    decArcParams.TargetDec = (int)Math.Floor(target.Coordinates.Dec);
                    decArcParams.SouthDecLimit = decArcParams.TargetDec;
                    decArcParams.NorthDecLimit = decArcParams.SouthDecLimit + decArcParams.DecSpacing;
                }
            } else {
                decArcParams.TargetDec = (int)Math.Round(target.Coordinates.Dec);
                var totalSpread = decArcParams.ArcQuantity * decArcParams.DecSpacing;

                decArcParams.NorthDecLimit = (int)Math.Round(decArcParams.TargetDec + (totalSpread / 2d));
                decArcParams.SouthDecLimit = decArcParams.NorthDecLimit - totalSpread + 1;
            }

            // Some sanity checks
            // We do need to tune the arcs a bit for polar declinations so that APPM can plot them
            if (decArcParams.NorthDecLimit > 85) {
                decArcParams.DecSpacing = 1;
                decArcParams.NorthDecLimit = 85;

                if (decArcParams.SouthDecLimit > 82) {
                    decArcParams.SouthDecLimit = 82;
                }

                if (decArcParams.RaSpacing < 16) {
                    decArcParams.RaSpacing = 16;
                }
            }

            if (decArcParams.SouthDecLimit < -85) {
                decArcParams.DecSpacing = 1;
                decArcParams.SouthDecLimit = -85;

                if (decArcParams.NorthDecLimit < -82) {
                    decArcParams.NorthDecLimit = -82;
                }

                if (decArcParams.RaSpacing < 16) {
                    decArcParams.RaSpacing = 16;
                }
            }

            return decArcParams;
        }

        public class DecArcParameters {
            public int TargetDec { get; set; } = 0;
            public int NorthDecLimit { get; set; } = 0;
            public int SouthDecLimit { get; set; } = 0;
            public int ArcQuantity { get; set; } = 0;
            public int DecSpacing { get; set; } = 0;
            public int RaSpacing { get; set; } = 0;
            public double TargetHa { get; set; } = 0;
            public double EastHaLimit { get; set; } = -12;
            public double WestHaLimit { get; set; } = 12;
            public double HaLeadIn { get; set; } = 0;
            public PointOrderingStrategy PointOrderingStrategy { get; set; } = PointOrderingStrategy.Auto;
        }

        public class AppmMeasurementConfig {
            public bool CreateWestPoints { get; set; } = true;
            public bool CreateEastPoints { get; set; } = true;
            public int DeclinationSpacing { get; set; } = 0;
            public int DeclinationOffset { get; set; } = 0;
            public bool UseMinAltitude { get; set; } = true;
            public int RightAscensionSpacing { get; set; } = 0;
            public int RightAscensionOffset { get; set; } = 0;
            public bool UseMinDeclination { get; set; } = true;
            public bool UseMaxDeclination { get; set; } = true;
            public int MinDeclination { get; set; } = 0;
            public int MaxDeclination { get; set; } = 0;
            public bool UseMinHourAngleEast { get; set; } = true;
            public bool UseMaxHourAngleWest { get; set; } = true;
            public double MinHourAngleEast { get; set; } = -12d;
            public double MaxHourAngleWest { get; set; } = 12d;
        }

        public enum PointOrderingStrategy {
            Auto,
            Declination,
            Declination_Equal_RA,
            Declination_Graduated_RA,
            Hour_Angle,
        }
    }
}