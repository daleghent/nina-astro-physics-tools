# Astro-Physics Tools

## 1.4.0.0 - 2021-11-??
* Plugin renamed to **Astro-Physics Tools** - same great taste, all-new flavor. But, really, the original name was a mouthful.
* Renamed the **Create APPM Model** instruction to **Create All-Sky Model**
* Initial beta of the new **Create Dec Arc Model** instruction
* Instructions now check to make sure a camera is connected in NINA (for APPM's use) and raise a validation warning if one is not
* Minimum supported NINA version is now 2.0 Beta 4

## 1.3.5.0 - 2021-10-27
* Updated the settings save routine to use the new safe multi-instance method in 1.11 build 172

## 1.3.0.0 - 2021-9-1
* Added instruction: Start APCC
    - Starts APCC and connects NINA to the Astro-Physics ASCOM driver. Requires the following:
        * **Auto-Connect** is selected in APCC's Setup tab for both the **Mount** and **AP V2 Driver**
        * The **Astro-Physics GTO V2 Mount** driver is already selected and saved in the active NINA profile

## 1.2.0.0 - unreleased
* Updated plugin description to markdown format
* Minimum supported NINA version is now 1.11 build 125

## 1.1.0.0 - 2021-8-1
* Minimum supported NINA version is now 1.11 build 116

## 1.0.0.1 - 2021-6-29

* Initial release
* Added Instruction: Create APPM Model