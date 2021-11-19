using System.Reflection;
using System.Runtime.InteropServices;

// [MANDATORY] The following GUID is used as a unique identifier of the plugin
[assembly: Guid("684FC0AA-49AD-4157-AF9F-D30521101332")]

// [MANDATORY] The assembly versioning
//Should be incremented for each new release build of a plugin
[assembly: AssemblyVersion("1.4.0.0")]
[assembly: AssemblyFileVersion("1.4.0.0")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("Astro-Physics Tools")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("A collection of useful Advanced Sequencer utilities for users of Astro-Physics mounts and APCC Pro")]

// The following attributes are not required for the plugin per se, but are required by the official manifest meta data

// Your name
[assembly: AssemblyCompany("Dale Ghent")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("Astro-Physics Tools")]
[assembly: AssemblyCopyright("Copyright © 2021 Dale Ghent")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "2.0.0.2004")]

// The license your plugin code is using
[assembly: AssemblyMetadata("License", "MPL-2.0")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
// The repository where your pluggin is hosted
[assembly: AssemblyMetadata("Repository", "https://github.com/daleghent/nina-astro-physics-tools")]

// The following attributes are optional for the official manifest meta data

//[Optional] Your plugin homepage - omit if not applicaple
[assembly: AssemblyMetadata("Homepage", "https://daleghent.com/utilities-for-astro-physics-mounts")]

//[Optional] Common tags that quickly describe your plugin
[assembly: AssemblyMetadata("Tags", "appm,astro-physics,apcc,sequencer")]

//[Optional] A link that will show a log of all changes in between your plugin's versions
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/daleghent/nina-astro-physics-tools/blob/main/CHANGELOG.md")]

//[Optional] The url to a featured logo that will be displayed in the plugin list next to the name
[assembly: AssemblyMetadata("FeaturedImageURL", "https://daleghent.github.io/nina-plugins/assets/images/ap-logo.jpg")]
//[Optional] A url to an example screenshot of your plugin in action
[assembly: AssemblyMetadata("ScreenshotURL", "https://daleghent.github.io/nina-plugins/assets/images/U4APMscreen1.png")]
//[Optional] An additional url to an example example screenshot of your plugin in action
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
//[Optional] An in-depth description of your plugin
[assembly: AssemblyMetadata("LongDescription", @"Astro-Physics Tools is a collection of Instructions and other things for running from within NINA's Advanced Sequencer.

Please refer to this [plugin's website](https://daleghent.com/utilities-for-astro-physics-mounts) for detailed documentation. I also maintain a [website with tips](https://daleghent.com/nina-and-astro-physics-mounts) on using NINA with Astro-Physics mounts.

# Requirements #

* NINA 1.11
* APCC Pro 1.9 or APCC Standard 1.8 (APCC Pro is required for the use of APPM)
* The **Enable Server** setting is **On** in NINA's options (refer to Options > General)

# Provided functions #

* Start APCC
    * Starts APCC and connects NINA to the Astro-Physics ASCOM driver. Assumes that APCC's **Auto-Connect** setting is selected in both the **Mount** and **AP V2 Driver** option areas in the Setup tab
* Create APPM Model
    * A sequence instruction that will run Astro-Physics Point Mapper (APPM) in an automated mode. When ran, APPM will use its existing default settings to run a point mapping session and will load the results into APCC Pro when complete. If the default settings and point map are not desired, an APPM settings or a point map file may be optionally specified below in this plugin's settings.

# Getting help #

Help for this plugin may be found in the **#plugin-discussions** channel on the NINA project [Discord chat server](https://discord.com/invite/rWRbVbw), Astro-Physics' [AP-GTO forum](https://ap-gto.groups.io/g/main), or by filing an issue report at this plugin's [Github repository](https://github.com/daleghent/nina-plugins/issues).

* Astro-Physics Tools is provided 'as is' under the terms of the [Mozilla Public License 2.0](https://github.com/daleghent/nina-astro-physics-tools/blob/main/LICENSE.txt)
* Source code for this plugin is available at this plugin's [source code repository](https://github.com/daleghent/nina-astro-physics-tools)

The Astro-Physics logo is copyright © Astro-Physics, Inc. Used with permission.")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]
// [Unused]
[assembly: AssemblyConfiguration("")]
// [Unused]
[assembly: AssemblyTrademark("")]
// [Unused]
[assembly: AssemblyCulture("")]