using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

public sealed class RenameDialog : Window
{
    public RenameDialog(string current)
    {
        Title = "Rename";
        Width = 400; Height = 130;
        Background = Brush(MenuBackground);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var input = new TextBox
        {
            Text = current,
            Background = Brush(InputBackground),
            Foreground = Brush(TextSecondary),
            BorderBrush = Brush(BorderColor),
            FontFamily = new FontFamily(MonoFont),
            Padding = new Thickness(10, 6),
            Margin = new Thickness(16, 16, 16, 0)
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 12, 16, 16)
        };
        actions.Children.Add(MenuButton("Cancel", (_, _) => Close(null)));
        actions.Children.Add(PrimaryButton("Rename", (_, _) => Close(input.Text)));

        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Close(input.Text);
            else if (e.Key == Key.Escape) Close(null);
        };

        var root = new StackPanel();
        root.Children.Add(input);
        root.Children.Add(actions);
        Content = root;

        Opened += (_, _) => { input.Focus(); input.SelectAll(); };
    }
}
