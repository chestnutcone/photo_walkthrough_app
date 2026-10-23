using Vela.Api.Branding;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Vela.Api.Auth;
using Vela.Api.Auth.EmailCodes;
using Vela.Api.Auth.Identity;
using Vela.Api.Images;
using Vela.Api.Inquiries;
using Vela.Api.Legal;
using Vela.Api.Share;
using Vela.Api.Moderation;
using Vela.Api.Notifications;
using Vela.Api.Profiles;
using Vela.Api.Account;
using Vela.Api.Works;
using Vela.Api.Search;
using Vela.Api.Social;
using Vela.Api.Stubs;
using Vela.Api.Admin;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection("Branding"));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Default is not configured. Set it via environment (ConnectionStrings__Default) or a secret store.");
builder.Services.AddDbContext<VelaDbContext>(o => o.ConfigureVela(connectionString));

var jwt = JwtOptions.Load(builder.Configuration);
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton<IUserStateCache, UserStateCache>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<TimeProvider>((o, clock) =>
    {
        o.MapInboundClaims = false;
        // Unauthenticated calls to secured operations get the standard envelope, not an empty 401.
        o.Events = new JwtBearerEvents
        {
            // A valid signature is not enough: the user must still be active (cached for a few seconds).
            OnTokenValidated = async ctx =>
            {
                if (!Guid.TryParse(ctx.Principal?.FindFirst("sub")?.Value, out var userId)) { ctx.Fail("no subject"); return; }
                var sp = ctx.HttpContext.RequestServices;
                var state = await sp.GetRequiredService<IUserStateCache>().GetAsync(userId, sp.GetRequiredService<VelaDbContext>());
                if (state != UserState.Active) ctx.Fail(new UserStateAuthException(state));
            },
            OnChallenge = ctx =>
            {
                ctx.HandleResponse();
                var result = ctx.AuthenticateFailure is UserStateAuthException { State: UserState.Suspended }
                    ? Results.Fail(StatusCodes.Status403Forbidden, "account_suspended", "This account is suspended.")
                    : ctx.AuthenticateFailure is UserStateAuthException { State: UserState.Deleted }
                        ? Results.Fail(StatusCodes.Status403Forbidden, "account_deleted", "This account has been deleted.")
                        : Results.Fail(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in to continue.");
                return result.ExecuteAsync(ctx.HttpContext);
            },
        };
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = jwt.SecurityKey,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (_, expires, _, _) => expires is { } e && e > clock.GetUtcNow().UtcDateTime,
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddIdentityVerification(builder.Configuration, builder.Environment);
builder.Services.AddEmailCodes(builder.Configuration, builder.Environment);
builder.Services.AddSafety(builder.Configuration);
builder.Services.AddSocial();
builder.Services.AddNotifications();
builder.Services.AddInquiries();
builder.Services.AddImageStorage(builder.Configuration, builder.Environment);
builder.Services.AddProfiles();
builder.Services.AddWorks();
builder.Services.AddSearch();
builder.Services.AddContentSafety(builder.Configuration, builder.Environment);
builder.Services.AddShare(builder.Configuration, builder.Environment);
builder.Services.AddLegal();
builder.Services.AddAccount(builder.Environment);
builder.Services.AddAdmin(builder.Configuration, builder.Environment);
if (!builder.Environment.IsEnvironment("Test")) builder.Services.AddHostedService<Vela.Api.Moderation.PublishPipelineWorker>();

var app = builder.Build();
if (app.Services.GetRequiredService<EmailCodeSettings>().ReviewMode && !app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Test"))
    app.Logger.LogWarning("Review mode is ON: a fixed sign-in code is accepted for allow-listed review accounts. Turn Auth:ReviewMode off after app review.");
app.UseAdminHeaders();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapSsoEndpoint();
app.MapEmailCodeEndpoints();
app.MapProfileEndpoints();
app.MapWorkEndpoints();
app.MapSafetyEndpoints();
app.MapSocialEndpoints();
app.MapNotificationEndpoints();
app.MapInquiryEndpoints();
app.MapSearchEndpoints();
app.MapSharePage();
app.MapWellKnown();
app.MapLegalPages();
app.MapDmcaEndpoints();
app.MapAccountEndpoints();
app.MapContractStubs();
app.MapAdmin();

app.Run();

public partial class Program;
