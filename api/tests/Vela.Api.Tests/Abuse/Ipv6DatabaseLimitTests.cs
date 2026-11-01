using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Abuse;
using Vela.Api.Auth.EmailCodes;
using Vela.Api.Common;
using Vela.Api.Legal;
using Vela.Api.Tests.Admin;
using Vela.Api.Tests.Legal;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Abuse;

/// <summary>
/// S1b-5: every database-backed per-address limit keys IPv6 by its /64 (the same <see cref="IpKey"/> the in-memory limiter uses),
/// so a host cannot rotate through its own prefix to reset a limit; the DMCA acknowledgement cap counts one mailbox however it is spelt.
/// </summary>
[Collection(PostgresCollection.Name)]
public class Ipv6DatabaseLimitTests(PostgresFixture pg)
{
    /// <summary>A distinct address inside 2001:db8:1:2::/64 for each <paramref name="i"/>.</summary>
    private static string InPrefix(int i) => $"2001:db8:1:2:{i:x}::{i + 1:x}";

    [Fact]
    public void Addresses_in_one_slash_64_share_a_key_and_text_that_is_not_an_address_is_kept_as_is()
    {
        Assert.Equal(IpKey.Of("2001:db8:1:2::1"), IpKey.Of("2001:db8:1:2:ffff:ffff:ffff:ffff"));
        Assert.NotEqual(IpKey.Of("2001:db8:1:2::1"), IpKey.Of("2001:db8:1:3::1"));
        Assert.Equal("203.0.113.5", IpKey.Of("::ffff:203.0.113.5"));
        Assert.Equal("unknown", IpKey.Of("unknown"));
    }

    [Fact]
    public async Task Email_code_requests_from_one_slash_64_share_the_per_address_limit()
    {
        using var site = await AdminSite.Create(pg, isolated: true);
        using var scope = site.Host.Scope();
        var codes = scope.ServiceProvider.GetRequiredService<IEmailCodeService>();

        for (var i = 0; i < EmailCodeService.MaxRequestsPerIpPerHour; i++)
            await codes.RequestAsync($"user{i}-{Guid.NewGuid():N}@example.test", InPrefix(i));

        await Assert.ThrowsAsync<RateLimitedException>(() => codes.RequestAsync($"late-{Guid.NewGuid():N}@example.test", InPrefix(999)));
        await using var db = site.Host.Db();
        Assert.Single(await db.EmailCodes.Select(c => c.RequestIp).Distinct().ToListAsync()); // stored as the /64 key, not the full address
    }

    [Fact]
    public async Task Email_code_verifies_from_one_slash_64_share_the_per_address_limit()
    {
        using var site = await AdminSite.Create(pg, isolated: true);
        using var scope = site.Host.Scope();
        var codes = scope.ServiceProvider.GetRequiredService<IEmailCodeService>();

        for (var i = 0; i < EmailCodeService.MaxVerifiesPerIpPerHour; i++)
            await Assert.ThrowsAsync<InvalidCodeException>(() => codes.VerifyAsync($"nobody{i}@example.test", "000000", InPrefix(i)));

        await Assert.ThrowsAsync<RateLimitedException>(() => codes.VerifyAsync("nobody@example.test", "000000", InPrefix(999)));
    }

    [Fact]
    public async Task Dmca_notices_from_one_slash_64_share_the_daily_limit()
    {
        var ip = new MutableIp(InPrefix(0));
        using var site = await AdminSite.Create(pg, isolated: true, extra: s => s.AddSingleton<IClientIpResolver>(ip),
            settings: new() { ["Abuse:RateLimits:public-form:Permit"] = "1000" });
        var http = site.Host.Anonymous();

        for (var i = 0; i < DmcaService.MaxPerIpPerDay; i++)
        {
            ip.Ip = InPrefix(i);
            Assert.Equal(HttpStatusCode.Accepted, (await http.PostNotice(DmcaKit.Valid())).Status);
        }
        ip.Ip = InPrefix(77);

        var limited = await http.PostNotice(DmcaKit.Valid());

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Status);
        Assert.Equal("rate_limited", limited.ErrorCode);
    }

    [Fact]
    public async Task The_dmca_form_is_behind_the_public_form_limiter_keyed_by_slash_64()
    {
        var ip = new MutableIp(InPrefix(0));
        using var site = await AdminSite.Create(pg, isolated: true, extra: s => s.AddSingleton<IClientIpResolver>(ip),
            settings: new() { ["Abuse:RateLimits:public-form:Permit"] = "2" });
        var http = site.Host.Anonymous();

        ip.Ip = InPrefix(1);
        Assert.Equal(HttpStatusCode.Accepted, (await http.PostNotice(DmcaKit.Valid())).Status);
        ip.Ip = InPrefix(2);
        Assert.Equal(HttpStatusCode.Accepted, (await http.PostNotice(DmcaKit.Valid())).Status);
        ip.Ip = InPrefix(3);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await http.PostNotice(DmcaKit.Valid())).Status);
    }

    [Fact]
    public async Task Dmca_acknowledgements_count_one_mailbox_across_plus_tags_dots_and_case()
    {
        var ip = new MutableIp("203.0.113.40");
        using var site = await AdminSite.Create(pg, isolated: true, extra: s => s.AddSingleton<IClientIpResolver>(ip),
            settings: new() { ["Abuse:RateLimits:public-form:Permit"] = "1000" });
        var http = site.Host.Anonymous();
        var local = $"victim{Guid.NewGuid():N}"[..20];
        var spellings = new[] { $"{local}@gmail.com", $"{local}+1@gmail.com", $"{local[..3]}.{local[3..]}@gmail.com", $"{local.ToUpperInvariant()}+x@googlemail.com", $"{local}+2@GMAIL.com" };

        for (var i = 0; i < spellings.Length; i++)
        {
            ip.Ip = $"203.0.113.{41 + i}";
            var body = DmcaKit.Valid();
            body["claimantEmail"] = spellings[i];
            Assert.Equal(HttpStatusCode.Accepted, (await http.PostNotice(body)).Status);
        }

        var sent = site.Host.Services.GetRequiredService<FakeEmailSender>().Sent.Count(m => spellings.Contains(m.To));
        Assert.Equal(DmcaService.MaxAcksPerEmailPerDay, sent);
        Assert.Equal(spellings.Length, (await site.Notices()).Count);
    }

    [Fact]
    public void Mailbox_normalisation_strips_tags_and_gmail_dots_only_where_they_are_ignored()
    {
        Assert.Equal("ab@gmail.com", DmcaService.Mailbox("A.b+tag@GoogleMail.com"));
        Assert.Equal("a.b@example.test", DmcaService.Mailbox("A.b+tag@Example.TEST"));
        Assert.Equal("ab@gmail.com", DmcaService.Mailbox("ab@gmail.com"));
    }

    [Fact]
    public async Task Inquiries_from_one_slash_64_share_the_per_address_limit()
    {
        using var site = await AdminSite.Create(pg, isolated: true);
        await using var db = site.Host.Db();
        var key = IpKey.Of(InPrefix(0));
        Assert.Equal(key, IpKey.Of(InPrefix(5)));
        // The service stores the key; a second address in the prefix must therefore match rows written for the first.
        using var scope = site.Host.Scope();
        var svc = scope.ServiceProvider.GetRequiredService<Vela.Api.Inquiries.IInquiryService>();
        var artist = await site.Host.SignInOnboarded(Vela.Api.Tests.Profiles.ProfileTestHost.UniqueHandle());
        var sender = await site.Host.SignInOnboarded(Vela.Api.Tests.Profiles.ProfileTestHost.UniqueHandle());
        await svc.SendAsync(sender.Id, artist.Id, new Vela.Api.Inquiries.InquiryRequest("Commission", "me@example.test", "Hello there, a commission?", null),
            IPAddress.Parse(InPrefix(3)));
        Assert.Equal(key, await db.Inquiries.Where(i => i.SenderId == sender.Id).Select(i => i.ClientIp).SingleAsync());
    }
}
