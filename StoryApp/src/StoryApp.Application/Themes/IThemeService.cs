namespace StoryApp.Application.Themes;

public interface IThemeService
{
    Task<IReadOnlyList<ThemeResponse>> GetActiveThemesAsync(CancellationToken cancellationToken = default);
}
