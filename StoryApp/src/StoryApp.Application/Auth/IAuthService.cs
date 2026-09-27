namespace StoryApp.Application.Auth;

public interface IAuthService
{
    Task<AuthResponseDto> CreateGuestAsync(CancellationToken cancellationToken = default);
    Task<AuthResponseDto?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<bool> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<AuthResponseDto?> UpgradeGuestWithGoogleAsync(Guid userId, string idToken, CancellationToken cancellationToken = default);
    Task<AuthResponseDto?> LoginWithGoogleAsync(string idToken, CancellationToken cancellationToken = default);
}
