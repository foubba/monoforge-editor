using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views;

internal static class UiFactory
{
    public static Button MenuButton(string text, EventHandler<RoutedEventArgs>? click = null)
    {
        var button = new Button
        {
            Content = text,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush(TextSecondary),
            Padding = new Thickness(6, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        if (click is not null)
        {
            button.Click += click;
        }

        return button;
    }

    public static Button PrimaryButton(string text, EventHandler<RoutedEventArgs> click)
    {
        var button = MenuButton(text, click);
        button.Background = Brush(Accent);
        button.BorderBrush = Brush(AccentBorder);
        return button;
    }

    public static TextBlock Tab(string text, bool active, string icon = "")
    {
        return new TextBlock
        {
            Text = "  " + (string.IsNullOrEmpty(icon) ? "" : icon + " ") + text + "  ",
            Background = Brush(ConsoleBackground),
            Foreground = Brush(active ? TextPrimary : TextDim),
            FontWeight = FontWeight.Normal,
            FontSize = 12,
            Padding = new Thickness(8, 9, 8, 0),
            Height = 38
        };
    }

    public static Button FilterButton(string text, EventHandler<RoutedEventArgs>? click = null)
    {
        var button = new Button
        {
            Content = text,
            Background = Brush(FilterBackground),
            BorderBrush = Brush(FilterBackground),
            Foreground = Brush(TextMuted),
            FontSize = 12,
            FontFamily = FontFamily.Parse("Menlo"),
            Padding = new Thickness(8, 4)
        };
        if (click is not null)
        {
            button.Click += click;
        }

        return button;
    }

    public static Border PaneFrame(Control content, string title)
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("34,*"),
            Background = Brush(PanelBackground)
        };
        grid.Children.Add(Text(title, TextMuted).WithMargin(12, 10, 10, 0).At(row: 0));
        grid.Children.Add(content.At(row: 1));
        return BorderBox(grid, BorderColor, 0, 0, 1, 0);
    }
}
