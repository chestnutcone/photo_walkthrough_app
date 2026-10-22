namespace Vela.Api.Moderation;

/// <summary>Content Safety scan. Both calls return the MAX severity across hate, self-harm, sexual and violence.</summary>
public interface IContentSafety
{
    Task<int> SeverityAsync(string text);
    Task<int> SeverityAsync(Stream image);
}
