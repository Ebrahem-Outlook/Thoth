using Thoth.Evaluation;

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
}
