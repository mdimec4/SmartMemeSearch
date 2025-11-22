using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SmartMemeSearch.ViewModels;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
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





        private async void CopyImage_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button btn &&
                btn.DataContext is SmartMemeSearch.SearchResult r &&
                File.Exists(r.FilePath))
            {
                var dp = new DataPackage();
                dp.SetBitmap(RandomAccessStreamReference.CreateFromFile(
                    await Windows.Storage.StorageFile.GetFileFromPathAsync(r.FilePath)
                ));

                Clipboard.SetContent(dp);
            }
        }
    }
}
