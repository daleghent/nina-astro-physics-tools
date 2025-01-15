#region "copyright"

/*
    Copyright (c) 2024 Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace DaleGhent.NINA.AstroPhysicsTools.ApPark {

    [ExportMetadata("Name", "Astro-Physics Park")]
    [ExportMetadata("Description", "Parks an Astro-Physics mount to any one of the specified Astro-Phyics Park 1-5 positions.")]
    [ExportMetadata("Icon", "ParkSVG")]
    [ExportMetadata("Category", "Astro-Physics Tools")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    [method: ImportingConstructor]
    public class ApPark(IProfileService profileService, ITelescopeMediator telescopeMediator) : SequenceItem, IValidatable {
        private readonly IProfileService profileService = profileService;
        private readonly ITelescopeMediator telescopeMediator = telescopeMediator;

        private ApPark(ApPark copyMe) : this(copyMe.profileService, copyMe.telescopeMediator) {
            CopyMetaData(copyMe);
        }

        private ApParkPosition parkPosition = ApParkPosition.Park1;

        [JsonProperty]
        public ApParkPosition ParkPosition {
            get => parkPosition;
            set {
                parkPosition = value;
                RaisePropertyChanged();
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            try {
                progress.Report(new ApplicationStatus() { Status = $"Parking at position {Utility.Utility.GetEnumDescription(parkPosition)}" });

                if (ParkPosition != ApParkPosition.Park0) {
                    // Stop all axis motion
                    telescopeMediator.SendCommandString(":Q");

                    // Clear ?
                    telescopeMediator.SendCommandString(":RD0.00000");

                    // Turn off tracking
                    telescopeMediator.SendCommandString(":RT9");

                    // Clear ?
                    telescopeMediator.SendCommandString(":SM0:00");

                    var parkCoordinates = ParkCommand(ParkPosition);
                    Logger.Debug($"Slewing to {ParkPosition}: HA: {parkCoordinates.Ha}, Dec: {parkCoordinates.Dec}");

                    // Set the declination
                    telescopeMediator.SendCommandString(parkCoordinates.Dec);

                    // Set the hour angle
                    telescopeMediator.SendCommandString(parkCoordinates.Ha);

                    // Execute slew to park coordinates
                    telescopeMediator.SendCommandString(":MS");

                    // Loop while the mount is slewing.
                    do { await Task.Delay(TimeSpan.FromSeconds(3.5), token); } while (telescopeMediator.GetInfo().Slewing);
                }

                // Turn off tracking
                telescopeMediator.SendCommandString(":RT9");

                // Stop all axis motion
                telescopeMediator.SendCommandString(":Q");

                // Stop all axis motion
                telescopeMediator.SendCommandString(":Q");

                // Turn off the motors
                telescopeMediator.SendCommandString(":KA");

                progress.Report(new ApplicationStatus() { Status = Loc.Instance["LblSettle"] });
                await Task.Delay(TimeSpan.FromSeconds(profileService.ActiveProfile.TelescopeSettings.SettleTime), token);
            } catch (Exception ex) {
                Logger.Error($"Failed to park mount: {ex}");
                throw new SequenceEntityFailedException("Park operation failed");
            } finally {
                progress.Report(new ApplicationStatus() { Status = string.Empty });
            }

            return;
        }

        public IList<string> Issues { get; set; } = new ObservableCollection<string>();

        public bool Validate() {
            var i = new List<string>();

            if (!telescopeMediator.GetInfo().Connected) {
                i.Add("Mount is not connected.");
                goto end;
            }

            if (!telescopeMediator.GetInfo().Name.Contains("AstroPhysics")) {
                i.Add($"{Name} is compatible only with Astro-Physics mounts.");
            }

        end:
            if (i != Issues) {
                Issues = i;
                RaisePropertyChanged(nameof(Issues));
            }

            return i.Count == 0;
        }

        public override object Clone() {
            return new ApPark(this) {
                ParkPosition = ParkPosition,
            };
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {Name}, Park Position: {ParkPosition}";
        }

        private AxisCommand ParkCommand(ApParkPosition parkPosition) {
            double dec;
            var latitude = profileService.ActiveProfile.AstrometrySettings.Latitude;

            switch (parkPosition) {
                case ApParkPosition.Park1:
                    dec = latitude >= 0 ? (90 - latitude) : (-90 - latitude);
                    return new AxisCommand() {
                        Dec = FormatDec(dec),
                        Ha = ":Sh11:59:59.80",
                    };

                case ApParkPosition.Park2:
                    return new AxisCommand() {
                        Dec = ":Sd+00*00:00.0",
                        Ha = ":Sh-06:00:00.00",
                    };

                case ApParkPosition.Park3:
                    var decSign = latitude >= 0 ? "+" : "-";
                    return new AxisCommand() {
                        Dec = $":Sd{decSign}89*59:59.0",
                        Ha = ":Sh-06:00:00.00",
                    };

                case ApParkPosition.Park4:
                    dec = latitude >= 0 ? (-90 + latitude) : (90 + latitude);
                    return new AxisCommand() {
                        Dec = FormatDec(dec),
                        Ha = ":Sh+00:00:00.20",
                    };

                case ApParkPosition.Park5:
                    dec = latitude >= 0 ? (90 - latitude) : (-90 - latitude);
                    return new AxisCommand() {
                        Dec = FormatDec(dec),
                        Ha = ":Sh-11:59:59.80",
                    };

                default:
                    throw new InvalidOperationException($"Unknown park position: {parkPosition}");
            }
        }

        private static string FormatDec(double dec) {
            // yeaaaaaah don't ask. How many formats can we represent degrees in? Not enough, apparently.
            string[] parts = AstroUtil.DegreesToFitsDMS(dec).Split();
            return $":Sd{parts[0]}*{parts[1]}:{parts[2]}";
        }

        private class AxisCommand {
            public string Dec { get; set; }
            public string Ha { get; set; }
        }
    }
}