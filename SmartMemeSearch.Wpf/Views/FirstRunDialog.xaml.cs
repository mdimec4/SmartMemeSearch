using System.Windows;

namespace SmartMemeSearch.Wpf.Views
{
    public partial class FirstRunDialog : Window
    {
        public bool ChooseFolders { get; private set; } = false;

        public FirstRunDialog()
        {
            InitializeComponent();
        }

        private void Choose_Click(object sender, RoutedEventArgs e)
        {
            ChooseFolders = true;
            DialogResult = true;
            Close();
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            ChooseFolders = false;
            DialogResult = true;
            Close();
        }
    }
}
