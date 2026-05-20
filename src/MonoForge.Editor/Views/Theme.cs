using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MonoForge.Editor.Views;

internal static class Theme
{
    public const string EditorBackground = "#1f2427";
    public const string PanelBackground = "#2b2d32";
    public const string PanelBackgroundAlt = "#202329";
    public const string MenuBackground = "#1b1e24";
    public const string ToolbarBackground = "#20242b";
    public const string ConsoleBackground = "#24272b";
    public const string ConsoleSurface = "#23272b";
    public const string DarkSurface = "#191c21";
    public const string BorderColor = "#343943";
    public const string BorderSubtle = "#22272b";
    public const string InputBackground = "#16191f";
    public const string InputBackgroundAlt = "#282a2f";
    public const string FilterBackground = "#34383e";

    public const string TextPrimary = "#f3f7ff";
    public const string TextSecondary = "#d7dce5";
    public const string TextMuted = "#a9b3bf";
    public const string TextDim = "#8e96a3";
    public const string TextFaint = "#717b85";
    public const string TextDivider = "#aeb8c2";

    public const string Accent = "#263d5f";
    public const string AccentBorder = "#356195";
    public const string Selection = "#2b3442";
    public const string SelectionStroke = "#ffffff";
    public const string ObjectStroke = "#4a5260";
    public const string GridLine = "#252a33";
    public const string CanvasBackground = "#111318";

    public const string OkColor = "#7bd88f";
    public const string WarnColor = "#ffd166";
    public const string ErrColor = "#ff6b6b";

    public const string MonoFont = "Menlo,Consolas,monospace";
    public const string UiFont = "Inter,Segoe UI";

    public static IBrush Brush(string color) => Avalonia.Media.Brush.Parse(color);

    public static TextBlock Text(string text, string color, FontWeight weight = FontWeight.Normal)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brush(color),
            FontWeight = weight,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    public static StackPanel RowStack(string background = PanelBackgroundAlt)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brush(background),
            Spacing = 4,
            Margin = new Thickness(8, 0)
        };
    }

    public static StackPanel ColumnStack(string background = PanelBackgroundAlt)
    {
        return new StackPanel
        {
            Background = Brush(background),
            Spacing = 2
        };
    }

    public static Border BorderBox(Control content, string color, double left, double top, double right, double bottom)
    {
        return new Border
        {
            Child = content,
            BorderBrush = Brush(color),
            BorderThickness = new Thickness(left, top, right, bottom)
        };
    }

    public static ScrollViewer Scroll(Control content)
    {
        return new ScrollViewer
        {
            Content = content,
            Background = Brush(PanelBackgroundAlt)
        };
    }
}
