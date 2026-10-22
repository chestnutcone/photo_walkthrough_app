using Microsoft.AspNetCore.Mvc;
using Vela.Api.Common;
using Vela.Api.Images;

namespace Vela.Api.Admin.Pages;

public sealed class IndexModel(IModerationService moderation, IBlobStore blobs, TimeProvider clock) : AdminPageModel
{
    public const int PageSize = 25;
    public static readonly TimeSpan ImageLifetime = TimeSpan.FromMinutes(5);
    public static readonly string[] Sources = ["user_report", "auto_flag", "dmca"];
    public static readonly string[] Reasons = ["illegal_content", "copyright", "harassment", "spam", "other", "auto"];
    public static readonly string[] Statuses = ["pending", "reviewed", "actioned", "dismissed", "all"];

    public string StatusFilter { get; private set; } = "pending";
    public string? ReasonFilter { get; private set; }
    public string? SourceFilter { get; private set; }
    public ModerationCounts Counts { get; private set; } = new(0, 0, 0, 0, null);
    public IReadOnlyList<ReportItem> Items { get; private set; } = [];
    public string? NextCursor { get; private set; }
    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task<IActionResult> OnGetAsync(string? status, string? reason, string? source, string? cursor)
    {
        Normalize(status, reason, source);
        Counts = await moderation.CountsAsync();
        try
        {
            var page = await moderation.QueueAsync(ParseStatus(StatusFilter), ReasonFilter, SourceFilter, cursor, PageSize);
            Items = page.Items;
            NextCursor = page.NextCursor;
        }
        catch (InvalidCursorException) { return RedirectToPage(new { status = StatusFilter, reason = ReasonFilter, source = SourceFilter }); }
        return Page();
    }

    /// <summary>A short-lived read link to the private original, made per page view. Never logged.</summary>
    public string? ThumbnailUrl(ReportItem item) =>
        item.TargetType == "work" ? blobs.CreateReadUrl(BlobNames.WorkOriginal(item.TargetId), ImageLifetime).ToString() : null;

    public async Task<IActionResult> OnPostDismissAsync(Guid reportId, string? status, string? reason, string? source)
    {
        var outcome = await moderation.DismissReportAsync(AdminId, reportId);
        return Back(outcome, status, reason, source);
    }

    public async Task<IActionResult> OnPostRestoreAsync(Guid reportId, string? actionReason, string? status, string? reason, string? source)
    {
        var outcome = await moderation.RestoreAsync(AdminId, reportId, actionReason ?? "");
        return Back(outcome, status, reason, source);
    }

    private IActionResult Back(ModerationOutcome outcome, string? status, string? reason, string? source)
    {
        Normalize(status, reason, source);
        return RedirectToPage(new { status = StatusFilter, reason = ReasonFilter, source = SourceFilter, flash = FlashCode(outcome) });
    }

    private void Normalize(string? status, string? reason, string? source)
    {
        StatusFilter = status is not null && Statuses.Contains(status) ? status : "pending";
        ReasonFilter = reason is not null && Reasons.Contains(reason) ? reason : null;
        SourceFilter = source is not null && Sources.Contains(source) ? source : null;
    }

    private static ReportStatus? ParseStatus(string status) =>
        Enum.TryParse<ReportStatus>(status, true, out var s) ? s : null;
}
