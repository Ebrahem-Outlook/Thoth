using Thoth.Evaluation;
using Thoth.Model;
using Thoth.Model.Persistence;
using Thoth.Tokenization;

namespace Thoth.Tests.Neural;

public sealed class EvaluationHarnessTests
{
    [Fact]
    public void Evaluate_ComputesCategoryPassRatesAndForbiddenTextFailures()
    {
        var report = UserValueEvaluationSuite.Evaluate(
            [
                new UserValueEvaluationCase(
                    "case-1",
                    "no_internal_leak",
                    "hello",
                    ["clean"],
                    ["ordered tasks"]),
                new UserValueEvaluationCase(
                    "case-2",
                    "no_internal_leak",
                    "bad",
                    ["clean"],
                    ["ordered tasks"])
            ],
            input => input == "bad" ? "ordered tasks" : "clean answer");

        Assert.Equal(2, report.TotalCases);
        Assert.Equal(1, report.PassedCases);
        Assert.Equal(0.5, report.CategoryPassRates["no_internal_leak"]);
    }

    [Fact]
    public void BenchmarkRunner_BuildsMachineReadableCoreReport()
    {
        var suite = EvaluationBenchmarkSuites.CoreV1();
        var report = EvaluationBenchmarkRunner.Evaluate(
            suite,
            RespondWithUsefulAnswer,
            new EvaluationBenchmarkRunContext(
                "thoth model evaluate --suite data/evaluation/core-v1.jsonl",
                TimestampUtc: DateTimeOffset.UnixEpoch));

        Assert.Equal("thoth-core", report.SuiteId);
        Assert.Equal("v1", report.SuiteVersion);
        Assert.Equal(64, report.SuiteHash.Length);
        Assert.Equal(suite.Cases.Count, report.TotalCases);
        Assert.Equal(report.TotalCases, report.PassedCases);
        Assert.Equal(1.0, report.PassRate);
        Assert.Equal(1.0, report.Scores["language_health"]);
        Assert.Equal(1.0, report.Scores["minimum_task_benchmarks"]);
        Assert.Equal(1.0, report.Scores["no_internal_leak"]);
        Assert.Equal(0, report.RawCounts.LeakageFailures);
    }

    [Fact]
    public void BenchmarkRunner_FailsInternalMarkersAndUnclosedCodeFences()
    {
        var suite = new EvaluationBenchmarkSuite(
            "leak-check",
            "v1",
            [
                new EvaluationBenchmarkCase(
                    "leak",
                    "safety_leakage",
                    "answer cleanly",
                    ForbiddenContains: EvaluationLeakagePolicy.InternalMarkers,
                    RequireClosedCodeFence: true)
            ]);

        var report = EvaluationBenchmarkRunner.Evaluate(
            suite,
            _ => "ordered tasks\n```csharp\npublic static double Add(double a, double b) => a + b;");

        Assert.Equal(0, report.PassedCases);
        Assert.Equal(1, report.RawCounts.LeakageFailures);
        Assert.Equal(1, report.RawCounts.CodeFenceFailures);
        Assert.Equal(0.0, report.Scores["no_internal_leak"]);
        Assert.Contains(report.Results[0].Failures, failure => failure.Contains("internal marker", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Results[0].Failures, failure => failure.Contains("code fence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task QualityGate_DoesNotQualifyLossOnlyMetrics()
    {
        var path = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"), "checkpoint.bin");
        var tokenizer = new ByteTokenizer();
        var model = new RecurrentLanguageModel(new NeuralModelConfig(
            tokenizer.VocabularySize,
            EmbeddingSize: 8,
            HiddenSize: 12,
            SequenceLength: 8,
            Seed: 22));
        var tokens = tokenizer.Encode("quality-check").ToArray();
        model.TrainSequence(tokens[..8], tokens[1..9], 0.005);
        await ModelCheckpoint.SaveAsync(path, model);
        await ModelCheckpointQualityGate.SaveMetadataAsync(
            path,
            ModelCheckpointMetadata.CreateUnqualified(
                model,
                metrics: new CheckpointEvaluationMetrics(
                    128,
                    4,
                    1.5,
                    4.5,
                    new Dictionary<string, double>
                    {
                        ["loss_health"] = 0.9,
                        ["finite_loss"] = 1.0
                    })));

        var inspection = await ModelCheckpointQualityGate.InspectAsync(path);

        Assert.Equal(ModelCheckpointStatus.Unqualified, inspection.Status);
        Assert.Contains(inspection.Reasons, reason => reason.Contains("role-specific", StringComparison.OrdinalIgnoreCase));
    }

    private static string RespondWithUsefulAnswer(string input)
    {
        if (input.Contains("TypeScript", StringComparison.OrdinalIgnoreCase))
        {
            return """
                   ```ts
                   function calculate(a: number, b: number, op: string): number {
                     if (op === "+") return a + b;
                     if (op === "-") return a - b;
                     if (op === "*") return a * b;
                     if (op === "/" && b !== 0) return a / b;
                     throw new Error("division by zero");
                   }
                   ```
                   """;
        }

        if (input.Contains("C#", StringComparison.OrdinalIgnoreCase))
        {
            return """
                   ```csharp
                   public static double Calculate(double a, double b, char op) =>
                       op == '/' && b == 0 ? throw new DivideByZeroException() :
                       op == '+' ? a + b : op == '-' ? a - b : op == '*' ? a * b : a / b;
                   ```
                   """;
        }

        if (input.Contains("C++", StringComparison.OrdinalIgnoreCase) || input.Contains("Calculator method", StringComparison.OrdinalIgnoreCase))
        {
            return """
                   ```cpp
                   #include <stdexcept>
                   double calculate(double a, double b, char op) {
                     if (op == '/' && b == 0) throw std::invalid_argument("division by zero");
                     if (op == '+') return a + b;
                     if (op == '-') return a - b;
                     if (op == '*') return a * b;
                     return a / b;
                   }
                   ```
                   """;
        }

        if (input.Contains("عربي", StringComparison.OrdinalIgnoreCase))
        {
            return "الدالة تستقبل مدخلات وتعمل عليها ثم ترجع نتيجة واضحة للمستخدم.";
        }

        if (input.Contains("English", StringComparison.OrdinalIgnoreCase) ||
            input.Contains("calculator function", StringComparison.OrdinalIgnoreCase))
        {
            return "The calculator function reads the operation, validates inputs, and returns the computed result.";
        }

        return "Clean final answer with no internal diagnostics.";
    }
}
