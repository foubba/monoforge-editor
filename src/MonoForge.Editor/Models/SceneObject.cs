using System.Text.Json.Serialization;

namespace MonoForge.Editor.Models;

public sealed class SceneObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Sprite";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 64;
    public double Height { get; set; } = 64;
    public string Color { get; set; } = "#65a7ff";
    public int Layer { get; set; }
    public bool Visible { get; set; } = true;

    public string TexturePath { get; set; } = "";
    public int SourceX { get; set; }
    public int SourceY { get; set; }
    public int SourceW { get; set; }
    public int SourceH { get; set; }
    public string TilemapPath { get; set; } = "";
    public string ModelPath { get; set; } = "";
    public bool Locked { get; set; }
    public double Rotation { get; set; }   // degrees
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    public double PivotX { get; set; } = 0.5; // 0..1 relative
    public double PivotY { get; set; } = 0.5;
    public int SliceLeft { get; set; }
    public int SliceRight { get; set; }
    public int SliceTop { get; set; }
    public int SliceBottom { get; set; }

    public List<SceneObject> Children { get; set; } = [];
    public List<ComponentData> Components { get; set; } = [];

    [JsonIgnore]
    public SceneObject? Parent { get; set; }
}

public sealed class ComponentData
{
    public string Kind { get; set; } = "";
    public string Source { get; set; } = "";
    public Dictionary<string, string> Properties { get; set; } = new();
}
