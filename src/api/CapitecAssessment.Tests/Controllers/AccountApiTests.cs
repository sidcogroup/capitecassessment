using System.Net;
using System.Net.Http.Json;
using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Controllers;
using CapitecAssessment.Models;
using CapitecAssessment.Models.Request;
using CapitecAssessment.Models.Response;
using CapitecAssessment.Tests.Fakes;
using CapitecAssessment.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CapitecAssessment.Tests.Controllers;

/// <summary>
/// HTTP tests for a customer viewing their own accounts and transactions (<c>/api/v1/accounts</c>).
///
/// Each test signs in for real, so the API works out who is calling from the session cookie.
/// The main security rule being tested: a customer can only ever see their OWN accounts.
///
/// Test data:
///   Lerato owns three accounts:
///     - a transact account with 5 transactions made 5, 10, 45, 75 and 120 days ago
///     - a savings account
///     - a credit card account with no transactions
///   Thabo owns one account with a single transaction, described as "THABO'S PRIVATE TRANSACTION".
/// Each transaction's description says how many days ago it happened, so expected results are easy to read.
/// </summary>
public class AccountApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string LeratoTransactAccount = "1052000101";
    private const string LeratoSavingsAccount = "1052000102";
    private const string LeratoAccountWithNoTransactions = "1052000103";
    private const string ThabosAccount = "1052000201";
    private const string ThabosPrivateTransaction = "THABO'S PRIVATE TRANSACTION";

    private static readonly AuthenticatedUser Lerato = new(1000000001, "lerato", UserRoles.Customer, "lerato@example.test");
    private static readonly AuthenticatedUser Thabo = new(1000000002, "thabo", UserRoles.Customer, "thabo@example.test");

    private readonly WebApplicationFactory<Program> _factory;

    public AccountApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private sealed record TestApp(HttpClient Client, FakeEmailSender Inbox);

    private TestApp StartApi()
    {
        // Who can sign in: each customer signs in with one of their account numbers.
        var users = new FakeAuthenticationRepository();
        users.AddAccount(LeratoTransactAccount, Lerato);
        users.AddAccount(ThabosAccount, Thabo);

        // The accounts and transactions the API will serve.
        var accounts = new FakeAccountRepository();
        var now = DateTime.UtcNow;
        accounts.Add(Lerato.UserId, MakeAccount("Global One Account", LeratoTransactAccount, AccountTypes.Transact, 1500.50m),
            MakeTransaction("10 days ago", 10m, TransactionTypes.Debit, now.AddDays(-10)),
            MakeTransaction("5 days ago", 20m, TransactionTypes.Credit, now.AddDays(-5)),
            MakeTransaction("45 days ago", 30m, TransactionTypes.Debit, now.AddDays(-45)),
            MakeTransaction("75 days ago", 40m, TransactionTypes.Debit, now.AddDays(-75)),
            MakeTransaction("120 days ago", 50m, TransactionTypes.Debit, now.AddDays(-120)));
        accounts.Add(Lerato.UserId, MakeAccount("Flexi Savings", LeratoSavingsAccount, AccountTypes.Savings, 25000m));
        accounts.Add(Lerato.UserId, MakeAccount("Credit Card", LeratoAccountWithNoTransactions, AccountTypes.Credit, -300m));
        accounts.Add(Thabo.UserId, MakeAccount("Global One Account", ThabosAccount, AccountTypes.Transact, 99m),
            MakeTransaction(ThabosPrivateTransaction, 5m, TransactionTypes.Debit, now.AddDays(-1)));

        var inbox = new FakeEmailSender();
        var client = TestApi.CreateClient(_factory, users, inbox,
            replaceMoreServices: services => TestApi.ReplaceWith<IAccountRepository>(services, accounts));
        return new TestApp(client, inbox);
    }

    private static AccountResponse MakeAccount(string name, string number, string type, decimal balance) =>
        new() { AccountName = name, AccountNumber = number, AccountType = type, Balance = balance, Currency = "ZAR" };

    private static TransactionResponse MakeTransaction(string description, decimal amount, string type, DateTime date) =>
        new() { Id = Guid.NewGuid(), Description = description, Amount = amount, Type = type, TransactionDate = date, Reference = "REF" };

    /// <summary>Gets one page of transactions and checks the call succeeded.</summary>
    private static async Task<PagedResponse<TransactionResponse>> GetTransactionsPage(TestApp app, string accountNumber, string query = "")
    {
        var response = await app.Client.GetAsync($"/api/v1/accounts/{accountNumber}/transactions{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>())!;
    }

    private static IEnumerable<string> Descriptions(PagedResponse<TransactionResponse> page) =>
        page.Items.Select(t => t.Description);

    // ---------------------------------------------------------------------------------------------
    // Must be signed in
    // ---------------------------------------------------------------------------------------------

    /// <summary>Without signing in first, every account endpoint returns 401 Unauthorized.</summary>
    [Theory]
    [InlineData("/api/v1/accounts")]
    [InlineData("/api/v1/accounts/1052000101/transactions")]
    public async Task Endpoints_WithoutSession_Return401(string url)
    {
        var app = StartApi();

        var response = await app.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------------
    // GET /api/v1/accounts : list my accounts
    // ---------------------------------------------------------------------------------------------

    /// <summary>Lerato sees her three accounts, with correct details, and none of Thabo's.</summary>
    [Fact]
    public async Task GetAccounts_ReturnsOnlyTheSignedInUsersAccounts()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, LeratoTransactAccount);

        var response = await app.Client.GetAsync("/api/v1/accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = (await response.Content.ReadFromJsonAsync<List<AccountResponse>>())!;
        Assert.Equal(
            new[] { LeratoTransactAccount, LeratoSavingsAccount, LeratoAccountWithNoTransactions },
            accounts.Select(a => a.AccountNumber).Order());

        var transact = accounts.Single(a => a.AccountNumber == LeratoTransactAccount);
        Assert.Equal("Global One Account", transact.AccountName);
        Assert.Equal(AccountTypes.Transact, transact.AccountType);
        Assert.Equal(1500.50m, transact.Balance);
    }

    /// <summary>
    /// The same URL gives a different answer depending on who is signed in.
    /// This proves the API uses the session cookie (not something in the request) to decide whose accounts to return.
    /// </summary>
    [Fact]
    public async Task GetAccounts_UsesTheCookieNotTheRequest_OtherUserSeesTheirOwn()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, ThabosAccount);

        var accounts = await app.Client.GetFromJsonAsync<List<AccountResponse>>("/api/v1/accounts");

        var onlyAccount = Assert.Single(accounts!);
        Assert.Equal(ThabosAccount, onlyAccount.AccountNumber);
    }

    // ---------------------------------------------------------------------------------------------
    // GET /api/v1/accounts/{accountNumber}/transactions : list transactions on one of my accounts
    // Query options: days = 30 | 60 | 90 (default 30), page (default 1), pageSize = 1..100 (default 20)
    // ---------------------------------------------------------------------------------------------

    /// <summary>With no query options: the last 30 days, newest first, first page.</summary>
    [Fact]
    public async Task GetTransactions_Defaults_Last30DaysNewestFirst_FirstPage()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, LeratoTransactAccount);

        var page = await GetTransactionsPage(app, LeratoTransactAccount);

        Assert.Equal(new[] { "5 days ago", "10 days ago" }, Descriptions(page));
        Assert.Equal(1, page.Page);
        Assert.Equal(TransactionsQueryRequest.DefaultPageSize, page.PageSize);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.TotalPages);
    }

    /// <summary>
    /// The <c>days</c> option controls how far back to look.
    /// The 120-day-old transaction is never returned, because 90 days is the maximum.
    /// </summary>
    [Theory]
    [InlineData(30, new[] { "5 days ago", "10 days ago" })]
    [InlineData(60, new[] { "5 days ago", "10 days ago", "45 days ago" })]
    [InlineData(90, new[] { "5 days ago", "10 days ago", "45 days ago", "75 days ago" })]
    public async Task GetTransactions_DaysSelectsTheWindow(int days, string[] expectedTransactions)
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, LeratoTransactAccount);

        var page = await GetTransactionsPage(app, LeratoTransactAccount, $"?days={days}");

        Assert.Equal(expectedTransactions, Descriptions(page));
        Assert.Equal(expectedTransactions.Length, page.TotalCount);
    }

    /// <summary>
    /// 4 transactions in the last 90 days, 3 per page: page 1 has 3, page 2 has the last 1, page 3 is empty.
    /// No transaction appears on two pages, and every page reports the same totals.
    /// </summary>
    [Fact]
    public async Task GetTransactions_Paginates_WithoutOverlap_AndReportsTotals()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, LeratoTransactAccount);

        var page1 = await GetTransactionsPage(app, LeratoTransactAccount, "?days=90&page=1&pageSize=3");
        var page2 = await GetTransactionsPage(app, LeratoTransactAccount, "?days=90&page=2&pageSize=3");
        var page3 = await GetTransactionsPage(app, LeratoTransactAccount, "?days=90&page=3&pageSize=3");

        Assert.Equal(new[] { "5 days ago", "10 days ago", "45 days ago" }, Descriptions(page1));
        Assert.Equal(new[] { "75 days ago" }, Descriptions(page2));
        Assert.Empty(page3.Items);

        foreach (var page in new[] { page1, page2, page3 })
        {
            Assert.Equal(4, page.TotalCount);
            Assert.Equal(2, page.TotalPages);
        }
    }

    /// <summary>Query options outside the allowed values are rejected with 400 Bad Request.</summary>
    [Theory]
    [InlineData("days=7")]        // only 30, 60 or 90 are allowed
    [InlineData("days=abc")]      // not a number
    [InlineData("days=0")]
    [InlineData("page=0")]        // pages start at 1
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]    // page size must be 1 to 100
    [InlineData("pageSize=101")]
    public async Task GetTransactions_InvalidQuery_Returns400(string query)
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, LeratoTransactAccount);

        var response = await app.Client.GetAsync($"/api/v1/accounts/{LeratoTransactAccount}/transactions?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>An account with no transactions is not an error: it returns 200 with an empty list.</summary>
    [Fact]
    public async Task GetTransactions_OwnAccountWithNoTransactions_Returns200WithEmptyPage()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, LeratoTransactAccount);

        var page = await GetTransactionsPage(app, LeratoAccountWithNoTransactions);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
    }

    /// <summary>
    /// Security: when Lerato asks for Thabo's transactions she gets 404 Not Found, exactly as if the account
    /// did not exist. (A 403 "Forbidden" would confirm to her that the account number is real.)
    /// None of Thabo's data may appear in the response.
    /// </summary>
    [Fact]
    public async Task GetTransactions_AnotherUsersAccount_Returns404_SameAsAnUnknownAccount()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, LeratoTransactAccount);

        var someoneElsesAccount = await app.Client.GetAsync($"/api/v1/accounts/{ThabosAccount}/transactions");
        var accountThatDoesNotExist = await app.Client.GetAsync("/api/v1/accounts/0000000000/transactions");

        Assert.Equal(HttpStatusCode.NotFound, someoneElsesAccount.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, accountThatDoesNotExist.StatusCode);
        var problem = await someoneElsesAccount.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(AccountController.AccountNotFoundTitle, problem!.Title);
        Assert.DoesNotContain(ThabosPrivateTransaction, await someoneElsesAccount.Content.ReadAsStringAsync());
    }

    /// <summary>An account number in the URL must be exactly 10 digits, otherwise 400 Bad Request.</summary>
    [Theory]
    [InlineData("123456789")]     // 9 digits
    [InlineData("12345678901")]   // 11 digits
    [InlineData("12345abcde")]    // contains letters
    public async Task GetTransactions_MalformedAccountNumber_Returns400(string accountNumber)
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, LeratoTransactAccount);

        var response = await app.Client.GetAsync($"/api/v1/accounts/{accountNumber}/transactions");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
