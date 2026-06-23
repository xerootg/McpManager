using System.Net;
using AngleSharp;
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
    public async Task Callback_WithUnverifiedEmailAndDefaultConfig_SignsIn()
    {
        var client = _factory.CreateClient(NoRedirect());
        var ct = TestContext.Current.CancellationToken;

        // RequireVerifiedEmail is off by default, so even an explicit email_verified=false
        // (as Authentik emits for accounts with no verification flow) must still match and
        // sign in — otherwise legitimate users are locked out.
        var planted = await client.GetAsync(
            $"{ExternalSignInTestStartupFilter.Path}?email=admin@mcpmanager.local&emailVerified=false",
            ct
        );
        planted.EnsureSuccessStatusCode();

        var callback = await client.GetAsync("/Auth/ExternalLoginCallback", ct);

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().ContainEquivalentOf("/home");

        var protectedPage = await client.GetAsync("/Home", ct);
        protectedPage.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Callback_WithMatchingEmailAndNoVerifiedClaim_SignsIn()
    {
        var client = _factory.CreateClient(NoRedirect());
        var ct = TestContext.Current.CancellationToken;

        // Many providers (e.g. Authentik) never emit email_verified. An absent claim must
        // NOT block sign-in — otherwise every such provider's users are locked out.
        var planted = await client.GetAsync(
            $"{ExternalSignInTestStartupFilter.Path}?email=admin@mcpmanager.local&emailVerified=absent",
            ct
        );
        planted.EnsureSuccessStatusCode();

        var callback = await client.GetAsync("/Auth/ExternalLoginCallback", ct);

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().ContainEquivalentOf("/home");

        var protectedPage = await client.GetAsync("/Home", ct);
        protectedPage.StatusCode.Should().Be(HttpStatusCode.OK);
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

/// <summary>
/// SSO-only mode (<c>Oidc__RequireSso=true</c>): the email/password form is removed from
/// the login page and password sign-in is refused server-side, while SSO remains
/// available. Agent API key authentication is on a separate scheme and is unaffected.
/// </summary>
public class OidcRequireSsoAuthControllerTests : IClassFixture<OidcRequireSsoWebFactoryFixture>
{
    private readonly OidcRequireSsoWebFactoryFixture _factory;

    public OidcRequireSsoAuthControllerTests(OidcRequireSsoWebFactoryFixture factory) =>
        _factory = factory;

    [Fact]
    public async Task LoginPage_WhenRequireSso_HidesPasswordFormAndShowsSso()
    {
        var client = _factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var response = await client.GetAsync("/Auth/Login", ct);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(ct);

        var document = await BrowsingContext
            .New(Configuration.Default)
            .OpenAsync(req => req.Content(html), ct);

        // The password form and its fields must not render in SSO-only mode.
        document.QuerySelector("form#loginForm").Should().BeNull();
        document.QuerySelector("input[name='Password']").Should().BeNull();

        // The SSO button is still offered.
        html.Should().Contain($"Sign in with {OidcWebFactoryFixture.DisplayName}");
        html.Should().Contain("/auth/externallogin");
    }

    [Fact]
    public async Task PasswordLogin_WhenRequireSso_IsRefusedAndDoesNotSignIn()
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            }
        );
        var ct = TestContext.Current.CancellationToken;

        // The SSO-only login page still emits an antiforgery token, so a same-origin POST
        // reaches the controller guard rather than being stopped only by antiforgery.
        var getResponse = await client.GetAsync("/Auth/Login", ct);
        getResponse.EnsureSuccessStatusCode();
        var html = await getResponse.Content.ReadAsStringAsync(ct);
        var document = await BrowsingContext
            .New(Configuration.Default)
            .OpenAsync(req => req.Content(html), ct);
        var token = document
            .QuerySelector("input[name='AntiForgery']")!
            .GetAttribute("value")!;

        var form = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["AntiForgery"] = token,
                ["Email"] = "admin@mcpmanager.local",
                ["Password"] = "123456",
            }
        );

        var post = await client.PostAsync("/Auth/Login", form, ct);

        // The guard re-renders the login page (200) rather than signing in / redirecting home.
        post.StatusCode.Should().Be(HttpStatusCode.OK);

        // The session is not authenticated: a protected page bounces back to login.
        var protectedPage = await client.GetAsync("/Home", ct);
        protectedPage.StatusCode.Should().Be(HttpStatusCode.Redirect);
        protectedPage.Headers.Location!.ToString().Should().ContainEquivalentOf("/auth/login");
    }
}

/// <summary>
/// Strict mode (<c>Oidc__RequireVerifiedEmail=true</c>): an SSO identity is only matched
/// when the provider asserts <c>email_verified=true</c>. An absent or explicitly-false
/// claim is rejected. For untrusted/multi-tenant providers where an unverified email
/// could be used to match an existing local account.
/// </summary>
public class OidcRequireVerifiedEmailAuthControllerTests
    : IClassFixture<OidcRequireVerifiedEmailWebFactoryFixture>
{
    private readonly OidcRequireVerifiedEmailWebFactoryFixture _factory;

    public OidcRequireVerifiedEmailAuthControllerTests(
        OidcRequireVerifiedEmailWebFactoryFixture factory
    ) => _factory = factory;

    private static WebApplicationFactoryClientOptions NoRedirect() =>
        new() { AllowAutoRedirect = false, HandleCookies = true };

    [Theory]
    [InlineData("false")]
    [InlineData("absent")]
    public async Task Callback_WhenStrictAndEmailNotVerified_RedirectsToLoginWithError(
        string emailVerified
    )
    {
        var client = _factory.CreateClient(NoRedirect());
        var ct = TestContext.Current.CancellationToken;

        await client.GetAsync(
            $"{ExternalSignInTestStartupFilter.Path}?email=admin@mcpmanager.local&emailVerified={emailVerified}",
            ct
        );

        var callback = await client.GetAsync("/Auth/ExternalLoginCallback", ct);

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().ContainEquivalentOf("/auth/login");
        callback.Headers.Location!.ToString().Should().Contain("error");

        var protectedPage = await client.GetAsync("/Home", ct);
        protectedPage.StatusCode.Should().Be(HttpStatusCode.Redirect);
        protectedPage.Headers.Location!.ToString().Should().ContainEquivalentOf("/auth/login");
    }

    [Fact]
    public async Task Callback_WhenStrictAndEmailVerified_SignsIn()
    {
        var client = _factory.CreateClient(NoRedirect());
        var ct = TestContext.Current.CancellationToken;

        var planted = await client.GetAsync(
            $"{ExternalSignInTestStartupFilter.Path}?email=admin@mcpmanager.local&emailVerified=true",
            ct
        );
        planted.EnsureSuccessStatusCode();

        var callback = await client.GetAsync("/Auth/ExternalLoginCallback", ct);

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().ContainEquivalentOf("/home");

        var protectedPage = await client.GetAsync("/Home", ct);
        protectedPage.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
