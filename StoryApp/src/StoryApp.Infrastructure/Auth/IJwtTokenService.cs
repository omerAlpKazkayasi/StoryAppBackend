using StoryApp.Domain.Enums;

namespace StoryApp.Infrastructure.Auth;

public interface IJwtTokenService
{
    (string RawToken, string TokenHash) GenerateRefreshToken();
    string HashToken(string rawToken);
    (string AccessToken, DateTime ExpiresAt) GenerateAccessToken(Guid userId, UserAccountType accountType);
}
