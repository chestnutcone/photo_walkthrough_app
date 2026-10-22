namespace Vela.Api.Data.Entities;

public class Profile
{
    public Guid UserId { get; init; }
    /// <summary>Always stored lowercase (check constraint); unique.</summary>
    public string Handle { get; set; } = "";
    public DateTimeOffset? HandleChangedAt { get; set; }
    public string DisplayName { get; set; } = "";
    public string Bio { get; set; } = "";
    public string City { get; set; } = "";
    public string Discipline { get; set; } = "";
    public string Availability { get; set; } = "";
    public string? AvatarBlob { get; set; }
    public string Website { get; set; } = "";
    public string Social { get; set; } = "";
    public string Studio { get; set; } = "";
    public string ReplyTime { get; set; } = "";
    public string? PublicEmail { get; set; }
    public List<ServiceItem> Services { get; set; } = [];
    public List<ShowItem> Shows { get; set; } = [];
}
