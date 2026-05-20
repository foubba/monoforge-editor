using System.Text;
using MonoForge.Editor.Models;

namespace MonoForge.Editor.Services;

public static class MgcbSync
{
    public sealed class SyncResult
    {
        public List<string> Copied { get; } = new();
        public List<string> AlreadyPresent { get; } = new();
        public List<string> Errors { get; } = new();
        public string MgcbPath { get; set; } = "";
    }

    /// <summary>
    /// Finds (or creates) Content.mgcb in the project, copies every TexturePath
    /// from the scene into Content/, and ensures each one has an entry in the .mgcb.
    /// </summary>
    public static SyncResult Sync(string projectRoot, SceneDocument scene)
    {
        var result = new SyncResult();
        var contentDir = Path.Combine(projectRoot, "Content");
        Directory.CreateDirectory(contentDir);

        var mgcbPath = Directory.EnumerateFiles(contentDir, "*.mgcb", SearchOption.TopDirectoryOnly).FirstOrDefault()
            ?? Path.Combine(contentDir, "Content.mgcb");
        result.MgcbPath = mgcbPath;

        if (!File.Exists(mgcbPath))
        {
            File.WriteAllText(mgcbPath, DefaultHeader());
        }

        var existing = File.ReadAllText(mgcbPath);
        var sb = new StringBuilder(existing);

        var textures = scene.Flatten()
            .Select(o => o.TexturePath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var src in textures)
        {
            if (!File.Exists(src))
            {
                result.Errors.Add("Missing: " + src);
                continue;
            }

            var fileName = Path.GetFileName(src);
            var dst = Path.Combine(contentDir, fileName);

            try
            {
                if (!File.Exists(dst) || File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(dst))
                {
                    File.Copy(src, dst, overwrite: true);
                    result.Copied.Add(fileName);
                }
                else
                {
                    result.AlreadyPresent.Add(fileName);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{fileName}: {ex.Message}");
                continue;
            }

            // Ensure entry in .mgcb
            var entry = MakeEntry(fileName);
            var marker = $"#begin {fileName}";
            if (!existing.Contains(marker, StringComparison.Ordinal))
            {
                sb.AppendLine();
                sb.Append(entry);
                existing = sb.ToString();
            }
        }

        File.WriteAllText(mgcbPath, sb.ToString());
        return result;
    }

    private static string MakeEntry(string fileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"#begin {fileName}");
        sb.AppendLine("/importer:TextureImporter");
        sb.AppendLine("/processor:TextureProcessor");
        sb.AppendLine("/processorParam:ColorKeyColor=255,0,255,255");
        sb.AppendLine("/processorParam:ColorKeyEnabled=True");
        sb.AppendLine("/processorParam:GenerateMipmaps=False");
        sb.AppendLine("/processorParam:PremultiplyAlpha=True");
        sb.AppendLine("/processorParam:ResizeToPowerOfTwo=False");
        sb.AppendLine("/processorParam:MakeSquare=False");
        sb.AppendLine("/processorParam:TextureFormat=Color");
        sb.AppendLine($"/build:{fileName}");
        return sb.ToString();
    }

    private static string DefaultHeader()
    {
        return """
            #----------------------------- Global Properties ----------------------------#

            /outputDir:bin/$(Platform)
            /intermediateDir:obj/$(Platform)
            /platform:DesktopGL
            /config:
            /profile:Reach
            /compress:False

            #-------------------------------- References --------------------------------#


            #---------------------------------- Content ---------------------------------#

            """;
    }
}
