using Thoth.Training;

namespace Thoth.Tests.Neural;

public sealed class InstructionDatasetLoaderTests
{
    [Fact]
    public async Task LoadJsonlAsync_LoadsAndValidatesInstructionExamples()
    {
        var path = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"), "instructions.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
        {"id":"ar-ts-001","language":"ar","task":"clarification","messages":[{"role":"user","content":"ممكن تبني method Type script"},{"role":"assistant","content":"أكيد. الميثود في TypeScript مطلوب منها تعمل إيه؟"}]}
        {"id":"ar-ts-001","language":"ar","task":"clarification","messages":[{"role":"user","content":"duplicate"},{"role":"assistant","content":"ignored"}]}
        """);

        var dataset = await InstructionDatasetLoader.LoadJsonlAsync(path);
        var trainingText = InstructionDatasetLoader.ToTrainingText(dataset.Examples[0]);

        Assert.Single(dataset.Examples);
        Assert.Contains("user:", trainingText);
        Assert.Contains("assistant:", trainingText);
        Assert.Contains("TypeScript", trainingText);
    }
}
