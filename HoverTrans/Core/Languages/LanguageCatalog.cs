namespace HoverTrans.Core.Languages;

/// <summary>The languages offered by the first version, and the mapping to model prompt names.</summary>
public static class LanguageCatalog
{
    /// <summary>Value used for "let the model detect the source language".</summary>
    public const string AutoCode = "auto";

    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new("zh-CN", "中文（简体）", "Simplified Chinese"),
        new("zh-TW", "中文（繁體）", "Traditional Chinese"),
        new("en", "英语", "English"),
        new("ja", "日语", "Japanese"),
        new("ko", "韩语", "Korean"),
        new("fr", "法语", "French"),
        new("de", "德语", "German"),
        new("es", "西班牙语", "Spanish"),
        new("ru", "俄语", "Russian"),
        new("pt", "葡萄牙语", "Portuguese"),
        new("it", "意大利语", "Italian"),
        new("ar", "阿拉伯语", "Arabic"),
        new("th", "泰语", "Thai"),
        new("vi", "越南语", "Vietnamese"),
        new("id", "印尼语", "Indonesian"),
        new("tr", "土耳其语", "Turkish"),
        new("nl", "荷兰语", "Dutch"),
        new("pl", "波兰语", "Polish"),
        new("uk", "乌克兰语", "Ukrainian"),
        new("ms", "马来语", "Malay"),
        new("hi", "印地语", "Hindi")
    ];

    /// <summary>Source-language list: "auto" first, then every language.</summary>
    public static IReadOnlyList<LanguageOption> Sources { get; } =
        [new(AutoCode, "自动检测", "auto"), .. All];

    public static LanguageOption Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code == AutoCode)
        {
            return Sources[0];
        }

        return All.FirstOrDefault(language =>
                   string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase))
               ?? All[0];
    }

    public static string PromptName(string? code) => Find(code).PromptName;
}