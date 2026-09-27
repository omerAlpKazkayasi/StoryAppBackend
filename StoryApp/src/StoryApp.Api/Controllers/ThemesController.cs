using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryApp.Application.Themes;

namespace StoryApp.Api.Controllers;

[ApiController]
[Route("api/themes")]
[Authorize]
public class ThemesController : ControllerBase
{
    private readonly IThemeService _themeService;

    public ThemesController(IThemeService themeService)
    {
        _themeService = themeService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ThemeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetThemes(CancellationToken cancellationToken)
    {
        var themes = await _themeService.GetActiveThemesAsync(cancellationToken);
        return Ok(themes);
    }
}
