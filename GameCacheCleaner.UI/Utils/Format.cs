namespace GameCacheCleaner.UI
{
    public static class Format
    {
        public static string Bytes(long b)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = b; int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return $"{v:0.##} {units[i]}";
        }
    }
}
