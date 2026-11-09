using System.Reflection;

namespace Journal.App.Services;

/// <summary>Facts about the running program, read from the build (FR-UI-02).</summary>
public static class AppInfo
{
    /// <summary>The version, e.g. "1.1.0", without the commit suffix the build adds ("1.1.0+b7c09ff…").</summary>
    public static string Version { get; } = Clean(
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>The app's name and version, as shown to the user: "Journal 1.1.0".</summary>
    public static string NameAndVersion => $"Journal {Version}";

    public static string Clean(string? informationalVersion)
    {
        var v = informationalVersion ?? "";
        var plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }
}
