using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryApp.Application.Common.Abstractions;
using StoryApp.Application.Reading;

namespace StoryApp.Api.Controllers;

[ApiController]
[Route("api/play")]
[Authorize]
public class PlayController : ControllerBase
{
    private readonly IPlayService _playService;
    private readonly ICurrentUser _currentUser;

    public PlayController(IPlayService playService, ICurrentUser currentUser)
    {
        _playService = playService;
        _currentUser = currentUser;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ReadingSessionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Play([FromBody] PlayRequest request, CancellationToken cancellationToken)
    {
        if (request.ThemeId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(request.ThemeId), "The ThemeId field cannot be empty.");
            return ValidationProblem(ModelState);
        }

        var response = await _playService.StartAsync(_currentUser.UserId, request.ThemeId, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
