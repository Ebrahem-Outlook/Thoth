using System.Text;
using Thoth.Core.Tools;

namespace Thoth.Tools.Http;

public sealed class HttpGetTool(HttpClient httpClient) : IAgentTool
{
    public string Name => "http.get";

    public string Description => "Fetches text from an HTTP or HTTPS URL.";

    public IReadOnlyList<ToolParameter> Parameters { get; } =
    [
        new("url", "HTTP or HTTPS URL to fetch."),
        new("maxChars", "Maximum characters to return.", false, "integer")
    ];

    public async ValueTask<ToolResult> InvokeAsync(
        ToolInvocation invocation,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var url = invocation.GetString("url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return ToolResult.Failure(Name, "A valid http/https URL is required.");
        }

        var maxChars = Math.Clamp(invocation.GetInt("maxChars", 12000), 1, 100000);
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (content.Length > maxChars)
        {
            content = content[..maxChars] + "\n[truncated]";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Status: {(int)response.StatusCode} {response.ReasonPhrase}");
        builder.AppendLine($"Content-Type: {response.Content.Headers.ContentType}");
        builder.AppendLine();
        builder.Append(content);

        return response.IsSuccessStatusCode
            ? ToolResult.Success(Name, builder.ToString())
            : ToolResult.Failure(Name, builder.ToString());
    }
}
