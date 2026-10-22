using Vela.Api.Branding;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Stubs;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection("Branding"));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Default is not configured. Set it via environment (ConnectionStrings__Default) or a secret store.");
builder.Services.AddDbContext<VelaDbContext>(o => o.ConfigureVela(connectionString));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapContractStubs();

app.Run();

public partial class Program;
