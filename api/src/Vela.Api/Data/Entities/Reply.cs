namespace Vela.Api.Data.Entities;

public class Reply
{
    public Guid Id { get; init; }
    public Guid WorkId { get; init; }
    public int ChapterIndex { get; init; }
    public Guid AuthorId { get; init; }
    public string Text { get; init; } = "";
    public bool Hidden { get; set; }
    public HiddenBy? HiddenBy { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}
