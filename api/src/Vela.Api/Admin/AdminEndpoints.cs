using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Vela.Api.Admin;

public static class AdminEndpoints
{
    public static IServiceCollection AddAdmin(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.AddSingleton<AdminReconciler>().AddSingleton<Moderation.TakedownReconciler>();
        if (!env.IsEnvironment("Test")) services.AddHostedService<AdminReconcileWorker>();
        services.AddSingleton(AdminSettings.Load(config, env));
        services.AddSingleton<IAdminAllowList>(new ConfigAdminAllowList(config));
        services.AddScoped<IModerationService, ModerationService>().AddScoped<IWorkVariantLifecycle, WorkVariantLifecycle>()
            .AddScoped<IBanList, BanList>();
        services.AddScoped<AdminSessionValidator>().AddScoped<AdminLoginService>().AddTransient<AdminCookieEvents>();

        services.AddAuthentication().AddCookie(AdminAuth.Scheme, o => AdminAuth.ConfigureCookie(o, env));
        services.AddOptions<CookieAuthenticationOptions>(AdminAuth.Scheme)
            .Configure<TimeProvider>((o, clock) => o.TimeProvider = clock);
        services.AddAuthorization(o => o.AddPolicy(AdminAuth.Policy, p => p
            .AddAuthenticationSchemes(AdminAuth.Scheme).RequireAuthenticatedUser().RequireClaim(AdminAuth.AdminClaim, "1")));

        services.AddAntiforgery(o =>
        {
            o.Cookie.Name = "vela_admin_af";
            o.Cookie.Path = "/admin";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = AdminAuth.SecurePolicy(env);
            o.SuppressXFrameOptionsHeader = true; // the admin middleware sends DENY
        });
        services.AddRazorPages(o =>
        {
            o.RootDirectory = "/Admin/Pages";
            // Default deny: every page needs the admin cookie unless it is explicitly the login page.
            o.Conventions.AuthorizeFolder("/", AdminAuth.Policy);
            o.Conventions.AllowAnonymousToPage("/Login");
        });
        return services;
    }

    public static IApplicationBuilder UseAdminHeaders(this IApplicationBuilder app) =>
        app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/admin"), b => b.UseMiddleware<AdminHeadersMiddleware>());

    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)
    {
        app.MapRazorPages();
        return app;
    }
}
