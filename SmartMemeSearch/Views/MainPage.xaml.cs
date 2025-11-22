using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SmartMemeSearch;
using SmartMemeSearch.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SmartMemeSearch.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
        private bool _autoSyncStarted = false;

        public MainPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_autoSyncStarted)
                return;

            _autoSyncStarted = true;

            var vm = DataContext as MainViewModel;
            if (vm == null)
                return;

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


        private async Task RunExclusiveAutoSync(MainViewModel vm)
        {
            if (vm is null)
                return;

            // Skip if another sync is running
            if (!vm.TryBeginSync())
                return;

            // Use the UI thread dispatcher stored in the Page field
            _dispatcher.TryEnqueue(() =>
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
                _dispatcher.TryEnqueue(() =>
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
            if (sender is MenuFlyoutItem m)
                return m.Tag as SearchResult;

            if (sender is FrameworkElement fe)
            {
                if (fe.DataContext is SearchResult r)
                    return r;
            }

            return null;
        }

        private void ResultItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            var r = GetResultFromSender(sender);
            if (r == null || string.IsNullOrEmpty(r.FilePath))
                return;

            OpenFileWithShell(r.FilePath);
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
                    var file = await StorageFile.GetFileFromPathAsync(path);
                    var dp = new DataPackage();
                    dp.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
                    Clipboard.SetContent(dp);
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
                    var file = await StorageFile.GetFileFromPathAsync(path);
                    var dp = new DataPackage();
                    dp.SetStorageItems(new[] { file });
                    Clipboard.SetContent(dp);
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
                    var dp = new DataPackage();
                    dp.SetText(path);
                    Clipboard.SetContent(dp);
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


    }
}
