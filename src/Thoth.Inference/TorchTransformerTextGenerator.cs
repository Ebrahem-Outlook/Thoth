using Thoth.Model;
using Thoth.Tokenization;

namespace Thoth.Inference;

public sealed class TorchTransformerTextGenerator(
    TorchTransformerLanguageModel model,
    ITextTokenizer tokenizer)
{
    public async Task<string> GenerateAsync(
        string prompt,
        GenerationOptions? options = null,
        TorchTransformerGenerationCache? cache = null,
        Func<int, string, CancellationToken, Task>? onToken = null,
        CancellationToken cancellationToken = default)
    {
        var tokens = await GenerateTokenIdsAsync(prompt, options, cache, onToken, cancellationToken);
        return tokenizer.Decode(tokens).Trim();
    }

    public async Task<IReadOnlyList<int>> GenerateTokenIdsAsync(
        string prompt,
        GenerationOptions? options = null,
        TorchTransformerGenerationCache? cache = null,
        Func<int, string, CancellationToken, Task>? onToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        options ??= new GenerationOptions();
        options.Validate();
        if (model.Config.VocabularySize != tokenizer.VocabularySize)
        {
            throw new InvalidOperationException("Tokenizer vocabulary does not match the Torch Transformer vocabulary.");
        }

        var ownsCache = cache is null;
        cache ??= new TorchTransformerGenerationCache();
        try
        {
            var promptTokens = tokenizer.Encode(prompt, addBeginningOfSequence: true);
            cache.Reset(promptTokens);
            var generated = new List<int>(options.MaxNewTokens);
            var random = new Random(options.Seed ?? HashCode.Combine(prompt, model.OptimizerStep));

            for (var index = 0; index < options.MaxNewTokens; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var context = cache.Tokens.TakeLast(model.Config.ContextLength).ToArray();
                var logits = ApplyRepetitionPenalty(model.NextTokenLogits(context), cache.Tokens, options.RepetitionPenalty);
                var token = options.Greedy
                    ? ArgMax(logits)
                    : TransformerTextGenerator.Sample(
                        logits.Select(value => (double)value).ToArray(),
                        options.Temperature,
                        options.TopK,
                        options.TopP,
                        random);

                if (token == tokenizer.EndOfSequenceTokenId || options.StopTokenIds.Contains(token))
                {
                    break;
                }

                generated.Add(token);
                cache.Append(token);
                if (onToken is not null)
                {
                    await onToken(token, tokenizer.Decode([token]), cancellationToken);
                }

                var decoded = tokenizer.Decode(generated);
                if (options.StopSequences.Any(sequence => decoded.EndsWith(sequence, StringComparison.Ordinal)))
                {
                    break;
                }
            }

            return generated;
        }
        finally
        {
            if (ownsCache)
            {
                cache.Dispose();
            }
        }
    }

    private static float[] ApplyRepetitionPenalty(
        IReadOnlyList<float> logits,
        IReadOnlyList<int> tokens,
        double penalty)
    {
        var adjusted = logits.ToArray();
        if (penalty <= 1)
        {
            return adjusted;
        }

        foreach (var token in tokens.Distinct())
        {
            if (token < 0 || token >= adjusted.Length)
            {
                continue;
            }

            adjusted[token] = adjusted[token] >= 0
                ? (float)(adjusted[token] / penalty)
                : (float)(adjusted[token] * penalty);
        }

        return adjusted;
    }

    private static int ArgMax(IReadOnlyList<float> logits)
    {
        var best = 0;
        for (var index = 1; index < logits.Count; index++)
        {
            if (logits[index] > logits[best])
            {
                best = index;
            }
        }

        return best;
    }
}
