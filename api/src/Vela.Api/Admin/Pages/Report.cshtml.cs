using Microsoft.AspNetCore.Mvc;
using Vela.Api.Images;

namespace Vela.Api.Admin.Pages;

public sealed class ReportModel(IModerationService moderation, IBlobStore blobs, TimeProvider clock) : AdminPageModel
{
    public ReportDetail Detail { get; private set; } = null!;
    public string? ImageUrl { get; private set; }
    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (await moderation.GetReportAsync(id) is not { } detail) return NotFound();
        Detail = detail;
        // The private original, one link per page view, valid five minutes, never logged.
        if (detail.Item.TargetType == "work")
            ImageUrl = blobs.CreateReadUrl(BlobNames.WorkOriginal(detail.Item.TargetId), IndexModel.ImageLifetime).ToString();
        return Page();
    }

    public async Task<IActionResult> OnPostRestoreAsync(Guid id, string? reason) =>
        Done(id, await moderation.RestoreAsync(AdminId, id, reason ?? ""));

    public async Task<IActionResult> OnPostDismissAsync(Guid id, string? reason) =>
        Done(id, await moderation.DismissReportAsync(AdminId, id, reason));

    public async Task<IActionResult> OnPostHideReplyAsync(Guid id, string? reason) =>
        await Target(id, "reply", (admin, target) => moderation.HideReplyAsync(admin, target, reason ?? ""));

    public async Task<IActionResult> OnPostRemoveWorkAsync(Guid id, string? reason) =>
        await Target(id, "work", (admin, target) => moderation.RemoveWorkAsync(admin, target, reason ?? ""));

    /// <summary>The account to ban comes from the stored report, never from the form.</summary>
    public async Task<IActionResult> OnPostBanOwnerAsync(Guid id, string? reason)
    {
        if (await moderation.GetReportAsync(id) is not { OwnerId: { } owner }) return Done(id, ModerationOutcome.NotFound);
        return Done(id, await moderation.SuspendUserAsync(AdminId, owner, reason ?? ""));
    }

    private async Task<IActionResult> Target(Guid reportId, string type, Func<Guid, Guid, Task<ModerationOutcome>> act)
    {
        if (await moderation.GetReportAsync(reportId) is not { } d || d.Item.TargetType != type)
            return Done(reportId, ModerationOutcome.NotApplicable);
        return Done(reportId, await act(AdminId, d.Item.TargetId));
    }

    private IActionResult Done(Guid id, ModerationOutcome outcome) => RedirectToPage(new { id, flash = FlashCode(outcome) });
}
