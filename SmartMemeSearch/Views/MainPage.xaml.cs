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

        public MainPage()
        {
            InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            var vm = (MainViewModel)DataContext;

            vm.IsImporting = true;
            vm.CurrentFile = "Checking folders...";
            vm.ProgressValue = 0;

            _ = Task.Run(async () =>
            {
                await vm.AutoSyncAllAsync();

                _dispatcher.TryEnqueue(() =>
                {
                    vm.IsImporting = false;
                    if (!string.IsNullOrWhiteSpace(vm.Query))
                        vm.Search();

                });
            });


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
