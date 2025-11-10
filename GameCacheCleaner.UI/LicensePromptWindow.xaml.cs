using System.Windows;

namespace GameCacheCleaner.UI
{
    public partial class LicensePromptWindow : Window
    {
        public string LicenseToken { get; private set; } = "";

        public LicensePromptWindow()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            LicenseToken = LicenseTokenBox.Text?.Trim() ?? "";
            DialogResult = true;
        }
    }
}

