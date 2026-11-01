using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Vela.Api.Telemetry;

/// <summary>Telemetry settings. Every exporter is off unless its connection string or DSN is configured.</summary>
public sealed record TelemetryOptions(double SamplingRatio, string? AzureMonitorConnectionString, string? SentryDsn)
{
    public const string Section = "Telemetry";
    public const double DefaultSamplingRatio = 0.1;

    public bool AzureMonitorEnabled => !string.IsNullOrWhiteSpace(AzureMonitorConnectionString);
    public bool SentryEnabled => !string.IsNullOrWhiteSpace(SentryDsn);

    public static TelemetryOptions Load(IConfiguration config)
    {
        var s = config.GetSection(Section);
        var ratio = double.TryParse(s["SamplingRatio"], NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && double.IsFinite(r)
            ? Math.Clamp(r, 0.0, 1.0)
            : DefaultSamplingRatio;
        return new TelemetryOptions(ratio, s["AzureMonitorConnectionString"]?.Trim(), s["SentryDsn"]?.Trim());
    }
}
