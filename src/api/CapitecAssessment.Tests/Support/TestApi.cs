using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CapitecAssessment.Tests.Support;

/// <summary>
/// The API's sign-in URLs, kept in one place so a route change is a one-line fix for every test.
/// </summary>
public static class AuthRoutes
{
    /// <summary>Step 1 of sign-in: ask for a one-time pin (OTP) to be emailed.</summary>
    public const string RequestOtp = "/api/v1/authenticate";

    /// <summary>Step 2 of sign-in: send the emailed pin back to receive a session cookie.</summary>
    public const string VerifyOtp = "/api/v1/authenticate/verify-otp";

    /// <summary>Returns who the current session cookie belongs to.</summary>
    public const string Me = "/api/v1/authenticate/me";

    /// <summary>Ends the session.</summary>
    public const string Logout = "/api/v1/authenticate/logout";
}

/// <summary>
/// Starts a real copy of the API in memory for HTTP-level tests.
///
/// Everything runs for real (routing, validation, cookie sign-in, rate limiting) EXCEPT the parts
/// that would touch the outside world. Those are swapped for in-memory fakes:
///   * the database tables used for sign-in  -> <see cref="FakeAuthenticationRepository"/>
///   * sending email                          -> <see cref="FakeEmailSender"/> (an "inbox" the test can read)
///   * saving notification records            -> <see cref="FakeNotificationRepository"/>
/// A test class can swap out more services (e.g. accounts) with the <c>replaceMoreServices</c> argument.
/// </summary>
public static class TestApi
{
    /// <summary>High enough that normal tests never hit the sign-in rate limit.</summary>
    public const int NoRateLimit = 1000;

    /// <summary>
    /// Creates an HTTP client connected to an in-memory copy of the API.
    /// The client remembers cookies, like a browser, so once a test signs in, later calls are signed in too.
    /// </summary>
    /// <param name="factory">The xUnit-provided factory that knows how to start the API.</param>
    /// <param name="users">Who can sign in, and with which account number or username.</param>
    /// <param name="inbox">Captures every email the API sends, so the test can read the one-time pin.</param>
    /// <param name="notifications">Captures notification records. Pass one in if the test wants to inspect them.</param>
    /// <param name="signInRequestsAllowed">How many sign-in calls one client may make before getting HTTP 429.</param>
    /// <param name="replaceMoreServices">Extra fakes this test class needs (for example an account repository).</param>
    public static HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        FakeAuthenticationRepository users,
        FakeEmailSender inbox,
        FakeNotificationRepository? notifications = null,
        int signInRequestsAllowed = NoRateLimit,
        Action<IServiceCollection>? replaceMoreServices = null)
    {
        notifications ??= new FakeNotificationRepository();

        var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Otp:HashKey", TestOtp.HashKey);
            builder.UseSetting("RateLimiting:Authentication:PermitLimit", signInRequestsAllowed.ToString());
            builder.ConfigureTestServices(services =>
            {
                ReplaceWith<IAuthenticationRepository>(services, users);
                ReplaceWith<INotificationRepository>(services, notifications);
                ReplaceWith<IEmailSender>(services, inbox);
                replaceMoreServices?.Invoke(services);
            });
        });

        // HTTPS is needed because the session cookie is marked "Secure": a browser (and this client)
        // only stores and sends it back over HTTPS.
        return configuredFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
    }

    /// <summary>Removes the API's real implementation of <typeparamref name="TService"/> and uses <paramref name="fake"/> instead.</summary>
    public static void ReplaceWith<TService>(IServiceCollection services, TService fake) where TService : class
    {
        services.RemoveAll<TService>();
        services.AddSingleton(fake);
    }

    /// <summary>Calls sign-in step 1: asks the API to email a one-time pin to whoever owns <paramref name="identifier"/>.</summary>
    public static Task<HttpResponseMessage> RequestOtpAsync(HttpClient client, string identifier) =>
        client.PostAsJsonAsync(AuthRoutes.RequestOtp, new { identifier });

    /// <summary>Calls sign-in step 2: sends the one-time pin back. On success the response sets the session cookie.</summary>
    public static Task<HttpResponseMessage> VerifyOtpAsync(HttpClient client, string identifier, string code) =>
        client.PostAsJsonAsync(AuthRoutes.VerifyOtp, new { identifier, code });

    /// <summary>Reads the 6-digit one-time pin out of the most recent email in the fake inbox.</summary>
    public static string OtpFromLatestEmail(FakeEmailSender inbox)
    {
        Assert.False(inbox.Sent.IsEmpty, "Expected the API to have emailed a one-time pin, but no email was sent.");
        return Regex.Match(inbox.Sent.Last().PlainText, "[0-9]{6}").Value;
    }

    /// <summary>
    /// Does the whole two-step sign-in (request a pin, read it from the inbox, verify it) and checks it worked.
    /// After this, <paramref name="client"/> holds the session cookie, so later calls are made as this user.
    /// </summary>
    public static async Task SignInAsync(HttpClient client, FakeEmailSender inbox, string identifier)
    {
        var request = await RequestOtpAsync(client, identifier);
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);

        var verify = await VerifyOtpAsync(client, identifier, OtpFromLatestEmail(inbox));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
    }
}
