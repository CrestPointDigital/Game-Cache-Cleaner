using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using System.Linq;

namespace GameCacheCleaner.UI
{
    public record CacheRoot(string Name, string Path)
    {
        public static List<CacheRoot> KnownRoots(int scanDepth = 1)
        {
            string LA = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string LocalLow = System.IO.Path.Combine(UserProfile, "AppData", "LocalLow");
            string CA = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string PF = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string PFx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string Temp = System.IO.Path.GetTempPath();

            // Base defaults (used if no better detection is found)
            var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DirectX ShaderCache"] = System.IO.Path.Combine(LA, "D3DCache"),
                ["NVIDIA DXCache"] = System.IO.Path.Combine(LA, "NVIDIA", "DXCache"),
                ["NVIDIA GLCache"] = System.IO.Path.Combine(LA, "NVIDIA", "GLCache"),
                ["NVIDIA NV_Cache"] = System.IO.Path.Combine(CA, "NVIDIA Corporation", "NV_Cache"),
                ["NVIDIA ComputeCache"] = System.IO.Path.Combine(CA, "NVIDIA Corporation", "ComputeCache"),
                ["AMD GLCache"] = System.IO.Path.Combine(LA, "AMD", "GLCache"),
                ["UE DerivedDataCache"] = System.IO.Path.Combine(LA, "UnrealEngine", "Common", "DerivedDataCache"),
                ["Unity GI/Cache"] = System.IO.Path.Combine(LA, "Unity", "cache"),
                ["Steam ShaderCache"] = System.IO.Path.Combine(LA, "Steam", "shadercache"),
                // Default here was wrong for many setups; override below via detection.
                ["Steam Download Temp"] = System.IO.Path.Combine(PFx86, "Steam", "steamapps", "downloading"),
                ["Epic DerivedDataCache"] = System.IO.Path.Combine(LA, "EpicGamesLauncher", "Saved", "DerivedDataCache"),
                ["Epic Download Cache"] = System.IO.Path.Combine(LA, "EpicGamesLauncher", "Saved", "Paks"),
                ["GOG Temp"] = System.IO.Path.Combine(LA, "GOG.com", "Galaxy", "webcache"),
                ["Battle.net Cache"] = System.IO.Path.Combine(LA, "Battle.net", "BrowserCache"),
                // Xbox (Microsoft Store) app caches under UWP Packages
                ["Xbox App Cache"] = System.IO.Path.Combine(LA, "Packages", "Microsoft.GamingApp_8wekyb3d8bbwe", "LocalCache"),
                ["Xbox App Temp"] = System.IO.Path.Combine(LA, "Packages", "Microsoft.GamingApp_8wekyb3d8bbwe", "TempState"),
                ["Xbox Identity Cache"] = System.IO.Path.Combine(LA, "Packages", "Microsoft.XboxIdentityProvider_8wekyb3d8bbwe", "LocalCache"),
                ["Xbox Overlay Cache"] = System.IO.Path.Combine(LA, "Packages", "Microsoft.XboxGamingOverlay_8wekyb3d8bbwe", "LocalCache"),
                ["Windows Temp"] = Temp,
            };

            // Prefer modern Windows 10/11 location for DirectX Shader Cache when present
            var dxModern = System.IO.Path.Combine(LA, "Microsoft", "DirectX Shader Cache");
            if (Directory.Exists(dxModern))
                roots["DirectX ShaderCache"] = dxModern;

            // Steam detection: registry + libraryfolders.vdf across drives
            foreach (var lib in DetectSteamLibraries(PF, PFx86))
            {
                var shader = System.IO.Path.Combine(lib, "shadercache");
                var downloading = System.IO.Path.Combine(lib, "downloading");
                // Prefer detected paths if they exist
                if (Directory.Exists(shader))
                    roots["Steam ShaderCache"] = shader;
                if (Directory.Exists(downloading))
                    roots["Steam Download Temp"] = downloading;
            }

            // Fallback: scan fixed drives for common Steam library patterns
            foreach (var d in DriveInfo.GetDrives().Where(dr => dr.IsReady && dr.DriveType == DriveType.Fixed))
            {
                var root = d.RootDirectory.FullName;
                var steamLibrary = System.IO.Path.Combine(root, "SteamLibrary", "steamapps");
                if (Directory.Exists(steamLibrary))
                {
                    var shader = System.IO.Path.Combine(steamLibrary, "shadercache");
                    var downloading = System.IO.Path.Combine(steamLibrary, "downloading");
                    if (Directory.Exists(shader)) roots["Steam ShaderCache"] = shader;
                    if (Directory.Exists(downloading)) roots["Steam Download Temp"] = downloading;
                }
            }

            // Epic ProgramData cache (commonly used for downloads)
            var epicDataCache = System.IO.Path.Combine(CA, "Epic", "EpicGamesLauncher", "DataCache");
            if (Directory.Exists(epicDataCache))
                roots["Epic Download Cache"] = epicDataCache;
            // Fallbacks seen on some machines/installers
            var epicData = System.IO.Path.Combine(CA, "Epic", "EpicGamesLauncher", "Data");
            if (Directory.Exists(epicData))
                roots["Epic Download Cache"] = epicData;
            var epicLegacy = System.IO.Path.Combine(CA, "Epic", "UnrealEngineLauncher");
            if (Directory.Exists(epicLegacy))
                roots["Epic Download Cache"] = epicLegacy;

            // NVIDIA ProgramData NV_Cache & ComputeCache (present on many systems)
            var nvProgramDataCache = System.IO.Path.Combine(CA, "NVIDIA Corporation", "NV_Cache");
            if (Directory.Exists(nvProgramDataCache))
                roots["NVIDIA NV_Cache"] = nvProgramDataCache;
            var nvComputeCache = System.IO.Path.Combine(CA, "NVIDIA Corporation", "ComputeCache");
            if (Directory.Exists(nvComputeCache))
                roots["NVIDIA ComputeCache"] = nvComputeCache;

            // Battle.net Agent cache under ProgramData (more impactful than BrowserCache)
            var bnetAgentCache = System.IO.Path.Combine(CA, "Battle.net", "Agent", "cache");
            if (Directory.Exists(bnetAgentCache))
                roots["Battle.net Cache"] = bnetAgentCache;

            // GOG Galaxy ProgramData webcache (when Galaxy is installed)
            var gogWebcachePd = System.IO.Path.Combine(CA, "GOG.com", "Galaxy", "webcache");
            if (Directory.Exists(gogWebcachePd))
                roots["GOG Temp"] = gogWebcachePd;

            // EA App caches (user cache and installer cache)
            var eaUserCache = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EA Desktop", "cache");
            if (Directory.Exists(eaUserCache))
                roots["EA App Cache"] = eaUserCache;
            var eaInstallerCache = System.IO.Path.Combine(CA, "EA Desktop", "InstallerCache");
            if (Directory.Exists(eaInstallerCache))
                roots["EA App InstallerCache"] = eaInstallerCache;

            // Ubisoft Connect cache
            var ubiCache = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft", "Ubisoft Game Launcher", "cache");
            if (Directory.Exists(ubiCache))
                roots["Ubisoft Connect Cache"] = ubiCache;

            // Rockstar Games Launcher caches
            var rockstarLauncherCache = System.IO.Path.Combine(LA, "Rockstar Games", "Launcher", "Cache");
            if (Directory.Exists(rockstarLauncherCache))
                roots["Rockstar Launcher Cache"] = rockstarLauncherCache;
            var rockstarSocialClubCache = System.IO.Path.Combine(LA, "Rockstar Games", "Social Club", "Cache");
            if (Directory.Exists(rockstarSocialClubCache))
                roots["Rockstar Social Club Cache"] = rockstarSocialClubCache;

            // Amazon Games launcher data/cache
            var amazonData = System.IO.Path.Combine(LA, "Amazon Games", "Data");
            if (Directory.Exists(amazonData))
                roots["Amazon Games Data"] = amazonData;
            var amazonCache = System.IO.Path.Combine(LA, "Amazon Games", "Cache");
            if (Directory.Exists(amazonCache))
                roots["Amazon Games Cache"] = amazonCache;

            // Itch.io caches
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var itchRoot = System.IO.Path.Combine(roaming, "itch");
            var itchCache = System.IO.Path.Combine(itchRoot, "cache");
            if (Directory.Exists(itchCache))
                roots["Itch Cache"] = itchCache;
            var itchDownloads = System.IO.Path.Combine(itchRoot, "downloads");
            if (Directory.Exists(itchDownloads))
                roots["Itch Downloads"] = itchDownloads;

            // UE DerivedDataCache variations
            var ueCommon = System.IO.Path.Combine(LA, "UnrealEngine", "Common", "DerivedDataCache");
            var ueLocal = System.IO.Path.Combine(LA, "UnrealEngine", "DerivedDataCache");
            var ueProgramData = System.IO.Path.Combine(CA, "UnrealEngine", "DerivedDataCache");
            if (Directory.Exists(ueLocal)) roots["UE DerivedDataCache"] = ueLocal;
            else if (Directory.Exists(ueCommon)) roots["UE DerivedDataCache"] = ueCommon; // keep default if exists
            else if (Directory.Exists(ueProgramData)) roots["UE DerivedDataCache"] = ueProgramData;

            // Unity caches can also live under LocalLow for player/runtime
            var unityLocalLow = System.IO.Path.Combine(LocalLow, "Unity", "Cache");
            if (Directory.Exists(unityLocalLow))
                roots["Unity GI/Cache"] = unityLocalLow;

            // Universal scan for Epic, NVIDIA, Battle.net, GOG across fixed drives
            foreach (var d in DriveInfo.GetDrives().Where(dr => dr.IsReady && dr.DriveType == DriveType.Fixed))
            {
                var root = d.RootDirectory.FullName;
                // Epic download cache patterns
                TryOverrideIfExists(roots, "Epic Download Cache", System.IO.Path.Combine(root, "Epic", "EpicGamesLauncher", "DataCache"));
                TryOverrideIfExists(roots, "Epic Download Cache", System.IO.Path.Combine(root, "Epic", "EpicGamesLauncher", "Data"));
                foreach (var dir in EnumerateLevels(root, Math.Max(1, scanDepth)))
                {
                    TryOverrideIfExists(roots, "Epic Download Cache", System.IO.Path.Combine(dir, "Epic", "EpicGamesLauncher", "DataCache"));
                    TryOverrideIfExists(roots, "Epic Download Cache", System.IO.Path.Combine(dir, "Epic", "EpicGamesLauncher", "Data"));
                    TryOverrideIfExists(roots, "Epic DerivedDataCache", System.IO.Path.Combine(dir, "EpicGamesLauncher", "Saved", "DerivedDataCache"));
                }

                // NVIDIA caches
                TryOverrideIfExists(roots, "NVIDIA DXCache", System.IO.Path.Combine(root, "NVIDIA", "DXCache"));
                TryOverrideIfExists(roots, "NVIDIA GLCache", System.IO.Path.Combine(root, "NVIDIA", "GLCache"));
                TryOverrideIfExists(roots, "NVIDIA NV_Cache", System.IO.Path.Combine(root, "NVIDIA Corporation", "NV_Cache"));
                TryOverrideIfExists(roots, "NVIDIA ComputeCache", System.IO.Path.Combine(root, "NVIDIA Corporation", "ComputeCache"));
                foreach (var dir in EnumerateLevels(root, Math.Max(1, scanDepth)))
                {
                    TryOverrideIfExists(roots, "NVIDIA DXCache", System.IO.Path.Combine(dir, "NVIDIA", "DXCache"));
                    TryOverrideIfExists(roots, "NVIDIA GLCache", System.IO.Path.Combine(dir, "NVIDIA", "GLCache"));
                    TryOverrideIfExists(roots, "NVIDIA NV_Cache", System.IO.Path.Combine(dir, "NVIDIA Corporation", "NV_Cache"));
                    TryOverrideIfExists(roots, "NVIDIA ComputeCache", System.IO.Path.Combine(dir, "NVIDIA Corporation", "ComputeCache"));
                }

                // Battle.net Agent cache patterns
                TryOverrideIfExists(roots, "Battle.net Cache", System.IO.Path.Combine(root, "ProgramData", "Battle.net", "Agent", "cache"));
                foreach (var dir in EnumerateLevels(root, Math.Max(1, scanDepth)))
                {
                    TryOverrideIfExists(roots, "Battle.net Cache", System.IO.Path.Combine(dir, "Battle.net", "Agent", "cache"));
                    TryOverrideIfExists(roots, "Battle.net Cache", System.IO.Path.Combine(dir, "ProgramData", "Battle.net", "Agent", "cache"));
                }

                // GOG Galaxy webcache patterns
                TryOverrideIfExists(roots, "GOG Temp", System.IO.Path.Combine(root, "ProgramData", "GOG.com", "Galaxy", "webcache"));
                foreach (var dir in EnumerateLevels(root, Math.Max(1, scanDepth)))
                {
                    TryOverrideIfExists(roots, "GOG Temp", System.IO.Path.Combine(dir, "GOG.com", "Galaxy", "webcache"));
                    TryOverrideIfExists(roots, "GOG Temp", System.IO.Path.Combine(dir, "ProgramData", "GOG.com", "Galaxy", "webcache"));
                }

                // EA App patterns
                TryOverrideIfExists(roots, "EA App Cache", System.IO.Path.Combine(root, "Users", Environment.UserName, "AppData", "Roaming", "EA Desktop", "cache"));
                TryOverrideIfExists(roots, "EA App InstallerCache", System.IO.Path.Combine(root, "ProgramData", "EA Desktop", "InstallerCache"));
                foreach (var dir in EnumerateLevels(root, Math.Max(1, scanDepth)))
                {
                    TryOverrideIfExists(roots, "EA App InstallerCache", System.IO.Path.Combine(dir, "EA Desktop", "InstallerCache"));
                }

                // Ubisoft Connect patterns
                TryOverrideIfExists(roots, "Ubisoft Connect Cache", System.IO.Path.Combine(root, "Program Files (x86)", "Ubisoft", "Ubisoft Game Launcher", "cache"));
                foreach (var dir in EnumerateLevels(root, Math.Max(1, scanDepth)))
                {
                    TryOverrideIfExists(roots, "Ubisoft Connect Cache", System.IO.Path.Combine(dir, "Ubisoft", "Ubisoft Game Launcher", "cache"));
                    TryOverrideIfExists(roots, "Ubisoft Connect Cache", System.IO.Path.Combine(dir, "Program Files (x86)", "Ubisoft", "Ubisoft Game Launcher", "cache"));
                }

                // Rockstar Games Launcher patterns
                TryOverrideIfExists(roots, "Rockstar Launcher Cache", System.IO.Path.Combine(root, "Users", Environment.UserName, "AppData", "Local", "Rockstar Games", "Launcher", "Cache"));
                TryOverrideIfExists(roots, "Rockstar Social Club Cache", System.IO.Path.Combine(root, "Users", Environment.UserName, "AppData", "Local", "Rockstar Games", "Social Club", "Cache"));
                foreach (var dir in EnumerateLevels(root, Math.Max(1, scanDepth)))
                {
                    TryOverrideIfExists(roots, "Rockstar Launcher Cache", System.IO.Path.Combine(dir, "Rockstar Games", "Launcher", "Cache"));
                    TryOverrideIfExists(roots, "Rockstar Social Club Cache", System.IO.Path.Combine(dir, "Rockstar Games", "Social Club", "Cache"));
                }

                // Amazon Games patterns (App/Data under moved installs)
                TryOverrideIfExists(roots, "Amazon Games Data", System.IO.Path.Combine(root, "Amazon Games", "Data"));
                TryOverrideIfExists(roots, "Amazon Games Cache", System.IO.Path.Combine(root, "Amazon Games", "Cache"));
                foreach (var dir in EnumerateLevels(root, Math.Max(1, scanDepth)))
                {
                    TryOverrideIfExists(roots, "Amazon Games Data", System.IO.Path.Combine(dir, "Amazon Games", "Data"));
                    TryOverrideIfExists(roots, "Amazon Games Cache", System.IO.Path.Combine(dir, "Amazon Games", "Cache"));
                }

                // Itch.io patterns
                TryOverrideIfExists(roots, "Itch Downloads", System.IO.Path.Combine(root, "Users", Environment.UserName, "AppData", "Roaming", "itch", "downloads"));
                TryOverrideIfExists(roots, "Itch Cache", System.IO.Path.Combine(root, "Users", Environment.UserName, "AppData", "Roaming", "itch", "cache"));
                foreach (var dir in EnumerateLevels(root, Math.Max(1, scanDepth)))
                {
                    TryOverrideIfExists(roots, "Itch Downloads", System.IO.Path.Combine(dir, "itch", "downloads"));
                    TryOverrideIfExists(roots, "Itch Cache", System.IO.Path.Combine(dir, "itch", "cache"));
                }
            }

            // Build final list
            var list = new List<CacheRoot>();
            foreach (var kv in roots)
            {
                list.Add(new CacheRoot(kv.Key, kv.Value));
            }
            return list;
        }

        private static IEnumerable<string> DetectSteamLibraries(string PF, string PFx86)
        {
            var libs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Candidate install roots
            var candidates = new List<string>();
            var regSteam = TryGetRegistryStringMultiHive(new[]{@"Software\Valve\Steam", @"SOFTWARE\WOW6432Node\Valve\Steam"}, "SteamPath");
            if (!string.IsNullOrWhiteSpace(regSteam)) candidates.Add(regSteam);
            // Common installs
            candidates.Add(System.IO.Path.Combine(PFx86 ?? string.Empty, "Steam"));
            candidates.Add(System.IO.Path.Combine(PF ?? string.Empty, "Steam"));

            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                var steamapps = System.IO.Path.Combine(c, "steamapps");
                if (Directory.Exists(steamapps)) libs.Add(steamapps);

                // Parse libraryfolders.vdf to discover additional libraries
                var vdf = System.IO.Path.Combine(steamapps, "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    try
                    {
                        foreach (var line in File.ReadAllLines(vdf))
                        {
                            // crude parse: look for lines like: "path" "D:\\SteamLibrary"
                            var idx = line.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase);
                            if (idx >= 0)
                            {
                                var parts = line.Split('"');
                                // parts: ["", path, "", value, ""]
                                foreach (var p in parts)
                                {
                                    if (p.Contains(":\\") || p.StartsWith("\\\\"))
                                    {
                                        var libRoot = p.Trim();
                                        if (!string.IsNullOrWhiteSpace(libRoot))
                                        {
                                            var sa = System.IO.Path.Combine(libRoot, "steamapps");
                                            if (Directory.Exists(sa)) libs.Add(sa);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { /* ignore parse errors */ }
                }
            }

            // Scan all fixed drives for common Steam library layouts (without deep recursion)
            foreach (var d in DriveInfo.GetDrives().Where(dr => dr.IsReady && dr.DriveType == DriveType.Fixed))
            {
                var root = d.RootDirectory.FullName;
                // Direct root conventions
                TryAddSteamApps(System.IO.Path.Combine(root, "SteamLibrary", "steamapps"), libs);
                TryAddSteamApps(System.IO.Path.Combine(root, "Steam", "steamapps"), libs);

                // One-level deep: look at top-level folders only
                foreach (var top in SafeEnumerateDirectories(root))
                {
                    TryAddSteamApps(System.IO.Path.Combine(top, "SteamLibrary", "steamapps"), libs);
                    TryAddSteamApps(System.IO.Path.Combine(top, "Steam", "steamapps"), libs);
                }
            }

            return libs;
        }

        private static string? TryGetRegistryStringMultiHive(string[] subKeys, string valueName)
        {
            try
            {
                foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    foreach (var subKey in subKeys)
                    {
                        using var key = hive.OpenSubKey(subKey);
                        var val = key?.GetValue(valueName) as string;
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                }
            }
            catch { }
            return null;
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string path)
        {
            try { return Directory.EnumerateDirectories(path); }
            catch { return Enumerable.Empty<string>(); }
        }

        private static IEnumerable<string> EnumerateLevels(string root, int levels)
        {
            // levels=1 => immediate children; levels=2 => grandchildren, etc.
            var acc = new List<string>();
            try
            {
                var current = new List<string> { root };
                for (int i = 0; i < levels; i++)
                {
                    var next = new List<string>();
                    foreach (var p in current)
                    {
                        foreach (var d in SafeEnumerateDirectories(p))
                        {
                            acc.Add(d);
                            next.Add(d);
                        }
                    }
                    current = next;
                }
            }
            catch { }
            return acc;
        }

        private static void TryAddSteamApps(string path, HashSet<string> libs)
        {
            try { if (Directory.Exists(path)) libs.Add(path); } catch { }
        }

        private static void TryOverrideIfExists(Dictionary<string, string> roots, string key, string path)
        {
            try { if (Directory.Exists(path)) roots[key] = path; } catch { }
        }
    }
}
