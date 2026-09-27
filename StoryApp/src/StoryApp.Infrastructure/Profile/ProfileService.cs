using Microsoft.EntityFrameworkCore;
using StoryApp.Application.Profile;
using StoryApp.Domain.Entities;
using StoryApp.Infrastructure.Persistence;

namespace StoryApp.Infrastructure.Profile;

public sealed class ProfileService : IProfileService
{
    private readonly AppDbContext _dbContext;

    public ProfileService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserProfileResponse> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _dbContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new UserProfileResponse(p.ChildAge, p.PreferredLanguage))
            .FirstOrDefaultAsync(cancellationToken);

        // If no row exists, return 200 with null values (do NOT return 404, do NOT create DB row)
        return profile ?? new UserProfileResponse(null, null);
    }

    public async Task<UserProfileResponse> UpdateAsync(
        Guid userId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var profile = await _dbContext.UserProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        var utcNow = DateTime.UtcNow;
        string? normalizedLanguage = null;
        if (request.PreferredLanguage != null)
        {
            normalizedLanguage = request.PreferredLanguage.Trim().ToLowerInvariant();
        }

        if (profile == null)
        {
            profile = new UserProfile
            {
                UserId = userId,
                ChildAge = request.ChildAge,
                PreferredLanguage = normalizedLanguage,
                CreatedAt = utcNow,
                UpdatedAt = null
            };
            _dbContext.UserProfiles.Add(profile);
        }
        else
        {
            profile.ChildAge = request.ChildAge;
            profile.PreferredLanguage = normalizedLanguage;
            profile.UpdatedAt = utcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new UserProfileResponse(profile.ChildAge, profile.PreferredLanguage);
    }
}
