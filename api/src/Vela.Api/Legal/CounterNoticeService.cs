using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Social;

namespace Vela.Api.Legal;

public sealed record CounterNoticeRequest(string Name, string Address, string? Phone, string Statement);

public interface ICounterNoticeService
{
    /// <summary>Only the owner of a removed work that has a takedown notice. Anything else is the same 404 as a work that does not exist.</summary>
    Task<Guid> SubmitAsync(Guid ownerId, Guid workId, CounterNoticeRequest request);
}

public sealed class CounterNoticeService(VelaDbContext db, TimeProvider clock) : ICounterNoticeService
{
    public static SafetyException NotFound() => new(StatusCodes.Status404NotFound, "work_not_found", "No such story.");

    public static CounterNoticeRequest Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw DmcaValidator.Invalid("body", "must be a JSON object");
        var name = DmcaValidator.Text(root, "name", 1, 120, multiline: false);
        var address = DmcaValidator.Text(root, "address", 1, 300, multiline: true);
        var phone = DmcaValidator.OptionalText(root, "phone", 40);
        var statement = DmcaValidator.Text(root, "statement", 1, 2000, multiline: true);
        DmcaValidator.MustBeTrue(root, "consentToJurisdiction");
        DmcaValidator.MustBeTrue(root, "goodFaithBelief");
        return new CounterNoticeRequest(name, address, phone, statement);
    }

    public async Task<Guid> SubmitAsync(Guid ownerId, Guid workId, CounterNoticeRequest req)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        // Lock the work row: two parallel submissions cannot both pass the "once per work" check.
        var owned = (await db.Database.SqlQuery<Guid>(
            $"SELECT id AS \"Value\" FROM works WHERE id = {workId} AND artist_id = {ownerId} AND status = 'removed' FOR UPDATE").ToListAsync()).Count == 1;
        if (!owned) throw NotFound();

        var notices = await db.DmcaNotices.Where(n => n.WorkId == workId && n.OwnerUserId == ownerId)
            .OrderByDescending(n => n.TakedownAt).ThenByDescending(n => n.CreatedAt).ToListAsync();
        var target = notices.FirstOrDefault(n => n.Status == DmcaStatus.Takedown);
        if (target is null)
        {
            if (notices.Any(n => n.Status is DmcaStatus.CounterNotice or DmcaStatus.Forwarded))
                throw new SafetyException(StatusCodes.Status409Conflict, "counter_notice_exists", "A counter-notice was already submitted for this story.");
            throw NotFound();
        }
        target.CounterName = req.Name;
        target.CounterAddress = req.Address;
        target.CounterPhone = req.Phone;
        target.CounterStatement = req.Statement;
        target.CounterAt = clock.GetUtcNow();
        target.Status = DmcaStatus.CounterNotice;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return target.Id;
    }
}
