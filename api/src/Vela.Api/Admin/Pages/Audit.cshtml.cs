using Microsoft.AspNetCore.Mvc;
using Vela.Api.Common;

namespace Vela.Api.Admin.Pages;

public sealed class AuditModel(IModerationService moderation) : AdminPageModel
{
    public const int PageSize = 50;
    public IReadOnlyList<AuditItem> Items { get; private set; } = [];
    public string? NextCursor { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? cursor)
    {
        try
        {
            var page = await moderation.AuditAsync(cursor, PageSize);
            Items = page.Items;
            NextCursor = page.NextCursor;
        }
        catch (InvalidCursorException) { return RedirectToPage(); }
        return Page();
    }
}
