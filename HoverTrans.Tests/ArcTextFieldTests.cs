using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace HoverTrans.Tests;

/// <summary>Arc ships a text field of its own; the plain Avalonia TextBox has no theme here and renders nothing.</summary>
public class ArcTextFieldTests
{
    [AvaloniaFact]
    public void The_arc_textarea_renders_its_text()
    {
        var textarea = new TKCode.Arc.Textarea { Text = "Hello world" };
        var window = new Window { Content = textarea };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var descendants = textarea.GetVisualDescendants().ToList();
        Assert.True(descendants.OfType<TextPresenter>().Any(),
            "no TextPresenter; visual children: " + string.Join(", ", descendants.Select(v => v.GetType().Name)));
    }

    [AvaloniaFact]
    public void A_plain_text_box_does_not_render_here()
    {
        var box = new TextBox { Text = "Hello world" };
        var window = new Window { Content = box };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Documents the reason the page uses Arc's text field: no ControlTheme for Avalonia's TextBox.
        Assert.Empty(box.GetVisualDescendants());
    }
}
