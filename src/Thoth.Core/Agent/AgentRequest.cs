namespace Thoth.Core.Agent;

public sealed record AgentRequest(
    string Goal,
    string WorkingDirectory,
    string Model,
    int MaxSteps = 8,
    bool DryRun = false);
