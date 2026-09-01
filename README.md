## [Download latest release](https://github.com/guzenco/msovideo_srgb/releases/latest/)

# About
This tool uses an ICC profile with MHC2 tag to convert colors before sending them to a wide gamut display to effectively clamp it to sRGB (alternatively: Display P3, Adobe RGB, or BT.2020), based on the chromaticities provided in its EDID.

ICC profiles are also supported and can be used in two different ways. By default, only the primary coordinates from the ICC profile will be used in place of the values reported in the EDID. This is useful if you want to use a profile created by someone else without taking their gamma/grayscale balance data into account, as that can vary a lot between units. If you enable the `Calibrate gamma to` checkbox, a full LUT-Matrix-LUT calibration will be applied. This is similar to the hardware calibration supported by some displays and can be used to achieve great color and grayscale accuracy on well-behaved displays.

You can use "Target White" to calibrate whitepoint to a desirable value (D50, D65, D93, or Custom x, y). It is especially useful if you can't achieve a desirable whitepoint with the display's RGB gain controls.

For HDR mode, ICC profiles can be used to calibrate display gamma to PQ ST2084 (hard clip) and to provide measured static metadata. For more details, see the section "Notes for HDR calibration" below.

The tool generates and applies an ICC profile that contains idealized display characteristics under MHC2 tag color transformations. In other words, this profile describes the display as ideally matching the selected color space and gamma, allowing applications that support ICC profiles to deliver accurate and consistent results.

# System requirements

The tool relies on MHC2, and therefore its requirements align with those of the [Windows hardware display color calibration pipeline](https://learn.microsoft.com/en-us/windows/win32/wcs/display-calibration-mhc#system-requirements).

Windows 10, version 2004 (20H1) or later:
* AMD:
  * AMD RX 500 400 Series, or later
  * AMD Ryzen processors with Radeon Graphics
* Intel:
  * Integrated: Intel 10th Gen GPU (Ice Lake), or later
  * Discrete: Intel DG1, or later
* NVIDIA GTX 10xx, or later (Pascal+)
* Qualcomm 8CX Gen 3, or later; 7C Gen 3, or later

# Usage

Extract `msovideo_srgb.zip` somewhere under your user directory and run `msovideo_srgb.exe`. To enable/disable the sRGB clamp for a displays, simply toggle the "Clamped" checkbox. For using ICC profiles, click the "Advanced" button.

Settings under "MHC2 Profile" can be used to resolve compatibility issues, such as unwanted gamma 2.2 to sRGB transformation, with color-aware applications.

The clamp may be lost under certain conditions. You can leave the application running minimized in the background to have it automatically reapply the clamp.

# Notes for use with EDID data

* If the clamp is active but does not affect colors, try changing the target. If saturation changes, that means the display is either natively sRGB or uses an sRGB emulation mode by default. In that case, if you are sure your display is not in sRGB, complain to the manufacturer about the EDID being wrong and try to find an ICC profile for your display to use instead of the EDID data.

* The reported white point is not taken into account when calculating the color space conversion matrix. Instead, the display is always assumed to be calibrated to D65 white.

# Notes for use with ICC profiles

Only the VCGT (if present), TRCs, PCS matrix parts, whitepoint, blackpoint, and luminance of an ICC profile are used. If present, the A2B1 data is used to calculate presumably higher-quality TRC and PCS matrix values. To achieve the best results, a profile must report the display’s behavior in these parameters as accurately as possible.

General recommendations for measurements:
* The display must be in native mode (usually named Custom or User in the display settings).
* All options that affect color reproduction must be disabled:
	* Windows:
		* MHC2: You must ensure that the display doesn’t have an active profile with the MHC2 tag.
		* ACM: In this mode there is always a clamp. Although workarounds exist, they have drawbacks, so ACM must be disabled for measurements.
	* NVIDIA:
		* Reference mode: Must be disabled for the 1D LUT from the MHC2 profile to work, so it’s better to disable it for measurements as well. Option available in the Display tab in NVIDIA App or NVIDIA Control Panel.
		* NVAPI: If you use software that applies a clamp through this, disable it.
	* AMD:
		* Custom Color: It applies driver level clamp, you must disable it. Option available in Display tab in AMD Software. 
		* ADL API: If you use software that applies a clamp through this, disable it.
* Identical dithering settings must be used for both measurement and profile use. Usually, enabling dithering improves results and fixes banding. The setup method depends on the GPU vendor.
* Do not use calibration options (like gamma, whitepoint, blackpoint, luminance, etc.) in measurement software. Set them to "Native" or "As measured". These calibrations utilize VCGT 1D LUTs, and profiles with them describe the display’s behavior under those LUTs. This usually takes more time to measure and usually produces worse results than the tool’s calibration options. Using profiles with VCGT alongside the tool’s calibration options usually yields results that are the same or worse compared to results with profiles without them.

Recommendations for measurements in DisplayCAL:
* Calibration tab: 
  * Tone curve: As measured (this disables calibration so you can freely use other settings for convenient interactive display adjustment)
  * Everything else: As measured (or target if using interactive display adjustment)
* Profile tab:
  * Profile type: Curves + matrix ("Black point compensation" disabled)
  * Profile quality: High
  * Testchart: Small testchart for matrix profiles (with a high number of neutral (grayscale) patches, such as 256)
  
Verification of calibration in DisplayCAL:
* Simulation profile (checked): profile of target color space ("sRGB IEC61966-2.1" for sRGB, for example)
* Use simulation profile as display profile (checked)
* Tone curve: corresponding to target gamma (for sRGB, use "Apple black output offset (100%)" with the simulation profile that has that curve, such as "sRGB IEC61966-2.1")
* Device link profile (unchecked)

Using the tool’s whitepoint calibration allows you to reach the desirable whitepoint with minimal contrast loss, though at the cost of signal range sent to the display. It is especially useful if the display lacks RGB gain controls, if those controls cannot reach the desirable whitepoint, or can only with a significant contrast loss. This option decreases luminance, so it is better to have some margin.

The sRGB gamma option provides the best ΔE. Using different gamma settings, especially Absolute or Relative with a 100% black output offset, results in oversaturation/desaturation of colors depending on the display’s native color space and its final gamma difference from sRGB. This comes from limitations of MHC2, and it can be mitigated by enabling "Optimize CSC matrix for gamma".

# Notes for HDR calibration

HDR calibration requires a separate profile measured in HDR mode. Recommendations for measurements are the same as in the "Notes for use with ICC profiles" with one addition. Measurement targets must be displayed in HDR (not SDR via HDR mode). 

To achieve this, you can use [dogegen](https://github.com/ledoge/dogegen):
 1. Set "Display" to "Resolve" in DisplayCAL.
 2. Uncheck "Override minimum display update delay" (optional).
 3. Start "Calibrate & profile".
 4. Run dogegen with:
```
dogegen.exe "resolve_hdr 127.0.0.1"
```
 5. Measure targets displayed in the dogegen window.

Tool settings:
* Peak target - Limits display luminance in HDR mode. It will be ignored if set higher than the display profile luminance.
* BPC threshold - Prevents black crush by linearly scaling target's `[0, threshold]` to `[profile black, threshold]`.

In HDR mode, the target color space is treated as native.

# Notes for whitepoint calibration

In some cases, usually in HDR, due to a discrepancy between the profile data and actual display behavior, whitepoint calibration might miss the target. You may also encounter problems when trying to achieve a whitepoint of an observer other than CIE 1931.

To finetune whitepoint:
1. Set "Target White" to "Custom" with the desired whitepoint `x, y` (for example, 0.3127, 0.329).
2. Measure the actual whitepoint `x, y`.
3. Adjust the `x, y` in the tool according to the measured ones. If the measured `x` or `y` is higher/lower than the desired values, then decrease/increase it accordingly.
4. Apply the change.
5. Repeat steps 2-4 until you reach the desired whitepoint.

# Command line arguments

 `-force` - Close other instances at startup
 
 `-minimize` - Minimize at startup
 
 `-autoclose` - Close after startup

 `-preset=<id>` - Set preset #\<id\> at startup

# Known issues

* The color space transform does not get applied properly to the mouse cursor, which results in it having wrong gamma and colors. This should be hardly noticeable with the default Windows cursor. Workaround: Force software rendering of the cursor, e.g. using [SoftCursor](https://www.monitortests.com/forum/Thread-SoftCursor).
* R590 NVIDIA drivers may cause unexpected color distortions with or without MHC2 profiles. Workaround: Enable ACM (available only on Windows 11). This issue seems to be fixed in R610.
* In a multidisplay setup, a logical disconnection of one of the displays (in Control Panel, for example) may cause issues with profile associations, to the point where multiple displays end up sharing the same profile association and are unable to use separate ones.
