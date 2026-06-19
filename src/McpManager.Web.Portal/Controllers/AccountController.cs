using McpManager.Core.Data.Models.Identity;
using McpManager.Web.Portal.Controllers.Abstract;
using McpManager.Web.Portal.Dtos.Account;
using McpManager.Web.Portal.Services.FlashMessage.Contracts;
using McpManager.Web.Portal.TagHelpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace McpManager.Web.Portal.Controllers;

public class AccountController : BaseController
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly IFlashMessage _flashMessage;

    public AccountController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IFlashMessage flashMessage
    )
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _flashMessage = flashMessage;
    }

    [HttpGet]
    public async Task<IActionResult> ChangePassword()
    {
        ViewData["Title"] = "Change Password";
        ViewData["Menu"] = "Account";
        ViewData["Icon"] = HeroIcons.Render("key", size: 5);

        var user = await GetAuthenticatedUser();
        ViewData["User"] = user;

        return View(new ChangePasswordDto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        ViewData["Title"] = "Change Password";
        ViewData["Menu"] = "Account";
        ViewData["Icon"] = HeroIcons.Render("key", size: 5);

        var user = await GetAuthenticatedUser();
        ViewData["User"] = user;

        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(nameof(dto.NewPassword), error.Description);
            }
            return View(dto);
        }

        _flashMessage.Success("Your password has been changed successfully.");
        return RedirectToAction(nameof(ChangePassword));
    }

    [HttpGet]
    public async Task<IActionResult> ChangeEmail()
    {
        ViewData["Title"] = "Change Email";
        ViewData["Menu"] = "Account";
        ViewData["Icon"] = HeroIcons.Render("identification", size: 5);

        var user = await GetAuthenticatedUser();
        ViewData["User"] = user;

        return View(new ChangeEmailDto { NewEmail = user.Email });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeEmail(ChangeEmailDto dto)
    {
        ViewData["Title"] = "Change Email";
        ViewData["Menu"] = "Account";
        ViewData["Icon"] = HeroIcons.Render("identification", size: 5);

        var user = await GetAuthenticatedUser();
        ViewData["User"] = user;

        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        if (string.Equals(user.Email, dto.NewEmail, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(
                nameof(dto.NewEmail),
                "This is already your email address."
            );
            return View(dto);
        }

        var existing = await _userManager.FindByEmailAsync(dto.NewEmail);
        if (existing != null && existing.Id != user.Id)
        {
            ModelState.AddModelError(nameof(dto.NewEmail), "A user with this email already exists.");
            return View(dto);
        }

        // The portal uses the email as the username, so keep them in sync. Email
        // stays confirmed: there is no email-verification flow, and sign-in
        // requires a confirmed email.
        user.Email = dto.NewEmail;
        user.UserName = dto.NewEmail;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(nameof(dto.NewEmail), error.Description);
            }
            return View(dto);
        }

        // Re-issue the auth cookie so its name/claims reflect the new email.
        await _signInManager.RefreshSignInAsync(user);

        _flashMessage.Success("Your email address has been updated.");
        return RedirectToAction(nameof(ChangeEmail));
    }
}
