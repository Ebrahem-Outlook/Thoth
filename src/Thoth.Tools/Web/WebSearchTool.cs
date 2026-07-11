using Thoth.Core.Tools;

namespace Thoth.Tools.Web;

public sealed class WebSearchTool(HttpClient httpClient) : IAgentTool
{
    public string Name => "web.search";

    public string Description => "Searches the public web and returns ranked result titles, URLs, and snippets.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("query", "Web search query."),
        new("maxResults", "Maximum search results to return.", false, "integer")
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
        try
        {
            var results = await WebLookup.SearchAsync(httpClient, query, maxResults, cancellationToken);
            return ToolResult.Success(
                Name,
                WebLookup.FormatSearchResults(query, results),
                new Dictionary<string, string>
                {
                    ["query"] = query,
                    ["resultCount"] = results.Count.ToString()
                });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or ArgumentException)
        {
            return ToolResult.Failure(Name, $"Web search failed: {exception.Message}", new Dictionary<string, string>
            {
                ["query"] = query
            });
        }
    }
}
