using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using HoverTrans.Services;
using HoverTrans.ViewModels;
using HoverTrans.Views;
using HoverTrans.Views.Pages;
using Xunit;

namespace HoverTrans.Tests;

/// <summary>
/// The page has to look like something: the Arc theme ships no ControlTheme for Avalonia's TextBox, so a bare TextBox
/// takes keystrokes and paints nothing (that is what made the composer and the translation look empty). These tests
/// keep real, rendered controls in place, at a size a person can type in and read.
/// </summary>
public class TranslatePageLayoutTests
{
    private static (MainWindow Window, MainViewModel ViewModel, TranslatePage Page) ShowPage()
    {
        var window = new MainWindow
        {
            DataContext = App.Services.GetRequiredService<MainViewModel>(),
            Width = 1240,
            Height = 820
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = Assert.IsType<MainViewModel>(window.DataContext);
        var page = Assert.Single(window.GetVisualDescendants().OfType<TranslatePage>());
        return (window, viewModel, page);
    }

    [AvaloniaFact]
    public void The_composer_is_a_rendered_field_a_person_can_type_in()
    {
        var (_, _, page) = ShowPage();
        var composer = Assert.Single(page.GetVisualDescendants().OfType<TKCode.Arc.Textarea>());

        Assert.Contains("composer", composer.Classes);
        Assert.NotEmpty(composer.GetVisualDescendants().OfType<TextPresenter>());
        Assert.True(composer.Bounds.Height >= 100, $"the composer is only {composer.Bounds.Height} px tall");
        Assert.True(composer.Bounds.Height <= 340, $"the composer is {composer.Bounds.Height} px tall with no text");
        Assert.True(composer.Bounds.Width > 200, $"the composer is only {composer.Bounds.Width} px wide");
    }

    [AvaloniaFact]
    public void The_translation_is_drawn_in_a_rendered_control()
    {
        var (_, viewModel, page) = ShowPage();

        viewModel.Translate.SourceText = "Hello world";
        viewModel.Translate.ResultText = "你好，世界";
        Dispatcher.UIThread.RunJobs();

        var result = Assert.Single(page.GetVisualDescendants().OfType<SelectableTextBlock>(), block => block.Classes.Contains("result"));
        Assert.True(result.IsVisible);
        Assert.Equal("你好，世界", result.Text);
        Assert.True(result.Bounds.Height > 10, $"the result is only {result.Bounds.Height} px tall");
        Assert.True(result.Bounds.Width > 100, $"the result is only {result.Bounds.Width} px wide");
    }

    [AvaloniaFact]
    public void Nothing_on_the_page_or_in_the_popup_uses_a_plain_text_box()
    {
        var (window, _, page) = ShowPage();
        var popup = new HoverWindow();
        popup.Show();
        popup.Apply(new HoverSnapshot("Hello", "你好", "翻译完成", false, false, 100, 100));
        Dispatcher.UIThread.RunJobs();

        var plain = page.GetVisualDescendants().OfType<TextBox>()
            .Concat(popup.GetVisualDescendants().OfType<TextBox>())
            .Where(box => box is not TKCode.Arc.FieldTextBox)
            .Select(box => box.GetType().Name)
            .ToList();

        Assert.Empty(plain);
    }

    /// <summary>A pasted 200-line document must not push the page past the window: the composer stops growing at its
    /// max lines and scrolls inside itself, and so does the translation card.</summary>
    [AvaloniaFact]
    public void A_very_long_text_scrolls_inside_the_cards_instead_of_growing_the_page()
    {
        var (window, viewModel, page) = ShowPage();
        var composer = Assert.Single(page.GetVisualDescendants().OfType<TKCode.Arc.Textarea>());

        var oneLine = composer.Bounds.Height;
        Assert.True(oneLine <= 160, $"an empty composer is already {oneLine} px tall");

        var longText = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"Line {i}: the quick brown fox."));
        viewModel.Translate.SourceText = longText;
        viewModel.Translate.ResultText = longText;
        Dispatcher.UIThread.RunJobs();

        Assert.True(composer.Bounds.Height > oneLine, "the composer did not grow with the text");
        Assert.True(composer.Bounds.Height <= 340, $"the composer grew to {composer.Bounds.Height} px");
        Assert.True(page.Bounds.Height <= window.ClientSize.Height,
            $"the page is {page.Bounds.Height} px tall in a {window.ClientSize.Height} px window");

        var composerScroll = Assert.Single(composer.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.True(composerScroll.Extent.Height > composerScroll.Viewport.Height + 100,
            "the composer does not scroll its own text");

        var result = Assert.Single(page.GetVisualDescendants().OfType<SelectableTextBlock>(), block => block.Classes.Contains("result"));
        var resultScroll = Assert.Single(result.GetVisualAncestors().OfType<ScrollViewer>());
        Assert.True(resultScroll.Extent.Height > resultScroll.Viewport.Height + 100,
            "the translation card does not scroll its own text");
    }
}
