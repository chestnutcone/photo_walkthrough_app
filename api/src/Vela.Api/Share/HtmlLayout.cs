using Vela.Api.Share;

namespace Vela.Api.Share;

/// <summary>Fixed page shell. Every dynamic value passed in must already be encoded with <see cref="HtmlText.E"/>.</summary>
public static class HtmlLayout
{
    private const string Css = """
        :root{--paper:#f6f1e9;--ink:#1d1a16;--muted:#6b6358;--accent:#b4472b;--card:#fffdf9;--line:#e3dccf}
        @media (prefers-color-scheme:dark){:root{--paper:#17140f;--ink:#f1ebe0;--muted:#a79d8e;--accent:#e08a6c;--card:#201c16;--line:#352f26}}
        *{box-sizing:border-box}
        body{margin:0;background:var(--paper);color:var(--ink);font:17px/1.55 Georgia,'Times New Roman',serif;padding:32px 16px}
        main{max-width:640px;margin:0 auto;background:var(--card);border:1px solid var(--line);border-radius:4px;overflow:hidden}
        .cover{display:block;width:100%;height:auto}
        .body{padding:28px 28px 32px}
        .brand{margin:0 0 20px;font:600 12px/1 system-ui,sans-serif;letter-spacing:.14em;text-transform:uppercase;color:var(--muted)}
        h1{margin:0 0 6px;font-size:34px;line-height:1.12;letter-spacing:-.01em;overflow-wrap:anywhere}
        .by{margin:0 0 18px;color:var(--muted);font-style:italic;overflow-wrap:anywhere}
        .intro{margin:0 0 28px;overflow-wrap:anywhere}
        .draft{margin:0 0 20px;padding:10px 14px;border:2px dashed var(--accent);color:var(--accent);font:700 14px/1.3 system-ui,sans-serif}
        .button{display:inline-block;background:var(--ink);color:var(--paper);padding:12px 22px;border-radius:2px;font:600 15px/1 system-ui,sans-serif;text-decoration:none}
        .button:hover,.button:focus-visible{background:var(--accent);outline:2px solid var(--accent);outline-offset:3px}
        .doc h2{font-size:22px;margin:28px 0 8px}
        .doc a{color:var(--accent)}
        """;

    public static string Document(string nonce, string headExtra, string title, string body) =>
        $"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{title}</title>
        {headExtra}
        <style nonce="{nonce}">{Css}</style>
        </head>
        <body>
        {body}
        </body>
        </html>
        """;

    /// <summary>The generic not-found page: identical for every missing-resource reason, so existence is not revealed.</summary>
    public static string Message(string nonce, string message) =>
        Document(nonce, "<meta name=\"robots\" content=\"noindex\">", HtmlText.E(message),
            $"<main><div class=\"body\"><h1>{HtmlText.E(message)}</h1><p class=\"intro\">This page is unavailable.</p></div></main>");
}
