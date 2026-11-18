using System.Globalization;
using System.Security.Claims;
using Asp.Versioning;
using CapitecAssessment.Configuration;
using CapitecAssessment.Models.Request;
using CapitecAssessment.Models.Response;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
// Microsoft.AspNetCore.Authentication has its own IAuthenticationService.
using IAuthenticationService = CapitecAssessment.Abstractions.Services.IAuthenticationService;

namespace CapitecAssessment.Controllers;

/// <summary>
/// Two-step sign-in: account number -> emailed one-time pin -> session cookie.
/// The session is an HttpOnly, Secure, SameSite=Strict cookie, so page scripts can never read it
/// and the browser sends it on every subsequent same-site request.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/authenticate")]
public class AuthenticateController : ControllerBase
{
    public const string InvalidCodeTitle = "Invalid or expired one-time pin.";

    private readonly IAuthenticationService _authenticationService;

    public AuthenticateController(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    /// <summary>Step 1: emails a one-time pin to the user who owns the account number.</summary>
    /// <remarks>Always returns the same 200 response for a well-formed account number, registered or not.</remarks>
    [HttpPost("request-otp")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationRateLimitOptions.PolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RequestOtpResponse>> RequestOtp([FromBody] RequestOtpRequest request, CancellationToken cancellationToken)
    {
        await _authenticationService.RequestOtpAsync(request.ResolvedIdentifier, cancellationToken);
        return Ok(new RequestOtpResponse());
    }

    /// <summary>Step 2: verifies the pin and, on success, issues the session cookie.</summary>
    [HttpPost("verify-otp")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationRateLimitOptions.PolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SessionResponse>> VerifyOtp([FromBody] VerifyOtpRequest request, CancellationToken cancellationToken)
    {
        var user = await _authenticationService.VerifyOtpAsync(request.ResolvedIdentifier, request.Code, cancellationToken);
        if (user is null)
        {
            // One response for every failure reason (unknown account, wrong, expired, used, locked).
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: InvalidCodeTitle);
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role)
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });

        return Ok(new SessionResponse { UserId = user.UserId, Username = user.Username, Role = user.Role });
    }

    /// <summary>Returns who the current session cookie belongs to.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<SessionResponse> Me()
    {
        return Ok(new SessionResponse
        {
            UserId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture),
            Username = User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            Role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty
        });
    }

    /// <summary>Ends the session by expiring the cookie.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}
