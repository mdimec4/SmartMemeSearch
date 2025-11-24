using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SmartMemeSearch
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    [SupportedOSPlatform("windows10.0.17763.0")]
    public sealed partial class MainWindow : Window
    {
        public static bool IsPackaged =>
            Windows.ApplicationModel.Package.Current != null;
        public MainWindow()
        {
            InitializeComponent();

            string iconPath = Path.Combine(IsPackaged ? Windows.ApplicationModel.Package.Current.InstalledLocation.Path : AppContext.BaseDirectory, "Assets", "smile.ico");
            this.AppWindow.SetIcon(iconPath);


            this.Content = new Views.MainPage();
        }
    }
}
