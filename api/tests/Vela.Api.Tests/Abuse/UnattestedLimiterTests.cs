using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Vela.Api.Abuse;
using Vela.Api.Telemetry;

namespace Vela.Api.Tests.Abuse;

/// <summary>The launch numbers of the unattested budget, exercised outside Development/Test (where the limiter is lenient).</summary>
public class UnattestedLimiterTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Vela.Api";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static UnattestedLimiter Make(string env = "Production", Dictionary<string, int>? overrides = null) =>
        new(new AbuseOptions { PermitOverrides = overrides ?? [] }, new Env(env));

    private static int Granted(UnattestedLimiter l, string action, string subject, int tries)
    {
        var n = 0;
        for (var i = 0; i < tries; i++) if (l.TryAcquire(action, subject) is null) n++;
        return n;
    }

    [Theory]
    [InlineData("email_request", 20, 1)]
    [InlineData("email_target", 5, 1)]
    [InlineData("sso", 60, 1)]
    [InlineData("create_work", 10, 24)]
    public void Launch_defaults_are_safe_for_shared_addresses(string action, int permit, int windowHours)
    {
        using var l = Make();

        Assert.Equal((permit, TimeSpan.FromHours(windowHours)), UnattestedLimiter.Defaults[action]);
        Assert.Equal(permit, Granted(l, action, "ip:203.0.113.7", permit + 3));
        Assert.Equal(permit, Granted(l, action, "ip:203.0.113.8", permit + 3)); // another subject has its own budget
    }

    [Fact]
    public void A_denied_request_carries_a_retry_after()
    {
        using var l = Make(overrides: new() { ["unattested-sso"] = 1 });
        Assert.Null(l.TryAcquire("sso", "ip:a"));
        var wait = l.TryAcquire("sso", "ip:a");
        Assert.NotNull(wait);
        Assert.True(wait > TimeSpan.Zero);
    }

    [Theory]
    [InlineData("email_request")]
    [InlineData("email_target")]
    [InlineData("sso")]
    [InlineData("create_work")]
    public void Every_limit_is_overridable(string action)
    {
        using var l = Make(overrides: new() { ["unattested-" + action] = 2 });
        Assert.Equal(2, Granted(l, action, "s", 5));
    }

    [Fact]
    public void Development_and_test_are_lenient_unless_overridden()
    {
        using var test = Make("Test");
        using var dev = Make("Development");
        Assert.Equal(200, Granted(test, "email_target", "s", 200));
        Assert.Equal(200, Granted(dev, "sso", "s", 200));
    }

    [Fact]
    public void Peek_checks_without_spending()
    {
        using var l = Make(overrides: new() { ["unattested-sso"] = 1 });
        for (var i = 0; i < 5; i++) Assert.Null(l.Peek("sso", "ip:a"));
        Assert.Null(l.TryAcquire("sso", "ip:a"));
        Assert.NotNull(l.Peek("sso", "ip:a"));
    }

    [Fact]
    public void Each_refusal_is_counted_by_action()
    {
        var seen = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (i, ml) => { if (i.Meter.Name == VelaMetrics.MeterName) ml.EnableMeasurementEvents(i); };
        listener.SetMeasurementEventCallback<long>((i, _, tags, _) =>
        {
            if (i.Name == "vela.abuse.unattested_limited") seen.Enqueue(string.Join(",", tags.ToArray().Select(t => $"{t.Key}={t.Value}")));
        });
        listener.Start();
        // A unique action name keeps refusals from tests running in parallel out of the assertion.
        var action = "test-" + Guid.NewGuid().ToString("N");
        using var l = Make(overrides: new() { ["unattested-" + action] = 1 });

        Assert.Null(l.TryAcquire(action, "s"));   // allowed: not counted
        Assert.NotNull(l.TryAcquire(action, "s")); // refused: counted
        Assert.NotNull(l.TryAcquire(action, "s")); // refused: counted
        Assert.NotNull(l.Peek(action, "s"));       // a peek spends nothing and is not a refusal

        Assert.Equal(["action=" + action, "action=" + action], seen.Where(t => t == "action=" + action));
    }
}
