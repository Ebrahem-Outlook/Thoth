using System.Net;
using Thoth.Core.Configuration;
using Thoth.Core.Memory;
using Thoth.Core.Tools;
using Thoth.Sandbox.Policies;
using Thoth.Tools.Web;

namespace Thoth.Tests.Tools;

public sealed class WebToolsTests
{
    [Fact]
    public async Task WebSearchTool_ReturnsRankedResults()
    {
        using var client = new HttpClient(new StaticHttpHandler(_ => Html("""
            <html><body>
              <a class="result__a" href="https://example.com/agent">Example Agent Result</a>
              <div class="result__snippet">Useful snippet about AI agents that search and summarize.</div>
            </body></html>
            """)));
        var tool = new WebSearchTool(client);

        var result = await tool.InvokeAsync(
            new ToolInvocation("web.search", new Dictionary<string, string?>
            {
                ["query"] = "AI agents",
                ["maxResults"] = "5"
            }),
            CreateContext());

        Assert.True(result.Succeeded, result.Content);
        Assert.Contains("Example Agent Result", result.Content);
        Assert.Contains("https://example.com/agent", result.Content);
        Assert.Contains("Useful snippet", result.Content);
    }

    [Fact]
    public async Task WebReadTool_ExtractsReadableTextAndSummary()
    {
        using var client = new HttpClient(new StaticHttpHandler(_ => Html("""
            <html>
              <head><title>Agent Page</title><style>.hidden{display:none}</style></head>
              <body>
                <script>window.noise = true;</script>
                <main>
                  <p>AI agents can search the web, inspect sources, and summarize useful findings for users.</p>
                  <p>Good agent tools expose clear inputs and return evidence that the model can cite later.</p>
                </main>
              </body>
            </html>
            """)));
        var tool = new WebReadTool(client);

        var result = await tool.InvokeAsync(
            new ToolInvocation("web.read", new Dictionary<string, string?>
            {
                ["url"] = "https://example.com/agent",
                ["query"] = "agent tools summarize sources"
            }),
            CreateContext());

        Assert.True(result.Succeeded, result.Content);
        Assert.Contains("Title: Agent Page", result.Content);
        Assert.Contains("Summary:", result.Content);
        Assert.Contains("AI agents can search the web", result.Content);
        Assert.DoesNotContain("<script>", result.Content);
    }

    [Fact]
    public async Task WebResearchTool_SearchesReadsAndReturnsSources()
    {
        using var client = new HttpClient(new StaticHttpHandler(request =>
        {
            if (request.RequestUri?.Host.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Html("""
                    <html><body>
                      <a class="result__a" href="https://example.com/agent">Example Agent Result</a>
                      <div class="result__snippet">AI agent search result.</div>
                    </body></html>
                    """);
            }

            return Html("""
                <html>
                  <head><title>Agent Source</title></head>
                  <body>
                    <article>
                      <p>Open source agents often combine a planner with tools for web search, file reading, and code execution.</p>
                      <p>The research step should keep URLs attached so the final answer can cite its sources.</p>
                    </article>
                  </body>
                </html>
                """);
        }));
        var tool = new WebResearchTool(client);

        var result = await tool.InvokeAsync(
            new ToolInvocation("web.research", new Dictionary<string, string?>
            {
                ["query"] = "open source ai agents",
                ["maxResults"] = "3",
                ["maxPages"] = "1"
            }),
            CreateContext());

        Assert.True(result.Succeeded, result.Content);
        Assert.Contains("Research query: open source ai agents", result.Content);
        Assert.Contains("Source 1: Agent Source", result.Content);
        Assert.Contains("URL: https://example.com/agent", result.Content);
        Assert.Contains("planner with tools", result.Content);
    }

    private static ToolContext CreateContext()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "thoth-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var policy = new LocalExecutionPolicy(new SandboxOptions());
        return new ToolContext(workspace, new InMemoryMemoryStore(), policy);
    }

    private static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html")
        };

    private sealed class StaticHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
