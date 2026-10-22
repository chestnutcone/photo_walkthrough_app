namespace Vela.Api.Data.Entities;

public class Chapter
{
    public Guid WorkId { get; init; }
    public int Index { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Null means "stay put".</summary>
    public double? Zoom { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Meta { get; set; }
    public string? AltText { get; set; }
}
