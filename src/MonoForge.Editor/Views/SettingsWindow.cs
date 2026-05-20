using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MonoForge.Editor.Services;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

public sealed class SettingsWindow : Window
{
    public SettingsWindow()
    {
        Title = "Preferences";
        Width = 460; Height = 360;
        Background = Brush(EditorBackground);
        CanResize = false;

        var s = UserSettings.Current;

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };

        panel.Children.Add(Text("Editor", TextPrimary, FontWeight.Bold));

        var fontSize = new NumericUpDown
        {
            Value = s.EditorFontSize,
            Minimum = 8,
            Maximum = 32,
            Increment = 1
        };
        panel.Children.Add(Row("Code font size", fontSize));

        var editorTheme = new ComboBox
        {
            ItemsSource = new[] { "DarkPlus", "Dark", "LightPlus", "Light", "Monokai", "Solarized" },
            SelectedItem = s.EditorTheme
        };
        panel.Children.Add(Row("Editor theme", editorTheme));

        panel.Children.Add(Text("Scene", TextPrimary, FontWeight.Bold).WithMargin(0, 10, 0, 0));

        var defaultSnap = new ComboBox
        {
            ItemsSource = new[] { 1, 4, 8, 16, 32 },
            SelectedItem = s.DefaultSnap
        };
        panel.Children.Add(Row("Default snap (px)", defaultSnap));

        var showGrid = new CheckBox { Content = "Show grid by default", IsChecked = s.ShowGridDefault, Foreground = Brush(TextSecondary) };
        panel.Children.Add(showGrid);
        var snap = new CheckBox { Content = "Snap to grid by default", IsChecked = s.SnapToGridDefault, Foreground = Brush(TextSecondary) };
        panel.Children.Add(snap);

        panel.Children.Add(Text("Application", TextPrimary, FontWeight.Bold).WithMargin(0, 10, 0, 0));
        var theme = new ComboBox
        {
            ItemsSource = new[] { "Dark", "Light" },
            SelectedItem = s.ThemeVariant
        };
        panel.Children.Add(Row("Theme variant", theme));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        actions.Children.Add(MenuButton("Cancel", (_, _) => Close()));
        actions.Children.Add(PrimaryButton("Save", (_, _) =>
        {
            var ns = new UserSettings
            {
                EditorFontSize = (int)(fontSize.Value ?? 13),
                EditorTheme = (string?)editorTheme.SelectedItem ?? "DarkPlus",
                DefaultSnap = (int)(defaultSnap.SelectedItem ?? 4),
                ShowGridDefault = showGrid.IsChecked == true,
                SnapToGridDefault = snap.IsChecked == true,
                ThemeVariant = (string?)theme.SelectedItem ?? "Dark"
            };
            ns.Save();
            Close();
        }));
        panel.Children.Add(actions);

        Content = panel;
    }

    private static Control Row(string label, Control input)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*") };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush(TextMuted),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        }.At(column: 0));
        grid.Children.Add(input.At(column: 1));
        return grid;
    }
}
