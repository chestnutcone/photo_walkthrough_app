using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vela.Api.Common;

namespace Vela.Api.Admin.Pages;

public sealed class LoginModel(AdminLoginService login, IClientIpResolver ips, TimeProvider clock) : PageModel
{
    public string Step { get; private set; } = "email";
    public bool Failed { get; private set; }
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? Code { get; set; }

    public void OnGet() { }

    /// <summary>The response is identical whether or not the address is on the allow-list.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        await login.RequestCodeAsync(Email, ips.Resolve(HttpContext).ToString());
        Step = "code";
        return Page();
    }

    public async Task<IActionResult> OnPostVerifyAsync()
    {
        var who = await login.VerifyAsync(Email, Code, ips.Resolve(HttpContext).ToString());
        if (who is null)
        {
            Step = "code";
            Failed = true;
            return Page();
        }
        await HttpContext.SignInAsync(AdminAuth.Scheme, AdminAuth.CreatePrincipal(who.UserId, who.Email),
            AdminAuth.SessionProperties(clock.GetUtcNow()));
        return Redirect("/admin");
    }
}
