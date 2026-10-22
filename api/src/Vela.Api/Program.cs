using Vela.Api.Branding;
using Vela.Api.Stubs;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection("Branding"));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapContractStubs();

app.Run();

public partial class Program;
