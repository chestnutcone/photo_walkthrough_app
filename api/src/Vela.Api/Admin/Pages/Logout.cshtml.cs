using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Vela.Api.Admin.Pages;

public sealed class LogoutModel(AdminLoginService login) : PageModel
{
    public IActionResult OnGet() => Redirect("/admin");

    public async Task<IActionResult> OnPostAsync()
    {
        if (AdminAuth.UserId(User) is { } adminId) await login.SignOutAsync(adminId); // invalidate copies of the cookie, not just this browser's
        await HttpContext.SignOutAsync(AdminAuth.Scheme);
        return Redirect("/admin/login");
    }
}
