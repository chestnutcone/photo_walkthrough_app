using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Security.SecurityWorld;

namespace Vela.Api.Tests.Security;

/// <summary>What anonymous (and other signed-in) callers can learn from responses: no login emails, provider subjects, secrets or internal ids.</summary>
[Collection(PostgresCollection.Name)]
public partial class PublicExposureTests(PostgresFixture pg)
{
    /// <summary>Field names that must never appear in any JSON a client receives.</summary>
    private static readonly string[] ForbiddenKeys =
    [
        "email", "loginEmail", "subject", "sub", "provider", "providerSubject", "subjectHash", "tokenHash", "passwordHash", "hash",
        "secret", "pepper", "signingKey", "clientIp", "ip", "ipAddress", "isAdmin", "suspendedAt", "deletedAt", "reportHidden",
        "moderationOverride", "hiddenBy", "imageBlob", "originalBlob", "senderEmail", "familyId", "userId",
    ];

    /// <summary>Keys that end in "id" and are allowed: the public identifiers the contract documents.</summary>
    private static readonly string[] AllowedIdKeys = ["id", "workId", "replyId", "artistId", "ids"];

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex AnyEmail();

    private static IEnumerable<string> Keys(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
            {
                yield return p.Name;
                foreach (var k in Keys(p.Value)) yield return k;
            }
        else if (e.ValueKind == JsonValueKind.Array)
            foreach (var item in e.EnumerateArray())
                foreach (var k in Keys(item)) yield return k;
    }

    private static void AssertClean(string what, string text, SecurityWorld w)
    {
        Assert.DoesNotContain(w.AliceLoginEmail, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.BobLoginEmail, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.AliceSubject, text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-login.test", text, StringComparison.OrdinalIgnoreCase);
        Assert.False(AnyEmail().IsMatch(text), $"{what} contains an email address");
        foreach (var key in Keys(JsonDocument.Parse(text).RootElement))
        {
            Assert.DoesNotContain(ForbiddenKeys, f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
            if (key.EndsWith("Id", StringComparison.Ordinal) || key.EndsWith("Ids", StringComparison.Ordinal))
                Assert.True(AllowedIdKeys.Contains(key), $"{what} exposes the internal id field '{key}'");
        }
    }

    [Fact]
    public async Task Public_endpoints_never_return_login_email_or_internal_ids_beyond_whitelist()
    {
        var w = await SharedAsync(pg);
        var urls = new[]
        {
            $"/v1/artists/{w.AliceHandle}", $"/v1/artists/{w.AliceHandle}/works", "/v1/artists/suggested", "/v1/feed?tab=discover",
            $"/v1/works/{w.AliceWork}", $"/v1/works/{w.AliceWork}/chapters/0/replies", "/v1/search?q=alice",
        };
        foreach (var url in urls)
        {
            var (status, text, _) = await SendAsync(w.Anonymous, HttpMethod.Get, url);
            Assert.Equal(HttpStatusCode.OK, status);
            AssertClean(url, text, w);
        }
    }

    [Fact]
    public async Task Public_endpoints_stay_clean_for_a_signed_in_viewer_too()
    {
        var w = await SharedAsync(pg);
        var urls = new[]
        {
            $"/v1/artists/{w.AliceHandle}", $"/v1/artists/{w.AliceHandle}/works", $"/v1/works/{w.AliceWork}",
            $"/v1/works/{w.AliceWork}/chapters/0/replies", $"/v1/artists/{w.Alice.Id}/followers", "/v1/feed?tab=following",
        };
        foreach (var url in urls)
        {
            var (status, text, _) = await SendAsync(w.Bob.Client, HttpMethod.Get, url);
            Assert.Equal(HttpStatusCode.OK, status);
            AssertClean(url, text, w);
        }
    }

    [Fact]
    public async Task The_callers_own_account_responses_hold_no_login_email_secrets_or_subjects()
    {
        var w = await SharedAsync(pg);
        foreach (var url in new[] { "/v1/me", "/v1/notifications", $"/v1/me/export/{w.AliceExport}" })
        {
            var (status, text, _) = await SendAsync(w.Alice.Client, HttpMethod.Get, url);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.DoesNotContain(w.AliceLoginEmail, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(w.AliceSubject, text, StringComparison.Ordinal);
            foreach (var key in Keys(JsonDocument.Parse(text).RootElement))
                Assert.DoesNotContain(new[] { "subject", "tokenHash", "hash", "secret", "pepper", "signingKey", "clientIp", "familyId" }, f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Sign_in_responses_hold_only_the_token_pair()
    {
        var w = await SharedAsync(pg);
        var (_, text, _) = await SendAsync(w.Anonymous, HttpMethod.Post, "/v1/auth/sso",
            $"{{\"provider\":\"fake\",\"idToken\":\"fake:p-{Guid.NewGuid()}:probe@secret-login.test\",\"nonce\":\"n\"}}");
        var data = JsonDocument.Parse(text).RootElement.GetProperty("data");
        Assert.Equal(["accessToken", "expiresIn", "isNewUser", "refreshToken"], data.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.DoesNotContain("probe@secret-login.test", text);
    }

    [Fact]
    public async Task Hostile_input_and_unknown_routes_never_leak_stack_traces_or_internal_paths()
    {
        var w = await SharedAsync(pg);
        var probes = new List<(HttpClient Client, HttpMethod Method, string Url, string? Body)>
        {
            (w.Anonymous, HttpMethod.Post, "/v1/auth/sso", "{not json"),
            (w.Anonymous, HttpMethod.Post, "/v1/auth/sso", "[]"),
            (w.Anonymous, HttpMethod.Post, "/v1/auth/email/verify", "{\"email\":12,\"code\":[]}"),
            (w.Anonymous, HttpMethod.Post, "/v1/auth/refresh", "null"),
            (w.Anonymous, HttpMethod.Get, "/v1/works/not-a-guid", null),
            (w.Anonymous, HttpMethod.Get, "/v1/works/" + Guid.NewGuid(), null),
            (w.Anonymous, HttpMethod.Get, "/v1/feed?cursor=%00%00&tab=discover", null),
            (w.Anonymous, HttpMethod.Get, "/v1/feed?tab=%27%3BDROP%20TABLE%20users%3B--", null),
            (w.Anonymous, HttpMethod.Get, "/v1/search?q=" + new string('a', 5000), null),
            (w.Anonymous, HttpMethod.Get, "/v1/artists/..%2F..%2Fetc%2Fpasswd", null),
            (w.Anonymous, HttpMethod.Get, "/no/such/route", null),
            (w.Anonymous, HttpMethod.Delete, "/v1/feed", null),
            (w.Anonymous, HttpMethod.Post, "/v1/legal/dmca-notices", "{\"claimantName\":1}"),
            (w.Anonymous, HttpMethod.Get, "/s/not-a-guid", null),
            (w.Anonymous, HttpMethod.Get, "/legal/../../etc/passwd", null),
            (w.Alice.Client, HttpMethod.Put, $"/v1/works/{w.AliceDraft}", "{\"title\":123,\"chapters\":\"x\"}"),
            (w.Alice.Client, HttpMethod.Patch, "/v1/me/profile", "{\"bio\":{}}"),
            (w.Alice.Client, HttpMethod.Post, "/v1/me/onboarding", "{\"handle\":null}"),
            (w.Alice.Client, HttpMethod.Post, "/v1/works", "{\"contentType\":\"text/html\"}"),
            (w.Alice.Client, HttpMethod.Post, "/v1/reports", "{\"targetType\":\"nope\",\"targetId\":\"x\",\"reason\":1}"),
            (w.Alice.Client, HttpMethod.Post, "/v1/notifications/read", "{\"ids\":[\"x\"]}"),
            (w.Alice.Client, HttpMethod.Post, $"/v1/works/{w.AliceWork}/chapters/0/replies", "{\"body\":null}"),
            (w.Alice.Client, HttpMethod.Get, "/v1/me/export/not-a-guid", null),
            (w.Anonymous, HttpMethod.Get, "/admin/users/not-a-guid", null),
        };
        foreach (var (client, method, url, body) in probes)
        {
            var (status, text, _) = await SendAsync(client, method, url, body);
            Assert.True((int)status < 500, $"{method} {url} -> {(int)status}: {text}");
            AssertNoInternals($"{method} {url}", text);
        }
    }

    [Fact]
    public async Task An_unexpected_exception_returns_the_generic_envelope_and_no_details()
    {
        var armed = new ExplodeSwitch();
        var w = await SecurityWorld.CreateWithServicesAsync(pg,
            s => s.AddScoped<Vela.Api.Works.IWorkService>(sp => new ExplodingWorkService(ActivatorUtilities.CreateInstance<Vela.Api.Works.WorkService>(sp), armed)));
        armed.On = true; // the world was built with the real service; from here every call blows up
        var (status, text, response) = await SendAsync(w.Anonymous, HttpMethod.Get, $"/v1/works/{w.AliceWork}");

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Equal("internal_error", ErrorCode(text));
        AssertNoInternals("500", text);
        Assert.DoesNotContain("boom-secret-detail", text);
        Assert.True(response.Headers.Contains("X-Trace-Id"));
        w.Dispose();
    }

    private static readonly string[] Internals =
    [
        "System.", "Microsoft.", "Npgsql", "EntityFramework", "StackTrace", "   at ", ".cs:line", "Exception", "/home/", "/src/", "/app/",
        "C:\\", "Vela.Api", "SELECT ", "INSERT ", "connection string", "Host=", "Password=", "appsettings", "Developer Exception",
    ];

    private static void AssertNoInternals(string what, string text)
    {
        foreach (var marker in Internals)
            Assert.False(text.Contains(marker, StringComparison.Ordinal), $"{what} leaks '{marker}': {text[..Math.Min(text.Length, 300)]}");
    }
}

internal sealed class ExplodeSwitch { public volatile bool On; }

/// <summary>Behaves like the real service until armed, then throws with a message that must never reach a client.</summary>
internal sealed class ExplodingWorkService(Vela.Api.Works.IWorkService inner, ExplodeSwitch armed) : Vela.Api.Works.IWorkService
{
    private InvalidOperationException Boom() => new("boom-secret-detail at /home/oliver/vela/api/src/Secret.cs:line 42 Host=db Password=hunter2");
    public Task<Vela.Api.Works.WorkDto?> GetAsync(Guid workId, Guid? viewerId) => armed.On ? throw Boom() : inner.GetAsync(workId, viewerId);
    public Task<Vela.Api.Works.WorkDto> UpdateAsync(Guid userId, Guid workId, Vela.Api.Works.UpdateWorkRequest request) => armed.On ? throw Boom() : inner.UpdateAsync(userId, workId, request);
    public Task<Vela.Api.Works.WorkDto> RequestPublishAsync(Guid userId, Guid workId) => armed.On ? throw Boom() : inner.RequestPublishAsync(userId, workId);
    public Task DeleteAsync(Guid userId, Guid workId) => armed.On ? throw Boom() : inner.DeleteAsync(userId, workId);
}
