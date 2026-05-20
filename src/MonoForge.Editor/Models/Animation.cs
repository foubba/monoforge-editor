namespace MonoForge.Editor.Models;

public sealed class AnimationClip
{
    public string Name { get; set; } = "idle";
    public string TexturePath { get; set; } = "";
    public int FrameWidth { get; set; } = 32;
    public int FrameHeight { get; set; } = 32;
    public int Fps { get; set; } = 12;
    public bool Loop { get; set; } = true;
    public List<int> Frames { get; set; } = new();
}
