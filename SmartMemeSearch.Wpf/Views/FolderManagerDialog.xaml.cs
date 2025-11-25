using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;

namespace SmartMemeSearch.Wpf.Views
{
    public partial class FolderManagerDialog : Window
    {
        public ObservableCollection<string> Folders { get; }

        public FolderManagerDialog(IEnumerable<string> existing)
        {
            InitializeComponent();
            Folders = new ObservableCollection<string>(existing);
            DataContext = this;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new FolderBrowserDialog();
            var result = dlg.ShowDialog();

            if (result != System.Windows.Forms.DialogResult.OK)
                return;

            var path = dlg.SelectedPath;

            path = Path.TrimEndingDirectorySeparator(path);

            if (Folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                return;

            // reject if subfolder of existing
            if (Folders.Any(existing => IsInside(path, existing)))
                return;

            // remove if existing is inside new
            foreach (var ex in Folders.ToList())
            {
                if (IsInside(ex, path))
                    Folders.Remove(ex);
            }

            Folders.Add(path);
        }

        public static bool IsInside(string sub, string parent)
        {
            parent = Path.TrimEndingDirectorySeparator(parent);
            sub = Path.TrimEndingDirectorySeparator(sub);

            return sub.StartsWith(parent + Path.DirectorySeparatorChar,
                                  StringComparison.OrdinalIgnoreCase);
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn &&
                btn.Tag is string folder)
            {
                Folders.Remove(folder);
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
