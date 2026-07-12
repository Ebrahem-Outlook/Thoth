using System.Text;
using System.Text.Json;

namespace Thoth.Training;

public static class InstructionDatasetLoader
{
    public static async Task<InstructionDataset> LoadJsonlAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var files = File.Exists(fullPath)
            ? new[] { fullPath }
            : Directory.Exists(fullPath)
                ? Directory.EnumerateFiles(fullPath, "*.jsonl", SearchOption.AllDirectories).OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToArray()
                : throw new FileNotFoundException("Instruction dataset path was not found.", fullPath);

        var examples = new List<InstructionExample>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var lineNumber = 0;
            await foreach (var line in File.ReadLinesAsync(file, Encoding.UTF8, cancellationToken))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                InstructionExample? example;
                try
                {
                    example = JsonSerializer.Deserialize<InstructionExample>(
                        line,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException($"{file}:{lineNumber} is not valid instruction JSONL.", exception);
                }

                if (example is null)
                {
                    continue;
                }

                Validate(example, file, lineNumber);
                if (seenIds.Add(example.Id))
                {
                    examples.Add(example);
                }
            }
        }

        if (examples.Count == 0)
        {
            throw new InvalidDataException("Instruction dataset did not contain any examples.");
        }

        return new InstructionDataset(fullPath, examples);
    }

    public static string ToTrainingText(InstructionExample example)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"<task:{example.Task}>");
        foreach (var message in example.Messages)
        {
            builder.Append(message.Role.Trim().ToLowerInvariant());
            builder.Append(": ");
            builder.AppendLine(message.Content.Trim());
        }

        return builder.ToString().Trim();
    }

    private static void Validate(InstructionExample example, string file, int lineNumber)
    {
        if (string.IsNullOrWhiteSpace(example.Id))
        {
            throw new InvalidDataException($"{file}:{lineNumber} is missing id.");
        }

        if (string.IsNullOrWhiteSpace(example.Language))
        {
            throw new InvalidDataException($"{file}:{lineNumber} is missing language.");
        }

        if (string.IsNullOrWhiteSpace(example.Task))
        {
            throw new InvalidDataException($"{file}:{lineNumber} is missing task.");
        }

        if (example.Messages.Count == 0 ||
            !example.Messages.Any(message => message.Role.Equals("user", StringComparison.OrdinalIgnoreCase)) ||
            !example.Messages.Any(message => message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"{file}:{lineNumber} must include user and assistant messages.");
        }

        foreach (var message in example.Messages)
        {
            if (string.IsNullOrWhiteSpace(message.Role) ||
                string.IsNullOrWhiteSpace(message.Content))
            {
                throw new InvalidDataException($"{file}:{lineNumber} contains an empty message role or content.");
            }
        }
    }
}

public sealed record InstructionDataset(
    string SourcePath,
    IReadOnlyList<InstructionExample> Examples);

public sealed record InstructionExample(
    string Id,
    string Language,
    string Task,
    IReadOnlyList<InstructionMessage> Messages,
    bool ValidationOnly = false);

public sealed record InstructionMessage(
    string Role,
    string Content);
