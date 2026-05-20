using Avalonia.Media.Imaging;

namespace MonoForge.Editor.Services;

public static class TextureCache
{
    private static readonly Dictionary<string, Bitmap?> Cache = new();
    private static readonly Dictionary<string, FileSystemWatcher> Watchers = new();
    public static event Action? TextureReloaded;

    public static Bitmap? Get(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;

        try
        {
            var bmp = new Bitmap(path);
            Cache[path] = bmp;
            StartWatch(path);
            return bmp;
        }
        catch
        {
            Cache[path] = null;
            return null;
        }
    }

    public static void Invalidate(string path)
    {
        if (Cache.TryGetValue(path, out var bmp))
        {
            bmp?.Dispose();
        }
        Cache.Remove(path);
        TextureReloaded?.Invoke();
    }

    private static void StartWatch(string path)
    {
        var dir = Path.GetDirectoryName(path);
        var file = Path.GetFileName(path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        if (Watchers.ContainsKey(path)) return;

        try
        {
            var watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            DateTime lastFire = DateTime.MinValue;
            watcher.Changed += (_, _) =>
            {
                var now = DateTime.UtcNow;
                if ((now - lastFire).TotalMilliseconds < 250) return;
                lastFire = now;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Invalidate(path));
            };
            Watchers[path] = watcher;
        }
        catch
        {
            /* ignored */
        }
    }
}
