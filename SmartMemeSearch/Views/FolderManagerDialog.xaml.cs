using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SmartMemeSearch.Views
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

            var hwnd = WindowNative.GetWindowHandle(App.Window);
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null)
                return;

            string path = Path.GetFullPath(folder.Path);

            // Already tracked?
            if (Folders.Contains(path))
                return;

            // If we already track a parent, skip adding child
            if (Folders.Any(f => path.StartsWith(f + Path.DirectorySeparatorChar)))
                return;

            // If we are adding parent, drop its children
            foreach (var f in Folders.ToList())
            {
                if (f.StartsWith(path + Path.DirectorySeparatorChar))
                    Folders.Remove(f);
            }

            Folders.Add(path);
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
                Folders.Remove(path);
        }
    }
}
