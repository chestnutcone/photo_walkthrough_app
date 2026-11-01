namespace Vela.Api.Common;

public interface IDomainEvent;

public interface IDomainEventHandler<in T> where T : IDomainEvent
{
    Task HandleAsync(T evt, CancellationToken ct);
}

public interface IDomainEventBus
{
    /// <summary>Runs every registered handler inline, inside the caller's unit of work (same DbContext and transaction).</summary>
    Task PublishAsync<T>(T evt, CancellationToken ct) where T : IDomainEvent;
}

/// <summary>Raised on every transition to <c>published</c>, including an admin restore.</summary>
public sealed record WorkPublished(Guid WorkId, Guid ArtistId) : IDomainEvent;

/// <summary>Raised only when a follow row is newly created.</summary>
public sealed record Followed(Guid FollowerId, Guid ArtistId) : IDomainEvent;

/// <summary>Raised when a visible reply is stored (not for replies hidden by the safety scan).</summary>
public sealed record ReplyAdded(Guid ReplyId, Guid WorkId, Guid AuthorId) : IDomainEvent;

/// <summary>A page of results plus the cursor for the next one (null on the last page).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Resolves handlers from the scope it was created in, so they share the caller's DbContext.</summary>
public sealed class DomainEventBus(IServiceProvider services, ILogger<DomainEventBus> log) : IDomainEventBus
{
    /// <summary>
    /// A failing handler is logged (type names only, never content) and the exception propagates: handlers write inside the
    /// caller's transaction, so the request fails and rolls back rather than leaving a half-applied change.
    /// </summary>
    public async Task PublishAsync<T>(T evt, CancellationToken ct) where T : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<T>>())
        {
            try { await handler.HandleAsync(evt, ct); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError("Domain event handler {Handler} failed for {Event} ({Reason}).", handler.GetType().Name, typeof(T).Name, e.GetType().Name);
                throw;
            }
        }
    }
}
