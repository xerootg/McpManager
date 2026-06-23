using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace McpManager.IntegrationTests.Fixtures;

/// <summary>
/// A web factory with OIDC single sign-on configured via the "Oidc" settings so the
/// SSO code paths (button rendering, the external-login callback) are exercised. The
/// authority is a dummy URL: the OpenID Connect handler retrieves metadata lazily on
/// the first challenge, so building the host, rendering the login page, and replaying
/// the callback (whose external cookie is planted by
/// <see cref="ExternalSignInTestStartupFilter"/>) never touch the network.
/// </summary>
public class OidcWebFactoryFixture
    : WebApplicationFactory<McpManager.Web.Portal.Program>,
        IAsyncLifetime
{
    public const string DisplayName = "Authentik";

    private string _dbPath;

    /// <summary>Whether the configured app should auto-provision unknown SSO emails.</summary>
    protected virtual bool AutoProvision => false;

    /// <summary>Whether the configured app should require SSO and disable password login.</summary>
    protected virtual bool RequireSso => false;

    /// <summary>Whether the configured app should require a provider-verified email.</summary>
    protected virtual bool RequireVerifiedEmail => false;

    public ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"mcpmanager-oidc-tests-{Guid.NewGuid():N}.db");
        _ = Services;
        return ValueTask.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (!string.IsNullOrEmpty(_dbPath) && File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:ApplicationConnection", $"Data Source={_dbPath}");

        builder.UseSetting(
            "Oidc:Authority",
            "https://authentik.example.com/application/o/mcpmanager/"
        );
        builder.UseSetting("Oidc:ClientId", "test-client-id");
        builder.UseSetting("Oidc:ClientSecret", "test-client-secret");
        builder.UseSetting("Oidc:DisplayName", DisplayName);
        builder.UseSetting("Oidc:RequireHttpsMetadata", "false");
        builder.UseSetting("Oidc:AutoProvision", AutoProvision ? "true" : "false");
        builder.UseSetting("Oidc:RequireSso", RequireSso ? "true" : "false");
        builder.UseSetting("Oidc:RequireVerifiedEmail", RequireVerifiedEmail ? "true" : "false");

        builder.ConfigureServices(services =>
        {
            services.AddFakeLogging();
            services.AddSingleton<IStartupFilter, ExternalSignInTestStartupFilter>();
        });
    }
}

/// <summary>OIDC web factory variant that auto-provisions unknown SSO emails.</summary>
public class OidcAutoProvisionWebFactoryFixture : OidcWebFactoryFixture
{
    protected override bool AutoProvision => true;
}

/// <summary>OIDC web factory variant that requires SSO and disables password login.</summary>
public class OidcRequireSsoWebFactoryFixture : OidcWebFactoryFixture
{
    protected override bool RequireSso => true;
}

/// <summary>OIDC web factory variant that requires a provider-verified email.</summary>
public class OidcRequireVerifiedEmailWebFactoryFixture : OidcWebFactoryFixture
{
    protected override bool RequireVerifiedEmail => true;
}
