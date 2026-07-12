namespace Thoth.Data.Acquisition;

public sealed class RemoteDownloadInspector(HttpClient httpClient)
{
    public async Task<long?> GetRemoteSizeAsync(
        Uri uri,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);

        using var request = new HttpRequestMessage(HttpMethod.Head, uri);
        request.Headers.UserAgent.ParseAdd(userAgent);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return response.Content.Headers.ContentLength;
    }
}
