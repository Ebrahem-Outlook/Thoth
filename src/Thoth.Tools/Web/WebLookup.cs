using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Thoth.Tools.Web;

internal static class WebLookup
{
    private static readonly Regex SearchAnchorRegex = new(
        @"<a[^>]+class=""[^""]*(?:result__a|result-link)[^""]*""[^>]+href=""(?<url>[^""]+)""[^>]*>(?<title>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SnippetRegex = new(
        @"<(?<tag>div|a|td|span)[^>]+class=""[^""]*(?:result__snippet|result-snippet|snippet)[^""]*""[^>]*>(?<snippet>.*?)</\k<tag>>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AnchorRegex = new(
        @"<a[^>]+href=""(?<url>https?://[^""]+)""[^>]*>(?<title>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public static async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        HttpClient httpClient,
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        maxResults = Math.Clamp(maxResults, 1, 20);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"https://duckduckgo.com/html/?q={Uri.EscapeDataString(query)}"));
        ApplyBrowserHeaders(request);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Search request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var results = ParseSearchResults(html, maxResults);
        if (IsSearchGate(html, results))
        {
            return await SearchBingAsync(httpClient, query, maxResults, cancellationToken);
        }

        return results;
    }

    public static async Task<WebPageReadResult> ReadAsync(
        HttpClient httpClient,
        string url,
        string query,
        int maxChars,
        int summarySentences,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("A valid http/https URL is required.", nameof(url));
        }

        maxChars = Math.Clamp(maxChars, 1000, 100000);
        summarySentences = Math.Clamp(summarySentences, 1, 12);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        ApplyBrowserHeaders(request);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Read request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var title = ExtractTitle(content);
        var text = ExtractReadableText(content, contentType);
        if (text.Length > maxChars)
        {
            text = text[..maxChars].TrimEnd() + "\n[truncated]";
        }

        var summary = Summarize(text, query, summarySentences);
        return new WebPageReadResult(uri.ToString(), title, text, summary, contentType);
    }

    public static IReadOnlyList<WebSearchResult> ParseSearchResults(string html, int maxResults)
    {
        var results = new List<WebSearchResult>();
        var matches = SearchAnchorRegex.Matches(html);
        for (var index = 0; index < matches.Count && results.Count < maxResults; index++)
        {
            var match = matches[index];
            var nextIndex = index + 1 < matches.Count ? matches[index + 1].Index : html.Length;
            var block = html[match.Index..Math.Min(nextIndex, match.Index + 2500)];
            AddResult(results, match.Groups["title"].Value, match.Groups["url"].Value, ExtractSnippet(block));
        }

        if (results.Count == 0)
        {
            foreach (Match match in AnchorRegex.Matches(html))
            {
                if (results.Count >= maxResults)
                {
                    break;
                }

                AddResult(results, match.Groups["title"].Value, match.Groups["url"].Value, string.Empty);
            }
        }

        return results
            .DistinctBy(result => result.Url, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToArray();
    }

    public static string ExtractReadableText(string content, string contentType = "")
    {
        if (!LooksLikeHtml(content, contentType))
        {
            return CompactWhitespace(content);
        }

        var value = Regex.Replace(content, @"<(script|style|svg|noscript|iframe|head)\b.*?</\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        value = Regex.Replace(value, @"</?(p|div|section|article|main|header|footer|br|li|h[1-6]|tr|table|ul|ol)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "<[^>]+>", " ", RegexOptions.Singleline);
        value = WebUtility.HtmlDecode(value);
        return CompactWhitespace(value);
    }

    public static string Summarize(string text, string query, int maxSentences)
    {
        var sentences = SplitSentences(text)
            .Where(sentence => sentence.Length is > 30 and < 650)
            .Take(200)
            .ToArray();
        if (sentences.Length == 0)
        {
            return string.Empty;
        }

        var queryTokens = Tokenize(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ranked = sentences
            .Select((sentence, index) => new
            {
                Sentence = sentence,
                Index = index,
                Score = ScoreSentence(sentence, queryTokens) - index * 0.002
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .Take(maxSentences)
            .OrderBy(item => item.Index)
            .Select(item => item.Sentence)
            .ToArray();

        return string.Join(Environment.NewLine, ranked.Select(sentence => "- " + sentence));
    }

    public static string FormatSearchResults(string query, IReadOnlyList<WebSearchResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Search query: {query}");
        builder.AppendLine("Source: DuckDuckGo HTML");
        builder.AppendLine();
        builder.AppendLine("Results:");

        if (results.Count == 0)
        {
            builder.AppendLine("- No results found.");
            return builder.ToString().Trim();
        }

        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            builder.AppendLine($"{index + 1}. {result.Title}");
            builder.AppendLine($"   URL: {result.Url}");
            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                builder.AppendLine($"   Snippet: {result.Snippet}");
            }
        }

        return builder.ToString().Trim();
    }

    private static void ApplyBrowserHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("ThothBot/1.0 (+local research assistant)");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,text/plain;q=0.8,*/*;q=0.5");
    }

    private static void AddResult(List<WebSearchResult> results, string rawTitle, string rawUrl, string rawSnippet)
    {
        var title = CleanText(rawTitle);
        var url = NormalizeUrl(rawUrl);
        var snippet = CleanText(rawSnippet);

        if (string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        if (IsSearchEngineSelfLink(uri, title))
        {
            return;
        }

        results.Add(new WebSearchResult(title, uri.ToString(), snippet));
    }

    private static async Task<IReadOnlyList<WebSearchResult>> SearchBingAsync(
        HttpClient httpClient,
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"https://www.bing.com/search?q={Uri.EscapeDataString(query)}"));
        ApplyBrowserHeaders(request);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Fallback search request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        return ParseSearchResults(html, maxResults);
    }

    private static bool IsSearchGate(string html, IReadOnlyList<WebSearchResult> results) =>
        results.Count == 0 ||
        html.Contains("Protection. Privacy. Peace of mind", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("anomaly", StringComparison.OrdinalIgnoreCase) ||
        results.All(result =>
            Uri.TryCreate(result.Url, UriKind.Absolute, out var uri) &&
            IsSearchEngineSelfLink(uri, result.Title));

    private static bool IsSearchEngineSelfLink(Uri uri, string title)
    {
        var host = uri.Host.ToLowerInvariant();
        if (!host.Contains("duckduckgo.com") && !host.Contains("bing.com"))
        {
            return false;
        }

        return title.Equals("here", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("DuckDuckGo", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("Bing", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath is "/" or "";
    }

    private static string ExtractSnippet(string block)
    {
        var match = SnippetRegex.Match(block);
        return match.Success ? match.Groups["snippet"].Value : string.Empty;
    }

    private static string ExtractTitle(string content)
    {
        var match = Regex.Match(content, @"<title[^>]*>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? CleanText(match.Groups["title"].Value) : string.Empty;
    }

    private static string NormalizeUrl(string rawUrl)
    {
        var url = WebUtility.HtmlDecode(rawUrl).Trim();
        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            url = "https:" + url;
        }

        if (url.Contains("duckduckgo.com/l/", StringComparison.OrdinalIgnoreCase) &&
            url.Contains("uddg=", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(url, @"[?&]uddg=(?<url>[^&]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                url = Uri.UnescapeDataString(match.Groups["url"].Value);
            }
        }

        return url;
    }

    private static bool LooksLikeHtml(string content, string contentType) =>
        contentType.Contains("html", StringComparison.OrdinalIgnoreCase) ||
        content.TrimStart().StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) ||
        content.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase);

    private static string CleanText(string value) =>
        CompactWhitespace(WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", " ")));

    private static string CompactWhitespace(string value)
    {
        var lines = value
            .Replace('\u00a0', ' ')
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
            .Where(line => line.Length > 0);

        return string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<string> SplitSentences(string text)
    {
        foreach (var sentence in Regex.Split(text, "(?<=[.!?\u061f])\\s+|\\n+"))
        {
            var trimmed = sentence.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                yield return trimmed;
            }
        }
    }

    private static double ScoreSentence(string sentence, IReadOnlySet<string> queryTokens)
    {
        if (queryTokens.Count == 0)
        {
            return 1;
        }

        var sentenceTokens = Tokenize(sentence).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overlap = queryTokens.Count(token => sentenceTokens.Contains(token));
        return overlap * 3.0 + overlap / (double)Math.Max(queryTokens.Count, 1);
    }

    private static IEnumerable<string> Tokenize(string value) =>
        Regex.Matches(value.ToLowerInvariant(), @"[\p{L}\p{N}_-]{3,}")
            .Select(match => match.Value)
            .Where(token => !StopWords.Contains(token));

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "from", "this", "that", "what", "when", "where", "who", "why", "how",
        "about", "into", "onto",
        "\u0639\u0644\u0649", "\u0639\u0646", "\u0645\u0646", "\u0641\u064a", "\u0627\u0644\u0649", "\u0625\u0644\u0649", "\u0627\u064a\u0647", "\u0645\u0627", "\u0647\u0648", "\u0647\u064a", "\u062f\u0647", "\u062f\u064a"
    };
}

internal sealed record WebSearchResult(string Title, string Url, string Snippet);

internal sealed record WebPageReadResult(
    string Url,
    string Title,
    string Text,
    string Summary,
    string ContentType);
