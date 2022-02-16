# Astro-Physics Tools

## 0.4.0.0 - ???
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
