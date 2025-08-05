# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

ExtractIcon is a command-line utility for Windows that extracts file icons as PNG images in their highest available resolution. It uses Windows Shell32 API to retrieve system file icons and saves them as transparent PNG files.

## Architecture

The application is split into two main files:
- `Program.cs`: Contains the main application logic for icon extraction, including bitmap manipulation and size detection
- `Program.References.cs`: Contains all P/Invoke declarations, structs, enums, and COM interface definitions for Windows API interaction

Key architectural components:
- Uses Windows Shell32 API (`SHGetFileInfo`, `SHGetImageList`) to retrieve system icons
- Leverages COM interop with `IImageList` interface for accessing jumbo-sized (256x256) icons
- Implements automatic icon size detection by checking pixel consistency to find actual icon dimensions
- Uses GDI+ for bitmap operations and PNG export

## Building the Project

The project uses MSBuild and targets .NET Framework 4.6.1:

```bash
# Build in Debug mode
msbuild ExtractIcon.sln /p:Configuration=Debug

# Build in Release mode
msbuild ExtractIcon.sln /p:Configuration=Release
```

The compiled executable will be in `bin\Debug\` or `bin\Release\` as `extracticon.exe`.

## Usage

The utility supports the following command-line arguments:
1. Input file path (the file whose icon you want to extract)
2. Output PNG file path (optional - if omitted, saves to temp and opens)
3. `-size N` (optional - resize icon to N×N pixels)
4. `-debug` (optional - enable detailed diagnostic output)

```bash
extracticon.exe [input_file] [output_png] [-size N] [-debug]
```

Examples:
- Extract icon from an executable: `extracticon.exe notepad.exe notepad-icon.png`
- Extract icon from a file type: `extracticon.exe document.pdf pdf-icon.png`
- Extract with custom size: `extracticon.exe app.exe icon.png -size 64`
- Extract with debug info: `extracticon.exe app.exe icon.png -debug`

## Technical Details

- The application attempts to extract icons at 256x256 resolution (JUMBO_SIZE)
- It automatically detects the actual icon size by checking pixel consistency, supporting 16x16, 32x32, 48x48, 64x64, 128x128, and 256x256 icons
- Output is always a transparent PNG file
- Uses short path names internally to handle paths with special characters
- Creates output directories if they don't exist
- Uses the original long path for icon extraction to avoid Windows icon cache issues that can return incorrect icons for files in certain locations (like Program Files)

## Debug Mode

When the `-debug` flag is specified, the application provides detailed diagnostic information:

- **Path Processing**: Shows original and short path conversions
- **API Calls**: Displays Windows Shell32 API call results and return codes
- **Icon Detection**: Reports icon index, detected size, and bitmap dimensions
- **Xbox Game Support**: Lists all PNG files found and their sizes when extracting Xbox icons
- **ImageMagick**: Shows command line arguments and process exit codes
- **Error Details**: Provides specific Win32 error codes and their meanings
- **Processing Steps**: Traces through each major step of the extraction process

This diagnostic information is prefixed with `[DEBUG]` and helps troubleshoot extraction issues.