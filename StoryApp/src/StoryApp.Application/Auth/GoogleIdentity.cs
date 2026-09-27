namespace StoryApp.Application.Auth;

public sealed record GoogleIdentity(
    string Subject,
    string? Email,
    bool EmailVerified);
