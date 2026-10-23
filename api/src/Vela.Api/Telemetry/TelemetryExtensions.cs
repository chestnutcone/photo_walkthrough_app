using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Vela.Api.Telemetry;

public static class TelemetryExtensions
{
    /// <summary>OpenTelemetry traces, metrics and logs to Azure Monitor (Log Analytics). Does nothing without a connection string.</summary>
    public static IServiceCollection AddVelaTelemetry(this IServiceCollection services, IConfiguration config)
    {
        var t = TelemetryOptions.Load(config);
        if (!t.AzureMonitorEnabled) return services;

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("vela-api"))
            .WithTracing(b => b
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(t.SamplingRatio)))
                .AddAspNetCoreInstrumentation(o => o.RecordException = false)
                .AddHttpClientInstrumentation()
                .AddAzureMonitorTraceExporter(o => o.ConnectionString = t.AzureMonitorConnectionString))
            .WithMetrics(b => b
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(VelaMetrics.MeterName)
                .AddAzureMonitorMetricExporter(o => o.ConnectionString = t.AzureMonitorConnectionString));

        services.AddLogging(l => l.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.ParseStateValues = false;
            o.IncludeScopes = false;
            o.AddProcessor(new ScrubbingLogProcessor());
            o.AddAzureMonitorLogExporter(x => x.ConnectionString = t.AzureMonitorConnectionString);
        }));
        return services;
    }

    /// <summary>Sentry crash and error reporting. Does nothing without a DSN.</summary>
    public static IWebHostBuilder UseVelaSentry(this IWebHostBuilder host, IConfiguration config, string environment)
    {
        if (!TelemetryOptions.Load(config).SentryEnabled) return host;
        return host.UseSentry(o => SentryScrubber.Configure(o, config, environment));
    }

    public static IApplicationBuilder UseVelaExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ExceptionHandlingMiddleware>();

    private sealed class ScrubbingLogProcessor : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord record)
        {
            record.FormattedMessage = PiiScrubber.Scrub(record.FormattedMessage);
            record.Body = PiiScrubber.Scrub(record.Body);
        }
    }
}
