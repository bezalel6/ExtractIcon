ExtractIcon
===========

Simple command line utility to extract a Windows file icon as a PNG in its highest resolution. Tested on Windows 10 for icons with resolutions of 16x16, 32x32, 48x48, 64x64, and 256x256.

## Key Improvements in This Fork

**Fixed icon extraction for system files**: Added an application manifest that declares the app as DPI-aware and uses proper Windows compatibility settings. Without this manifest, Windows would return generic cached icons instead of the actual file icons for executables in Program Files and other protected directories.

**Added size selection**: You can now specify which icon size you want with the `-size` parameter. The original always extracted at 256x256; now you can choose 16, 32, 48, 64, 128, or 256 to get the high-quality icon resized to the resolution you need.

Usage
=====

To output the icon associated with an executable as a PNG:

```
extracticon.exe file.exe file-icon.png
```

To output the icon associated with the PDF file handler as a PNG:

```
extracticon.exe file.pdf file-icon.png
```

To output an icon with a specific size (e.g., 64x64):

```
extracticon.exe file.exe file-icon.png -size 64
```