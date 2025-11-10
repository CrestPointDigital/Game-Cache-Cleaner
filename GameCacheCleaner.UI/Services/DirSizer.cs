using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GameCacheCleaner.UI
{
    public static class DirSizer
    {
        public static async Task<(long size, long files)> SizeOfAsync(string path, List<string> excludes)
        {
            long total = 0; long files = 0;
            try
            {
                var exset = excludes.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!Directory.Exists(path)) return (0, 0);

                // Use EnumerationOptions to avoid aborting the entire walk when a single
                // subfolder is inaccessible or disappears during enumeration.
                var opts = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false,
                };

                foreach (var f in Directory.EnumerateFiles(path, "*", opts))
                {
                    if (IsExcluded(f, exset)) continue;
                    try { var fi = new FileInfo(f); total += fi.Length; files++; } catch { }
                    await Task.Yield();
                }
            }
            catch { }
            return (total, files);
        }

        private static bool IsExcluded(string p, HashSet<string> exset)
        {
            foreach (var ex in exset) if (p.StartsWith(ex, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
