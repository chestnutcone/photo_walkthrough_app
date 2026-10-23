using System.Collections.Concurrent;

namespace Vela.Api.Abuse;

/// <summary>Development and Test only: the token "valid" passes on either platform; anything else fails.</summary>
public sealed class FakeAttestationVerifier : IAttestationVerifier
{
    public const string ValidToken = "valid";
    private readonly ConcurrentQueue<string> _actions = new();

    public IReadOnlyList<string> Actions => [.. _actions];

    public Task<bool> VerifyAsync(string platform, string token, string action)
    {
        _actions.Enqueue(action);
        return Task.FromResult(token == ValidToken && platform is "ios" or "android");
    }
}
