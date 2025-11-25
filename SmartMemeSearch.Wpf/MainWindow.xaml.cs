using SmartMemeSearch.Wpf.Services;
using SmartMemeSearch.Wpf.ViewModels;
using System;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Clipboard = System.Windows.Clipboard;
using ContextMenu = System.Windows.Controls.ContextMenu;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;

namespace SmartMemeSearch.Wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private bool _autoSyncStarted = false;
        public MainWindow()
        {
            InitializeComponent();
            Loaded += Page_Loaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            if (vm == null)
                return;

            vm.SearchCompleted += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (ResultsList.Items.Count > 0)
                    {
                        ResultsList.ScrollIntoView(ResultsList.Items[0]);
                        ResultsList.UpdateLayout();
                    }
                });
            };

            if (AdsView != null)
            {

#if MS_STORE_FREE_WITH_ADDS
                    InitAdds();      
#else
                vm.IsPremium = true;
#endif
            }

            if (_autoSyncStarted)
                return;

            _autoSyncStarted = true;



            _ = Task.Run(async () =>
            {
                await RunExclusiveAutoSync(vm); // first sync

                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(5));
                    await RunExclusiveAutoSync(vm);
                }
            });
        }

#if MS_STORE_FREE_WITH_ADDS
        private async void InitAdds()
        {
            // A-Ads HTML
            string html = @"
<!DOCTYPE html>
<html>
<body style='margin:0;padding:0;background:transparent;overflow:hidden;'>

<iframe 
    data-aa='2363747'
    src='https://acceptable.a-ads.com/2363747'
    style='border:0;width:100%;height:80px;background:transparent;'>
</iframe>

</body>
</html>";

            try
            {
                await AdsView.EnsureCoreWebView2Async();
                AdsView.NavigateToString(html);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ADS LOAD ERROR: " + ex);
            }
        }
#endif


        private async Task RunExclusiveAutoSync(MainViewModel vm)
        {
            if (vm is null)
                return;

            // Skip if another sync is running
            if (!vm.TryBeginSync())
                return;

            // Use the UI thread dispatcher stored in the Page field
            _dispatcher.Invoke(() =>
            {
                vm.IsImporting = true;
                vm.CurrentFile = "Checking folders...";
                vm.ProgressValue = 0;
            });

            try
            {
                await vm.AutoSyncAllAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("AUTO SYNC ERROR: " + ex);
            }
            finally
            {
                _dispatcher.Invoke(() =>
                {
                    vm.IsImporting = false;
                    vm.CurrentFile = "Done";

                    if (!string.IsNullOrWhiteSpace(vm.Query))
                        vm.Search();
                });

                vm.EndSync();
            }
        }

        private static SearchResult? GetResultFromSender(object sender)
        {
            // If the event comes from an element inside the DataTemplate
            if (sender is FrameworkElement fe && fe.DataContext is SearchResult sr1)
                return sr1;

            // If a WPF MenuItem triggered the event
            if (sender is MenuItem mi)
            {
                if (mi.DataContext is SearchResult sr2)
                    return sr2;

                // When used with ContextMenu, DataContext may be missing—
                // we can get the item from PlacementTarget.
                if (mi.Parent is ContextMenu cm &&
                    cm.PlacementTarget is FrameworkElement fe2 &&
                    fe2.DataContext is SearchResult sr3)
                    return sr3;
            }

            return null;
        }



        private void ManageFolders_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            if (vm == null)
                return;
           vm.ManageFolders();
        }

        private void OpenItem_Click(object sender, RoutedEventArgs e)
        {
            if (GetResultFromSender(sender) is { FilePath: var path } &&
                !string.IsNullOrEmpty(path))
            {
                OpenFileWithShell(path);
            }
        }

        private async void CopyImageMenu_Click(object sender, RoutedEventArgs e)
        {
            if (GetResultFromSender(sender) is { FilePath: var path } &&
                File.Exists(path))
            {
                try
                {
                    var image = new BitmapImage();
                    using (var stream = File.OpenRead(path))
                    {
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = stream;
                        image.EndInit();
                    }

                    Clipboard.SetImage(image);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Copy image failed: " + ex);
                }
            }
        }

        private async void CopyFileMenu_Click(object sender, RoutedEventArgs e)
        {
            if (GetResultFromSender(sender) is { FilePath: var path } &&
                File.Exists(path))
            {
                try
                {
                    Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection() { path });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Copy file failed: " + ex);
                }
            }
        }

        private void CopyPathMenu_Click(object sender, RoutedEventArgs e)
        {
            if (GetResultFromSender(sender) is { FilePath: var path } &&
                !string.IsNullOrEmpty(path))
            {
                try
                {

                    Clipboard.SetText(path);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Copy path failed: " + ex);
                }
            }
        }

        private void OpenFolderMenu_Click(object sender, RoutedEventArgs e)
        {
            if (GetResultFromSender(sender) is { FilePath: var path } &&
                File.Exists(path))
            {
                try
                {
                    // Open Explorer and select the file
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Open folder failed: " + ex);
                }
            }
        }

        private void OpenFileWithShell(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Open file failed: " + ex);
            }
        }

        private void ResultsList_KeyDown(object sender, KeyEventArgs e)
        {
            var r = GetSelected();
            if (r == null) return;

            // --- Ctrl + L → Open folder ---
            if (e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (File.Exists(r.FilePath))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{r.FilePath}\"")
                    {
                        UseShellExecute = true
                    });
                }

                e.Handled = true;
                return;
            }

            // --- Ctrl + C → Copy file ---
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.None)
            {
                if (File.Exists(r.FilePath))
                {
                    var col = new System.Collections.Specialized.StringCollection();
                    col.Add(r.FilePath);
                    Clipboard.SetFileDropList(col);
                }

                e.Handled = true;
                return;
            }

            // --- Ctrl + Shift + C → Copy image ---
            if (e.Key == Key.C &&
                (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) ==
                (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (File.Exists(r.FilePath))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        using (var stream = File.OpenRead(r.FilePath))
                        {
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = stream;
                            bmp.EndInit();
                        }

                        Clipboard.SetImage(bmp);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Copy image failed: " + ex);
                    }
                }

                e.Handled = true;
                return;
            }

            // Enter key → open image
            if (e.Key == Key.Enter)
            {
                if (File.Exists(r.FilePath))
                    OpenFileWithShell(r.FilePath);

                e.Handled = true;
            }
        }


        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            // ENTER → go to first result
            if (e.Key == Key.Enter)
            {
                if (ResultsList.Items.Count > 0)
                {
                    ResultsList.Focus();
                    ResultsList.SelectedIndex = 0;

                    var item = ResultsList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                    item?.Focus();
                }

                e.Handled = true;
                return;
            }

            // ESC → clear search
            if (e.Key == Key.Escape)
            {
                SearchBox.Clear();
                e.Handled = true;
                return;
            }

            // DOWN ARROW → jump to first item
            if (e.Key == Key.Down)
            {
                if (ResultsList.Items.Count > 0)
                {
                    ResultsList.Focus();
                    ResultsList.SelectedIndex = 0;

                    var item = ResultsList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                    item?.Focus();
                }

                e.Handled = true;
                return;
            }

            // UP ARROW → jump to last item
            if (e.Key == Key.Up)
            {
                if (ResultsList.Items.Count > 0)
                {
                    int last = ResultsList.Items.Count - 1;
                    ResultsList.Focus();
                    ResultsList.SelectedIndex = last;

                    var item = ResultsList.ItemContainerGenerator.ContainerFromIndex(last) as ListBoxItem;
                    item?.Focus();
                }

                e.Handled = true;
                return;
            }
        }


        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+F → focus search box
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
        }

        private SearchResult? GetSelected()
        {
            return ResultsList.SelectedItem as SearchResult;
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var r = GetSelected();
            if (r == null || string.IsNullOrEmpty(r.FilePath)) return;

            OpenFileWithShell(r.FilePath);
        }
    }
}