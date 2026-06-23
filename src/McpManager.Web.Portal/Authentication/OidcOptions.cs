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

    /// <summary>When true (and SSO is configured), the email/password login form is
    /// disabled and interactive users must sign in via SSO. API key authentication for
    /// agents is unaffected. Ignored when SSO is not configured, so a misconfiguration
    /// cannot lock everyone out of the portal.</summary>
    public bool RequireSso { get; set; }

    /// <summary>When true, an SSO identity is only matched to a local account if the
    /// provider asserts <c>email_verified=true</c>. Off by default: some providers (e.g.
    /// Authentik without an email-verification flow) report <c>email_verified=false</c>
    /// — or omit the claim — for legitimate accounts, so gating on it locks real users
    /// out. Enable only for providers where an unverified email could let an attacker
    /// match an existing local account (e.g. multi-tenant IdPs with open sign-up).</summary>
    public bool RequireVerifiedEmail { get; set; }

    /// <summary>True when the minimum required values for the authorization-code flow
    /// are present, in which case the OIDC handler is registered.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>True when interactive password sign-in should be refused. Only takes
    /// effect once SSO is actually configured.</summary>
    public bool PasswordLoginDisabled => IsConfigured && RequireSso;
}
