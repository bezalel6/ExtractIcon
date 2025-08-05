ExtractIcon
===========

Simple command line utility to extract a Windows file icon as a PNG in its highest resolution. Automatically detects and extracts icons at their native sizes up to 256x256.

## Key Improvements in This Fork

**Custom size output**: Use the `-size` parameter to resize extracted icons to any dimension you need. Icons are resized using high-quality algorithms (ImageMagick if available, or bicubic interpolation as fallback).

**Xbox Game Pass support**: Automatically detects and extracts icons from Xbox Game Pass and Windows Store applications, which store their icons as PNG files rather than embedded resources.

Usage
=====

Basic usage:
```
extracticon.exe [input_file] [output_png] [-size N] [-debug]
```

### Examples

Extract icon to a specific file:
```
extracticon.exe notepad.exe notepad-icon.png
```

Extract and automatically preview (saves to temp and opens):
```
extracticon.exe notepad.exe
```

Extract icon from any file type (uses associated program's icon):
```
extracticon.exe document.pdf pdf-icon.png
```

Extract and resize to specific dimensions:
```
extracticon.exe notepad.exe icon-64.png -size 64
extracticon.exe game.exe icon-512.png -size 512
```

Extract with debug information:
```
extracticon.exe notepad.exe icon.png -debug
extracticon.exe "C:\Program Files\App\app.exe" -debug
```

### Parameters

- **input_file**: Path to the file whose icon you want to extract
- **output_png**: (Optional) Path for the output PNG file. If omitted, saves to temp directory and opens automatically
- **-size N**: (Optional) Resize the icon to N×N pixels. Accepts any positive integer value
- **-debug**: (Optional) Enable debug mode to display detailed diagnostic information

### Debug Mode

When the `-debug` flag is enabled, ExtractIcon provides detailed diagnostic information about the icon extraction process, including:

- File paths being processed (original and short paths)
- Windows API call results and error codes
- Icon detection and sizing information
- Xbox Game Pass icon search details
- ImageMagick command execution details
- Bitmap dimensions and processing steps

This information is helpful for troubleshooting extraction issues or understanding why certain icons may not extract as expected.

### Xbox Game Pass Support

ExtractIcon automatically detects Xbox Game Pass and Windows Store applications and extracts their icons from PNG files in the application package. The largest available icon is automatically selected for the best quality.

### Requirements

- Windows 7 or later
- .NET Framework 4.6.1 or later
- (Optional) ImageMagick for highest quality resizing - available at https://imagemagick.org/

### Building

The project uses MSBuild and targets .NET Framework 4.6.1:

```bash
# Build in Release mode
msbuild ExtractIcon.sln /p:Configuration=Release
```

The compiled executable will be in `bin\Release\extracticon.exe`.