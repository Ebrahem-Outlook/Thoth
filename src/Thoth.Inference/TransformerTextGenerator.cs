using Thoth.Model;
using Thoth.Tokenization;

namespace Thoth.Inference;

public sealed class TransformerTextGenerator(
    TransformerLanguageModel model,
    ITextTokenizer tokenizer)
{
    public string Generate(string prompt, GenerationOptions? options = null)
    {
        options ??= new GenerationOptions();
        options.Validate();

        if (model.VocabularySize != tokenizer.VocabularySize)
        {
            throw new InvalidOperationException("Tokenizer vocabulary does not match the Transformer vocabulary.");
        }

        var generated = GenerateTokenIds(prompt, options);

        return tokenizer.Decode(generated).Trim();
    }

    public IReadOnlyList<int> GenerateTokenIds(string prompt, GenerationOptions? options = null)
    {
        options ??= new GenerationOptions();
        options.Validate();

        if (model.VocabularySize != tokenizer.VocabularySize)
        {
            throw new InvalidOperationException("Tokenizer vocabulary does not match the Transformer vocabulary.");
        }

        var promptTokens = tokenizer.Encode(prompt, addBeginningOfSequence: true).ToList();
        var generated = new List<int>(options.MaxNewTokens);
        var random = new Random(options.Seed ?? HashCode.Combine(prompt, model.OptimizerStep));

        for (var index = 0; index < options.MaxNewTokens; index++)
        {
            var context = promptTokens.Concat(generated).TakeLast(model.ContextLength).ToArray();
            var logits = model.NextTokenLogits(context);
            var token = Sample(logits, options.Temperature, options.TopK, options.TopP, random);
            if (token == tokenizer.EndOfSequenceTokenId)
            {
                break;
            }

            generated.Add(token);
        }

        return generated;
    }

    public static int Sample(
        IReadOnlyList<double> logits,
        double temperature,
        int topK,
        double topP,
        Random random)
    {
        if (logits.Count == 0)
        {
            throw new ArgumentException("Logits must not be empty.", nameof(logits));
        }

        var candidates = Enumerable.Range(0, logits.Count)
            .Select(index => (Index: index, Logit: logits[index] / temperature))
            .OrderByDescending(item => item.Logit)
            .Take(topK <= 0 ? logits.Count : Math.Min(topK, logits.Count))
            .ToArray();

        var maximum = candidates[0].Logit;
        var weighted = candidates
            .Select(item => (item.Index, Weight: Math.Exp(Math.Clamp(item.Logit - maximum, -60, 60))))
            .ToArray();
        var total = weighted.Sum(item => item.Weight);
        var normalized = weighted
            .Select(item => (item.Index, Probability: item.Weight / total))
            .OrderByDescending(item => item.Probability)
            .ToArray();

        var nucleus = new List<(int Index, double Probability)>();
        var cumulative = 0.0;
        foreach (var item in normalized)
        {
            nucleus.Add(item);
            cumulative += item.Probability;
            if (cumulative >= topP)
            {
                break;
            }
        }

        var nucleusTotal = nucleus.Sum(item => item.Probability);
        var sample = random.NextDouble() * nucleusTotal;
        foreach (var item in nucleus)
        {
            sample -= item.Probability;
            if (sample <= 0)
            {
                return item.Index;
            }
        }

        return nucleus[^1].Index;
    }
}
