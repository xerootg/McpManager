using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace McpManager.IntegrationTests.Fixtures;

/// <summary>
/// Test-only middleware that mints the temporary external-auth cookie the OIDC handler
/// would normally write after a provider round-trip. A GET to
/// <c>/__test/external-signin?email=...</c> signs the supplied identity into
/// <see cref="IdentityConstants.ExternalScheme"/> exactly as the real handler does
/// (principal + a "LoginProvider" property item), so a follow-up request to
/// <c>/Auth/ExternalLoginCallback</c> exercises the production matching/sign-in code
/// without contacting an identity provider.
/// </summary>
public class ExternalSignInTestStartupFilter : IStartupFilter
{
    public const string Path = "/__test/external-signin";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(
                async (context, nextMiddleware) =>
                {
                    if (!context.Request.Path.Equals(Path, StringComparison.OrdinalIgnoreCase))
                    {
                        await nextMiddleware();
                        return;
                    }

                    var email = context.Request.Query["email"].ToString();
                    var claims = new List<Claim>
                    {
                        new(ClaimTypes.NameIdentifier, "test-provider-key"),
                    };
                    if (!string.IsNullOrEmpty(email))
                    {
                        claims.Add(new Claim(ClaimTypes.Email, email));
                    }

                    var identity = new ClaimsIdentity(claims, IdentityConstants.ExternalScheme);
                    var properties = new AuthenticationProperties();
                    properties.Items["LoginProvider"] = "oidc";

                    await context.SignInAsync(
                        IdentityConstants.ExternalScheme,
                        new ClaimsPrincipal(identity),
                        properties
                    );

                    context.Response.StatusCode = StatusCodes.Status200OK;
                }
            );

            next(app);
        };
    }
}
