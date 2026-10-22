namespace Vela.Api.Stubs;

/// <summary>
/// Registers every contract operation that has no real handler yet as a 501 not_implemented
/// stub named by its operationId. A task that implements an operation maps the real endpoint
/// (with the same <c>.WithName(operationId)</c>) and deletes that operation's line below.
/// </summary>
public static class ContractStubs
{
    private static readonly (string Method, string Route, string OperationId)[] Operations =
    [
        ("GET", "/v1/me", "getMe"),
        ("POST", "/v1/me/onboarding", "completeOnboarding"),
        ("PATCH", "/v1/me/profile", "updateProfile"),
        ("POST", "/v1/me/avatar-upload", "avatarUpload"),
        ("DELETE", "/v1/me", "deleteAccount"),
        ("POST", "/v1/me/export", "requestExport"),
        ("GET", "/v1/me/export/{id}", "getExport"),
        ("GET", "/v1/artists/suggested", "suggestedArtists"),
        ("GET", "/v1/artists/{handle}", "getArtist"),
        ("GET", "/v1/artists/{handle}/works", "listArtistWorks"),
        ("PUT", "/v1/artists/{id}/follow", "follow"),
        ("DELETE", "/v1/artists/{id}/follow", "unfollow"),
        ("GET", "/v1/artists/{id}/followers", "listFollowers"),
        ("POST", "/v1/artists/{id}/inquiries", "sendInquiry"),
        ("GET", "/v1/feed", "getFeed"),
        ("POST", "/v1/works", "createDraft"),
        ("GET", "/v1/works/{id}", "getWork"),
        ("PUT", "/v1/works/{id}", "updateWork"),
        ("POST", "/v1/works/{id}/publish", "publishWork"),
        ("DELETE", "/v1/works/{id}", "deleteWork"),
        ("GET", "/v1/works/{id}/chapters/{index}/replies", "listReplies"),
        ("POST", "/v1/works/{id}/chapters/{index}/replies", "addReply"),
        ("DELETE", "/v1/replies/{id}", "deleteReply"),
        ("GET", "/v1/notifications", "listNotifications"),
        ("GET", "/v1/notifications/unread-count", "unreadCount"),
        ("POST", "/v1/notifications/read", "markRead"),
        ("GET", "/v1/search", "search"),
        ("POST", "/v1/reports", "report"),
        ("PUT", "/v1/blocks/{userId}", "block"),
        ("DELETE", "/v1/blocks/{userId}", "unblock"),
    ];

    public static IEndpointRouteBuilder MapContractStubs(this IEndpointRouteBuilder app)
    {
        foreach (var (method, route, operationId) in Operations)
        {
            app.MapMethods(route, [method], () => Results.Fail(StatusCodes.Status501NotImplemented, "not_implemented", "This operation is not implemented yet."))
                .WithName(operationId);
        }

        return app;
    }
}
