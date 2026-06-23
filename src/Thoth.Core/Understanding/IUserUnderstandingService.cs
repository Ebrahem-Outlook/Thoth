namespace Thoth.Core.Understanding;

public interface IUserUnderstandingService
{
    Task<UnderstandingResult> UnderstandAsync(
        UnderstandingRequest request,
        CancellationToken cancellationToken = default);
}
