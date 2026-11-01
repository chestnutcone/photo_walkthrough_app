using System.Reflection;
using Markdig;
using Vela.Api.Branding;
using Vela.Api.Share;
using static Vela.Api.Share.HtmlText;

namespace Vela.Api.Legal;

/// <summary>Renders the embedded placeholder documents. Markdown runs with raw HTML disabled; configured values are encoded after rendering.</summary>
public sealed class LegalDocuments(BrandingOptionsAccessor brand, LegalOptions legal)
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();
    public static readonly IReadOnlyList<string> Slugs = ["privacy", "terms", "content", "dmca"];
    private const string AgentToken = "<p>{{DmcaAgentBlock}}</p>";

    public bool Exists(string slug) => Slugs.Contains(slug, StringComparer.Ordinal);

    public string Render(string slug, string nonce)
    {
        var html = Markdown.ToHtml(Load(slug), Pipeline)
            .Replace(AgentToken, AgentBlock(), StringComparison.Ordinal)
            .Replace("{{AppName}}", E(brand.Value.AppName), StringComparison.Ordinal)
            .Replace("{{Domain}}", E(brand.Value.Domain), StringComparison.Ordinal);
        var title = $"{E(brand.Value.AppName)}";
        var body = $"<main><div class=\"body doc\"><p class=\"draft\">DRAFT: pending legal review</p>{html}</div></main>";
        return HtmlLayout.Document(nonce, "<meta name=\"robots\" content=\"noindex\">", title, body);
    }

    private string AgentBlock() => legal.HasAgent
        ? $"<p>Name: {E(legal.DmcaAgentName)}<br>Email: {E(legal.DmcaAgentEmail)}<br>Address: {E(legal.DmcaAgentAddress)}</p>"
        : "<p><strong>The designated agent is not yet configured.</strong> Placeholder: contact details will appear here once an agent is registered.</p>";

    private static string Load(string slug)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream($"Vela.Api.Legal.{slug}.md")
            ?? throw new InvalidOperationException($"Embedded legal document '{slug}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>Singleton view of the branding options (the bound options object never changes after startup).</summary>
public sealed record BrandingOptionsAccessor(BrandingOptions Value);
