/*
 * ExtractIcon (https://github.com/bertjohnson/extracticon)
 * 
 * Licensed according to the MIT License (http://mit-license.org/).
 * 
 * Copyright © Bert Johnson (https://bertjohnson.com/) of Allcloud Inc. (https://allcloud.com/).
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 * 
 */

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace extracticon
{
    public partial class Program
    {
        private static bool _debug = false;
        
        private static void DebugLog(string message)
        {
            if (_debug)
            {
                Console.WriteLine($"[DEBUG] {message}");
            }
        }
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                ShowUsage();
                return;
            }
            
            // Initialize COM for Shell API
            CoInitialize(IntPtr.Zero);

            // Parse arguments
            var options = ParseArguments(args);
            if (options == null) return;
            
            // Set debug mode
            _debug = options.Debug;
            if (_debug)
            {
                Console.WriteLine("[DEBUG] Debug mode enabled");
                Console.WriteLine($"[DEBUG] Input path: {options.InputPath}");
                Console.WriteLine($"[DEBUG] Output path: {options.OutputPath ?? "(will be generated)"}");
                if (options.CustomSize.HasValue)
                    Console.WriteLine($"[DEBUG] Custom size: {options.CustomSize.Value}");
            }

            // Set up output path
            if (string.IsNullOrEmpty(options.OutputPath))
            {
                string tempFileName = Path.GetFileNameWithoutExtension(options.InputPath) + "_icon.png";
                options.OutputPath = Path.Combine(Path.GetTempPath(), tempFileName);
                options.OpenAfterExtract = true;
            }

            try
            {
                // Clean paths
                options.InputPath = options.InputPath.Replace("file://", "").Replace("/", "\\");
                options.OutputPath = options.OutputPath.Replace("file://", "").Replace("/", "\\");

                // Create output directory if needed
                CreateOutputDirectory(options.OutputPath);
                DebugLog($"Output directory ensured: {Path.GetDirectoryName(options.OutputPath)}");

                // Remove existing output file
                if (File.Exists(options.OutputPath))
                {
                    DebugLog($"Removing existing output file: {options.OutputPath}");
                    File.Delete(options.OutputPath);
                }

                bool success = false;

                // Try Xbox icon extraction first if applicable
                if (IsXboxGame(options.InputPath))
                {
                    DebugLog("Detected Xbox game path, attempting Xbox icon extraction");
                    success = TryExtractXboxIcon(options);
                    DebugLog($"Xbox icon extraction {(success ? "succeeded" : "failed")}");
                }

                // Fall back to standard icon extraction
                if (!success)
                {
                    DebugLog("Attempting standard Windows icon extraction");
                    success = ExtractStandardIcon(options);
                    DebugLog($"Standard icon extraction {(success ? "succeeded" : "failed")}");
                }

                if (success)
                {
                    Console.WriteLine("Success");
                    
                    if (options.OpenAfterExtract && File.Exists(options.OutputPath))
                    {
                        OpenFile(options.OutputPath);
                    }
                }
                else
                {
                    Console.WriteLine("Error: Failed to extract icon");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                // Uninitialize COM
                CoUninitialize();
            }
        }

        private static ExtractionOptions ParseArguments(string[] args)
        {
            var options = new ExtractionOptions();
            
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLower();
                
                if (arg == "-size" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int size) && size > 0)
                    {
                        options.CustomSize = size;
                        i++;
                    }
                    else
                    {
                        Console.WriteLine("Error: -size must be a positive number");
                        return null;
                    }
                }
                else if (arg == "-debug")
                {
                    options.Debug = true;
                }
                else if (string.IsNullOrEmpty(options.InputPath))
                {
                    options.InputPath = args[i];
                }
                else if (string.IsNullOrEmpty(options.OutputPath))
                {
                    options.OutputPath = args[i];
                }
            }

            if (string.IsNullOrEmpty(options.InputPath))
            {
                ShowUsage();
                return null;
            }

            return options;
        }

        private static bool IsXboxGame(string path)
        {
            return path.Contains("\\XboxGames\\") || path.Contains("\\WindowsApps\\");
        }

        private static bool TryExtractXboxIcon(ExtractionOptions options)
        {
            string contentDir = Path.GetDirectoryName(options.InputPath);
            DebugLog($"Xbox icon extraction - initial directory: {contentDir}");
            
            // Look for Content subdirectory
            if (!contentDir.EndsWith("\\Content", StringComparison.OrdinalIgnoreCase))
            {
                string contentSubDir = Path.Combine(contentDir, "Content");
                DebugLog($"Checking for Content subdirectory: {contentSubDir}");
                if (Directory.Exists(contentSubDir))
                {
                    contentDir = contentSubDir;
                    DebugLog($"Using Content subdirectory: {contentDir}");
                }
            }

            if (!Directory.Exists(contentDir))
            {
                DebugLog($"Content directory does not exist: {contentDir}");
                return false;
            }

            // Find the best icon file
            string iconFile = FindBestIcon(contentDir);
            if (string.IsNullOrEmpty(iconFile))
            {
                DebugLog("No suitable icon file found");
                return false;
            }
            
            DebugLog($"Selected icon file: {iconFile}");

            // Process the icon
            int targetSize = options.CustomSize ?? 256;
            DebugLog($"Processing icon with target size: {targetSize}x{targetSize}");
            bool processed = ProcessIcon(iconFile, options.OutputPath, targetSize);
            
            if (processed)
            {
                string fileName = Path.GetFileName(iconFile);
                Console.WriteLine($"Using image: {fileName}");
                DebugLog($"Icon processing succeeded for: {fileName}");
            }
            else
            {
                DebugLog("Icon processing failed");
            }
            
            return processed;
        }

        private static string FindBestIcon(string searchDir)
        {
            DebugLog($"Searching for PNG files in: {searchDir}");
            
            // Look for all PNG files that might be icons
            var allPngFiles = Directory.GetFiles(searchDir, "*.png", SearchOption.AllDirectories);
            DebugLog($"Found {allPngFiles.Length} PNG files");
            
            if (allPngFiles.Length == 0)
                return null;
            
            // Log all found files with sizes in debug mode
            if (_debug)
            {
                foreach (var file in allPngFiles)
                {
                    var fileInfo = new FileInfo(file);
                    DebugLog($"  - {Path.GetFileName(file)} ({fileInfo.Length:N0} bytes)");
                }
            }
            
            // Simply return the largest PNG file (by file size)
            var bestFile = allPngFiles.OrderByDescending(f => new FileInfo(f).Length).First();
            var bestFileInfo = new FileInfo(bestFile);
            DebugLog($"Selected largest file: {Path.GetFileName(bestFile)} ({bestFileInfo.Length:N0} bytes)");
            
            return bestFile;
        }

        private static bool ProcessIcon(string inputPath, string outputPath, int targetSize)
        {
            DebugLog($"Processing icon: {inputPath} -> {outputPath} at {targetSize}x{targetSize}");
            
            // Try ImageMagick first for high quality
            if (TryImageMagick(inputPath, outputPath, targetSize))
            {
                DebugLog("Icon processed successfully with ImageMagick");
                return true;
            }

            // Fall back to basic .NET resize
            DebugLog("ImageMagick not available, falling back to .NET resize");
            try
            {
                using (var original = new Bitmap(inputPath))
                {
                    DebugLog($"Original image dimensions: {original.Width}x{original.Height}");
                    
                    using (var resized = new Bitmap(targetSize, targetSize))
                    using (var graphics = Graphics.FromImage(resized))
                    {
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(original, 0, 0, targetSize, targetSize);
                        resized.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                        DebugLog(".NET resize completed successfully");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($".NET resize failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryImageMagick(string inputPath, string outputPath, int targetSize)
        {
            try
            {
                string args = $"\"{inputPath}\" -resize {targetSize}x{targetSize}^ -gravity center -extent {targetSize}x{targetSize} -filter Catrom \"{outputPath}\"";
                DebugLog($"Attempting ImageMagick with command: magick {args}");
                
                var psi = new ProcessStartInfo
                {
                    FileName = "magick",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    
                    DebugLog($"ImageMagick exit code: {process.ExitCode}");
                    if (!string.IsNullOrEmpty(output))
                        DebugLog($"ImageMagick output: {output}");
                    
                    if (process.ExitCode != 0)
                    {
                        Console.WriteLine($"Error: ImageMagick failed - {error}");
                        Console.WriteLine("Please ensure ImageMagick is installed and available in PATH");
                        DebugLog($"ImageMagick error: {error}");
                        return false;
                    }
                    
                    DebugLog("ImageMagick completed successfully");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: Could not run ImageMagick - {ex.Message}");
                Console.WriteLine("Please install ImageMagick from https://imagemagick.org/");
                DebugLog($"ImageMagick exception: {ex.GetType().Name} - {ex.Message}");
                return false;
            }
        }

        private static bool ExtractStandardIcon(ExtractionOptions options)
        {
            try
            {
                // Store original path for icon extraction
                string originalPath = options.InputPath;
                DebugLog($"Original path: {originalPath}");

                // Get short path
                StringBuilder sb = new StringBuilder(255);
                GetShortPathName(options.InputPath, sb, sb.Capacity);
                string shortPath = sb.ToString();
                DebugLog($"Short path: {shortPath}");

                // Create canvas
                IntPtr iconHDC = CreateDC("Display", null, null, IntPtr.Zero);
                IntPtr iconHDCDest = CreateCompatibleDC(iconHDC);
                Bitmap iconBMP = new Bitmap(256, 256, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                
                using (Graphics g = Graphics.FromImage(iconBMP))
                {
                    g.Clear(Color.Transparent);
                }

                IntPtr iconHBitmap = iconBMP.GetHbitmap();
                IntPtr iconHObj = SelectObject(iconHDCDest, iconHBitmap);

                // Get system image list
                Guid guidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                IImageList iImageList = null;
                IntPtr hIml = IntPtr.Zero;
                
                int ret = SHGetImageList(JUMBO_SIZE, ref guidImageList, ref iImageList);
                int ret2 = SHGetImageListHandle(JUMBO_SIZE, ref guidImageList, ref hIml);
                
                DebugLog($"SHGetImageList returned: 0x{ret:X8}, Handle: 0x{ret2:X8}");
                DebugLog($"IImageList instance: {(iImageList != null ? "obtained" : "null")}");
                DebugLog($"Image list handle: 0x{hIml.ToInt64():X}");

                if (ret != 0 || iImageList == null)
                {
                    Console.WriteLine($"Warning: Failed to get system image list (HRESULT: 0x{ret:X8})");
                }

                // Get icon index - use original path
                int iconIndex = GetIconIndex(originalPath);
                DebugLog($"Icon index: {iconIndex}");
                
                // Check if this might be a generic icon
                if (_debug)
                {
                    // Common generic icon indices (may vary by system)
                    int[] genericIconIndices = { 0, 2, 3, 150, 151 };
                    if (Array.IndexOf(genericIconIndices, iconIndex) >= 0)
                    {
                        DebugLog($"WARNING: Icon index {iconIndex} is commonly associated with generic/default icons");
                    }
                }
                
                if (iconIndex == -1)
                {
                    Console.WriteLine("Warning: Could not get icon for file");
                    int lastError = Marshal.GetLastWin32Error();
                    Console.WriteLine($"  Error: {GetErrorMessage(lastError)}");
                    DebugLog($"Win32 error code: {lastError}");
                    
                    if (IsXboxGame(originalPath))
                    {
                        Console.WriteLine("Note: Xbox Game Pass app may show generic icon due to access restrictions");
                    }
                    
                    iconIndex = 0; // Use default icon index
                    DebugLog("Using default icon index: 0");
                }

                // Draw icon
                DebugLog($"Drawing icon at index {iconIndex} with ILD_PRESERVEALPHA");
                DrawImage(iImageList, iconHDCDest, iconIndex, 0, 0, ImageListDrawItemConstants.ILD_PRESERVEALPHA);

                // Get the bitmap from the device context
                iconBMP = Bitmap.FromHbitmap(iconHBitmap);
                DebugLog($"Bitmap dimensions: {iconBMP.Width}x{iconBMP.Height}");
                
                int detectedSize = DetectIconSize(iconBMP);
                DebugLog($"Detected icon size: {detectedSize}x{detectedSize}");

                // Crop to detected size if needed
                if (detectedSize < 256)
                {
                    DebugLog($"Cropping bitmap from 256x256 to {detectedSize}x{detectedSize}");
                    Bitmap croppedBitmap = iconBMP.Clone(new Rectangle(0, 0, detectedSize, detectedSize), iconBMP.PixelFormat);
                    iconBMP.Dispose();
                    iconBMP = croppedBitmap;
                }

                // Make transparent and save the icon
                iconBMP.MakeTransparent();
                
                // Debug: check if the bitmap has actual content
                if (_debug)
                {
                    bool hasContent = false;
                    Color firstPixel = iconBMP.GetPixel(0, 0);
                    for (int x = 0; x < Math.Min(50, iconBMP.Width) && !hasContent; x++)
                    {
                        for (int y = 0; y < Math.Min(50, iconBMP.Height) && !hasContent; y++)
                        {
                            Color pixel = iconBMP.GetPixel(x, y);
                            if (pixel != firstPixel || pixel.A > 0)
                            {
                                hasContent = true;
                            }
                        }
                    }
                    DebugLog($"Bitmap content check: {(hasContent ? "has visible content" : "appears empty/transparent")}");
                    DebugLog($"First pixel: ARGB({firstPixel.A},{firstPixel.R},{firstPixel.G},{firstPixel.B})");
                }
                
                DebugLog($"Saving icon to: {options.OutputPath}");
                iconBMP.Save(options.OutputPath, System.Drawing.Imaging.ImageFormat.Png);
                iconBMP.Dispose();

                // Clean up handles
                DeleteDC(iconHDCDest);
                DeleteObject(iconHObj);
                DeleteObject(iconHBitmap);
                DeleteDC(iconHDC);

                // Resize if custom size specified
                if (options.CustomSize.HasValue)
                {
                    DebugLog($"Resizing icon to custom size: {options.CustomSize.Value}x{options.CustomSize.Value}");
                    ProcessIcon(options.OutputPath, options.OutputPath, options.CustomSize.Value);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting icon: {ex.Message}");
                DebugLog($"Exception type: {ex.GetType().Name}");
                DebugLog($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        private static int DetectIconSize(Bitmap bitmap)
        {
            // Check common icon sizes from largest to smallest
            int[] sizes = { 256, 128, 64, 48, 32, 16 };
            
            foreach (int size in sizes)
            {
                if (bitmap.Width >= size && bitmap.Height >= size)
                {
                    // Check if pixels outside this size are transparent/consistent
                    bool hasContentOutside = false;
                    
                    // Get the color at the edge
                    Color edgeColor = (size < bitmap.Width) ? bitmap.GetPixel(size, size - 1) : Color.Transparent;
                    
                    // Check right edge
                    if (bitmap.Width > size)
                    {
                        for (int y = 0; y < Math.Min(size, bitmap.Height) && !hasContentOutside; y++)
                        {
                            Color pixel = bitmap.GetPixel(size, y);
                            if (pixel != edgeColor) hasContentOutside = true;
                        }
                    }
                    
                    // Check bottom edge
                    if (bitmap.Height > size && !hasContentOutside)
                    {
                        for (int x = 0; x < Math.Min(size, bitmap.Width) && !hasContentOutside; x++)
                        {
                            Color pixel = bitmap.GetPixel(x, size);
                            if (pixel != edgeColor) hasContentOutside = true;
                        }
                    }
                    
                    if (!hasContentOutside)
                        return size;
                }
            }
            
            // Default to actual size if no standard size detected
            return Math.Min(bitmap.Width, bitmap.Height);
        }

        private static int GetIconIndex(string fileName)
        {
            SHFILEINFO shfi = new SHFILEINFO();
            uint shfiSize = (uint)Marshal.SizeOf(shfi.GetType());
            DebugLog($"Calling SHGetFileInfo for: {fileName}");
            
            // Try with SYSICONINDEX first
            IntPtr retVal = SHGetFileInfo(fileName, 0, ref shfi, shfiSize, (uint)SHGetFileInfoConstants.SHGFI_SYSICONINDEX);
            DebugLog($"SHGetFileInfo returned: 0x{retVal.ToInt64():X}, Icon index: {shfi.iIcon}");
            
            // If we get a generic icon, try with additional flags
            if (_debug && (shfi.iIcon == 0 || shfi.iIcon == 2 || shfi.iIcon == 3 || shfi.iIcon == 150 || shfi.iIcon == 151))
            {
                DebugLog("Trying alternative SHGetFileInfo with SHGFI_ICONLOCATION flag");
                SHFILEINFO shfi2 = new SHFILEINFO();
                IntPtr retVal2 = SHGetFileInfo(fileName, 0, ref shfi2, shfiSize, 
                    (uint)(SHGetFileInfoConstants.SHGFI_ICONLOCATION | SHGetFileInfoConstants.SHGFI_SYSICONINDEX));
                DebugLog($"Alternative call returned: 0x{retVal2.ToInt64():X}, Icon index: {shfi2.iIcon}");
                if (shfi2.szDisplayName != null && shfi2.szDisplayName.Length > 0)
                {
                    DebugLog($"Icon location: {shfi2.szDisplayName}, Icon index in file: {shfi2.iIcon}");
                }
                
                // If still generic, try forcing the file to be treated as an executable
                DebugLog("Trying with FILE_ATTRIBUTE_NORMAL flag");
                SHFILEINFO shfi3 = new SHFILEINFO();
                IntPtr retVal3 = SHGetFileInfo(fileName, 0x80, ref shfi3, shfiSize, 
                    (uint)(SHGetFileInfoConstants.SHGFI_SYSICONINDEX | SHGetFileInfoConstants.SHGFI_USEFILEATTRIBUTES));
                DebugLog($"FILE_ATTRIBUTE_NORMAL call returned: 0x{retVal3.ToInt64():X}, Icon index: {shfi3.iIcon}");
                
                // If we got a better icon with the alternative methods, use it
                if (shfi3.iIcon != 0 && shfi3.iIcon != 2 && shfi3.iIcon != 3 && shfi3.iIcon != 150 && shfi3.iIcon != 151)
                {
                    DebugLog($"Using improved icon index from FILE_ATTRIBUTE_NORMAL: {shfi3.iIcon}");
                    return shfi3.iIcon;
                }
            }
            
            return retVal.Equals(IntPtr.Zero) ? -1 : shfi.iIcon;
        }

        private static string GetErrorMessage(int errorCode)
        {
            switch (errorCode)
            {
                case 2: return "File not found";
                case 3: return "Path not found";
                case 5: return "Access denied (may require elevated permissions)";
                case 32: return "File is being used by another process";
                case 87: return "Invalid parameter";
                case 1223: return "Operation cancelled by user";
                case 1314: return "Required privilege not held";
                default: return $"Error code {errorCode}";
            }
        }

        private static void CreateOutputDirectory(string outputPath)
        {
            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static void OpenFile(string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
                Console.WriteLine($"Icon saved to: {filePath}");
            }
            catch
            {
                Console.WriteLine($"Icon saved to: {filePath}");
            }
        }

        private static void ShowUsage()
        {
            Console.WriteLine("Syntax: extracticon.exe [input_filename] [output_image] [-size N] [-debug]");
            Console.WriteLine("        extracticon.exe [input_filename] [-size N] [-debug]");
            Console.WriteLine("  -size N: Optional icon size (tested up to 256; larger sizes may impact quality/performance)");
            Console.WriteLine("  -debug:  Enable debug mode to display detailed diagnostic information");
            Console.WriteLine("  If output_image is omitted, the icon will be saved to a temp file and opened");
        }

        private static void DrawImage(IImageList iImageList, IntPtr hdc, int index, int x, int y, ImageListDrawItemConstants flags)
        {
            IMAGELISTDRAWPARAMS pimldp = new IMAGELISTDRAWPARAMS();
            pimldp.hdcDst = hdc;
            pimldp.cbSize = Marshal.SizeOf(pimldp.GetType());
            pimldp.i = index;
            pimldp.x = x;
            pimldp.y = y;
            pimldp.rgbFg = -1;
            pimldp.fStyle = (int)flags;
            iImageList.Draw(ref pimldp);
        }

        private class ExtractionOptions
        {
            public string InputPath { get; set; }
            public string OutputPath { get; set; }
            public int? CustomSize { get; set; }
            public bool OpenAfterExtract { get; set; }
            public bool Debug { get; set; }
        }
    }
}