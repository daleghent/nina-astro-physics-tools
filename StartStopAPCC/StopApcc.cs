#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

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
using System.Threading;
using System.Threading.Tasks;

namespace DaleGhent.NINA.AstroPhysicsTools {
    /*
     * Disabled for now. There's no way for us to know if we can safely kill APCC
     * Perhaps there will be a way in the future.
     */

    /*
    [ExportMetadata("Name", "Stop APCC")]
    [ExportMetadata("Description", "Stops Astro-Physics Command Center")]
    [ExportMetadata("Icon", "APCC_SVG")]
    [ExportMetadata("Category", "Astro-Physics Tools")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    */

    public class StopApcc : SequenceItem, IValidatable {
        private ITelescopeMediator telescopeMediator;

        [ImportingConstructor]
        public StopApcc(ITelescopeMediator telescopeMediator) {
            this.telescopeMediator = telescopeMediator;
        }

        private StopApcc(StopApcc copyMe) : this(copyMe.telescopeMediator) {
            CopyMetaData(copyMe);
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            await DisonnectTelescope();

            if (Process.GetProcessesByName("AstroPhysicsCommandCenter").Length > 0) {
                KillApcc();
            } else {
                Logger.Error("APCC is not running!");
            }

            return;
        }

        public override object Clone() {
            return new StopApcc(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(StopApcc)}";
        }

        public IList<string> Issues { get; set; } = new ObservableCollection<string>();

        public bool Validate() {
            return true;
        }

        private void KillApcc() {
            var apcc = Process.GetProcessesByName("AstroPhysicsCommandCenter");

            try {
                if (apcc.Length > 0) {
                    foreach (var proc in Process.GetProcessesByName("AstroPhysicsCommandCenter")) {
                        Logger.Info($"Killing APCC PID {proc.Id}");
                        proc.Kill();
                    }
                }
            } catch {
                throw new SequenceEntityFailedException("Could not stop APCC");
            }
        }

        private async Task DisonnectTelescope() {
            var type = telescopeMediator.GetType();
            var GetInfo = type.GetMethod("GetInfo");
            DeviceInfo info = (DeviceInfo)GetInfo.Invoke(telescopeMediator, null);

            if (info.Connected) {
                var Disconnect = type.GetMethod("Disconnect");
                await (Task)Disconnect.Invoke(telescopeMediator, null);

                DeviceInfo infoAfterDisconnect = (DeviceInfo)GetInfo.Invoke(telescopeMediator, null);

                if (!infoAfterDisconnect.Connected) {
                    Logger.Info($"{info.Name} has been disconnected");
                }
            } else {
                Logger.Info($"{info.Name} is already disconnected");
            }
        }
    }
}