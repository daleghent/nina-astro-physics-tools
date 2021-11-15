# Astro-Physics Tools

Astro-Physics Tools is a collection of Instructions and other things for running from within NINA's Advanced Sequencer.

Please refer to this [plugin's website](https://daleghent.com/utilities-for-astro-physics-mounts) for detailed documentation. I also maintain a [website with tips](https://daleghent.com/nina-and-astro-physics-mounts) on using NINA with Astro-Physics mounts.

## Requirements

* NINA 1.11
* APCC Pro 1.9 or APCC Standard 1.8 (APCC Pro is required for the use of APPM)
* The **Enable Server** setting is **On** in NINA's options (refer to Options > General)

## Provided functions

* Start APCC
    * Starts APCC and connects NINA to the Astro-Physics ASCOM driver. Assumes that APCC's **Auto-Connect** setting is selected in both the **Mount** and **AP V2 Driver** option areas in the Setup tab
* Create APPM Model
    * A sequence instruction that will run Astro-Physics Point Mapper (APPM) in an automated mode. When ran, APPM will use its existing default settings to run a point mapping session and will load the results into APCC Pro when complete. If the default settings and point map are not desired, an APPM settings or a point map file may be optionally specified below in this plugin's settings.

## Getting help

Help for this plugin may be found in the **#plugin-discussions** channel on the NINA project [Discord chat server](https://discord.com/invite/rWRbVbw), Astro-Physics' [AP-GTO forum](https://ap-gto.groups.io/g/main), or by filing an issue report at this plugin's [Github repository](https://github.com/daleghent/nina-plugins/issues).

* Astro-Physics Tools is provided 'as is' under the terms of the [Mozilla Public License 2.0](https://github.com/daleghent/nina-astro-physics-tools/blob/main/LICENSE.txt)
* Source code for this plugin is available at this plugin's [source code repository](https://github.com/daleghent/nina-astro-physics-tools)