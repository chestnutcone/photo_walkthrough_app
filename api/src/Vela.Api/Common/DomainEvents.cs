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

/// <summary>Resolves handlers from the scope it was created in, so they share the caller's DbContext.</summary>
public sealed class DomainEventBus(IServiceProvider services) : IDomainEventBus
{
    public async Task PublishAsync<T>(T evt, CancellationToken ct) where T : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<T>>())
            await handler.HandleAsync(evt, ct);
    }
}
