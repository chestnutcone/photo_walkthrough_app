using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Account.AccountKit;
using static Vela.Api.Tests.Account.ExportKit;

namespace Vela.Api.Tests.Account;

[Collection(PostgresCollection.Name)]
public class ExportLimitsTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];

    private async Task<(ProfileTestHost Host, World World, string Cs)> Seeded()
    {
        var cs = await pg.CreateIsolatedDatabaseAsync();
        var h = NewHost(pg, cs);
        _hosts.Add(h);
        return (h, await Seed(h), cs);
    }

    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    [Fact]
    public async Task Export_second_request_same_day_is_refused()
    {
        var (h, w, _) = await Seeded();
        Assert.Equal(HttpStatusCode.OK, (await Request(w.U)).Status);
        h.Clock.Advance(TimeSpan.FromHours(2));
        await Renew(h, w.U);

        var response = await w.U.Client.PostAsJsonAsync("/v1/me/export", new { });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var wait = int.Parse(response.Headers.GetValues("Retry-After").Single());
        Assert.InRange(wait, 22 * 3600 - 5, 22 * 3600 + 5);
        Assert.Contains("rate_limited", await response.Content.ReadAsStringAsync());
        h.Clock.Advance(TimeSpan.FromHours(22).Add(TimeSpan.FromSeconds(1)));
        await Renew(h, w.U);
        Assert.Equal(HttpStatusCode.OK, (await Request(w.U)).Status);
    }

    [Fact]
    public async Task The_limit_is_per_user_and_a_failed_export_does_not_count()
    {
        var (h, w, cs) = await Seeded();
        h.Blobs.FailWrites = n => n.StartsWith("exports/");
        var first = (await Request(w.U)).Data.GetProperty("id").GetGuid();
        await RunWorker(h);

        var failed = await w.U.Get($"/v1/me/export/{first}");
        Assert.Equal("failed", failed.Data.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, failed.Data.GetProperty("downloadUrl").ValueKind);
        h.Blobs.FailWrites = null;

        Assert.Equal(HttpStatusCode.OK, (await Request(w.U)).Status); // retry right away
        Assert.Equal(HttpStatusCode.OK, (await Request(w.O)).Status); // someone else is unaffected
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Request(w.U)).Status);
        Assert.Equal(1, await Count(cs, "export_jobs", "id", first));
    }

    [Fact]
    public async Task Export_download_link_lives_at_most_15_minutes_and_expires_with_the_export()
    {
        var (h, w, _) = await Seeded();
        var (id, _) = await RequestAndBuild(h, w.U);
        var url = (await w.U.Get($"/v1/me/export/{id}")).Data.GetProperty("downloadUrl").GetString()!;
        Assert.True(int.Parse(System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["ttl"]!) <= 900);

        h.Clock.Advance(TimeSpan.FromDays(7).Add(TimeSpan.FromMinutes(1)));
        await Renew(h, w.U);
        var expired = await w.U.Get($"/v1/me/export/{id}");

        Assert.Equal("expired", expired.Data.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, expired.Data.GetProperty("downloadUrl").ValueKind);
    }

    [Fact]
    public async Task The_worker_deletes_expired_archives_and_marks_the_job_expired()
    {
        var (h, w, cs) = await Seeded();
        var (id, _) = await RequestAndBuild(h, w.U);
        var blob = $"exports/{w.U.Id}/{id}.zip";
        Assert.True(h.Blobs.Exists(blob));

        h.Clock.Advance(TimeSpan.FromDays(7).Add(TimeSpan.FromMinutes(1)));
        await RunWorker(h);
        await Renew(h, w.U);

        Assert.False(h.Blobs.Exists(blob));
        Assert.Equal("expired", (await Query(cs, $"SELECT status FROM export_jobs WHERE id = '{id}'"))[0][0]);
        Assert.Equal("expired", (await w.U.Get($"/v1/me/export/{id}")).Data.GetProperty("status").GetString());
        Assert.True(h.Blobs.Exists($"exports/{w.U.Id}/seed.zip")); // an unrelated blob is untouched
    }

    [Fact]
    public async Task Another_users_export_and_a_missing_one_look_identical()
    {
        var (h, w, _) = await Seeded();
        var mine = (await Request(w.U)).Data.GetProperty("id").GetGuid();

        var theirs = await w.O.Get($"/v1/me/export/{mine}");
        var missing = await w.O.Get($"/v1/me/export/{Guid.NewGuid()}");
        var garbage = await w.O.Get("/v1/me/export/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, theirs.Status);
        Assert.Equal("export_not_found", theirs.ErrorCode);
        Assert.Equal(missing.Body.GetRawText(), theirs.Body.GetRawText());
        Assert.Equal(missing.Body.GetRawText(), garbage.Body.GetRawText());
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Anonymous().GetAsync($"/v1/me/export/{mine}")).StatusCode);
    }
}
