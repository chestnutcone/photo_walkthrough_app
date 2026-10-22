using Vela.Api.Branding;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Vela.Api.Auth;
using Vela.Api.Auth.EmailCodes;
using Vela.Api.Auth.Identity;
using Vela.Api.Images;
using Vela.Api.Moderation;
using Vela.Api.Profiles;
using Vela.Api.Works;
using Vela.Api.Social;
using Vela.Api.Stubs;

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
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<TimeProvider>((o, clock) =>
    {
        o.MapInboundClaims = false;
        // Unauthenticated calls to secured operations get the standard envelope, not an empty 401.
        o.Events = new JwtBearerEvents
        {
            OnChallenge = ctx =>
            {
                ctx.HandleResponse();
                return Results.Fail(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in to continue.").ExecuteAsync(ctx.HttpContext);
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
builder.Services.AddImageStorage(builder.Configuration, builder.Environment);
builder.Services.AddProfiles();
builder.Services.AddWorks();
builder.Services.AddContentSafety(builder.Configuration, builder.Environment);
if (!builder.Environment.IsEnvironment("Test")) builder.Services.AddHostedService<Vela.Api.Moderation.PublishPipelineWorker>();

var app = builder.Build();
if (app.Services.GetRequiredService<EmailCodeSettings>().ReviewMode && !app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Test"))
    app.Logger.LogWarning("Review mode is ON: a fixed sign-in code is accepted for allow-listed review accounts. Turn Auth:ReviewMode off after app review.");
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
app.MapContractStubs();

app.Run();

public partial class Program;
