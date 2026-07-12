using Thoth.Cognition.Concepts;
using Thoth.Cognition.Tasks;

namespace Thoth.Cognition.Procedures;

public sealed record ProcedureExecutionResult(
    string ProcedureId,
    string Content,
    IReadOnlyList<string> VerificationNotes);

public sealed class ProcedureRegistry
{
    public bool TryExecute(CodeGenerationTask task, out ProcedureExecutionResult result)
    {
        if (string.Equals(task.Behavior, CodeTaskBehaviors.Calculator, StringComparison.OrdinalIgnoreCase) &&
            task.IsReady)
        {
            result = CalculatorProcedure.Execute(task);
            return true;
        }

        result = new ProcedureExecutionResult("unsupported", string.Empty, []);
        return false;
    }
}

internal static class CalculatorProcedure
{
    public static ProcedureExecutionResult Execute(CodeGenerationTask task)
    {
        var code = task.Language switch
        {
            CognitiveProgrammingLanguage.CSharp => RenderCSharp(),
            CognitiveProgrammingLanguage.TypeScript or CognitiveProgrammingLanguage.JavaScript => RenderTypeScript(),
            CognitiveProgrammingLanguage.Cpp => RenderCpp(),
            _ => string.Empty
        };

        var fence = task.Language.CodeFence();
        var display = task.Language.DisplayName();
        var content = string.Join(Environment.NewLine,
            $"Here is a {display} calculator {ArtifactLabel(task.ArtifactKind)}:",
            "",
            $"```{fence}",
            code.TrimEnd(),
            "```",
            "",
            "It supports add, subtract, multiply, and divide, with guards for division by zero and unsupported operations.");

        return new ProcedureExecutionResult(
            "calculator.method.v1",
            content,
            ["operation enum/union present", "division by zero guard present", "unsupported operation guard present"]);
    }

    private static string ArtifactLabel(CodeArtifactKind artifactKind) => artifactKind switch
    {
        CodeArtifactKind.Method => "method",
        CodeArtifactKind.Function => "function",
        CodeArtifactKind.Class => "class",
        _ => "function"
    };

    private static string RenderCSharp() => """
        public enum CalculatorOperation
        {
            Add,
            Subtract,
            Multiply,
            Divide
        }

        public static decimal Calculate(decimal left, decimal right, CalculatorOperation operation)
        {
            return operation switch
            {
                CalculatorOperation.Add => left + right,
                CalculatorOperation.Subtract => left - right,
                CalculatorOperation.Multiply => left * right,
                CalculatorOperation.Divide when right != 0 => left / right,
                CalculatorOperation.Divide => throw new DivideByZeroException("Cannot divide by zero."),
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported calculator operation.")
            };
        }
        """;

    private static string RenderTypeScript() => """
        export type CalculatorOperation = 'add' | 'subtract' | 'multiply' | 'divide';

        export function calculate(left: number, right: number, operation: CalculatorOperation): number {
          switch (operation) {
            case 'add': return left + right;
            case 'subtract': return left - right;
            case 'multiply': return left * right;
            case 'divide':
              if (right === 0) throw new Error('Cannot divide by zero.');
              return left / right;
            default:
              throw new Error(`Unsupported calculator operation: ${String(operation)}`);
          }
        }
        """;

    private static string RenderCpp() => """
        #include <stdexcept>

        enum class CalculatorOperation
        {
            Add,
            Subtract,
            Multiply,
            Divide
        };

        double Calculate(double left, double right, CalculatorOperation operation)
        {
            switch (operation)
            {
                case CalculatorOperation::Add:
                    return left + right;
                case CalculatorOperation::Subtract:
                    return left - right;
                case CalculatorOperation::Multiply:
                    return left * right;
                case CalculatorOperation::Divide:
                    if (right == 0)
                    {
                        throw std::invalid_argument("Cannot divide by zero.");
                    }

                    return left / right;
                default:
                    throw std::invalid_argument("Unsupported calculator operation.");
            }
        }
        """;
}
