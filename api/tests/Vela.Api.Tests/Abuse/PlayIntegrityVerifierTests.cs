using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vela.Api.Abuse;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Abuse;

/// <summary>Builds synthetic standard-API Play Integrity tokens: a JWS (ES256) wrapped in a JWE (A256KW + A256GCM).</summary>
public sealed class PlayIntegrityKit
{
    public static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    public const string Package = "com.example.vela";

    public byte[] AesKey { get; } = RandomNumberGenerator.GetBytes(32);
    public ECDsa SigningKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public MutableTimeProvider Clock { get; } = new(Now);

    public PlayIntegrityOptions Options(Action<PlayIntegrityOptions>? tweak = null)
    {
        var o = new PlayIntegrityOptions
        {
            PackageName = Package,
            DecryptionKey = Convert.ToBase64String(AesKey),
            VerificationKey = Convert.ToBase64String(SigningKey.ExportSubjectPublicKeyInfo()),
        };
        tweak?.Invoke(o);
        return o;
    }

    public PlayIntegrityVerifier Verifier(Action<PlayIntegrityOptions>? tweak = null) => new(Options(tweak), Clock);

    public static string RequestHash(string action) => B64(SHA256.HashData(Encoding.UTF8.GetBytes(action)));

    public static Dictionary<string, object?> Payload(string action, DateTimeOffset at, string package = Package,
        string app = "PLAY_RECOGNIZED", string[]? device = null, string[]? digests = null, string? hash = null) => new()
    {
        ["requestDetails"] = new Dictionary<string, object?>
        {
            ["requestPackageName"] = package,
            ["requestHash"] = hash ?? RequestHash(action),
            ["timestampMillis"] = at.ToUnixTimeMilliseconds().ToString(),
        },
        ["appIntegrity"] = new Dictionary<string, object?>
        {
            ["appRecognitionVerdict"] = app,
            ["packageName"] = package,
            ["certificateSha256Digest"] = digests ?? ["AAAA"],
            ["versionCode"] = "7",
        },
        ["deviceIntegrity"] = new Dictionary<string, object?> { ["deviceRecognitionVerdict"] = device ?? ["MEETS_DEVICE_INTEGRITY"] },
    };

    public string Token(Dictionary<string, object?> payload, ECDsa? signer = null, byte[]? kek = null, bool tamperTag = false)
    {
        var jws = Jws(JsonSerializer.Serialize(payload), signer ?? SigningKey);
        return Jwe(Encoding.UTF8.GetBytes(jws), kek ?? AesKey, tamperTag);
    }

    public static string Jws(string payloadJson, ECDsa key)
    {
        var header = B64(Encoding.UTF8.GetBytes("{\"alg\":\"ES256\"}"));
        var body = B64(Encoding.UTF8.GetBytes(payloadJson));
        var sig = key.SignData(Encoding.ASCII.GetBytes($"{header}.{body}"), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{body}.{B64(sig)}";
    }

    public static string Jwe(byte[] plaintext, byte[] kek, bool tamperTag = false)
    {
        var header = B64(Encoding.UTF8.GetBytes("{\"alg\":\"A256KW\",\"enc\":\"A256GCM\"}"));
        var cek = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(cek, 16)) gcm.Encrypt(iv, plaintext, cipher, tag, Encoding.ASCII.GetBytes(header));
        if (tamperTag) tag[0] ^= 1;
        return $"{header}.{B64(AesKeyWrap(kek, cek))}.{B64(iv)}.{B64(cipher)}.{B64(tag)}";
    }

    /// <summary>RFC 3394 wrap, written independently of the production unwrap.</summary>
    public static byte[] AesKeyWrap(byte[] kek, byte[] key)
    {
        var n = key.Length / 8;
        var a = Enumerable.Repeat((byte)0xA6, 8).ToArray();
        var r = Enumerable.Range(0, n).Select(i => key[(i * 8)..(i * 8 + 8)]).ToArray();
        using var aes = Aes.Create();
        aes.Key = kek;
        for (var j = 0; j < 6; j++)
            for (var i = 0; i < n; i++)
            {
                var b = aes.EncryptEcb([.. a, .. r[i]], PaddingMode.None);
                var t = (long)(n * j + i + 1);
                a = b[..8];
                for (var k = 0; k < 8; k++) a[7 - k] ^= (byte)(t >> (8 * k));
                r[i] = b[8..];
            }
        return [.. a, .. r.SelectMany(x => x)];
    }

    public static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public class PlayIntegrityVerifierTests
{
    private readonly PlayIntegrityKit _kit = new();
    private const string Action = "email_request";

    private Dictionary<string, object?> Good(string action = Action) => PlayIntegrityKit.Payload(action, PlayIntegrityKit.Now);

    [Fact]
    public async Task A_genuine_token_for_the_action_is_accepted()
    {
        Assert.True(await _kit.Verifier().VerifyAsync("android", _kit.Token(Good()), Action));
    }

    [Fact]
    public async Task Strong_integrity_is_accepted_too()
    {
        var p = PlayIntegrityKit.Payload(Action, PlayIntegrityKit.Now, device: ["MEETS_STRONG_INTEGRITY", "MEETS_DEVICE_INTEGRITY"]);
        Assert.True(await _kit.Verifier().VerifyAsync("android", _kit.Token(p), Action));
    }

    [Fact]
    public async Task Other_platform_is_rejected()
    {
        Assert.False(await _kit.Verifier().VerifyAsync("ios", _kit.Token(Good()), Action));
    }

    [Fact]
    public async Task Token_minted_for_another_action_is_rejected()
    {
        Assert.False(await _kit.Verifier().VerifyAsync("android", _kit.Token(Good("sso")), Action));
    }

    [Fact]
    public async Task Wrong_package_is_rejected()
    {
        var p = PlayIntegrityKit.Payload(Action, PlayIntegrityKit.Now, package: "com.evil.clone");
        Assert.False(await _kit.Verifier().VerifyAsync("android", _kit.Token(p), Action));
    }

    [Theory]
    [InlineData("UNRECOGNIZED_VERSION")]
    [InlineData("UNEVALUATED")]
    public async Task Unrecognised_app_is_rejected(string verdict)
    {
        var p = PlayIntegrityKit.Payload(Action, PlayIntegrityKit.Now, app: verdict);
        Assert.False(await _kit.Verifier().VerifyAsync("android", _kit.Token(p), Action));
    }

    [Fact]
    public async Task Device_that_fails_integrity_is_rejected()
    {
        var p = PlayIntegrityKit.Payload(Action, PlayIntegrityKit.Now, device: ["MEETS_BASIC_INTEGRITY"]);
        Assert.False(await _kit.Verifier().VerifyAsync("android", _kit.Token(p), Action));
        var none = PlayIntegrityKit.Payload(Action, PlayIntegrityKit.Now, device: []);
        Assert.False(await _kit.Verifier().VerifyAsync("android", _kit.Token(none), Action));
    }

    [Fact]
    public async Task Stale_and_future_tokens_are_rejected()
    {
        var old = PlayIntegrityKit.Payload(Action, PlayIntegrityKit.Now.AddMinutes(-11));
        var future = PlayIntegrityKit.Payload(Action, PlayIntegrityKit.Now.AddMinutes(3));
        var fresh = PlayIntegrityKit.Payload(Action, PlayIntegrityKit.Now.AddMinutes(-9));
        Assert.False(await _kit.Verifier().VerifyAsync("android", _kit.Token(old), Action));
        Assert.False(await _kit.Verifier().VerifyAsync("android", _kit.Token(future), Action));
        Assert.True(await _kit.Verifier().VerifyAsync("android", _kit.Token(fresh), Action));
    }

    [Fact]
    public async Task Token_expires_as_the_clock_advances()
    {
        var token = _kit.Token(Good());
        var v = _kit.Verifier();
        Assert.True(await v.VerifyAsync("android", token, Action));
        _kit.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.False(await v.VerifyAsync("android", token, Action));
    }

    [Fact]
    public async Task Signature_from_another_key_is_rejected()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.False(await _kit.Verifier().VerifyAsync("android", _kit.Token(Good(), signer: attacker), Action));
    }

    [Fact]
    public async Task Token_encrypted_to_another_key_or_tampered_is_rejected()
    {
        Assert.False(await _kit.Verifier().VerifyAsync("android", _kit.Token(Good(), kek: RandomNumberGenerator.GetBytes(32)), Action));
        Assert.False(await _kit.Verifier().VerifyAsync("android", _kit.Token(Good(), tamperTag: true), Action));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("a.b.c.d.e")]
    [InlineData("a.b.c")]
    public async Task Garbage_is_rejected_without_throwing(string token)
    {
        Assert.False(await _kit.Verifier().VerifyAsync("android", token, Action));
    }

    [Fact]
    public async Task Certificate_digest_allow_list_is_enforced_when_configured()
    {
        var token = _kit.Token(PlayIntegrityKit.Payload(Action, PlayIntegrityKit.Now, digests: ["AAAA"]));
        Assert.True(await _kit.Verifier(o => o.AllowedCertificateDigests = ["AAAA", "BBBB"]).VerifyAsync("android", token, Action));
        Assert.False(await _kit.Verifier(o => o.AllowedCertificateDigests = ["CCCC"]).VerifyAsync("android", token, Action));
    }

    [Fact]
    public async Task Unsigned_alg_none_jws_inside_a_valid_envelope_is_rejected()
    {
        var header = PlayIntegrityKit.B64(Encoding.UTF8.GetBytes("{\"alg\":\"none\"}"));
        var body = PlayIntegrityKit.B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Good())));
        var token = PlayIntegrityKit.Jwe(Encoding.UTF8.GetBytes($"{header}.{body}."), _kit.AesKey);
        Assert.False(await _kit.Verifier().VerifyAsync("android", token, Action));
    }

    [Fact]
    public void Key_unwrap_matches_the_rfc_3394_vector()
    {
        var kek = Convert.FromHexString("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F");
        var wrapped = Convert.FromHexString("28C9F404C4B810F4CBCCB35CFB87F8263F5786E2D80ED326CBC7F0E71A99F43BFB988B9B7A02DD21");
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF000102030405060708090A0B0C0D0E0F");

        Assert.Equal(wrapped, PlayIntegrityKit.AesKeyWrap(kek, key));
        Assert.Equal(key, PlayIntegrityVerifier.AesKeyUnwrap(kek, wrapped));
        Assert.Null(PlayIntegrityVerifier.AesKeyUnwrap(kek, wrapped.Select(b => (byte)(b ^ 1)).ToArray()));
    }
}
