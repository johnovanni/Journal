using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Journal.Core.Persistence;
using Journal.Core.Settings;

namespace Journal.Infrastructure.Storage;

/// <summary>Standard locations under the user profile.</summary>
public static class AppPaths
{
    public static string LocalRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Journal");
    public static string DefaultVaultFolder => Path.Combine(LocalRoot, "Vault");
    public static string TempRoot => Path.Combine(LocalRoot, "temp");
    public static string LogFolder => Path.Combine(LocalRoot, "logs");
    public static string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Journal", "settings.json");

    /// <summary>The OneDrive folder when OneDrive is set up for this user, otherwise null (FR-SEC-02).</summary>
    public static string? OneDriveFolder
    {
        get
        {
            var od = Environment.GetEnvironmentVariable("OneDrive") ?? Environment.GetEnvironmentVariable("OneDriveConsumer");
            return !string.IsNullOrEmpty(od) && Directory.Exists(od) ? Path.Combine(od, "Journal") : null;
        }
    }
}

/// <summary>
/// Decrypted copies of attachments opened by the user, kept in a private folder and deleted on lock,
/// on exit and at the next startup (FR-ATT-03, FR-ATT-06).
/// </summary>
public sealed class TempFileManager(string root)
{
    private string? _sessionFolder;

    public string Root { get; } = root;

    public string SessionFolder
    {
        get
        {
            if (_sessionFolder is not null && Directory.Exists(_sessionFolder)) return _sessionFolder;
            Directory.CreateDirectory(Root);
            _sessionFolder = Path.Combine(Root, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)));
            CreatePrivateDirectory(_sessionFolder);
            return _sessionFolder;
        }
    }

    /// <summary>Creates a unique sub-folder and returns the path for <paramref name="fileName"/> inside it.</summary>
    public string NewFilePath(string fileName)
    {
        var dir = Path.Combine(SessionFolder, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4)));
        Directory.CreateDirectory(dir);
        var safe = string.Concat(fileName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(dir, string.IsNullOrWhiteSpace(safe) ? "attachment" : safe);
    }

    /// <summary>Deletes every decrypted temp file. Files still open in another app are skipped.</summary>
    public int CleanAll()
    {
        _sessionFolder = null;
        if (!Directory.Exists(Root)) return 0;
        int failures = 0;
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch (IOException) { failures++; }
            catch (UnauthorizedAccessException) { failures++; }
        }
        foreach (var dir in Directory.EnumerateDirectories(Root).ToList())
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return failures;
    }

    private static void CreatePrivateDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        if (!OperatingSystem.IsWindows())
        {
            info.Create();
            return;
        }
        var user = WindowsIdentity.GetCurrent().User!;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.Create(security);
    }

    /// <summary>True when only the current user has access (used by tests).</summary>
    public static bool IsPrivate(string path)
    {
        var rules = new DirectoryInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
        var user = WindowsIdentity.GetCurrent().User!;
        return rules.Cast<FileSystemAccessRule>().All(r => r.IdentityReference == user);
    }
}

/// <summary>Export and restore of the encrypted vault as a zip file (FR-SET-04, FR-SET-05).</summary>
public static class BackupService
{
    public static void Export(string vaultRoot, string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(vaultRoot, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(AtomicFileWriter.TempSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            var relative = Path.GetRelativePath(vaultRoot, file).Replace('\\', '/');
            zip.CreateEntryFromFile(file, relative, CompressionLevel.NoCompression);
        }
    }

    public static void Restore(string zipPath, string targetFolder)
    {
        if (Directory.Exists(targetFolder) && Directory.EnumerateFileSystemEntries(targetFolder).Any())
            throw new IOException("Choose an empty folder to restore the backup into.");
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            if (zip.GetEntry("vault.json") is null) throw new InvalidDataException("This file isn't a journal backup.");
        }
        Directory.CreateDirectory(targetFolder);
        ZipFile.ExtractToDirectory(zipPath, targetFolder);
    }
}

/// <summary>Copies the vault to a new folder, verifies every file, then removes the old copy (FR-SET-03).</summary>
public static class VaultMover
{
    /// <summary>Test hook to simulate a failure during copy.</summary>
    internal static Action<string>? OnCopy;

    public static void Move(string from, string to)
    {
        from = Path.GetFullPath(from);
        to = Path.GetFullPath(to);
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return;
        if (Directory.Exists(to) && Directory.EnumerateFileSystemEntries(to).Any())
            throw new IOException("Choose an empty folder for the journal.");
        var files = Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(AtomicFileWriter.TempSuffix, StringComparison.OrdinalIgnoreCase)).ToList();
        try
        {
            foreach (var file in files)
            {
                var target = Path.Combine(to, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                OnCopy?.Invoke(file);
                File.Copy(file, target);
                if (!SameContent(file, target)) throw new IOException($"Copy verification failed for {Path.GetFileName(file)}.");
            }
        }
        catch
        {
            try { if (Directory.Exists(to)) Directory.Delete(to, recursive: true); } catch (IOException) { }
            throw;
        }
        Directory.Delete(from, recursive: true);
    }

    private static bool SameContent(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using var fa = File.OpenRead(a);
        using var fb = File.OpenRead(b);
        return SHA256.HashData(fa).AsSpan().SequenceEqual(SHA256.HashData(fb));
    }
}

public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JournalJson.Options) { WriteIndented = true };

    public string Path { get; } = path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(Path), Options) ?? new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(settings, Options));
        File.Move(temp, Path, overwrite: true);
    }
}
