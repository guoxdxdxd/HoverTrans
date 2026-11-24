using System.Text.Json;
using System.Text.Json.Serialization;

namespace HoverTrans.Core.History;

/// <summary>
/// Keeps the recent translations in a JSON file next to the settings. Reading and writing are best effort: a damaged
/// or unreadable history must never stop the app. The newest entry comes first and the file keeps at most
/// <see cref="Capacity"/> entries.
/// </summary>
public sealed class HistoryStore
{
    /// <summary>How many entries the file keeps; older ones are dropped when a new one is added.</summary>
    public const int Capacity = 120;

    /// <summary>The default file: <c>&lt;application data&gt;/HoverTrans/history.json</c>.</summary>
    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "HoverTrans",
        "history.json");

    private readonly string _path;

    public HistoryStore(string? path = null) => _path = path ?? DefaultPath;

    /// <summary>Raised after an entry is added or the history is cleared, so an open page can refresh.</summary>
    public event EventHandler? Changed;

    /// <summary>The file the entries are kept in.</summary>
    public string FilePath => _path;

    /// <summary>Reads the entries, newest first; a missing, empty or damaged file gives an empty list.</summary>
    public IReadOnlyList<HistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var entries = JsonSerializer.Deserialize(File.ReadAllText(_path), HistoryJsonContext.Default.ListHistoryEntry);
            return entries is null ? [] : [.. entries.Where(IsUsable).OrderByDescending(entry => entry.CreatedAt)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Writes the entries (at most <see cref="Capacity"/>), newest first. Failures are ignored.</summary>
    public void Save(IEnumerable<HistoryEntry> entries)
    {
        try
        {
            var kept = entries.Where(IsUsable)
                              .OrderByDescending(entry => entry.CreatedAt)
                              .Take(Capacity)
                              .ToList();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(kept, HistoryJsonContext.Default.ListHistoryEntry));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Clears the history file; the next <see cref="Load"/> is empty.</summary>
    public void Clear()
    {
        Save([]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Puts <paramref name="entry"/> at the top. The same text translated into the same language again replaces the
    /// older row instead of piling up duplicates, which is what a person expects when they re-run a translation.
    /// </summary>
    public void Append(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var entries = Load()
            .Where(existing => !(string.Equals(existing.SourceText, entry.SourceText, StringComparison.Ordinal)
                                 && string.Equals(existing.TargetLanguage, entry.TargetLanguage, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        entries.Insert(0, entry);
        Save(entries);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsUsable(HistoryEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.SourceText) && !string.IsNullOrWhiteSpace(entry.ResultText);
}

/// <summary>The history file's serializer, generated at build time (no reflection, trimming and AOT safe).</summary>
[JsonSourceGenerationOptions(WriteIndented = false, UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<HistoryEntry>))]
internal sealed partial class HistoryJsonContext : JsonSerializerContext
{
}
