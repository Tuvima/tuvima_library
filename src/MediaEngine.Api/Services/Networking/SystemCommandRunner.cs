using System.ComponentModel;
using System.Diagnostics;

namespace MediaEngine.Api.Services.Networking;

public sealed class SystemCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new FileNotFoundException($"Command '{fileName}' was not found.", fileName, ex);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);
        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"Command '{fileName}' exceeded its {timeout.TotalSeconds:0}-second timeout.");
        }

        return new CommandResult(process.ExitCode, await outputTask, await errorTask);
    }
}
