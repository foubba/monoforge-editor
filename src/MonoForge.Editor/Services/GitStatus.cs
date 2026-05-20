using System.Diagnostics;

namespace MonoForge.Editor.Services;

public static class GitStatus
{
    public sealed record Entry(string Status, string Path);

    public static Dictionary<string, string> Read(string repoRoot)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var psi = new ProcessStartInfo("git", "status --porcelain -z")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return map;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            if (proc.ExitCode != 0) return map;

            foreach (var rawEntry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (rawEntry.Length < 4) continue;
                var status = rawEntry.Substring(0, 2).Trim();
                var path = rawEntry.Substring(3);
                var full = Path.Combine(repoRoot, path);
                if (status.Contains('?')) status = "??";
                else if (status.Contains('M')) status = "M";
                else if (status.Contains('A')) status = "A";
                else if (status.Contains('D')) status = "D";
                else if (status.Contains('R')) status = "R";
                map[full] = status;
            }
        }
        catch { /* git not available */ }
        return map;
    }
}
