using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Thoth.Core.Chat;

namespace Thoth.Llm.Models;

public sealed class OpenAiResponsesChatModel(
    HttpClient httpClient,
    OpenAiCompatibleChatModelOptions options) : IChatModel
{
    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("OpenAI Responses provider selected but no API key was supplied.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = string.IsNullOrWhiteSpace(request.Model) || request.Model == "thoth-understanding"
                ? options.Model
                : request.Model,
            ["temperature"] = request.Temperature,
            ["input"] = request.Messages.Select(ToResponseMessage).ToArray()
        };

        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Responses API request failed with {(int)response.StatusCode}: {responseContent}");
        }

        using var document = JsonDocument.Parse(responseContent);
        var outputText = ExtractOutputText(document.RootElement);
        return new ChatResponse(outputText, payload["model"]?.ToString() ?? options.Model);
    }

    private static object ToResponseMessage(ChatMessage message)
    {
        var role = message.Role switch
        {
            ChatRole.System => "developer",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "user",
            _ => "user"
        };

        var content = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["type"] = message.Role == ChatRole.Assistant ? "output_text" : "input_text",
                ["text"] = message.Content
            }
        };

        if (message.Role == ChatRole.User && message.Attachments is not null)
        {
            foreach (var attachment in message.Attachments)
            {
                if (attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                    TryReadDataUrl(attachment, out var dataUrl))
                {
                    content.Add(new Dictionary<string, object?>
                    {
                        ["type"] = "input_image",
                        ["image_url"] = dataUrl
                    });
                }
                else
                {
                    content.Add(new Dictionary<string, object?>
                    {
                        ["type"] = "input_text",
                        ["text"] = $"Attached file: {attachment.FileName} ({attachment.ContentType}, {attachment.SizeBytes} bytes)."
                    });
                }
            }
        }

        return new
        {
            role,
            content
        };
    }

    private static bool TryReadDataUrl(ChatAttachment attachment, out string dataUrl)
    {
        dataUrl = string.Empty;
        if (!File.Exists(attachment.StoragePath))
        {
            return false;
        }

        var fileInfo = new FileInfo(attachment.StoragePath);
        if (fileInfo.Length > 20 * 1024 * 1024)
        {
            return false;
        }

        var base64 = Convert.ToBase64String(File.ReadAllBytes(attachment.StoragePath));
        dataUrl = $"data:{attachment.ContentType};base64,{base64}";
        return true;
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    builder.Append(text.GetString());
                }
            }
        }

        return builder.ToString();
    }
}
