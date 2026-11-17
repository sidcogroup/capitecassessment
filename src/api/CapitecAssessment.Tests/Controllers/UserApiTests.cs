using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Controllers;
using CapitecAssessment.Models;
using CapitecAssessment.Models.Response;
using CapitecAssessment.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CapitecAssessment.Tests.Controllers;

/// <summary>Full pipeline with a real sign-in, so the session cookie is what identifies the caller.</summary>
public class UserApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string LeratoAccount = "1052000101";
    private const string ThaboAccount = "1052000201";
    private const string NoProfileAccount = "1052000301";

    private static readonly AuthenticatedUser Lerato = new(1000000001, "lerato", UserRoles.Customer, "lerato@example.test");
    private static readonly AuthenticatedUser Thabo = new(1000000002, "thabo", UserRoles.Customer, "thabo@example.test");
    private static readonly AuthenticatedUser NoProfile = new(1000000003, "noprofile", UserRoles.Customer, "noprofile@example.test");

    private readonly WebApplicationFactory<Program> _baseFactory;

    public UserApiTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    private sealed record TestApp(HttpClient Client, FakeEmailSender Email);

    private TestApp CreateApp()
    {
        var auth = new FakeAuthenticationRepository();
        auth.AddAccount(LeratoAccount, Lerato);
        auth.AddAccount(ThaboAccount, Thabo);
        auth.AddAccount(NoProfileAccount, NoProfile);

        var users = new FakeUserRepository();
        users.Add(new UserProfileResponse { UserId = Lerato.UserId, FirstName = "Lerato", LastName = "Dlamini" });
        users.Add(new UserProfileResponse { UserId = Thabo.UserId, FirstName = "Thabo", LastName = "Nkosi" });

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
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(users);
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

    [Fact]
    public async Task GetProfile_WithoutSession_Returns401()
    {
        var app = CreateApp();

        var response = await app.Client.GetAsync("/api/v1/users/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_ReturnsTheSignedInUsersName()
    {
        var app = CreateApp();
        await SignIn(app, LeratoAccount);

        var profile = await app.Client.GetFromJsonAsync<UserProfileResponse>("/api/v1/users/profile");

        Assert.Equal(Lerato.UserId, profile!.UserId);
        Assert.Equal("Lerato", profile.FirstName);
        Assert.Equal("Dlamini", profile.LastName);
        Assert.Equal("Lerato Dlamini", profile.FullName);
    }

    [Fact]
    public async Task GetProfile_UsesTheCookieNotTheRequest_OtherUserSeesTheirOwn()
    {
        var app = CreateApp();
        await SignIn(app, ThaboAccount);

        var profile = await app.Client.GetFromJsonAsync<UserProfileResponse>("/api/v1/users/profile");

        Assert.Equal("Thabo Nkosi", profile!.FullName);
    }

    [Fact]
    public async Task GetProfile_UserWithoutProfile_Returns404()
    {
        var app = CreateApp();
        await SignIn(app, NoProfileAccount);

        var response = await app.Client.GetAsync("/api/v1/users/profile");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(UserController.ProfileNotFoundTitle, problem!.Title);
    }
}
