namespace StoryApp.Application.Profile;

public sealed record UserProfileResponse(
    int? ChildAge,
    string? PreferredLanguage);
