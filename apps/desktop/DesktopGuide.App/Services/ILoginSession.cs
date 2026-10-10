namespace DesktopGuide.App.Services;

public interface ILoginSession
{
    bool SignedIn { get; }
    bool IsMock { get; }
    string? AccountName { get; }
    bool RememberMe { get; set; }
    event Action? Changed;
    Task SignInAsync(CancellationToken cancellation);
    Task SignOutAsync();
    Task<string?> GetAccessAsync(CancellationToken cancellation);
}

public sealed class SignInException(string message, Exception? inner = null) : Exception(message, inner);
