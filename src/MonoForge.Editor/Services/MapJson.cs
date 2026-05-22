using System.Text.Json;
using MonoForge.Editor.Models;

namespace MonoForge.Editor.Services;

/// <summary>JSON load/save for <see cref="MapDocument"/>. .mfmap is just pretty-printed JSON.</summary>
public static class MapJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(MapDocument map) => JsonSerializer.Serialize(map, Options);

    public static MapDocument Deserialize(string json)
        => JsonSerializer.Deserialize<MapDocument>(json, Options) ?? new MapDocument();

    public static MapDocument Load(string path)
    {
        var text = File.ReadAllText(path);
        return Deserialize(text);
    }

    public static void Save(string path, MapDocument map)
    {
        File.WriteAllText(path, Serialize(map));
    }

    /// <summary>Convenience factory for the "New Map" flow: empty 3D scene with a couple of default entities.</summary>
    public static MapDocument CreateTemplate3D(string name)
    {
        var doc = new MapDocument { Name = name, Mode = "3D" };
        doc.Entities.Add(new MapEntity
        {
            Name = "Sun",
            Type = "Light",
            LightType = "directional",
            Position = new[] { 4f, 8f, 4f },
            Rotation = new[] { -50f, 30f, 0f },
            Color = "#fff4e0",
            LightIntensity = 1.1f,
        });
        doc.Entities.Add(new MapEntity
        {
            Name = "Main Camera",
            Type = "Camera",
            Position = new[] { 0f, 4f, 8f },
            Rotation = new[] { -20f, 0f, 0f },
        });
        return doc;
    }
}
