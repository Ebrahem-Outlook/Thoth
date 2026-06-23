namespace Thoth.Core.Configuration;

public sealed class ThothOptions
{
    public string WorkspaceRoot { get; set; } = Environment.CurrentDirectory;

    public string DataDirectory { get; set; } = Path.Combine(Environment.CurrentDirectory, "data");

    public int MaxAgentSteps { get; set; } = 8;

    public ModelOptions Model { get; set; } = new();

    public SandboxOptions Sandbox { get; set; } = new();
}

public sealed class ModelOptions
{
    public string Provider { get; set; } = "local";

    public string Model { get; set; } = "thoth-local";

    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";

    public string ApiKeyEnvironmentVariable { get; set; } = "OPENAI_API_KEY";

    public double Temperature { get; set; } = 0.2;
}

public sealed class SandboxOptions
{
    public bool AllowFileWrites { get; set; } = true;

    public bool AllowShell { get; set; }

    public int ShellTimeoutSeconds { get; set; } = 20;

    public List<string> AllowedShellExecutables { get; set; } = ["dotnet", "git", "rg"];

    public List<string> BlockedCommandTokens { get; set; } =
    [
        " rm ",
        " del ",
        " rmdir ",
        "format",
        "shutdown",
        "git reset",
        "git clean"
    ];
}
