using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using HoverTrans.ViewModels;
using HoverTrans.Views;
using HoverTrans.Views.Pages;
using Xunit;

namespace HoverTrans.Tests;

/// <summary>Typing into the composer is the page's whole job; it must reach the view model, keystroke by keystroke.</summary>
public class TranslatePageInputTests
{
    private static (MainWindow Window, MainViewModel ViewModel, TranslatePage Page, TextBox Box) ShowTranslatePage()
    {
        var window = new MainWindow { DataContext = App.Services.GetRequiredService<MainViewModel>() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = Assert.IsType<MainViewModel>(window.DataContext);
        var page = Assert.Single(window.GetVisualDescendants().OfType<TranslatePage>());
        // The page holds two text boxes: the composer and the read-only result.
        var box = Assert.Single(page.GetVisualDescendants().OfType<TextBox>(), candidate => candidate.Classes.Contains("composer"));
        return (window, viewModel, page, box);
    }

    [AvaloniaFact]
    public void Typing_into_the_composer_reaches_the_view_model()
    {
        var (window, viewModel, _, box) = ShowTranslatePage();

        box.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(box.IsFocused, "the composer did not take focus");

        window.KeyTextInput("hello");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("hello", box.Text);
        Assert.Equal("hello", viewModel.Translate.SourceText);
    }
}
