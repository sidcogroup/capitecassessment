using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Configuration;
using CapitecAssessment.Models;
using Microsoft.Extensions.Options;

namespace CapitecAssessment.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly IAuthenticationRepository _repository;
    private readonly IMessagingService _messagingService;
    private readonly OtpHasher _hasher;
    private readonly OtpOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        IAuthenticationRepository repository,
        IMessagingService messagingService,
        OtpHasher hasher,
        IOptions<OtpOptions> options,
        TimeProvider timeProvider,
        ILogger<AuthenticationService> logger)
    {
        _repository = repository;
        _messagingService = messagingService;
        _hasher = hasher;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RequestOtpAsync(string identifier, CancellationToken cancellationToken)
    {
        var user = await _repository.FindUserByIdentifierAsync(identifier, cancellationToken);
        if (user is null)
        {
            _logger.LogInformation("OTP requested for unknown identifier {Identifier}", Mask(identifier));
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var latest = await _repository.GetLatestOtpAsync(user.UserId, cancellationToken);
        if (latest is not null && latest.CreatedAt > now - _options.ResendCooldown)
        {
            _logger.LogInformation("OTP for user {UserId} not sent: previous code issued less than {Cooldown}s ago",
                user.UserId, _options.ResendCooldownSeconds);
            return;
        }

        var code = OtpHasher.GenerateCode();

        // Saving a new code supersedes any earlier one: only the latest code is ever verified.
        await _repository.AddOtpAsync(new OtpCode
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId,
            CodeHash = _hasher.Hash(user.UserId, code),
            CreatedAt = now,
            ExpiresAt = now + _options.Validity
        }, cancellationToken);

        if (_options.LogCodeToConsole)
        {
            _logger.LogWarning("DEV ONLY - OTP for {Identifier} (user {UserId}): {Code}", Mask(identifier), user.UserId, code);
        }

        try
        {
            await _messagingService.SendOtpAsync(user.UserId, user.Email, code, _options.Validity, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Swallowed so the caller's response is identical to the unknown-account case.
            // The failure is logged and recorded in notifications; the user can retry after the cooldown.
            _logger.LogError(ex, "Failed to send OTP to user {UserId}", user.UserId);
        }
    }

    public async Task<AuthenticatedUser?> VerifyOtpAsync(string identifier, string code, CancellationToken cancellationToken)
    {
        var user = await _repository.FindUserByIdentifierAsync(identifier, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var otp = await _repository.GetLatestOtpAsync(user.UserId, cancellationToken);
        if (otp is null)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Reserve the attempt before comparing, so parallel guesses each cost an attempt.
        // This also rejects consumed, expired and exhausted codes.
        if (!await _repository.TryReserveAttemptAsync(otp.Id, _options.MaxAttempts, now, cancellationToken))
        {
            _logger.LogInformation("OTP verification rejected for user {UserId}: code consumed, expired or out of attempts", user.UserId);
            return null;
        }

        if (!_hasher.Verify(user.UserId, code, otp.CodeHash))
        {
            _logger.LogInformation("OTP verification failed for user {UserId}: wrong code", user.UserId);
            return null;
        }

        // Guards against two correct submissions racing: only one consumes the code.
        if (!await _repository.TryConsumeAsync(otp.Id, now, cancellationToken))
        {
            return null;
        }

        _logger.LogInformation("User {UserId} signed in with OTP", user.UserId);
        return user;
    }

    /// <summary>
    /// Account numbers keep their last 4 digits only. Anything else (a username, or a mistyped value that
    /// could be a secret) is fully hidden, so neither ends up in logs.
    /// </summary>
    private static string Mask(string identifier)
    {
        return SignInIdentifier.IsAccountNumber(identifier)
            ? new string('*', identifier.Length - 4) + identifier[^4..]
            : "****";
    }
}
