using Vela.Api.Branding;
using Results = Vela.Api.Common.Results;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection("Branding"));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
