using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vela.Api.Abuse;

/// <summary>
/// Google Play Integrity (standard API) token verification done locally, with the response-encryption keys from Play
/// Console: the token is a JWE (A256KW + A256GCM) wrapping an ES256 JWS whose payload is the verdict. No call to Google.
/// The client must set requestHash to base64url(SHA-256(action)) so a token cannot be replayed across operations.
/// </summary>
public sealed class PlayIntegrityVerifier : IAttestationVerifier
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(1);
    private const int MaxTokenLength = 32 * 1024;

    private readonly PlayIntegrityOptions _options;
    private readonly TimeProvider _clock;
    private readonly byte[] _decryptionKey;
    private readonly ECDsa _verificationKey;

    public PlayIntegrityVerifier(PlayIntegrityOptions options, TimeProvider clock)
    {
        options.Validate();
        _options = options;
        _clock = clock;
        _decryptionKey = Convert.FromBase64String(options.DecryptionKey);
        _verificationKey = ECDsa.Create();
        _verificationKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(options.VerificationKey), out _);
    }

    public static string RequestHash(string action) => Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(action)));

    public Task<bool> VerifyAsync(string platform, string token, string action)
    {
        if (!string.Equals(platform, "android", StringComparison.OrdinalIgnoreCase) || token.Length is 0 or > MaxTokenLength)
            return Task.FromResult(false);
        try { return Task.FromResult(Verify(token, action)); }
        catch (Exception e) when (e is FormatException or JsonException or CryptographicException or InvalidOperationException
                                       or KeyNotFoundException or ArgumentException or OverflowException)
        {
            return Task.FromResult(false);
        }
    }

    private bool Verify(string token, string action)
    {
        var jws = Decrypt(token);
        if (jws is null) return false;
        using var payload = VerifySignature(jws);
        return payload is not null && VerdictAcceptable(payload.RootElement, action);
    }

    private string? Decrypt(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 5) return null;
        using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
        if (Text(header.RootElement, "alg") != "A256KW" || Text(header.RootElement, "enc") != "A256GCM") return null;

        var cek = AesKeyUnwrap(_decryptionKey, Base64Url.DecodeFromChars(parts[1]));
        var iv = Base64Url.DecodeFromChars(parts[2]);
        var cipher = Base64Url.DecodeFromChars(parts[3]);
        var tag = Base64Url.DecodeFromChars(parts[4]);
        if (cek is not { Length: 32 } || iv.Length != 12 || tag.Length != 16) return null;

        var plain = new byte[cipher.Length];
        using var gcm = new AesGcm(cek, 16);
        gcm.Decrypt(iv, cipher, tag, plain, Encoding.ASCII.GetBytes(parts[0]));
        return Encoding.UTF8.GetString(plain);
    }

    private JsonDocument? VerifySignature(string jws)
    {
        var parts = jws.Split('.');
        if (parts.Length != 3) return null;
        using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
        if (Text(header.RootElement, "alg") != "ES256") return null;
        var signature = Base64Url.DecodeFromChars(parts[2]);
        var ok = _verificationKey.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), signature,
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return ok ? JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1])) : null;
    }

    private bool VerdictAcceptable(JsonElement root, string action)
    {
        var request = root.GetProperty("requestDetails");
        var app = root.GetProperty("appIntegrity");
        if (Text(request, "requestPackageName") != _options.PackageName || Text(app, "packageName") != _options.PackageName) return false;
        if (Text(request, "requestHash") != RequestHash(action)) return false;
        if (Text(app, "appRecognitionVerdict") != "PLAY_RECOGNIZED") return false;
        if (!root.GetProperty("deviceIntegrity").GetProperty("deviceRecognitionVerdict").EnumerateArray()
                .Any(v => v.GetString() is "MEETS_DEVICE_INTEGRITY" or "MEETS_STRONG_INTEGRITY")) return false;
        if (_options.AllowedCertificateDigests.Length > 0 &&
            !app.GetProperty("certificateSha256Digest").EnumerateArray().Any(d => _options.AllowedCertificateDigests.Contains(d.GetString())))
            return false;

        var issued = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(Text(request, "timestampMillis")!));
        var age = _clock.GetUtcNow() - issued;
        return age <= MaxAge && age >= -MaxClockSkew;
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>RFC 3394 AES key unwrap. Null when the integrity check value does not match.</summary>
    public static byte[]? AesKeyUnwrap(byte[] kek, byte[] wrapped)
    {
        if (wrapped.Length < 24 || wrapped.Length % 8 != 0) return null;
        var n = wrapped.Length / 8 - 1;
        var a = wrapped[..8];
        var r = Enumerable.Range(1, n).Select(i => wrapped[(i * 8)..(i * 8 + 8)]).ToArray();
        using var aes = Aes.Create();
        aes.Key = kek;
        for (var j = 5; j >= 0; j--)
            for (var i = n; i >= 1; i--)
            {
                var t = (long)(n * j + i);
                for (var k = 0; k < 8; k++) a[7 - k] ^= (byte)(t >> (8 * k));
                var b = aes.DecryptEcb([.. a, .. r[i - 1]], PaddingMode.None);
                a = b[..8];
                r[i - 1] = b[8..];
            }
        return CryptographicOperations.FixedTimeEquals(a, Enumerable.Repeat((byte)0xA6, 8).ToArray()) ? [.. r.SelectMany(x => x)] : null;
    }
}
