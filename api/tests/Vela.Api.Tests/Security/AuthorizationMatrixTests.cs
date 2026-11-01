using System.Net;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Security.SecurityWorld;
using static Vela.Api.Tests.Works.WorkTestKit;

namespace Vela.Api.Tests.Security;

/// <summary>How an operation decides who may call it. One entry per contract operation id: adding an operation without a decision fails the build of the matrix.</summary>
public enum Access
{
    /// <summary>No bearer token needed. The credential, if any, travels in the body (refresh token, email code, identity token).</summary>
    Public,
    /// <summary>Signed in; acts only on the caller's own account, so there is no other user's id to reach.</summary>
    SelfScoped,
    /// <summary>Signed in; may act on other users by design (follow, report, reply, inquire) within server-side rules.</summary>
    AnyUser,
    /// <summary>Signed in; the id in the path belongs to one user and everyone else gets 403 or 404.</summary>
    OwnerOnly,
}

[Collection(PostgresCollection.Name)]
public class AuthorizationMatrixTests(PostgresFixture pg)
{
    /// <summary>The decision for every operation in contract/openapi.yaml. Keep this list in step with the contract.</summary>
    public static readonly IReadOnlyDictionary<string, Access> Decisions = new Dictionary<string, Access>
    {
        ["ssoSignIn"] = Access.Public, ["emailRequest"] = Access.Public, ["emailVerify"] = Access.Public,
        ["refresh"] = Access.Public, ["logout"] = Access.Public,
        ["getMe"] = Access.SelfScoped, ["deleteAccount"] = Access.SelfScoped, ["completeOnboarding"] = Access.SelfScoped,
        ["updateProfile"] = Access.SelfScoped, ["avatarUpload"] = Access.SelfScoped, ["requestExport"] = Access.SelfScoped,
        ["getExport"] = Access.OwnerOnly,
        ["suggestedArtists"] = Access.Public, ["getArtist"] = Access.Public, ["listArtistWorks"] = Access.Public,
        ["follow"] = Access.AnyUser, ["unfollow"] = Access.AnyUser, ["listFollowers"] = Access.AnyUser, ["sendInquiry"] = Access.AnyUser,
        ["getFeed"] = Access.Public, ["search"] = Access.Public,
        ["createDraft"] = Access.SelfScoped,
        ["getWork"] = Access.Public, ["updateWork"] = Access.OwnerOnly, ["deleteWork"] = Access.OwnerOnly, ["publishWork"] = Access.OwnerOnly,
        ["listReplies"] = Access.Public, ["addReply"] = Access.AnyUser, ["deleteReply"] = Access.OwnerOnly,
        ["listNotifications"] = Access.SelfScoped, ["unreadCount"] = Access.SelfScoped, ["markRead"] = Access.SelfScoped,
        ["report"] = Access.AnyUser, ["block"] = Access.AnyUser, ["unblock"] = Access.AnyUser,
        ["submitDmcaNotice"] = Access.Public, ["submitCounterNotice"] = Access.OwnerOnly,
    };

    public static IEnumerable<object[]> NonPublicOperationIds() =>
        SecurityContract.Operations.Where(o => !o.IsPublic).Select(o => new object[] { o.OperationId });

    public static IEnumerable<object[]> OwnerOnlyOperationIds() =>
        Decisions.Where(d => d.Value == Access.OwnerOnly).Select(d => new object[] { d.Key });

    private Task<SecurityWorld> World() => SharedAsync(pg);

    [Fact]
    public void Every_contract_operation_has_an_explicit_security_decision_that_matches_the_contract()
    {
        var ids = SecurityContract.Operations.Select(o => o.OperationId).ToHashSet();
        Assert.True(ids.Count >= 37, "the contract lost operations");
        Assert.Empty(ids.Except(Decisions.Keys));            // a new operation must be classified here
        Assert.Empty(Decisions.Keys.Except(ids));            // a removed operation must leave the map
        foreach (var op in SecurityContract.Operations)
            Assert.Equal(op.IsPublic, Decisions[op.OperationId] == Access.Public);
    }

    [Theory]
    [MemberData(nameof(NonPublicOperationIds))]
    public async Task Every_non_public_operation_returns_401_without_token(string operationId)
    {
        var w = await World();
        var op = SecurityContract.Find(operationId);

        var (status, text, response) = await SendAsync(w.Anonymous, op.Method, op.Url(w.AliceWork), op.IsWrite ? "{}" : null);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("unauthorized", ErrorCode(text));
        Assert.Contains("Bearer", string.Join(",", response.Headers.WwwAuthenticate)); // RFC 6750
    }

    [Theory]
    [MemberData(nameof(NonPublicOperationIds))]
    public async Task Every_non_public_operation_rejects_garbage_forged_expired_and_misdirected_tokens(string operationId)
    {
        var w = await World();
        var op = SecurityContract.Find(operationId);
        foreach (var (name, token) in ForgedTokens.All(w.Alice.Id))
        {
            var (status, text, _) = await SendAsync(w.ClientWithToken(token), op.Method, op.Url(w.AliceWork), op.IsWrite ? "{}" : null);
            Assert.True(status == HttpStatusCode.Unauthorized, $"{operationId} accepted a {name} token ({(int)status})");
            Assert.Equal("unauthorized", ErrorCode(text));
        }
    }

    [Theory]
    [MemberData(nameof(OwnerOnlyOperationIds))]
    public async Task Every_owner_only_operation_returns_403_or_404_for_another_user(string operationId)
    {
        var w = await World();
        foreach (var (method, url, body) in OwnerOnlyRequests(w, operationId))
        {
            var (status, text, _) = await SendAsync(w.Bob.Client, method, url, body);
            Assert.True(status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"{operationId} {method} {url} answered {(int)status}: {text}");
            Assert.False(JsonSuccess(text), $"{operationId} reported success to a user who does not own the resource");
        }
    }

    [Fact]
    public async Task Every_owner_only_operation_has_a_request_in_this_matrix()
    {
        var w = await World();
        foreach (var id in Decisions.Where(d => d.Value == Access.OwnerOnly).Select(d => d.Key))
            Assert.NotEmpty(OwnerOnlyRequests(w, id));
    }

    [Fact]
    public async Task Another_users_attempts_leave_the_owners_drafts_works_replies_and_exports_untouched()
    {
        var w = await World();
        foreach (var id in Decisions.Where(d => d.Value == Access.OwnerOnly).Select(d => d.Key))
            foreach (var (method, url, body) in OwnerOnlyRequests(w, id))
                await SendAsync(w.Bob.Client, method, url, body);

        await using var db = w.Host.Db();
        var draft = await db.Works.AsNoTracking().SingleAsync(x => x.Id == w.AliceDraft);
        Assert.Equal("Alice draft", draft.Title);
        Assert.Equal(Vela.Api.Data.Entities.WorkStatus.Draft, draft.Status);
        var work = await db.Works.AsNoTracking().SingleAsync(x => x.Id == w.AliceWork);
        Assert.Equal("Alice published story", work.Title);
        Assert.Equal(Vela.Api.Data.Entities.WorkStatus.Published, work.Status);
        Assert.True(await db.Replies.AnyAsync(r => r.Id == w.AliceReply));
        Assert.True(await db.ExportJobs.AnyAsync(e => e.Id == w.AliceExport));
    }

    [Fact]
    public async Task Drafts_are_invisible_to_everyone_but_their_owner()
    {
        var w = await World();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(w.Alice.Client, HttpMethod.Get, $"/v1/works/{w.AliceDraft}")).Status);
        foreach (var client in new[] { w.Anonymous, w.Bob.Client })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/v1/works/{w.AliceDraft}")).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/v1/works/{w.AliceDraft}/chapters/0/replies")).Status);
        }
        var share = await SendAsync(w.Anonymous, HttpMethod.Get, $"/s/{w.AliceDraft}");
        Assert.Equal(HttpStatusCode.NotFound, share.Status);
        var reply = await SendAsync(w.Bob.Client, HttpMethod.Post, $"/v1/works/{w.AliceDraft}/chapters/0/replies", "{\"body\":\"hi\"}");
        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
    }

    [Fact]
    public async Task Marking_notifications_read_only_touches_the_callers_own_rows()
    {
        var w = await SecurityWorld.CreateAsync(pg);
        var bob = await w.NewUserAsync();
        var mine = await w.Alice.Get("/v1/notifications");
        var aliceIds = mine.Data.EnumerateArray().Select(n => n.GetProperty("id").GetGuid()).ToList();
        Assert.NotEmpty(aliceIds);

        var r = await bob.Post("/v1/notifications/read", new { ids = aliceIds });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        var all = await bob.Post("/v1/notifications/read", new { all = true });
        Assert.Equal(HttpStatusCode.OK, all.Status);

        var after = await w.Alice.Get("/v1/notifications/unread-count");
        Assert.True(after.Data.GetProperty("count").GetInt32() >= 1, "Bob marked Alice's notifications as read");
        w.Dispose();
    }

    [Fact]
    public async Task An_inquiry_cannot_reference_a_story_that_is_not_the_artists_published_story()
    {
        var w = await World();
        var carol = await w.NewUserAsync();
        var carolWork = await Vela.Api.Tests.Share.ShareTestKit.Published(w.Host, carol, "Carol story");
        var body = new { type = "general", email = "bob@example.test", message = "Hello there" };

        var foreign = await w.Bob.Post($"/v1/artists/{w.Alice.Id}/inquiries", new { body.type, body.email, body.message, workId = carolWork });
        var draft = await w.Bob.Post($"/v1/artists/{w.Alice.Id}/inquiries", new { body.type, body.email, body.message, workId = w.AliceDraft });
        var self = await w.Alice.Post($"/v1/artists/{w.Alice.Id}/inquiries", body);

        Assert.Equal(HttpStatusCode.BadRequest, foreign.Status);
        Assert.Equal(HttpStatusCode.BadRequest, draft.Status);
        Assert.Equal(HttpStatusCode.BadRequest, self.Status);
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("deleted")]
    public async Task Suspended_and_deleted_accounts_are_refused_on_every_non_public_operation(string state)
    {
        var w = await SecurityWorld.CreateAsync(pg);
        var victim = await w.NewUserAsync();
        await using (var db = w.Host.Db())
        {
            var q = db.Users.Where(u => u.Id == victim.Id);
            if (state == "suspended") await q.ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, Vela.Api.Tests.Profiles.ProfileTestHost.Now));
            else await q.ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, Vela.Api.Tests.Profiles.ProfileTestHost.Now));
        }

        foreach (var op in SecurityContract.Operations.Where(o => !o.IsPublic))
        {
            var (status, text, _) = await SendAsync(victim.Client, op.Method, op.Url(w.AliceWork), op.IsWrite ? "{}" : null);
            Assert.True(status == HttpStatusCode.Forbidden, $"{op.OperationId} let a {state} account through ({(int)status})");
            Assert.Equal($"account_{state}", ErrorCode(text));
        }
        w.Dispose();
    }

    [Fact]
    public async Task A_suspended_accounts_refresh_token_is_refused_and_signing_in_again_is_forbidden()
    {
        var w = await SecurityWorld.CreateAsync(pg);
        var sub = "p-" + Guid.NewGuid();
        var first = await SendAsync(w.Anonymous, HttpMethod.Post, "/v1/auth/sso", $"{{\"provider\":\"fake\",\"idToken\":\"fake:{sub}\",\"nonce\":\"n\"}}");
        var refresh = System.Text.Json.JsonDocument.Parse(first.Text).RootElement.GetProperty("data").GetProperty("refreshToken").GetString();
        await using (var db = w.Host.Db())
        {
            var uid = (await db.Identities.SingleAsync(i => i.Subject == sub)).UserId;
            await db.Users.Where(u => u.Id == uid).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, Vela.Api.Tests.Profiles.ProfileTestHost.Now));
        }

        var again = await SendAsync(w.Anonymous, HttpMethod.Post, "/v1/auth/refresh", $"{{\"refreshToken\":\"{refresh}\"}}");
        var signIn = await SendAsync(w.Anonymous, HttpMethod.Post, "/v1/auth/sso", $"{{\"provider\":\"fake\",\"idToken\":\"fake:{sub}\",\"nonce\":\"n\"}}");

        Assert.Equal(HttpStatusCode.Unauthorized, again.Status);
        Assert.Equal(HttpStatusCode.Forbidden, signIn.Status);
        Assert.Equal("account_suspended", ErrorCode(signIn.Text));
        w.Dispose();
    }

    /// <summary>The requests another user could try against Alice's resources for one owner-only operation.</summary>
    private static IReadOnlyList<(HttpMethod Method, string Url, string? Body)> OwnerOnlyRequests(SecurityWorld w, string operationId)
    {
        var update = System.Text.Json.JsonSerializer.Serialize(Update("Hijacked"));
        return operationId switch
        {
            "getExport" => [(HttpMethod.Get, $"/v1/me/export/{w.AliceExport}", null)],
            "updateWork" => [(HttpMethod.Put, $"/v1/works/{w.AliceDraft}", update), (HttpMethod.Put, $"/v1/works/{w.AliceWork}", update)],
            "deleteWork" => [(HttpMethod.Delete, $"/v1/works/{w.AliceDraft}", null), (HttpMethod.Delete, $"/v1/works/{w.AliceWork}", null)],
            "publishWork" => [(HttpMethod.Post, $"/v1/works/{w.AliceDraft}/publish", null)],
            "deleteReply" => [(HttpMethod.Delete, $"/v1/replies/{w.AliceReply}", null)],
            "submitCounterNotice" => [(HttpMethod.Post, $"/v1/works/{w.AliceWork}/counter-notice",
                "{\"name\":\"Bob\",\"address\":\"1 Road\",\"statement\":\"It is mine\",\"consentToJurisdiction\":true,\"goodFaithBelief\":true}")],
            _ => [],
        };
    }

    private static bool JsonSuccess(string text)
    {
        try { return System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("success").GetBoolean(); }
        catch (Exception e) when (e is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException) { return false; }
    }
}
