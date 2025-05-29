#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using System.ComponentModel;

namespace DaleGhent.NINA.AstroPhysicsTools.Interfaces {

    public interface IAstroPhysicsToolsOptions : INotifyPropertyChanged {
        /*
         * General settings
         */

        string APPMExePath { get; set; }
        string APPMSettingsPath { get; set; }
        string ApccExePath { get; set; }
        uint ApccStartupTimeout { get; set; }
        uint ApccDriverConnectTimeout { get; set; }

        bool AppmSetSlewRate { get; set; }
        int AppmSlewRate { get; set; }
        int AppmSlewSettleTime { get; set; }
        double AppmZenithSafetyDistance { get; set; }
        double AppmZenithSyncDistance { get; set; }
        bool AppmUseMinAltitude { get; set; }
        int AppmMinAltitude { get; set; }
        bool AppmUseMeridianLimits { get; set; }
        bool AppmUseHorizonLimits { get; set; }

        /*
         * Create Dec Arc Model properties
         */

        int DecArcRaSpacing { get; set; }
        int DecArcDecSpacing { get; set; }
        double DecArcHourAngleLeadIn { get; set; }
        double DecArcHourAngleTail { get; set; }
        int DecArcQuantity { get; set; }
        int DecArcPointOrderingStrategy { get; set; }
        int DecArcPolarPointOrderingStrategy { get; set; }
        int DecArcPolarProximityLimit { get; set; }

        /*
         * Create All Sky Model properties
         */

        bool AllSkyCreateWestPoints { get; set; }
        bool AllSkyCreateEastPoints { get; set; }
        int AllSkyPointOrderingStrategy { get; set; }
        int AllSkyDeclinationSpacing { get; set; }
        int AllSkyDeclinationOffset { get; set; }
        bool AllSkyUseMinDeclination { get; set; }
        bool AllSkyUseMaxDeclination { get; set; }
        int AllSkyMinDeclination { get; set; }
        int AllSkyMaxDeclination { get; set; }
        int AllSkyRightAscensionSpacing { get; set; }
        int AllSkyRightAscensionOffset { get; set; }
        bool AllSkyUseMinHourAngleEast { get; set; }
        bool AllSkyUseMaxHourAngleWest { get; set; }
        double AllSkyMinHourAngleEast { get; set; }
        double AllSkyMaxHourAngleWest { get; set; }
    }
}