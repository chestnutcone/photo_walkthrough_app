namespace Vela.Api.Images;

/// <summary>Blob storage for originals, image variants and avatars. Names are "{container}/{path}".</summary>
public interface IBlobStore
{
    /// <summary>Write-only, single-blob upload URL valid for at most 15 minutes (longer requests are clamped).</summary>
    Uri CreateUploadUrl(string blobName, TimeSpan ttl);
    Task WriteAsync(string name, Stream data, string contentType);
    /// <summary>Throws <see cref="FileNotFoundException"/> when the blob does not exist.</summary>
    Task<Stream> OpenReadAsync(string name);
    Task DeletePrefixAsync(string prefix);
    /// <summary>The CDN URL for a blob in the public containers.</summary>
    Uri PublicUrl(string name);
}
