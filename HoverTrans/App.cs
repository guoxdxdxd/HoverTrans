using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using HoverTrans.Core.Model;
using HoverTrans.Core.Translation;
using HoverTrans.Services;
using HoverTrans.Settings;
using HoverTrans.ViewModels;
using HoverTrans.Views;
using Microsoft.Extensions.DependencyInjection;

namespace HoverTrans;

/// <summary>
/// The application. Arc needs nothing here: no base class, no App.axaml and no styles. UseArcApp() (AppSetup.cs)
/// registers the themes, and Arc's base theme styles the window and the Avalonia controls Arc's templates use.
/// </summary>
public class App : Application
{
    /// <summary>The app's services (Services/AppServices.cs), built when the app starts.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public override void OnFrameworkInitializationCompleted()
    {
        Services = AppServices.Build();
        // Every theme change (the top bar's switch, the Settings page, your own ArcTheme.Apply calls) is saved.
        AppSettings.Current.TrackTheme();
        switch (ApplicationLifetime)
        {
            // A desktop window.
            case IClassicDesktopStyleApplicationLifetime desktop:
                // Closing the main window must end the app. With Avalonia's default (last window closed) the hidden
                // hover window kept the process alive with no window left to reach it: hover kept translating, and
                // llama-server stayed up with nothing in the UI able to stop it.
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                var viewModel = CreateMainViewModel();
                desktop.MainWindow = new MainWindow { DataContext = viewModel };
                Services.GetRequiredService<HoverWindowManager>().Initialize();
                viewModel.Initialize();
                desktop.Exit += (_, _) => Shutdown();
                break;
            // A browser page or a phone screen: the content without a window.
            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = new MainView { DataContext = CreateMainViewModel() };
                break;
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static MainViewModel CreateMainViewModel() => Services.GetRequiredService<MainViewModel>();

    private static void Shutdown()
    {
        Services.GetRequiredService<MainViewModel>().Dispose();
        Services.GetRequiredService<HoverWindowManager>().Dispose();
        _ = Services.GetRequiredService<IHoverService>().DisposeAsync();
        _ = Services.GetRequiredService<HunyuanEngine>().DisposeAsync();
    }
}