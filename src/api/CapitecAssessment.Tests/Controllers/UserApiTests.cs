using System.Net;
using System.Net.Http.Json;
using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Controllers;
using CapitecAssessment.Models;
using CapitecAssessment.Models.Response;
using CapitecAssessment.Tests.Fakes;
using CapitecAssessment.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CapitecAssessment.Tests.Controllers;

/// <summary>
/// HTTP tests for <c>GET /api/v1/users/profile</c>, which returns the signed-in user's name
/// (shown as "Hello, Lerato" in the banking app).
///
/// Test data:
///   - Lerato and Thabo both have a profile with their first and last name.
///   - A third user can sign in but has no profile saved.
/// </summary>
public class UserApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string LeratoAccount = "1052000101";
    private const string ThaboAccount = "1052000201";
    private const string AccountOfUserWithoutProfile = "1052000301";

    private static readonly AuthenticatedUser Lerato = new(1000000001, "lerato", UserRoles.Customer, "lerato@example.test");
    private static readonly AuthenticatedUser Thabo = new(1000000002, "thabo", UserRoles.Customer, "thabo@example.test");
    private static readonly AuthenticatedUser UserWithoutProfile = new(1000000003, "noprofile", UserRoles.Customer, "noprofile@example.test");

    private const string ProfileUrl = "/api/v1/users/profile";

    private readonly WebApplicationFactory<Program> _factory;

    public UserApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private sealed record TestApp(HttpClient Client, FakeEmailSender Inbox);

    private TestApp StartApi()
    {
        var users = new FakeAuthenticationRepository();
        users.AddAccount(LeratoAccount, Lerato);
        users.AddAccount(ThaboAccount, Thabo);
        users.AddAccount(AccountOfUserWithoutProfile, UserWithoutProfile);

        // Only Lerato and Thabo have a saved profile.
        var profiles = new FakeUserRepository();
        profiles.Add(new UserProfileResponse { UserId = Lerato.UserId, FirstName = "Lerato", LastName = "Dlamini" });
        profiles.Add(new UserProfileResponse { UserId = Thabo.UserId, FirstName = "Thabo", LastName = "Nkosi" });

        var inbox = new FakeEmailSender();
        var client = TestApi.CreateClient(_factory, users, inbox,
            replaceMoreServices: services => TestApi.ReplaceWith<IUserRepository>(services, profiles));
        return new TestApp(client, inbox);
    }

    /// <summary>Without signing in, the profile endpoint returns 401 Unauthorized.</summary>
    [Fact]
    public async Task GetProfile_WithoutSession_Returns401()
    {
        var app = StartApi();

        var response = await app.Client.GetAsync(ProfileUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Lerato signs in and gets her own first name, last name and full name.</summary>
    [Fact]
    public async Task GetProfile_ReturnsTheSignedInUsersName()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, LeratoAccount);

        var profile = await app.Client.GetFromJsonAsync<UserProfileResponse>(ProfileUrl);

        Assert.Equal(Lerato.UserId, profile!.UserId);
        Assert.Equal("Lerato", profile.FirstName);
        Assert.Equal("Dlamini", profile.LastName);
        Assert.Equal("Lerato Dlamini", profile.FullName);
    }

    /// <summary>
    /// The same URL returns Thabo's name when Thabo is signed in. This proves the API decides whose
    /// profile to return from the session cookie, not from anything the caller sends.
    /// </summary>
    [Fact]
    public async Task GetProfile_UsesTheCookieNotTheRequest_OtherUserSeesTheirOwn()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, ThaboAccount);

        var profile = await app.Client.GetFromJsonAsync<UserProfileResponse>(ProfileUrl);

        Assert.Equal("Thabo Nkosi", profile!.FullName);
    }

    /// <summary>A signed-in user who has no saved profile gets 404 Not Found with a clear error title.</summary>
    [Fact]
    public async Task GetProfile_UserWithoutProfile_Returns404()
    {
        var app = StartApi();
        await TestApi.SignInAsync(app.Client, app.Inbox, AccountOfUserWithoutProfile);

        var response = await app.Client.GetAsync(ProfileUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(UserController.ProfileNotFoundTitle, problem!.Title);
    }
}
