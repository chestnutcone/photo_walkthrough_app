using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Sentry;
using Vela.Api.Auth;
using Vela.Api.Common;
using Vela.Api.Inquiries;
using Vela.Api.Profiles;
using Vela.Api.Tests.Inquiries;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using Vela.Api.Telemetry;
using static Vela.Api.Tests.Notifications.NotificationKit;

namespace Vela.Api.Tests.Telemetry;

/// <summary>Records every log entry (message, structured values, exception text) so tests can assert nothing personal leaks.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);
    public void Dispose() { }
    public string All => string.Join("\n", Entries);

    private sealed class Sink(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? e, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> kv ? string.Join(";", kv.Select(p => $"{p.Key}={p.Value}")) : "";
            owner.Entries.Enqueue($"{category} {level} {formatter(state, e)} {values} {e}");
        }
    }
}

public sealed class ThrowingReplyService : Vela.Api.Social.IReplyService
{
    public Task<PagedResult<Vela.Api.Social.ReplyDto>> ListAsync(Guid? viewerId, Guid workId, int chapterIndex, string? cursor, int limit) => throw Boom();
    public Task<Vela.Api.Social.ReplyDto> AddAsync(Guid userId, Guid workId, int chapterIndex, string text) => throw Boom();
    public Task DeleteAsync(Guid userId, Guid replyId) => throw Boom();
    private static Exception Boom() => new InvalidOperationException("boom with fan@example.test and SECRET-BODY-TEXT at /src/Secret.cs:line 42");
}

public class PiiScrubberTests
{
    [Theory]
    [InlineData("contact me at jane.doe+tag@example.com please", "jane.doe+tag@example.com")]
    [InlineData("failed for USER@Sub.Example.CO.UK.", "USER@Sub.Example.CO.UK")]
    [InlineData("addr=\"o'brien@example.org\"", "o'brien@example.org")]
    public void Scrubber_redacts_email_addresses(string input, string secret)
    {
        var scrubbed = PiiScrubber.Scrub(input);

        Assert.DoesNotContain(secret, scrubbed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[email]", scrubbed);
    }

    [Theory]
    [InlineData("Authorization: Bearer abc.DEF-123_xyz~+/=", "abc.DEF-123_xyz")]
    [InlineData("code=123456", "123456")]
    [InlineData("{\"code\":\"654321\"}", "654321")]
    [InlineData("https://x.blob.core.windows.net/c/b.jpg?sv=2024&sig=AbC%2Bdef%3D&se=2026", "AbC%2Bdef%3D")]
    [InlineData("refresh_token=r-9f8e7d", "r-9f8e7d")]
    public void Scrubber_redacts_tokens_codes_and_sas_signatures(string input, string secret)
    {
        var scrubbed = PiiScrubber.Scrub(input);

        Assert.DoesNotContain(secret, scrubbed);
    }

    [Fact]
    public void Scrubber_keeps_harmless_text_and_handles_null_and_empty()
    {
        Assert.Equal("Draft cleanup removed 3 of 4 expired drafts.", PiiScrubber.Scrub("Draft cleanup removed 3 of 4 expired drafts."));
        Assert.Equal("order 123456 shipped", PiiScrubber.Scrub("order 123456 shipped"));
        Assert.Equal("", PiiScrubber.Scrub(""));
        Assert.Null(PiiScrubber.Scrub(null));
    }
}

[Collection(PostgresCollection.Name)]
public class TelemetryTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    private readonly CapturingLoggerProvider _logs = new();

    private async Task<ProfileTestHost> Host(Action<IServiceCollection>? extra = null, Dictionary<string, string>? settings = null)
    {
        var all = new Dictionary<string, string> { ["Abuse:RateLimits:public-form:Permit"] = "1000" };
        foreach (var (k, v) in settings ?? []) all[k] = v;
        var h = NewHost(pg, await pg.CreateIsolatedDatabaseAsync(), s =>
        {
            s.AddLogging(l => { l.SetMinimumLevel(LogLevel.Trace); l.AddProvider(_logs); });
            extra?.Invoke(s);
        }, all);
        _hosts.Add(h);
        return h;
    }

    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private static SentryEvent DirtyEvent()
    {
        var evt = new SentryEvent(new InvalidOperationException("failed for jane@example.com code=123456"))
        {
            Message = new SentryMessage { Message = "user jane@example.com wrote", Formatted = "user jane@example.com wrote" },
            User = new SentryUser { Id = "u1", Email = "jane@example.com", IpAddress = "203.0.113.9", Username = "jane" },
            Request = new SentryRequest
            {
                Data = "{\"body\":\"my private reply\"}",
                Cookies = "session=abc",
                QueryString = "email=jane@example.com&sig=SIGSECRET",
                Url = "https://api.example.test/v1/x?sig=SIGSECRET",
                Headers = { ["Authorization"] = "Bearer tok123", ["Cookie"] = "a=b", ["X-Forwarded-For"] = "203.0.113.9", ["Accept"] = "application/json" },
                Env = { ["REMOTE_ADDR"] = "203.0.113.9" },
            },
        };
        evt.AddBreadcrumb("sent mail to jane@example.com", "mail", "info", (IReadOnlyDictionary<string, string>?)null, BreadcrumbLevel.Info);
        evt.SetExtra("body", "my private reply");
        evt.SetTag("owner", "jane@example.com");
        return evt;
    }

    [Fact]
    public void Sentry_events_contain_no_email_ip_or_request_body()
    {
        var evt = SentryScrubber.Apply(DirtyEvent());

        Assert.NotNull(evt);
        var dump = string.Join("|", evt!.Message?.Message, evt.Message?.Formatted, evt.User.Email, evt.User.IpAddress, evt.User.Username,
            evt.Request.Data, evt.Request.Cookies, evt.Request.QueryString, evt.Request.Url,
            string.Join(",", evt.Request.Headers.Select(h => h.Key + "=" + h.Value)),
            string.Join(",", evt.Request.Env.Select(h => h.Key + "=" + h.Value)),
            string.Join(",", evt.Breadcrumbs.Select(b => b.Message)),
            string.Join(",", evt.Tags.Select(t => t.Value)),
            string.Join(",", evt.Extra.Select(t => t.Key + "=" + t.Value)),
            string.Join(",", evt.SentryExceptions?.Select(x => x.Value) ?? []));
        Assert.DoesNotContain("jane", dump);
        Assert.DoesNotContain("203.0.113.9", dump);
        Assert.DoesNotContain("private reply", dump);
        Assert.DoesNotContain("SIGSECRET", dump);
        Assert.DoesNotContain("tok123", dump);
        Assert.DoesNotContain("123456", dump);
        Assert.Null(evt.Request.Data);
        Assert.Null(evt.Request.Cookies);
        Assert.False(evt.Request.Headers.ContainsKey("Authorization"));
        Assert.False(evt.Request.Headers.ContainsKey("Cookie"));
        Assert.Equal("application/json", evt.Request.Headers["Accept"]);
        Assert.Equal("u1", evt.User.Id);
    }

    [Fact]
    public void Sentry_drops_the_real_attestation_header_s1b_12()
    {
        var evt = new SentryEvent(new InvalidOperationException("x"))
        {
            Request = new SentryRequest
            {
                Headers =
                {
                    [Vela.Api.Abuse.AttestationMiddleware.TokenHeader] = "ATTEST-TOKEN-SECRET", ["x-attestation"] = "ATTEST-TOKEN-SECRET",
                    [Vela.Api.Abuse.AttestationMiddleware.PlatformHeader] = "ios", ["Accept"] = "application/json",
                },
            },
        };

        var scrubbed = SentryScrubber.Apply(evt)!;

        Assert.Equal("X-Attestation", Vela.Api.Abuse.AttestationMiddleware.TokenHeader);
        Assert.DoesNotContain(scrubbed.Request.Headers, h => h.Value.Contains("ATTEST-TOKEN-SECRET"));
        Assert.Equal("application/json", scrubbed.Request.Headers["Accept"]);
    }

    [Fact]
    public void Sentry_options_disable_pii_and_install_the_scrubber()
    {
        var options = new Sentry.AspNetCore.SentryAspNetCoreOptions();
        SentryScrubber.Configure(options, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Telemetry:SentryDsn"] = "http://abc@127.0.0.1:1/1", ["Telemetry:SamplingRatio"] = "0.25",
        }).Build(), "Test");

        Assert.False(options.SendDefaultPii);
        Assert.False(options.MaxRequestBodySize != Sentry.Extensibility.RequestSize.None);
        Assert.Equal(0.25, options.TracesSampleRate);
        Assert.Equal("Test", options.Environment);
        var hook = (Func<SentryEvent, SentryHint, SentryEvent?>?)typeof(SentryOptions)
            .GetProperty("BeforeSendInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(options);
        Assert.NotNull(hook);
        Assert.Null(hook!(DirtyEvent(), new SentryHint())!.User.Email);
    }

    [Fact]
    public async Task Reply_and_inquiry_text_never_appear_in_logs()
    {
        const string replyText = "SECRET-REPLY-TEXT-8841 fan@leak.example";
        const string inquiryText = "SECRET-INQUIRY-TEXT-7712 call me";
        const string buyerEmail = "buyer-leak@example.test";
        var h = await Host(s => s.AddSingleton<IEmailSender, ThrowingEmailSender>());
        var artist = await h.SignIn("artist.login@secret-mail.test");
        await artist.Onboard(ProfileTestHost.UniqueHandle());
        var fan = await User(h);
        var work = await SeedWork(h, artist.Id);

        var reply = await fan.Post(ReplyUrl(work), new { body = replyText });
        var inquiry = await fan.Post($"/v1/artists/{artist.Id}/inquiries", new { type = "Commission", email = buyerEmail, message = inquiryText, workId = work });
        var bad = await fan.Post($"/v1/artists/{artist.Id}/inquiries", "{ not json " + inquiryText);

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal(HttpStatusCode.OK, inquiry.Status);
        Assert.NotEqual(HttpStatusCode.OK, bad.Status);
        Assert.NotEmpty(_logs.Entries);
        var logs = _logs.All;
        foreach (var secret in new[] { "SECRET-REPLY-TEXT", "SECRET-INQUIRY-TEXT", "fan@leak.example", buyerEmail, "artist.login@secret-mail.test" })
            Assert.DoesNotContain(secret, logs);
    }

    [Fact]
    public async Task Unhandled_exception_returns_envelope_without_stack_trace_and_logs_no_message()
    {
        var h = await Host(s => s.AddScoped<Vela.Api.Social.IReplyService, ThrowingReplyService>());
        var user = await h.SignIn();

        var r = await user.Client.PostAsJsonAsync("/v1/works/" + Guid.NewGuid() + "/chapters/0/replies", new { body = "SECRET-BODY-TEXT" });
        var text = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
        var json = System.Text.Json.JsonDocument.Parse(text).RootElement;
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("internal_error", json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("Something went wrong. Please try again.", json.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.GetProperty("data").ValueKind);
        Assert.DoesNotContain("Secret.cs", text);
        Assert.DoesNotContain("InvalidOperation", text);
        Assert.DoesNotContain("fan@example.test", text);
        Assert.True(r.Headers.Contains("X-Trace-Id"));
        Assert.DoesNotContain("fan@example.test", _logs.All);
        Assert.DoesNotContain("SECRET-BODY-TEXT", _logs.All);
        Assert.Contains(_logs.Entries, e => e.Contains("InvalidOperationException") && e.Contains("Error"));
    }

    [Fact]
    public async Task Validation_and_unknown_routes_are_not_turned_into_500s()
    {
        var h = await Host();
        var u = await User(h);

        var bad = await u.Post(ReplyUrl(Guid.NewGuid()), "{ not json");
        var missing = await u.Get("/v1/works/" + Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
    }

    [Fact]
    public async Task Telemetry_is_disabled_without_configuration_and_app_still_starts()
    {
        var h = await Host();

        var r = await h.Anonymous().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Null(h.Services.GetService<TracerProvider>());
        Assert.False(SentrySdk.IsEnabled);
    }

    [Fact]
    public async Task Telemetry_starts_when_configured_with_unreachable_endpoints()
    {
        var h = await Host(settings: new()
        {
            ["Telemetry:AzureMonitorConnectionString"] = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://127.0.0.1:1/",
            ["Telemetry:SentryDsn"] = "http://abc@127.0.0.1:1/1",
        });

        var r = await h.Anonymous().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.NotNull(h.Services.GetService<TracerProvider>());
    }

    [Fact]
    public void Options_default_sampling_to_ten_percent_and_clamp_bad_values()
    {
        static TelemetryOptions Load(string? ratio) => TelemetryOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(
            ratio is null ? [] : new Dictionary<string, string?> { ["Telemetry:SamplingRatio"] = ratio }).Build());

        Assert.Equal(0.1, Load(null).SamplingRatio);
        Assert.Equal(1.0, Load("7").SamplingRatio);
        Assert.Equal(0.0, Load("-1").SamplingRatio);
        Assert.Equal(0.1, Load("abc").SamplingRatio);
    }
}

public class ExceptionHandlingMiddlewareTests
{
    private static ExceptionHandlingMiddleware Make(Exception e) =>
        new(_ => throw e, Microsoft.Extensions.Logging.Abstractions.NullLogger<ExceptionHandlingMiddleware>.Instance);

    [Fact]
    public async Task Client_aborts_are_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestAborted = cts.Token };
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Make(new OperationCanceledException(cts.Token)).InvokeAsync(ctx));
    }

    [Fact]
    public async Task Kestrel_bad_request_errors_keep_their_own_status()
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        await Assert.ThrowsAsync<Microsoft.AspNetCore.Http.BadHttpRequestException>(
            () => Make(new Microsoft.AspNetCore.Http.BadHttpRequestException("too big", 413)).InvokeAsync(ctx));
    }

    [Fact]
    public async Task Unexpected_cancellation_without_an_abort_is_a_500()
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();

        await Make(new TaskCanceledException("timeout talking to storage")).InvokeAsync(ctx);

        Assert.Equal(500, ctx.Response.StatusCode);
    }
}

public class VelaMetricsTests
{
    [Fact]
    public void Counters_record_with_low_cardinality_tags_only()
    {
        var seen = new List<(string Name, string Tag)>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (i, l) => { if (i.Meter.Name == VelaMetrics.MeterName) l.EnableMeasurementEvents(i); };
        listener.SetMeasurementEventCallback<long>((i, _, tags, _) => seen.Add((i.Name, string.Join(",", tags.ToArray().Select(t => $"{t.Key}={t.Value}")))));
        listener.Start();

        VelaMetrics.ReportCreated("reply");
        VelaMetrics.Takedown("auto_block");
        VelaMetrics.ScanFailed("work");

        Assert.Contains(("vela.reports.created", "target_type=reply"), seen);
        Assert.Contains(("vela.takedowns", "reason=auto_block"), seen);
        Assert.Contains(("vela.scan.failures", "target=work"), seen);
    }
}


public sealed class CapturingLogExporter : OpenTelemetry.BaseExporter<OpenTelemetry.Logs.LogRecord>
{
    public List<(string? Message, string? Body, Exception? Exception)> Seen { get; } = [];
    public override OpenTelemetry.ExportResult Export(in OpenTelemetry.Batch<OpenTelemetry.Logs.LogRecord> batch)
    {
        foreach (var r in batch) Seen.Add((r.FormattedMessage, r.Body, r.Exception));
        return OpenTelemetry.ExportResult.Success;
    }
}

public class ScrubbingPipelineTests
{
    [Fact]
    public void Exception_text_never_reaches_the_exporter()
    {
        var exporter = new CapturingLogExporter();
        using var factory = LoggerFactory.Create(b => b.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.AddProcessor(new ScrubbingLogProcessor());
            o.AddProcessor(new OpenTelemetry.SimpleLogRecordExportProcessor(exporter));
        }));
        var ex = new InvalidOperationException("23505: duplicate key. Key (email)=(jane@example.com) already exists. at /src/X.cs");

        factory.CreateLogger("t").LogError(ex, "Save failed for jane@example.com");

        var seen = Assert.Single(exporter.Seen);
        Assert.Null(seen.Exception);
        var text = seen.Message + "|" + seen.Body;
        Assert.DoesNotContain("jane", text);
        Assert.DoesNotContain("Key (email)", text);
        Assert.Contains("InvalidOperationException", text);
    }
}

public class SentryTransactionScrubTests
{
    [Fact]
    public void Transactions_carry_no_query_text_email_ip_or_auth()
    {
        var t = new SentryTransaction("GET /v1/search", "http.server")
        {
            Request = new SentryRequest
            {
                Url = "https://api.example.test/v1/search?q=a@b.com", QueryString = "q=a@b.com", Data = "body", Cookies = "c=1",
                Headers = { ["Authorization"] = "Bearer tok123", ["Accept"] = "application/json" },
            },
            User = new SentryUser { Id = "u1", Email = "a@b.com", IpAddress = "203.0.113.9" },
        };
        t.SetTag("who", "a@b.com");

        var r = SentryScrubber.ApplyTransaction(t)!;

        var dump = string.Join("|", r.Request.Url, r.Request.QueryString, r.Request.Data, r.Request.Cookies,
            string.Join(",", r.Request.Headers.Select(h => h.Key + h.Value)), r.User.Email, r.User.IpAddress, string.Join(",", r.Tags.Values));
        Assert.DoesNotContain("a@b.com", dump);
        Assert.DoesNotContain("203.0.113.9", dump);
        Assert.DoesNotContain("tok123", dump);
        Assert.False(r.Request.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public void Scrubber_fails_closed_when_a_regex_times_out()
    {
        var result = PiiScrubber.ScrubGuarded("secret jane@example.com", _ => throw new System.Text.RegularExpressions.RegexMatchTimeoutException());

        Assert.Equal(PiiScrubber.TimeoutMask, result);
    }
}
