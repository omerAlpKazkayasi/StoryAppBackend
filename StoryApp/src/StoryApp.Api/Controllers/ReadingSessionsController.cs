using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryApp.Application.Common.Abstractions;
using StoryApp.Application.Reading;
using StoryApp.Domain.Enums;

namespace StoryApp.Api.Controllers;

[ApiController]
[Route("api/reading-sessions")]
[Authorize]
public class ReadingSessionsController : ControllerBase
{
    private readonly IReadingService _readingService;
    private readonly ICurrentUser _currentUser;

    public ReadingSessionsController(
        IReadingService readingService,
        ICurrentUser currentUser)
    {
        _readingService = readingService;
        _currentUser = currentUser;
    }

    [HttpPost("{sessionId:guid}/choices")]
    [ProducesResponseType(typeof(ReadingSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Choose(
        [FromRoute] Guid sessionId,
        [FromBody] ChoiceRequest request,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(sessionId), "SessionId cannot be empty.");
        }

        if (request.CurrentNodeId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(request.CurrentNodeId), "CurrentNodeId cannot be empty.");
        }

        if (request.TransitionId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(request.TransitionId), "TransitionId cannot be empty.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var response = await _readingService.ChooseAsync(
            _currentUser.UserId,
            sessionId,
            request.CurrentNodeId,
            request.TransitionId,
            cancellationToken);

        return Ok(response);
    }

    [HttpPost("{sessionId:guid}/back")]
    [ProducesResponseType(typeof(ReadingSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Back(
        [FromRoute] Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(sessionId), "SessionId cannot be empty.");
            return ValidationProblem(ModelState);
        }

        var response = await _readingService.BackAsync(
            _currentUser.UserId,
            sessionId,
            cancellationToken);

        return Ok(response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ReadingSessionListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetList(
        [FromQuery] ReadingSessionListQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Page < 1)
        {
            ModelState.AddModelError(nameof(query.Page), "Page must be greater than or equal to 1.");
        }

        if (query.PageSize < 1 || query.PageSize > 100)
        {
            ModelState.AddModelError(nameof(query.PageSize), "PageSize must be between 1 and 100.");
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<StoryReadingStatus>(query.Status, ignoreCase: true, out var status) || !Enum.IsDefined(status))
            {
                ModelState.AddModelError(nameof(query.Status), "Invalid status value. Supported values: InProgress, Completed, Abandoned.");
            }
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var response = await _readingService.GetListAsync(
            _currentUser.UserId,
            query,
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("{sessionId:guid}")]
    [ProducesResponseType(typeof(ReadingSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromRoute] Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(sessionId), "SessionId cannot be empty.");
            return ValidationProblem(ModelState);
        }

        var response = await _readingService.GetAsync(
            _currentUser.UserId,
            sessionId,
            cancellationToken);

        return Ok(response);
    }

    [HttpPost("{sessionId:guid}/abandon")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Abandon(
        [FromRoute] Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(sessionId), "SessionId cannot be empty.");
            return ValidationProblem(ModelState);
        }

        await _readingService.AbandonAsync(
            _currentUser.UserId,
            sessionId,
            cancellationToken);

        return NoContent();
    }

    [HttpPost("{sessionId:guid}/restart")]
    [ProducesResponseType(typeof(ReadingSessionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restart(
        [FromRoute] Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(sessionId), "SessionId cannot be empty.");
            return ValidationProblem(ModelState);
        }

        var response = await _readingService.RestartAsync(
            _currentUser.UserId,
            sessionId,
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }
}
