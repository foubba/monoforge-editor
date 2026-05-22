using System.Text.Json;
using MonoForge.Editor.Models;

namespace MonoForge.Editor.Services;

public sealed class ProjectWorkspace
{
    public string LastScenePath { get; set; } = "";
    public double Zoom { get; set; } = 1;
    public double CameraX { get; set; } = 90;
    public double CameraY { get; set; } = 78;
    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public SceneDocument? Scene { get; set; }
    /// <summary>Document-tab keys (file paths, or the special "__scene__" sentinel) that
    /// were open at the time the workspace was last saved. Persisted so the editor reopens
    /// the same files on the next launch — Defold / VS Code / Cursor all behave this way
    /// and losing tabs across restarts is one of the most annoying parts of bespoke editors.</summary>
    public List<string> OpenTabs { get; set; } = new();
    /// <summary>The key of the tab that was focused when the workspace was saved.</summary>
    public string ActiveTabKey { get; set; } = "";

    public const string FileName = "monoforge.json";

    public static string PathFor(string projectRoot) => Path.Combine(projectRoot, FileName);

    public static ProjectWorkspace Load(string projectRoot)
    {
        var path = PathFor(projectRoot);
        if (!File.Exists(path))
        {
            return new ProjectWorkspace();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProjectWorkspace>(json) ?? new ProjectWorkspace();
        }
        catch
        {
            return new ProjectWorkspace();
        }
    }

    public void Save(string projectRoot)
    {
        var path = PathFor(projectRoot);
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            /* ignored */
        }
    }
}
