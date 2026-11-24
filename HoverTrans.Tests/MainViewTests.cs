using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using HoverTrans.ViewModels;
using HoverTrans.Views;
using TKCode.Arc;

namespace HoverTrans.Tests;

public class MainViewTests
{
    private static MainWindow ShowMainWindow()
    {
        var window = new MainWindow { DataContext = App.Services.GetRequiredService<MainViewModel>() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Main_window_shows_the_main_view()
    {
        var window = ShowMainWindow();

        var view = window.GetVisualDescendants().OfType<MainView>().SingleOrDefault();
        Assert.NotNull(view);
        Assert.NotEmpty(view.GetVisualChildren());
    }

    [AvaloniaFact]
    public void A_theme_change_is_saved()
    {
        ShowMainWindow();

        ArcTheme.Apply(t => t.Appearance(ArcAppearance.Dark));
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("\"Dark\"", TestApp.Settings.Json);
    }
}
