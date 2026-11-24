namespace CFC.ChatAI.Web.Services;

public interface IChatSessionAccess
{
    Task<T> WithAccessTokenAsync<T>(string ownerId, Func<string, CancellationToken, Task<T>> request,
        CancellationToken cancellationToken);
}
