using System.Collections.Concurrent;
using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Configuration;
using CapitecAssessment.Models;
using CapitecAssessment.Models.Response;
using CapitecAssessment.Services;
using Microsoft.Extensions.Options;

namespace CapitecAssessment.Tests.Fakes;

public sealed class FakeTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 15, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>In-memory stand-in for the EF repository, with the same conditional-update semantics.</summary>
public sealed class FakeAuthenticationRepository : IAuthenticationRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AuthenticatedUser> _accounts = new();
    private readonly List<OtpCode> _otps = new();

    public IReadOnlyList<OtpCode> Otps
    {
        get { lock (_gate) { return _otps.ToList(); } }
    }

    public void AddAccount(string accountNumber, AuthenticatedUser user)
    {
        lock (_gate) { _accounts[accountNumber] = user; }
    }

    public Task<AuthenticatedUser?> FindUserByAccountNumberAsync(string accountNumber, CancellationToken cancellationToken)
    {
        lock (_gate) { return Task.FromResult(_accounts.GetValueOrDefault(accountNumber)); }
    }

    public Task<OtpCode?> GetLatestOtpAsync(long userId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var latest = _otps.Where(o => o.UserId == userId).OrderByDescending(o => o.CreatedAt).FirstOrDefault();
            return Task.FromResult(latest is null ? null : Copy(latest)); // like AsNoTracking
        }
    }

    public Task AddOtpAsync(OtpCode otp, CancellationToken cancellationToken)
    {
        lock (_gate) { _otps.Add(Copy(otp)); }
        return Task.CompletedTask;
    }

    public Task<bool> TryReserveAttemptAsync(Guid otpId, int maxAttempts, DateTime nowUtc, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var otp = _otps.Single(o => o.Id == otpId);
            if (otp.ConsumedAt is not null || otp.ExpiresAt <= nowUtc || otp.Attempts >= maxAttempts)
            {
                return Task.FromResult(false);
            }

            otp.Attempts++;
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryConsumeAsync(Guid otpId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var otp = _otps.Single(o => o.Id == otpId);
            if (otp.ConsumedAt is not null)
            {
                return Task.FromResult(false);
            }

            otp.ConsumedAt = nowUtc;
            return Task.FromResult(true);
        }
    }

    private static OtpCode Copy(OtpCode o) => new()
    {
        Id = o.Id, UserId = o.UserId, CodeHash = o.CodeHash, ExpiresAt = o.ExpiresAt,
        Attempts = o.Attempts, ConsumedAt = o.ConsumedAt, CreatedAt = o.CreatedAt
    };
}

public sealed record SentOtp(long UserId, string Recipient, string Code, TimeSpan Validity);

public sealed class FakeMessagingService : IMessagingService
{
    public ConcurrentQueue<SentOtp> Sent { get; } = new();
    public bool Fail { get; set; }

    public Task SendOtpAsync(long userId, string recipientEmail, string code, TimeSpan validity, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Simulated send failure");
        }

        Sent.Enqueue(new SentOtp(userId, recipientEmail, code, validity));
        return Task.CompletedTask;
    }
}

public sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<OutgoingEmail> Sent { get; } = new();
    public bool Fail { get; set; }

    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Simulated ACS failure");
        }

        Sent.Enqueue(email);
        return Task.CompletedTask;
    }
}

public sealed class FakeNotificationRepository : INotificationRepository
{
    public ConcurrentQueue<Notification> Added { get; } = new();

    public Task AddAsync(Notification notification, CancellationToken cancellationToken)
    {
        Added.Enqueue(notification);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory stand-in for AccountRepository with the same ownership rule.</summary>
public sealed class FakeAccountRepository : IAccountRepository
{
    private readonly List<(long UserId, AccountResponse Account, List<TransactionResponse> Transactions)> _rows = new();

    public void Add(long userId, AccountResponse account, params TransactionResponse[] transactions) =>
        _rows.Add((userId, account, transactions.ToList()));

    public Task<IReadOnlyList<AccountResponse>> GetAccountsAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AccountResponse>>(_rows.Where(r => r.UserId == userId).Select(r => r.Account).ToList());

    public Task<PagedResponse<TransactionResponse>?> GetTransactionsAsync(
        long userId, string accountNumber, DateTime fromUtc, int page, int pageSize, CancellationToken cancellationToken)
    {
        var row = _rows.FirstOrDefault(r => r.UserId == userId && r.Account.AccountNumber == accountNumber);
        if (row.Account is null)
        {
            return Task.FromResult<PagedResponse<TransactionResponse>?>(null);
        }

        var inRange = row.Transactions.Where(t => t.TransactionDate >= fromUtc).OrderByDescending(t => t.TransactionDate).ThenBy(t => t.Id).ToList();
        return Task.FromResult<PagedResponse<TransactionResponse>?>(new PagedResponse<TransactionResponse>
        {
            Items = inRange.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = inRange.Count
        });
    }
}

public static class TestOtp
{
    public static readonly string HashKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    public static IOptions<OtpOptions> Options(Action<OtpOptions>? configure = null)
    {
        var options = new OtpOptions { HashKey = HashKey };
        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    public static OtpHasher Hasher() => new(Options());
}
