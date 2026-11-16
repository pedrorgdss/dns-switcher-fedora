using System.ComponentModel;
using System.Diagnostics;
using DnsSwitcher.Core.Models;

namespace DnsSwitcher.Fedora.Services;

public interface ICommandRunner
{
    Task<string> RunAsync(string program, string[] args, CancellationToken token);
}

public sealed class CommandRunner : ICommandRunner
{
    public async Task<string> RunAsync(string program, string[] args, CancellationToken token)
    {
        var info = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardError = true,
            RedirectStandardOutput = true, RedirectStandardInput = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["LC_ALL"] = "C";
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Win32Exception e) { throw new AppError("MissingCommand", program, e); }
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(65));
        try { await process.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            throw new AppError("SystemTimeout");
        }
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var detail = stderr.Result.Trim();
            bool denied = new[] { "not authorized", "not permitted", "insufficient privileges", "permission denied",
                "access denied", "authentication" }.Any(s => detail.Contains(s, StringComparison.OrdinalIgnoreCase));
            throw new AppError(denied ? "Permission" : program.EndsWith("resolvectl") ? "ResolverError" : "SystemError", detail);
        }
        return stdout.Result;
    }
}
