using System.Text;
using Thoth.Core.Chat;
using Thoth.Model;
using Thoth.Tokenization;

namespace Thoth.Inference;

public sealed class NeuralChatModel(
    RecurrentLanguageModel model,
    ITextTokenizer tokenizer,
    GenerationOptions? defaultOptions = null) : IChatModel
{
    private readonly GenerationOptions defaultOptions = defaultOptions ?? new GenerationOptions();

    public Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prompt = BuildPrompt(request.Messages);
        var options = defaultOptions with
        {
            Temperature = request.Temperature > 0 ? request.Temperature : defaultOptions.Temperature
        };
        var generator = new NeuralTextGenerator(model, tokenizer);
        var content = generator.Generate(prompt, options);

        return Task.FromResult(new ChatResponse(
            content,
            request.Model,
            tokenizer.Encode(prompt).Count,
            tokenizer.Encode(content).Count));
    }

    private static string BuildPrompt(IReadOnlyList<ChatMessage> messages)
    {
        var builder = new StringBuilder();
        foreach (var message in messages)
        {
            builder.Append(message.Role switch
            {
                ChatRole.System => "System: ",
                ChatRole.User => "User: ",
                ChatRole.Assistant => "Assistant: ",
                ChatRole.Tool => "Tool: ",
                _ => "Message: "
            });
            builder.AppendLine(message.Content.Trim());
        }

        builder.Append("Assistant: ");
        return builder.ToString();
    }
}
