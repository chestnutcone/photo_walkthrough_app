using Microsoft.AspNetCore.Mvc;
using Vela.Api.Common;

namespace Vela.Api.Admin.Pages;

public sealed class EvidenceModel(IEvidenceService evidence) : AdminPageModel
{
    public const int PageSize = 25;
    public IReadOnlyList<EvidenceListItem> Items { get; private set; } = [];
    public string? NextCursor { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? cursor)
    {
        try
        {
            var page = await evidence.ListAsync(cursor, PageSize);
            Items = page.Items;
            NextCursor = page.NextCursor;
        }
        catch (InvalidCursorException) { return RedirectToPage(); }
        return Page();
    }
}
