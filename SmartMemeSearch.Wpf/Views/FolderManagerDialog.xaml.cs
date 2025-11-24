using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SmartMemeSearch.Wpf.Views
{
    public sealed partial class FolderManagerDialog : ContentDialog
    {
        // This is what {x:Bind Folders} uses
        public ObservableCollection<string> Folders { get; }

        public FolderManagerDialog(IEnumerable<string> existingFolders)
        {
            this.InitializeComponent();
            Folders = new ObservableCollection<string>(existingFolders);
        }

        private async void Add_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");

            var hwnd = WindowNative.GetWindowHandle(App.Current.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null)
                return;

            string path = Path.GetFullPath(folder.Path);
            path = Path.TrimEndingDirectorySeparator(path);

            // Already tracked?
            if (Folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                return;

            // 1) If path is inside an existing folder → reject
            if (Folders.Any(existing =>
                IsInside(path, existing)))
            {
                // user selected child of an already-selected folder → skip
                return;
            }

            // 2) If existing folders are inside the new folder → remove them
            foreach (var existing in Folders.ToList())
            {
                if (IsInside(existing, path))
                    Folders.Remove(existing);
            }

            // 3) Add new folder
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
            if (sender is Button btn && btn.Tag is string path)
                Folders.Remove(path);
        }
    }
}
