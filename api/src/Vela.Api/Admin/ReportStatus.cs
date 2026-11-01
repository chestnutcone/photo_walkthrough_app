namespace Vela.Api.Admin;

/// <summary>Stored in <c>reports.status</c> as the lowercase member name.</summary>
public enum ReportStatus { Pending, Reviewed, Actioned, Dismissed }

public static class ReportStatusExtensions
{
    public static string ToDb(this ReportStatus status) => status.ToString().ToLowerInvariant();
}
