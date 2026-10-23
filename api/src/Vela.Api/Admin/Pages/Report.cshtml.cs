using Microsoft.AspNetCore.Mvc;
using Vela.Api.Images;

namespace Vela.Api.Admin.Pages;

public sealed class ReportModel(IModerationService moderation, IDmcaAdminService dmca, IBlobStore blobs, TimeProvider clock) : AdminPageModel
{
    public ReportDetail Detail { get; private set; } = null!;
    public string? ImageUrl { get; private set; }
    public IReadOnlyList<DmcaNoticeView> Notices { get; private set; } = [];
    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (await moderation.GetReportAsync(id) is not { } detail) return NotFound();
        Detail = detail;
        // The private original, one link per page view, valid five minutes, never logged.
        if (detail.Item.TargetType == "work")
            ImageUrl = blobs.CreateReadUrl(BlobNames.WorkOriginal(detail.Item.TargetId), IndexModel.ImageLifetime).ToString();
        if (detail.Item.Source == "dmca" && detail.Item.TargetType == "work") Notices = await dmca.NoticesForWorkAsync(detail.Item.TargetId);
        return Page();
    }

    public Task<IActionResult> OnPostDmcaTakedownAsync(Guid id, Guid noticeId) =>
        DmcaAsync(id, noticeId, () => dmca.TakeDownAsync(AdminId, noticeId));

    public Task<IActionResult> OnPostDmcaRejectAsync(Guid id, Guid noticeId, string? reason) =>
        DmcaAsync(id, noticeId, () => dmca.RejectAsync(AdminId, noticeId, reason));

    public Task<IActionResult> OnPostDmcaForwardAsync(Guid id, Guid noticeId) =>
        DmcaAsync(id, noticeId, () => dmca.ForwardAsync(AdminId, noticeId));

    public Task<IActionResult> OnPostDmcaRestoreAsync(Guid id, Guid noticeId, string? withdrew, string? reason) =>
        DmcaAsync(id, noticeId, () => dmca.RestoreAsync(AdminId, noticeId, withdrew is not null, reason));

    /// <summary>The notice must be about the work this report is about, whatever the form says.</summary>
    private async Task<IActionResult> DmcaAsync(Guid reportId, Guid noticeId, Func<Task<ModerationOutcome>> act)
    {
        if (await moderation.GetReportAsync(reportId) is not { } d || d.Item.TargetType != "work" || await dmca.WorkOfAsync(noticeId) != d.Item.TargetId)
            return Done(reportId, ModerationOutcome.NotApplicable);
        return Done(reportId, await act());
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

    /// <summary>Holds the evidence, takes the content down and bans the owner. Needs the confirmation box.</summary>
    public async Task<IActionResult> OnPostMarkIllegalAsync(Guid id, string? reason, string? confirm)
    {
        if (confirm is null) return Done(id, "confirmation_required");
        var result = await moderation.MarkIllegalAsync(AdminId, id, reason ?? "");
        if (result.Outcome != ModerationOutcome.Ok || result.HoldId is not { } hold) return Done(id, FlashCode(result.Outcome));
        return RedirectToPage("/EvidenceDetail", new { id = hold, flash = result.BanRefused ? "ban_refused" : "ok" });
    }

    private IActionResult Done(Guid id, string flash) => RedirectToPage(new { id, flash });

    private async Task<IActionResult> Target(Guid reportId, string type, Func<Guid, Guid, Task<ModerationOutcome>> act)
    {
        if (await moderation.GetReportAsync(reportId) is not { } d || d.Item.TargetType != type)
            return Done(reportId, ModerationOutcome.NotApplicable);
        return Done(reportId, await act(AdminId, d.Item.TargetId));
    }

    private IActionResult Done(Guid id, ModerationOutcome outcome) => RedirectToPage(new { id, flash = FlashCode(outcome) });
}
