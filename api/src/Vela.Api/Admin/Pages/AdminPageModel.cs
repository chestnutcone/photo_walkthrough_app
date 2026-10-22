using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Vela.Api.Admin.Pages;

/// <summary>Base for the signed-in admin pages: the acting admin and the one-word flash messages shown after a redirect.</summary>
public abstract class AdminPageModel : PageModel
{
    private static readonly Dictionary<string, (string Text, bool Error)> Flashes = new()
    {
        ["ok"] = ("Done.", false),
        ["reason_required"] = ("A reason is required (up to 500 characters).", true),
        ["reason_too_long"] = ("The reason is too long (500 characters at most).", true),
        ["not_found"] = ("That item no longer exists.", true),
        ["not_applicable"] = ("That action does not apply to this item.", true),
        ["forbidden"] = ("You are not allowed to do that.", true),
        ["cannot_ban_self"] = ("You cannot ban your own account.", true),
        ["cannot_ban_admin"] = ("An admin account cannot be banned.", true),
        ["still_blocked"] = ("This content is still blocked: restore it or remove it instead of dismissing.", true),
        ["variants_unavailable"] = ("The original image could not be read, so the story was not restored.", true),
        ["already_resolved"] = ("That item was already resolved.", true),
    };

    public Guid AdminId => AdminAuth.UserId(User) ?? Guid.Empty;

    public (string Text, bool Error)? Flash =>
        Request.Query.TryGetValue("flash", out var code) && Flashes.TryGetValue(code.ToString(), out var f) ? f : null;

    protected static string FlashCode(ModerationOutcome outcome) => outcome switch
    {
        ModerationOutcome.Ok => "ok",
        ModerationOutcome.ReasonRequired => "reason_required",
        ModerationOutcome.ReasonTooLong => "reason_too_long",
        ModerationOutcome.NotFound => "not_found",
        ModerationOutcome.NotApplicable => "not_applicable",
        ModerationOutcome.CannotBanSelf => "cannot_ban_self",
        ModerationOutcome.CannotBanAdmin => "cannot_ban_admin",
        ModerationOutcome.TargetStillBlocked => "still_blocked",
        ModerationOutcome.VariantsUnavailable => "variants_unavailable",
        ModerationOutcome.AlreadyResolved => "already_resolved",
        _ => "forbidden",
    };
}
