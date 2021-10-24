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
using NINA.Core.Utility;
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
    [ExportMetadata("Category", "Astro-Physics Utilities")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CreateDecArcModel : SequenceItem, IValidatable, INotifyPropertyChanged {
        private bool doNotExit = false;

        [ImportingConstructor]
        public CreateDecArcModel() {
            APPMExePath = Properties.Settings.Default.APPMExePath;
            APPMSettingsPath = Properties.Settings.Default.APPMSettingsPath;
            APPMMapPath = Properties.Settings.Default.APPMMapPath;

            Properties.Settings.Default.PropertyChanged += SettingsChanged;
        }

        public CreateDecArcModel(CreateDecArcModel copyMe) : this() {
            CopyMetaData(copyMe);
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
            var taget = Utilities.FindDsoInfo(this.Parent);

            if (taget == null) {
                throw new SequenceEntityFailedException("No DSO has been defined or this instruction is not contained within one");
            }

            var tmpFile = new Utilities.TemporaryFile();
            AppmMeasurementConfPath = tmpFile.FilePath;

            GenerateMesurementFile(tmpFile);
            _ = RunAPPM();

            tmpFile.Dispose();

            return Task.CompletedTask;
        }

        public override object Clone() {
            return new CreateDecArcModel(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(CreateDecArcModel)}, DotNotExit: {DoNotExit}, Exe Path: {APPMExePath}, Settings: {APPMSettingsPath}, Map File: {APPMMapPath}";
        }

        public IList<string> Issues { get; set; } = new ObservableCollection<string>();

        public bool Validate() {
            bool passes = true;

            if (Utilities.FindDsoInfo(this.Parent) == null) {
                Issues.Add("No DSO has been defined or this instruction is not contained within one");
                passes = false;
            } 

            if (string.IsNullOrEmpty(APPMExePath) || !File.Exists(APPMExePath)) {
                Issues.Add("Invalid location for ApPointMapper.exe");
                passes = false;
            }

            if (!string.IsNullOrEmpty(APPMSettingsPath) && !File.Exists(APPMSettingsPath)) {
                Issues.Add("Invalid location for APPM settings file");
                passes = false;
            }

            if (!string.IsNullOrEmpty(APPMMapPath) && !File.Exists(APPMMapPath)) {
                Issues.Add("Invalid location for APPM map file");
                passes = false;
            }

            return passes;
        }

        private string APPMExePath { get; set; }
        private string APPMSettingsPath { get; set; }
        private string APPMMapPath { get; set; }
        private string AppmMeasurementConfPath { get; set; }

        private int RunAPPM() {
            List<string> args = new List<string> {
                "-auto"
            };

            if (!string.IsNullOrEmpty(APPMSettingsPath) && !string.IsNullOrWhiteSpace(APPMSettingsPath)) {
                args.Add($"-s{APPMSettingsPath}");
            }

            if (!string.IsNullOrEmpty(APPMMapPath) && !string.IsNullOrWhiteSpace(APPMMapPath)) {
                args.Add($"-m{APPMMapPath}");
            }

            if (!string.IsNullOrEmpty(AppmMeasurementConfPath) && !string.IsNullOrWhiteSpace(AppmMeasurementConfPath)) {
                args.Add($"-M{AppmMeasurementConfPath}");
            }

            if (DoNotExit) {
                args.Add("-dontexit");
            }

            var appm = new ProcessStartInfo(APPMExePath) {
                Arguments = string.Join(" ", args.ToArray())
            };

            Logger.Info($"Executing: {appm.FileName} {appm.Arguments}");

            var cmd = Process.Start(appm);
            cmd.WaitForExit();

            return cmd.ExitCode;
        }

        private Dictionary<string, object> measurementConfig = new Dictionary<string, object>() {
            { "CreateWestPoints", true },
            { "CreateEastPoints", true },
            { "SetSlewRate", false },
            { "SlewRate", 900 },
            { "SlewSettleTime", 2 },
            { "UseMeridianLimits", false },
            { "UseHorizonLimits", false },
            { "ZenithSafetyDistance", 0d },
            { "ZenithSyncDistance", 3d },
            { "PointOrderingStrategy", 0 },
            { "DeclinationSpacing", 10 },
            { "DeclinationOffset", 0 },
            { "UseMinDeclination", false },
            { "MinDeclination", -85d },
            { "UseMaxDeclination", false },
            { "MaxDeclination", 85d },
            { "RightAscensionSpacing", 10},
            { "RightAscensionOffset", 0 },
            { "UseMinHourAngleEast", true },
            { "MinHourAngleEast", -8d },
            { "UseMaxHourAngleWest", true },
            { "MaxHourAngleWest", 8d },
            { "UseMinAltitude", true },
            { "MinAltitude", 25 },
        };

        private void GenerateMesurementFile(Utilities.TemporaryFile tmpFile) {
            var fileStream = File.OpenWrite(tmpFile.FilePath);

            foreach (var configParam in measurementConfig) {
                var bytes = Encoding.UTF8.GetBytes($"{configParam.Key}={configParam.Value}{Environment.NewLine}");
                fileStream.Write(bytes, 0, bytes.Length);
            }

            fileStream.Flush();
            fileStream.Close();

            Logger.Debug($"{File.ReadAllText(tmpFile.FilePath)}");
        }

        void SettingsChanged(object sender, PropertyChangedEventArgs e) {
            switch (e.PropertyName) {
                case "APPMExePath":
                    APPMExePath = Properties.Settings.Default.APPMExePath;
                    break;
                case "APPMSettingsPath":
                    APPMSettingsPath = Properties.Settings.Default.APPMSettingsPath;
                    break;
                case "APPMMapPath":
                    APPMMapPath = Properties.Settings.Default.APPMMapPath;
                    break;
            }
        }
    }
}