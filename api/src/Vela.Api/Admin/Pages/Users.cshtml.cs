using Microsoft.AspNetCore.Mvc;
using Vela.Api.Common;

namespace Vela.Api.Admin.Pages;

public sealed class UsersModel(IModerationService moderation) : AdminPageModel
{
    public const int PageSize = 25;
    public string Query { get; private set; } = "";
    public IReadOnlyList<AdminUserItem> Items { get; private set; } = [];
    public string? NextCursor { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? q, string? cursor)
    {
        Query = (q ?? "").Trim();
        try
        {
            var page = await moderation.FindUsersAsync(Query, cursor, PageSize);
            Items = page.Items;
            NextCursor = page.NextCursor;
        }
        catch (InvalidCursorException) { return RedirectToPage(new { q = Query }); }
        return Page();
    }
}
