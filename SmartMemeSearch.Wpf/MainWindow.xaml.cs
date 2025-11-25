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
#if MS_STORE_FREE_WITH_ADDS
            StoreService.NotifyWindowReady();
#endif
            Loaded += Page_Loaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            if (vm == null)
                return;

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

        private void RemoveAdds_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            if (vm == null)
                return;
            _ = vm.RemoveAdsAsync();
        }

        // TODO
        /*
        private void ResultItem_DoubleTapped(object sender, MouseDoubleClickEventArgs e)
        {
            var r = GetResultFromSender(sender);
            if (r == null || string.IsNullOrEmpty(r.FilePath))
                return;

            OpenFileWithShell(r.FilePath);
        }
        */

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

        // TODO reintroduce Keyboard functionality
        /*
        private void Open_KA_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (ResultsList.SelectedItem is SearchResult r)
                OpenFileWithShell(r.FilePath);

            args.Handled = true;
        }

        private async void CopyImage_KA_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (ResultsList.SelectedItem is SearchResult r)
            {
                if (File.Exists(r.FilePath))
                {
                    var image = new BitmapImage();
                    using (var stream = File.OpenRead(r.FilePath))
                    {
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = stream;
                        image.EndInit();
                    }

                    Clipboard.SetImage(image);
                }
            }
            args.Handled = true;
        }
        

        private void CopyPath_KA_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (ResultsList.SelectedItem is SearchResult r)
            {
                Clipboard.SetText(r.FilePath);
            }
            args.Handled = true;
        }

        private void FocusSearch_KA_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SearchBox.Focus(FocusState.Programmatic);
            SearchBox.SelectAll(); // optional
            args.Handled = true;
        }
        private void ClearSearch_KA_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SearchBox.Text = "";
            args.Handled = true;
        }

        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (ResultsList.Items.Count == 0)
                return;

            // Down arrow → select first item
            if (e.Key == Windows.System.VirtualKey.Down)
            {
                ResultsList.SelectedIndex = 0;
                ResultsList.Focus(FocusState.Programmatic);
                e.Handled = true;
                return;
            }

            // Up arrow → select last item
            if (e.Key == Windows.System.VirtualKey.Up)
            {
                ResultsList.SelectedIndex = ResultsList.Items.Count - 1;
                ResultsList.Focus(FocusState.Programmatic);
                e.Handled = true;
                return;
            }

            // Enter: focus and select first item
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (ResultsList.Items.Count > 0)
                {
                    ResultsList.SelectedIndex = 0;
                    ResultsList.Focus(FocusState.Programmatic);
                }

                e.Handled = true;
            }
        }*/
    }
}