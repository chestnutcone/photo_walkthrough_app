using Vela.Api.Auth;
using Vela.Api.Common;
using Vela.Api.Social;

namespace Vela.Api.Account;

public static class AccountEndpoints
{
    public static IServiceCollection AddAccount(this IServiceCollection services, IHostEnvironment env)
    {
        services.AddScoped<IAccountDeletionService, AccountDeletionService>().AddScoped<DeletionJob>()
            .AddScoped<IExportService, ExportService>().AddScoped<ExportData>().AddScoped<ExportArchiveBuilder>()
            .AddSingleton<ICdnPurger, NoOpCdnPurger>().AddSingleton<IAppleTokenRevoker, LoggingAppleTokenRevoker>()
            .AddSingleton<IDeletionStepObserver, NoOpDeletionStepObserver>().AddSingleton<DeletionWorker>().AddSingleton<ExportWorker>();
        // Tests drive the sweeps by hand with a controllable clock; a real timer would race them.
        if (!env.IsEnvironment("Test"))
            services.AddHostedService(sp => sp.GetRequiredService<DeletionWorker>()).AddHostedService(sp => sp.GetRequiredService<ExportWorker>());
        return services;
    }

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/v1/me", async (ICurrentUser me, IAccountDeletionService svc) =>
            Common.Results.Ok(await svc.RequestAsync(me.UserId!.Value))).RequireAuthorization().WithName("deleteAccount");

        app.MapPost("/v1/me/export", (HttpContext ctx, ICurrentUser me, IExportService svc) =>
            Run(ctx, async () =>
            {
                var id = await svc.RequestAsync(me.UserId!.Value);
                return Common.Results.Ok(await svc.GetAsync(me.UserId.Value, id));
            })).RequireAuthorization().WithName("requestExport");

        app.MapGet("/v1/me/export/{id}", (string id, HttpContext ctx, ICurrentUser me, IExportService svc) =>
            Run(ctx, async () =>
            {
                var exportId = Guid.TryParse(id, out var g) ? g : throw ExportService.NotFound();
                return Common.Results.Ok(await svc.GetAsync(me.UserId!.Value, exportId));
            })).RequireAuthorization().WithName("getExport");
        return app;
    }

    private static async Task<IResult> Run(HttpContext ctx, Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (SafetyException e)
        {
            if (e.RetryAfter is { } wait) ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
            return Common.Results.Fail(e.Status, e.Code, e.Message);
        }
    }
}
