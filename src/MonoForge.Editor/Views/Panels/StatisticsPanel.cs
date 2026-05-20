using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MonoForge.Editor.Models;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views.Panels;

public sealed class StatisticsPanel : UserControl
{
    private readonly TextBlock _content = new();

    public StatisticsPanel()
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("28,*"),
            Background = Brush(PanelBackground)
        };
        grid.Children.Add(Text("Statistics", TextMuted).WithMargin(12, 6, 10, 0).At(row: 0));
        _content.FontFamily = new FontFamily("Menlo");
        _content.FontSize = 11;
        _content.Foreground = Brush(TextSecondary);
        _content.Padding = new Thickness(12, 2, 12, 8);
        _content.TextWrapping = TextWrapping.NoWrap;
        grid.Children.Add(_content.At(row: 1));
        Content = BorderBox(grid, BorderColor, 0, 1, 0, 0);
    }

    public void Update(SceneDocument scene)
    {
        var all = scene.Flatten().ToList();
        var visible = all.Count(o => o.Visible);
        var locked = all.Count(o => o.Locked);
        var layers = all.Select(o => o.Layer).Distinct().Count();
        var textures = all.Select(o => o.TexturePath).Where(p => !string.IsNullOrEmpty(p)).Distinct().Count();
        var anim = all.Count(o => o.Components.Any(c => c.Kind == "Animation"));
        var coll = all.Count(o => o.Components.Any(c => c.Kind == "Collision"));
        var script = all.Count(o => o.Components.Any(c => c.Kind == "Script"));

        var bbox = "—";
        if (all.Count > 0)
        {
            var minX = all.Min(o => o.X);
            var minY = all.Min(o => o.Y);
            var maxX = all.Max(o => o.X + o.Width);
            var maxY = all.Max(o => o.Y + o.Height);
            bbox = $"{maxX - minX:0}×{maxY - minY:0}";
        }

        _content.Text =
            $"objects   {all.Count}\n" +
            $"visible   {visible}\n" +
            $"locked    {locked}\n" +
            $"layers    {layers}\n" +
            $"textures  {textures}\n" +
            $"bbox      {bbox}\n" +
            $"\n" +
            $"animation {anim}\n" +
            $"collision {coll}\n" +
            $"scripts   {script}";
    }
}
