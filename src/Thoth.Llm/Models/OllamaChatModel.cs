using System.Text;
using System.Text.Json;
using Thoth.Core.Chat;

namespace Thoth.Llm.Models;

public sealed class OllamaChatModel(
    HttpClient httpClient,
    OllamaChatModelOptions options) : IChatModel
{
    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) || request.Model == "thoth-understanding"
            ? options.Model
            : request.Model;

        var payload = new
        {
            model,
            stream = false,
            messages = request.Messages.Select(ToOllamaMessage).ToArray(),
            options = new
            {
                temperature = request.Temperature <= 0 ? options.Temperature : request.Temperature
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Ollama request failed with {(int)response.StatusCode}: {responseContent}");
        }

        using var document = JsonDocument.Parse(responseContent);
        var root = document.RootElement;
        var content = root.TryGetProperty("message", out var message) &&
                      message.TryGetProperty("content", out var contentElement)
            ? contentElement.GetString() ?? string.Empty
            : string.Empty;

        return new ChatResponse(content, model);
    }

    private static object ToOllamaMessage(ChatMessage message)
    {
        var images = message.Attachments?
            .Where(attachment =>
                attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(attachment.StoragePath))
            .Select(attachment => Convert.ToBase64String(File.ReadAllBytes(attachment.StoragePath)))
            .ToArray();

        var role = message.Role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "user",
            _ => "user"
        };

        var payload = new Dictionary<string, object?>
        {
            ["role"] = role,
            ["content"] = message.Content
        };

        if (images is { Length: > 0 })
        {
            payload["images"] = images;
        }

        return payload;
    }
}
