using System.Net;
using System.Net.Http.Json;
using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Controllers;
using CapitecAssessment.DataAccess;
using CapitecAssessment.Models;
using CapitecAssessment.Models.Response;
using CapitecAssessment.Tests.Fakes;
using CapitecAssessment.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CapitecAssessment.Tests.Controllers;

/// <summary>
/// HTTP tests for the consultant (bank staff) API under <c>/api/v1/consultant</c>.
/// Consultants can look up any client, view their accounts and transactions, and move disputes forward.
///
/// The main security rule being tested: only users with the Consultant role may call these endpoints.
/// Customers get 403 Forbidden, and anonymous callers get 401 Unauthorized.
///
/// Test data:
///   - Lerato (client) has a transact account with two transactions ("OLDER" 10 days ago, "NEWER" 1 day ago)
///     and a savings account with no transactions. She has one dispute, DISP00000001, still "Submitted".
///   - Thabo (client) has no accounts. He has one dispute, DISP00000002, already "Resolved" (final).
///   - Clients are listed by last name, so Lerato Dlamini comes before Thabo Nkosi.
/// </summary>
public class ConsultantApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    // Consultants sign in with a username; customers sign in with an account number.
    private const string ConsultantUsername = "consultant1";
    private const string LeratoAccountNumber = "1052000101";

    private const long LeratoId = 1000000001;
    private const long ThaboId = 1000000002;
    private const long ClientIdThatDoesNotExist = 42;
    private static readonly Guid LeratoTransactAccountId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000101");
    private static readonly Guid LeratoSavingsAccountId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000102");

    private const string LeratoOpenDispute = "DISP00000001";   // status: Submitted (can still change)
    private const string ThaboClosedDispute = "DISP00000002";  // status: Resolved (final, cannot change)

    private static readonly AuthenticatedUser Consultant = new(1000000006, ConsultantUsername, UserRoles.Consultant, "consultant@example.test");
    private static readonly AuthenticatedUser Lerato = new(LeratoId, "lerato", UserRoles.Customer, "lerato@example.test");

    private readonly WebApplicationFactory<Program> _factory;

    public ConsultantApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// What each test gets:
    ///   Client        - to call the API with
    ///   Inbox         - every email the API sent
    ///   Notifications - the notification records the API saved (one per email, with Sent/Failed status)
    ///   Data          - the fake consultant database, so tests can check what changed
    /// </summary>
    private sealed record TestApp(
        HttpClient Client, FakeEmailSender Inbox, FakeNotificationRepository Notifications, FakeConsultantRepository Data);

    private TestApp StartApi()
    {
        var users = new FakeAuthenticationRepository();
        users.AddAccount(ConsultantUsername, Consultant);
        users.AddAccount(LeratoAccountNumber, Lerato);

        var data = new FakeConsultantRepository();
        var now = DateTime.UtcNow;
        data.AddClient(MakeProfile(LeratoId, "Lerato", "Dlamini", "lerato@example.test"),
            (MakeAccount(LeratoTransactAccountId, LeratoAccountNumber, AccountTypes.Transact, 100m),
                new[] { MakeTransaction("OLDER", now.AddDays(-10)), MakeTransaction("NEWER", now.AddDays(-1)) }),
            (MakeAccount(LeratoSavingsAccountId, "1052000102", AccountTypes.Savings, 500m),
                Array.Empty<TransactionResponse>()));
        data.AddClient(MakeProfile(ThaboId, "Thabo", "Nkosi", "thabo@example.test"));
        data.AddDispute(LeratoId, "lerato@example.test", LeratoOpenDispute, DisputeStatuses.Submitted);
        data.AddDispute(ThaboId, "thabo@example.test", ThaboClosedDispute, DisputeStatuses.Resolved);

        var inbox = new FakeEmailSender();
        var notifications = new FakeNotificationRepository();
        var client = TestApi.CreateClient(_factory, users, inbox, notifications,
            replaceMoreServices: services => TestApi.ReplaceWith<IConsultantRepository>(services, data));
        return new TestApp(client, inbox, notifications, data);
    }

    private static ClientProfileResponse MakeProfile(long id, string first, string last, string email) =>
        new() { UserId = id, Username = first.ToLowerInvariant(), FirstName = first, LastName = last, Email = email };

    private static ClientAccountResponse MakeAccount(Guid id, string number, string type, decimal balance) =>
        new() { AccountId = id, AccountName = "Acct " + number, AccountNumber = number, AccountType = type, Balance = balance, Currency = "ZAR" };

    private static TransactionResponse MakeTransaction(string description, DateTime date) =>
        new() { Id = Guid.NewGuid(), Description = description, Amount = 10m, Type = TransactionTypes.Debit, TransactionDate = date };

    private static string DisputeUrl(string reference) => $"/api/v1/consultant/disputes/{reference}";

    private static FakeConsultantRepository.FakeDispute LeratosDispute(TestApp app) => app.Data.Disputes[0];
    private static FakeConsultantRepository.FakeDispute ThabosDispute(TestApp app) => app.Data.Disputes[1];

    /// <summary>
    /// The notification saved for the dispute update email. The sign-in pin email also creates a
    /// notification (type Otp), so that one is skipped.
    /// </summary>
    private static Notification TheDisputeNotification(TestApp app) =>
        Assert.Single(app.Notifications.Added, n => n.Type != NotificationTypes.Otp);

    // ---------------------------------------------------------------------------------------------
    // Who may call the consultant API
    // ---------------------------------------------------------------------------------------------

    public static TheoryData<string> ConsultantGetUrls => new(
        "/api/v1/consultant/clients",
        "/api/v1/consultant/clients/1000000001",
        "/api/v1/consultant/clients/1000000001/accounts",
        "/api/v1/consultant/accounts/aaaaaaaa-0000-0000-0000-000000000101/transactions");

    /// <summary>Without signing in, every consultant endpoint returns 401 Unauthorized.</summary>
    [Theory]
    [MemberData(nameof(ConsultantGetUrls))]
    public async Task Endpoints_WithoutSession_Return401(string url)
    {
        var app = StartApi();

        var response = await app.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed-in customer (not a consultant) gets 403 Forbidden from every consultant endpoint.</summary>
    [Theory]
    [MemberData(nameof(ConsultantGetUrls))]
    public async Task Endpoints_AsCustomer_Return403(string url)
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, LeratoAccountNumber);

        var response = await app.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A customer cannot resolve their own dispute through the consultant API; the dispute stays unchanged.</summary>
    [Fact]
    public async Task UpdateDispute_AsCustomer_Returns403_AndChangesNothing()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, LeratoAccountNumber);

        var response = await app.Client.PutAsJsonAsync(
            DisputeUrl(LeratoOpenDispute), new { status = "Resolved", note = "self-service" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(DisputeStatuses.Submitted, LeratosDispute(app).Response.Status);
    }

    // ---------------------------------------------------------------------------------------------
    // Looking up clients, accounts and transactions
    // ---------------------------------------------------------------------------------------------

    /// <summary>With a page size of 1, the first page holds only Lerato (Dlamini sorts before Nkosi) and the totals show 2 clients.</summary>
    [Fact]
    public async Task GetClients_AsConsultant_ReturnsAPageOfClients()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, ConsultantUsername);

        var page = await app.Client.GetFromJsonAsync<PagedResponse<ClientSummaryResponse>>("/api/v1/consultant/clients?pageSize=1");

        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(LeratoId, Assert.Single(page.Items).UserId);
    }

    /// <summary>A page size above the maximum of 100 is rejected with 400 Bad Request.</summary>
    [Fact]
    public async Task GetClients_InvalidPaging_Returns400()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, ConsultantUsername);

        var response = await app.Client.GetAsync("/api/v1/consultant/clients?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A known client's profile is returned; an unknown client ID gives 404 with a clear error title.</summary>
    [Fact]
    public async Task GetClientProfile_ReturnsTheClientsProfile_OrNotFound()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, ConsultantUsername);

        var lerato = await app.Client.GetFromJsonAsync<ClientProfileResponse>($"/api/v1/consultant/clients/{LeratoId}");
        var missing = await app.Client.GetAsync($"/api/v1/consultant/clients/{ClientIdThatDoesNotExist}");

        Assert.Equal("Dlamini", lerato!.LastName);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var problem = await missing.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(ConsultantController.ClientNotFoundTitle, problem!.Title);
    }

    /// <summary>Both of Lerato's accounts are returned; an unknown client ID gives 404.</summary>
    [Fact]
    public async Task GetClientAccounts_ReturnsAllTheClientsAccounts_OrNotFound()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, ConsultantUsername);

        var accounts = await app.Client.GetFromJsonAsync<List<ClientAccountResponse>>($"/api/v1/consultant/clients/{LeratoId}/accounts");
        var missing = await app.Client.GetAsync($"/api/v1/consultant/clients/{ClientIdThatDoesNotExist}/accounts");

        Assert.Equal(new[] { LeratoTransactAccountId, LeratoSavingsAccountId }, accounts!.Select(a => a.AccountId));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /// <summary>An account's transactions come back newest first; an unknown account ID gives 404 with a clear error title.</summary>
    [Fact]
    public async Task GetAccountTransactions_ReturnsNewestFirst_OrNotFound()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, ConsultantUsername);

        var page = await app.Client.GetFromJsonAsync<PagedResponse<TransactionResponse>>(
            $"/api/v1/consultant/accounts/{LeratoTransactAccountId}/transactions");
        var missing = await app.Client.GetAsync($"/api/v1/consultant/accounts/{Guid.NewGuid()}/transactions");

        Assert.Equal(new[] { "NEWER", "OLDER" }, page!.Items.Select(t => t.Description));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var problem = await missing.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(ConsultantController.AccountNotFoundTitle, problem!.Title);
    }

    // ---------------------------------------------------------------------------------------------
    // Updating a dispute: PUT /api/v1/consultant/disputes/{reference}  body: { status, note }
    //
    // Allowed moves (forward only):
    //   Submitted    -> Under Review, Resolved or Rejected
    //   Under Review -> Resolved or Rejected
    //   Resolved / Rejected are final and can never change.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Moving a dispute to "Under Review":
    ///   - the API returns the updated dispute,
    ///   - a history entry records the new status, the note (with surrounding spaces trimmed) and which consultant did it,
    ///   - the client is emailed about the change,
    ///   - a "dispute progress" notification is saved as Sent.
    /// </summary>
    [Fact]
    public async Task UpdateDispute_UnderReview_UpdatesRecordsHistoryAndEmailsTheClient()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, ConsultantUsername);

        var response = await app.Client.PutAsJsonAsync(
            DisputeUrl(LeratoOpenDispute), new { status = "Under Review", note = "  We are checking with the merchant.  " });

        // The response shows the new status.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dispute = await response.Content.ReadFromJsonAsync<DisputeResponse>();
        Assert.Equal(DisputeStatuses.UnderReview, dispute!.Status);

        // One history entry: (new status, trimmed note, consultant who made the change).
        var history = Assert.Single(LeratosDispute(app).History);
        Assert.Equal((DisputeStatuses.UnderReview, "We are checking with the merchant.", Consultant.UserId), history);

        // Lerato was emailed about it.
        var email = app.Inbox.Sent.Last();
        Assert.Equal("lerato@example.test", email.Recipient);
        Assert.Contains(LeratoOpenDispute, email.Subject);
        Assert.Contains("Under Review", email.PlainText);
        Assert.Contains("We are checking with the merchant.", email.PlainText);

        // A notification record was saved for that email.
        var notification = TheDisputeNotification(app);
        Assert.Equal(NotificationTypes.DisputeProgress, notification.Type);
        Assert.Equal(NotificationStatuses.Sent, notification.Status);
        Assert.Equal(LeratoId, notification.UserId);
        Assert.Equal(LeratosDispute(app).Response.Id, notification.DisputeId);
    }

    /// <summary>Resolving or rejecting a dispute closes it, so the saved notification is of type "dispute finalised".</summary>
    [Theory]
    [InlineData("Resolved")]
    [InlineData("Rejected")]
    public async Task UpdateDispute_ClosingStatuses_AreRecordedAsFinalised(string status)
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, ConsultantUsername);

        var response = await app.Client.PutAsJsonAsync(DisputeUrl(LeratoOpenDispute), new { status, note = "Decision made." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(NotificationTypes.DisputeFinalised, TheDisputeNotification(app).Type);
    }

    /// <summary>
    /// If the email to the client fails to send, the dispute update must still succeed.
    /// The failure is recorded on the notification so someone can follow up.
    /// </summary>
    [Fact]
    public async Task UpdateDispute_EmailFailure_DoesNotFailTheUpdate_AndIsRecordedAsFailed()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, ConsultantUsername);
        app.Inbox.Fail = true; // from now on, every email "fails" to send

        var response = await app.Client.PutAsJsonAsync(
            DisputeUrl(LeratoOpenDispute), new { status = "Under Review", note = "Looking into it." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DisputeStatuses.UnderReview, LeratosDispute(app).Response.Status);
        Assert.Equal(NotificationStatuses.Failed, TheDisputeNotification(app).Status);
    }

    /// <summary>Thabo's dispute is already Resolved (final). Trying to reopen it returns 409 Conflict, changes nothing and sends no email.</summary>
    [Fact]
    public async Task UpdateDispute_FinalStatus_CannotBeChanged_Returns409_AndSendsNothing()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, ConsultantUsername);
        var emailsSentBefore = app.Inbox.Sent.Count;

        var response = await app.Client.PutAsJsonAsync(
            DisputeUrl(ThaboClosedDispute), new { status = "Under Review", note = "Reopening." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(ConsultantController.InvalidTransitionTitle, problem!.Title);
        Assert.Empty(ThabosDispute(app).History);
        Assert.Equal(emailsSentBefore, app.Inbox.Sent.Count);
    }

    /// <summary>Updating a dispute reference that does not exist returns 404 with a clear error title.</summary>
    [Fact]
    public async Task UpdateDispute_UnknownReference_Returns404()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, ConsultantUsername);

        var response = await app.Client.PutAsJsonAsync(DisputeUrl("DISPNOPE"), new { status = "Resolved", note = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(ConsultantController.DisputeNotFoundTitle, problem!.Title);
    }

    /// <summary>An invalid status or a missing note is rejected with 400 Bad Request, and the dispute is left unchanged.</summary>
    [Theory]
    [InlineData("Submitted", "note")]   // a consultant cannot set a dispute back to Submitted
    [InlineData("Closed", "note")]      // not a real status
    [InlineData("", "note")]            // status missing
    [InlineData("Resolved", "")]        // note missing: a note is required
    [InlineData("Resolved", "   ")]     // note is only spaces
    public async Task UpdateDispute_InvalidBody_Returns400(string status, string note)
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, ConsultantUsername);

        var response = await app.Client.PutAsJsonAsync(DisputeUrl(LeratoOpenDispute), new { status, note });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(LeratosDispute(app).History);
    }

    /// <summary>A note longer than 500 characters is rejected with 400 Bad Request.</summary>
    [Fact]
    public async Task UpdateDispute_NoteOver500Characters_Returns400()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, ConsultantUsername);
        var noteThatIsTooLong = new string('x', 501);

        var response = await app.Client.PutAsJsonAsync(DisputeUrl(LeratoOpenDispute), new { status = "Resolved", note = noteThatIsTooLong });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Checks the dispute status rule directly (no HTTP involved): disputes only move forward,
    /// and once Resolved or Rejected they can never change.
    /// </summary>
    [Theory]
    [InlineData(DisputeStatuses.Submitted, DisputeStatuses.UnderReview, true)]
    [InlineData(DisputeStatuses.Submitted, DisputeStatuses.Resolved, true)]
    [InlineData(DisputeStatuses.Submitted, DisputeStatuses.Rejected, true)]
    [InlineData(DisputeStatuses.UnderReview, DisputeStatuses.Resolved, true)]
    [InlineData(DisputeStatuses.UnderReview, DisputeStatuses.Rejected, true)]
    [InlineData(DisputeStatuses.UnderReview, DisputeStatuses.UnderReview, false)]  // no change is not a move
    [InlineData(DisputeStatuses.UnderReview, DisputeStatuses.Submitted, false)]    // cannot go backwards
    [InlineData(DisputeStatuses.Resolved, DisputeStatuses.UnderReview, false)]     // final: cannot reopen
    [InlineData(DisputeStatuses.Resolved, DisputeStatuses.Rejected, false)]        // final: cannot change outcome
    [InlineData(DisputeStatuses.Rejected, DisputeStatuses.Resolved, false)]        // final: cannot change outcome
    public void IsAllowedTransition_FollowsTheForwardOnlyRules(string from, string to, bool expected) =>
        Assert.Equal(expected, ConsultantRepository.IsAllowedTransition(from, to));
}
