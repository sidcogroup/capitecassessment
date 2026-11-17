using System.Globalization;
using System.Security.Claims;
using Asp.Versioning;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CapitecAssessment.Controllers;

/// <summary>
/// The signed-in user's own details. The user is always taken from the session cookie, never from
/// the request, so one customer can never read another's profile.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users")]
[Authorize]
public class UserController : ControllerBase
{
    public const string ProfileNotFoundTitle = "User profile not found.";

    private readonly IUsersService _usersService;

    public UserController(IUsersService usersService)
    {
        _usersService = usersService;
    }

    /// <summary>The signed-in user's profile (first name, last name and full name).</summary>
    [HttpGet("profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileResponse>> GetProfile(CancellationToken cancellationToken)
    {
        var profile = await _usersService.GetProfileAsync(CurrentUserId(), cancellationToken);
        if (profile is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: ProfileNotFoundTitle);
        }

        return Ok(profile);
    }

    private long CurrentUserId() =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
}
