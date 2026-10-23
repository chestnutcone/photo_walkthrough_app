using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Common;
using Vela.Api.Data.Entities;

namespace Vela.Api.Admin;

public sealed partial class EvidenceService
{
    private const int MaxPage = 100;

    public async Task<PagedResult<EvidenceListItem>> ListAsync(string? cursor, int limit)
    {
        limit = Math.Clamp(limit, 1, MaxPage);
        var q = db.EvidenceHolds.AsNoTracking().AsQueryable();
        if (cursor is not null)
        {
            var (at, id) = Cursor.Decode(cursor);
            q = q.Where(h => h.CreatedAt < at || (h.CreatedAt == at && h.Id.CompareTo(id) < 0));
        }
        var rows = await q.OrderByDescending(h => h.CreatedAt).ThenByDescending(h => h.Id).Take(limit + 1).ToListAsync();
        var page = rows.Take(limit).Select(ToItem).ToList();
        return new PagedResult<EvidenceListItem>(page, rows.Count > limit ? Cursor.Encode(page[^1].CreatedAt, page[^1].Id) : null);
    }

    public async Task<EvidenceDetail?> GetAsync(Guid holdId)
    {
        var hold = await db.EvidenceHolds.AsNoTracking().SingleOrDefaultAsync(h => h.Id == holdId);
        return hold is null ? null : new EvidenceDetail(ToItem(hold), hold.UserId, hold.WorkId, hold.ReplyId, hold.Reason, hold.CreatedByAdminId, Parse(hold.Snapshot));
    }

    private static EvidenceListItem ToItem(EvidenceHold h) =>
        new(h.Id, h.CreatedAt, h.TargetType, Parse(h.Snapshot)?.Owner.Handle, h.HoldUntil, h.NcmecReportId, h.NcmecSubmittedAt);

    private static EvidenceSnapshot? Parse(string json)
    {
        try { return JsonSerializer.Deserialize<EvidenceSnapshot>(json, Json); }
        catch (JsonException) { return null; }
    }
}
