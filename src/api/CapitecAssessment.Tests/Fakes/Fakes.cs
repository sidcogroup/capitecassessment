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

    public Task<AuthenticatedUser?> FindUserByIdentifierAsync(string accountNumber, CancellationToken cancellationToken)
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

    public Task SendDisputeUpdateAsync(
        long userId, string recipientEmail, Guid disputeId, string disputeReference, string status, string note,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>In-memory stand-in for ConsultantRepository, using the same transition rule.</summary>
public sealed class FakeConsultantRepository : IConsultantRepository
{
    public sealed record FakeClient(ClientProfileResponse Profile, List<(ClientAccountResponse Account, List<TransactionResponse> Transactions)> Accounts);

    public sealed class FakeDispute
    {
        public required DisputeResponse Response { get; set; }
        public required long ClientUserId { get; init; }
        public required string ClientEmail { get; init; }
        public List<(string Status, string Note, long ChangedBy)> History { get; } = new();
    }

    private readonly List<FakeClient> _clients = new();
    private readonly List<FakeDispute> _disputes = new();

    public IReadOnlyList<FakeDispute> Disputes => _disputes;

    public void AddClient(ClientProfileResponse profile, params (ClientAccountResponse Account, TransactionResponse[] Transactions)[] accounts) =>
        _clients.Add(new FakeClient(profile, accounts.Select(a => (a.Account, a.Transactions.ToList())).ToList()));

    public FakeDispute AddDispute(long clientUserId, string clientEmail, string reference, string status)
    {
        var dispute = new FakeDispute
        {
            ClientUserId = clientUserId,
            ClientEmail = clientEmail,
            Response = new DisputeResponse { Id = Guid.NewGuid(), DisputeReference = reference, Reason = "Not me", Status = status }
        };
        _disputes.Add(dispute);
        return dispute;
    }

    public Task<PagedResponse<ClientSummaryResponse>> GetClientsAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var all = _clients.Select(c => c.Profile).OrderBy(p => p.LastName).ThenBy(p => p.UserId).ToList();
        return Task.FromResult(new PagedResponse<ClientSummaryResponse>
        {
            Items = all.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(p => new ClientSummaryResponse
                {
                    UserId = p.UserId, Username = p.Username, FirstName = p.FirstName, LastName = p.LastName, Email = p.Email
                }).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = all.Count
        });
    }

    public Task<ClientProfileResponse?> GetClientProfileAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult(_clients.FirstOrDefault(c => c.Profile.UserId == userId)?.Profile);

    public Task<IReadOnlyList<ClientAccountResponse>?> GetClientAccountsAsync(long userId, CancellationToken cancellationToken)
    {
        var client = _clients.FirstOrDefault(c => c.Profile.UserId == userId);
        return Task.FromResult<IReadOnlyList<ClientAccountResponse>?>(client?.Accounts.Select(a => a.Account).ToList());
    }

    public Task<PagedResponse<TransactionResponse>?> GetAccountTransactionsAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var row = _clients.SelectMany(c => c.Accounts).FirstOrDefault(a => a.Account.AccountId == accountId);
        if (row.Account is null)
        {
            return Task.FromResult<PagedResponse<TransactionResponse>?>(null);
        }

        var ordered = row.Transactions.OrderByDescending(t => t.TransactionDate).ThenBy(t => t.Id).ToList();
        return Task.FromResult<PagedResponse<TransactionResponse>?>(new PagedResponse<TransactionResponse>
        {
            Items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = ordered.Count
        });
    }

    public Task<PagedResponse<DisputeResponse>> GetAllDisputesAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(PageDisputes(_disputes, page, pageSize));

    public Task<PagedResponse<DisputeResponse>?> GetClientDisputesAsync(
        long userId, int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult<PagedResponse<DisputeResponse>?>(
            _clients.Any(c => c.Profile.UserId == userId)
                ? PageDisputes(_disputes.Where(d => d.ClientUserId == userId).ToList(), page, pageSize)
                : null);

    private static PagedResponse<DisputeResponse> PageDisputes(IReadOnlyCollection<FakeDispute> source, int page, int pageSize) =>
        new()
        {
            Items = source.OrderByDescending(d => d.Response.CreatedAt).Select(d => d.Response)
                .Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = source.Count
        };

    public Task<UpdateDisputeResult> UpdateDisputeStatusAsync(
        long consultantId, string disputeReference, string newStatus, string note, CancellationToken cancellationToken)
    {
        var dispute = _disputes.FirstOrDefault(d => d.Response.DisputeReference == disputeReference);
        if (dispute is null)
        {
            return Task.FromResult(new UpdateDisputeResult(UpdateDisputeOutcome.NotFound));
        }

        if (!CapitecAssessment.DataAccess.ConsultantRepository.IsAllowedTransition(dispute.Response.Status, newStatus))
        {
            return Task.FromResult(new UpdateDisputeResult(UpdateDisputeOutcome.InvalidTransition));
        }

        var current = dispute.Response;
        dispute.Response = new DisputeResponse
        {
            Id = current.Id, DisputeReference = current.DisputeReference, TransactionId = current.TransactionId,
            Reason = current.Reason, Details = current.Details, Status = newStatus,
            CreatedAt = current.CreatedAt, UpdatedAt = DateTime.UtcNow
        };
        dispute.History.Add((newStatus, note, consultantId));
        return Task.FromResult(new UpdateDisputeResult(UpdateDisputeOutcome.Updated, dispute.Response, dispute.ClientUserId, dispute.ClientEmail));
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

/// <summary>In-memory stand-in for UserRepository.</summary>
public sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<long, UserProfileResponse> _profiles = new();

    public void Add(UserProfileResponse profile) => _profiles[profile.UserId] = profile;

    public Task<UserProfileResponse?> GetProfileAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult(_profiles.GetValueOrDefault(userId));
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
