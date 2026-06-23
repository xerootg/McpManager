using System.Security.Claims;
using McpManager.Core.Data.Models.Identity;
using McpManager.Web.Portal.Authentication;
using McpManager.Web.Portal.Controllers.Abstract;
using McpManager.Web.Portal.Dtos.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace McpManager.Web.Portal.Controllers;

[AllowAnonymous]
public class AuthController : BaseController
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly OidcOptions _oidcOptions;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        SignInManager<User> signInManager,
        UserManager<User> userManager,
        OidcOptions oidcOptions,
        ILogger<AuthController> logger
    )
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _oidcOptions = oidcOptions;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Login(string returnUrl, string error)
    {
        await _signInManager.SignOutAsync();
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["Error"] = error;
        ViewData["OidcEnabled"] = _oidcOptions.IsConfigured;
        ViewData["OidcDisplayName"] = _oidcOptions.DisplayName;
        ViewData["PasswordLoginDisabled"] = _oidcOptions.PasswordLoginDisabled;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginDto loginDto)
    {
        ViewData["OidcEnabled"] = _oidcOptions.IsConfigured;
        ViewData["OidcDisplayName"] = _oidcOptions.DisplayName;
        ViewData["PasswordLoginDisabled"] = _oidcOptions.PasswordLoginDisabled;

        // When SSO is required, password sign-in is refused server-side as well so the
        // policy holds even if a request reaches this endpoint directly. Agent API keys
        // authenticate on a separate scheme and are unaffected.
        if (_oidcOptions.PasswordLoginDisabled)
        {
            ViewData["InvalidLogin"] = "Password sign-in is disabled. Please sign in with SSO.";
            return View(new LoginDto());
        }

        if (!ModelState.IsValid)
        {
            return View(loginDto);
        }

        var user = await _userManager.FindByNameAsync(loginDto.Email);
        if (user?.IsActive ?? false)
        {
            var result = await _signInManager.PasswordSignInAsync(
                loginDto.Email,
                loginDto.Password,
                true,
                false
            );

            if (result.Succeeded)
            {
                return RedirectToAction("Index", "Home");
            }
        }
        ViewData["InvalidLogin"] = "Invalid credentials or inactive account.";
        return View(loginDto);
    }

    [HttpGet]
    public IActionResult ExternalLogin(string returnUrl = null)
    {
        if (!_oidcOptions.IsConfigured)
        {
            return RedirectToAction(nameof(Login));
        }

        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Auth", new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(
            OidcOptions.SchemeName,
            redirectUrl
        );
        return Challenge(properties, OidcOptions.SchemeName);
    }

    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(
        string returnUrl = null,
        string remoteError = null
    )
    {
        if (!_oidcOptions.IsConfigured)
        {
            return RedirectToAction(nameof(Login));
        }

        if (remoteError != null)
        {
            _logger.LogWarning("SSO sign-in returned a remote error: {RemoteError}", remoteError);
            return RedirectToAction(nameof(Login), new { error = "Sign-in with SSO failed." });
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            return RedirectToAction(
                nameof(Login),
                new { error = "Could not read the SSO sign-in response. Please try again." }
            );
        }

        // Match strictly on the email claim. preferred_username is intentionally NOT
        // used as a fallback: it is frequently user-settable and not guaranteed to be a
        // verified email, so matching on it would let an attacker target a known local
        // address (e.g. the admin) by choosing their username at the provider.
        var email =
            info.Principal.FindFirstValue(ClaimTypes.Email)
            ?? info.Principal.FindFirstValue("email");

        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToAction(
                nameof(Login),
                new { error = "Your SSO account did not provide an email address." }
            );
        }

        // Optionally require a provider-verified email before matching to a local account
        // (the account-takeover vector from the security review). Off by default: some
        // providers — e.g. Authentik without an email-verification flow — report
        // email_verified=false (or omit it) for perfectly legitimate accounts, so gating
        // on it locks real users out. Enable Oidc__RequireVerifiedEmail only for providers
        // where an unverified email could let an attacker match an existing local account.
        // JSON booleans surface as "true"/"True" depending on the claim source, so compare
        // case-insensitively; absent or anything other than true is treated as unverified.
        if (_oidcOptions.RequireVerifiedEmail)
        {
            var emailVerified = info.Principal.FindFirstValue("email_verified");
            if (!string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "SSO sign-in for {Email} rejected: Oidc__RequireVerifiedEmail is set and the provider did not assert email_verified=true.",
                    email
                );
                return RedirectToAction(
                    nameof(Login),
                    new { error = "Your SSO email address has not been verified by the provider." }
                );
            }
        }

        var user = await _userManager.FindByEmailAsync(email);

        if (user == null && _oidcOptions.AutoProvision)
        {
            user = new User
            {
                Email = email,
                UserName = email,
                EmailConfirmed = true,
                IsActive = true,
                GivenName = info.Principal.FindFirstValue(ClaimTypes.GivenName) ?? string.Empty,
                Surname = info.Principal.FindFirstValue(ClaimTypes.Surname) ?? string.Empty,
            };

            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                _logger.LogError(
                    "Failed to auto-provision SSO user {Email}: {Errors}",
                    email,
                    string.Join("; ", createResult.Errors.Select(e => e.Description))
                );
                return RedirectToAction(
                    nameof(Login),
                    new { error = "Could not create an account for your SSO identity." }
                );
            }

            _logger.LogInformation("Auto-provisioned new user {Email} from SSO sign-in.", email);
        }

        if (user == null)
        {
            _logger.LogWarning("SSO sign-in for {Email} has no matching local account.", email);
            return RedirectToAction(
                nameof(Login),
                new { error = "No account is associated with your SSO email address." }
            );
        }

        if (!user.IsActive)
        {
            return RedirectToAction(nameof(Login), new { error = "Your account is inactive." });
        }

        // Complete the login with the application cookie and drop the temporary
        // external cookie used during the OIDC round-trip.
        await _signInManager.SignInAsync(user, isPersistent: true);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return SignOut(
            new AuthenticationProperties() { RedirectUri = Url.Action("Index", "Home") }
        );
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        ViewData["Title"] = "Access denied";
        ViewData["Text"] = "You do not have access to the requested page.";
        return View();
    }

    [HttpGet]
    public IActionResult LockedOut()
    {
        ViewData["Title"] = "Account locked";
        ViewData["Text"] = "Your account is locked.";
        return View();
    }
}
