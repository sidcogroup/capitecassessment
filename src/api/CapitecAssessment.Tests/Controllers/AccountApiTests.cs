using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Controllers;
using CapitecAssessment.Models;
using CapitecAssessment.Models.Request;
using CapitecAssessment.Models.Response;
using CapitecAssessment.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CapitecAssessment.Tests.Controllers;

/// <summary>Full pipeline with a real sign-in, so the session cookie is what identifies the caller.</summary>
public class AccountApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OwnTransact = "1052000101";
    private const string OwnSavings = "1052000102";
    private const string OwnEmpty = "1052000103";
    private const string OtherUsersAccount = "1052000201";

    private static readonly AuthenticatedUser Lerato = new(1000000001, "lerato", UserRoles.Customer, "lerato@example.test");
    private static readonly AuthenticatedUser Thabo = new(1000000002, "thabo", UserRoles.Customer, "thabo@example.test");

    private readonly WebApplicationFactory<Program> _baseFactory;

    public AccountApiTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    private static AccountResponse Acct(string name, string number, string type, decimal balance) =>
        new() { AccountName = name, AccountNumber = number, AccountType = type, Balance = balance, Currency = "ZAR" };

    private static TransactionResponse Txn(string description, decimal amount, string type, DateTime date) =>
        new() { Id = Guid.NewGuid(), Description = description, Amount = amount, Type = type, TransactionDate = date, Reference = "REF" };

    private sealed record TestApp(HttpClient Client, FakeEmailSender Email);

    private TestApp CreateApp()
    {
        var auth = new FakeAuthenticationRepository();
        auth.AddAccount(OwnTransact, Lerato);
        auth.AddAccount(OtherUsersAccount, Thabo);

        var accounts = new FakeAccountRepository();
        var now = DateTime.UtcNow;
        accounts.Add(Lerato.UserId, Acct("Global One Account", OwnTransact, AccountTypes.Transact, 1500.50m),
            Txn("D10", 10m, TransactionTypes.Debit, now.AddDays(-10)),
            Txn("D5", 20m, TransactionTypes.Credit, now.AddDays(-5)),
            Txn("D45", 30m, TransactionTypes.Debit, now.AddDays(-45)),
            Txn("D75", 40m, TransactionTypes.Debit, now.AddDays(-75)),
            Txn("D120", 50m, TransactionTypes.Debit, now.AddDays(-120)));
        accounts.Add(Lerato.UserId, Acct("Flexi Savings", OwnSavings, AccountTypes.Savings, 25000m));
        accounts.Add(Lerato.UserId, Acct("Credit Card", OwnEmpty, AccountTypes.Credit, -300m));
        accounts.Add(Thabo.UserId, Acct("Global One Account", OtherUsersAccount, AccountTypes.Transact, 99m),
            Txn("SECRET", 5m, TransactionTypes.Debit, now.AddDays(-1)));

        var email = new FakeEmailSender();
        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Otp:HashKey", TestOtp.HashKey);
            builder.UseSetting("RateLimiting:Authentication:PermitLimit", "1000");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuthenticationRepository>();
                services.AddSingleton<IAuthenticationRepository>(auth);
                services.RemoveAll<INotificationRepository>();
                services.AddSingleton<INotificationRepository, FakeNotificationRepository>();
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(email);
                services.RemoveAll<IAccountRepository>();
                services.AddSingleton<IAccountRepository>(accounts);
            });
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

        return new TestApp(client, email);
    }

    private static async Task SignIn(TestApp app, string accountNumber)
    {
        await app.Client.PostAsJsonAsync("/api/v1/authenticate/request-otp", new { accountNumber });
        var code = Regex.Match(app.Email.Sent.Last().PlainText, "[0-9]{6}").Value;
        var verify = await app.Client.PostAsJsonAsync("/api/v1/authenticate/verify-otp", new { accountNumber, code });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/accounts")]
    [InlineData("/api/v1/accounts/1052000101/transactions")]
    public async Task Endpoints_WithoutSession_Return401(string url)
    {
        var app = CreateApp();

        var response = await app.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAccounts_ReturnsOnlyTheSignedInUsersAccounts()
    {
        var app = CreateApp();
        await SignIn(app, OwnTransact);

        var response = await app.Client.GetAsync("/api/v1/accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = await response.Content.ReadFromJsonAsync<List<AccountResponse>>();
        Assert.Equal(new[] { OwnTransact, OwnSavings, OwnEmpty }, accounts!.Select(a => a.AccountNumber).Order());

        var main = accounts!.Single(a => a.AccountNumber == OwnTransact);
        Assert.Equal("Global One Account", main.AccountName);
        Assert.Equal(AccountTypes.Transact, main.AccountType);
        Assert.Equal(1500.50m, main.Balance);
    }

    [Fact]
    public async Task GetAccounts_UsesTheCookieNotTheRequest_OtherUserSeesTheirOwn()
    {
        var app = CreateApp();
        await SignIn(app, OtherUsersAccount);

        var accounts = await app.Client.GetFromJsonAsync<List<AccountResponse>>("/api/v1/accounts");

        Assert.Equal(OtherUsersAccount, Assert.Single(accounts!).AccountNumber);
    }

    private static async Task<PagedResponse<TransactionResponse>> GetPage(TestApp app, string accountNumber, string query = "")
    {
        var response = await app.Client.GetAsync($"/api/v1/accounts/{accountNumber}/transactions{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>())!;
    }

    [Fact]
    public async Task GetTransactions_Defaults_Last30DaysNewestFirst_FirstPage()
    {
        var app = CreateApp();
        await SignIn(app, OwnTransact);

        var page = await GetPage(app, OwnTransact);

        Assert.Equal(new[] { "D5", "D10" }, page.Items.Select(t => t.Description));
        Assert.Equal(1, page.Page);
        Assert.Equal(TransactionsQueryRequest.DefaultPageSize, page.PageSize);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.TotalPages);
    }

    [Theory]
    [InlineData(30, new[] { "D5", "D10" })]
    [InlineData(60, new[] { "D5", "D10", "D45" })]
    [InlineData(90, new[] { "D5", "D10", "D45", "D75" })]
    public async Task GetTransactions_DaysSelectsTheWindow(int days, string[] expected)
    {
        var app = CreateApp();
        await SignIn(app, OwnTransact);

        var page = await GetPage(app, OwnTransact, $"?days={days}");

        Assert.Equal(expected, page.Items.Select(t => t.Description));
        Assert.Equal(expected.Length, page.TotalCount);
    }

    [Fact]
    public async Task GetTransactions_Paginates_WithoutOverlap_AndReportsTotals()
    {
        var app = CreateApp();
        await SignIn(app, OwnTransact);

        var first = await GetPage(app, OwnTransact, "?days=90&page=1&pageSize=3");
        var second = await GetPage(app, OwnTransact, "?days=90&page=2&pageSize=3");
        var beyond = await GetPage(app, OwnTransact, "?days=90&page=3&pageSize=3");

        Assert.Equal(new[] { "D5", "D10", "D45" }, first.Items.Select(t => t.Description));
        Assert.Equal(new[] { "D75" }, second.Items.Select(t => t.Description));
        Assert.Empty(beyond.Items);
        Assert.All(new[] { first, second, beyond }, p => Assert.Equal(4, p.TotalCount));
        Assert.All(new[] { first, second, beyond }, p => Assert.Equal(2, p.TotalPages));
    }

    [Theory]
    [InlineData("days=7")]
    [InlineData("days=abc")]
    [InlineData("days=0")]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task GetTransactions_InvalidQuery_Returns400(string query)
    {
        var app = CreateApp();
        await SignIn(app, OwnTransact);

        var response = await app.Client.GetAsync($"/api/v1/accounts/{OwnTransact}/transactions?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTransactions_OwnAccountWithNoTransactions_Returns200WithEmptyPage()
    {
        var app = CreateApp();
        await SignIn(app, OwnTransact);

        var page = await GetPage(app, OwnEmpty);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
    }

    [Fact]
    public async Task GetTransactions_AnotherUsersAccount_Returns404_SameAsAnUnknownAccount()
    {
        var app = CreateApp();
        await SignIn(app, OwnTransact);

        var foreign = await app.Client.GetAsync($"/api/v1/accounts/{OtherUsersAccount}/transactions");
        var unknown = await app.Client.GetAsync("/api/v1/accounts/0000000000/transactions");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var problem = await foreign.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(AccountController.AccountNotFoundTitle, problem!.Title);
        Assert.DoesNotContain("SECRET", await foreign.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("12345abcde")]
    public async Task GetTransactions_MalformedAccountNumber_Returns400(string accountNumber)
    {
        var app = CreateApp();
        await SignIn(app, OwnTransact);

        var response = await app.Client.GetAsync($"/api/v1/accounts/{accountNumber}/transactions");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
