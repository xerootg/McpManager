namespace McpManager.Web.Portal.Authentication;

/// <summary>
/// OpenID Connect single sign-on configuration, bound from the "Oidc" configuration
/// section (e.g. environment variables prefixed with <c>Oidc__</c>). When the required
/// values are not present OIDC is left unregistered and the app falls back to
/// password-only login.
/// </summary>
public class OidcOptions
{
    public const string SectionName = "Oidc";
    public const string SchemeName = "oidc";

    /// <summary>OIDC authority / issuer URL. For Authentik this is the application
    /// provider URL, e.g. <c>https://authentik.example.com/application/o/mcpmanager/</c>.</summary>
    public string Authority { get; set; }

    public string ClientId { get; set; }

    public string ClientSecret { get; set; }

    /// <summary>Space-separated scopes requested from the provider.</summary>
    public string Scope { get; set; } = "openid profile email";

    /// <summary>Path the provider redirects back to. Must be registered as a redirect
    /// URI on the provider as <c>{app-origin}{CallbackPath}</c>.</summary>
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>Label shown on the sign-in button, e.g. "Authentik".</summary>
    public string DisplayName { get; set; } = "Single Sign-On";

    /// <summary>When true, a successful SSO login with no matching local account
    /// creates one (matched later by email). The new account is created active with
    /// no permission claims until an admin grants them. When false, only existing
    /// accounts (matched by email) may sign in via SSO.</summary>
    public bool AutoProvision { get; set; }

    /// <summary>Require HTTPS for the provider metadata endpoint. Leave enabled in
    /// production; only disable for local providers served over plain HTTP.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>True when the minimum required values for the authorization-code flow
    /// are present, in which case the OIDC handler is registered.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);
}
