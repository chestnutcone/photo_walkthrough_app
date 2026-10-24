using System.Net;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Security.SecurityWorld;

namespace Vela.Api.Tests.Security;

/// <summary>Every write operation sits under the global write rate limit and the 1 MB body cap, and the abuse-prone ones carry their extra policy.</summary>
[Collection(PostgresCollection.Name)]
public class WriteEndpointLimitTests(PostgresFixture pg)
{
    public static IEnumerable<object[]> WriteOperationIds() =>
        SecurityContract.Operations.Where(o => o.IsWrite).Select(o => new object[] { o.OperationId });

    /// <summary>Operations that need a stricter policy than the global write limit, by name.</summary>
    private static readonly Dictionary<string, string> ExtraPolicy = new()
    {
        ["emailRequest"] = "auth-email", ["createDraft"] = "upload", ["avatarUpload"] = "upload", ["sendInquiry"] = "public-form",
    };

    [Fact]
    public async Task Abuse_prone_operations_carry_their_named_rate_limit_policy()
    {
        var w = await SharedAsync(pg);
        var endpoints = w.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .ToDictionary(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? e.DisplayName ?? "", e => e);
        foreach (var (op, policy) in ExtraPolicy)
            Assert.Equal(policy, endpoints[op].Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Contains("submitDmcaNotice", SecurityContract.Operations.Select(o => o.OperationId)); // limited by the global write policy per IP
    }

    [Theory]
    [MemberData(nameof(WriteOperationIds))]
    public async Task Every_write_operation_rejects_a_body_over_1_MB(string operationId)
    {
        var w = await SharedAsync(pg);
        var op = SecurityContract.Find(operationId);
        var client = op.IsPublic ? w.Anonymous : w.Bob.Client;

        var huge = "{\"x\":\"" + new string('a', 1024 * 1024 + 10) + "\"}";
        var declared = await SendAsync(client, op.Method, op.Url(w.AliceWork), huge);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, declared.Status);
        Assert.Equal("payload_too_large", ErrorCode(declared.Text));

        // Chunked: no Content-Length to check up front.
        var req = new HttpRequestMessage(op.Method, op.Url(w.AliceWork)) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(huge))) };
        req.Content.Headers.ContentLength = null;
        req.Headers.TransferEncodingChunked = true;
        var chunked = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, chunked.StatusCode);
    }

    [Fact]
    public async Task Every_write_operation_is_rate_limited_per_caller()
    {
        var settings = new Dictionary<string, string>(Settings)
        {
            ["Abuse:RateLimits:write:Permit"] = "2",
            ["Abuse:RateLimits:upload:Permit"] = "1000", ["Abuse:RateLimits:auth-email:Permit"] = "1000", ["Abuse:RateLimits:public-form:Permit"] = "1000",
        };
        using var host = await Vela.Api.Tests.Abuse.AbuseKit.Host(pg, settings);
        var misses = new List<string>();
        foreach (var op in SecurityContract.Operations.Where(o => o.IsWrite))
        {
            // The first call deletes the account, so a repeat is refused at authentication and never meets the limiter. The
            // classification is path-independent (every non-GET is a write), which the other operations demonstrate.
            if (op.OperationId == "deleteAccount") continue;
            var ip = Vela.Api.Tests.Abuse.AbuseKit.NewIp();
            HttpClient caller;
            if (op.IsPublic) caller = host.Anonymous();
            else
            {
                // Sign in from a throwaway address so this caller's own bucket is untouched.
                var anon = host.Anonymous();
                var sub = "p-" + Guid.NewGuid();
                var signIn = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/sso")
                    { Content = new StringContent($"{{\"provider\":\"fake\",\"idToken\":\"fake:{sub}\",\"nonce\":\"n\"}}", Encoding.UTF8, "application/json") };
                signIn.Headers.Add("X-Test-Ip", Vela.Api.Tests.Abuse.AbuseKit.NewIp());
                var token = System.Text.Json.JsonDocument.Parse(await (await anon.SendAsync(signIn)).Content.ReadAsStringAsync())
                    .RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
                caller = host.Anonymous();
                caller.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
            var codes = new List<HttpResponseMessage>();
            for (var i = 0; i < 3; i++)
            {
                var req = new HttpRequestMessage(op.Method, op.Url(Guid.NewGuid())) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                req.Headers.Add("X-Test-Ip", ip);
                codes.Add(await caller.SendAsync(req));
            }
            // requestExport also has its own once-a-day rule that answers 429 on the second call: still rate limited, just earlier.
            var ok = op.OperationId == "requestExport" ? codes.Any(c => c.StatusCode == HttpStatusCode.TooManyRequests) : codes[0].StatusCode != HttpStatusCode.TooManyRequests && codes[1].StatusCode != HttpStatusCode.TooManyRequests
                && codes[2].StatusCode == HttpStatusCode.TooManyRequests && codes[2].Headers.Contains("Retry-After")
                && ErrorCode(await codes[2].Content.ReadAsStringAsync()) == "rate_limited";
            if (!ok) misses.Add($"{op.OperationId}: {string.Join(",", codes.Select(c => (int)c.StatusCode))}");
        }
        Assert.True(misses.Count == 0, "Write operations not under the write limit: " + string.Join("; ", misses));
    }
}
