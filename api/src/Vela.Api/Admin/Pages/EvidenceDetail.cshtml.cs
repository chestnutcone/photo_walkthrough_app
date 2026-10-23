using Microsoft.AspNetCore.Mvc;
using Vela.Api.Images;

namespace Vela.Api.Admin.Pages;

public sealed class EvidenceDetailModel(IEvidenceService evidence, IBlobStore blobs, TimeProvider clock) : AdminPageModel
{
    public EvidenceDetail Detail { get; private set; } = null!;
    public string? ImageUrl { get; private set; }
    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (!await LoadAsync(id)) return NotFound();
        return Page();
    }

    /// <summary>The only caller of NCMEC submission: an explicit admin post with the confirmation box ticked and a reason.</summary>
    public async Task<IActionResult> OnPostSubmitNcmecAsync(Guid id, string? reason, string? confirm)
    {
        if (confirm is null) return RedirectToPage(new { id, flash = "confirmation_required" });
        var outcome = await evidence.SubmitToNcmecAsync(AdminId, id, reason ?? "", HttpContext.RequestAborted);
        if (outcome != ModerationOutcome.AlreadySubmitted) return RedirectToPage(new { id, flash = FlashCode(outcome) });
        if (!await LoadAsync(id)) return NotFound();
        ShowFlash(FlashCode(outcome));
        Response.StatusCode = StatusCodes.Status409Conflict;
        return Page();
    }

    private async Task<bool> LoadAsync(Guid id)
    {
        if (await evidence.GetAsync(id) is not { } detail) return false;
        Detail = detail;
        // One link per page view from the private evidence container, valid five minutes, never logged.
        if (detail.Snapshot is { OriginalCopied: true })
            ImageUrl = blobs.CreateReadUrl(BlobNames.EvidenceOriginal(id), IndexModel.ImageLifetime).ToString();
        return true;
    }
}
