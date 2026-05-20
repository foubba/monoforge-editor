using MonoForge.Editor.Models;

namespace MonoForge.Editor.Views;

internal static class AssetIcon
{
    public static string For(ProjectTreeNode node)
    {
        if (node.IsDirectory) return "📁";
        var extension = Path.GetExtension(node.Name).ToLowerInvariant();
        var name = node.Name.ToLowerInvariant();
        return extension switch
        {
            _ when IsCodeLike(name, extension) => "▤",
            ".png" or ".jpg" or ".jpeg" => "▧",
            ".json" => "▦",
            ".mgcb" or ".atlas" => "▦",
            ".fx" or ".fxc" or ".fp" => "▣",
            ".sprite" => "◆",
            ".material" => "○",
            ".vp" => "✣",
            ".gui" => "▱",
            ".script" => "▤",
            ".collection" => "▰",
            ".wav" or ".mp3" or ".ogg" => "♪",
            ".glb" or ".gltf" or ".fbx" or ".obj" => "◈",
            _ when name.EndsWith(".gui_script") => "▤",
            _ when name.EndsWith(".input_binding") => "▰",
            _ => "▧"
        };
    }

    public static string ColorFor(ProjectTreeNode node)
    {
        if (node.IsDirectory) return "#9b9fa2";
        var extension = Path.GetExtension(node.Name).ToLowerInvariant();
        var name = node.Name.ToLowerInvariant();
        return extension switch
        {
            _ when IsCodeLike(name, extension) => "#aeb8c2",
            ".png" or ".jpg" or ".jpeg" => "#aeb8c2",
            ".fp" or ".fx" or ".fxc" => "#f0c400",
            ".material" => "#42c21a",
            ".vp" => "#f0c400",
            ".gui" or ".atlas" or ".collection" => "#42b7ff",
            ".mgcb" => "#f0c400",
            ".script" => "#aeb8c2",
            _ when name.EndsWith(".gui_script") => "#aeb8c2",
            _ when name.EndsWith(".input_binding") => "#3dd51e",
            _ => "#9b9fa2"
        };
    }

    public static bool IsImage(string extension)
    {
        return extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
    }

    public static bool IsTextLike(string name, string extension)
    {
        if (name.EndsWith(".gui_script", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".input_binding", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsCodeLike(name, extension)
            || extension is ".json" or ".xml" or ".txt" or ".md" or ".mgcb" or ".fx" or ".fxc"
                or ".fp" or ".vp" or ".script" or ".material" or ".atlas" or ".collection";
    }

    public static bool IsCodeLike(string name, string extension)
    {
        if (name.EndsWith(".gui_script", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return extension is ".cs" or ".py" or ".js" or ".jsx" or ".ts" or ".tsx" or ".html"
            or ".css" or ".scss" or ".go" or ".java" or ".cpp" or ".cc" or ".c" or ".h"
            or ".hpp" or ".rs" or ".lua" or ".rb" or ".php" or ".swift" or ".kt" or ".fs"
            or ".vb" or ".sh" or ".zsh" or ".ps1";
    }
}
