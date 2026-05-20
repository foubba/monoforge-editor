using System.Diagnostics;

namespace MonoForge.Editor.Services;

public static class ProcessRunner
{
    public static Task<int> RunAsync(string fileName, string arguments, string workingDirectory, Action<string> onOutput, Action<string>? onError = null, CancellationToken cancellation = default)
    {
        var tcs = new TaskCompletionSource<int>();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } line) onOutput(line);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { } line) (onError ?? onOutput)(line);
        };
        process.Exited += (_, _) =>
        {
            tcs.TrySetResult(process.ExitCode);
            process.Dispose();
        };

        cancellation.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch { /* ignored */ }
        });

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            onOutput("Failed to start: " + ex.Message);
            tcs.TrySetResult(-1);
        }

        return tcs.Task;
    }
}
