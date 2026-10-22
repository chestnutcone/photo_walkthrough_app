using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Auth.EmailCodes;
using Vela.Api.Common;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Auth.EmailCodes;

[Collection(PostgresCollection.Name)]
public class EmailCodeEndpointTests(PostgresFixture pg) : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public void Dispose() => _factories.ForEach(f => f.Dispose());

    private sealed class HeaderIpResolver : IClientIpResolver
    {
        public System.Net.IPAddress Resolve(HttpContext ctx) =>
            System.Net.IPAddress.Parse(ctx.Request.Headers["X-Test-Ip"].FirstOrDefault() ?? "127.0.0.1");
    }

    private WebApplicationFactory<Program> Factory(string env = "Test", Action<IWebHostBuilder>? tweak = null)
    {
        var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(env);
            b.UseSetting("ConnectionStrings:Default", pg.ConnectionString);
            b.UseSetting("Auth:Jwt:SigningKey", "integration-test-signing-key-0123456789-abc");
            b.UseSetting("Auth:EmailCode:Pepper", "integration-test-pepper-0123456789-abcdef");
            tweak?.Invoke(b);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<TimeProvider>(new MutableTimeProvider(Now));
                s.AddSingleton<IClientIpResolver, HeaderIpResolver>();
            });
        });
        _factories.Add(f);
        return f;
    }

    private static string NewEmail() => $"ep-{Guid.NewGuid():N}@example.test";
    private static string NewIp() => $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}";

    private static async Task<(HttpResponseMessage Res, JsonElement Body)> Post(HttpClient c, string path, object req, string? ip = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(req) };
        msg.Headers.Add("X-Test-Ip", ip ?? NewIp());
        var r = await c.SendAsync(msg);
        return (r, JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone());
    }

    private static string Code(WebApplicationFactory<Program> f) =>
        Regex.Match(f.Services.GetRequiredService<FakeEmailSender>().Sent.Last().TextBody, @"\b\d{6}\b").Value;

    [Fact]
    public async Task Request_then_verify_returns_token_pair()
    {
        var f = Factory();
        var c = f.CreateClient();
        var email = NewEmail();

        var (r1, b1) = await Post(c, "/v1/auth/email/request", new { email });
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.True(b1.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, b1.GetProperty("data").ValueKind);

        var (r2, b2) = await Post(c, "/v1/auth/email/verify", new { email, code = Code(f) });
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var d = b2.GetProperty("data");
        Assert.False(string.IsNullOrEmpty(d.GetProperty("accessToken").GetString()));
        Assert.True(d.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(900, d.GetProperty("expiresIn").GetInt32());
    }

    [Fact]
    public async Task Request_response_is_identical_for_existing_and_new_emails()
    {
        var f = Factory();
        var c = f.CreateClient();
        var known = NewEmail();
        await Post(c, "/v1/auth/email/request", new { email = known });
        await Post(c, "/v1/auth/email/verify", new { email = known, code = Code(f) });

        var (rk, bk) = await Post(c, "/v1/auth/email/request", new { email = known });
        var (rn, bn) = await Post(c, "/v1/auth/email/request", new { email = NewEmail() });

        Assert.Equal(rk.StatusCode, rn.StatusCode);
        Assert.Equal(bk.GetRawText(), bn.GetRawText());
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("")]
    public async Task Invalid_email_returns_400_invalid_email(string email)
    {
        var (r, b) = await Post(Factory().CreateClient(), "/v1/auth/email/request", new { email });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("invalid_email", b.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Missing_body_returns_400_invalid_email()
    {
        var c = Factory().CreateClient();
        var r = await c.PostAsync("/v1/auth/email/request", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task All_verify_failures_return_the_same_400_invalid_code()
    {
        var f = Factory();
        var c = f.CreateClient();
        var email = NewEmail();
        await Post(c, "/v1/auth/email/request", new { email });
        var real = Code(f);

        var (rw, bw) = await Post(c, "/v1/auth/email/verify", new { email, code = real == "000000" ? "000001" : "000000" });
        var (ru, bu) = await Post(c, "/v1/auth/email/verify", new { email = NewEmail(), code = "123456" });
        var (rm, bm) = await Post(c, "/v1/auth/email/verify", new { email, code = "abc" });

        foreach (var (r, b) in new[] { (rw, bw), (ru, bu), (rm, bm) })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("invalid_code", b.GetProperty("error").GetProperty("code").GetString());
        }
        Assert.Equal(bw.GetProperty("error").GetProperty("message").GetString(), bu.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Rate_limited_request_returns_429_with_retry_after()
    {
        var f = Factory();
        var c = f.CreateClient();
        var email = NewEmail();
        for (var i = 0; i < 5; i++) await Post(c, "/v1/auth/email/request", new { email });

        var (r, b) = await Post(c, "/v1/auth/email/request", new { email });

        Assert.Equal(HttpStatusCode.TooManyRequests, r.StatusCode);
        Assert.Equal("rate_limited", b.GetProperty("error").GetProperty("code").GetString());
        Assert.True(r.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task Suspended_user_gets_403_on_verify()
    {
        var f = Factory();
        var c = f.CreateClient();
        var email = NewEmail();
        await Post(c, "/v1/auth/email/request", new { email });
        await Post(c, "/v1/auth/email/verify", new { email, code = Code(f) });
        await using var db = pg.NewContext();
        var uid = (await db.Identities.SingleAsync(i => i.Subject == email)).UserId;
        await db.Users.Where(u => u.Id == uid).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, Now));

        await Post(c, "/v1/auth/email/request", new { email });
        var (r, b) = await Post(c, "/v1/auth/email/verify", new { email, code = Code(f) });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("account_suspended", b.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Review_mode_accepts_fixed_code_through_the_endpoint()
    {
        var email = NewEmail();
        var f = Factory(tweak: b =>
        {
            b.UseSetting("Auth:ReviewMode", "true");
            b.UseSetting("Auth:ReviewEmails:0", email);
            b.UseSetting("Auth:ReviewCode", "135790");
        });
        var c = f.CreateClient();

        await Post(c, "/v1/auth/email/request", new { email });
        var (r, _) = await Post(c, "/v1/auth/email/verify", new { email, code = "135790" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.DoesNotContain(f.Services.GetRequiredService<FakeEmailSender>().Sent, s => s.To == email);
    }

    [Theory]
    [InlineData(null, null, "Auth:ReviewEmails")]
    [InlineData("a@x.test", null, "Auth:ReviewCode")]
    [InlineData("a@x.test", "12345", "Auth:ReviewCode")]
    [InlineData(null, "123456", "Auth:ReviewEmails")]
    public void Review_mode_without_emails_or_six_digit_code_fails_startup(string? email, string? code, string setting)
    {
        var f = Factory(tweak: b =>
        {
            b.UseSetting("Auth:ReviewMode", "true");
            if (email is not null) b.UseSetting("Auth:ReviewEmails:0", email);
            if (code is not null) b.UseSetting("Auth:ReviewCode", code);
        });
        var ex = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
        Assert.Contains(setting, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("short-pepper")]
    public void Missing_or_short_pepper_fails_startup_outside_development(string? pepper)
    {
        var f = Factory("Production", b =>
        {
            b.UseSetting("Auth:EmailCode:Pepper", pepper);
            b.UseSetting("Email:Acs:ConnectionString", "endpoint=https://x.communication.azure.com/;accesskey=abc");
            b.UseSetting("Email:SenderAddress", "noreply@example.test");
            b.UseSetting("Storage:ConnectionString", "UseDevelopmentStorage=true");
            b.UseSetting("Storage:PublicBaseUrl", "https://cdn.example.test/");
            b.UseSetting("Web:ViewerUrl", "https://viewer.example.test");
            b.UseSetting("ContentSafety:Endpoint", "https://safety.example.test/");
            b.UseSetting("ContentSafety:Key", "test-key");
        });
        var ex = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
        Assert.Contains("Auth:EmailCode:Pepper", ex.Message);
    }

    [Theory]
    [InlineData("Email:Acs:ConnectionString")]
    [InlineData("Email:SenderAddress")]
    public void Missing_acs_settings_fail_startup_outside_development_and_test(string missing)
    {
        var f = Factory("Production", b =>
        {
            if (missing != "Email:Acs:ConnectionString") b.UseSetting("Email:Acs:ConnectionString", "endpoint=https://x.communication.azure.com/;accesskey=abc");
            if (missing != "Email:SenderAddress") b.UseSetting("Email:SenderAddress", "noreply@example.test");
        });
        var ex = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
        Assert.Contains(missing, ex.Message);
    }

    [Theory]
    [InlineData("Production", typeof(AcsEmailSender))]
    [InlineData("Development", typeof(ConsoleEmailSender))]
    [InlineData("Test", typeof(FakeEmailSender))]
    public void Sender_implementation_depends_on_environment(string env, Type expected)
    {
        var f = Factory(env, b =>
        {
            b.UseSetting("Email:Acs:ConnectionString", "endpoint=https://x.communication.azure.com/;accesskey=abc");
            b.UseSetting("Email:SenderAddress", "noreply@example.test");
            b.UseSetting("Storage:ConnectionString", "UseDevelopmentStorage=true");
            b.UseSetting("Storage:PublicBaseUrl", "https://cdn.example.test/");
            b.UseSetting("Web:ViewerUrl", "https://viewer.example.test");
            b.UseSetting("ContentSafety:Endpoint", "https://safety.example.test/");
            b.UseSetting("ContentSafety:Key", "test-key");
        });
        using var scope = f.Services.CreateScope();
        Assert.IsType(expected, scope.ServiceProvider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public async Task Console_sender_prints_only_the_code_never_the_address()
    {
        var output = new StringWriter();
        new ConsoleEmailSender(output).SendAsync("secret@example.test", "Subject", "Your code is 123456. It expires in 10 minutes.");
        Assert.Contains("123456", output.ToString());
        Assert.DoesNotContain("secret", output.ToString());
        Assert.DoesNotContain("example.test", output.ToString());
    }

    [Fact]
    public void Default_client_ip_resolver_returns_remote_address()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");
        Assert.Equal("203.0.113.9", new RemoteAddressClientIpResolver().Resolve(ctx).ToString());
    }

    [Fact]
    public async Task Thirty_first_verify_from_one_ip_returns_429_with_exact_retry_after()
    {
        var c = Factory().CreateClient();
        var ip = NewIp();
        for (var i = 0; i < 30; i++)
        {
            var (r0, _) = await Post(c, "/v1/auth/email/verify", new { email = NewEmail(), code = "123456" }, ip);
            Assert.Equal(HttpStatusCode.BadRequest, r0.StatusCode);
        }

        var (r, b) = await Post(c, "/v1/auth/email/verify", new { email = NewEmail(), code = "123456" }, ip);

        Assert.Equal(HttpStatusCode.TooManyRequests, r.StatusCode);
        Assert.Equal("rate_limited", b.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(TimeSpan.FromSeconds(3600), r.Headers.RetryAfter?.Delta);
    }

    private sealed class CaptureLogger(List<string> sink, LogLevel[] levels) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (sink) { sink.Add($"{logLevel}|{formatter(state, exception)}"); }
        }
    }

    private sealed class CaptureProvider(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(sink, []);
        public void Dispose() { }
    }

    [Fact]
    public void Review_mode_outside_development_logs_a_warning_without_email_or_code()
    {
        var logs = new List<string>();
        var f = Factory("Production", b =>
        {
            b.UseSetting("Email:Acs:ConnectionString", "endpoint=https://x.communication.azure.com/;accesskey=abc");
            b.UseSetting("Email:SenderAddress", "noreply@example.test");
            b.UseSetting("Storage:ConnectionString", "UseDevelopmentStorage=true");
            b.UseSetting("Storage:PublicBaseUrl", "https://cdn.example.test/");
            b.UseSetting("Web:ViewerUrl", "https://viewer.example.test");
            b.UseSetting("ContentSafety:Endpoint", "https://safety.example.test/");
            b.UseSetting("ContentSafety:Key", "test-key");
            b.UseSetting("Auth:ReviewMode", "true");
            b.UseSetting("Auth:ReviewEmails:0", "reviewer@example.test");
            b.UseSetting("Auth:ReviewCode", "987654");
            b.ConfigureLogging(l => l.AddProvider(new CaptureProvider(logs)));
        });
        f.CreateClient();

        var warning = Assert.Single(logs, l => l.StartsWith("Warning|") && l.Contains("Review mode", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("reviewer@example.test", warning);
        Assert.DoesNotContain("987654", warning);
        Assert.DoesNotContain(logs, l => l.Contains("reviewer@example.test") || l.Contains("987654"));
    }
}
