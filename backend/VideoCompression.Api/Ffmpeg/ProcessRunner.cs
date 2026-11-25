using System.Diagnostics;
using System.Text;

namespace VideoCompression.Api.Ffmpeg;

public sealed record ProcessResult(int ExitCode, string Stdout, string StderrTail);

/// <summary>Runs a child process with an argument list (never a shell string), streaming its output.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        string? workingDirectory = null,
        bool captureStdout = false,
        Action<string>? onStdoutLine = null,
        int stderrTailLines = 30)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (workingDirectory is not null) startInfo.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        // SVT-AV1 prints an info banner on every run; only keep warnings and errors.
        startInfo.Environment["SVT_LOG"] = "2";

        using var process = new Process { StartInfo = startInfo };
        var stdout = captureStdout ? new StringBuilder() : null;
        var stderr = new Queue<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout?.AppendLine(e.Data);
            onStdoutLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (stderr)
            {
                stderr.Enqueue(e.Data);
                while (stderr.Count > stderrTailLines) stderr.Dequeue();
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        process.WaitForExit(); // make sure the async output handlers have drained

        string tail;
        lock (stderr) tail = string.Join(Environment.NewLine, stderr);
        return new ProcessResult(process.ExitCode, stdout?.ToString() ?? string.Empty, tail);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (Exception)
        {
            // The process may already be gone; nothing else to do.
        }
    }
}

public static class FfmpegErrors
{
    /// <summary>Turns FFmpeg's stderr into a short, readable error message.</summary>
    public static string Summarize(ProcessResult result)
    {
        var lines = result.StderrTail
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("Svt[info]", StringComparison.Ordinal))
            .TakeLast(4);
        var text = string.Join(" ", lines);
        if (string.IsNullOrWhiteSpace(text)) text = $"FFmpeg exited with code {result.ExitCode}.";
        return text.Length > 500 ? text[..500] + "…" : text;
    }
}
