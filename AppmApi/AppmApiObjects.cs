#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using Newtonsoft.Json;
using System.Collections.Generic;

namespace DaleGhent.NINA.AstroPhysics.AppmApi {

    public class AppmPointCountResult {

        [JsonProperty]
        public bool Success { get; set; }

        [JsonProperty]
        public string Result { get; set; }

        [JsonProperty]
        public int PointCount { get; set; }
    }

    public class AppmMappingPointsResult {

        [JsonProperty]
        public bool Success { get; set; }

        [JsonProperty]
        public string Result { get; set; }

        [JsonProperty]
        public int PointCount { get; set; }

        [JsonProperty]
        public List<AppmMappingPoint> MappingPoints { get; set; }
    }

    public class AppmMappingPoint {

        [JsonProperty]
        public bool PierSideEast { get; set; }

        [JsonProperty]
        public bool CounterweightUp { get; set; }

        [JsonProperty]
        public double HourAngle { get; set; }

        [JsonProperty]
        public double Declination { get; set; }
    }

    public class AppmMeasurementPoint {

        [JsonProperty]
        public int Num { get; set; }

        [JsonProperty]
        public string Time { get; set; }

        [JsonProperty]
        public string Side { get; set; }

        [JsonProperty]
        public string Cw { get; set; }

        [JsonProperty]
        public string HourAngle { get; set; }

        [JsonProperty]
        public string Ra { get; set; }

        [JsonProperty]
        public string Dec { get; set; }

        [JsonProperty]
        public string Status { get; set; }

        [JsonProperty]
        public string RaDelta { get; set; }

        [JsonProperty]
        public string DecDelta { get; set; }

        [JsonProperty]
        public string RaSolved { get; set; }

        [JsonProperty]
        public string DecSolved { get; set; }

        [JsonProperty]
        public double Temperature { get; set; }

        [JsonProperty]
        public string SolveError { get; set; }

        [JsonProperty]
        public string FitsFile { get; set; }
    }

    public class AppmMeasurementConfigurationResult {

        [JsonProperty]
        public bool Success { get; set; }

        [JsonProperty]
        public string Result { get; set; }

        [JsonProperty]
        public int PointCount { get; set; }

        [JsonProperty]
        public AppmMeasurementConfiguration Configuration { get; set; }
    }

    public class AppmMeasurementConfigurationRequest {

        [JsonProperty]
        public AppmMeasurementConfiguration Configuration { get; set; }
    }

    public class AppmMeasurementConfiguration {

        [JsonProperty]
        public bool CreateWestPoints { get; set; } = true;

        [JsonProperty]
        public bool CreateEastPoints { get; set; } = true;

        [JsonProperty]
        public bool SetSlewRate { get; set; } = true;

        [JsonProperty]
        public int SlewRate { get; set; } = 600;

        [JsonProperty]
        public int SlewSettleTime { get; set; } = 2;

        [JsonProperty]
        public bool UseMeridianLimits { get; set; } = true;

        [JsonProperty]
        public bool UseHorizonLimits { get; set; } = true;

        [JsonProperty]
        public double ZenithSafetyDistance { get; set; } = 0;

        [JsonProperty]
        public double ZenithSyncDistance { get; set; } = 3;

        [JsonProperty]
        public int PointOrderingStrategy { get; set; } = 0;

        [JsonProperty]
        public int DeclinationSpacing { get; set; } = 5;

        [JsonProperty]
        public int DeclinationOffset { get; set; } = 0;

        [JsonProperty]
        public bool UseMinDeclination { get; set; } = true;

        [JsonProperty]
        public bool UseMaxDeclination { get; set; } = true;

        [JsonProperty]
        public int MinDeclination { get; set; } = -85;

        [JsonProperty]
        public int MaxDeclination { get; set; } = 85;

        [JsonProperty]
        public int RightAscensionSpacing { get; set; } = 5;

        [JsonProperty]
        public int RightAscensionOffset { get; set; } = 0;

        [JsonProperty]
        public bool UseMinHourAngleEast { get; set; } = true;

        [JsonProperty]
        public bool UseMaxHourAngleWest { get; set; } = true;

        [JsonProperty]
        public double MinHourAngleEast { get; set; } = -12d;

        [JsonProperty]
        public double MaxHourAngleWest { get; set; } = 12d;

        [JsonProperty]
        public bool UseMinAltitude { get; set; } = true;

        [JsonProperty]
        public int MinAltitude { get; set; } = 25;
    }

    public class AppmMappingRunStatusResult {

        [JsonProperty]
        public bool Success { get; set; }

        [JsonProperty]
        public string Result { get; set; }

        [JsonProperty]
        public AppmMappingRunStatus Status { get; set; }
    }

    public class AppmMappingRunStatus {

        [JsonProperty]
        public string MappingRunState { get; set; }

        [JsonProperty]
        public string Measurement { get; set; }

        [JsonProperty]
        public int GoodSolves { get; set; }

        [JsonProperty]
        public int BadSolves { get; set; }

        [JsonProperty]
        public string CurrentState { get; set; }

        [JsonProperty]
        public string ActionAfterRunCompleted { get; set; }

        [JsonProperty]
        public int SlewRate { get; set; }

        [JsonProperty]
        public double TemperatureC { get; set; }

        [JsonProperty]
        public double PressureMb { get; set; }

        [JsonProperty]
        public double HumidityPercent { get; set; }

        [JsonProperty]
        public bool ScopeConnected { get; set; }

        [JsonProperty]
        public bool CameraConnected { get; set; }

        [JsonProperty]
        public string AppmCameraType { get; set; }

        [JsonProperty]
        public string AscomCameraDriver { get; set; }

        [JsonProperty]
        public bool DomeConnected { get; set; }

        [JsonProperty]
        public string AscomDomeDriver { get; set; }

        [JsonProperty]
        public bool RecalNearZenithAtStart { get; set; }

        [JsonProperty]
        public bool PrecessJ2000toJNow { get; set; }

        [JsonProperty]
        public bool VerifyPointingModel { get; set; }

        [JsonProperty]
        public bool SkipPlateSolves { get; set; }

        [JsonProperty]
        public bool PauseAfterEachSlew { get; set; }

        [JsonProperty]
        public bool RequireHighAccuracySlews { get; set; }

        [JsonProperty]
        public int MeasurementPointsCount { get; set; }

        [JsonProperty]
        public List<AppmMeasurementPoint> MeasureMentPoints { get; set; }
    }
}