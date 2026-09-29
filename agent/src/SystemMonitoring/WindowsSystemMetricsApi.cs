using System.Runtime.InteropServices;

namespace StreamDeck.SystemMonitoring;

public sealed class WindowsSystemMetricsApi : IWindowsSystemMetricsApi
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public bool TryGetSystemTimes(out CpuTimes times)
    {
        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            times = new CpuTimes(ToUInt64(idle), ToUInt64(kernel), ToUInt64(user));
            return true;
        }

        times = default;
        return false;
    }

    public bool TryGetMemoryStatus(out MemoryStatus status)
    {
        var memory = new NativeMethods.MemoryStatusEx();
        memory.dwLength = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>();

        if (NativeMethods.GlobalMemoryStatusEx(ref memory))
        {
            status = new MemoryStatus(memory.ullTotalPhys, memory.ullAvailPhys);
            return true;
        }

        status = default;
        return false;
    }

    public ulong GetTickCount64() => NativeMethods.GetTickCount64();

    private static ulong ToUInt64(NativeMethods.FileTime fileTime) =>
        ((ulong)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime;

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

        [DllImport("kernel32.dll")]
        internal static extern ulong GetTickCount64();

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileTime
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct MemoryStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
    }
}
