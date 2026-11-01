namespace Vela.Api.Data.Entities;

public class Tier
{
    public static readonly Guid FreemiumId = new("5b0c1c3e-7a3d-4c55-9a52-1f0e2d6b7a01");

    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public int MaxWorks { get; init; }
}
