ExtractIcon
===========

Simple command line utility to extract a Windows file icon as a PNG in its highest resolution. Tested on Windows 10 for icons with resolutions of 16x16, 32x32, 48x48, 64x64, and 256x256.

This fork fixes an issue where files in certain locations (like Program Files) would return incorrect cached icons instead of their actual icons.

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