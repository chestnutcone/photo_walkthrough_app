namespace Vela.Api.Data.Entities;

/// <summary>One row per successfully created account, keyed by client address bucket. Pruned after a day; holds no user id.</summary>
public class SignupEvent
{
    public Guid Id { get; init; }
    public string IpKey { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
}
