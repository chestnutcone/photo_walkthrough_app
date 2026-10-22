namespace Vela.Api.Works;

/// <summary>A chapter as returned to clients. <c>ReplyCount</c> is computed per viewer and ignored on input.</summary>
public sealed record ChapterDto(double X, double Y, double? Zoom, string Title, string Body, string? Meta, string? AltText, int ReplyCount);

/// <summary>A chapter as received; every field is nullable so a missing one becomes a validation error rather than a default.</summary>
public sealed record ChapterInput(double? X, double? Y, double? Zoom, string? Title, string? Body, string? Meta, string? AltText);

public sealed record UpdateWorkRequest(string? Title, string? Intro, string? AltText, IReadOnlyList<ChapterInput?>? Chapters);

public sealed record ArtistSummaryDto(Guid Id, string Handle, string DisplayName, string? AvatarUrl);

public sealed record WorkDto(
    Guid Id, ArtistSummaryDto Author, string Title, string Intro, string? AltText, bool IsOwner, string? Status,
    string? ImageUrl, string? ThumbUrl, string? FeedUrl, int? ImageWidth, int? ImageHeight,
    IReadOnlyList<ChapterDto> Chapters, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt, DateTimeOffset? DraftExpiresAt, string ShareUrl);

public interface IWorkService
{
    Task<WorkDto> UpdateAsync(Guid userId, Guid workId, UpdateWorkRequest req);
    Task<WorkDto> RequestPublishAsync(Guid userId, Guid workId);
    Task DeleteAsync(Guid userId, Guid workId);
    Task<WorkDto?> GetAsync(Guid workId, Guid? viewerId);
}

/// <summary>A refused work operation; the endpoint turns it into an envelope error. Messages never echo user text.</summary>
public sealed class WorkException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;

    public static WorkException Validation(string field, string problem) =>
        new(StatusCodes.Status400BadRequest, "validation_failed", $"{field} {problem}");

    public static WorkException NotFound() =>
        new(StatusCodes.Status404NotFound, "work_not_found", "No such story.");
}
