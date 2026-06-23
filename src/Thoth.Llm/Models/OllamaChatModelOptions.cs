namespace Thoth.Llm.Models;

public sealed record OllamaChatModelOptions(
    string Endpoint,
    string Model,
    double Temperature = 0.2);
