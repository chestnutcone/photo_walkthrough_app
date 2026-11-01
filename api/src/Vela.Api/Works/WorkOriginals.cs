using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Images;

namespace Vela.Api.Works;

/// <summary>A sealed copy of an upload and the SHA-256 of exactly the bytes that were copied.</summary>
public sealed record SealedOriginal(string Blob, string Sha256);

/// <summary>
/// Sealing and resolving work originals (S1b-3). The upload URL can overwrite <c>originals/works/{id}</c> for 15 minutes, so at
/// publish the bytes are copied once to a server-owned name and only that copy is read afterwards.
/// </summary>
public sealed class WorkOriginals(VelaDbContext db, IBlobStore blobs)
{
    /// <summary>
    /// Streams the upload once into the sealed name, hashing what passes through and refusing more than
    /// <see cref="VariantGenerator.MaxSourceBytes"/>; nothing is buffered, so sealing costs no memory per request.
    /// </summary>
    public async Task<SealedOriginal> SealAsync(Guid workId)
    {
        var name = BlobNames.NewSealedOriginal(workId);
        Stream source;
        string? declared;
        try
        {
            declared = await blobs.GetContentTypeAsync(BlobNames.WorkOriginal(workId));
            source = await blobs.OpenReadAsync(BlobNames.WorkOriginal(workId));
        }
        catch (FileNotFoundException) { throw new InvalidImageException("missing"); }
        await using (source)
        {
            if (source.CanSeek && source.Length > VariantGenerator.MaxSourceBytes) throw new InvalidImageException("too_large");
            using var hashing = new HashingStream(source, VariantGenerator.MaxSourceBytes);
            try
            {
                // Always stored as opaque bytes, whatever content type the uploader declared (S1b-19).
                await blobs.WriteAsync(name, hashing, "application/octet-stream");
            }
            catch (Exception) when (hashing.Exceeded)
            {
                await blobs.DeletePrefixAsync(name);
                throw new InvalidImageException("too_large");
            }
            try { ImageSniffer.Verify(declared, hashing.Head); } // S2-4: the bytes must be what the uploader declared
            catch (InvalidImageException)
            {
                await blobs.DeletePrefixAsync(name);
                throw;
            }
            return new SealedOriginal(name, hashing.Sha256Hex());
        }
    }

    /// <summary>True when the sealed blob still holds exactly the bytes whose hash was recorded.</summary>
    public async Task<bool> MatchesAsync(string sealedBlob, string sha256)
    {
        try
        {
            await using var stream = await blobs.OpenReadAsync(sealedBlob);
            return string.Equals(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)), sha256, StringComparison.Ordinal);
        }
        catch (FileNotFoundException) { return false; }
    }

    /// <summary>The original to read for one work (sealed copy, else the upload).</summary>
    public async Task<string> NameAsync(Guid workId) =>
        BlobNames.OriginalOf(workId, await db.Works.AsNoTracking().Where(w => w.Id == workId).Select(w => w.SealedOriginalBlob).FirstOrDefaultAsync());

    /// <summary>The original to read for each work id (missing works are absent from the result).</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> workIds)
    {
        if (workIds.Count == 0) return new Dictionary<Guid, string>();
        var rows = await db.Works.AsNoTracking().Where(w => workIds.Contains(w.Id)).Select(w => new { w.Id, w.SealedOriginalBlob }).ToListAsync();
        return rows.ToDictionary(r => r.Id, r => BlobNames.OriginalOf(r.Id, r.SealedOriginalBlob));
    }

    /// <summary>Read-only pass-through that hashes every byte and throws once more than the limit has been read.</summary>
    private sealed class HashingStream(Stream inner, long limit) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private long _read;
        private readonly byte[] _head = new byte[ImageSniffer.HeadLength];
        private int _headLength;
        public bool Exceeded { get; private set; }
        /// <summary>The first bytes that passed through, for the content-type check.</summary>
        public ReadOnlySpan<byte> Head => _head.AsSpan(0, _headLength);

        public string Sha256Hex() => Convert.ToHexStringLower(_hash.GetHashAndReset());

        public override int Read(byte[] buffer, int offset, int count) => Count(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = await inner.ReadAsync(buffer, ct);
            return Count(buffer.Span[..n]);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        private int Count(ReadOnlySpan<byte> data)
        {
            var take = Math.Min(_head.Length - _headLength, data.Length);
            data[..take].CopyTo(_head.AsSpan(_headLength));
            _headLength += take;
            _read += data.Length;
            if (_read > limit) { Exceeded = true; throw new InvalidImageException("too_large"); }
            _hash.AppendData(data);
            return data.Length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _hash.Dispose();
            base.Dispose(disposing);
        }
    }
}
