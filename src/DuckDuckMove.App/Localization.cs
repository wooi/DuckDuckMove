using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DuckDuckMove.App;

internal static class Localization
{
    internal static readonly string[] Codes = ["zh-CN", "zh-TW", "en", "ja", "es", "pt"];
    internal static readonly string[] Names = ["简体中文", "繁體中文", "English", "日本語", "Español", "Português"];
    private static readonly Dictionary<string, string[]> Catalog = LoadCatalog();
    internal static string Preference { get; private set; } = "system";
    internal static string Language { get; private set; } = "en";
    internal static event Action? Changed;
    internal static IEnumerable<string> Keys => Catalog.Keys;
    private static string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuckDuckMove", "preferences.json");
    internal static void UseTestSettings(string path) => SettingsPath = Path.GetFullPath(path);
    private static Dictionary<string, string[]> LoadCatalog()
    {
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream("DuckDuckMove.Translations")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r').Split('¦')).ToDictionary(parts => parts[0], parts => parts[1..]);
    }
    internal static string Match(string culture)
    {
        string name = culture.ToLowerInvariant();
        if (name == "zh" || name.StartsWith("zh-"))
            return name.Contains("hant") || name is "zh-tw" or "zh-hk" or "zh-mo" ? "zh-TW" : "zh-CN";
        return Codes.FirstOrDefault(c => name == c || name.StartsWith(c + "-")) ?? "en";
    }
    private static string SystemLanguage()
    {
        try { return Match(CultureInfo.GetCultureInfo(GetUserDefaultUILanguage()).Name); }
        catch (CultureNotFoundException) { return "en"; }
    }
    internal static void Initialize(bool isolated = false)
    {
        string preference = "system";
        if (!isolated)
        {
            try
            {
                if (File.Exists(SettingsPath)) preference = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath))?.GetValueOrDefault("language") ?? "system";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        }
        Select(preference, false);
    }
    internal static bool Select(string preference, bool save)
    {
        Preference = preference == "system" || Codes.Contains(preference) ? preference : "system";
        Language = Preference == "system" ? SystemLanguage() : Preference;
        bool saved = true;
        if (save)
        {
            string temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(temporary, JsonSerializer.Serialize(new { language = Preference }));
                File.Move(temporary, SettingsPath, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { saved = false; }
            finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        Changed?.Invoke();
        return saved;
    }
    internal static string T(string source)
    {
        if (Language == "zh-CN") return source;
        if (Catalog.TryGetValue(source, out var translations)) return translations[Array.IndexOf(Codes, Language) - 1];
        // Broker and system exceptions may contain several owned messages plus OS diagnostics.
        foreach (var key in Catalog.Keys.OrderByDescending(k => k.Length))
            if (source.Contains(key, StringComparison.Ordinal)) source = source.Replace(key, Catalog[key][Array.IndexOf(Codes, Language) - 1], StringComparison.Ordinal);
        return source;
    }
    internal static string Format(string source, params object[] args) => string.Format(CultureInfo.GetCultureInfo(Language), T(source), args);
    internal static void Validate()
    {
        foreach (var (key, values) in Catalog)
        {
            if (values.Length != 5 || values.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Incomplete translation: " + key);
            var expected = System.Text.RegularExpressions.Regex.Matches(key, @"\{\d+(?::[^}]+)?\}").Select(m => m.Value).Order().ToArray();
            foreach (var value in values)
                if (!expected.SequenceEqual(System.Text.RegularExpressions.Regex.Matches(value, @"\{\d+(?::[^}]+)?\}").Select(m => m.Value).Order())) throw new InvalidDataException("Placeholder mismatch: " + key);
        }
    }
    [DllImport("kernel32.dll")] private static extern ushort GetUserDefaultUILanguage();
}
