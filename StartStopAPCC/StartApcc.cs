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
using NINA.Equipment.Equipment;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DaleGhent.NINA.AstroPhysics {

    [ExportMetadata("Name", "Start APCC")]
    [ExportMetadata("Description", "Starts APCC and connects NINA to the ASCOM driver")]
    [ExportMetadata("Icon", "APCC_SVG")]
    [ExportMetadata("Category", "Astro-Physics Tools")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class StartApcc : SequenceItem, IValidatable {
        private ITelescopeMediator telescopeMediator;

        [ImportingConstructor]
        public StartApcc(ITelescopeMediator telescopeMediator) {
            this.telescopeMediator = telescopeMediator;

            ApccExePath = Properties.Settings.Default.ApccExePath;
            ApccStartupTimeout = Properties.Settings.Default.ApccStartupTimeout;
            ApccDriverConnectTimeout = Properties.Settings.Default.ApccDriverConnectTimeout;
        }

        private StartApcc(StartApcc copyMe) : this(copyMe.telescopeMediator) {
            CopyMetaData(copyMe);
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            bool success;

            if (Process.GetProcessesByName("AstroPhysicsCommandCenter").Length == 0) {
                RunApcc();
            } else {
                Logger.Info($"APCC is already running!");
            }

            for (int i = 0; i < ApccStartupTimeout; i++) {
                if (Process.GetProcessesByName("AstroPhysicsV2 Driver").Length > 0) {
                    await Task.Delay(TimeSpan.FromSeconds(ApccDriverConnectTimeout));
                    break;
                } else {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }

            success = await ConnectTelescope();

            if (!success) {
                throw new SequenceEntityFailedException("Failed to connect to the Astro-Physics ASCOM driver");
            }

            return;
        }

        public override object Clone() {
            return new StartApcc(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(StartApcc)}";
        }

        public IList<string> Issues { get; set; } = new ObservableCollection<string>();

        public bool Validate() {
            bool passes = true;

            if (string.IsNullOrEmpty(ApccExePath) || !File.Exists(ApccExePath)) {
                Issues.Add("Invalid location for AstroPhysicsCommandCenter.exe");
                passes = false;
            }

            return passes;
        }

        private string ApccExePath { get; set; }
        private uint ApccStartupTimeout { get; set; }
        private uint ApccDriverConnectTimeout { get; set; }

        private void RunApcc() {
            var appm = new ProcessStartInfo(ApccExePath);

            try {
                var cmd = Process.Start(appm);
                Logger.Info($"{ApccExePath} started with PID {cmd.Id}");
            } catch (Exception ex) {
                throw new SequenceEntityFailedException(ex.Message);
            }

            return;
        }

        private async Task<bool> ConnectTelescope() {
            bool success = false;

            var type = telescopeMediator.GetType();
            var GetInfo = type.GetMethod("GetInfo");
            DeviceInfo info = (DeviceInfo)GetInfo.Invoke(telescopeMediator, null);

            if (!info.Connected) {
                var Rescan = type.GetMethod("Rescan");
                await (Task)Rescan.Invoke(telescopeMediator, null);

                var Connect = type.GetMethod("Connect");
                success = await (Task<bool>)Connect.Invoke(telescopeMediator, null);

                DeviceInfo infoAfterConnect = (DeviceInfo)GetInfo.Invoke(telescopeMediator, null);
                success = success && infoAfterConnect.Connected;

                if (!info.Name.Equals("")) { }

                if (success) {
                    Logger.Info($"{info.Name} has been connected");
                }
            } else {
                success = true;
                Logger.Info($"{info.Name} is already connected");
            }

            return success;
        }
    }
}