using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Controllers;
using CapitecAssessment.Extensions;
using CapitecAssessment.Models;
using CapitecAssessment.Models.Response;
using CapitecAssessment.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CapitecAssessment.Tests.Controllers;

/// <summary>
/// Full HTTP pipeline (validation, cookie auth, rate limiting) with the database and Azure
/// Communication Services swapped for in-memory fakes. The emailed code is read from the fake sender.
/// </summary>
public class AuthenticateApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AccountNumber = "1234567890";
    private static readonly AuthenticatedUser User = new(5550001111, "thandi", UserRoles.Customer, "thandi@example.test");

    private readonly WebApplicationFactory<Program> _baseFactory;

    public AuthenticateApiTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    private sealed record TestApp(HttpClient Client, FakeEmailSender Email);

    private TestApp CreateApp(int permitLimit = 1000)
    {
        var repository = new FakeAuthenticationRepository();
        repository.AddAccount(AccountNumber, User);
        var email = new FakeEmailSender();

        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Otp:HashKey", TestOtp.HashKey);
            builder.UseSetting("RateLimiting:Authentication:PermitLimit", permitLimit.ToString());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuthenticationRepository>();
                services.AddSingleton<IAuthenticationRepository>(repository);
                services.RemoveAll<INotificationRepository>();
                services.AddSingleton<INotificationRepository, FakeNotificationRepository>();
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(email);
            });
        });

        // https so the browser-like cookie container accepts and returns the Secure, __Host- cookie.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

        return new TestApp(client, email);
    }

    private static string CodeFrom(FakeEmailSender email) =>
        Regex.Match(email.Sent.Last().PlainText, "[0-9]{6}").Value;

    private static Task<HttpResponseMessage> RequestOtp(HttpClient client, string accountNumber) =>
        client.PostAsJsonAsync("/api/v1/authenticate/request-otp", new { accountNumber });

    private static Task<HttpResponseMessage> VerifyOtp(HttpClient client, string accountNumber, string code) =>
        client.PostAsJsonAsync("/api/v1/authenticate/verify-otp", new { accountNumber, code });

    [Theory]
    [InlineData("")]
    [InlineData("123456789")]     // 9 digits
    [InlineData("12345678901")]   // 11 digits
    [InlineData("12345abcde")]
    [InlineData("١٢٣٤٥٦٧٨٩٠")]    // Arabic-Indic digits: \d would accept these, [0-9] does not
    public async Task RequestOtp_MalformedAccountNumber_Returns400(string accountNumber)
    {
        var app = CreateApp();

        var response = await RequestOtp(app.Client, accountNumber);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task RequestOtp_KnownAndUnknownAccounts_GetIdenticalResponses()
    {
        var app = CreateApp();

        var known = await RequestOtp(app.Client, AccountNumber);
        var unknown = await RequestOtp(app.Client, "0000000000");

        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(User.Email, Assert.Single(app.Email.Sent).Recipient);
    }

    [Fact]
    public async Task FullSignIn_SetsHardenedSessionCookie_ThatIdentifiesTheUser()
    {
        var app = CreateApp();
        await RequestOtp(app.Client, AccountNumber);

        var verify = await VerifyOtp(app.Client, AccountNumber, CodeFrom(app.Email));

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var session = await verify.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(User.UserId, session!.UserId);

        var setCookie = Assert.Single(verify.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(ServiceCollectionExtensions.SessionCookieName + "=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);

        // The cookie alone tells the API who is calling.
        var me = await app.Client.GetAsync("/api/v1/authenticate/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var identity = await me.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(User.UserId, identity!.UserId);
        Assert.Equal(User.Username, identity.Username);
        Assert.Equal(User.Role, identity.Role);
    }

    [Fact]
    public async Task Me_WithoutSession_Returns401NotRedirect()
    {
        var app = CreateApp();

        var response = await app.Client.GetAsync("/api/v1/authenticate/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyOtp_WrongCode_Returns401WithoutCookie()
    {
        var app = CreateApp();
        await RequestOtp(app.Client, AccountNumber);
        var wrong = CodeFrom(app.Email) == "000000" ? "000001" : "000000";

        var response = await VerifyOtp(app.Client, AccountNumber, wrong);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(AuthenticateController.InvalidCodeTitle, problem!.Title);
    }

    [Fact]
    public async Task VerifyOtp_UnknownAccount_Returns401()
    {
        var app = CreateApp();

        var response = await VerifyOtp(app.Client, "0000000000", "123456");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    public async Task VerifyOtp_MalformedCode_Returns400(string code)
    {
        var app = CreateApp();

        var response = await VerifyOtp(app.Client, AccountNumber, code);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var app = CreateApp();
        await RequestOtp(app.Client, AccountNumber);
        await VerifyOtp(app.Client, AccountNumber, CodeFrom(app.Email));

        var logout = await app.Client.PostAsync("/api/v1/authenticate/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.GetAsync("/api/v1/authenticate/me")).StatusCode);
    }

    [Fact]
    public async Task SignInEndpoints_AreRateLimitedPerClient()
    {
        var app = CreateApp(permitLimit: 2);

        Assert.Equal(HttpStatusCode.OK, (await RequestOtp(app.Client, "0000000001")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RequestOtp(app.Client, "0000000002")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RequestOtp(app.Client, "0000000003")).StatusCode);
    }
}
