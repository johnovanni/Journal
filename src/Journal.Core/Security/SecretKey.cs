using System.Security.Cryptography;

namespace Journal.Core.Security;

/// <summary>
/// Key material held in a pinned array (so the GC never copies it) and zeroed on dispose.
/// Managed memory gives no hard guarantee; this is best effort (Design §6.1).
/// </summary>
public sealed class SecretKey : IDisposable
{
    public const int Length = 32;

    private readonly byte[] _bytes;
    private bool _disposed;

    private SecretKey(int length)
    {
        _bytes = GC.AllocateArray<byte>(length, pinned: true);
    }

    public static SecretKey Generate()
    {
        var key = new SecretKey(Length);
        RandomNumberGenerator.Fill(key._bytes);
        return key;
    }

    /// <summary>Copies <paramref name="source"/> into a new pinned key. The caller should clear its own copy.</summary>
    public static SecretKey FromBytes(ReadOnlySpan<byte> source)
    {
        var key = new SecretKey(source.Length);
        source.CopyTo(key._bytes);
        return key;
    }

    public ReadOnlySpan<byte> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _bytes;
        }
    }

    public bool IsDisposed => _disposed;

    /// <summary>True when every byte is zero (used by tests to confirm keys are wiped on lock).</summary>
    internal bool IsZeroed() => _bytes.All(b => b == 0);

    /// <summary>Exposes the backing array for tests only.</summary>
    internal byte[] UnsafeBuffer => _bytes;

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_bytes);
        _disposed = true;
    }
}
