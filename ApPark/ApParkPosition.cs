#region "copyright"

/*
    Copyright (c) 2024 Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using DaleGhent.NINA.AstroPhysicsTools.Utility;
using System.ComponentModel;

namespace DaleGhent.NINA.AstroPhysicsTools.ApPark {

    [TypeConverter(typeof(EnumDescriptionTypeConverter))]
    public enum ApParkPosition {

        [Description("Park 1")]
        Park1,

        [Description("Park 2")]
        Park2,

        [Description("Park 3")]
        Park3,

        [Description("Park 4")]
        Park4,

        [Description("Park 5")]
        Park5,
    }
}