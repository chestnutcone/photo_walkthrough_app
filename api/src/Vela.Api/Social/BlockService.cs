using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Social;

public interface IBlockService
{
    Task BlockAsync(Guid userId, Guid targetId);
    Task UnblockAsync(Guid userId, Guid targetId);
}

public sealed class BlockService(VelaDbContext db, TimeProvider clock) : IBlockService
{
    public async Task BlockAsync(Guid userId, Guid targetId)
    {
        if (userId == targetId)
            throw new SafetyException(StatusCodes.Status400BadRequest, "cannot_block_self", "You cannot block yourself.");
        if (!await db.Users.AnyAsync(u => u.Id == targetId && u.DeletedAt == null))
            throw new SafetyException(StatusCodes.Status404NotFound, "user_not_found", "No such user.");

        await using var tx = await db.Database.BeginTransactionAsync();
        await PairLock.AcquireAsync(db, userId, targetId);
        await db.Follows
            .Where(f => (f.FollowerId == userId && f.FolloweeId == targetId) || (f.FollowerId == targetId && f.FolloweeId == userId))
            .ExecuteDeleteAsync();
        // Idempotent insert: a concurrent or repeated block is a no-op.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO blocks (blocker_id, blocked_id, created_at) VALUES ({userId}, {targetId}, {clock.GetUtcNow()}) ON CONFLICT DO NOTHING");
        await tx.CommitAsync();
    }

    public Task UnblockAsync(Guid userId, Guid targetId) =>
        db.Blocks.Where(b => b.BlockerId == userId && b.BlockedId == targetId).ExecuteDeleteAsync();
}
