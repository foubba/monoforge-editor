using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace MonoForge.Editor.Views;

/// <summary>
/// Shared application icon. The bitmap is generated once from
/// <see cref="MonoForgeLogo"/> at startup (256 px for Retina-crisp dock /
/// taskbar entries) and reused for every <see cref="Window"/> the editor
/// opens, so every secondary editor (Atlas, Tilemap, Animation, Particle,
/// Settings, dialogs…) shows the MonoForge brand instead of the default
/// .NET icon.
/// </summary>
public static class AppIcon
{
    private static Bitmap? _bitmap;
    private static WindowIcon? _windowIcon;

    public static Bitmap Bitmap => _bitmap ??= MonoForgeLogo.RenderToBitmap(256);

    public static WindowIcon WindowIcon => _windowIcon ??= new WindowIcon(Bitmap);

    /// <summary>Attach the shared icon to the given window. Swallows failures
    /// (some headless / unit test contexts can't load images).</summary>
    public static void Apply(Window window)
    {
        try { window.Icon = WindowIcon; }
        catch { /* icon is decorative — never crash for it */ }
    }

    /// <summary>
    /// Set the application-level icon shown in the macOS Dock and ⌘-Tab switcher.
    /// `Window.Icon` only controls the in-window title-bar / taskbar entry; on macOS
    /// the Dock pulls its icon from the .app bundle's Info.plist, which means when
    /// the editor runs via `dotnet run` (no bundle) it shows the generic .NET
    /// document icon. We override that at runtime by calling
    /// NSApplication.setApplicationIconImage with an NSImage built from a PNG of
    /// our logo. No-op on non-Mac platforms.
    /// </summary>
    public static void TryApplyMacDockIcon()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            // Serialize the Avalonia bitmap to a PNG byte[] — NSImage initWithData:
            // is happy to ingest PNG bytes directly, so no .icns or temp file needed.
            using var ms = new MemoryStream();
            Bitmap.Save(ms);
            var pngBytes = ms.ToArray();

            var nsAppClass = objc_getClass("NSApplication");
            var nsApp = objc_msgSend_id(nsAppClass, sel_registerName("sharedApplication"));
            if (nsApp == IntPtr.Zero) return;

            var nsDataClass = objc_getClass("NSData");
            var dataSel = sel_registerName("dataWithBytes:length:");
            var nsData = objc_msgSend_data(nsDataClass, dataSel, pngBytes, (UIntPtr)pngBytes.Length);
            if (nsData == IntPtr.Zero) return;

            var nsImageClass = objc_getClass("NSImage");
            var alloc = objc_msgSend_id(nsImageClass, sel_registerName("alloc"));
            var nsImage = objc_msgSend_ptr(alloc, sel_registerName("initWithData:"), nsData);
            if (nsImage == IntPtr.Zero) return;

            objc_msgSend_ptr(nsApp, sel_registerName("setApplicationIconImage:"), nsImage);
        }
        catch { /* icon is decorative */ }
    }

    // ── ObjC runtime P/Invoke ────────────────────────────────────────────────────
    private const string Libobjc = "/usr/lib/libobjc.dylib";

    [DllImport(Libobjc)] private static extern IntPtr objc_getClass(string name);
    [DllImport(Libobjc)] private static extern IntPtr sel_registerName(string name);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_id(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ptr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_data(IntPtr receiver, IntPtr selector, byte[] bytes, UIntPtr length);
}
