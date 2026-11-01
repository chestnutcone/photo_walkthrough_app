namespace Vela.Api.Data.Entities;

public class Notification
{
    public Guid Id { get; init; }
    /// <summary>Recipient.</summary>
    public Guid UserId { get; init; }
    public string Type { get; init; } = "";
    public Guid? ActorId { get; init; }
    public Guid? WorkId { get; init; }
    public int? ChapterIndex { get; init; }
    public Guid? ReplyId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ReadAt { get; set; }
}
