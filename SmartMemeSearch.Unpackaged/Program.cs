using System;
using Microsoft.UI.Xaml;
using WinRT;

namespace SmartMemeSearch.Unpackaged;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // No manual Bootstrap.Init – auto-init handles it
        ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ => new SmartMemeSearch.App());
    }
}
