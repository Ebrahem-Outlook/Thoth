using System.Text.Json;

namespace Thoth.Data.Synthetic;

public sealed record OwnedInstructionExample(
    string Id,
    string Language,
    string Task,
    IReadOnlyList<InstructionMessage> Messages,
    VerifierInfo Verifier,
    IReadOnlyDictionary<string, string> Provenance);

public sealed record InstructionMessage(string Role, string Content);

public sealed record VerifierInfo(string Type, string Status);

public sealed class OwnedSyntheticInstructionGenerator
{
    private static readonly string[] Languages = ["csharp", "typescript", "cpp"];
    private static readonly string[] Operations = ["add", "subtract", "multiply", "divide"];

    public IReadOnlyList<OwnedInstructionExample> Generate(int count, int seed = 1337)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var examples = new List<OwnedInstructionExample>(count);
        for (var index = 0; index < count; index++)
        {
            var language = Languages[(seed + index) % Languages.Length];
            var operationCount = 2 + (seed + index) % 3;
            var operations = Operations.Take(operationCount).ToArray();
            var arabic = index % 2 == 1;
            var user = arabic
                ? $"\u0627\u0639\u0645\u0644 calculator method \u0628\u0640 {language} \u062a\u062f\u0639\u0645 {string.Join(", ", operations)}"
                : $"Build a {language} calculator method that supports {string.Join(", ", operations)}.";
            var assistant = BuildAnswer(language, operations);

            examples.Add(new OwnedInstructionExample(
                $"owned-calculator-{seed}-{index:000000}",
                arabic ? "ar" : "en",
                "calculator-procedure",
                [
                    new InstructionMessage("user", user),
                    new InstructionMessage("assistant", assistant)
                ],
                new VerifierInfo("deterministic-template", "passed"),
                new Dictionary<string, string>
                {
                    ["type"] = "owned-synthetic",
                    ["templateFamily"] = "calculator",
                    ["seed"] = seed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        }

        return examples;
    }

    public async Task WriteJsonlAsync(
        string path,
        int count,
        int seed = 1337,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream);
        foreach (var example in Generate(count, seed))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(JsonSerializer.Serialize(example, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
    }

    private static string BuildAnswer(string language, IReadOnlyList<string> operations) =>
        language switch
        {
            "typescript" => $"export function calculate(a: number, b: number, op: string): number {{ return {BuildExpression("a", "b", "op", operations)}; }}",
            "cpp" => $"double calculate(double a, double b, std::string op) {{ return {BuildExpression("a", "b", "op", operations)}; }}",
            _ => $"public static decimal Calculate(decimal a, decimal b, string op) => {BuildExpression("a", "b", "op", operations)};"
        };

    private static string BuildExpression(string left, string right, string op, IReadOnlyList<string> operations)
    {
        var branches = new List<string>();
        foreach (var operation in operations)
        {
            branches.Add(operation switch
            {
                "add" => $"{op} == \"add\" ? {left} + {right}",
                "subtract" => $"{op} == \"subtract\" ? {left} - {right}",
                "multiply" => $"{op} == \"multiply\" ? {left} * {right}",
                "divide" => $"{op} == \"divide\" ? ({right} == 0 ? throw new DivideByZeroException() : {left} / {right})",
                _ => throw new InvalidOperationException("Unknown operation template.")
            });
        }

        return string.Join(" : ", branches) + " : throw new ArgumentOutOfRangeException(nameof(op))";
    }
}
