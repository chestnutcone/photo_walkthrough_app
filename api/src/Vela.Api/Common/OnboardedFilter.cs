using Microsoft.EntityFrameworkCore;
using Vela.Api.Auth;
using Vela.Api.Data;

namespace Vela.Api.Common;

/// <summary>
/// 18+/terms gate for write endpoints that reach other people (FINALAPI M18): an account that signed in but never confirmed
/// it is 18 or over and never accepted the terms cannot mail artists, file reports or block. 403 <c>onboarding_required</c>,
/// the same answer as replies. Drafts and profile edits keep their own 409 check inside the service.
/// </summary>
public static class OnboardedFilter
{
    public const string Code = "onboarding_required";

    public static TBuilder RequireOnboarded<TBuilder>(this TBuilder b) where TBuilder : IEndpointConventionBuilder =>
        b.AddEndpointFilter(async (ctx, next) =>
        {
            var services = ctx.HttpContext.RequestServices;
            if (services.GetRequiredService<ICurrentUser>().UserId is not { } userId) return await next(ctx);
            var db = services.GetRequiredService<VelaDbContext>();
            var onboarded = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId
                && u.AgeConfirmedAt != null && u.TermsAcceptedAt != null && db.Profiles.Any(p => p.UserId == userId));
            return onboarded ? await next(ctx) : Results.Fail(StatusCodes.Status403Forbidden, Code, "Finish onboarding first.");
        });
}
