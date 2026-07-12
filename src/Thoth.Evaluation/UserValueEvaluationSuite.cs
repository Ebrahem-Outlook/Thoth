using System.Text.Json;

namespace Thoth.Evaluation;

public sealed record UserValueEvaluationCase(
    string Id,
    string Category,
    string Input,
    IReadOnlyList<string>? RequiredContains = null,
    IReadOnlyList<string>? ForbiddenContains = null);

public sealed record UserValueCaseResult(
    string Id,
    string Category,
    bool Passed,
    IReadOnlyList<string> Failures);

public sealed record UserValueEvaluationReport(
    int TotalCases,
    int PassedCases,
    double PassRate,
    IReadOnlyDictionary<string, double> CategoryPassRates,
    IReadOnlyList<UserValueCaseResult> Results);

public static class UserValueEvaluationSuite
{
    public static async Task<IReadOnlyList<UserValueEvaluationCase>> LoadJsonlAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var cases = new List<UserValueEvaluationCase>();
        await foreach (var line in File.ReadLinesAsync(path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<UserValueEvaluationCase>(
                line,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (item is not null)
            {
                cases.Add(item);
            }
        }

        return cases;
    }

    public static UserValueEvaluationReport Evaluate(
        IReadOnlyList<UserValueEvaluationCase> cases,
        Func<string, string> respond)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(respond);

        var results = new List<UserValueCaseResult>();
        foreach (var item in cases)
        {
            var output = respond(item.Input);
            var failures = new List<string>();
            foreach (var required in item.RequiredContains ?? [])
            {
                if (!output.Contains(required, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"missing required text: {required}");
                }
            }

            foreach (var forbidden in item.ForbiddenContains ?? [])
            {
                if (output.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"contains forbidden text: {forbidden}");
                }
            }

            results.Add(new UserValueCaseResult(item.Id, item.Category, failures.Count == 0, failures));
        }

        var passRates = results
            .GroupBy(result => result.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(result => result.Passed) / (double)Math.Max(group.Count(), 1),
                StringComparer.OrdinalIgnoreCase);
        var passed = results.Count(result => result.Passed);
        return new UserValueEvaluationReport(
            results.Count,
            passed,
            passed / (double)Math.Max(results.Count, 1),
            passRates,
            results);
    }
}
