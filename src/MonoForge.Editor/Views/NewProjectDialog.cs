using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MonoForge.Editor.Services;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

public sealed class NewProjectDialog : Window
{
    private string _location = "";
    private readonly TextBox _name = new() { Text = "MyGame" };
    private readonly TextBlock _locLabel = new() { Foreground = Brush(TextDim), FontSize = 12 };
    private readonly TextBlock _status = new() { Foreground = Brush(TextMuted), FontSize = 12 };
    private readonly ComboBox _template = new()
    {
        ItemsSource = new[] { "Empty", "Platformer", "TopDown" },
        SelectedIndex = 0
    };

    public string? CreatedAt { get; private set; }

    public NewProjectDialog()
    {
        Title = "New Project";
        Width = 540; Height = 280;
        Background = Brush(MenuBackground);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(Text("Create MonoGame Project", TextPrimary, FontWeight.Bold));

        _name.Background = Brush(InputBackground);
        _name.Foreground = Brush(TextSecondary);
        _name.BorderBrush = Brush(BorderColor);
        _name.Padding = new Thickness(8, 4);
        panel.Children.Add(Row("Name", _name));
        panel.Children.Add(Row("Template", _template));

        var locRow = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*,Auto") };
        locRow.Children.Add(new TextBlock { Text = "Location", Foreground = Brush(TextDim), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }.At(column: 0));
        locRow.Children.Add(_locLabel.WithMargin(0, 0, 8, 0).At(column: 1));
        var pickBtn = MenuButton("Choose…", async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose parent folder" });
            var f = folders.FirstOrDefault();
            if (f is not null)
            {
                _location = f.Path.LocalPath;
                _locLabel.Text = _location;
            }
        });
        locRow.Children.Add(pickBtn.At(column: 2));
        panel.Children.Add(locRow);

        panel.Children.Add(new TextBlock
        {
            Text = "Creates: .csproj, Program.cs, Game1.cs, Content/Content.mgcb, Content/Scenes/main.scene.json.\nTarget: DesktopGL (net8.0).",
            Foreground = Brush(TextDim),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(_status);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        actions.Children.Add(MenuButton("Cancel", (_, _) => Close()));
        actions.Children.Add(PrimaryButton("Create", (_, _) => Create()));
        panel.Children.Add(actions);

        Content = panel;
    }

    private void Create()
    {
        var name = (_name.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        { _status.Text = "Invalid name."; return; }
        if (string.IsNullOrEmpty(_location) || !Directory.Exists(_location))
        { _status.Text = "Pick a valid location."; return; }

        var root = Path.Combine(_location, name);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        { _status.Text = "Folder exists and is not empty."; return; }

        try
        {
            var tpl = (_template.SelectedItem as string) switch
            {
                "Platformer" => ProjectTemplate.Template.Platformer,
                "TopDown" => ProjectTemplate.Template.TopDown,
                _ => ProjectTemplate.Template.Empty
            };
            ProjectTemplate.CreateDesktopGL(root, name, tpl);
            CreatedAt = root;
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = "Failed: " + ex.Message;
        }
    }

    private static Control Row(string label, Control input)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*") };
        grid.Children.Add(new TextBlock { Text = label, Foreground = Brush(TextDim), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }.At(column: 0));
        grid.Children.Add(input.At(column: 1));
        return grid;
    }
}
