using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Journal.Core.Security;

namespace Journal.Infrastructure.Crypto;

public enum FileKind : byte
{
    Entry = 1,
    Catalog = 2,
    Blob = 3,
    UserList = 4,
}

/// <summary>Key derivation helpers (Design §6.3).</summary>
public static class KeyDerivation
{
    public const int RecoveryIterations = 600_000;

    public static SecretKey HelloKek(ReadOnlySpan<byte> signature, ReadOnlySpan<byte> challenge) =>
        Derive(signature, challenge, "journal/hello-kek/v1");

    public static SecretKey RecoveryKek(RecoveryCode code, byte[] salt, int iterations)
    {
        var input = code.ToKdfInput();
        try
        {
            var kek = Rfc2898DeriveBytes.Pbkdf2(input, salt, iterations, HashAlgorithmName.SHA256, SecretKey.Length);
            try { return SecretKey.FromBytes(kek); }
            finally { CryptographicOperations.ZeroMemory(kek); }
        }
        finally { CryptographicOperations.ZeroMemory(input); }
    }

    public static SecretKey SubKey(SecretKey dataKey, string purpose) =>
        Derive(dataKey.Span, [], purpose);

    private static SecretKey Derive(ReadOnlySpan<byte> ikm, ReadOnlySpan<byte> salt, string info)
    {
        Span<byte> output = stackalloc byte[SecretKey.Length];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, output, salt, Encoding.UTF8.GetBytes(info));
        try { return SecretKey.FromBytes(output); }
        finally { CryptographicOperations.ZeroMemory(output); }
    }
}

/// <summary>Wraps a 256-bit key with AES-256-GCM: nonce(12) | ciphertext(32) | tag(16).</summary>
public static class KeyWrapper
{
    public static byte[] Wrap(SecretKey kek, SecretKey key, byte[] aad)
    {
        var output = new byte[AesGcmSizes.Nonce + key.Span.Length + AesGcmSizes.Tag];
        var nonce = output.AsSpan(0, AesGcmSizes.Nonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(kek.Span, AesGcmSizes.Tag);
        aes.Encrypt(nonce, key.Span, output.AsSpan(AesGcmSizes.Nonce, key.Span.Length), output.AsSpan(output.Length - AesGcmSizes.Tag), aad);
        return output;
    }

    /// <summary>Throws <see cref="CryptographicException"/> when the KEK or associated data is wrong.</summary>
    public static SecretKey Unwrap(SecretKey kek, byte[] wrapped, byte[] aad)
    {
        int keyLength = wrapped.Length - AesGcmSizes.Nonce - AesGcmSizes.Tag;
        if (keyLength != SecretKey.Length) throw new CryptographicException("Wrapped key has the wrong length.");
        Span<byte> plain = stackalloc byte[keyLength];
        try
        {
            using var aes = new AesGcm(kek.Span, AesGcmSizes.Tag);
            aes.Decrypt(wrapped.AsSpan(0, AesGcmSizes.Nonce), wrapped.AsSpan(AesGcmSizes.Nonce, keyLength),
                wrapped.AsSpan(wrapped.Length - AesGcmSizes.Tag), plain, aad);
            return SecretKey.FromBytes(plain);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}

internal static class AesGcmSizes
{
    public const int Nonce = 12;
    public const int Tag = 16;
}

/// <summary>
/// Encrypts vault files with keys derived from the data key. Every file binds its kind, the vault id and
/// its logical id (file name or blob id) as associated data (NFR-SEC-03).
/// </summary>
public sealed class VaultCrypto : IDisposable
{
    public static ReadOnlySpan<byte> Magic => "JRNL"u8;
    public const byte Version = 1;
    public const int HeaderLength = 4 + 1 + 1;
    public const int ChunkSize = 1024 * 1024;

    private readonly SecretKey _contentKey;
    private readonly SecretKey _nameKey;

    public Guid VaultId { get; }

    public VaultCrypto(SecretKey dataKey, Guid vaultId)
    {
        VaultId = vaultId;
        _contentKey = KeyDerivation.SubKey(dataKey, "journal/content/v1");
        _nameKey = KeyDerivation.SubKey(dataKey, "journal/filenames/v1");
    }

    /// <summary>File name that does not reveal the date (NFR-SEC-04): 64 hex characters.</summary>
    public string EntryFileName(DateOnly date)
    {
        var mac = HMACSHA256.HashData(_nameKey.Span, Encoding.ASCII.GetBytes(date.ToString("yyyy-MM-dd")));
        return Convert.ToHexStringLower(mac);
    }

    public byte[] Seal(FileKind kind, string logicalId, ReadOnlySpan<byte> plain)
    {
        var output = new byte[HeaderLength + AesGcmSizes.Nonce + plain.Length + AesGcmSizes.Tag];
        WriteHeader(output, kind);
        var nonce = output.AsSpan(HeaderLength, AesGcmSizes.Nonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_contentKey.Span, AesGcmSizes.Tag);
        aes.Encrypt(nonce, plain, output.AsSpan(HeaderLength + AesGcmSizes.Nonce, plain.Length),
            output.AsSpan(output.Length - AesGcmSizes.Tag), Aad(kind, logicalId));
        return output;
    }

    /// <summary>Throws <see cref="CryptographicException"/> for damaged, tampered or swapped files.</summary>
    public byte[] Open(FileKind kind, string logicalId, ReadOnlySpan<byte> sealedData)
    {
        if (sealedData.Length < HeaderLength + AesGcmSizes.Nonce + AesGcmSizes.Tag) throw new CryptographicException("File is too short.");
        CheckHeader(sealedData, kind);
        int ctLength = sealedData.Length - HeaderLength - AesGcmSizes.Nonce - AesGcmSizes.Tag;
        var plain = new byte[ctLength];
        using var aes = new AesGcm(_contentKey.Span, AesGcmSizes.Tag);
        aes.Decrypt(sealedData.Slice(HeaderLength, AesGcmSizes.Nonce), sealedData.Slice(HeaderLength + AesGcmSizes.Nonce, ctLength),
            sealedData[^AesGcmSizes.Tag..], plain, Aad(kind, logicalId));
        return plain;
    }

    /// <summary>
    /// Streams <paramref name="input"/> into <paramref name="output"/> as 1 MB GCM chunks. Each chunk's associated data
    /// includes its index and whether it is the last one, so reordering or truncation is detected.
    /// Returns the plaintext SHA-256 (hex) and length.
    /// </summary>
    public async Task<(string Sha256, long Length)> EncryptStreamAsync(Stream input, Stream output, string logicalId, CancellationToken ct = default)
    {
        var header = new byte[HeaderLength + 4];
        WriteHeader(header, FileKind.Blob);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(HeaderLength), ChunkSize);
        await output.WriteAsync(header, ct);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var aes = new AesGcm(_contentKey.Span, AesGcmSizes.Tag);
        var current = new byte[ChunkSize];
        var next = new byte[ChunkSize];
        int currentLen = await FillAsync(input, current, ct);
        long index = 0, total = 0;
        var cipher = new byte[ChunkSize];
        var meta = new byte[AesGcmSizes.Nonce + 4];
        var tag = new byte[AesGcmSizes.Tag];
        while (true)
        {
            int nextLen = currentLen == ChunkSize ? await FillAsync(input, next, ct) : 0;
            bool isFinal = nextLen == 0;
            hash.AppendData(current, 0, currentLen);
            total += currentLen;

            RandomNumberGenerator.Fill(meta.AsSpan(0, AesGcmSizes.Nonce));
            BinaryPrimitives.WriteInt32LittleEndian(meta.AsSpan(AesGcmSizes.Nonce), currentLen);
            aes.Encrypt(meta.AsSpan(0, AesGcmSizes.Nonce), current.AsSpan(0, currentLen), cipher.AsSpan(0, currentLen), tag,
                ChunkAad(logicalId, index, isFinal));
            await output.WriteAsync(meta, ct);
            await output.WriteAsync(cipher.AsMemory(0, currentLen), ct);
            await output.WriteAsync(tag, ct);
            if (isFinal) break;
            (current, next) = (next, current);
            currentLen = nextLen;
            index++;
        }
        CryptographicOperations.ZeroMemory(current);
        CryptographicOperations.ZeroMemory(next);
        return (Convert.ToHexStringLower(hash.GetHashAndReset()), total);
    }

    public async Task DecryptStreamAsync(Stream input, Stream output, string logicalId, CancellationToken ct = default)
    {
        var header = new byte[HeaderLength + 4];
        if (await FillAsync(input, header, ct) != header.Length) throw new CryptographicException("Blob is too short.");
        CheckHeader(header, FileKind.Blob);
        int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(HeaderLength));
        if (chunkSize is <= 0 or > 64 * 1024 * 1024) throw new CryptographicException("Invalid chunk size.");

        using var aes = new AesGcm(_contentKey.Span, AesGcmSizes.Tag);
        var meta = new byte[AesGcmSizes.Nonce + 4];
        var cipher = new byte[chunkSize];
        var plain = new byte[chunkSize];
        var tag = new byte[AesGcmSizes.Tag];
        long index = 0;
        try
        {
            while (true)
            {
                if (await FillAsync(input, meta, ct) != meta.Length) throw new CryptographicException("Blob is truncated.");
                int len = BinaryPrimitives.ReadInt32LittleEndian(meta.AsSpan(AesGcmSizes.Nonce));
                if (len < 0 || len > chunkSize) throw new CryptographicException("Invalid chunk length.");
                if (await FillAsync(input, cipher.AsMemory(0, len), ct) != len) throw new CryptographicException("Blob is truncated.");
                if (await FillAsync(input, tag, ct) != tag.Length) throw new CryptographicException("Blob is truncated.");
                bool isFinal = input.CanSeek ? input.Position >= input.Length : len < chunkSize;
                aes.Decrypt(meta.AsSpan(0, AesGcmSizes.Nonce), cipher.AsSpan(0, len), tag, plain.AsSpan(0, len),
                    ChunkAad(logicalId, index, isFinal));
                await output.WriteAsync(plain.AsMemory(0, len), ct);
                if (isFinal) break;
                index++;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static async Task<int> FillAsync(Stream s, Memory<byte> buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await s.ReadAsync(buffer[total..], ct);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static void WriteHeader(Span<byte> output, FileKind kind)
    {
        Magic.CopyTo(output);
        output[4] = Version;
        output[5] = (byte)kind;
    }

    private static void CheckHeader(ReadOnlySpan<byte> data, FileKind kind)
    {
        if (!data[..4].SequenceEqual(Magic)) throw new CryptographicException("Not a journal file.");
        if (data[4] != Version) throw new CryptographicException($"Unsupported file version {data[4]}.");
        if (data[5] != (byte)kind) throw new CryptographicException("Unexpected file kind.");
    }

    private byte[] Aad(FileKind kind, string logicalId)
    {
        var id = Encoding.UTF8.GetBytes(logicalId);
        var aad = new byte[HeaderLength + 16 + id.Length];
        WriteHeader(aad, kind);
        VaultId.TryWriteBytes(aad.AsSpan(HeaderLength, 16));
        id.CopyTo(aad.AsSpan(HeaderLength + 16));
        return aad;
    }

    private byte[] ChunkAad(string logicalId, long index, bool isFinal)
    {
        var baseAad = Aad(FileKind.Blob, logicalId);
        var aad = new byte[baseAad.Length + 9];
        baseAad.CopyTo(aad, 0);
        BinaryPrimitives.WriteInt64LittleEndian(aad.AsSpan(baseAad.Length), index);
        aad[^1] = isFinal ? (byte)1 : (byte)0;
        return aad;
    }

    public void Dispose()
    {
        _contentKey.Dispose();
        _nameKey.Dispose();
    }
}
