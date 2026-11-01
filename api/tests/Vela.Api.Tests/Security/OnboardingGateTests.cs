using System.Net;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Security;

/// <summary>FINALAPI M18: an account that never confirmed 18+ and the terms cannot reach other people.</summary>
[Collection(PostgresCollection.Name)]
public class OnboardingGateTests(PostgresFixture pg) : IDisposable
{
    private readonly ProfileTestHost _host = new(pg);
    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task A_signed_in_but_not_onboarded_account_cannot_send_inquiries_report_or_block()
    {
        var artist = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var raw = await _host.SignIn();

        var inquiry = await raw.Post($"/v1/artists/{artist.Id}/inquiries", new { type = "Commission", email = "b@example.test", message = "hi" });
        var report = await raw.Post("/v1/reports", new { targetType = "user", targetId = artist.Id, reason = "spam" });
        var block = await raw.Put($"/v1/blocks/{artist.Id}");

        foreach (var r in new[] { inquiry, report, block })
        {
            Assert.Equal(HttpStatusCode.Forbidden, r.Status);
            Assert.Equal("onboarding_required", r.ErrorCode);
        }
        await using var db = _host.Db();
        Assert.False(await db.Inquiries.AnyAsync(i => i.SenderId == raw.Id));
        Assert.False(await db.Reports.AnyAsync(x => x.ReporterId == raw.Id));
        Assert.False(await db.Blocks.AnyAsync(x => x.BlockerId == raw.Id));
    }

    [Fact]
    public async Task An_onboarded_account_passes_the_gate()
    {
        var artist = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var me = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());

        Assert.Equal(HttpStatusCode.OK, (await me.Put($"/v1/blocks/{artist.Id}")).Status);
    }
}
