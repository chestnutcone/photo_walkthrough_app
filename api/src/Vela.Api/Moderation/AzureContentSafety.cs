using Azure;
using Azure.AI.ContentSafety;

namespace Vela.Api.Moderation;

public sealed class AzureContentSafety(ContentSafetyClient client) : IContentSafety
{
    public async Task<int> SeverityAsync(string text)
    {
        var result = await client.AnalyzeTextAsync(new AnalyzeTextOptions(text));
        return result.Value.CategoriesAnalysis.Max(c => c.Severity ?? 0);
    }

    public async Task<int> SeverityAsync(Stream image)
    {
        var data = await BinaryData.FromStreamAsync(image);
        var result = await client.AnalyzeImageAsync(new AnalyzeImageOptions(new ContentSafetyImageData(data)));
        return result.Value.CategoriesAnalysis.Max(c => c.Severity ?? 0);
    }
}

public static class ContentSafetyServiceCollectionExtensions
{
    public static IServiceCollection AddContentSafety(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        if (env.EnvironmentName is "Development" or "Test")
        {
            services.AddSingleton<FakeContentSafety>();
            services.AddSingleton<IContentSafety>(sp => sp.GetRequiredService<FakeContentSafety>());
            return services;
        }
        var endpoint = Require(config, "Endpoint");
        var key = Require(config, "Key");
        // Replies are scanned inside the request, so a slow scanner must fail quickly (and fail open, by design) rather than hold a request
        // open for the SDK's defaults of three retries with 100-second network timeouts.
        var options = new ContentSafetyClientOptions { Retry = { MaxRetries = 2, NetworkTimeout = TimeSpan.FromSeconds(10), Mode = Azure.Core.RetryMode.Exponential } };
        services.AddSingleton<IContentSafety>(new AzureContentSafety(new ContentSafetyClient(new Uri(endpoint), new AzureKeyCredential(key), options)));
        return services;
    }

    private static string Require(IConfiguration config, string name) =>
        config[$"ContentSafety:{name}"] is { Length: > 0 } v ? v
        : throw new InvalidOperationException(
            $"ContentSafety:{name} is not configured. Set it via environment (ContentSafety__{name}) or a secret store.");
}
