using Microsoft.Extensions.Options;
using Vela.Api.Branding;
using Vela.Api.Share;

namespace Vela.Api.Legal;

public static class LegalEndpoints
{
    public static IServiceCollection AddLegal(this IServiceCollection services) =>
        services.AddSingleton(sp => new BrandingOptionsAccessor(sp.GetRequiredService<IOptions<BrandingOptions>>().Value))
            .AddSingleton<LegalDocuments>();

    public static IEndpointRouteBuilder MapLegalPages(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/legal/{slug}", [HttpMethods.Get, HttpMethods.Head], (string slug, LegalDocuments docs) =>
            docs.Exists(slug) ? HtmlResult.Found(nonce => docs.Render(slug, nonce)) : HtmlResult.NotFound("Page not found"));
        return app;
    }
}
