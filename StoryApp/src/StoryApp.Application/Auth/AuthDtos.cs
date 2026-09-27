namespace StoryApp.Application.Auth;

public sealed record UserDto(
    Guid Id,
    string AccountType);

public sealed record AuthResponseDto(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserDto User);

public sealed record RefreshTokenRequest(
    string RefreshToken);

public sealed record LogoutRequest(
    string RefreshToken);
