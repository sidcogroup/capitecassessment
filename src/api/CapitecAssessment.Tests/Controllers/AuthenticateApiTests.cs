using System.Net;
using System.Net.Http.Json;
using CapitecAssessment.Controllers;
using CapitecAssessment.Extensions;
using CapitecAssessment.Models;
using CapitecAssessment.Models.Response;
using CapitecAssessment.Tests.Fakes;
using CapitecAssessment.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CapitecAssessment.Tests.Controllers;

/// <summary>
/// HTTP tests for sign-in (<c>/api/v1/authenticate</c>).
///
/// How sign-in works:
///   1. The user sends their account number (customers) or username (consultants).
///      The API emails them a 6-digit one-time pin (OTP).
///   2. The user sends the pin back. If it is correct, the API sets a session cookie.
///   3. Every later request is identified by that cookie alone.
///
/// The emails are caught by a fake inbox, so tests can read the pin just like a real user would.
/// </summary>
public class AuthenticateApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    // A customer signs in with their 10-digit account number.
    private const string CustomerAccountNumber = "1234567890";
    private static readonly AuthenticatedUser Customer = new(5550001111, "thandi", UserRoles.Customer, "thandi@example.test");

    // A consultant (bank staff) signs in with their username instead.
    private const string ConsultantUsername = "consultant1";
    private static readonly AuthenticatedUser Consultant = new(5550002222, ConsultantUsername, UserRoles.Consultant, "consultant@example.test");

    // A well-formed account number that does not belong to anybody.
    private const string UnregisteredAccountNumber = "0000000000";

    private readonly WebApplicationFactory<Program> _factory;

    public AuthenticateApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    /// <summary>What each test gets: a client to call the API with, and the inbox the API emails pins to.</summary>
    private sealed record TestApp(HttpClient Client, FakeEmailSender Inbox);

    private TestApp StartApi(int signInRequestsAllowed = TestApi.NoRateLimit)
    {
        var users = new FakeAuthenticationRepository();
        users.AddAccount(CustomerAccountNumber, Customer);
        users.AddAccount(ConsultantUsername, Consultant);
        var inbox = new FakeEmailSender();

        var client = TestApi.CreateClient(_factory, users, inbox, signInRequestsAllowed: signInRequestsAllowed);
        return new TestApp(client, inbox);
    }

    // ---------------------------------------------------------------------------------------------
    // Step 1: requesting a one-time pin
    // ---------------------------------------------------------------------------------------------

    /// <summary>Anything that is neither a 10-digit account number nor a valid username is rejected, and no email is sent.</summary>
    [Theory]
    [InlineData("")]               // empty
    [InlineData("123456789")]      // 9 digits: too short for an account number
    [InlineData("12345678901")]    // 11 digits: too long for an account number
    [InlineData("ab")]             // too short for a username (minimum 3 characters)
    [InlineData("has space")]      // spaces are not allowed
    [InlineData("bad!name")]       // '!' is not allowed
    [InlineData("12345678-90")]    // not 10 digits, and a username must contain at least one letter
    [InlineData("١٢٣٤٥٦٧٨٩٠")]     // Arabic-Indic digits: they look like numbers but only 0-9 are accepted
    public async Task RequestOtp_MalformedIdentifier_Returns400(string identifier)
    {
        var app = StartApi();

        var response = await TestApi.RequestOtpAsync(app.Client, identifier);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(app.Inbox.Sent);
    }

    /// <summary>
    /// The current field name is <c>identifier</c>. Older apps still send <c>accountNumber</c>,
    /// so both must keep working.
    /// </summary>
    [Fact]
    public async Task RequestOtp_AcceptsIdentifierAndTheOlderAccountNumberField()
    {
        var app = StartApi();

        var usingNewField = await app.Client.PostAsJsonAsync(AuthRoutes.RequestOtp, new { identifier = CustomerAccountNumber });
        var usingOldField = await app.Client.PostAsJsonAsync(AuthRoutes.RequestOtp, new { accountNumber = CustomerAccountNumber });

        Assert.Equal(HttpStatusCode.OK, usingNewField.StatusCode);
        Assert.Equal(HttpStatusCode.OK, usingOldField.StatusCode);
    }

    /// <summary>If both fields are sent with different values the API cannot tell who is signing in, so it rejects the request.</summary>
    [Fact]
    public async Task RequestOtp_TwoDifferentValues_Returns400()
    {
        var app = StartApi();

        var response = await app.Client.PostAsJsonAsync(
            AuthRoutes.RequestOtp, new { identifier = CustomerAccountNumber, accountNumber = UnregisteredAccountNumber });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(app.Inbox.Sent);
    }

    /// <summary>
    /// Security: an attacker must not be able to discover which account numbers exist.
    /// So a registered and an unregistered account number get exactly the same response.
    /// Only the real owner actually receives an email.
    /// </summary>
    [Fact]
    public async Task RequestOtp_KnownAndUnknownAccounts_GetIdenticalResponses()
    {
        var app = StartApi();

        var registered = await TestApi.RequestOtpAsync(app.Client, CustomerAccountNumber);
        var unregistered = await TestApi.RequestOtpAsync(app.Client, UnregisteredAccountNumber);

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unregistered.StatusCode);
        Assert.Equal(await registered.Content.ReadAsStringAsync(), await unregistered.Content.ReadAsStringAsync());

        var onlyEmailSent = Assert.Single(app.Inbox.Sent);
        Assert.Equal(Customer.Email, onlyEmailSent.Recipient);
    }

    // ---------------------------------------------------------------------------------------------
    // Step 2: verifying the pin and receiving the session cookie
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The happy path for a customer. Checks that the session cookie has the security settings
    /// that stop it being stolen or misused, and that the cookie alone is enough to identify the user.
    /// </summary>
    [Fact]
    public async Task FullSignIn_SetsHardenedSessionCookie_ThatIdentifiesTheUser()
    {
        // Arrange: ask for a pin, then read it from the inbox.
        var app = StartApi();
        await TestApi.RequestOtpAsync(app.Client, CustomerAccountNumber);
        var pin = TestApi.OtpFromLatestEmail(app.Inbox);

        // Act: send the pin back.
        var verify = await TestApi.VerifyOtpAsync(app.Client, CustomerAccountNumber, pin);

        // Assert: signed in as the right user.
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var session = await verify.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(Customer.UserId, session!.UserId);

        // Assert: exactly one cookie is set, and it is locked down.
        var cookie = Assert.Single(verify.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(ServiceCollectionExtensions.SessionCookieName + "=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);        // page JavaScript cannot read it
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);          // only ever sent over HTTPS
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase); // not sent by other websites
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);

        // Assert: the next request (which carries only the cookie) is recognised as this user.
        var me = await app.Client.GetAsync(AuthRoutes.Me);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var whoAmI = await me.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(Customer.UserId, whoAmI!.UserId);
        Assert.Equal(Customer.Username, whoAmI.Username);
        Assert.Equal(Customer.Role, whoAmI.Role);
    }

    /// <summary>A consultant signs in with a username (not an account number) and gets a session with the Consultant role.</summary>
    [Fact]
    public async Task SignIn_ConsultantByUsername_ReturnsAConsultantSession()
    {
        var app = StartApi();
        await TestApi.RequestOtpAsync(app.Client, ConsultantUsername);

        var verify = await TestApi.VerifyOtpAsync(app.Client, ConsultantUsername, TestApi.OtpFromLatestEmail(app.Inbox));

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.Equal(Consultant.Email, app.Inbox.Sent.Last().Recipient);
        var session = await verify.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(UserRoles.Consultant, session!.Role);
        Assert.Equal(ConsultantUsername, session.Username);
    }

    /// <summary>A wrong pin is refused with 401, no cookie is set, and the error message does not say why (to give attackers nothing).</summary>
    [Fact]
    public async Task VerifyOtp_WrongCode_Returns401WithoutCookie()
    {
        var app = StartApi();
        await TestApi.RequestOtpAsync(app.Client, CustomerAccountNumber);
        var realPin = TestApi.OtpFromLatestEmail(app.Inbox);
        var wrongPin = realPin == "000000" ? "000001" : "000000";

        var response = await TestApi.VerifyOtpAsync(app.Client, CustomerAccountNumber, wrongPin);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(AuthenticateController.InvalidCodeTitle, problem!.Title);
    }

    /// <summary>Verifying a pin for an account number nobody owns fails with the same 401 as a wrong pin.</summary>
    [Fact]
    public async Task VerifyOtp_UnknownAccount_Returns401()
    {
        var app = StartApi();

        var response = await TestApi.VerifyOtpAsync(app.Client, UnregisteredAccountNumber, "123456");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Verifying a pin for a username nobody owns fails with the same 401 as a wrong pin.</summary>
    [Fact]
    public async Task VerifyOtp_UnknownUsername_Returns401()
    {
        var app = StartApi();

        var response = await TestApi.VerifyOtpAsync(app.Client, "nobody.here", "123456");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A pin that is not exactly 6 digits is rejected as a bad request before it is even checked.</summary>
    [Theory]
    [InlineData("12345")]     // 5 digits
    [InlineData("1234567")]   // 7 digits
    [InlineData("abcdef")]    // letters
    public async Task VerifyOtp_MalformedCode_Returns400(string code)
    {
        var app = StartApi();

        var response = await TestApi.VerifyOtpAsync(app.Client, CustomerAccountNumber, code);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------------
    // Session: who am I, and logging out
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Without a session cookie the API answers 401. It must not redirect to a login page
    /// (that is what websites do, but an API client needs a clear status code).
    /// </summary>
    [Fact]
    public async Task Me_WithoutSession_Returns401NotRedirect()
    {
        var app = StartApi();

        var response = await app.Client.GetAsync(AuthRoutes.Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>After logging out, the old session no longer works.</summary>
    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, CustomerAccountNumber);

        var logout = await app.Client.PostAsync(AuthRoutes.Logout, null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var meAfterLogout = await app.Client.GetAsync(AuthRoutes.Me);
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogout.StatusCode);
    }

    // ---------------------------------------------------------------------------------------------
    // Abuse protection
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Sign-in calls are rate limited, so an attacker cannot hammer the endpoint.
    /// Here the limit is set to 2: the first two calls succeed and the third gets HTTP 429 Too Many Requests.
    /// </summary>
    [Fact]
    public async Task SignInEndpoints_AreRateLimitedPerClient()
    {
        var app = StartApi(signInRequestsAllowed: 2);

        var first = await TestApi.RequestOtpAsync(app.Client, "0000000001");
        var second = await TestApi.RequestOtpAsync(app.Client, "0000000002");
        var third = await TestApi.RequestOtpAsync(app.Client, "0000000003");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }
}
