using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryApp.Application.Auth;
using StoryApp.Application.Common.Abstractions;
using StoryApp.Application.Profile;

namespace StoryApp.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly IProfileService _profileService;

    public MeController(
        ICurrentUser currentUser,
        IProfileService profileService)
    {
        _currentUser = currentUser;
        _profileService = profileService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetMe()
    {
        var response = new MeResponse(
            Id: _currentUser.UserId,
            AccountType: _currentUser.AccountType.ToString());

        return Ok(response);
    }

    [HttpGet("profile")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var response = await _profileService.GetAsync(_currentUser.UserId, cancellationToken);
        return Ok(response);
    }

    [HttpPut("profile")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ChildAge.HasValue && request.ChildAge.Value <= 0)
        {
            ModelState.AddModelError(nameof(request.ChildAge), "ChildAge must be greater than 0.");
        }

        if (request.PreferredLanguage != null)
        {
            var trimmed = request.PreferredLanguage.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                ModelState.AddModelError(nameof(request.PreferredLanguage), "PreferredLanguage cannot be empty or whitespace.");
            }
            else if (trimmed.Length > 10)
            {
                ModelState.AddModelError(nameof(request.PreferredLanguage), "PreferredLanguage cannot exceed 10 characters.");
            }
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var response = await _profileService.UpdateAsync(
            _currentUser.UserId,
            request,
            cancellationToken);

        return Ok(response);
    }
}
