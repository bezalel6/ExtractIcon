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
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace extracticon
{
    public partial class Program
    {
        /// <summary>
        /// Compiled regex patterns for Xbox icon file matching
        /// </summary>
        private static readonly Regex IconFilePattern = new Regex(
            @"^(store)?logo(\d+x\d+)?\.png$|" +                    // logo, storelogo with optional size
            @"^square(\d+x\d+)logo\.png$|" +                       // square*logo patterns
            @"^xbc_logo(\d+x\d+)?\.png$|" +                        // xbox classic patterns
            @"^(small|large)?logo\.png$|" +                        // small/large variants
            @"^splash(screen)?(image)?\.png$|" +                   // splash screen variants
            @"^largesquare\.png$",                                 // other variants
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        /// <summary>
        /// Regex for extracting size from filename
        /// </summary>
        private static readonly Regex SizeExtractor = new Regex(
            @"(\d+)x(\d+)", 
            RegexOptions.Compiled
        );

        /// <summary>
        /// Cache for Xbox icon discoveries to improve performance on repeated operations
        /// </summary>
        private static readonly ConcurrentDictionary<string, List<XboxIconCandidate>> IconCache = new ConcurrentDictionary<string, List<XboxIconCandidate>>();

        /// <summary>
        /// Represents an available Xbox icon file with its size
        /// </summary>
        private class XboxIconCandidate
        {
            public string FilePath { get; set; }
            public int Size { get; set; }
            public string FileName { get; set; }
            public int Priority { get; set; }
            public bool IsSplash { get; set; }
        }
        /// <summary>
        /// Extract an icon and paint it onto a canvas, then save as PNG.
        /// </summary>
        /// <param name="args">Array of command line arguments.</param>
        static void Main(string[] args)
        {
            if (args.Length >= 1)
            {
                // Parse parameters.
                string inp = "";
                string op = "";
                int? customSize = null;
                bool openAfterExtract = false;
                bool requireLarger = false; // Require using an icon larger than target size
                bool allowSplashFallback = false; // Allow using splash screen as fallback

                // Check for parameters
                int argIndex = 0;
                while (argIndex < args.Length)
                {
                    if (args[argIndex].ToLower() == "-size" && argIndex + 1 < args.Length)
                    {
                        int size;
                        if (int.TryParse(args[argIndex + 1], out size))
                        {
                            // Check if size is a power of 2 between 4 and 256
                            if ((size & (size - 1)) == 0 && size >= 4 && size <= 256)
                            {
                                customSize = size;
                            }
                            else
                            {
                                Console.WriteLine("Error: -size must be a power of 2 (4, 8, 16, 32, 64, 128, or 256)");
                                return;
                            }
                        }
                        else
                        {
                            Console.WriteLine("Error: -size must be followed by a power of 2 (4, 8, 16, 32, 64, 128, or 256)");
                            return;
                        }
                        argIndex += 2;
                    }
                    else if (args[argIndex].ToLower() == "-larger")
                    {
                        requireLarger = true;
                        argIndex++;
                    }
                    else if (args[argIndex].ToLower() == "-splash" || args[argIndex].ToLower() == "-allowsplash")
                    {
                        allowSplashFallback = true;
                        argIndex++;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(inp))
                            inp = args[argIndex];
                        else if (string.IsNullOrEmpty(op))
                            op = args[argIndex];
                        argIndex++;
                    }
                }

                // Validate we have at least an input file
                if (string.IsNullOrEmpty(inp))
                {
                    Console.WriteLine("Syntax: extracticon.exe [input_filename] [output_image] [-size N] [-larger] [-splash]");
                    Console.WriteLine("        extracticon.exe [input_filename] [-size N] [-larger] [-splash]");
                    Console.WriteLine("  -size N: Optional icon size - must be a power of 2 (4, 8, 16, 32, 64, 128, or 256)");
                    Console.WriteLine("  -larger: For 256px output, uses 1080px source instead of 150px (Xbox games only)");
                    Console.WriteLine("  -splash: Allow using splash screen as fallback if no suitable logo found (Xbox games only)");
                    Console.WriteLine("  If output_image is omitted, the icon will be saved to a temp file and opened");
                    return;
                }

                // If no output path provided, use temp file
                if (string.IsNullOrEmpty(op))
                {
                    string tempFileName = Path.GetFileNameWithoutExtension(inp) + "_icon.png";
                    op = Path.Combine(Path.GetTempPath(), tempFileName);
                    openAfterExtract = true;
                }

                inp = inp.Replace("file://", "").Replace("/", "\\");
                op = op.Replace("file://", "").Replace("/", "\\");

                // Store original path for icon extraction
                string originalPath = inp;

                // Determine the full file path.
                StringBuilder sb = new StringBuilder(255);
                GetShortPathName(inp, sb, sb.Capacity);
                inp = sb.ToString();
                if (op.IndexOf("\\") > -1)
                {
                    string[] subfolders = op.Split('\\');
                    string folderssofar = subfolders[0];
                    for (long i = 1; i < subfolders.Length - 1; i++)
                    {
                        folderssofar += "\\" + subfolders[i];
                        if (!Directory.Exists(folderssofar))
                            Directory.CreateDirectory(folderssofar);
                    }
                }

                // Remove the output if it already exists.
                if (File.Exists(op))
                    File.Delete(op);

                try
                {
                    // Prepare blank canvas.
                    IntPtr iconHDC = CreateDC("Display", null, null, IntPtr.Zero);
                    IntPtr iconHDCDest = CreateCompatibleDC(iconHDC);
                    Bitmap iconBMP = new Bitmap(256, 256, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(iconBMP))
                    {
                        g.Clear(Color.Transparent);
                    }

                    // Draw the image onto the canvas.
                    IntPtr iconHBitmap = iconBMP.GetHbitmap();
                    IntPtr iconHObj = SelectObject(iconHDCDest, iconHBitmap);
                    Guid guidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                    IImageList iImageList = null;
                    IntPtr hIml = IntPtr.Zero;
                    int ret = SHGetImageList(JUMBO_SIZE, ref guidImageList, ref iImageList);
                    int ret2 = SHGetImageListHandle(JUMBO_SIZE, ref guidImageList, ref hIml);
                    
                    if (ret != 0 || iImageList == null)
                    {
                        System.Diagnostics.Debug.WriteLine(String.Format("SHGetImageList failed with HRESULT: 0x{0:X8}", ret));
#if DEBUG
                        Console.WriteLine(String.Format("Warning: Failed to get system image list (HRESULT: 0x{0:X8})", ret));
#endif
                    }
                    
                    // Check if this is an Xbox Game Pass game
                    bool isXboxGame = originalPath.Contains("\\XboxGames\\") || originalPath.Contains("\\WindowsApps\\");
                    bool extractedFromPng = false;
                    
                    if (isXboxGame && originalPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
#if DEBUG
                        Console.WriteLine("Detected Xbox Game Pass game - looking for icon PNG files...");
#endif
                        
                        // Try to find icon in the Content folder
                        string contentDir = Path.GetDirectoryName(originalPath);
                        if (contentDir.EndsWith("\\Content", StringComparison.OrdinalIgnoreCase) || 
                            Directory.Exists(Path.Combine(contentDir, "Content")))
                        {
                            if (!contentDir.EndsWith("\\Content", StringComparison.OrdinalIgnoreCase))
                                contentDir = Path.Combine(contentDir, "Content");
                            
                            // Use custom size if specified, otherwise default to 256
                            int targetSize = customSize ?? 256;
                            
                            // Find the best Xbox icon based on target size and quality requirements
                            XboxIconCandidate bestIcon = FindBestXboxIcon(contentDir, targetSize, requireLarger, allowSplashFallback);
                            
                            if (bestIcon != null)
                            {
                                try
                                {
                                    // Check if this is a splash screen (marked with Priority = -1)
                                    bool isSplashScreen = bestIcon.Priority == -1;
                                    
                                    // If we need to resize, use appropriate method
                                    if (bestIcon.Size != targetSize || isSplashScreen)
                                    {
                                        // Use splash screen processing for splash screens
                                        if (isSplashScreen)
                                        {
                                            if (ProcessSplashScreenToIcon(bestIcon.FilePath, op, targetSize))
                                            {
                                                extractedFromPng = true;
#if DEBUG
                                                Console.WriteLine(String.Format("Successfully cropped splash screen to {0}x{0} icon", targetSize));
#endif
                                                Console.WriteLine(String.Format("Using image: {0} ({1}x{1}) [Splash Screen]", bestIcon.FileName, bestIcon.Size));
                                            }
                                            else
                                            {
                                                // Fallback to regular processing if splash crop fails
                                                if (ProcessIconWithImageMagick(bestIcon.FilePath, op, targetSize))
                                                {
                                                    extractedFromPng = true;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Use regular high-quality resizing for logos
                                            if (ProcessIconWithImageMagick(bestIcon.FilePath, op, targetSize))
                                            {
                                                extractedFromPng = true;
#if DEBUG
                                                Console.WriteLine(String.Format("Successfully extracted and resized Xbox game icon to {0}x{0} using ImageMagick", targetSize));
#endif
                                                Console.WriteLine(String.Format("Using image: {0} ({1}x{1})", bestIcon.FileName, bestIcon.Size));
                                            }
                                            else
                                            {
                                                // Fallback to GDI+ if ImageMagick fails
#if DEBUG
                                            Console.WriteLine("ImageMagick failed, falling back to GDI+");
#endif
                                            using (Bitmap iconBitmap = new Bitmap(bestIcon.FilePath))
                                            {
                                                using (Bitmap resizedIcon = new Bitmap(targetSize, targetSize, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                                                {
                                                    using (Graphics g = Graphics.FromImage(resizedIcon))
                                                    {
                                                        g.Clear(Color.Transparent);
                                                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                                                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                                                        
                                                        // Center the image if it's not square
                                                        float scale = Math.Min((float)targetSize / iconBitmap.Width, (float)targetSize / iconBitmap.Height);
                                                        int width = (int)(iconBitmap.Width * scale);
                                                        int height = (int)(iconBitmap.Height * scale);
                                                        int x = (targetSize - width) / 2;
                                                        int y = (targetSize - height) / 2;
                                                        
                                                        g.DrawImage(iconBitmap, x, y, width, height);
                                                    }
                                                    
                                                    resizedIcon.Save(op, System.Drawing.Imaging.ImageFormat.Png);
                                                    extractedFromPng = true;
                                                }
                                            }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // No resizing needed, just copy the file
                                        File.Copy(bestIcon.FilePath, op, true);
                                        extractedFromPng = true;
#if DEBUG
                                        Console.WriteLine(String.Format("Xbox game icon already at target size {0}x{0}, copied directly", targetSize));
#endif
                                        Console.WriteLine(String.Format("Using image: {0} ({1}x{1})", bestIcon.FileName, bestIcon.Size));
                                    }
                                }
                                catch (Exception ex)
                                {
#if DEBUG
                                    Console.WriteLine(String.Format("Failed to process {0}: {1}", bestIcon.FileName, ex.Message));
#endif
                                }
                            }
                        }
                    }
                    
                    // If we didn't extract from PNG, try the normal method
                    if (!extractedFromPng)
                    {
                        int iconIndex = IconIndex(originalPath, true);
    #if DEBUG
                    Console.WriteLine(String.Format("Icon index retrieved: {0}", iconIndex));
#endif
                        
                        if (isXboxGame && iconIndex != -1)
                        {
#if DEBUG
                            Console.WriteLine("Note: Xbox Game Pass app may show generic icon due to access restrictions");
#endif
                        }
                        
                        if (iconIndex == -1)
                    {
                        Console.WriteLine("Warning: Failed to retrieve icon information for: " + originalPath);
                        
                        // Get more detailed error info for console output
                        int lastError = Marshal.GetLastWin32Error();
                        string errorMsg = GetErrorMessage(lastError);
                        Console.WriteLine(String.Format("  Error: {0}", errorMsg));
                        
                        if (lastError == 5 || lastError == 1314) // Access denied or privilege not held
                        {
                            Console.WriteLine("  Hint: Try running as Administrator if accessing system files");
                        }
                        else if (!File.Exists(originalPath))
                        {
                            Console.WriteLine("  Hint: Check if the file path is correct");
                        }
                        
                        Console.WriteLine("  Attempting to continue with default icon...");
                        iconIndex = 0; // Use default icon index
                    }
                    
                    DrawImage(iImageList, iconHDCDest, iconIndex, 0, 0, ImageListDrawItemConstants.ILD_PRESERVEALPHA);
                    iconBMP.Dispose();

                    // Find the largest dimension of the copied bitmap.
                    int size = 256;
                    iconBMP = Bitmap.FromHbitmap(iconHBitmap);
                    if (CheckPixelRangeConsistency(ref iconBMP, 128, 128, 255, 255))
                    {
                        size = 128;
                        if (CheckPixelRangeConsistency(ref iconBMP, 64, 64, 127, 127))
                        {
                            size = 64;
                            if (CheckPixelRangeConsistency(ref iconBMP, 48, 48, 63, 63))
                            {
                                size = 48;
                                if (CheckPixelRangeConsistency(ref iconBMP, 32, 32, 47, 47))
                                {
                                    size = 32;
                                    if (CheckPixelRangeConsistency(ref iconBMP, 16, 16, 31, 31))
                                    {
                                        size = 16;
                                    }
                                }
                            }
                        }
                    }

                    // Resize the bitmap if needed.
                    if (size != 256)
                    {
                        iconBMP = iconBMP.Clone(new Rectangle(0, 0, size, size), iconBMP.PixelFormat);
                    }

                    // Save as a PNG without MakeTransparent() which can cause edge artifacts
                    iconBMP.Save(op, System.Drawing.Imaging.ImageFormat.Png);
                    iconBMP.Dispose();

                    // Clean up handles.
                    DeleteDC(iconHDCDest);
                    DeleteObject(iconHObj);
                    DeleteObject(iconHBitmap);
                    DeleteDC(iconHDC);
                    } // End of if (!extractedFromPng)

                    // If custom size specified and we haven't already processed with ImageMagick, resize now
                    if (customSize.HasValue && File.Exists(op) && !extractedFromPng)
                    {
                        // Use our high-quality processing method
                        string tempFile = op + ".tmp";
                        if (ProcessIconWithImageMagick(op, tempFile, customSize.Value))
                        {
                            File.Delete(op);
                            File.Move(tempFile, op);
                            Console.WriteLine("Success");
                        }
                        else
                        {
#if DEBUG
                            Console.WriteLine("Icon extracted at original size. Install ImageMagick to enable resizing.");
#endif
                            if (File.Exists(tempFile))
                                File.Delete(tempFile);
                            Console.WriteLine("Success");
                        }
                    }
                    else
                    {
                        // Print success for all other cases
                        Console.WriteLine("Success");
                    }

                    // Open the file if it was saved to temp location
                    if (openAfterExtract && File.Exists(op))
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = op,
                                UseShellExecute = true
                            });
                            Console.WriteLine(String.Format("Icon saved to: {0}", op));
                        }
                        catch (Exception openEx)
                        {
                            Console.WriteLine(String.Format("Warning: Could not open file: {0}", openEx.Message));
                            Console.WriteLine(String.Format("Icon saved to: {0}", op));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.ToString());
                }
            }
            else
            {
                Console.WriteLine("Syntax: extracticon.exe [input_filename] [output_image] [-size N] [-larger] [-splash]");
                Console.WriteLine("        extracticon.exe [input_filename] [-size N] [-larger] [-splash]");
                Console.WriteLine("  -size N: Optional icon size - must be a power of 2 (4, 8, 16, 32, 64, 128, or 256)");
                Console.WriteLine("  -larger: For 256px output, uses 1080px source instead of 150px (Xbox games only)");
                Console.WriteLine("  -splash: Allow using splash screen as fallback if no suitable logo found (Xbox games only)");
                Console.WriteLine("  If output_image is omitted, the icon will be saved to a temp file and opened");
            }
        }

        /// <summary>
        /// Find all available Xbox icon files and select the best one based on target size
        /// </summary>
        /// <param name="contentDir">The Content directory to search in</param>
        /// <param name="targetSize">The desired icon size</param>
        /// <param name="requireLarger">If true, only consider icons >= target size</param>
        /// <param name="allowSplashFallback">If true, allow using splash screen as fallback</param>
        /// <returns>The best icon candidate or null if none found</returns>
        static XboxIconCandidate FindBestXboxIcon(string contentDir, int targetSize, bool requireLarger, bool allowSplashFallback)
        {
            // Check cache first
            string cacheKey = contentDir.ToLowerInvariant();
            List<XboxIconCandidate> cachedCandidates;
            if (IconCache.TryGetValue(cacheKey, out cachedCandidates))
            {
#if DEBUG
                Console.WriteLine("Using cached Xbox icon candidates");
#endif
                return SelectOptimalIcon(cachedCandidates, targetSize, requireLarger, allowSplashFallback);
            }

            // Enumerate all PNG files in parallel
            var candidates = new List<XboxIconCandidate>();
            
            try
            {
                var iconFiles = Directory.EnumerateFiles(contentDir, "*.png", SearchOption.AllDirectories)
                    .Where(f => {
                        // Quick path filters
                        var relativePath = GetRelativePath(contentDir, f).ToLowerInvariant();
                        var fileName = Path.GetFileName(f);
                        
                        // Check if file matches our patterns
                        if (!IconFilePattern.IsMatch(fileName))
                            return false;
                        
                        // Prioritize certain directories
                        return relativePath.StartsWith("media\\logos\\") || 
                               relativePath.StartsWith("resources\\") ||
                               !relativePath.Contains("\\") || // Root directory
                               relativePath.Contains("_data\\streamingassets\\"); // Special case for some games
                    })
                    .AsParallel()
                    .WithDegreeOfParallelism(Environment.ProcessorCount)
                    .Select<string, XboxIconCandidate>(f => {
                        try
                        {
                            var candidate = new XboxIconCandidate
                            {
                                FilePath = f,
                                FileName = Path.GetFileName(f),
                                Priority = CalculatePriority(f),
                                IsSplash = Path.GetFileName(f).ToLowerInvariant().Contains("splash")
                            };
                            
                            // Try to extract size from filename first
                            candidate.Size = ExtractSizeFromFileName(candidate.FileName);
                            
                            // If no size in filename, read actual PNG dimensions
                            if (candidate.Size == 0)
                            {
                                candidate.Size = GetPngDimensions(f);
                            }
                            
                            return candidate;
                        }
                        catch
                        {
                            return null;
                        }
                    })
                    .Where(c => c != null && c.Size > 0)
                    .ToList();

                candidates.AddRange(iconFiles);

#if DEBUG
                Console.WriteLine(String.Format("Found {0} Xbox icon candidates:", candidates.Count));
                foreach (var c in candidates.OrderByDescending(c => c.Priority).ThenByDescending(c => c.Size))
                {
                    Console.WriteLine(String.Format("  {0} - Size: {1}x{1}, Priority: {2}", c.FileName, c.Size, c.Priority));
                }
#endif

                // Cache the results
                IconCache.TryAdd(cacheKey, candidates);
                
                if (candidates.Count == 0)
                    return null;
                
                return SelectOptimalIcon(candidates, targetSize, requireLarger, allowSplashFallback);
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine(String.Format("Error scanning for Xbox icons: {0}", ex.Message));
#endif
                return null;
            }
        }

        /// <summary>
        /// Get relative path (compatibility method for .NET Framework)
        /// </summary>
        static string GetRelativePath(string relativeTo, string path)
        {
            if (string.IsNullOrEmpty(relativeTo)) throw new ArgumentNullException("relativeTo");
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");

            Uri fromUri = new Uri(AppendDirectorySeparatorChar(relativeTo));
            Uri toUri = new Uri(path);

            if (fromUri.Scheme != toUri.Scheme) { return path; } // path can't be made relative.

            Uri relativeUri = fromUri.MakeRelativeUri(toUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            if (toUri.Scheme.Equals("file", StringComparison.InvariantCultureIgnoreCase))
            {
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }

            return relativePath;
        }
        
        static string AppendDirectorySeparatorChar(string path)
        {
            if (!Path.IsPathRooted(path))
                path = Path.GetFullPath(path);

            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
                path += Path.DirectorySeparatorChar;

            return path;
        }

        /// <summary>
        /// Extract size from filename using regex (does not read actual file)
        /// </summary>
        static int ExtractSizeFromFileName(string fileName)
        {
            var match = SizeExtractor.Match(fileName);
            if (match.Success)
            {
                int width, height;
                if (int.TryParse(match.Groups[1].Value, out width) &&
                    int.TryParse(match.Groups[2].Value, out height))
                {
                    // Return the smaller dimension (icons should be square)
                    return Math.Min(width, height);
                }
            }
            
            // No hardcoded assumptions - return 0 if size not in filename
            return 0; // Size unknown
        }
        
        /// <summary>
        /// Read actual dimensions from PNG file header
        /// </summary>
        static int GetPngDimensions(string filePath)
        {
            try
            {
                // PNG files have their dimensions at bytes 16-23
                // Width: bytes 16-19 (big-endian)
                // Height: bytes 20-23 (big-endian)
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] header = new byte[24];
                    if (stream.Read(header, 0, 24) < 24)
                        return 0;
                    
                    // Check PNG signature
                    if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47)
                        return 0; // Not a PNG file
                    
                    // Read width and height (big-endian)
                    int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                    int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                    
                    // Return the smaller dimension (for square icons)
                    return Math.Min(width, height);
                }
            }
            catch
            {
                return 0; // Return 0 if we can't read the file
            }
        }

        /// <summary>
        /// Calculate priority score for an icon based on path and filename
        /// </summary>
        static int CalculatePriority(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
            var relativePath = filePath.ToLowerInvariant();
            
            int score = 100;
            
            // Path scoring
            if (!relativePath.Contains("\\"))
                score += 20; // Root content directory
            else if (relativePath.Contains("media\\logos"))
                score += 15;
            else if (relativePath.Contains("resources"))
                score += 10;
            
            // Name scoring
            if (fileName == "logo")
                score += 25;
            else if (fileName.Contains("square") && fileName.Contains("logo"))
                score += 20;
            else if (fileName == "storelogo" || fileName.StartsWith("storelogo"))
                score += 15;
            else if (fileName.Contains("xbc_logo"))
                score += 12;
            else if (fileName == "smalllogo" || fileName == "largelogo")
                score += 10;
            else if (fileName.Contains("splash"))
                score -= 10; // Splash screens are less ideal
            
            return score;
        }

        /// <summary>
        /// Select the optimal icon from candidates based on target size and requirements
        /// </summary>
        static XboxIconCandidate SelectOptimalIcon(List<XboxIconCandidate> candidates, int targetSize, bool requireLarger, bool allowSplashFallback)
        {
            if (candidates.Count == 0)
                return null;
            
            // First, separate splash and non-splash candidates
            var nonSplashCandidates = candidates.Where(c => !c.IsSplash).ToList();
            var splashCandidates = candidates.Where(c => c.IsSplash).ToList();
            
            // Try to find suitable non-splash icons first
            if (nonSplashCandidates.Count > 0)
            {
                // Filter by size requirement if specified
                var eligibleCandidates = requireLarger && targetSize == 256
                    ? nonSplashCandidates.Where(c => c.Size >= targetSize).ToList()
                    : nonSplashCandidates;
                
                // If we have eligible candidates after filtering, use them
                if (eligibleCandidates.Count > 0)
                {
                    // Calculate match scores and select best
                    return eligibleCandidates
                        .Select(c => new {
                            Icon = c,
                            Score = CalculateMatchScore(c, targetSize)
                        })
                        .OrderByDescending(x => x.Score)
                        .First()
                        .Icon;
                }
                
                // If no candidates meet size requirement but splash fallback is NOT allowed
                // Use the best available non-splash icon regardless of size
                if (!allowSplashFallback)
                {
                    return nonSplashCandidates
                        .Select(c => new {
                            Icon = c,
                            Score = CalculateMatchScore(c, targetSize)
                        })
                        .OrderByDescending(x => x.Score)
                        .First()
                        .Icon;
                }
            }
            
            // If we reach here, either:
            // 1. No non-splash icons exist, OR
            // 2. No non-splash icons meet size requirements AND splash fallback is allowed
            if (allowSplashFallback && splashCandidates.Count > 0)
            {
#if DEBUG
                if (nonSplashCandidates.Count > 0)
                {
                    Console.WriteLine("No suitable logo found matching size requirements, using splash screen as fallback");
                }
                else
                {
                    Console.WriteLine("No logo icons found, using splash screen as fallback");
                }
#endif
                // Return the largest splash screen
                return splashCandidates.OrderByDescending(c => c.Size).First();
            }
            
            return null;
        }

        /// <summary>
        /// Calculate how well an icon matches the target size
        /// </summary>
        static double CalculateMatchScore(XboxIconCandidate icon, int targetSize)
        {
            // Size matching score (0-1, where 1 is perfect match)
            double sizeRatio = (double)icon.Size / targetSize;
            double sizeScore;
            
            if (sizeRatio == 1.0)
                sizeScore = 1.0; // Perfect match
            else if (sizeRatio > 1.0)
                sizeScore = 1.0 - (Math.Log(sizeRatio) / Math.Log(2) * 0.1); // Prefer downscaling (Log2 equivalent)
            else
                sizeScore = Math.Pow(sizeRatio, 2); // Penalize upscaling more
            
            // Quality preference (prefer larger source for better quality)
            double qualityScore = icon.Size >= targetSize ? 1.0 : 0.7;
            
            // Priority score normalized to 0-1
            double priorityScore = icon.Priority / 200.0;
            
            // Combined score with weights
            return (sizeScore * 0.5) + (qualityScore * 0.3) + (priorityScore * 0.2);
        }

        /// <summary>
        /// Process a splash screen by cropping to square and resizing
        /// </summary>
        /// <param name="inputPath">Path to splash screen image</param>
        /// <param name="outputPath">Path to output icon</param>
        /// <param name="targetSize">Target icon size</param>
        /// <returns>True if successful, false otherwise</returns>
        static bool ProcessSplashScreenToIcon(string inputPath, string outputPath, int targetSize)
        {
            // For splash screens, we need to:
            // 1. Get the minimum dimension (width or height)
            // 2. Crop to square using that dimension from center
            // 3. Resize to target size
            // Using ImageMagick's %[fx:min(w,h)] to get minimum dimension
            string magickArgs = String.Format("\"{0}\" " +
                "-gravity center " +               // Focus on center of image
                "-crop %[fx:min(w,h)]x%[fx:min(w,h)]+0+0 " +  // Crop to square using minimum dimension
                "+repage " +                       // Reset virtual canvas
                "-colorspace sRGB " +              // Proper color space handling
                "-filter Mitchell " +              // Mitchell filter for icons
                "-resize {1}x{1} " +
                "-unsharp 0x1+0.5+0 " +           // Subtle sharpening
                "-strip " +                        // Remove metadata
                "-colorspace sRGB " +              // Convert back to sRGB
                "\"{2}\"", inputPath, targetSize, outputPath);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "magick",
                Arguments = magickArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                    {
                        string error = p.StandardError.ReadToEnd();
#if DEBUG
                        Console.WriteLine(String.Format("ImageMagick splash crop error: {0}", error));
#endif
                        return false;
                    }
#if DEBUG
                    Console.WriteLine(String.Format("Successfully cropped splash screen to {0}x{0} icon", targetSize));
#endif
                    return true;
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine(String.Format("Could not process splash screen: {0}", ex.Message));
#endif
                return false;
            }
        }

        /// <summary>
        /// Process an icon using ImageMagick for high-quality resizing
        /// </summary>
        /// <param name="inputPath">Path to input image</param>
        /// <param name="outputPath">Path to output image</param>
        /// <param name="targetSize">Target size in pixels</param>
        /// <returns>True if successful, false otherwise</returns>
        static bool ProcessIconWithImageMagick(string inputPath, string outputPath, int targetSize)
        {
            // Use Mitchell filter for icons (better than Lanczos for this use case)
            // Add subtle sharpening to compensate for downscaling
            // Proper sRGB color space handling for gamma-aware resizing
            string magickArgs = String.Format("\"{0}\" " +
                "-colorspace sRGB " +              // Convert to linear color space
                "-filter Mitchell " +              // Mitchell filter - balanced for icons
                "-resize {1}x{1} " +
                "-unsharp 0x1+0.5+0 " +           // Subtle sharpening (radius=0, sigma=1, amount=0.5, threshold=0)
                "-strip " +                        // Remove metadata for smaller file
                "-colorspace sRGB " +              // Convert back to sRGB
                "\"{2}\"", inputPath, targetSize, outputPath);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "magick",
                Arguments = magickArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                    {
                        string error = p.StandardError.ReadToEnd();
#if DEBUG
                        Console.WriteLine(String.Format("ImageMagick error: {0}", error));
#endif
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine(String.Format("Could not run ImageMagick: {0}", ex.Message));
#endif
                return false;
            }
        }

        /// <summary>
        /// Get a human-readable error message for a Win32 error code
        /// </summary>
        /// <param name="errorCode">The Win32 error code</param>
        /// <returns>Error message string</returns>
        static string GetErrorMessage(int errorCode)
        {
            switch (errorCode)
            {
                case 2: return "ERROR_FILE_NOT_FOUND - The system cannot find the file specified";
                case 3: return "ERROR_PATH_NOT_FOUND - The system cannot find the path specified";
                case 5: return "ERROR_ACCESS_DENIED - Access is denied (may require elevated permissions)";
                case 32: return "ERROR_SHARING_VIOLATION - The file is being used by another process";
                case 87: return "ERROR_INVALID_PARAMETER - The parameter is incorrect";
                case 1223: return "ERROR_CANCELLED - The operation was cancelled by the user";
                case 1314: return "ERROR_PRIVILEGE_NOT_HELD - A required privilege is not held by the client";
                default: return "Unknown error (see https://docs.microsoft.com/en-us/windows/win32/debug/system-error-codes)";
            }
        }

        /// <summary>
        /// Check if a pixel range all has the same color.
        /// </summary>
        /// <param name="bmp">Bitmap to check.</param>
        /// <param name="startX">Starting X coordinate.</param>
        /// <param name="startY">Starting Y coordinate.</param>
        /// <param name="endX">Ending X coordinate.</param>
        /// <param name="endY">Ending Y coordinate.</param>
        /// <returns>True if all pixels in the range have the same value; otherwise, false.</returns>
        static int DetectIconSize(Bitmap bitmap)
        {
            // Check common icon sizes from largest to smallest
            int[] sizes = { 256, 128, 64, 48, 32, 16 };
            
            foreach (int size in sizes)
            {
                if (bitmap.Width >= size && bitmap.Height >= size)
                {
                    // Check if pixels outside this size are transparent/empty
                    bool hasContentOutside = false;
                    
                    // Check right edge
                    if (bitmap.Width > size)
                    {
                        for (int y = 0; y < Math.Min(size, bitmap.Height) && !hasContentOutside; y++)
                        {
                            Color pixel = bitmap.GetPixel(size, y);
                            if (pixel.A > 0) hasContentOutside = true;
                        }
                    }
                    
                    // Check bottom edge
                    if (bitmap.Height > size && !hasContentOutside)
                    {
                        for (int x = 0; x < Math.Min(size, bitmap.Width) && !hasContentOutside; x++)
                        {
                            Color pixel = bitmap.GetPixel(x, size);
                            if (pixel.A > 0) hasContentOutside = true;
                        }
                    }
                    
                    if (!hasContentOutside)
                        return size;
                }
            }
            
            // Default to actual size if no standard size detected
            return Math.Min(bitmap.Width, bitmap.Height);
        }

        static bool CheckPixelRangeConsistency(ref Bitmap bmp, int startX, int startY, int endX, int endY)
        {
            Color finalColor = bmp.GetPixel(endX, endY);
            for (int x = 0; x <= endX; x++)
            {
                for (int y = 0; y <= endY; y++)
                {
                    if (x >= startX || y >= endY)
                    {
                        if (bmp.GetPixel(x, y) != finalColor)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Draws an image to the specified context.
        /// </summary>
        /// <param name="hdc"></param>
        /// <param name="index"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="flags"></param>
        static void DrawImage(IImageList iImageList, IntPtr hdc, int index, int x, int y, ImageListDrawItemConstants flags)
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

        /// <summary>
        /// Returns the index of the icon for the specified file
        /// </summary>
        /// <param name="fileName">Filename to get icon for</param>
        /// <param name="forceLoadFromDisk">If True, then hit the disk to get the icon,
        /// otherwise only hit the disk if no cached icon is available.</param>
        /// <param name="iconState">Flags specifying the state of the icon
        /// returned.</param>
        /// <returns>Index of the icon, or -1 if failed</returns>
        static int IconIndex(string fileName, bool forceLoadFromDisk)
        {
            SHGetFileInfoConstants dwFlags = SHGetFileInfoConstants.SHGFI_SYSICONINDEX;
            int dwAttr = 0;
            SHFILEINFO shfi = new SHFILEINFO();
            uint shfiSize = (uint)Marshal.SizeOf(shfi.GetType());
            IntPtr retVal = SHGetFileInfo(fileName, dwAttr, ref shfi, shfiSize, ((uint)(dwFlags)));

            if (retVal.Equals(IntPtr.Zero))
            {
                // Get the last Win32 error to understand why it failed
                int errorCode = Marshal.GetLastWin32Error();
                string errorMessage = GetErrorMessage(errorCode);
                
                System.Diagnostics.Debug.WriteLine(String.Format("SHGetFileInfo failed for '{0}'", fileName));
                System.Diagnostics.Debug.WriteLine(String.Format("  Error code: {0} (0x{0:X})", errorCode));
                System.Diagnostics.Debug.WriteLine(String.Format("  Error message: {0}", errorMessage));
                System.Diagnostics.Debug.WriteLine(String.Format("  File exists: {0}", File.Exists(fileName)));
                if (File.Exists(fileName))
                {
                    try
                    {
                        var fileInfo = new FileInfo(fileName);
                        System.Diagnostics.Debug.WriteLine(String.Format("  File attributes: {0}", fileInfo.Attributes));
                        System.Diagnostics.Debug.WriteLine(String.Format("  File size: {0} bytes", fileInfo.Length));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(String.Format("  Could not get file info: {0}", ex.Message));
                    }
                }
                
                return -1; // Return -1 to indicate failure
            }
            else
            {
                return shfi.iIcon;
            }
        }
    }
}