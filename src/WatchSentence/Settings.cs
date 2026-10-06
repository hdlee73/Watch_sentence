using System.Text.Json;

namespace WatchSentence;

public sealed class Settings
{
    public bool Use24Hour { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public bool KoreanWeekday { get; set; } = true;
    public string Theme { get; set; } = "Paper";
    public string Background { get; set; } = "#F2EFE6";
    public string Foreground { get; set; } = "#2B2B2B";
    public double Opacity { get; set; } = 1.0;
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int Width { get; set; } = 460;
    public int Height { get; set; } = 220;

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WatchSentence");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            // A corrupt settings file should never stop the clock; fall back to defaults.
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}

public sealed record ThemePreset(string Name, string Label, string Background, string Foreground);

public static class Themes
{
    // Low-contrast, warm "e-ink" palettes.
    public static readonly ThemePreset[] All =
    {
        new("Paper", "Paper (e-ink 밝게)", "#F2EFE6", "#2B2B2B"),
        new("Kindle", "Kindle (회색 종이)", "#E6E4DE", "#1F1F1F"),
        new("Sepia", "Sepia (세피아)", "#F4ECD8", "#5B4636"),
        new("Slate", "Slate (e-ink 어둡게)", "#2E2E2C", "#D9D6CC"),
        new("Ink", "Ink (검정)", "#121212", "#C8C8C8"),
    };
}
