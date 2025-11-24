using Microsoft.UI.Xaml;

namespace SmartMemeSearch;

public partial class App : Application
{
    public static Window? Window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
    }
}