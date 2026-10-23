using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Vela.Api.Abuse;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Abuse;

/// <summary>Builds synthetic App Attest attestation objects signed by a throwaway CA chain.</summary>
public sealed class AppAttestKit : IDisposable
{
    public const string AppId = "ABCDE12345.com.example.vela";
    public static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    public const string NonceOid = "1.2.840.113635.100.8.2";

    public MutableTimeProvider Clock { get; } = new(Now);
    private readonly ECDsa _rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _interKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public X509Certificate2 Root { get; }
    public X509Certificate2 Intermediate { get; }

    public AppAttestKit()
    {
        var rootReq = Req("CN=Test Attestation Root", _rootKey, ca: true);
        Root = rootReq.CreateSelfSigned(Now.AddYears(-1), Now.AddYears(10));
        var interReq = Req("CN=Test Attestation CA 1", _interKey, ca: true);
        Intermediate = interReq.Create(Root, Now.AddMonths(-6), Now.AddYears(5), RandomNumberGenerator.GetBytes(8))
            .CopyWithPrivateKey(_interKey);
    }

    public void Dispose() { Root.Dispose(); Intermediate.Dispose(); _rootKey.Dispose(); _interKey.Dispose(); }

    private static CertificateRequest Req(string subject, ECDsa key, bool ca)
    {
        var r = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        r.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, false, 0, true));
        return r;
    }

    public AppAttestOptions Options(Action<AppAttestOptions>? tweak = null)
    {
        var o = new AppAttestOptions { AppId = AppId, RootCertificatePem = Root.ExportCertificatePem() };
        tweak?.Invoke(o);
        return o;
    }

    public AppAttestVerifier Verifier(Action<AppAttestOptions>? tweak = null) => new(Options(tweak), Clock);

    public sealed record Spec
    {
        public string Action { get; init; } = "email_request";
        public DateTimeOffset At { get; init; } = Now;
        public string AppId { get; init; } = AppAttestKit.AppId;
        public byte[]? Aaguid { get; init; }
        public uint Counter { get; init; }
        public bool WrongNonce { get; init; }
        public bool WrongKeyId { get; init; }
        public string Fmt { get; init; } = "apple-appattest";
        public bool UntrustedChain { get; init; }
        public bool MissingNonceExtension { get; init; }
        public string? ChallengeOverride { get; init; }
        public bool ExpiredLeaf { get; init; }
    }

    public static readonly byte[] ProdAaguid = Encoding.ASCII.GetBytes("appattest").Concat(new byte[7]).ToArray();
    public static readonly byte[] DevAaguid = Encoding.ASCII.GetBytes("appattestdevelop");

    public string Token(Spec? spec = null)
    {
        spec ??= new Spec();
        var challenge = Encoding.UTF8.GetBytes(spec.ChallengeOverride ?? $"{spec.Action}|{spec.At.ToUnixTimeSeconds()}");
        var clientDataHash = SHA256.HashData(challenge);

        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = leafKey.ExportParameters(false);
        var point = new byte[] { 4 }.Concat(p.Q.X!).Concat(p.Q.Y!).ToArray();
        var keyId = SHA256.HashData(point);
        var credId = spec.WrongKeyId ? RandomNumberGenerator.GetBytes(32) : keyId;

        var authData = new List<byte>();
        authData.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(spec.AppId)));
        authData.Add(0x40);
        authData.AddRange([(byte)(spec.Counter >> 24), (byte)(spec.Counter >> 16), (byte)(spec.Counter >> 8), (byte)spec.Counter]);
        authData.AddRange(spec.Aaguid ?? ProdAaguid);
        authData.AddRange([0, (byte)credId.Length]);
        authData.AddRange(credId);
        authData.AddRange(new byte[] { 0xA1, 0x01, 0x02 }); // stand-in COSE key; the verifier stops at the credential id
        var auth = authData.ToArray();

        var nonce = SHA256.HashData([.. auth, .. clientDataHash]);
        if (spec.WrongNonce) nonce[0] ^= 1;

        var leafReq = Req("CN=Test Leaf", leafKey, ca: false);
        if (!spec.MissingNonceExtension) leafReq.CertificateExtensions.Add(new X509Extension(new Oid(NonceOid), NonceExtension(nonce), false));
        var notBefore = spec.ExpiredLeaf ? Now.AddDays(-10) : Now.AddHours(-1);
        var notAfter = spec.ExpiredLeaf ? Now.AddDays(-5) : Now.AddDays(2);
        using var leaf = leafReq.Create(spec.UntrustedChain ? UntrustedIssuer() : Intermediate, notBefore, notAfter, RandomNumberGenerator.GetBytes(8));

        var cbor = Cbor.Map(
            ("fmt", Cbor.Text(spec.Fmt)),
            ("attStmt", Cbor.Map(("x5c", Cbor.Array(Cbor.Bytes(leaf.RawData), Cbor.Bytes(Intermediate.RawData))), ("receipt", Cbor.Bytes([1, 2, 3])))),
            ("authData", Cbor.Bytes(auth)));
        return $"{PlayIntegrityKit.B64(challenge)}.{PlayIntegrityKit.B64(cbor)}";
    }

    private static X509Certificate2 UntrustedIssuer()
    {
        using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Req("CN=Rogue CA", k, ca: true).CreateSelfSigned(Now.AddYears(-1), Now.AddYears(1));
    }

    private static byte[] NonceExtension(byte[] nonce)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1)))
            w.WriteOctetString(nonce);
        return w.Encode();
    }
}

/// <summary>Tiny CBOR writer for the test fixtures.</summary>
public static class Cbor
{
    private static byte[] Head(int major, int len) => len switch
    {
        < 24 => [(byte)(major << 5 | len)],
        < 256 => [(byte)(major << 5 | 24), (byte)len],
        _ => [(byte)(major << 5 | 25), (byte)(len >> 8), (byte)len],
    };

    public static byte[] Text(string s) { var b = Encoding.UTF8.GetBytes(s); return [.. Head(3, b.Length), .. b]; }
    public static byte[] Bytes(byte[] b) => [.. Head(2, b.Length), .. b];
    public static byte[] Array(params byte[][] items) => [.. Head(4, items.Length), .. items.SelectMany(x => x)];
    public static byte[] Map(params (string Key, byte[] Value)[] pairs) =>
        [.. Head(5, pairs.Length), .. pairs.SelectMany(p => Text(p.Key).Concat(p.Value))];
}

public class AppAttestVerifierTests : IDisposable
{
    private readonly AppAttestKit _kit = new();
    private const string Action = "email_request";
    public void Dispose() => _kit.Dispose();

    private Task<bool> Verify(AppAttestKit.Spec? spec = null, Action<AppAttestOptions>? tweak = null, string platform = "ios", string action = Action) =>
        _kit.Verifier(tweak).VerifyAsync(platform, _kit.Token(spec), action);

    [Fact]
    public async Task A_genuine_attestation_for_the_action_is_accepted() => Assert.True(await Verify());

    [Fact]
    public async Task Other_platform_is_rejected() => Assert.False(await Verify(platform: "android"));

    [Fact]
    public async Task Token_minted_for_another_action_is_rejected() => Assert.False(await Verify(action: "sso"));

    [Fact]
    public async Task Challenge_not_in_the_expected_shape_is_rejected()
    {
        Assert.False(await Verify(new() { ChallengeOverride = "email_request" }));
        Assert.False(await Verify(new() { ChallengeOverride = "email_request|soon" }));
    }

    [Fact]
    public async Task Stale_and_future_challenges_are_rejected()
    {
        Assert.False(await Verify(new() { At = AppAttestKit.Now.AddMinutes(-6) }));
        Assert.False(await Verify(new() { At = AppAttestKit.Now.AddMinutes(3) }));
        Assert.True(await Verify(new() { At = AppAttestKit.Now.AddMinutes(-4) }));
    }

    [Fact]
    public async Task Another_app_id_is_rejected() => Assert.False(await Verify(new() { AppId = "ZZZZZ99999.com.evil.clone" }));

    [Fact]
    public async Task Nonce_that_does_not_bind_the_challenge_is_rejected() => Assert.False(await Verify(new() { WrongNonce = true }));

    [Fact]
    public async Task Credential_id_that_is_not_the_key_hash_is_rejected() => Assert.False(await Verify(new() { WrongKeyId = true }));

    [Fact]
    public async Task Non_zero_counter_is_rejected() => Assert.False(await Verify(new() { Counter = 1 }));

    [Fact]
    public async Task Wrong_format_is_rejected() => Assert.False(await Verify(new() { Fmt = "packed" }));

    [Fact]
    public async Task Chain_to_an_unknown_root_is_rejected() => Assert.False(await Verify(new() { UntrustedChain = true }));

    [Fact]
    public async Task Missing_nonce_extension_is_rejected() => Assert.False(await Verify(new() { MissingNonceExtension = true }));

    [Fact]
    public async Task Expired_leaf_certificate_is_rejected() => Assert.False(await Verify(new() { ExpiredLeaf = true }));

    [Fact]
    public async Task Development_environment_aaguid_needs_explicit_opt_in()
    {
        var dev = new AppAttestKit.Spec { Aaguid = AppAttestKit.DevAaguid };
        Assert.False(await Verify(dev));
        Assert.True(await Verify(dev, o => o.AllowDevelopment = true));
    }

    [Fact]
    public async Task Unknown_aaguid_is_rejected() => Assert.False(await Verify(new() { Aaguid = new byte[16] }, o => o.AllowDevelopment = true));

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("a.b")]
    [InlineData("Zm9v.Zm9v")]
    public async Task Garbage_is_rejected_without_throwing(string token) =>
        Assert.False(await _kit.Verifier().VerifyAsync("ios", token, Action));

    [Fact]
    public async Task Truncated_attestation_is_rejected_without_throwing()
    {
        var token = _kit.Token();
        var cut = token[..(token.Length - 40)];
        Assert.False(await _kit.Verifier().VerifyAsync("ios", cut, Action));
    }

    [Fact]
    public void Default_root_is_the_published_apple_app_attestation_root()
    {
        using var cert = X509Certificate2.CreateFromPem(AppleRoots.AppAttestationRootPem);
        Assert.Equal("1CB9823BA28BA6AD2D33A006941DE2AE4F513EF1D4E831B9F7E0FA7B6242C932", cert.GetCertHashString(HashAlgorithmName.SHA256));
        Assert.Contains("Apple App Attestation Root CA", cert.Subject);
    }
}
