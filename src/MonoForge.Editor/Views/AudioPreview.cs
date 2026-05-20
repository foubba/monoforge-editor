using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

public sealed class AudioPreview : UserControl
{
    private Process? _process;
    private readonly string _path;
    private readonly TextBlock _status;

    public AudioPreview(string filePath)
    {
        _path = filePath;
        Background = Brush(EditorBackground);

        var info = new FileInfo(filePath);
        var size = FormatBytes(info.Length);

        var icon = new TextBlock
        {
            Text = "♪",
            FontSize = 92,
            Foreground = Brush("#65a7ff"),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var name = new TextBlock
        {
            Text = Path.GetFileName(filePath),
            Foreground = Brush(TextDivider),
            FontFamily = new FontFamily(UiFont),
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 4)
        };

        var meta = new TextBlock
        {
            Text = size + "    " + Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
            Foreground = Brush(TextDim),
            FontFamily = new FontFamily("Menlo"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _status = new TextBlock
        {
            Text = "Ready",
            Foreground = Brush(TextFaint),
            FontFamily = new FontFamily("Menlo"),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0)
        };
        actions.Children.Add(PrimaryButton("▶ Play", (_, _) => Play()));
        actions.Children.Add(MenuButton("◼ Stop", (_, _) => Stop()));
        actions.Children.Add(MenuButton("Reveal", (_, _) => Reveal()));

        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 0
        };
        panel.Children.Add(icon);
        panel.Children.Add(name);
        panel.Children.Add(meta);
        panel.Children.Add(actions);
        panel.Children.Add(_status);

        Content = panel;
    }

    private void Play()
    {
        Stop();
        try
        {
            ProcessStartInfo psi;
            if (OperatingSystem.IsMacOS()) psi = new ProcessStartInfo("afplay", "\"" + _path + "\"") { UseShellExecute = false };
            else if (OperatingSystem.IsWindows()) psi = new ProcessStartInfo { FileName = _path, UseShellExecute = true };
            else psi = new ProcessStartInfo("xdg-open", "\"" + _path + "\"") { UseShellExecute = false };
            _process = Process.Start(psi);
            _status.Text = "Playing…";
            if (_process is not null)
            {
                _process.EnableRaisingEvents = true;
                _process.Exited += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => _status.Text = "Done");
            }
        }
        catch (Exception ex)
        {
            _status.Text = "Play failed: " + ex.Message;
        }
    }

    private void Stop()
    {
        try { if (_process is { HasExited: false }) _process.Kill(true); }
        catch { /* ignored */ }
        _process = null;
        _status.Text = "Stopped";
    }

    private void Reveal()
    {
        try
        {
            ProcessStartInfo psi;
            if (OperatingSystem.IsMacOS()) psi = new ProcessStartInfo("open", "-R \"" + _path + "\"") { UseShellExecute = false };
            else if (OperatingSystem.IsWindows()) psi = new ProcessStartInfo("explorer", "/select,\"" + _path + "\"") { UseShellExecute = false };
            else psi = new ProcessStartInfo("xdg-open", "\"" + Path.GetDirectoryName(_path) + "\"") { UseShellExecute = false };
            Process.Start(psi);
        }
        catch { /* ignored */ }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }
}
