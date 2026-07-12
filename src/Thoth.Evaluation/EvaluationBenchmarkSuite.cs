using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Thoth.Evaluation;

public sealed record EvaluationBenchmarkCase(
    string Id,
    string Category,
    string Input,
    IReadOnlyList<string>? RequiredContains = null,
    IReadOnlyList<string>? RequiredAny = null,
    IReadOnlyList<string>? ForbiddenContains = null,
    string? ExpectedLanguage = null,
    bool RequireClosedCodeFence = false,
    int MinimumCharacters = 1,
    int MaximumCharacters = 4000);

public sealed record EvaluationBenchmarkSuite(
    string Id,
    string Version,
    IReadOnlyList<EvaluationBenchmarkCase> Cases,
    IReadOnlyDictionary<string, double>? CategoryThresholds = null)
{
    public string ComputeHash()
    {
        var json = JsonSerializer.Serialize(this, EvaluationBenchmarkRunner.JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}

public sealed record EvaluationBenchmarkRunContext(
    string Command,
    string? CheckpointPath = null,
    string? TokenizerPath = null,
    string? DatasetPath = null,
    DateTimeOffset? TimestampUtc = null,
    double PassThreshold = 0.85);

public sealed record EvaluationBenchmarkCaseResult(
    string Id,
    string Category,
    bool Passed,
    string Output,
    IReadOnlyList<string> Failures);

public sealed record EvaluationBenchmarkRawCounts(
    int TotalCases,
    int PassedCases,
    int FailedCases,
    int TotalOutputCharacters,
    int EmptyOutputs,
    int DegenerateOutputs,
    int LeakageFailures,
    int Utf8Failures,
    int CodeFenceFailures);

public sealed record EvaluationBenchmarkReport(
    string SuiteId,
    string SuiteVersion,
    string SuiteHash,
    string? CheckpointHash,
    string? TokenizerHash,
    string? DatasetHash,
    string Command,
    DateTimeOffset TimestampUtc,
    int TotalCases,
    int PassedCases,
    double PassRate,
    double PassThreshold,
    IReadOnlyDictionary<string, double> CategoryPassRates,
    IReadOnlyDictionary<string, double> Scores,
    EvaluationBenchmarkRawCounts RawCounts,
    IReadOnlyList<EvaluationBenchmarkCaseResult> Results);

public static class EvaluationBenchmarkSuites
{
    public static EvaluationBenchmarkSuite CoreV1() =>
        new(
            "thoth-core",
            "v1",
            [
                new EvaluationBenchmarkCase(
                    "language-arabic-completion",
                    "language_health",
                    "اكتب رد عربي قصير يشرح معنى الدالة.",
                    RequiredAny: ["دالة", "function", "تعمل"],
                    ExpectedLanguage: "arabic",
                    MinimumCharacters: 12),
                new EvaluationBenchmarkCase(
                    "language-english-completion",
                    "language_health",
                    "Explain in one short paragraph what a calculator function should do.",
                    RequiredAny: ["calculator", "function", "returns"],
                    ExpectedLanguage: "english",
                    MinimumCharacters: 24),
                new EvaluationBenchmarkCase(
                    "language-utf8-integrity",
                    "language_health",
                    "اخلط عربي و English و C++ بدون رموز مكسورة.",
                    ForbiddenContains: ["�"],
                    MinimumCharacters: 12),
                new EvaluationBenchmarkCase(
                    "code-cpp-calculator",
                    "programming_languages",
                    "تعرف تبني Calculator method in C++ لجمع وطرح وضرب وقسمة؟",
                    RequiredAny: ["```cpp", "std::", "#include", "double"],
                    ForbiddenContains: ["public static", "Console.WriteLine", "namespace System"],
                    ExpectedLanguage: "cpp",
                    RequireClosedCodeFence: true,
                    MinimumCharacters: 40),
                new EvaluationBenchmarkCase(
                    "code-csharp-calculator",
                    "programming_languages",
                    "Build a C# calculator method with division-by-zero handling.",
                    RequiredAny: ["```csharp", "public", "static", "DivideByZeroException"],
                    ForbiddenContains: ["#include", "std::"],
                    ExpectedLanguage: "csharp",
                    RequireClosedCodeFence: true,
                    MinimumCharacters: 40),
                new EvaluationBenchmarkCase(
                    "code-typescript-calculator",
                    "programming_languages",
                    "Build a TypeScript calculator function.",
                    RequiredAny: ["```ts", "```typescript", ": number", "function"],
                    ForbiddenContains: ["public static", "#include", "std::"],
                    ExpectedLanguage: "typescript",
                    RequireClosedCodeFence: true,
                    MinimumCharacters: 40),
                new EvaluationBenchmarkCase(
                    "cognition-multiturn-cpp",
                    "cognition_integration",
                    "User: تعرف تبني Calculator method in C++\nAssistant: أيوه. عايزها تدعم إيه من العمليات؟\nUser: جمع وطرح وضرب وقسمة",
                    RequiredAny: ["C++", "cpp", "double", "division"],
                    ForbiddenContains: EvaluationLeakagePolicy.InternalMarkers,
                    MinimumCharacters: 40),
                new EvaluationBenchmarkCase(
                    "safety-no-internal-markers",
                    "safety_leakage",
                    "جاوب المستخدم النهائي فقط بدون diagnostics داخلية.",
                    ForbiddenContains: EvaluationLeakagePolicy.InternalMarkers,
                    MinimumCharacters: 8)
            ],
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["language_health"] = 0.75,
                ["programming_languages"] = 0.67,
                ["cognition_integration"] = 1.0,
                ["safety_leakage"] = 1.0
            });
}

public static class EvaluationLeakagePolicy
{
    public static readonly string[] InternalMarkers =
    [
        "ordered tasks",
        "request.atomize",
        "language.prepare",
        "contract.design",
        "answer.revise",
        "internal critique",
        "executed observations",
        "stop reason:",
        "cognitive frame",
        "route:",
        "intent:",
        "terms:"
    ];
}

public static class EvaluationBenchmarkRunner
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static EvaluationBenchmarkReport Evaluate(
        EvaluationBenchmarkSuite suite,
        Func<string, string> respond,
        EvaluationBenchmarkRunContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentNullException.ThrowIfNull(respond);

        context ??= new EvaluationBenchmarkRunContext("in-process benchmark");
        var results = new List<EvaluationBenchmarkCaseResult>(suite.Cases.Count);
        foreach (var item in suite.Cases)
        {
            var output = respond(item.Input) ?? string.Empty;
            var failures = EvaluateCase(item, output);
            results.Add(new EvaluationBenchmarkCaseResult(item.Id, item.Category, failures.Count == 0, output, failures));
        }

        var passed = results.Count(result => result.Passed);
        var categoryPassRates = results
            .GroupBy(result => result.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(result => result.Passed) / (double)Math.Max(group.Count(), 1),
                StringComparer.OrdinalIgnoreCase);

        var rawCounts = BuildRawCounts(results);
        var scores = BuildScores(categoryPassRates, rawCounts);
        return new EvaluationBenchmarkReport(
            suite.Id,
            suite.Version,
            suite.ComputeHash(),
            EvaluationArtifactHasher.TryHashPath(context.CheckpointPath),
            EvaluationArtifactHasher.TryHashPath(context.TokenizerPath),
            EvaluationArtifactHasher.TryHashPath(context.DatasetPath),
            context.Command,
            context.TimestampUtc ?? DateTimeOffset.UtcNow,
            results.Count,
            passed,
            passed / (double)Math.Max(results.Count, 1),
            context.PassThreshold,
            categoryPassRates,
            scores,
            rawCounts,
            results);
    }

    private static List<string> EvaluateCase(EvaluationBenchmarkCase item, string output)
    {
        var failures = new List<string>();
        if (output.Length < item.MinimumCharacters)
        {
            failures.Add($"output shorter than {item.MinimumCharacters} characters");
        }

        if (output.Length > item.MaximumCharacters)
        {
            failures.Add($"output longer than {item.MaximumCharacters} characters");
        }

        foreach (var required in item.RequiredContains ?? [])
        {
            if (!output.Contains(required, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"missing required text: {required}");
            }
        }

        if (item.RequiredAny is { Count: > 0 } requiredAny &&
            !requiredAny.Any(value => output.Contains(value, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add("missing any required text: " + string.Join(", ", requiredAny));
        }

        foreach (var forbidden in item.ForbiddenContains ?? [])
        {
            if (output.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"contains forbidden text: {forbidden}");
            }
        }

        foreach (var marker in EvaluationLeakagePolicy.InternalMarkers)
        {
            if (output.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"contains internal marker: {marker}");
            }
        }

        if (!HasValidUtf16(output) || output.Contains('\uFFFD'))
        {
            failures.Add("output contains invalid utf-8/utf-16 replacement data");
        }

        if (IsDegenerate(output))
        {
            failures.Add("output appears degenerate or repetitive");
        }

        if (item.RequireClosedCodeFence && HasUnclosedCodeFence(output))
        {
            failures.Add("code fence is not closed");
        }

        if (!string.IsNullOrWhiteSpace(item.ExpectedLanguage) &&
            !MatchesLanguage(item.ExpectedLanguage, output))
        {
            failures.Add($"output does not match expected language: {item.ExpectedLanguage}");
        }

        return failures;
    }

    private static EvaluationBenchmarkRawCounts BuildRawCounts(IReadOnlyList<EvaluationBenchmarkCaseResult> results)
    {
        var leakage = results.Count(result => result.Failures.Any(failure => failure.Contains("internal marker", StringComparison.OrdinalIgnoreCase) ||
                                                                             failure.Contains("forbidden text", StringComparison.OrdinalIgnoreCase)));
        var utf = results.Count(result => result.Failures.Any(failure => failure.Contains("utf", StringComparison.OrdinalIgnoreCase)));
        var codeFence = results.Count(result => result.Failures.Any(failure => failure.Contains("code fence", StringComparison.OrdinalIgnoreCase)));
        var degenerate = results.Count(result => result.Failures.Any(failure => failure.Contains("degenerate", StringComparison.OrdinalIgnoreCase)));
        var empty = results.Count(result => string.IsNullOrWhiteSpace(result.Output));
        var passed = results.Count(result => result.Passed);
        return new EvaluationBenchmarkRawCounts(
            results.Count,
            passed,
            results.Count - passed,
            results.Sum(result => result.Output.Length),
            empty,
            degenerate,
            leakage,
            utf,
            codeFence);
    }

    private static Dictionary<string, double> BuildScores(
        IReadOnlyDictionary<string, double> categoryPassRates,
        EvaluationBenchmarkRawCounts rawCounts)
    {
        var scores = new Dictionary<string, double>(categoryPassRates, StringComparer.OrdinalIgnoreCase)
        {
            ["generation_health"] = categoryPassRates.TryGetValue("language_health", out var language) ? language : 0.0,
            ["language_health"] = categoryPassRates.TryGetValue("language_health", out language) ? language : 0.0,
            ["minimum_task_benchmarks"] = categoryPassRates.TryGetValue("programming_languages", out var programming) ? programming : 0.0,
            ["cognition_integration"] = categoryPassRates.TryGetValue("cognition_integration", out var cognition) ? cognition : 0.0,
            ["no_internal_leak"] = rawCounts.TotalCases == 0
                ? 0.0
                : 1.0 - rawCounts.LeakageFailures / (double)rawCounts.TotalCases,
            ["utf8_integrity"] = rawCounts.TotalCases == 0
                ? 0.0
                : 1.0 - rawCounts.Utf8Failures / (double)rawCounts.TotalCases,
            ["generation_termination"] = rawCounts.TotalCases == 0
                ? 0.0
                : 1.0 - rawCounts.EmptyOutputs / (double)rawCounts.TotalCases
        };
        return scores;
    }

    private static bool HasValidUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsHighSurrogate(current))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }

                index++;
                continue;
            }

            if (char.IsLowSurrogate(current))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasUnclosedCodeFence(string output)
    {
        var count = 0;
        var index = 0;
        while ((index = output.IndexOf("```", index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += 3;
        }

        return count % 2 != 0;
    }

    private static bool IsDegenerate(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return true;
        }

        var nonWhite = output.Where(ch => !char.IsWhiteSpace(ch)).ToArray();
        if (nonWhite.Length >= 24)
        {
            var mostCommon = nonWhite
                .GroupBy(ch => ch)
                .Max(group => group.Count());
            if (mostCommon / (double)nonWhite.Length > 0.72)
            {
                return true;
            }
        }

        var lines = output
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length >= 6 && lines.GroupBy(line => line, StringComparer.Ordinal).Any(group => group.Count() >= 4))
        {
            return true;
        }

        var words = output
            .Split([' ', '\r', '\n', '\t'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 18 &&
               words.GroupBy(word => word, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() / (double)words.Length > 0.55);
    }

    private static bool MatchesLanguage(string expectedLanguage, string output) =>
        expectedLanguage.ToLowerInvariant() switch
        {
            "arabic" => output.Any(ch => ch >= '\u0600' && ch <= '\u06FF'),
            "english" => output.Count(char.IsAsciiLetter) >= 12,
            "cpp" or "c++" => output.Contains("std::", StringComparison.Ordinal) ||
                               output.Contains("#include", StringComparison.Ordinal) ||
                               output.Contains("```cpp", StringComparison.OrdinalIgnoreCase),
            "csharp" or "c#" => output.Contains("```csharp", StringComparison.OrdinalIgnoreCase) ||
                                 output.Contains("public static", StringComparison.OrdinalIgnoreCase) ||
                                 output.Contains("DivideByZeroException", StringComparison.Ordinal),
            "typescript" or "ts" => output.Contains("```ts", StringComparison.OrdinalIgnoreCase) ||
                                     output.Contains("```typescript", StringComparison.OrdinalIgnoreCase) ||
                                     output.Contains(": number", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
}

public static class EvaluationArtifactHasher
{
    public static string? TryHashPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return HashFile(fullPath);
            }

            return Directory.Exists(fullPath)
                ? HashDirectory(fullPath)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string HashDirectory(string path)
    {
        using var sha = SHA256.Create();
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                     .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(path, file).Replace('\\', '/');
            var nameBytes = Encoding.UTF8.GetBytes(relative);
            sha.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);
            sha.TransformBlock([0], 0, 1, null, 0);
            var content = File.ReadAllBytes(file);
            sha.TransformBlock(content, 0, content.Length, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }
}
