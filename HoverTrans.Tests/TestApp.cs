using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;
using HoverTrans.Settings;

// Every test gets a fresh app on the headless platform, and the tests run one after another (Avalonia has one UI thread).
[assembly: AvaloniaTestApplication(typeof(HoverTrans.Tests.TestApp))]
[assembly: AvaloniaTestFramework]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerTest)]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HoverTrans.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        // Keep the settings in memory: tests must never read or change the person's own settings file.
        AppSettings.Store = Settings;
        return AppBuilder.Configure<App>()
                         .UseArcApp()
                         .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }

    /// <summary>Where the app under test saves its settings.</summary>
    public static MemorySettingsStore Settings { get; } = new();
}
