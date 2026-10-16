using System.Security.Cryptography;
using System.Text;
using Journal.Core.Inspiration;
using Journal.Core.Security;
using Journal.Core.Services;
using Journal.Core.Tests.Support;
using Journal.Infrastructure;
using Journal.Infrastructure.Crypto;
using Journal.Infrastructure.Hello;
using Journal.Infrastructure.Storage;

namespace Journal.Infrastructure.Tests;

/// <summary>Simulates Windows Hello: signatures are HMAC(deviceSecret, data), deterministic unless told otherwise.</summary>
public sealed class FakeHelloApi : IHelloCredentialApi
{
    private readonly Dictionary<string, byte[]> _keys = [];
    public bool Supported { get; set; } = true;
    public bool Deterministic { get; set; } = true;
    public bool Cancel { get; set; }
    public List<(string Name, byte[] Data)> SignCalls { get; } = [];

    public Task<bool> IsSupportedAsync() => Task.FromResult(Supported);

    public Task CreateAsync(string name, nint windowHandle, CancellationToken cancellation = default)
    {
        if (Cancel) throw new UnlockFailedException(UnlockFailure.Cancelled, "cancelled");
        _keys[name] = RandomNumberGenerator.GetBytes(32);
        return Task.CompletedTask;
    }

    /// <summary>When set, SignAsync waits until it is cancelled, like a Windows Hello dialog nobody answers.</summary>
    public bool WaitForUser { get; set; }
    public bool SawCancellation { get; private set; }

    public async Task<byte[]> SignAsync(string name, byte[] data, nint windowHandle, CancellationToken cancellation = default)
    {
        if (WaitForUser)
        {
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            catch (OperationCanceledException) { SawCancellation = true; throw; }
        }
        if (Cancel) throw new UnlockFailedException(UnlockFailure.Cancelled, "cancelled");
        if (!_keys.TryGetValue(name, out var key)) throw new UnlockFailedException(UnlockFailure.NotAvailable, "no key");
        SignCalls.Add((name, data));
        byte[] input = Deterministic ? data : [.. data, .. RandomNumberGenerator.GetBytes(8)];
        return HMACSHA256.HashData(key, input).Concat(new byte[224]).ToArray();
    }

    public Task DeleteAsync(string name)
    {
        _keys.Remove(name);
        return Task.CompletedTask;
    }
}

public sealed class FakeConsentApi : IUserConsentApi
{
    public bool Available { get; set; } = true;
    public bool Verified { get; set; } = true;
    public Task<bool> IsAvailableAsync() => Task.FromResult(Available);
    public nint LastWindowHandle { get; private set; }
    public Task<bool> VerifyAsync(string message, nint windowHandle, CancellationToken cancellation = default)
    {
        LastWindowHandle = windowHandle;
        return Task.FromResult(Verified);
    }
}

/// <summary>A real vault in a temporary folder with fast PBKDF2 for most tests.</summary>
public sealed class TempVault : IAsyncDisposable
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "journal-tests", Guid.NewGuid().ToString("N"));
    public FakeHelloApi Hello { get; } = new();
    public FakeConsentApi Consent { get; } = new();
    public FakeMachine Machine { get; } = new();
    public FakeClock Clock { get; } = new();
    public InspirationSettings Inspiration { get; set; } = new();
    public VaultSession Session { get; private set; } = null!;
    public VaultServices Services { get; private set; } = null!;
    public RecoveryCode Code { get; private set; } = null!;
    public AtomicFileWriter Writer { get; } = new();
    public int Iterations { get; init; } = 1_000;

    public VaultSession NewSession() => new(new JsonVaultHeaderStore(new VaultPaths(Folder), Writer),
        [new WindowsHelloKeyProtector(Hello, Machine), new HelloGatedDpapiProtector(Consent, Machine), new RecoveryCodeKeyProtector(Iterations)],
        Machine, Clock);

    public static async Task<TempVault> CreateAsync(bool useHello = true, int iterations = 1_000)
    {
        var v = new TempVault { Iterations = iterations };
        v.Session = v.NewSession();
        v.Code = (await v.Session.CreateVaultAsync(useHello)).RecoveryCode;
        v.Services = await VaultServices.OpenAsync(v.Session, v.Folder, v.Clock, () => v.Inspiration, v.Writer);
        return v;
    }

    /// <summary>Locks, then unlocks with the recovery code into fresh services.</summary>
    public async Task ReopenAsync()
    {
        Services.Dispose();
        await Session.LockAsync(LockReason.User);
        Session = NewSession();
        await Session.UnlockWithRecoveryCodeAsync(Code);
        Services = await VaultServices.OpenAsync(Session, Folder, Clock, () => Inspiration, Writer);
    }

    public JournalService Journal => Services.Journal;

    public IEnumerable<string> AllFiles() => Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories);

    /// <summary>True when the marker appears in any vault file as UTF-8 or UTF-16.</summary>
    public bool ContainsPlaintext(string marker) =>
        AllFiles().Any(f =>
        {
            var bytes = File.ReadAllBytes(f);
            return IndexOf(bytes, Encoding.UTF8.GetBytes(marker)) >= 0 || IndexOf(bytes, Encoding.Unicode.GetBytes(marker)) >= 0;
        });

    public static int IndexOf(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle);

    public async ValueTask DisposeAsync()
    {
        Services?.Dispose();
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
        await Task.CompletedTask;
    }
}
