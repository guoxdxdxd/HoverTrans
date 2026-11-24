using HoverTrans.Core.History;
using HoverTrans.Core.Model;
using HoverTrans.Core.Platform;
using HoverTrans.Core.Translation;
using HoverTrans.Navigation;
using HoverTrans.Platform;
using HoverTrans.Settings;
using HoverTrans.ViewModels;
using Microsoft.Extensions.DependencyInjection;
#if WINDOWS
using HoverTrans.Platform.Windows;
#endif

namespace HoverTrans.Services;

/// <summary>
/// The app's services. Register a service here and take it as a constructor parameter of a view model; the container
/// creates the view models (App.cs for the shell). Everything is registered with its type written out, so the
/// container needs no reflection over the assembly and the app can be trimmed.
/// </summary>
public static class AppServices
{
    /// <summary>
    /// Builds the container. <paramref name="history"/> is where the history page reads and writes: the default file,
    /// or a temporary file for tests and screenshots that must not touch the person's own app data.
    /// </summary>
    public static IServiceProvider Build(HistoryStore? history = null)
    {
        var services = new ServiceCollection();
        var settings = AppSettings.Current;

        services.AddSingleton(new LlamaServerOptions(
            settings.ServerPath,
            settings.ModelPath,
            settings.Port,
            ContextSize: 4096,
            GpuLayers: 99,
            IdleTimeout: TimeSpan.FromMinutes(Math.Clamp(settings.IdleMinutes, 1, 120))));

        services.AddSingleton<LlamaServerManager>();
        services.AddSingleton<LlamaTranslationClient>();
        services.AddSingleton<ITranslationService, TranslationService>();

#if WINDOWS
        services.AddSingleton<IOcrService, WindowsOcrService>();
        services.AddSingleton<IHoverTextSource, WindowsHoverTextSource>();
#else
        services.AddSingleton<IOcrService, NullOcrService>();
        services.AddSingleton<IHoverTextSource, NullHoverTextSource>();
#endif

        services.AddSingleton<IHoverService, HoverTranslationService>();
        services.AddSingleton<HoverWindowManager>();
        services.AddSingleton(history ?? new HistoryStore());
        services.AddSingleton<IUiNavigation, UiNavigation>();

        // Page state is shared (the shell and the settings page both show hover, the shell counts history), so the
        // pages and their view models live as long as the app.
        services.AddSingleton<AppState>();
        services.AddSingleton<TranslatePageViewModel>();
        services.AddSingleton<HistoryPageViewModel>();
        services.AddSingleton<SettingsPageViewModel>();
        services.AddSingleton<MainViewModel>();
        return services.BuildServiceProvider();
    }
}
