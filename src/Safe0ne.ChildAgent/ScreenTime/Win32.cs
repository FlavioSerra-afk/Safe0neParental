using System.Runtime.InteropServices;

namespace Safe0ne.ChildAgent.ScreenTime;

internal static class Win32
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    internal static extern bool LockWorkStation();

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    internal static int? TryGetForegroundProcessId()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return null;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;
        return unchecked((int)pid);
    }

    internal static TimeSpan GetIdleTime()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref lii))
            return TimeSpan.Zero;

        // dwTime is tick count in ms at last input; Environment.TickCount is ms since boot (signed).
        var lastInputMs = unchecked((int)lii.dwTime);
        var nowMs = Environment.TickCount;
        var idleMs = unchecked(nowMs - lastInputMs);
        if (idleMs < 0) idleMs = 0;
        return TimeSpan.FromMilliseconds(idleMs);
    }
}
