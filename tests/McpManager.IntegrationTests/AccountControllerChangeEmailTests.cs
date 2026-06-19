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

public class AccountControllerChangeEmailTests : IClassFixture<WebFactoryFixture>
{
    private readonly WebFactoryFixture _factory;

    public AccountControllerChangeEmailTests(WebFactoryFixture factory) => _factory = factory;

    private static HttpClient NewClient(WebFactoryFixture factory) =>
        factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            }
        );

    private static async Task<string> AntiForgeryTokenAsync(
        HttpClient client,
        string url,
        CancellationToken ct
    )
    {
        var resp = await client.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var html = await resp.Content.ReadAsStringAsync(ct);
        var document = await BrowsingContext
            .New(Configuration.Default)
            .OpenAsync(req => req.Content(html), ct);
        return document.QuerySelector("form input[name='AntiForgery']")!.GetAttribute("value")!;
    }

    [Fact]
    public async Task GetChangeEmail_AsAuthenticatedAdmin_PrefillsCurrentEmail()
    {
        var client = NewClient(_factory);
        var ct = TestContext.Current.CancellationToken;
        await _factory.SignInAsAdminAsync(client, ct);

        var response = await client.GetAsync("/Account/ChangeEmail", ct);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct);
        var document = await BrowsingContext
            .New(Configuration.Default)
            .OpenAsync(req => req.Content(html), ct);

        var input = document.QuerySelector("input[name='NewEmail']");
        input.Should().NotBeNull();
        input!.GetAttribute("value").Should().Be("admin@mcpmanager.local");
    }

    [Fact]
    public async Task PostChangeEmail_WithNewEmail_UpdatesEmailAndUserName()
    {
        var client = NewClient(_factory);
        var ct = TestContext.Current.CancellationToken;
        await _factory.SignInAsAdminAsync(client, ct);

        const string newEmail = "admin-renamed@mcpmanager.local";
        var token = await AntiForgeryTokenAsync(client, "/Account/ChangeEmail", ct);

        try
        {
            var form = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["AntiForgery"] = token,
                    ["NewEmail"] = newEmail,
                }
            );

            var response = await client.PostAsync("/Account/ChangeEmail", form, ct);
            response.StatusCode.Should().Be(HttpStatusCode.Found);

            using var verify = _factory.Services.CreateScope();
            var users = verify.ServiceProvider.GetRequiredService<UserManager<User>>();
            var updated = await users.FindByEmailAsync(newEmail);
            updated.Should().NotBeNull("the email change must persist");
            // The portal authenticates by username, so it must track the new email.
            updated!.UserName.Should().Be(newEmail);
            updated.EmailConfirmed.Should().BeTrue("sign-in requires a confirmed email");
        }
        finally
        {
            // Restore the seeded admin so sibling tests' SignInAsAdminAsync keeps working.
            using var restore = _factory.Services.CreateScope();
            var users = restore.ServiceProvider.GetRequiredService<UserManager<User>>();
            var admin = await users.FindByEmailAsync(newEmail) ?? await users.FindByIdAsync(
                IntToGuid(1).ToString()
            );
            if (admin != null)
            {
                admin.Email = "admin@mcpmanager.local";
                admin.UserName = "admin@mcpmanager.local";
                await users.UpdateAsync(admin);
            }
        }
    }

    [Fact]
    public async Task PostChangeEmail_WithEmailTakenByAnotherUser_ReRendersWithError()
    {
        var ct = TestContext.Current.CancellationToken;
        const string otherEmail = "taken@mcpmanager.local";
        await CreateUserAsync(otherEmail);

        var client = NewClient(_factory);
        await _factory.SignInAsAdminAsync(client, ct);
        var token = await AntiForgeryTokenAsync(client, "/Account/ChangeEmail", ct);

        var form = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["AntiForgery"] = token,
                ["NewEmail"] = otherEmail,
            }
        );

        var response = await client.PostAsync("/Account/ChangeEmail", form, ct);

        // A conflicting email must re-render the form (200) rather than redirect.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(ct);
        body.Should().Contain("A user with this email already exists.");

        // The admin's own email must be untouched.
        using var verify = _factory.Services.CreateScope();
        var users = verify.ServiceProvider.GetRequiredService<UserManager<User>>();
        var admin = await users.FindByEmailAsync("admin@mcpmanager.local");
        admin.Should().NotBeNull("the admin email must not have changed");
    }

    private async Task CreateUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        if (await users.FindByEmailAsync(email) != null)
        {
            return;
        }

        var user = new User
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            IsActive = true,
            GivenName = "Taken",
            Surname = "User",
        };
        (await users.CreateAsync(user, "123456")).Succeeded.Should().BeTrue();
    }

    private static Guid IntToGuid(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }
}
