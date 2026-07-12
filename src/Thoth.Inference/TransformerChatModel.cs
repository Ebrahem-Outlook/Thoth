using Thoth.Core.Chat;
using Thoth.Model;
using Thoth.Tokenization;

namespace Thoth.Inference;

public sealed class TransformerChatModel(
    TransformerLanguageModel model,
    ITextTokenizer tokenizer,
    GenerationOptions? defaultOptions = null) : IChatModel
{
    private readonly GenerationOptions defaultOptions = defaultOptions ?? new GenerationOptions();

    public Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prompt = NeuralChatModel.BuildPrompt(request.Messages);
        var options = defaultOptions with
        {
            Temperature = request.Temperature > 0 ? request.Temperature : defaultOptions.Temperature
        };
        var content = new TransformerTextGenerator(model, tokenizer).Generate(prompt, options);

        return Task.FromResult(new ChatResponse(
            content,
            request.Model,
            tokenizer.Encode(prompt).Count,
            tokenizer.Encode(content).Count));
    }
}
