using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vela.Api.Branding;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Works;
using static Vela.Api.Share.HtmlText;

namespace Vela.Api.Share;

/// <summary>Public preview page with Open Graph tags for link unfurling. Contains no script and does not redirect.</summary>
public static class SharePageEndpoint
{
    public const string NotFoundMessage = "Story not found";
    private const int MaxMetaTitle = 200, MaxVisibleTitle = 300, MaxDescription = 200, MaxVisibleIntro = 600, MaxName = 100;

    public static IEndpointRouteBuilder MapSharePage(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/s/{workId}", [HttpMethods.Get, HttpMethods.Head], HandleAsync);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        string workId, WorkReader reader, VelaDbContext db, IBlobStore blobs, IOptions<BrandingOptions> branding, WebOptions web, IHostEnvironment env)
    {
        // Guests only: the same visibility as an anonymous API read, so every hidden state looks like "unknown".
        if (!Guid.TryParseExact(workId, "D", out var id) || await reader.FindPublicAsync(id, null) is not { } work
            || await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == work.ArtistId) is not { } artist)
            return HtmlResult.NotFound(NotFoundMessage);

        var cover = work.Width > 0 && work.Height > 0 ? SafeImage(blobs.PublicUrl($"{BlobNames.WorkVariantsPrefix(id)}feed.webp"), env) : null;
        var model = new ShareModel(work, artist.DisplayName, cover, branding.Value, web.StoryUrl(id));
        return HtmlResult.Found(nonce => Render(model, nonce));
    }

    /// <summary>Image URLs must be absolute https (http only in Development); anything else is left out.</summary>
    private static string? SafeImage(Uri url, IHostEnvironment env) =>
        url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttps || (env.IsDevelopment() && url.Scheme == Uri.UriSchemeHttp))
            ? url.AbsoluteUri : null;

    private sealed record ShareModel(Work Work, string Artist, string? Cover, BrandingOptions Brand, string ViewerUrl);

    private static string Render(ShareModel m, string nonce)
    {
        var w = m.Work;
        var app = E(m.Brand.AppName);
        var title = E(Short(w.Title, MaxMetaTitle));
        var artist = E(Short(m.Artist, MaxName));
        var description = E(Short(string.IsNullOrWhiteSpace(Clean(w.Intro)) ? $"A photo story by {Short(m.Artist, MaxName)}" : w.Intro, MaxDescription));
        var canonical = E($"https://{m.Brand.Domain}/s/{w.Id}");

        var head = new List<string>
        {
            Meta("name", "description", description), Meta("property", "og:type", "article"), Meta("property", "og:title", title),
            Meta("property", "og:description", description), Meta("property", "og:url", canonical), Meta("property", "og:site_name", app),
            Meta("name", "twitter:card", "summary_large_image"), Meta("name", "twitter:title", title), Meta("name", "twitter:description", description),
            $"<link rel=\"canonical\" href=\"{canonical}\">",
        };
        if (m.Cover is { } cover)
        {
            var url = E(cover);
            head.Add(Meta("property", "og:image", url));
            head.Add(Meta("property", "og:image:width", w.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            head.Add(Meta("property", "og:image:height", w.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            head.Add(Meta("name", "twitter:image", url));
        }

        var img = m.Cover is { } c
            ? $"<img class=\"cover\" src=\"{E(c)}\" alt=\"{E(Short(w.AltText ?? w.Title, MaxVisibleTitle))}\" width=\"{w.Width}\" height=\"{w.Height}\">"
            : "";
        var intro = Clean(w.Intro).Length > 0 ? $"<p class=\"intro\">{E(Short(w.Intro, MaxVisibleIntro))}</p>" : "";
        var body = $"""
            <main>
            {img}
            <div class="body">
            <p class="brand">{app}</p>
            <h1>{E(Short(w.Title, MaxVisibleTitle))}</h1>
            <p class="by">by {artist}</p>
            {intro}
            <a class="button" href="{E(m.ViewerUrl)}">View story</a>
            </div>
            </main>
            """;
        return HtmlLayout.Document(nonce, string.Join("\n", head), $"{title} · {app}", body);
    }

    /// <summary>Both arguments must already be encoded.</summary>
    private static string Meta(string attr, string name, string encodedContent) => $"<meta {attr}=\"{name}\" content=\"{encodedContent}\">";
}
