ExtractIcon
===========

Simple command line utility to extract a Windows file icon as a PNG in its highest resolution. Tested on Windows 10 for icons with resolutions of 16x16, 32x32, 48x48, 64x64, and 256x256.

## Key Improvements in This Fork

**Fixed icon extraction for system files**: Added an application manifest that declares the app as DPI-aware and uses proper Windows compatibility settings. Without this manifest, Windows would return generic cached icons instead of the actual file icons for executables in Program Files and other protected directories.

**Added size selection**: You can now specify which icon size you want with the `-size` parameter. The original always extracted at 256x256; now you can choose any power of 2 from 4 to 256 (4, 8, 16, 32, 64, 128, or 256) to get the high-quality icon resized to the resolution you need.

**Automatic preview mode**: When no output path is provided, the icon is saved to a temporary file and automatically opened in your default image viewer. Windows handles cleanup of temporary files automatically.

Usage
=====

To output the icon associated with an executable as a PNG:

```
extracticon.exe file.exe file-icon.png
```

To extract and automatically preview an icon (saves to temp directory):

```
extracticon.exe file.exe
```

To output the icon associated with the PDF file handler as a PNG:

```
extracticon.exe file.pdf file-icon.png
```

To output an icon with a specific size (e.g., 64x64):

```
extracticon.exe file.exe file-icon.png -size 64
```

To extract and preview an icon at a specific size:

```
extracticon.exe file.exe -size 32
```

### Size Parameter

The `-size` parameter accepts power of 2 values: 4, 8, 16, 32, 64, 128, or 256. This uses ImageMagick (if installed) to resize the extracted icon to your desired dimensions while maintaining quality.

### Xbox Game Pass Support

ExtractIcon includes special support for Xbox Game Pass applications, which store their icons as PNG files rather than embedded resources. For 256px output, you can use the `-larger` flag to force extraction from the high-resolution 1080px source icon instead of the default 150px icon, resulting in better quality at the cost of processing time.

```
extracticon.exe XboxGame.exe icon.png -size 256 -larger
```

### Troubleshooting

The debug build (`bin\Debug\extracticon.exe`) is included in this repository specifically for troubleshooting icon extraction issues. It provides detailed diagnostic logging for:

- File access errors and permission issues (especially for protected directories)
- Xbox Game Pass icon detection and file selection process
- Icon size detection and resizing decisions
- Windows API error codes with human-readable descriptions
- Step-by-step extraction process for debugging failures

To use the debug build, simply run `bin\Debug\extracticon.exe` instead of the release version. The debug output will help identify why icon extraction might be failing for specific files.