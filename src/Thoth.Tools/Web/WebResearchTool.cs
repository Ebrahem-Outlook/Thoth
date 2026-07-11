using System.Text;
using Thoth.Core.Tools;

namespace Thoth.Tools.Web;

public sealed class WebResearchTool(HttpClient httpClient) : IAgentTool
{
    public string Name => "web.research";

    public string Description => "Searches the public web, reads top pages, and returns a compact research brief with sources.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("query", "Research question or search query."),
        new("maxResults", "Maximum search results to list.", false, "integer"),
        new("maxPages", "Maximum result pages to read and summarize.", false, "integer")
    ];

    public async ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var query = invocation.GetString("query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Failure(Name, "query is required.");
        }

        var maxResults = Math.Clamp(invocation.GetInt("maxResults", 8), 1, 20);
        var maxPages = Math.Clamp(invocation.GetInt("maxPages", 3), 0, 5);

        try
        {
            var results = await WebLookup.SearchAsync(httpClient, query, maxResults, cancellationToken);
            var builder = new StringBuilder();
            builder.AppendLine($"Research query: {query}");
            builder.AppendLine();
            builder.AppendLine("Search results:");
            builder.AppendLine(WebLookup.FormatSearchResults(query, results));

            var readPages = 0;
            if (maxPages > 0 && results.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Read summaries:");
                foreach (var result in results.Take(maxPages))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var page = await WebLookup.ReadAsync(
                            httpClient,
                            result.Url,
                            query,
                            maxChars: 12000,
                            summarySentences: 4,
                            cancellationToken);
                        readPages++;
                        builder.AppendLine();
                        builder.AppendLine($"Source {readPages}: {(string.IsNullOrWhiteSpace(page.Title) ? result.Title : page.Title)}");
                        builder.AppendLine($"URL: {page.Url}");
                        builder.AppendLine(string.IsNullOrWhiteSpace(page.Summary)
                            ? "- No summary extracted."
                            : page.Summary);
                    }
                    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or ArgumentException)
                    {
                        builder.AppendLine();
                        builder.AppendLine($"Skipped: {result.Url}");
                        builder.AppendLine($"Reason: {exception.Message}");
                    }
                }
            }

            return ToolResult.Success(
                Name,
                builder.ToString().Trim(),
                new Dictionary<string, string>
                {
                    ["query"] = query,
                    ["resultCount"] = results.Count.ToString(),
                    ["pagesRead"] = readPages.ToString()
                });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or ArgumentException)
        {
            return ToolResult.Failure(Name, $"Web research failed: {exception.Message}", new Dictionary<string, string>
            {
                ["query"] = query
            });
        }
    }
}
