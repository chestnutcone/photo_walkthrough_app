using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Social;

/// <summary>Replies a viewer may see before the Hidden flag is considered: not by a hidden user, author still active.</summary>
public static class ReplyRules
{
    public static IQueryable<Reply> FromVisibleAuthors(VelaDbContext db, IReadOnlyCollection<Guid> hiddenUsers)
    {
        var hidden = hiddenUsers.ToArray();
        return db.Replies.Where(r => !hidden.Contains(r.AuthorId)
            && db.Users.Any(u => u.Id == r.AuthorId && u.DeletedAt == null && u.SuspendedAt == null));
    }
}
