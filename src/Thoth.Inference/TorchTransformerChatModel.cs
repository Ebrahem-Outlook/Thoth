using Thoth.Core.Chat;
using Thoth.Model;
using Thoth.Tokenization;

namespace Thoth.Inference;

public sealed class TorchTransformerChatModel(
    TorchTransformerLanguageModel model,
    ITextTokenizer tokenizer,
    GenerationOptions? defaultOptions = null) : IChatModel, IDisposable
{
    private readonly GenerationOptions defaultOptions = defaultOptions ?? new GenerationOptions();

    public async Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prompt = NeuralChatModel.BuildPrompt(request.Messages);
        var options = defaultOptions with
        {
            Temperature = request.Temperature > 0 ? request.Temperature : defaultOptions.Temperature
        };
        var content = await new TorchTransformerTextGenerator(model, tokenizer)
            .GenerateAsync(prompt, options, cancellationToken: cancellationToken);

        return new ChatResponse(
            content,
            request.Model,
            tokenizer.Encode(prompt).Count,
            tokenizer.Encode(content).Count);
    }

    public void Dispose() => model.Dispose();
}
