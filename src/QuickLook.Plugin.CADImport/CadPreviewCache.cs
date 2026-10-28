using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;

namespace QuickLook.Plugin.CADImport;

internal static class CadPreviewCache
{
    private const long MaximumCacheSize = 128 * 1024 * 1024;
    private const string CacheVersion = "1080p-bg292929";

    public static bool ShouldCache(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Unable to inspect CAD preview cache source: {ex}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Unable to inspect CAD preview cache source: {ex}");
            return false;
        }
    }

    public static string GetCachePath(string sourcePath, bool isDark)
    {
        var source = new FileInfo(sourcePath);
        string fullPath = source.FullName.ToUpperInvariant();
        string pathHash = Hash(fullPath);
        string theme = isDark ? "dark" : "light";
        string filename = $"{pathHash}_{source.Length}_{source.LastWriteTimeUtc.Ticks}_{theme}_{CacheVersion}.png";
        return Path.Combine(GetCacheDirectory(), filename);
    }

    public static Bitmap TryLoad(string sourcePath, bool isDark)
    {
        string cachePath;
        try
        {
            cachePath = GetCachePath(sourcePath, isDark);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Unable to locate cached CAD preview: {ex}");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Unable to locate cached CAD preview: {ex}");
            return null;
        }

        if (!File.Exists(cachePath))
            return null;

        try
        {
            using (var stream = File.OpenRead(cachePath))
            using (var image = Image.FromStream(stream, true, true))
                return new Bitmap(image);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Unable to read cached CAD preview: {ex}");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Unable to read cached CAD preview: {ex}");
            return null;
        }
        catch (ArgumentException ex)
        {
            Debug.WriteLine($"Cached CAD preview is invalid: {ex}");
            return null;
        }
        catch (ExternalException ex)
        {
            Debug.WriteLine($"Unable to decode cached CAD preview: {ex}");
            return null;
        }
    }

    public static void Store(string sourcePath, bool isDark, string expectedCachePath, Bitmap preview)
    {
        string cachePath = GetCachePath(sourcePath, isDark);
        if (!string.Equals(cachePath, expectedCachePath, StringComparison.OrdinalIgnoreCase) ||
            File.Exists(cachePath))
        {
            return;
        }

        string cacheDirectory = GetCacheDirectory();
        Directory.CreateDirectory(cacheDirectory);
        string temporaryPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            preview.Save(temporaryPath, ImageFormat.Png);
            try
            {
                File.Move(temporaryPath, cachePath);
            }
            catch (IOException) when (File.Exists(cachePath))
            {
            }

            RemoveStaleEntries(cachePath);
            EnforceCacheLimit(cachePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException ex)
                {
                    Debug.WriteLine($"Unable to remove temporary CAD preview cache file: {ex}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    Debug.WriteLine($"Unable to remove temporary CAD preview cache file: {ex}");
                }
            }
        }
    }

    private static void RemoveStaleEntries(string cachePath)
    {
        string directory = Path.GetDirectoryName(cachePath);
        string prefix = Path.GetFileName(cachePath).Split('_')[0] + "_";
        foreach (string stalePath in Directory.GetFiles(directory, prefix + "*.png"))
        {
            if (string.Equals(stalePath, cachePath, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                File.Delete(stalePath);
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"Unable to remove stale CAD preview cache entry: {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"Unable to remove stale CAD preview cache entry: {ex}");
            }
        }
    }

    private static void EnforceCacheLimit(string keepPath)
    {
        string directory = Path.GetDirectoryName(keepPath);
        var entries = Directory.GetFiles(directory, "*.png")
            .Select(path => new FileInfo(path))
            .OrderBy(info => info.LastWriteTimeUtc)
            .ToArray();
        long totalSize = entries.Sum(info => info.Length);
        foreach (FileInfo entry in entries)
        {
            if (totalSize <= MaximumCacheSize)
                break;
            if (string.Equals(entry.FullName, keepPath, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                totalSize -= entry.Length;
                entry.Delete();
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"Unable to trim CAD preview cache: {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"Unable to trim CAD preview cache: {ex}");
            }
        }
    }

    private static string GetCacheDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickLook",
            "CADPreviewCache");
    }

    private static string Hash(string value)
    {
        using (SHA256 algorithm = SHA256.Create())
        {
            byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash, 0, 12).Replace("-", string.Empty);
        }
    }
}
