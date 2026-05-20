namespace MonoForge.Editor.Models;

public sealed class ParticleSystem
{
    public string Name { get; set; } = "particles";
    public double SpawnRate { get; set; } = 30;
    public double Lifetime { get; set; } = 1.5;
    public double SpeedMin { get; set; } = 40;
    public double SpeedMax { get; set; } = 120;
    public double AngleSpread { get; set; } = 360;
    public double AngleStart { get; set; } = 0;
    public double GravityX { get; set; } = 0;
    public double GravityY { get; set; } = 80;
    public double SizeStart { get; set; } = 4;
    public double SizeEnd { get; set; } = 12;
    public string ColorStart { get; set; } = "#ffd166";
    public string ColorEnd { get; set; } = "#ff6b6b";
    public double AlphaStart { get; set; } = 1.0;
    public double AlphaEnd { get; set; } = 0.0;
}
