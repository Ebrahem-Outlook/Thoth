using Thoth.Cognition.Concepts;
using Thoth.Cognition.Procedures;
using Thoth.Cognition.Tasks;
using Thoth.Core.Understanding;

namespace Thoth.Tests.Cognition;

public sealed class CognitionPipelineTests
{
    [Theory]
    [InlineData("build C++ method calculator")]
    [InlineData("build cpp method calculator")]
    [InlineData("build c plus plus method calculator")]
    public void CodeTaskExtractor_DetectsCppCalculatorAliases(string text)
    {
        var extractor = new CodeTaskExtractor();

        var task = extractor.ExtractNewTask(Guid.NewGuid(), text);

        Assert.NotNull(task);
        Assert.Equal(CognitiveProgrammingLanguage.Cpp, task!.Language);
        Assert.Equal(CodeTaskBehaviors.Calculator, task.Behavior);
        Assert.Equal(Thoth.Cognition.Tasks.TaskStatus.Ready, task.Status);
    }

    [Theory]
    [InlineData("build C++ method")]
    [InlineData("\u0627\u0639\u0645\u0644\u064a method \u0633\u064a \u0628\u0644\u0633 \u0628\u0644\u0633")]
    public void LegacyLanguageDetector_DetectsCppAliases(string text)
    {
        var match = ProgrammingLanguageDetector.Detect(text);

        Assert.NotNull(match);
        Assert.Equal(ProgrammingLanguage.Cpp, match!.Language);
    }

    [Fact]
    public void ArithmeticOperationMatcher_NormalizesArabicDivisionTypo()
    {
        var operation = ArithmeticOperationMatcher.Match("\u0642\u0645\u0633\u0647");

        Assert.Equal(ArithmeticOperation.Divide, operation);
    }

    [Fact]
    public void TaskMerger_MergesSecondTurnIntoIncompleteCodeTask()
    {
        var conversationId = Guid.NewGuid();
        var extractor = new CodeTaskExtractor();
        var merger = new TaskMerger(extractor);
        var first = extractor.ExtractNewTask(conversationId, "can you build C# method");

        var merged = merger.Merge(first!, "work as calculator");

        Assert.Equal(CognitiveProgrammingLanguage.CSharp, merged.Language);
        Assert.Equal(CodeArtifactKind.Method, merged.ArtifactKind);
        Assert.Equal(CodeTaskBehaviors.Calculator, merged.Behavior);
        Assert.Equal(Thoth.Cognition.Tasks.TaskStatus.Ready, merged.Status);
        Assert.Empty(merged.MissingSlots);
    }

    [Fact]
    public void ProcedureRegistry_GeneratesVerifiedCppCalculator()
    {
        var task = new CodeTaskExtractor().ExtractNewTask(Guid.NewGuid(), "build C++ method calculator");
        var registry = new ProcedureRegistry();

        var executed = registry.TryExecute(task!, out var result);

        Assert.True(executed);
        Assert.Contains("```cpp", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enum class CalculatorOperation", result.Content, StringComparison.Ordinal);
        Assert.Contains("std::invalid_argument", result.Content, StringComparison.Ordinal);
    }
}
