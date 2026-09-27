namespace StoryApp.Application.Profile;

public interface IProfileService
{
    Task<UserProfileResponse> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<UserProfileResponse> UpdateAsync(
        Guid userId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken = default);
}
