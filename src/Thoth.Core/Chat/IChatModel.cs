namespace Thoth.Core.Chat;

public interface IChatModel
{
    Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
