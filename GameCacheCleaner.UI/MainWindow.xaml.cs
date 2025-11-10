using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;
// Alias the WPF dialog types so there’s no ambiguity with WinForms:
using WpfMessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using System.Management;
using Microsoft.Win32;

namespace GameCacheCleaner.UI
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<CacheItem> Items { get; } = new ObservableCollection<CacheItem>();
        private Forms.NotifyIcon? _tray;
        private bool _isClosingToTray = false;
        private bool IsPro => LicenseService.ProEnabled;

        public MainWindow()
        {
            InitializeComponent();
            Grid.ItemsSource = Items;
            SummaryTxt.Text = "Ready.";
            PopulateRoots();
            InitTray();
            LoadScheduleState();

            var args = Environment.GetCommandLineArgs();
            if (args.Any(a => string.Equals(a, "--auto-clean", StringComparison.OrdinalIgnoreCase)))
            {
                if (!IsPro)
                {
                    WpfMessageBox.Show(this,
                        "--auto-clean is a Pro feature.\nGet Pro for £5 (lifetime).",
                        "Pro required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    _isClosingToTray = true;
                    System.Windows.Application.Current.Shutdown();
                    return;
                }
                // Headless scheduled run
                Dispatcher.InvokeAsync(async () =>
                {
                    DryRunChk.IsChecked = false;
                    await AnalyzeNow();
                    await CleanNow(false);
                    _isClosingToTray = true;
                    System.Windows.Application.Current.Shutdown();
                });
            }
            // Auto-analyze on normal startup so the grid fills immediately.
            Dispatcher.InvokeAsync(async () => await AnalyzeNow());
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            ApplyProState();
        }

        private void ApplyProState()
        {
            // FREE stays enabled: AnalyzeBtn, CleanBtn, DryRunChk, ExcludeTxt, BreakdownBtn, ExportBtn (txt)
            if (ScheduleChk != null) ScheduleChk.IsEnabled = IsPro;
            if (AggressiveScanChk != null) AggressiveScanChk.IsEnabled = IsPro; // Cross-drive / deeper scans gated
            // No trial messaging; Pro is enabled only when licensed.
        }

        private void RequirePro(string featureName)
        {
            var r = WpfMessageBox.Show(
                this,
                $"{featureName} is a Pro feature.\nGet Pro for £5 (lifetime).",
                "Pro required",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
            if (r == MessageBoxResult.OK)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://your-buy-url.example") { UseShellExecute = true });
                }
                catch { }
            }
        }

        private void PopulateRoots()
        {
            Items.Clear();
            bool aggressive = (AggressiveScanChk?.IsChecked ?? false);
            int depth = GetScanDepth();
            if (aggressive && depth < 2) depth = 2;
            foreach (var r in CacheRoot.KnownRoots(depth))
            {
                Items.Add(new CacheItem { Name = r.Name, Path = r.Path, Status = "Pending" });
            }
        }

        private void InitTray()
        {
            _tray = new Forms.NotifyIcon();
            try
            {
                var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "crestpoint.ico");
                if (File.Exists(iconPath)) _tray.Icon = new System.Drawing.Icon(iconPath);
            }
            catch { }
            _tray.Visible = true;
            _tray.Text = "Game Cache Cleaner — CrestPoint Digital";

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(new Forms.ToolStripMenuItem("Analyze now", null,
                async (_, __) => await Dispatcher.InvokeAsync(async () => await AnalyzeNow())));
            menu.Items.Add(new Forms.ToolStripMenuItem("Clean now", null,
                async (_, __) => await Dispatcher.InvokeAsync(async () => await CleanNow(false))));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(new Forms.ToolStripMenuItem("Toggle Dry run", null,
                (_, __) => Dispatcher.Invoke(() => DryRunChk.IsChecked = !(DryRunChk.IsChecked ?? false))));
            menu.Items.Add(new Forms.ToolStripMenuItem("Show window", null,
                (_, __) => Dispatcher.Invoke(() => { this.Show(); this.WindowState = WindowState.Normal; this.Activate(); })));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(new Forms.ToolStripMenuItem("Exit", null, (_, __) => Dispatcher.Invoke(() =>
            {
                if (_tray != null) _tray.Visible = false;
                _isClosingToTray = true;
                System.Windows.Application.Current.Shutdown();
            })));
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, __) => Dispatcher.Invoke(() => { this.Show(); this.WindowState = WindowState.Normal; this.Activate(); });
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            // When minimized, hide to tray instead of showing a taskbar button
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
                try { _tray?.ShowBalloonTip(1200, "Game Cache Cleaner", "Hidden to tray.", Forms.ToolTipIcon.Info); } catch { }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isClosingToTray)
            {
                e.Cancel = true;
                this.Hide();
                try { _tray?.ShowBalloonTip(1500, "Game Cache Cleaner", "Still running in the tray.", Forms.ToolTipIcon.Info); } catch { }
            }
            else
            {
                base.OnClosing(e);
            }
        }

        private async void AnalyzeBtn_Click(object sender, RoutedEventArgs e) => await AnalyzeNow();

        private async Task AnalyzeNow()
        {
            SummaryTxt.Text = "Analyzing...";
            SetUiEnabled(false);
            try
            {
                var excludes = ParseExcludes();
                long total = 0;
                var vendor = DetectGpuVendor();
                var installed = DetectInstalledLaunchers();
                foreach (var item in Items)
                {
                    // Vendor-aware N/A statuses for GL caches
                    if (item.Name.Contains("AMD GLCache", StringComparison.OrdinalIgnoreCase) && vendor != "AMD")
                    {
                        item.Status = "Not applicable"; item.SizeBytes = 0; item.Files = 0; continue;
                    }
                    if (item.Name.Contains("NVIDIA GLCache", StringComparison.OrdinalIgnoreCase) && vendor != "NVIDIA")
                    {
                        item.Status = "Not applicable"; item.SizeBytes = 0; item.Files = 0; continue;
                    }
                    if (item.Name.Contains("NVIDIA DXCache", StringComparison.OrdinalIgnoreCase) && vendor != "NVIDIA")
                    {
                        item.Status = "Not applicable"; item.SizeBytes = 0; item.Files = 0; continue;
                    }
                    // New NVIDIA caches are vendor-specific as well
                    if (item.Name.Contains("NVIDIA NV_Cache", StringComparison.OrdinalIgnoreCase) && vendor != "NVIDIA")
                    {
                        item.Status = "Not applicable"; item.SizeBytes = 0; item.Files = 0; continue;
                    }
                    if (item.Name.Contains("NVIDIA ComputeCache", StringComparison.OrdinalIgnoreCase) && vendor != "NVIDIA")
                    {
                        item.Status = "Not applicable"; item.SizeBytes = 0; item.Files = 0; continue;
                    }

                    // Launcher presence awareness
                    if (item.Name.Contains("Battle.net", StringComparison.OrdinalIgnoreCase) && !installed.BattleNet)
                    { item.Status = "Not installed"; item.SizeBytes = 0; item.Files = 0; continue; }
                    if (item.Name.Contains("GOG", StringComparison.OrdinalIgnoreCase) && !installed.GogGalaxy)
                    { item.Status = "Not installed"; item.SizeBytes = 0; item.Files = 0; continue; }
                    if (item.Name.Contains("Epic", StringComparison.OrdinalIgnoreCase) && !installed.Epic)
                    { item.Status = "Not installed"; item.SizeBytes = 0; item.Files = 0; continue; }
                    if (item.Name.Contains("Steam", StringComparison.OrdinalIgnoreCase) && !installed.Steam)
                    { item.Status = "Not installed"; item.SizeBytes = 0; item.Files = 0; continue; }
                    if (item.Name.Contains("Unity", StringComparison.OrdinalIgnoreCase) && !installed.Unity)
                    { item.Status = "Not installed"; item.SizeBytes = 0; item.Files = 0; continue; }
                    if (item.Name.Contains("UE", StringComparison.OrdinalIgnoreCase) && !installed.Unreal)
                    { item.Status = "Not installed"; item.SizeBytes = 0; item.Files = 0; continue; }
                    // Epic DDC is effectively Unreal-related; mark N/A when UE not installed
                    if (item.Name.Contains("Epic DerivedDataCache", StringComparison.OrdinalIgnoreCase) && !installed.Unreal)
                    { item.Status = "Not applicable"; item.SizeBytes = 0; item.Files = 0; continue; }
                    // Xbox app presence awareness (Microsoft Gaming App)
                    if (item.Name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) && !installed.Xbox)
                    { item.Status = "Not installed"; item.SizeBytes = 0; item.Files = 0; continue; }
                    // EA App / Ubisoft Connect / Rockstar Launcher presence awareness
                    if (item.Name.Contains("EA App", StringComparison.OrdinalIgnoreCase) && !installed.EA)
                    { item.Status = "Not installed"; item.SizeBytes = 0; item.Files = 0; continue; }
                    if (item.Name.Contains("Ubisoft", StringComparison.OrdinalIgnoreCase) && !installed.Ubisoft)
                    { item.Status = "Not installed"; item.SizeBytes = 0; item.Files = 0; continue; }
                    if (item.Name.Contains("Rockstar", StringComparison.OrdinalIgnoreCase) && !installed.Rockstar)
                    { item.Status = "Not installed"; item.SizeBytes = 0; item.Files = 0; continue; }
                    // Amazon Games / Itch.io presence awareness
                    if (item.Name.Contains("Amazon Games", StringComparison.OrdinalIgnoreCase) && !installed.Amazon)
                    { item.Status = "Not installed"; item.SizeBytes = 0; item.Files = 0; continue; }
                    if (item.Name.Contains("Itch", StringComparison.OrdinalIgnoreCase) && !installed.Itch)
                    { item.Status = "Not installed"; item.SizeBytes = 0; item.Files = 0; continue; }

                    var res = await DirSizer.SizeOfAsync(item.Path, excludes);
                    item.SizeBytes = res.size;
                    item.Files = res.files;
                    // Mark Found if directory exists or has contents; otherwise classify better than "Missing".
                    if (Directory.Exists(item.Path) || res.size > 0 || res.files > 0)
                        item.Status = "Found";
                    else if (item.Name.Contains("DirectX ShaderCache", StringComparison.OrdinalIgnoreCase))
                        item.Status = "Not initialized";
                    else if (item.Name.Contains("NVIDIA GLCache", StringComparison.OrdinalIgnoreCase) && vendor == "NVIDIA")
                        item.Status = "Not initialized";
                    else if (item.Name.Contains("NVIDIA DXCache", StringComparison.OrdinalIgnoreCase) && vendor == "NVIDIA")
                        item.Status = "Not initialized";
                    else if (item.Name.Contains("NVIDIA NV_Cache", StringComparison.OrdinalIgnoreCase) && vendor == "NVIDIA")
                        item.Status = "Not initialized";
                    else if (item.Name.Contains("NVIDIA ComputeCache", StringComparison.OrdinalIgnoreCase) && vendor == "NVIDIA")
                        item.Status = "Not initialized";
                    else if (item.Name.Contains("Battle.net", StringComparison.OrdinalIgnoreCase) && installed.BattleNet)
                        item.Status = "Not initialized";
                    else if (item.Name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) && installed.Xbox)
                        item.Status = "Not initialized";
                    else if (item.Name.Contains("EA App", StringComparison.OrdinalIgnoreCase) && installed.EA)
                        item.Status = "Not initialized";
                    else if (item.Name.Contains("Ubisoft", StringComparison.OrdinalIgnoreCase) && installed.Ubisoft)
                        item.Status = "Not initialized";
                    else if (item.Name.Contains("Rockstar", StringComparison.OrdinalIgnoreCase) && installed.Rockstar)
                        item.Status = "Not initialized";
                    else if (item.Name.Contains("Amazon Games", StringComparison.OrdinalIgnoreCase) && installed.Amazon)
                        item.Status = "Not initialized";
                    else if (item.Name.Contains("Itch", StringComparison.OrdinalIgnoreCase) && installed.Itch)
                        item.Status = "Not initialized";
                    else
                        item.Status = "Missing";
                    total += res.size;
                }
                Grid.Items.Refresh();
                SummaryTxt.Text = "Total reclaimable (est.): " + Format.Bytes(total);
            }
            finally
            {
                SetUiEnabled(true);
            }
        }

        private async void CleanBtn_Click(object sender, RoutedEventArgs e) => await CleanNow(true);

        private async Task CleanNow(bool prompt)
        {
            bool dry = (DryRunChk.IsChecked == true);
            var excludes = ParseExcludes();

            if (prompt && !dry)
            {
                var confirm = WpfMessageBox.Show(this, "Proceed to delete cache contents? This cannot be undone.",
                    "Confirm clean", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;
            }

            SummaryTxt.Text = dry ? "Dry run: simulating deletions..." : "Cleaning...";
            SetUiEnabled(false);

            long freed = 0; int files = 0; int dirs = 0;
            string logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "cleaner.log");
            var log = new Logger(logPath);

            try
            {
                foreach (var item in Items)
                {
                    if (!Directory.Exists(item.Path)) { item.Status = "Missing"; continue; }
                    var res = await DirSizer.SizeOfAsync(item.Path, excludes);
                    if (res.size == 0) { item.Status = "Empty"; continue; }

                    log.Write("CLEAN " + item.Name + " | " + item.Path + " | " + Format.Bytes(res.size));
                    item.Status = dry ? "Would delete" : "Deleting...";

                    if (!dry)
                    {
                        var del = await Cleaner.SafeDeleteAsync(item.Path, excludes);
                        files += del.files; dirs += del.dirs; freed += res.size;
                        item.Status = "Deleted";
                        item.SizeBytes = 0; item.Files = 0;
                    }
                    else
                    {
                        item.Status = "Simulated";
                    }
                }
                Grid.Items.Refresh();
                SummaryTxt.Text = dry
                    ? "Dry run complete. No files deleted."
                    : "Done. Freed ~" + Format.Bytes(freed) + " (files: " + files.ToString("n0") + ", dirs: " + dirs.ToString("n0") + "). Log: " + logPath;
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetUiEnabled(true);
            }
        }

        private string DetectGpuVendor()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("select Name, AdapterCompatibility from Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    var name = (obj["Name"]?.ToString() ?? "").ToUpperInvariant();
                    var compat = (obj["AdapterCompatibility"]?.ToString() ?? "").ToUpperInvariant();
                    if (name.Contains("NVIDIA") || compat.Contains("NVIDIA")) return "NVIDIA";
                    if (name.Contains("ADVANCED MICRO DEVICES") || name.Contains("AMD") || compat.Contains("ATI") || compat.Contains("AMD")) return "AMD";
                    if (name.Contains("INTEL") || compat.Contains("INTEL")) return "Intel";
                }
            }
            catch { }
            return "Unknown";
        }

        private (bool Steam, bool Epic, bool GogGalaxy, bool BattleNet, bool Xbox, bool Unity, bool Unreal, bool EA, bool Ubisoft, bool Rockstar, bool Amazon, bool Itch) DetectInstalledLaunchers()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pfx = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string ca = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            // Treat a launcher as installed only if its executable exists.
            // Use uninstall keys solely to discover non-default InstallLocation paths.
            var steamPaths = new List<string>
            {
                System.IO.Path.Combine(pfx, "Steam", "steam.exe"),
                System.IO.Path.Combine(pf, "Steam", "steam.exe")
            };
            foreach (var loc in GetInstallLocationsFromUninstall("Steam"))
                steamPaths.Add(System.IO.Path.Combine(loc, "steam.exe"));
            bool steam = HasExecutableAny(steamPaths.ToArray());

            var epicPaths = new List<string>
            {
                System.IO.Path.Combine(pfx, "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"),
                System.IO.Path.Combine(pf, "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe")
            };
            foreach (var loc in GetInstallLocationsFromUninstall("Epic Games Launcher"))
                epicPaths.Add(System.IO.Path.Combine(loc, "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"));
            bool epic = HasExecutableAny(epicPaths.ToArray());

            var gogPaths = new List<string>
            {
                System.IO.Path.Combine(pfx, "GOG Galaxy", "GalaxyClient.exe"),
                System.IO.Path.Combine(pf, "GOG Galaxy", "GalaxyClient.exe")
            };
            foreach (var loc in GetInstallLocationsFromUninstall("GOG Galaxy"))
                gogPaths.Add(System.IO.Path.Combine(loc, "GalaxyClient.exe"));
            bool gog = HasExecutableAny(gogPaths.ToArray());

            var bnetPaths = new List<string>
            {
                System.IO.Path.Combine(pfx, "Battle.net", "Battle.net.exe"),
                System.IO.Path.Combine(pf, "Battle.net", "Battle.net.exe")
            };
            foreach (var loc in GetInstallLocationsFromUninstall("Battle.net"))
                bnetPaths.Add(System.IO.Path.Combine(loc, "Battle.net.exe"));
            bool bnet = HasExecutableAny(bnetPaths.ToArray());
            // Xbox app (Microsoft Gaming App) presence via UWP Packages folder
            bool xbox = Directory.Exists(System.IO.Path.Combine(la, "Packages", "Microsoft.GamingApp_8wekyb3d8bbwe"));
            bool unity = Directory.Exists(System.IO.Path.Combine(la, "Unity")) || Directory.Exists(System.IO.Path.Combine(ca, "Unity"));
            bool unreal = HasUnrealInstall(pf) || HasUnrealInstall(pfx);
            // EA App
            var eaPaths = new List<string>
            {
                System.IO.Path.Combine(pf, "EA", "EA App", "EAApp.exe"),
                System.IO.Path.Combine(pfx, "EA", "EA App", "EAApp.exe"),
                System.IO.Path.Combine(pf, "EA Desktop", "EA Desktop.exe"),
                System.IO.Path.Combine(pfx, "EA Desktop", "EA Desktop.exe")
            };
            foreach (var loc in GetInstallLocationsFromUninstall("EA App"))
                eaPaths.Add(System.IO.Path.Combine(loc, "EAApp.exe"));
            foreach (var loc in GetInstallLocationsFromUninstall("EA Desktop"))
                eaPaths.Add(System.IO.Path.Combine(loc, "EA Desktop.exe"));
            bool ea = HasExecutableAny(eaPaths.ToArray());

            // Ubisoft Connect
            var ubiPaths = new List<string>
            {
                System.IO.Path.Combine(pfx, "Ubisoft", "Ubisoft Game Launcher", "UbisoftConnect.exe"),
                System.IO.Path.Combine(pf, "Ubisoft", "Ubisoft Game Launcher", "UbisoftConnect.exe")
            };
            foreach (var loc in GetInstallLocationsFromUninstall("Ubisoft Connect"))
                ubiPaths.Add(System.IO.Path.Combine(loc, "UbisoftConnect.exe"));
            foreach (var loc in GetInstallLocationsFromUninstall("Ubisoft Game Launcher"))
                ubiPaths.Add(System.IO.Path.Combine(loc, "UbisoftConnect.exe"));
            bool ubi = HasExecutableAny(ubiPaths.ToArray());

            // Rockstar Games Launcher
            var rockPaths = new List<string>
            {
                System.IO.Path.Combine(pf, "Rockstar Games", "Launcher", "Launcher.exe"),
                System.IO.Path.Combine(pfx, "Rockstar Games", "Launcher", "Launcher.exe"),
                System.IO.Path.Combine(la, "Rockstar Games", "Launcher", "Launcher.exe")
            };
            foreach (var loc in GetInstallLocationsFromUninstall("Rockstar Games Launcher"))
                rockPaths.Add(System.IO.Path.Combine(loc, "Launcher.exe"));
            bool rockstar = HasExecutableAny(rockPaths.ToArray());

            // Amazon Games Launcher
            var amazonPaths = new List<string>
            {
                System.IO.Path.Combine(la, "Amazon Games", "App", "Amazon Games.exe"),
                System.IO.Path.Combine(pf, "Amazon Games", "Amazon Games.exe"),
                System.IO.Path.Combine(pfx, "Amazon Games", "Amazon Games.exe")
            };
            foreach (var loc in GetInstallLocationsFromUninstall("Amazon Games"))
                amazonPaths.Add(System.IO.Path.Combine(loc, "Amazon Games.exe"));
            bool amazon = HasExecutableAny(amazonPaths.ToArray());

            // Itch.io
            var itchPaths = new List<string>
            {
                System.IO.Path.Combine(la, "itch", "itch.exe"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "itch", "itch.exe")
            };
            foreach (var loc in GetInstallLocationsFromUninstall("itch"))
                itchPaths.Add(System.IO.Path.Combine(loc, "itch.exe"));
            bool itch = HasExecutableAny(itchPaths.ToArray());

            return (steam, epic, gog, bnet, xbox, unity, unreal, ea, ubi, rockstar, amazon, itch);
        }

        private bool HasUnrealInstall(string root)
        {
            try
            {
                foreach (var d in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = System.IO.Path.GetFileName(d) ?? "";
                    if (name.StartsWith("UE", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        private int GetScanDepth()
        {
            try
            {
                var item = ScanDepthCmb?.SelectedItem as System.Windows.Controls.ComboBoxItem;
                var txt = item?.Content?.ToString() ?? "1";
                if (int.TryParse(txt, out int d)) return Math.Max(1, Math.Min(3, d));
                return 1;
            }
            catch { return 1; }
        }

        private bool HasRegistryKeyAnyHive(string[] subkeys)
        {
            try
            {
                foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    foreach (var sk in subkeys)
                    {
                        using var key = hive.OpenSubKey(sk);
                        if (key != null) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private bool HasUninstallEntry(string displayNameContains)
        {
            try
            {
                foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    foreach (var uninstallPath in new[]{
                        "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                        "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
                    })
                    {
                        using var baseKey = hive.OpenSubKey(uninstallPath);
                        if (baseKey == null) continue;
                        foreach (var sub in baseKey.GetSubKeyNames())
                        {
                            try
                            {
                                using var key = baseKey.OpenSubKey(sub);
                                var name = key?.GetValue("DisplayName") as string;
                                if (!string.IsNullOrWhiteSpace(name) && name.IndexOf(displayNameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                                    return true;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private bool HasExecutableAny(string[] paths)
        {
            try
            {
                foreach (var p in paths)
                {
                    if (File.Exists(p)) return true;
                }
            }
            catch { }
            return false;
        }

        private IEnumerable<string> GetInstallLocationsFromUninstall(string displayNameContains)
        {
            var results = new List<string>();
            try
            {
                foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    foreach (var uninstallPath in new[]{
                        "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                        "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
                    })
                    {
                        using var baseKey = hive.OpenSubKey(uninstallPath);
                        if (baseKey == null) continue;
                        foreach (var sub in baseKey.GetSubKeyNames())
                        {
                            try
                            {
                                using var key = baseKey.OpenSubKey(sub);
                                var name = key?.GetValue("DisplayName") as string;
                                if (!string.IsNullOrWhiteSpace(name) && name.IndexOf(displayNameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    var loc = key?.GetValue("InstallLocation") as string;
                                    if (!string.IsNullOrWhiteSpace(loc)) results.Add(loc);
                                    // Some uninstall entries use UninstallString with full exe path; try to derive directory
                                    var uninst = key?.GetValue("UninstallString") as string;
                                    if (!string.IsNullOrWhiteSpace(uninst))
                                    {
                                        try
                                        {
                                            var dir = System.IO.Path.GetDirectoryName(uninst) ?? "";
                                            if (!string.IsNullOrWhiteSpace(dir)) results.Add(dir);
                                        }
                                        catch { }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            return results;
        }

        private void AggressiveScanChk_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsPro)
            {
                AggressiveScanChk.IsChecked = false;
                RequirePro("Cross-drive scanning");
                return;
            }
            PopulateRoots();
            Dispatcher.InvokeAsync(async () => await AnalyzeNow());
        }
        private void AggressiveScanChk_Unchecked(object sender, RoutedEventArgs e)
        {
            PopulateRoots();
            Dispatcher.InvokeAsync(async () => await AnalyzeNow());
        }

        private void ScanDepthCmb_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            PopulateRoots();
            Dispatcher.InvokeAsync(async () => await AnalyzeNow());
        }

        private void BreakdownBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new BreakdownWindow(Items.Select(i => new CacheRoot(i.Name, i.Path)).ToList());
            dlg.Owner = this;
            dlg.ShowDialog();
        }

        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                FileName = "GameCacheCleaner-Report.txt",
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Game Cache Cleaner — CrestPoint Digital");
                sb.AppendLine("Date: " + DateTime.Now.ToString());
                sb.AppendLine();
                long total = 0;
                foreach (var it in Items)
                {
                    sb.AppendLine(it.Name);
                    sb.AppendLine("  Path : " + it.Path);
                    sb.AppendLine("  Size : " + it.SizeFormatted);
                    sb.AppendLine("  Files: " + it.FilesFormatted);
                    sb.AppendLine("  Status: " + it.Status);
                    sb.AppendLine();
                    total += it.SizeBytes;
                }
                sb.AppendLine("Total estimated: " + Format.Bytes(total));
                File.WriteAllText(dlg.FileName, sb.ToString());
                SummaryTxt.Text = "Report saved: " + dlg.FileName;
            }
        }

        private List<string> ParseExcludes()
        {
            var val = ExcludeTxt != null ? (ExcludeTxt.Text ?? "").Trim() : "";
            var list = new List<string>();
            if (!string.IsNullOrEmpty(val))
            {
                foreach (var part in val.Split(';'))
                {
                    var p = (part ?? "").Trim();
                    if (string.IsNullOrEmpty(p)) continue;
                    try { list.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(p))); } catch { }
                }
            }
            return list;
        }

        private void HideToTrayBtn_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            try { _tray?.ShowBalloonTip(1200, "Game Cache Cleaner", "Hidden to tray.", Forms.ToolTipIcon.Info); } catch { }
        }

        private void AddCustomRootBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var dlg = new Forms.FolderBrowserDialog())
                {
                    dlg.Description = "Select a cache folder to add";
                    dlg.ShowNewFolderButton = false;
                    var result = dlg.ShowDialog();
                    if (result == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                    {
                        var path = dlg.SelectedPath;
                        // Avoid duplicates
                        if (!Items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
                        {
                            Items.Add(new CacheItem { Name = "Custom Root", Path = path, Status = "Pending" });
                            // Immediately analyze to populate size/status for the newly added root
                            Dispatcher.InvokeAsync(async () => await AnalyzeNow());
                        }
                        else
                        {
                            WpfMessageBox.Show(this, "That path is already in the list.", "Custom root", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(this, ex.Message, "Custom root", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetUiEnabled(bool enabled)
        {
            AnalyzeBtn.IsEnabled = enabled;
            CleanBtn.IsEnabled = enabled;
            ExportBtn.IsEnabled = enabled;
            BreakdownBtn.IsEnabled = enabled;
            AddCustomRootBtn.IsEnabled = enabled;
            if (ScanDepthCmb != null) ScanDepthCmb.IsEnabled = enabled;
            if (AggressiveScanChk != null) AggressiveScanChk.IsEnabled = enabled;
        }

        // ---------- Scheduler ----------
        private string TaskName { get { return "GameCacheCleaner_WeeklyClean"; } }

        private void LoadScheduleState()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.ArgumentList.Add("/Query");
                psi.ArgumentList.Add("/TN");
                psi.ArgumentList.Add(TaskName);

                var p = Process.Start(psi);
                if (p != null) p.WaitForExit(1500);
                bool exists = (p != null && p.ExitCode == 0);
                ScheduleChk.IsChecked = exists;
                UpdateScheduleInfo(exists);
            }
            catch { ScheduleChk.IsChecked = false; }
        }

        private void ScheduleChk_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsPro)
            {
                ScheduleChk.IsChecked = false;
                RequirePro("Scheduler");
                return;
            }
            TryCreateWeeklyTask();
        }
        private void ScheduleChk_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!IsPro)
            {
                ScheduleChk.IsChecked = true;
                RequirePro("Scheduler");
                return;
            }
            TryDeleteWeeklyTask();
        }

        private void EnterLicenseBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new LicensePromptWindow { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                if (string.IsNullOrWhiteSpace(dlg.LicenseToken))
                {
                    WpfMessageBox.Show(this, "Please paste a license token.", "Enter License", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (LicenseService.Activate(dlg.LicenseToken))
                {
                    WpfMessageBox.Show(this, "Pro activated. Thank you!", "Activated", MessageBoxButton.OK, MessageBoxImage.Information);
                    ApplyProState();
                }
                else
                {
                    WpfMessageBox.Show(this, "Invalid license.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void TryCreateWeeklyTask()
{
    try
    {
        // robust exe path for single-file
        string exePath = Process.GetCurrentProcess().MainModule != null
            ? (Process.GetCurrentProcess().MainModule!.FileName ?? "")
            : "";
        if (string.IsNullOrWhiteSpace(exePath))
            exePath = System.IO.Path.Combine(AppContext.BaseDirectory, "GameCacheCleaner.UI.exe");

        var trValue = "\"" + exePath + "\" --auto-clean";

        var psi = new ProcessStartInfo("schtasks");
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;

        psi.ArgumentList.Add("/Create");
        psi.ArgumentList.Add("/F");
        psi.ArgumentList.Add("/SC"); psi.ArgumentList.Add("WEEKLY");
        psi.ArgumentList.Add("/D");  psi.ArgumentList.Add("SUN");
        psi.ArgumentList.Add("/ST"); psi.ArgumentList.Add("03:00");
        psi.ArgumentList.Add("/TN"); psi.ArgumentList.Add(TaskName);
        psi.ArgumentList.Add("/TR"); psi.ArgumentList.Add(trValue);
        psi.ArgumentList.Add("/RL"); psi.ArgumentList.Add("LIMITED");

        var p = Process.Start(psi);
        if (p != null) p.WaitForExit();

        if (p != null && p.ExitCode == 0)
        {
            try { _tray?.ShowBalloonTip(1500, "Scheduled", "Weekly clean scheduled (Sun 03:00).", Forms.ToolTipIcon.Info); } catch { }
            LoadScheduleState();
        }
        else
        {
            var err = p != null ? p.StandardError.ReadToEnd() : "Failed to start schtasks.";
            System.Windows.MessageBox.Show(this, string.IsNullOrWhiteSpace(err) ? "Failed to create task." : err,
                "Scheduler", MessageBoxButton.OK, MessageBoxImage.Warning);
            ScheduleChk.IsChecked = false;
        }
    }
    catch (Exception ex)
    {
        System.Windows.MessageBox.Show(this, ex.Message, "Scheduler", MessageBoxButton.OK, MessageBoxImage.Error);
        ScheduleChk.IsChecked = false;
    }
}

        private void TryDeleteWeeklyTask()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                psi.ArgumentList.Add("/Delete");
                psi.ArgumentList.Add("/F");
                psi.ArgumentList.Add("/TN");
                psi.ArgumentList.Add(TaskName);

                var p = Process.Start(psi);
                if (p != null) p.WaitForExit();

                if (p != null && p.ExitCode == 0)
                {
                    try { _tray?.ShowBalloonTip(1500, "Scheduler", "Weekly clean task removed.", Forms.ToolTipIcon.Info); } catch { }
                    LoadScheduleState();
                }
                else
                {
                    var err = p != null ? p.StandardError.ReadToEnd() : "Failed to start schtasks.";
                    System.Windows.MessageBox.Show(this, string.IsNullOrWhiteSpace(err) ? "Failed to delete task." : err,
                        "Scheduler", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ScheduleChk.IsChecked = true;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "Scheduler", MessageBoxButton.OK, MessageBoxImage.Error);
                ScheduleChk.IsChecked = true;
            }
        }

        private void UpdateScheduleInfo(bool exists)
        {
            try
            {
                if (!exists)
                {
                    if (ScheduleInfoTxt != null) ScheduleInfoTxt.Text = "";
                    return;
                }

                var psi = new ProcessStartInfo("schtasks");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.ArgumentList.Add("/Query");
                psi.ArgumentList.Add("/TN");
                psi.ArgumentList.Add(TaskName);
                psi.ArgumentList.Add("/FO"); psi.ArgumentList.Add("LIST");
                psi.ArgumentList.Add("/V");

                var p = Process.Start(psi);
                if (p != null) p.WaitForExit(2000);
                string output = p != null ? p.StandardOutput.ReadToEnd() : "";
                string lastRun = ""; string lastResult = "";
                foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var idx = line.IndexOf(':');
                    if (idx < 0) continue;
                    var key = line.Substring(0, idx).Trim();
                    var val = line.Substring(idx + 1).Trim();
                    if (key.Contains("Last Run Time", StringComparison.OrdinalIgnoreCase)) lastRun = val;
                    if (key.Contains("Last Result", StringComparison.OrdinalIgnoreCase)) lastResult = val;
                }
                if (ScheduleInfoTxt != null)
                {
                    if (!string.IsNullOrWhiteSpace(lastRun) || !string.IsNullOrWhiteSpace(lastResult))
                        ScheduleInfoTxt.Text = $"Last run: {lastRun}  ({lastResult})";
                    else
                        ScheduleInfoTxt.Text = "Scheduled";
                }
            }
            catch
            {
                if (ScheduleInfoTxt != null) ScheduleInfoTxt.Text = "";
            }
        }


        }

    public class CacheItem : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        private long _size;
        public long SizeBytes { get { return _size; } set { _size = value; OnChanged("SizeBytes"); OnChanged("SizeFormatted"); } }
        private long _files;
        public long Files { get { return _files; } set { _files = value; OnChanged("Files"); OnChanged("FilesFormatted"); } }
        public string Status { get; set; } = "Pending";

        public string SizeFormatted { get { return Format.Bytes(SizeBytes); } }
        public string FilesFormatted { get { return Files.ToString("n0"); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
