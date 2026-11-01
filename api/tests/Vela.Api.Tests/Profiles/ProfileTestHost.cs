using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Data;
using Vela.Api.Images;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Profiles;

public sealed record Reply(HttpStatusCode Status, JsonElement Body)
{
    public JsonElement Data => Body.GetProperty("data");
    public string? ErrorCode => Body.GetProperty("error").ValueKind == JsonValueKind.Null ? null : Body.GetProperty("error").GetProperty("code").GetString();
}

public sealed class TestUser(HttpClient client, Guid id)
{
    public HttpClient Client => client;
    public Guid Id { get; } = id;

    private async Task<Reply> Send(HttpMethod m, string url, object? body)
    {
        var req = new HttpRequestMessage(m, url);
        if (body is string raw) req.Content = new StringContent(raw, Encoding.UTF8, "application/json");
        else if (body is not null) req.Content = JsonContent.Create(body);
        var r = await client.SendAsync(req);
        return new Reply(r.StatusCode, JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone());
    }

    public Task<Reply> Get(string url) => Send(HttpMethod.Get, url, null);
    public Task<Reply> Post(string url, object body) => Send(HttpMethod.Post, url, body);
    public Task<Reply> Put(string url) => Send(HttpMethod.Put, url, null);
    public Task<Reply> Put(string url, object body) => Send(HttpMethod.Put, url, body);
    public Task<Reply> Delete(string url) => Send(HttpMethod.Delete, url, null);
    public Task<Reply> Patch(string url, object body) => Send(HttpMethod.Patch, url, body);

    public Task<Reply> Onboard(string handle, string name = "Test Artist", bool age = true, bool terms = true, IEnumerable<Guid>? follow = null) =>
        Post("/v1/me/onboarding", new { displayName = name, handle, ageConfirmed = age, termsAccepted = terms, followArtistIds = follow ?? [] });
}

/// <summary>Spins up the API on the shared Postgres with a controllable clock and the fake sign-in provider.</summary>
public sealed class ProfileTestHost : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _cs;
    public MutableTimeProvider Clock { get; } = new(Now);
    public InMemoryBlobStore Blobs { get; } = new();

    public ProfileTestHost(PostgresFixture pg, IReadOnlyDictionary<string, string>? settings = null, string? connectionString = null, Action<IServiceCollection>? configureServices = null)
    {
        _cs = connectionString ?? pg.ConnectionString;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Test");
            b.UseSetting("ConnectionStrings:Default", _cs);
            b.UseSetting("Auth:Jwt:SigningKey", "integration-test-signing-key-0123456789-abc");
            b.UseSetting("Auth:EmailCode:Pepper", "integration-test-pepper-0123456789-abcdef");
            b.UseSetting("Email:Acs:ConnectionString", "endpoint=https://x.communication.azure.com/;accesskey=abc");
            b.UseSetting("Email:SenderAddress", "noreply@example.test");
            foreach (var (k, v) in settings ?? new Dictionary<string, string>()) b.UseSetting(k, v);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<TimeProvider>(Clock);
                s.AddSingleton<IBlobStore>(Blobs);
                configureServices?.Invoke(s);
            });
        });
    }

    public IServiceProvider Services => _factory.Services;

    public IServiceScope Scope() => _factory.Services.CreateScope();

    public HttpClient Anonymous() => _factory.CreateClient();

    /// <summary>A client with its own cookie jar; redirects are not followed unless asked.</summary>
    public HttpClient NewClient(bool allowAutoRedirect = false) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = allowAutoRedirect });

    public VelaDbContext Db()
    {
        var o = new DbContextOptionsBuilder<VelaDbContext>();
        o.ConfigureVela(_cs);
        return new VelaDbContext(o.Options);
    }

    public async Task<TestUser> SignIn(string? email = null)
    {
        var client = _factory.CreateClient();
        var sub = "p-" + Guid.NewGuid();
        var token = email is null ? $"fake:{sub}" : $"fake:{sub}:{email}";
        var r = await client.PostAsJsonAsync("/v1/auth/sso", new { provider = "fake", idToken = token, nonce = "n" });
        var data = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data.GetProperty("accessToken").GetString());
        await using var db = Db();
        var uid = (await db.Identities.SingleAsync(i => i.Subject == sub)).UserId;
        return new TestUser(client, uid);
    }

    public async Task<TestUser> SignInOnboarded(string handle, string name = "Test Artist")
    {
        var u = await SignIn();
        var r = await u.Onboard(handle, name);
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return u;
    }

    public static string UniqueHandle(string prefix = "u") => (prefix + Guid.NewGuid().ToString("N"))[..18];

    public void Dispose() => _factory.Dispose();
}
