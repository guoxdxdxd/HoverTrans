namespace HoverTrans.Navigation;

/// <summary>
/// The two kinds of navigation the pages ask for without knowing about the shell: show another page, or open the
/// translate page with a piece of text in it (the history page's "translate this again"). The shell view model
/// listens and does the work; a page never holds a reference to the shell.
/// </summary>
public interface IUiNavigation
{
    /// <summary>Raised with a page key from <see cref="AppPages"/>.</summary>
    event EventHandler<string>? NavigateRequested;

    /// <summary>Raised with text to put in the translate page and show it.</summary>
    event EventHandler<string>? TranslateRequested;

    /// <summary>Asks the shell to show <paramref name="key"/>.</summary>
    void Navigate(string key);

    /// <summary>Asks the shell to translate <paramref name="text"/> now.</summary>
    void Translate(string text);
}

/// <summary>The single instance the pages talk to (registered in <see cref="Services.AppServices"/>).</summary>
public sealed class UiNavigation : IUiNavigation
{
    public event EventHandler<string>? NavigateRequested;

    public event EventHandler<string>? TranslateRequested;

    public void Navigate(string key) => NavigateRequested?.Invoke(this, key);

    public void Translate(string text) => TranslateRequested?.Invoke(this, text);
}

/// <summary>The pages of the app: their sidebar keys and the title and subtitle the top bar shows.</summary>
public sealed record AppPage(string Key, string Title, string Subtitle);

/// <summary>The page keys, in sidebar order.</summary>
public static class AppPages
{
    public const string Translate = "translate";

    public const string History = "history";

    public const string Settings = "settings";

    public static IReadOnlyList<AppPage> All { get; } =
    [
        new(Translate, "翻译", "悬停即译，也可以粘贴文字或图片"),
        new(History, "历史", "最近翻过的内容，点一下就能回到原文"),
        new(Settings, "设置", "模型、悬停取词与外观")
    ];

    public static AppPage? Find(string? key) =>
        All.FirstOrDefault(page => string.Equals(page.Key, key, StringComparison.Ordinal));
}
