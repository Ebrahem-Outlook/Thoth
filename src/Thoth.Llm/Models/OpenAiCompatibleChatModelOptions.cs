namespace Thoth.Llm.Models;

public sealed record OpenAiCompatibleChatModelOptions(
    string Endpoint,
    string ApiKey,
    string Model,
    double Temperature = 0.2);
