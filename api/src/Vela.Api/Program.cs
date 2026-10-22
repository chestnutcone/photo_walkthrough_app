using Vela.Api.Branding;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Stubs;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection("Branding"));

builder.Services.AddDbContext<VelaDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? "Host=localhost;Port=5432;Database=vela;Username=vela;Password=vela_dev_only")
    .UseSnakeCaseNamingConvention());

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapContractStubs();

app.Run();

public partial class Program;
