namespace Vela.Api.Auth.Identity;

/// <summary>Development/Test only. idToken format: <c>fake:&lt;subject&gt;[:&lt;email&gt;]</c>; nonce is ignored.</summary>
public sealed class FakeIdentityVerifier : IIdentityVerifier
{
    public string Provider => "fake";

    public Task<VerifiedIdentity> VerifyAsync(string idToken, string? nonce)
    {
        var parts = idToken.Split(':', 3);
        if (parts.Length < 2 || parts[0] != "fake" || parts[1].Length == 0)
            throw new IdentityVerificationException();
        var email = parts.Length == 3 && parts[2].Length > 0 ? parts[2] : null;
        return Task.FromResult(new VerifiedIdentity("fake", parts[1], email, email is not null));
    }
}
