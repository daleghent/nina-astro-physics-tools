#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.Core.Utility.WindowService;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;

namespace DaleGhent.NINA.AstroPhysicsTools.ApMeridianLimitTrigger {

    /// <summary>
    /// The NINA services a meridian flip needs.
    ///
    /// The trigger itself only uses the telescope mediator; everything else exists solely to be handed
    /// to <see cref="ApMeridianFlipEngine"/>. Carrying them as one value keeps that pass-through from
    /// being restated in every constructor and lets the engine be built from the trigger without
    /// re-listing them a fourth time.
    /// </summary>
    internal record ApMeridianFlipDependencies(
        IProfileService ProfileService,
        ICameraMediator CameraMediator,
        ITelescopeMediator TelescopeMediator,
        IFocuserMediator FocuserMediator,
        ISafetyMonitorMediator SafetyMonitorMediator,
        IFilterWheelMediator FilterWheelMediator,
        IGuiderMediator GuiderMediator,
        IImagingMediator ImagingMediator,
        IDomeMediator DomeMediator,
        IDomeFollower DomeFollower,
        IImageHistoryVM History,
        IAutoFocusVMFactory AutoFocusVMFactory,
        IPlateSolverFactory PlateSolverFactory,
        IWindowServiceFactory WindowServiceFactory,
        ISequenceMediator SequenceMediator);
}
