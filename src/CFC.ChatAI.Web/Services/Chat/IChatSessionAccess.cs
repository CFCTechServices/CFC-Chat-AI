namespace CFC.ChatAI.Web.Services.Chat;

public interface IChatSessionAccess
{
    Task<T> WithAccessTokenAsync<T>(string ownerId, Func<string, CancellationToken, Task<T>> request,
        CancellationToken cancellationToken);
}
