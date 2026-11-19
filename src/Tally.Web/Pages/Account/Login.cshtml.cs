using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Tally.Web.Api;
using Tally.Web.Application;
using Tally.Web.Infrastructure.Auth;

namespace Tally.Web.Pages.Account;

[EnableRateLimiting(AuthEndpoints.LoginRateLimit)]
public sealed class LoginModel(AuthService auth) : PageModel
{
    [BindProperty]
    public LoginRequest Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await auth.VerifyCredentialsAsync(Input.Email, Input.Password, ct);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return Page();
        }

        var identity = new ClaimsIdentity(
            TallyClaimTypes.For(result.Value),
            CookieAuthenticationDefaults.AuthenticationScheme,
            TallyClaimTypes.Name,
            TallyClaimTypes.Role);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        // Only redirect within this site, never to an attacker-supplied URL.
        return Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : RedirectToPage("/Claims/Index");
    }
}
