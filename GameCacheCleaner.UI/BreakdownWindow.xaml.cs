using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace GameCacheCleaner.UI
{
    public partial class BreakdownWindow : Window
    {
        private readonly List<CacheRoot> _roots;
        public BreakdownWindow(List<CacheRoot> roots)
        {
            InitializeComponent();
            _roots = roots;
        }

        private async void ScanBtn_Click(object sender, RoutedEventArgs e)
        {
            ScanBtn.IsEnabled = false;
            List.Items.Clear();
            try
            {
                foreach (var r in _roots)
                {
                    if (!Directory.Exists(r.Path)) continue;
                    var subs = Directory.EnumerateDirectories(r.Path, "*", SearchOption.TopDirectoryOnly).ToList();
                    var sizes = new List<(string sub, long size)>();
                    foreach (var s in subs)
                    {
                        long size = 0;
                        try
                        {
                            foreach (var f in Directory.EnumerateFiles(s, "*", SearchOption.AllDirectories))
                            {
                                try { size += new FileInfo(f).Length; } catch { }
                                await Task.Yield();
                            }
                        } catch { }
                        sizes.Add((s, size));
                    }
                    foreach (var (sub, size) in sizes.OrderByDescending(x => x.size).Take(50))
                    {
                        List.Items.Add(new BreakdownRow { Root = r.Name, Subfolder = sub, SizeBytes = size });
                    }
                }
            }
            finally
            {
                ScanBtn.IsEnabled = true;
            }
        }
    }

    public class BreakdownRow
    {
        public string Root { get; set; } = "";
        public string Subfolder { get; set; } = "";
        public long SizeBytes { get; set; }
        public string SizeFormatted => Format.Bytes(SizeBytes);
    }
}
