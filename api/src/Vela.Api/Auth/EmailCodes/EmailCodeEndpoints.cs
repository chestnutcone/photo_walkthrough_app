using Microsoft.EntityFrameworkCore;
using Vela.Api.Abuse;
using Vela.Api.Auth.Identity;
using Vela.Api.Common;
using Vela.Api.Data;

namespace Vela.Api.Auth.EmailCodes;

public record EmailRequestBody(string? Email);
public record EmailVerifyBody(string? Email, string? Code);

public static class EmailCodeEndpoints
{
    public static IServiceCollection AddEmailCodes(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.AddSingleton(EmailCodeSettings.Load(config, env));
        services.AddScoped<IEmailCodeService, EmailCodeService>();
        services.AddSingleton<AuthDataRetentionJob>();
        // Tests run the sweep by hand against a controllable clock.
        if (!env.IsEnvironment("Test")) services.AddHostedService(sp => sp.GetRequiredService<AuthDataRetentionJob>());
        // Literal names on purpose: only Development prints codes and only Test keeps an outbox.
        switch (env.EnvironmentName)
        {
            case "Development":
                services.AddSingleton<IEmailSender, ConsoleEmailSender>();
                break;
            case "Test":
                services.AddSingleton<FakeEmailSender>();
                services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<FakeEmailSender>());
                break;
            default:
                services.AddSingleton<IEmailSender>(new AcsEmailSender(AcsEmailOptions.Load(config)));
                break;
        }
        return services;
    }

    public static IEndpointRouteBuilder MapEmailCodeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/auth/email/request", RequestAsync).RequireAuthEmailLimit().RequireAttestation("email_request").WithName("emailRequest");
        app.MapPost("/v1/auth/email/verify", VerifyAsync).WithName("emailVerify");
        return app;
    }

    private static async Task<IResult> RequestAsync(EmailRequestBody? req, HttpContext ctx, IEmailCodeService svc, IClientIpResolver ips, DisposableEmailList disposable)
    {
        if (EmailCodeService.Normalize(req?.Email) is { } email && disposable.IsDisposable(email))
            return Results.Fail(StatusCodes.Status400BadRequest, "disposable_email", "Use a permanent email address.");
        try
        {
            await svc.RequestAsync(req?.Email ?? "", ips.Resolve(ctx).ToString());
            return Results.Ok<object?>(null);
        }
        catch (InvalidEmailException)
        {
            return Results.Fail(StatusCodes.Status400BadRequest, "invalid_email", "Enter a valid email address.");
        }
        catch (RateLimitedException e)
        {
            return RateLimited(ctx, e);
        }
    }

    private static async Task<IResult> VerifyAsync(EmailVerifyBody? req, HttpContext ctx, IEmailCodeService svc,
        IClientIpResolver ips, VelaDbContext db, TimeProvider clock)
    {
        try
        {
            var r = await svc.VerifyAsync(req?.Email ?? "", req?.Code ?? "", ips.Resolve(ctx).ToString());
            var onboarded = !r.IsNew && await db.Users.AnyAsync(u => u.Id == r.UserId && u.TermsAcceptedAt != null && u.AgeConfirmedAt != null);
            var expiresIn = (int)Math.Round((r.Pair.AccessExpiresAt - clock.GetUtcNow()).TotalSeconds);
            return Results.Ok(new TokenPairResponse(r.Pair.AccessToken, r.Pair.RefreshToken, expiresIn, !onboarded));
        }
        catch (RateLimitedException e)
        {
            return RateLimited(ctx, e);
        }
        catch (InvalidCodeException)
        {
            return Results.Fail(StatusCodes.Status400BadRequest, "invalid_code", "The code is invalid or has expired.");
        }
        catch (AccountSuspendedException)
        {
            return Results.Fail(StatusCodes.Status403Forbidden, "account_suspended", "This account is suspended.");
        }
        catch (AccountDeletedException)
        {
            return Results.Fail(StatusCodes.Status403Forbidden, "account_deleted", "This account has been deleted.");
        }
    }

    private static IResult RateLimited(HttpContext ctx, RateLimitedException e)
    {
        ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling(e.RetryAfter.TotalSeconds)).ToString();
        return Results.Fail(StatusCodes.Status429TooManyRequests, "rate_limited", "Too many requests. Try again later.");
    }
}
