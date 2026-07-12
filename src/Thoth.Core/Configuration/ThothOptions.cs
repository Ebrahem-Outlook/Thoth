namespace Thoth.Core.Configuration;

public sealed class ThothOptions
{
    public string WorkspaceRoot { get; set; } = Environment.CurrentDirectory;

    public string DataDirectory { get; set; } = Path.Combine(Environment.CurrentDirectory, "data");

    public int MaxAgentSteps { get; set; } = 12;

    public ModelOptions Model { get; set; } = new();

    public SandboxOptions Sandbox { get; set; } = new();
}

public sealed class ModelOptions
{
    /// <summary>self, neural, or hybrid. Hybrid uses a checkpoint when one exists and otherwise falls back to self.</summary>
    public string Provider { get; set; } = "hybrid";

    public string Model { get; set; } = "thoth-bootstrap";

    public string CheckpointPath { get; set; } = Path.Combine("data", "models", "thoth-bootstrap.bin");

    public double Temperature { get; set; } = 0.8;

    public int MaxNewTokens { get; set; } = 256;

    public int TopK { get; set; } = 40;

    public int EmbeddingSize { get; set; } = 64;

    public int HiddenSize { get; set; } = 128;

    public int SequenceLength { get; set; } = 128;

    public int Seed { get; set; } = 1337;

    public CheckpointQualityOptions Quality { get; set; } = new();
}

public sealed class CheckpointQualityOptions
{
    public long MinimumOptimizerSteps { get; set; } = 1;

    public int MinimumEvaluatedTokens { get; set; } = 32;

    public double MaximumAverageLoss { get; set; } = 8.0;

    public double MaximumPerplexity { get; set; } = 3000.0;

    public double MinimumGenerationHealthScore { get; set; } = 0.8;

    public double MinimumUnderstandingScore { get; set; } = 0.9;

    public double MinimumAgentDecisionScore { get; set; } = 0.9;
}

public sealed class SandboxOptions
{
    public bool AllowFileWrites { get; set; } = true;

    public bool AllowShell { get; set; }

    public int ShellTimeoutSeconds { get; set; } = 20;

    public List<string> AllowedShellExecutables { get; set; } = ["dotnet", "git", "rg", "npm", "node"];

    public List<string> BlockedCommandTokens { get; set; } =
    [
        " rm ",
        " del ",
        " rmdir ",
        "format",
        "shutdown",
        "git reset",
        "git clean",
        "--force",
        "> /dev/"
    ];
}
