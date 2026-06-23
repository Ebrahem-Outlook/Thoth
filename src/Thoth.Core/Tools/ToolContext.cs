using Thoth.Core.Memory;
using Thoth.Core.Sandbox;

namespace Thoth.Core.Tools;

public sealed record ToolContext(
    string WorkingDirectory,
    IMemoryStore Memory,
    IExecutionPolicy Policy,
    bool DryRun = false);
