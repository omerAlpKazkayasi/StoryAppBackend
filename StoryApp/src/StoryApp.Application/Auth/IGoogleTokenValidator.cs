namespace StoryApp.Application.Auth;

public interface IGoogleTokenValidator
{
    Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken = default);
}
