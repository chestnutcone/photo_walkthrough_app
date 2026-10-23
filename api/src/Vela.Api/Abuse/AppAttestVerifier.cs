using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Vela.Api.Abuse;

/// <summary>
/// Apple App Attest attestation-object verification (https://developer.apple.com/documentation/devicecheck/validating-apps-that-connect-to-your-server).
/// Token: base64url(challenge) "." base64url(attestation object), challenge = "{action}|{unix seconds}".
/// The client attests a key with SHA-256(challenge) as clientDataHash. Each token is a fresh attestation, so the
/// server needs no key store; ongoing assertions with counters are a later refinement.
/// </summary>
public sealed class AppAttestVerifier : IAttestationVerifier
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(1);
    private const string NonceOid = "1.2.840.113635.100.8.2";
    private const int MaxTokenLength = 32 * 1024;
    private static readonly byte[] ProductionAaguid = [.. "appattest"u8, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] DevelopmentAaguid = "appattestdevelop"u8.ToArray();

    private readonly AppAttestOptions _options;
    private readonly TimeProvider _clock;
    private readonly X509Certificate2 _root;
    private readonly byte[] _appIdHash;

    public AppAttestVerifier(AppAttestOptions options, TimeProvider clock)
    {
        options.Validate();
        _options = options;
        _clock = clock;
        _root = X509Certificate2.CreateFromPem(options.EffectiveRootPem);
        _appIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(options.AppId));
    }

    public Task<bool> VerifyAsync(string platform, string token, string action)
    {
        if (!string.Equals(platform, "ios", StringComparison.OrdinalIgnoreCase) || token.Length is 0 or > MaxTokenLength)
            return Task.FromResult(false);
        try { return Task.FromResult(Verify(token, action)); }
        catch (Exception e) when (e is FormatException or CryptographicException or AsnContentException or InvalidCastException
                                       or KeyNotFoundException or ArgumentException or OverflowException or InvalidOperationException)
        {
            return Task.FromResult(false);
        }
    }

    private bool Verify(string token, string action)
    {
        var parts = token.Split('.');
        if (parts.Length != 2) return false;
        var challenge = Base64Url.DecodeFromChars(parts[0]);
        if (!ChallengeFresh(Encoding.UTF8.GetString(challenge), action)) return false;

        var root = (Dictionary<string, object?>)MiniCbor.Parse(Base64Url.DecodeFromChars(parts[1]))!;
        if (root["fmt"] as string != "apple-appattest") return false;
        var authData = (byte[])root["authData"]!;
        var chain = ((List<object?>)((Dictionary<string, object?>)root["attStmt"]!)["x5c"]!).Cast<byte[]>().ToList();
        if (chain.Count is < 2 or > 4) return false;

        using var leaf = X509CertificateLoader.LoadCertificate(chain[0]);
        if (!ChainTrusted(leaf, chain.Skip(1))) return false;

        var nonce = SHA256.HashData([.. authData, .. SHA256.HashData(challenge)]);
        return NonceMatches(leaf, nonce) && AuthDataAcceptable(authData, leaf);
    }

    private bool ChallengeFresh(string challenge, string action)
    {
        var bar = challenge.LastIndexOf('|');
        if (bar < 0 || challenge[..bar] != action || !long.TryParse(challenge[(bar + 1)..], out var seconds)) return false;
        var age = _clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds);
        return age <= MaxAge && age >= -MaxClockSkew;
    }

    private bool ChainTrusted(X509Certificate2 leaf, IEnumerable<byte[]> intermediates)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(_root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = _clock.GetUtcNow().UtcDateTime;
        foreach (var raw in intermediates) chain.ChainPolicy.ExtraStore.Add(X509CertificateLoader.LoadCertificate(raw));
        return chain.Build(leaf);
    }

    private static bool NonceMatches(X509Certificate2 leaf, byte[] expected)
    {
        var ext = leaf.Extensions.OfType<X509Extension>().SingleOrDefault(e => e.Oid?.Value == NonceOid);
        if (ext is null) return false;
        var seq = new AsnReader(ext.RawData, AsnEncodingRules.DER).ReadSequence();
        var tagged = seq.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1));
        var actual = tagged.ReadOctetString();
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private bool AuthDataAcceptable(byte[] auth, X509Certificate2 leaf)
    {
        // rpIdHash(32) flags(1) counter(4) aaguid(16) credentialIdLength(2) credentialId(n) ...
        if (auth.Length < 55) return false;
        var credLength = BinaryPrimitives.ReadUInt16BigEndian(auth.AsSpan(53, 2));
        if (credLength != 32 || auth.Length < 55 + credLength) return false;
        var aaguid = auth.AsSpan(37, 16);
        var aaguidOk = aaguid.SequenceEqual(ProductionAaguid) || (_options.AllowDevelopment && aaguid.SequenceEqual(DevelopmentAaguid));
        if (!aaguidOk || !auth.AsSpan(0, 32).SequenceEqual(_appIdHash)) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(auth.AsSpan(33, 4)) != 0) return false;

        using var key = leaf.GetECDsaPublicKey() ?? throw new CryptographicException("leaf key is not EC");
        var p = key.ExportParameters(false);
        var keyId = SHA256.HashData([4, .. p.Q.X!, .. p.Q.Y!]);
        return CryptographicOperations.FixedTimeEquals(keyId, auth.AsSpan(55, credLength));
    }
}
