using System.Diagnostics.Metrics;

namespace Vela.Api.Telemetry;

/// <summary>Business counters. Tags are fixed low-cardinality labels; never put ids, handles or text in a tag.</summary>
public static class VelaMetrics
{
    public const string MeterName = "Vela.Api";
    private static readonly Meter Meter = new(MeterName, "1.0");

    private static readonly Counter<long> ReportsCreated = Meter.CreateCounter<long>("vela.reports.created", description: "User reports filed");
    private static readonly Counter<long> Takedowns = Meter.CreateCounter<long>("vela.takedowns", description: "Works or replies taken down");
    private static readonly Counter<long> ScanFailures = Meter.CreateCounter<long>("vela.scan.failures", description: "Content scans that were unavailable");

    public static void ReportCreated(string targetType) => ReportsCreated.Add(1, new KeyValuePair<string, object?>("target_type", targetType));
    public static void Takedown(string reason) => Takedowns.Add(1, new KeyValuePair<string, object?>("reason", reason));
    public static void ScanFailed(string target) => ScanFailures.Add(1, new KeyValuePair<string, object?>("target", target));
}
