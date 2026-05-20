using System.Text.Json;

namespace MonoForge.Editor.Services;

public sealed class UserSettings
{
    public int EditorFontSize { get; set; } = 13;
    public int DefaultSnap { get; set; } = 4;
    public int AutoSaveSeconds { get; set; } = 60;
    public bool AutoSaveEnabled { get; set; } = true;
    public bool ShowGridDefault { get; set; } = true;
    public bool SnapToGridDefault { get; set; } = true;
    public string ThemeVariant { get; set; } = "Dark";
    public string EditorTheme { get; set; } = "DarkPlus";
    public List<string> RecentProjects { get; set; } = new();
    public List<string> RecentFiles { get; set; } = new();

    public void PushRecentProject(string path)
    {
        RecentProjects.Remove(path);
        RecentProjects.Insert(0, path);
        if (RecentProjects.Count > 8) RecentProjects.RemoveRange(8, RecentProjects.Count - 8);
        Save();
    }

    public void PushRecentFile(string path)
    {
        RecentFiles.Remove(path);
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 12) RecentFiles.RemoveRange(12, RecentFiles.Count - 12);
        Save();
    }

    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".monoforge", "settings.json");

    public static UserSettings Current { get; private set; } = Load();

    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new UserSettings();
            var json = File.ReadAllText(Path);
            return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            Current = this;
            Changed?.Invoke();
        }
        catch { /* ignored */ }
    }

    public static event Action? Changed;
}
