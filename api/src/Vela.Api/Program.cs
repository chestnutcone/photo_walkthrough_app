using Vela.Api.Branding;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Vela.Api.Auth;
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

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapContractStubs();

app.Run();

public partial class Program;
