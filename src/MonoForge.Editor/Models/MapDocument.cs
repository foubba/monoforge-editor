using System.Numerics;

namespace MonoForge.Editor.Models;

/// <summary>
/// Top-level container for a .mfmap document — a unified format that can express 2D
/// scenes, 3D scenes, and UI screens (we ship 3D first). Designed to be JSON-friendly
/// and round-trip cleanly through System.Text.Json.
/// </summary>
public sealed class MapDocument
{
    public string Name { get; set; } = "Untitled Map";
    /// <summary>"3D", "2D", or "UI". Drives which editor view opens.</summary>
    public string Mode { get; set; } = "3D";
    public MapCamera Camera { get; set; } = new();
    public List<MapEntity> Entities { get; set; } = new();
}

/// <summary>Default editor camera state, also used by the runtime as the starting view.</summary>
public sealed class MapCamera
{
    public float[] Position { get; set; } = { 0f, 6f, 12f };
    public float[] Target { get; set; } = { 0f, 0f, 0f };
    public float FovDegrees { get; set; } = 60f;
    public Vector3 GetPosition() => new(Position[0], Position[1], Position[2]);
    public Vector3 GetTarget() => new(Target[0], Target[1], Target[2]);
    public void SetPosition(Vector3 v) { Position = new[] { v.X, v.Y, v.Z }; }
    public void SetTarget(Vector3 v) { Target = new[] { v.X, v.Y, v.Z }; }
}

/// <summary>One placeable in the map. Type drives what fields downstream consumers read.</summary>
public sealed class MapEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Entity";
    /// <summary>"Empty" | "Model3D" | "Sprite" | "Light" | "Camera" | "Trigger" | "Text"</summary>
    public string Type { get; set; } = "Empty";

    /// <summary>World-space position, [x, y, z].</summary>
    public float[] Position { get; set; } = { 0f, 0f, 0f };
    /// <summary>Euler XYZ in degrees.</summary>
    public float[] Rotation { get; set; } = { 0f, 0f, 0f };
    /// <summary>Per-axis scale.</summary>
    public float[] Scale { get; set; } = { 1f, 1f, 1f };

    // Type-specific fields (only populated when relevant). Kept as plain strings/floats
    // so the JSON stays human-editable; we don't need a discriminated union for V1.
    public string? ModelPath { get; set; }        // Model3D
    public string? TexturePath { get; set; }      // Sprite
    public string? Text { get; set; }             // Text
    public string Color { get; set; } = "#ffffff";

    public string TriggerShape { get; set; } = "box"; // Trigger: "box" | "sphere"
    public float[] TriggerSize { get; set; } = { 2f, 2f, 2f };

    public string LightType { get; set; } = "directional"; // Light: "directional" | "point" | "spot"
    public float LightIntensity { get; set; } = 1f;

    // Convenience accessors so editor code reads more naturally.
    public Vector3 PositionVec
    {
        get => new(Position[0], Position[1], Position[2]);
        set { Position = new[] { value.X, value.Y, value.Z }; }
    }
    public Vector3 RotationVec
    {
        get => new(Rotation[0], Rotation[1], Rotation[2]);
        set { Rotation = new[] { value.X, value.Y, value.Z }; }
    }
    public Vector3 ScaleVec
    {
        get => new(Scale[0], Scale[1], Scale[2]);
        set { Scale = new[] { value.X, value.Y, value.Z }; }
    }
}
