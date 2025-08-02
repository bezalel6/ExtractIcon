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
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                ShowUsage();
                return;
            }

            // Parse arguments
            var options = ParseArguments(args);
            if (options == null) return;

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

                // Remove existing output file
                if (File.Exists(options.OutputPath))
                    File.Delete(options.OutputPath);

                bool success = false;

                // Try Xbox icon extraction first if applicable
                if (IsXboxGame(options.InputPath))
                {
                    success = TryExtractXboxIcon(options);
                }

                // Fall back to standard icon extraction
                if (!success)
                {
                    success = ExtractStandardIcon(options);
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
            
            // Look for Content subdirectory
            if (!contentDir.EndsWith("\\Content", StringComparison.OrdinalIgnoreCase))
            {
                string contentSubDir = Path.Combine(contentDir, "Content");
                if (Directory.Exists(contentSubDir))
                    contentDir = contentSubDir;
            }

            if (!Directory.Exists(contentDir))
                return false;

            // Find the best icon file
            string iconFile = FindBestIcon(contentDir);
            if (string.IsNullOrEmpty(iconFile))
                return false;

            // Process the icon
            int targetSize = options.CustomSize ?? 256;
            bool processed = ProcessIcon(iconFile, options.OutputPath, targetSize);
            
            if (processed)
            {
                string fileName = Path.GetFileName(iconFile);
                Console.WriteLine($"Using image: {fileName}");
            }
            
            return processed;
        }

        private static string FindBestIcon(string searchDir)
        {
            // Look for all PNG files that might be icons
            var allPngFiles = Directory.GetFiles(searchDir, "*.png", SearchOption.AllDirectories);
            
            if (allPngFiles.Length == 0)
                return null;
            
            // Simply return the largest PNG file (by file size)
            return allPngFiles.OrderByDescending(f => new FileInfo(f).Length).First();
        }

        private static bool ProcessIcon(string inputPath, string outputPath, int targetSize)
        {
            // Try ImageMagick first for high quality
            if (TryImageMagick(inputPath, outputPath, targetSize))
                return true;

            // Fall back to basic .NET resize
            try
            {
                using (var original = new Bitmap(inputPath))
                using (var resized = new Bitmap(targetSize, targetSize))
                using (var graphics = Graphics.FromImage(resized))
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(original, 0, 0, targetSize, targetSize);
                    resized.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryImageMagick(string inputPath, string outputPath, int targetSize)
        {
            try
            {
                string args = $"\"{inputPath}\" -resize {targetSize}x{targetSize}^ -gravity center -extent {targetSize}x{targetSize} -filter Catrom \"{outputPath}\"";
                
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
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        string error = process.StandardError.ReadToEnd();
                        Console.WriteLine($"Error: ImageMagick failed - {error}");
                        Console.WriteLine("Please ensure ImageMagick is installed and available in PATH");
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: Could not run ImageMagick - {ex.Message}");
                Console.WriteLine("Please install ImageMagick from https://imagemagick.org/");
                return false;
            }
        }

        private static bool ExtractStandardIcon(ExtractionOptions options)
        {
            try
            {
                // Store original path for icon extraction
                string originalPath = options.InputPath;

                // Get short path
                StringBuilder sb = new StringBuilder(255);
                GetShortPathName(options.InputPath, sb, sb.Capacity);
                string shortPath = sb.ToString();

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

                if (ret != 0 || iImageList == null)
                {
                    Console.WriteLine($"Warning: Failed to get system image list (HRESULT: 0x{ret:X8})");
                }

                // Get icon index - use original path
                int iconIndex = GetIconIndex(originalPath);
                if (iconIndex == -1)
                {
                    Console.WriteLine("Warning: Could not get icon for file");
                    int lastError = Marshal.GetLastWin32Error();
                    Console.WriteLine($"  Error: {GetErrorMessage(lastError)}");
                    
                    if (IsXboxGame(originalPath))
                    {
                        Console.WriteLine("Note: Xbox Game Pass app may show generic icon due to access restrictions");
                    }
                    
                    iconIndex = 0; // Use default icon index
                }

                // Draw icon
                DrawImage(iImageList, iconHDCDest, iconIndex, 0, 0, ImageListDrawItemConstants.ILD_PRESERVEALPHA);
                iconBMP.Dispose();

                // Get the bitmap
                iconBMP = Bitmap.FromHbitmap(iconHBitmap);
                int detectedSize = DetectIconSize(iconBMP);

                // Crop to detected size if needed
                if (detectedSize < 256)
                {
                    Bitmap croppedBitmap = iconBMP.Clone(new Rectangle(0, 0, detectedSize, detectedSize), iconBMP.PixelFormat);
                    iconBMP.Dispose();
                    iconBMP = croppedBitmap;
                }

                // Make transparent and save the icon
                iconBMP.MakeTransparent();
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
                    ProcessIcon(options.OutputPath, options.OutputPath, options.CustomSize.Value);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting icon: {ex.Message}");
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
            IntPtr retVal = SHGetFileInfo(fileName, 0, ref shfi, shfiSize, (uint)SHGetFileInfoConstants.SHGFI_SYSICONINDEX);
            
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
            Console.WriteLine("Syntax: extracticon.exe [input_filename] [output_image] [-size N]");
            Console.WriteLine("        extracticon.exe [input_filename] [-size N]");
            Console.WriteLine("  -size N: Optional icon size (tested up to 256; larger sizes may impact quality/performance)");
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
        }
    }
}