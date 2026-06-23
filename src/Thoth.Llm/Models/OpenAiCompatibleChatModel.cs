using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Thoth.Core.Chat;

namespace Thoth.Llm.Models;

public sealed class OpenAiCompatibleChatModel(
    HttpClient httpClient,
    OpenAiCompatibleChatModelOptions options) : IChatModel
{
    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("OpenAI-compatible provider selected but no API key was supplied.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(request.Model) ? options.Model : request.Model,
            temperature = request.Temperature,
            messages = request.Messages.Select(message => new
            {
                role = ToWireRole(message.Role),
                content = message.Content,
                name = message.Name
            })
        };

        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Model request failed with {(int)response.StatusCode}: {responseContent}");
        }

        using var document = JsonDocument.Parse(responseContent);
        var root = document.RootElement;
        var content = root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        int? promptTokens = null;
        int? completionTokens = null;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var promptTokenElement))
            {
                promptTokens = promptTokenElement.GetInt32();
            }

            if (usage.TryGetProperty("completion_tokens", out var completionTokenElement))
            {
                completionTokens = completionTokenElement.GetInt32();
            }
        }

        return new ChatResponse(content, payload.model, promptTokens, completionTokens);
    }

    private static string ToWireRole(ChatRole role)
    {
        return role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => "user"
        };
    }
}
