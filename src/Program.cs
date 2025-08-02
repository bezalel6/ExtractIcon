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

                // Check for parameters
                int argIndex = 0;
                while (argIndex < args.Length)
                {
                    if (args[argIndex].ToLower() == "-size" && argIndex + 1 < args.Length)
                    {
                        if (int.TryParse(args[argIndex + 1], out int size))
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
                    Console.WriteLine("Syntax: extracticon.exe [input_filename] [output_image] [-size N] [-larger]");
                    Console.WriteLine("        extracticon.exe [input_filename] [-size N] [-larger]");
                    Console.WriteLine("  -size N: Optional icon size - must be a power of 2 (4, 8, 16, 32, 64, 128, or 256)");
                    Console.WriteLine("  -larger: For 256px output, uses 1080px source instead of 150px (Xbox games only)");
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
                        System.Diagnostics.Debug.WriteLine($"SHGetImageList failed with HRESULT: 0x{ret:X8}");
#if DEBUG
                        Console.WriteLine($"Warning: Failed to get system image list (HRESULT: 0x{ret:X8})");
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
                            XboxIconCandidate bestIcon = FindBestXboxIcon(contentDir, targetSize, requireLarger);
                            
                            if (bestIcon != null)
                            {
                                try
                                {
                                    // Load and resize the selected icon
                                    using (Bitmap iconBitmap = new Bitmap(bestIcon.FilePath))
                                    {
                                        // Create properly sized output
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
#if DEBUG
                                            Console.WriteLine($"Successfully extracted and resized Xbox game icon to {targetSize}x{targetSize}");
#endif
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
#if DEBUG
                                    Console.WriteLine($"Failed to process {bestIcon.FileName}: {ex.Message}");
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
                    Console.WriteLine($"Icon index retrieved: {iconIndex}");
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
                        Console.WriteLine($"  Error: {errorMsg}");
                        
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

                    // Save as a PNG.
                    iconBMP.MakeTransparent();
                    iconBMP.Save(op, System.Drawing.Imaging.ImageFormat.Png);
                    iconBMP.Dispose();

                    // Clean up handles.
                    DeleteDC(iconHDCDest);
                    DeleteObject(iconHObj);
                    DeleteObject(iconHBitmap);
                    DeleteDC(iconHDC);
                    } // End of if (!extractedFromPng)

                    // If custom size specified, use ImageMagick to resize
                    if (customSize.HasValue && File.Exists(op))
                    {
                        // Use Lanczos filter for sharp downscaling to small icon sizes
                        // -background none preserves transparency
                        // -gravity center ensures centered scaling
                        // -extent ensures exact dimensions if aspect ratio differs
                        string magickArgs = $"convert \"{op}\" -background none -gravity center -filter Lanczos -resize {customSize.Value}x{customSize.Value} -extent {customSize.Value}x{customSize.Value} \"{op}\"";
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
                                    Console.WriteLine($"Warning: ImageMagick resize failed: {error}");
                                    Console.WriteLine("Icon extracted at original size.");
#endif
                                }
                                else
                                {
                                    Console.WriteLine("Success");
                                }
                            }
                        }
                        catch (Exception magickEx)
                        {
#if DEBUG
                            Console.WriteLine($"Warning: Could not run ImageMagick: {magickEx.Message}");
                            Console.WriteLine("Icon extracted at original size. Install ImageMagick to enable resizing.");
#endif
                        }
                    }
                    else if (!extractedFromPng)
                    {
                        // Only print success if we haven't already extracted from PNG
                        Console.WriteLine("Success");
                    }
                    
                    // If we extracted from Xbox PNG, print success
                    if (extractedFromPng && !customSize.HasValue)
                    {
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
                            Console.WriteLine($"Icon saved to: {op}");
                        }
                        catch (Exception openEx)
                        {
                            Console.WriteLine($"Warning: Could not open file: {openEx.Message}");
                            Console.WriteLine($"Icon saved to: {op}");
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
                Console.WriteLine("Syntax: extracticon.exe [input_filename] [output_image] [-size N] [-larger]");
                Console.WriteLine("        extracticon.exe [input_filename] [-size N] [-larger]");
                Console.WriteLine("  -size N: Optional icon size - must be a power of 2 (4, 8, 16, 32, 64, 128, or 256)");
                Console.WriteLine("  -larger: For 256px output, uses 1080px source instead of 150px (Xbox games only)");
                Console.WriteLine("  If output_image is omitted, the icon will be saved to a temp file and opened");
            }
        }

        /// <summary>
        /// Find all available Xbox icon files and select the best one based on target size
        /// </summary>
        /// <param name="contentDir">The Content directory to search in</param>
        /// <param name="targetSize">The desired icon size</param>
        /// <param name="requireLarger">If true, only consider icons >= target size</param>
        /// <returns>The best icon candidate or null if none found</returns>
        static XboxIconCandidate FindBestXboxIcon(string contentDir, int targetSize, bool requireLarger)
        {
            // Check cache first
            string cacheKey = contentDir.ToLowerInvariant();
            if (IconCache.TryGetValue(cacheKey, out var cachedCandidates))
            {
#if DEBUG
                Console.WriteLine("Using cached Xbox icon candidates");
#endif
                return SelectOptimalIcon(cachedCandidates, targetSize, requireLarger);
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
                                Priority = CalculatePriority(f)
                            };
                            
                            // Try to extract size from filename first
                            candidate.Size = ExtractSizeFromFileName(candidate.FileName);
                            
                            // If no size in filename, detect from image
                            if (candidate.Size == 0)
                            {
                                using (var img = new Bitmap(f))
                                {
                                    candidate.Size = DetectIconSize(img);
                                }
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
                Console.WriteLine($"Found {candidates.Count} Xbox icon candidates:");
                foreach (var c in candidates.OrderByDescending(c => c.Priority).ThenByDescending(c => c.Size))
                {
                    Console.WriteLine($"  {c.FileName} - Size: {c.Size}x{c.Size}, Priority: {c.Priority}");
                }
#endif

                // Cache the results
                IconCache.TryAdd(cacheKey, candidates);
                
                if (candidates.Count == 0)
                    return null;
                
                return SelectOptimalIcon(candidates, targetSize, requireLarger);
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"Error scanning for Xbox icons: {ex.Message}");
#endif
                return null;
            }
        }

        /// <summary>
        /// Get relative path (compatibility method for .NET Framework)
        /// </summary>
        static string GetRelativePath(string relativeTo, string path)
        {
            if (string.IsNullOrEmpty(relativeTo)) throw new ArgumentNullException(nameof(relativeTo));
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

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
        /// Extract size from filename using regex
        /// </summary>
        static int ExtractSizeFromFileName(string fileName)
        {
            var match = SizeExtractor.Match(fileName);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int width) &&
                    int.TryParse(match.Groups[2].Value, out int height))
                {
                    // Return the smaller dimension (icons should be square)
                    return Math.Min(width, height);
                }
            }
            
            // Special cases for known sizes
            var lowerName = fileName.ToLowerInvariant();
            if (lowerName.Contains("storelogo") && !lowerName.Contains("x"))
                return 1080; // Default StoreLogo.png is usually 1080x1080
            
            return 0; // Size unknown
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
        static XboxIconCandidate SelectOptimalIcon(List<XboxIconCandidate> candidates, int targetSize, bool requireLarger)
        {
            if (candidates.Count == 0)
                return null;
            
            // Filter by size requirement if specified
            var eligibleCandidates = requireLarger && targetSize == 256
                ? candidates.Where(c => c.Size >= targetSize).ToList()
                : candidates;
            
            // If no candidates meet size requirement, use all
            if (eligibleCandidates.Count == 0)
                eligibleCandidates = candidates;
            
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
                default: return $"Unknown error (see https://docs.microsoft.com/en-us/windows/win32/debug/system-error-codes)";
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
                
                System.Diagnostics.Debug.WriteLine($"SHGetFileInfo failed for '{fileName}'");
                System.Diagnostics.Debug.WriteLine($"  Error code: {errorCode} (0x{errorCode:X})");
                System.Diagnostics.Debug.WriteLine($"  Error message: {errorMessage}");
                System.Diagnostics.Debug.WriteLine($"  File exists: {File.Exists(fileName)}");
                if (File.Exists(fileName))
                {
                    try
                    {
                        var fileInfo = new FileInfo(fileName);
                        System.Diagnostics.Debug.WriteLine($"  File attributes: {fileInfo.Attributes}");
                        System.Diagnostics.Debug.WriteLine($"  File size: {fileInfo.Length} bytes");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"  Could not get file info: {ex.Message}");
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