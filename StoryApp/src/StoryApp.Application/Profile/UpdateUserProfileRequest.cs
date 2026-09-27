namespace StoryApp.Application.Profile;

public sealed record UpdateUserProfileRequest(
    int? ChildAge,
    string? PreferredLanguage);
