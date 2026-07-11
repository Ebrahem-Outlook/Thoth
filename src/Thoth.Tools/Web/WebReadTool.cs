using System.Text;
using Thoth.Core.Tools;

namespace Thoth.Tools.Web;

public sealed class WebReadTool(HttpClient httpClient) : IAgentTool
{
    public string Name => "web.read";

    public string Description => "Reads a public web page, extracts readable text, and returns a focused summary.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("url", "HTTP or HTTPS URL to read."),
        new("query", "Optional focus query for the summary.", false),
        new("maxChars", "Maximum readable characters to return.", false, "integer"),
        new("summarySentences", "Maximum summary bullets.", false, "integer")
    ];

    public async ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var url = invocation.GetString("url");
        var query = invocation.GetString("query");
        var maxChars = Math.Clamp(invocation.GetInt("maxChars", 16000), 1000, 100000);
        var summarySentences = Math.Clamp(invocation.GetInt("summarySentences", 5), 1, 12);

        try
        {
            var page = await WebLookup.ReadAsync(httpClient, url, query, maxChars, summarySentences, cancellationToken);
            var builder = new StringBuilder();
            builder.AppendLine($"Title: {(string.IsNullOrWhiteSpace(page.Title) ? "(untitled)" : page.Title)}");
            builder.AppendLine($"URL: {page.Url}");
            builder.AppendLine($"Content-Type: {page.ContentType}");
            builder.AppendLine();
            builder.AppendLine("Summary:");
            builder.AppendLine(string.IsNullOrWhiteSpace(page.Summary) ? "- No concise summary could be extracted." : page.Summary);
            builder.AppendLine();
            builder.AppendLine("Extract:");
            builder.AppendLine(page.Text);

            return ToolResult.Success(
                Name,
                builder.ToString().Trim(),
                new Dictionary<string, string>
                {
                    ["url"] = page.Url,
                    ["title"] = page.Title,
                    ["contentType"] = page.ContentType
                });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or ArgumentException)
        {
            return ToolResult.Failure(Name, $"Web read failed: {exception.Message}", new Dictionary<string, string>
            {
                ["url"] = url
            });
        }
    }
}
