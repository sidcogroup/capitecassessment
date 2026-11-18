using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Controllers;
using CapitecAssessment.DataAccess;
using CapitecAssessment.Models;
using CapitecAssessment.Models.Response;
using CapitecAssessment.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CapitecAssessment.Tests.Controllers;

/// <summary>Full pipeline with a real sign-in: the role claim in the session cookie decides who may call the consultant API.</summary>
public class ConsultantApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ConsultantAccount = "consultant1"; // consultants sign in with their username
    private const string CustomerAccount = "1052000101";

    private const long LeratoId = 1000000001;
    private const long ThaboId = 1000000002;
    private static readonly Guid LeratoTransact = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000101");
    private static readonly Guid LeratoSavings = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000102");

    private static readonly AuthenticatedUser Consultant = new(1000000006, "consultant1", UserRoles.Consultant, "consultant@example.test");
    private static readonly AuthenticatedUser Lerato = new(LeratoId, "lerato", UserRoles.Customer, "lerato@example.test");

    private readonly WebApplicationFactory<Program> _baseFactory;

    public ConsultantApiTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    private sealed record TestApp(
        HttpClient Client, FakeEmailSender Email, FakeNotificationRepository Notifications, FakeConsultantRepository Consultants);

    private static ClientProfileResponse Profile(long id, string first, string last, string email) =>
        new() { UserId = id, Username = first.ToLowerInvariant(), FirstName = first, LastName = last, Email = email };

    private static ClientAccountResponse Acct(Guid id, string number, string type, decimal balance) =>
        new() { AccountId = id, AccountName = "Acct " + number, AccountNumber = number, AccountType = type, Balance = balance, Currency = "ZAR" };

    private static TransactionResponse Txn(string description, DateTime date) =>
        new() { Id = Guid.NewGuid(), Description = description, Amount = 10m, Type = TransactionTypes.Debit, TransactionDate = date };

    private TestApp CreateApp()
    {
        var auth = new FakeAuthenticationRepository();
        auth.AddAccount(ConsultantAccount, Consultant);
        auth.AddAccount(CustomerAccount, Lerato);

        var consultants = new FakeConsultantRepository();
        var now = DateTime.UtcNow;
        consultants.AddClient(Profile(LeratoId, "Lerato", "Dlamini", "lerato@example.test"),
            (Acct(LeratoTransact, CustomerAccount, AccountTypes.Transact, 100m),
                new[] { Txn("OLD", now.AddDays(-10)), Txn("NEW", now.AddDays(-1)) }),
            (Acct(LeratoSavings, "1052000102", AccountTypes.Savings, 500m), Array.Empty<TransactionResponse>()));
        consultants.AddClient(Profile(ThaboId, "Thabo", "Nkosi", "thabo@example.test"));
        consultants.AddDispute(LeratoId, "lerato@example.test", "DISP00000001", DisputeStatuses.Submitted);
        consultants.AddDispute(ThaboId, "thabo@example.test", "DISP00000002", DisputeStatuses.Resolved);

        var email = new FakeEmailSender();
        var notifications = new FakeNotificationRepository();
        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Otp:HashKey", TestOtp.HashKey);
            builder.UseSetting("RateLimiting:Authentication:PermitLimit", "1000");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuthenticationRepository>();
                services.AddSingleton<IAuthenticationRepository>(auth);
                services.RemoveAll<INotificationRepository>();
                services.AddSingleton<INotificationRepository>(notifications);
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(email);
                services.RemoveAll<IConsultantRepository>();
                services.AddSingleton<IConsultantRepository>(consultants);
            });
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

        return new TestApp(client, email, notifications, consultants);
    }

    private static async Task SignIn(TestApp app, string accountNumber)
    {
        await app.Client.PostAsJsonAsync("/api/v1/authenticate/request-otp", new { identifier = accountNumber });
        var code = Regex.Match(app.Email.Sent.Last().PlainText, "[0-9]{6}").Value;
        var verify = await app.Client.PostAsJsonAsync("/api/v1/authenticate/verify-otp", new { identifier = accountNumber, code });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
    }

    private static readonly string[] AllGetRoutes =
    [
        "/api/v1/consultant/clients",
        "/api/v1/consultant/clients/1000000001",
        "/api/v1/consultant/clients/1000000001/accounts",
        "/api/v1/consultant/accounts/aaaaaaaa-0000-0000-0000-000000000101/transactions"
    ];

    public static TheoryData<string> GetRoutes => new(AllGetRoutes);

    [Theory]
    [MemberData(nameof(GetRoutes))]
    public async Task Endpoints_WithoutSession_Return401(string url)
    {
        var app = CreateApp();

        var response = await app.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(GetRoutes))]
    public async Task Endpoints_AsCustomer_Return403(string url)
    {
        var app = CreateApp();
        await SignIn(app, CustomerAccount);

        var response = await app.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDispute_AsCustomer_Returns403_AndChangesNothing()
    {
        var app = CreateApp();
        await SignIn(app, CustomerAccount);

        var response = await app.Client.PutAsJsonAsync(
            "/api/v1/consultant/disputes/DISP00000001", new { status = "Resolved", note = "self-service" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(DisputeStatuses.Submitted, app.Consultants.Disputes[0].Response.Status);
    }

    [Fact]
    public async Task GetClients_AsConsultant_ReturnsAPageOfClients()
    {
        var app = CreateApp();
        await SignIn(app, ConsultantAccount);

        var page = await app.Client.GetFromJsonAsync<PagedResponse<ClientSummaryResponse>>("/api/v1/consultant/clients?pageSize=1");

        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(LeratoId, Assert.Single(page.Items).UserId); // ordered by last name: Dlamini before Nkosi
    }

    [Fact]
    public async Task GetClients_InvalidPaging_Returns400()
    {
        var app = CreateApp();
        await SignIn(app, ConsultantAccount);

        var response = await app.Client.GetAsync("/api/v1/consultant/clients?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetClientProfile_ReturnsTheClientsProfile_OrNotFound()
    {
        var app = CreateApp();
        await SignIn(app, ConsultantAccount);

        var profile = await app.Client.GetFromJsonAsync<ClientProfileResponse>($"/api/v1/consultant/clients/{LeratoId}");
        var missing = await app.Client.GetAsync("/api/v1/consultant/clients/42");

        Assert.Equal("Dlamini", profile!.LastName);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(ConsultantController.ClientNotFoundTitle, (await missing.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
    }

    [Fact]
    public async Task GetClientAccounts_ReturnsAllTheClientsAccounts_OrNotFound()
    {
        var app = CreateApp();
        await SignIn(app, ConsultantAccount);

        var accounts = await app.Client.GetFromJsonAsync<List<ClientAccountResponse>>($"/api/v1/consultant/clients/{LeratoId}/accounts");
        var missing = await app.Client.GetAsync("/api/v1/consultant/clients/42/accounts");

        Assert.Equal(new[] { LeratoTransact, LeratoSavings }, accounts!.Select(a => a.AccountId));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task GetAccountTransactions_ReturnsNewestFirst_OrNotFound()
    {
        var app = CreateApp();
        await SignIn(app, ConsultantAccount);

        var page = await app.Client.GetFromJsonAsync<PagedResponse<TransactionResponse>>(
            $"/api/v1/consultant/accounts/{LeratoTransact}/transactions");
        var missing = await app.Client.GetAsync($"/api/v1/consultant/accounts/{Guid.NewGuid()}/transactions");

        Assert.Equal(new[] { "NEW", "OLD" }, page!.Items.Select(t => t.Description));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(ConsultantController.AccountNotFoundTitle, (await missing.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
    }

    [Fact]
    public async Task UpdateDispute_UnderReview_UpdatesRecordsHistoryAndEmailsTheClient()
    {
        var app = CreateApp();
        await SignIn(app, ConsultantAccount);

        var response = await app.Client.PutAsJsonAsync(
            "/api/v1/consultant/disputes/DISP00000001", new { status = "Under Review", note = "  We are checking with the merchant.  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dispute = await response.Content.ReadFromJsonAsync<DisputeResponse>();
        Assert.Equal(DisputeStatuses.UnderReview, dispute!.Status);

        var history = Assert.Single(app.Consultants.Disputes[0].History);
        Assert.Equal((DisputeStatuses.UnderReview, "We are checking with the merchant.", Consultant.UserId), history);

        var sent = app.Email.Sent.Last();
        Assert.Equal("lerato@example.test", sent.Recipient);
        Assert.Contains("DISP00000001", sent.Subject);
        Assert.Contains("Under Review", sent.PlainText);
        Assert.Contains("We are checking with the merchant.", sent.PlainText);

        var notification = Assert.Single(app.Notifications.Added, n => n.Type != NotificationTypes.Otp);
        Assert.Equal(NotificationTypes.DisputeProgress, notification.Type);
        Assert.Equal(NotificationStatuses.Sent, notification.Status);
        Assert.Equal(LeratoId, notification.UserId);
        Assert.Equal(app.Consultants.Disputes[0].Response.Id, notification.DisputeId);
    }

    [Theory]
    [InlineData("Resolved")]
    [InlineData("Rejected")]
    public async Task UpdateDispute_ClosingStatuses_AreRecordedAsFinalised(string status)
    {
        var app = CreateApp();
        await SignIn(app, ConsultantAccount);

        var response = await app.Client.PutAsJsonAsync(
            "/api/v1/consultant/disputes/DISP00000001", new { status, note = "Decision made." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var notification = Assert.Single(app.Notifications.Added, n => n.Type != NotificationTypes.Otp);
        Assert.Equal(NotificationTypes.DisputeFinalised, notification.Type);
    }

    [Fact]
    public async Task UpdateDispute_EmailFailure_DoesNotFailTheUpdate_AndIsRecordedAsFailed()
    {
        var app = CreateApp();
        await SignIn(app, ConsultantAccount);
        app.Email.Fail = true;

        var response = await app.Client.PutAsJsonAsync(
            "/api/v1/consultant/disputes/DISP00000001", new { status = "Under Review", note = "Looking into it." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DisputeStatuses.UnderReview, app.Consultants.Disputes[0].Response.Status);
        var notification = Assert.Single(app.Notifications.Added, n => n.Type != NotificationTypes.Otp);
        Assert.Equal(NotificationStatuses.Failed, notification.Status);
    }

    [Fact]
    public async Task UpdateDispute_FinalStatus_CannotBeChanged_Returns409_AndSendsNothing()
    {
        var app = CreateApp();
        await SignIn(app, ConsultantAccount);
        var emailsBefore = app.Email.Sent.Count;

        var response = await app.Client.PutAsJsonAsync(
            "/api/v1/consultant/disputes/DISP00000002", new { status = "Under Review", note = "Reopening." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ConsultantController.InvalidTransitionTitle, (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
        Assert.Empty(app.Consultants.Disputes[1].History);
        Assert.Equal(emailsBefore, app.Email.Sent.Count);
    }

    [Fact]
    public async Task UpdateDispute_UnknownReference_Returns404()
    {
        var app = CreateApp();
        await SignIn(app, ConsultantAccount);

        var response = await app.Client.PutAsJsonAsync(
            "/api/v1/consultant/disputes/DISPNOPE", new { status = "Resolved", note = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ConsultantController.DisputeNotFoundTitle, (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
    }

    [Theory]
    [InlineData("Submitted", "note")]   // not a status a consultant can set
    [InlineData("Closed", "note")]      // not a status at all
    [InlineData("", "note")]
    [InlineData("Resolved", "")]        // note is required
    [InlineData("Resolved", "   ")]
    public async Task UpdateDispute_InvalidBody_Returns400(string status, string note)
    {
        var app = CreateApp();
        await SignIn(app, ConsultantAccount);

        var response = await app.Client.PutAsJsonAsync("/api/v1/consultant/disputes/DISP00000001", new { status, note });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(app.Consultants.Disputes[0].History);
    }

    [Fact]
    public async Task UpdateDispute_NoteOver500Characters_Returns400()
    {
        var app = CreateApp();
        await SignIn(app, ConsultantAccount);

        var response = await app.Client.PutAsJsonAsync(
            "/api/v1/consultant/disputes/DISP00000001", new { status = "Resolved", note = new string('x', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(DisputeStatuses.Submitted, DisputeStatuses.UnderReview, true)]
    [InlineData(DisputeStatuses.Submitted, DisputeStatuses.Resolved, true)]
    [InlineData(DisputeStatuses.Submitted, DisputeStatuses.Rejected, true)]
    [InlineData(DisputeStatuses.UnderReview, DisputeStatuses.Resolved, true)]
    [InlineData(DisputeStatuses.UnderReview, DisputeStatuses.Rejected, true)]
    [InlineData(DisputeStatuses.UnderReview, DisputeStatuses.UnderReview, false)]
    [InlineData(DisputeStatuses.UnderReview, DisputeStatuses.Submitted, false)]
    [InlineData(DisputeStatuses.Resolved, DisputeStatuses.UnderReview, false)]
    [InlineData(DisputeStatuses.Resolved, DisputeStatuses.Rejected, false)]
    [InlineData(DisputeStatuses.Rejected, DisputeStatuses.Resolved, false)]
    public void IsAllowedTransition_FollowsTheForwardOnlyRules(string from, string to, bool expected) =>
        Assert.Equal(expected, ConsultantRepository.IsAllowedTransition(from, to));
}
