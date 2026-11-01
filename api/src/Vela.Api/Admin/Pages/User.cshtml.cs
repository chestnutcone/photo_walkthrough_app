using Microsoft.AspNetCore.Mvc;

namespace Vela.Api.Admin.Pages;

public sealed class UserModel(IModerationService moderation, IDmcaAdminService dmca) : AdminPageModel
{
    public UserDetail Detail { get; private set; } = null!;
    public int DmcaTakedowns { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (await moderation.GetUserAsync(id) is not { } detail) return NotFound();
        Detail = detail;
        DmcaTakedowns = await dmca.TakedownCountAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostBanAsync(Guid id, string? reason) =>
        Done(id, await moderation.SuspendUserAsync(AdminId, id, reason ?? ""));

    public async Task<IActionResult> OnPostReinstateAsync(Guid id, string? reason) =>
        Done(id, await moderation.ReinstateUserAsync(AdminId, id, reason ?? ""));

    private IActionResult Done(Guid id, ModerationOutcome outcome) => RedirectToPage(new { id, flash = FlashCode(outcome) });
}
