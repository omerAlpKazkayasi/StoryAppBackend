using Microsoft.EntityFrameworkCore;
using StoryApp.Application.Themes;
using StoryApp.Infrastructure.Persistence;

namespace StoryApp.Infrastructure.Themes;

public sealed class ThemeService : IThemeService
{
    private readonly AppDbContext _dbContext;

    public ThemeService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ThemeResponse>> GetActiveThemesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Themes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new ThemeResponse(
                x.Id,
                x.Name,
                x.Description,
                x.Icon,
                x.Color,
                x.SortOrder))
            .ToListAsync(cancellationToken);
    }
}
