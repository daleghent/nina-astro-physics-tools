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

namespace DaleGhent.NINA.AstroPhysics.CreateAPPMModel {

    [ExportMetadata("Name", "Create APPM Model")]
    [ExportMetadata("Description", "Runs Astro-Physics Point Mapper (APPM) in automatic mode for unattended model creation")]
    [ExportMetadata("Icon", "APPM_SVG")]
    [ExportMetadata("Category", "Astro-Physics Utilities")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CreateAPPMModel : SequenceItem, IValidatable, INotifyPropertyChanged {
        private bool doNotExit = false;

        [ImportingConstructor]
        public CreateAPPMModel() {
            APPMExePath = Properties.Settings.Default.APPMExePath;
            APPMSettingsPath = Properties.Settings.Default.APPMSettingsPath;
            APPMMapPath = Properties.Settings.Default.APPMMapPath;

            Properties.Settings.Default.PropertyChanged += SettingsChanged;
        }

        public CreateAPPMModel(CreateAPPMModel copyMe) : this() {
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
            _ = RunAPPM();

            return Task.CompletedTask;
        }

        public override object Clone() {
            return new CreateAPPMModel(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(CreateAPPMModel)}, DotNotExit: {DoNotExit}, Exe Path: {APPMExePath}, Settings: {APPMSettingsPath}, Map File: {APPMMapPath}";
        }

        public IList<string> Issues { get; set; } = new ObservableCollection<string>();

        public bool Validate() {
            bool passes = true;

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

        private int RunAPPM() {
            List<string> args = new List<string>();
            args.Add("-auto");

            if (!string.IsNullOrEmpty(APPMSettingsPath) && !string.IsNullOrWhiteSpace(APPMSettingsPath)) {
                args.Add($"-s {APPMSettingsPath}");
            }

            if (!string.IsNullOrEmpty(APPMMapPath) && !string.IsNullOrWhiteSpace(APPMMapPath)) {
                args.Add($"-m {APPMMapPath}");
            }

            if (DoNotExit) {
                args.Add("-dontexit");
            }

            var appm = new ProcessStartInfo(APPMExePath) {
                Arguments = string.Join(" ", args.ToArray())
            };

            var cmd = Process.Start(appm);
            cmd.WaitForExit();

            return cmd.ExitCode;
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