﻿# Astro-Physics Tools

## 2.1.0.0 - 2024-06-02
* **New, Functional change:** The **Create Dec-Arc Model** instruction will now set the westward extent of the declination arc to where the target will be at sunrise. The purpose of this is to avoid modeling the sky all the way to the horizon, some portion of which may be pointless because that is where the target will be after sunrise. A new global and runtime option for adding a "tail" to the arc model in one-tenth of an hour increments has been added to optionally extend the model beyond where the target will be at sunrise. This is akin to the hour angle lead-in option that already exists for the east end of the declination arc. Activating the **Do full arc** runtime option will still cause the arc to be completely modeled from east to west horizon.

## 2.0.1.0 - 2024-05-12
* Start APCC: Updated process management logic to handle APCC 1.9.7.0 and later.

## 2.0.0.0 - 2022-11-12
* Updated plugin to Microsoft .NET 7 for compatibility with NINA 3.0. The version of Astro-Physics Tools that is compatible with NINA 2.x will remain under the 1.x versioning scheme, and Astro-Physics Tools 2.x and later is relvant only to NINA 3.x and later.

## 1.0.0.0 - 2022-05-25
* Fixed validations for **Start APCC**
* Fixed rounding error that, under very specific circumstances, would result in 1 less arc being modeled than desired
* Fixed settings storage and fully migrated to profile-specific plugin settings
* The **APPM settings file** parameter may now be completely cleared of any text without gripe or complaint
* Minimum supported NINA version is now 2.0 Beta 59

## 0.5.0.0 - 2022-03-13
* Updated to support changes to DSO containers in NINA 2.0 beta 50
* Minimum supported NINA version is now 2.0 Beta 50

## 0.4.0.0 - 2022-02-17
* Removed "beta" designation
* Converted plugin to use NINA's new managed plugin options system. This allows for settings to be saved to the NINA profile. This means different profiles can have unique settings and parameters
* Minimum supported NINA version is now 2.0 Beta 45

## 0.3.0.0 - 2021-12-26
* Prevent plugin load failure on systems that don't have APCC Pro installed
* Removed unnecessary WPF data context bindings that just love to hold on to memory when they really should not be doing that. Bad bindings! BAD!
* Minimum supported NINA version is now 2.0 Beta 20

## 0.2.0.0 - 2021-11-26
* Moved Meridian and Horizon Limits setting to General Settings and both are now considered in the all-sky and dec arc instructions

## 0.1.0.0 - 2021-11-25
* **New:** **Create Dec Arc Model** instruction
* **New:** **Create All-Sky Model** instruction
* Instructions now check to make sure a camera is connected in NINA (for APPM's use) and raise a validation warning if one is not
* Minimum supported NINA version is now 2.0 Beta 4
