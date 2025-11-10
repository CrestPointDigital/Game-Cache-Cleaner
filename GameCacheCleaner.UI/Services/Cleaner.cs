using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GameCacheCleaner.UI
{
    public static class Cleaner
    {
        public static async Task<(int files, int dirs)> SafeDeleteAsync(string root, List<string> excludes)
        {
            int files = 0, dirs = 0;
            if (!Directory.Exists(root)) return (files, dirs);

            var exset = excludes.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Make the deletion walk resilient: skip inaccessible folders and keep going.
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
            };

            foreach (var path in Directory.EnumerateFiles(root, "*", opts))
            {
                if (IsExcluded(path, exset)) continue;
                try { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); files++; }
                catch { }
                await Task.Yield();
            }
            var allDirs = Directory.EnumerateDirectories(root, "*", opts).OrderByDescending(d => d.Length);
            foreach (var d in allDirs)
            {
                if (IsExcluded(d, exset)) continue;
                try { if (!Directory.EnumerateFileSystemEntries(d).Any()) { Directory.Delete(d, false); dirs++; } }
                catch { }
                await Task.Yield();
            }
            return (files, dirs);
        }

        private static bool IsExcluded(string path, HashSet<string> exset)
        {
            foreach (var ex in exset)
                if (path.StartsWith(ex, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    public class Logger
    {
        private readonly string _file;
        public Logger(string file) { _file = file; }
        public void Write(string line)
        {
            try { File.AppendAllText(_file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}\n"); } catch { }
        }
    }
}
