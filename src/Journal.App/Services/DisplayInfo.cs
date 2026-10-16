using System.Runtime.InteropServices;

namespace Journal.App.Services;

/// <summary>Which display a window is on (ISS-009).</summary>
public static partial class DisplayInfo
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint MonitorInfoPrimary = 1;

    /// <summary>True when most of the window is on the primary (main) display, or when it can't be told.</summary>
    public static bool IsOnPrimaryDisplay(nint windowHandle)
    {
        if (windowHandle == 0) return true;
        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return monitor == 0 || !GetMonitorInfo(monitor, ref info) || (info.Flags & MonitorInfoPrimary) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
