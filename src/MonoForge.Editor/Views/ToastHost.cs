using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views;

/// <summary>
/// Light-weight toast notifications. Stacked top-right of the workspace; each toast
/// auto-dismisses after a few seconds. There's one host per window (set via
/// <see cref="AttachTo"/>) so any code can fire a toast via the static helpers
/// without plumbing a service reference around.
/// </summary>
public sealed class ToastHost : Panel
{
    public enum ToastKind { Info, Success, Warn, Error }

    private static ToastHost? _active;

    private readonly StackPanel _stack = new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 6,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 14, 14, 0)
    };

    public ToastHost()
    {
        IsHitTestVisible = false; // toasts shouldn't block clicks on the editor below
        Children.Add(_stack);
        Background = null;
    }

    /// <summary>Register this host as the active sink for global toast calls.</summary>
    public void AttachAsActive() => _active = this;

    // ── Public API (call from anywhere) ─────────────────────────────────────────

    public static void Info(string message, double seconds = 3) => Show(message, ToastKind.Info, seconds);
    public static void Success(string message, double seconds = 3) => Show(message, ToastKind.Success, seconds);
    public static void Warn(string message, double seconds = 4) => Show(message, ToastKind.Warn, seconds);
    public static void Error(string message, double seconds = 5) => Show(message, ToastKind.Error, seconds);

    public static void Show(string message, ToastKind kind, double seconds = 3)
    {
        var host = _active;
        if (host is null) return;
        Dispatcher.UIThread.Post(() => host.ShowOnUi(message, kind, seconds));
    }

    // ── Internals ───────────────────────────────────────────────────────────────

    private void ShowOnUi(string message, ToastKind kind, double seconds)
    {
        var (bg, accent, glyph) = kind switch
        {
            ToastKind.Success => ("#1c2f1f", "#5fff7a", "✓"),
            ToastKind.Warn    => ("#2f291c", "#ffd166", "!"),
            ToastKind.Error   => ("#2f1c1c", "#ff6b6b", "✕"),
            _                 => ("#1c2330", "#5fb0ff", "i"),
        };

        var icon = new TextBlock
        {
            Text = glyph,
            Foreground = Brush(accent),
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var text = new TextBlock
        {
            Text = message,
            Foreground = Brush(TextPrimary),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360,
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(icon);
        row.Children.Add(text);

        var card = new Border
        {
            Background = Brush(bg),
            BorderBrush = Brush(accent),
            BorderThickness = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            BoxShadow = BoxShadows.Parse("0 6 20 0 #66000000"),
            Child = row,
            Opacity = 0,
        };
        _stack.Children.Insert(0, card);

        // Fade in, hold, fade out, remove. Avalonia's transitions API would let us
        // declare this with property animations, but the per-shot timer here keeps the
        // class self-contained and easy to read.
        DispatcherTimer.RunOnce(() => card.Opacity = 1, TimeSpan.FromMilliseconds(10));
        DispatcherTimer.RunOnce(() => card.Opacity = 0, TimeSpan.FromSeconds(seconds));
        DispatcherTimer.RunOnce(() =>
        {
            if (_stack.Children.Contains(card)) _stack.Children.Remove(card);
        }, TimeSpan.FromSeconds(seconds + 0.5));
    }
}
