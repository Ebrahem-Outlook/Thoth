using Thoth.Model;
using Thoth.Tokenization;

namespace Thoth.Inference;

public sealed class NeuralTextGenerator(
    RecurrentLanguageModel model,
    ITextTokenizer tokenizer)
{
    public string Generate(string prompt, GenerationOptions? options = null)
    {
        options ??= new GenerationOptions();
        options.Validate();

        if (model.Config.VocabularySize != tokenizer.VocabularySize)
        {
            throw new InvalidOperationException("Tokenizer vocabulary does not match the model vocabulary.");
        }

        var promptTokens = tokenizer.Encode(prompt, addBeginningOfSequence: true);
        var hidden = model.CreateHiddenState();
        double[]? logits = null;

        foreach (var token in promptTokens)
        {
            logits = model.ForwardToken(token, hidden);
        }

        logits ??= model.ForwardToken(tokenizer.BeginningOfSequenceTokenId, hidden);
        var generated = new List<int>(options.MaxNewTokens);
        var random = new Random(options.Seed ?? HashCode.Combine(prompt, model.OptimizerStep));

        for (var index = 0; index < options.MaxNewTokens; index++)
        {
            var token = Sample(logits, options.Temperature, options.TopK, random);
            if (token == tokenizer.EndOfSequenceTokenId)
            {
                break;
            }

            generated.Add(token);
            logits = model.ForwardToken(token, hidden);
        }

        return tokenizer.Decode(generated).Trim();
    }

    private static int Sample(double[] logits, double temperature, int topK, Random random)
    {
        var candidates = Enumerable.Range(0, logits.Length)
            .Select(index => (Index: index, Logit: logits[index] / temperature))
            .OrderByDescending(item => item.Logit)
            .Take(topK <= 0 ? logits.Length : Math.Min(topK, logits.Length))
            .ToArray();

        var maximum = candidates[0].Logit;
        var weights = new double[candidates.Length];
        var total = 0.0;
        for (var index = 0; index < candidates.Length; index++)
        {
            var weight = Math.Exp(Math.Clamp(candidates[index].Logit - maximum, -60, 60));
            weights[index] = weight;
            total += weight;
        }

        var sample = random.NextDouble() * total;
        for (var index = 0; index < candidates.Length; index++)
        {
            sample -= weights[index];
            if (sample <= 0)
            {
                return candidates[index].Index;
            }
        }

        return candidates[^1].Index;
    }
}
