using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Vela.Api.Telemetry;

namespace Vela.Api.Tests.Telemetry;

public sealed class CapturingActivityExporter : BaseExporter<Activity>
{
    public List<(string Name, Dictionary<string, string?> Tags)> Seen { get; } = [];
    public override ExportResult Export(in Batch<Activity> batch)
    {
        foreach (var a in batch) Seen.Add((a.DisplayName, a.TagObjects.ToDictionary(t => t.Key, t => t.Value?.ToString())));
        return ExportResult.Success;
    }
}

/// <summary>FINALAPI I2: spans carry no search text, handles, user ids, client address or SAS signature to the exporter.</summary>
public class TraceScrubbingTests
{
    private static (string Name, Dictionary<string, string?> Tags) Export(string name, ActivityKind kind, params (string, object?)[] tags)
    {
        var source = "vela-test-" + Guid.NewGuid();
        var exporter = new CapturingActivityExporter();
        using (var provider = Sdk.CreateTracerProviderBuilder().AddSource(source).SetSampler(new AlwaysOnSampler())
                   .AddVelaTraceScrubbing().AddProcessor(new SimpleActivityExportProcessor(exporter)).Build())
        using (var src = new ActivitySource(source))
        using (var a = src.StartActivity(name, kind)!)
            foreach (var (k, v) in tags) a.SetTag(k, v);
        return Assert.Single(exporter.Seen);
    }

    [Fact]
    public void Server_spans_keep_the_route_template_and_lose_query_path_values_and_client_address()
    {
        var (name, tags) = Export("GET /v1/search", ActivityKind.Server,
            ("http.route", "v1/search"), ("url.path", "/v1/search"), ("url.query", "?q=jane@example.com"),
            ("url.full", "https://api.vela.test/v1/search?q=jane@example.com"), ("client.address", "203.0.113.9"),
            ("http.request.method", "GET"), ("http.response.status_code", 200));

        var dump = name + "|" + string.Join("|", tags.Select(t => t.Key + "=" + t.Value));
        Assert.DoesNotContain("jane", dump);
        Assert.DoesNotContain("203.0.113.9", dump);
        Assert.False(tags.ContainsKey("url.query"));
        Assert.Equal("v1/search", tags["url.path"]);
        Assert.Equal("https://api.vela.test/v1/search", tags["url.full"]);
        Assert.Equal("GET", tags["http.request.method"]);
        Assert.Equal("200", tags["http.response.status_code"]);
    }

    [Fact]
    public void Handles_and_ids_in_paths_are_replaced_by_the_template()
    {
        var id = Guid.NewGuid().ToString();
        var (_, tags) = Export("GET /v1/artists/{handle}", ActivityKind.Server,
            ("http.route", "/v1/artists/{handle}"), ("url.path", "/v1/artists/secret_handle"), ("http.target", $"/v1/blocks/{id}"));

        Assert.Equal("/v1/artists/{handle}", tags["url.path"]);
        Assert.Equal("/v1/artists/{handle}", tags["http.target"]);
        Assert.DoesNotContain(tags.Values, v => v?.Contains("secret_handle") == true || v?.Contains(id) == true);
    }

    [Fact]
    public void Unmatched_routes_keep_no_path_and_the_name_is_cut_to_the_method()
    {
        var (name, tags) = Export("GET /s/jane@example.com", ActivityKind.Server, ("url.path", "/s/jane@example.com"));

        Assert.Equal("GET", name);
        Assert.Equal(ScrubbingTraceProcessor.Redacted, tags["url.path"]);
    }

    [Fact]
    public void Outbound_urls_lose_their_query_so_sas_signatures_never_leave()
    {
        var (_, tags) = Export("GET", ActivityKind.Client,
            ("url.full", "https://acct.blob.core.windows.net/originals/sealed/x/y?sv=2024&sig=SIGSECRET"),
            ("http.url", "https://h.test/u/jane%40example.com?x=1"));

        Assert.Equal("https://acct.blob.core.windows.net/originals/sealed/x/y", tags["url.full"]);
        Assert.Equal("https://h.test/" + ScrubbingTraceProcessor.Redacted, tags["http.url"]);
    }
}
