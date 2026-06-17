using System.Net;
using AwesomeAssertions;
using McpManager.Core.Data.Models.Identity;
using McpManager.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace McpManager.IntegrationTests;

/// <summary>
/// SSO behaviour when OIDC is NOT configured (the default). The button must be absent
/// and the SSO endpoints must degrade gracefully instead of throwing because no "oidc"
/// authentication scheme is registered.
/// </summary>
public class OidcDisabledAuthControllerTests : IClassFixture<WebFactoryFixture>
{
    private readonly WebFactoryFixture _factory;

    public OidcDisabledAuthControllerTests(WebFactoryFixture factory) => _factory = factory;

    [Fact]
    public async Task LoginPage_WhenOidcNotConfigured_HasNoSsoButton()
    {
        var client = _factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var response = await client.GetAsync("/Auth/Login", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);

        body.Should().NotContain("Sign in with");
        body.Should().NotContain("/auth/externallogin");
    }

    [Fact]
    public async Task ExternalLogin_WhenOidcNotConfigured_RedirectsToLoginWithoutThrowing()
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
        var ct = TestContext.Current.CancellationToken;

        // Challenging an unregistered "oidc" scheme would 500; the action must guard
        // on IsConfigured and bounce back to the login page instead.
        var response = await client.GetAsync("/Auth/ExternalLogin", ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().ContainEquivalentOf("/auth/login");
    }

    [Fact]
    public async Task ExternalLoginCallback_WhenOidcNotConfigured_RedirectsToLogin()
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
        var ct = TestContext.Current.CancellationToken;

        var response = await client.GetAsync("/Auth/ExternalLoginCallback", ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().ContainEquivalentOf("/auth/login");
    }
}

/// <summary>
/// SSO behaviour when OIDC IS configured. Covers the login button plus the full
/// callback matching logic. The provider round-trip is simulated by
/// <see cref="ExternalSignInTestStartupFilter"/>, which plants the external-auth
/// cookie the OIDC handler would normally write; the callback then runs the real
/// email-matching and sign-in code. (The challenge → provider redirect needs the live
/// discovery document and is exercised against a real provider rather than here.)
/// </summary>
public class OidcEnabledAuthControllerTests : IClassFixture<OidcWebFactoryFixture>
{
    private readonly OidcWebFactoryFixture _factory;

    public OidcEnabledAuthControllerTests(OidcWebFactoryFixture factory) => _factory = factory;

    private static WebApplicationFactoryClientOptions NoRedirect() =>
        new() { AllowAutoRedirect = false, HandleCookies = true };

    [Fact]
    public async Task LoginPage_WhenOidcConfigured_ShowsSsoButton()
    {
        var client = _factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var response = await client.GetAsync("/Auth/Login", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);

        body.Should().Contain($"Sign in with {OidcWebFactoryFixture.DisplayName}");
        body.Should().Contain("/auth/externallogin");
    }

    [Fact]
    public async Task Callback_WithMatchingActiveEmail_SignsInAndRedirectsHome()
    {
        var client = _factory.CreateClient(NoRedirect());
        var ct = TestContext.Current.CancellationToken;

        // Plant the external cookie for the seeded admin's email, then replay the callback.
        var planted = await client.GetAsync(
            $"{ExternalSignInTestStartupFilter.Path}?email=admin@mcpmanager.local",
            ct
        );
        planted.EnsureSuccessStatusCode();

        var callback = await client.GetAsync("/Auth/ExternalLoginCallback", ct);

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().ContainEquivalentOf("/home");

        // The application cookie should now grant access to a protected page.
        var protectedPage = await client.GetAsync("/Home", ct);
        protectedPage.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Callback_WithUnknownEmailAndNoAutoProvision_RedirectsToLoginWithError()
    {
        var client = _factory.CreateClient(NoRedirect());
        var ct = TestContext.Current.CancellationToken;

        await client.GetAsync(
            $"{ExternalSignInTestStartupFilter.Path}?email=nobody@example.com",
            ct
        );

        var callback = await client.GetAsync("/Auth/ExternalLoginCallback", ct);

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().ContainEquivalentOf("/auth/login");
        callback.Headers.Location!.ToString().Should().Contain("error");
    }

    [Fact]
    public async Task Callback_WithInactiveMatchedUser_RedirectsToLoginWithError()
    {
        var ct = TestContext.Current.CancellationToken;
        const string email = "inactive-sso@example.com";
        await CreateUserAsync(email, isActive: false);

        var client = _factory.CreateClient(NoRedirect());
        await client.GetAsync($"{ExternalSignInTestStartupFilter.Path}?email={email}", ct);

        var callback = await client.GetAsync("/Auth/ExternalLoginCallback", ct);

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().ContainEquivalentOf("/auth/login");

        // A protected page must still bounce to login — the inactive user is not signed in.
        var protectedPage = await client.GetAsync("/Home", ct);
        protectedPage.StatusCode.Should().Be(HttpStatusCode.Redirect);
        protectedPage.Headers.Location!.ToString().Should().ContainEquivalentOf("/auth/login");
    }

    [Fact]
    public async Task Callback_WithNoEmailClaim_RedirectsToLoginWithError()
    {
        var client = _factory.CreateClient(NoRedirect());
        var ct = TestContext.Current.CancellationToken;

        // Plant an external cookie with no email claim.
        await client.GetAsync(ExternalSignInTestStartupFilter.Path, ct);

        var callback = await client.GetAsync("/Auth/ExternalLoginCallback", ct);

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().ContainEquivalentOf("/auth/login");
        callback.Headers.Location!.ToString().Should().Contain("error");
    }

    private async Task CreateUserAsync(string email, bool isActive)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            return;
        }

        var user = new User
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            IsActive = isActive,
            GivenName = "Test",
            Surname = "User",
        };
        var result = await userManager.CreateAsync(user, "123456");
        result.Succeeded.Should().BeTrue();
    }
}

/// <summary>
/// SSO callback with auto-provisioning enabled: an unknown SSO email creates a local
/// account on the fly and signs the user in.
/// </summary>
public class OidcAutoProvisionAuthControllerTests : IClassFixture<OidcAutoProvisionWebFactoryFixture>
{
    private readonly OidcAutoProvisionWebFactoryFixture _factory;

    public OidcAutoProvisionAuthControllerTests(OidcAutoProvisionWebFactoryFixture factory) =>
        _factory = factory;

    [Fact]
    public async Task Callback_WithUnknownEmail_ProvisionsUserAndSignsIn()
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true }
        );
        var ct = TestContext.Current.CancellationToken;
        const string email = "newcomer@example.com";

        await client.GetAsync($"{ExternalSignInTestStartupFilter.Path}?email={email}", ct);

        var callback = await client.GetAsync("/Auth/ExternalLoginCallback", ct);

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().ContainEquivalentOf("/home");

        // The user should have been created and be able to reach a protected page.
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var created = await userManager.FindByEmailAsync(email);
        created.Should().NotBeNull();
        created!.IsActive.Should().BeTrue();

        var protectedPage = await client.GetAsync("/Home", ct);
        protectedPage.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
