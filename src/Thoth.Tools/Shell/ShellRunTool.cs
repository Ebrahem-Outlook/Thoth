using System.Diagnostics;
using System.Text;
using Thoth.Core.Tools;

namespace Thoth.Tools.Shell;

public sealed class ShellRunTool(TimeSpan timeout) : IAgentTool
{
    public string Name => "shell.run";

    public string Description => "Runs an approved executable in the workspace with a timeout.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("executable", "Executable name, for example dotnet or rg."),
        new("arguments", "Command arguments.", false)
    ];

    public async ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var executable = invocation.GetString("executable");
        var arguments = invocation.GetString("arguments");

        if (string.IsNullOrWhiteSpace(executable))
        {
            return ToolResult.Failure(Name, "Executable is required.");
        }

        if (context.DryRun)
        {
            return ToolResult.Success(Name, $"Dry run: would execute {executable} {arguments}");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var startInfo = new ProcessStartInfo(executable, arguments)
        {
            WorkingDirectory = context.WorkingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return ToolResult.Failure(Name, $"Failed to start process: {executable}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return ToolResult.Failure(Name, $"Command timed out after {timeout.TotalSeconds:n0}s.");
        }

        var output = new StringBuilder();
        output.Append(await stdoutTask);
        var stderr = await stderrTask;
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            output.AppendLine();
            output.AppendLine("[stderr]");
            output.Append(stderr);
        }

        return process.ExitCode == 0
            ? ToolResult.Success(Name, output.ToString(), new Dictionary<string, string> { ["exitCode"] = process.ExitCode.ToString() })
            : ToolResult.Failure(Name, output.ToString(), new Dictionary<string, string> { ["exitCode"] = process.ExitCode.ToString() });
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
