using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using MonoForge.Editor.Views;

namespace MonoForge.Editor;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new StyleInclude(new Uri("avares://Avalonia.Themes.Fluent/FluentTheme.xaml"))
        {
            Source = new Uri("avares://Avalonia.Themes.Fluent/FluentTheme.xaml")
        });

        // AvaloniaEdit ships its TextEditor template in this XAML — without registering it,
        // TextEditor renders no text and no line numbers.
        Styles.Add(new StyleInclude(new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml")
        });

        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Replace the generic .NET document icon shown in the macOS Dock with our logo.
        // No-op on Windows / Linux. Window.Icon already handles those.
        AppIcon.TryApplyMacDockIcon();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new EditorWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
