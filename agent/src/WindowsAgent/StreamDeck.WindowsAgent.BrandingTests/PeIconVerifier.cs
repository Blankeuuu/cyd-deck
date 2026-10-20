using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Diagnostics;
using StreamDeck.WindowsAgent.Tray;

namespace StreamDeck.WindowsAgent.BrandingTests;

// LOAD_LIBRARY_AS_DATAFILE: inspect resources without executing the application.
internal static class PeIconVerifier
{
    public static void VerifyWindow(int processId)
    {
        using var process = Process.GetProcessById(processId);
        var window = process.MainWindowHandle;
        if (window == IntPtr.Zero) throw new InvalidOperationException("No visible main window to inspect");
        // WM_GETICON only reads the window's icon; never activates or changes the window.
        if (SendMessageTimeout(window, 0x007F, (IntPtr)1, IntPtr.Zero, 2, 1000, out var handle) == IntPtr.Zero || handle == IntPtr.Zero)
            throw new InvalidOperationException("Window did not provide ICON_BIG");
        using var actualIcon = Icon.FromHandle(handle); // borrowed handle; do not DestroyIcon.
        using var actual = actualIcon.ToBitmap();
        using var expectedIcon = AppIconFactory.CreateIcon();
        using var expected = expectedIcon.ToBitmap();
        if (actual.Size != expected.Size) throw new InvalidOperationException("Window icon dimensions differ from AppIconFactory");
        for (var y = 0; y < actual.Height; y++)
            for (var x = 0; x < actual.Width; x++)
                if (actual.GetPixel(x, y) != expected.GetPixel(x, y))
                    throw new InvalidOperationException($"Window icon differs from AppIconFactory at {x},{y}");
        Console.WriteLine($"  [PASS] PID {processId} window ICON_BIG ({actual.Width}px) pixel-identical to AppIconFactory; title={process.MainWindowTitle}");
    }

    public static void Verify(string executable, string icoPath)
    {
        var ico = File.ReadAllBytes(icoPath);
        var module = LoadLibraryEx(executable, IntPtr.Zero, 2);
        if (module == IntPtr.Zero) throw new InvalidOperationException($"Cannot read PE resources: {Marshal.GetLastWin32Error()}");
        try
        {
            var group = ReadResource(module, 14, 32512);
            var count = BitConverter.ToUInt16(group, 4);
            if (count != BitConverter.ToUInt16(ico, 4)) throw new InvalidOperationException("PE icon count differs from canonical ICO");
            for (var i = 0; i < count; i++)
            {
                var entry = 6 + i * 14;
                var id = BitConverter.ToUInt16(group, entry + 12);
                var actual = ReadResource(module, 3, id);
                var icoEntry = 6 + i * 16;
                var size = BitConverter.ToInt32(ico, icoEntry + 8);
                var offset = BitConverter.ToInt32(ico, icoEntry + 12);
                if (!actual.AsSpan().SequenceEqual(ico.AsSpan(offset, size)))
                    throw new InvalidOperationException($"PE RT_ICON #{id} differs at size {(group[entry] == 0 ? 256 : group[entry])}");
            }
            Console.WriteLine($"  [PASS] PE RT_GROUP_ICON + {count} RT_ICON byte-identical to canonical ICO; EXE SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable)))}");
        }
        finally { FreeLibrary(module); }
    }

    private static byte[] ReadResource(IntPtr module, int type, int id)
    {
        var resource = FindResource(module, (IntPtr)id, (IntPtr)type);
        if (resource == IntPtr.Zero) throw new InvalidOperationException($"Missing PE resource type={type} id={id}");
        var size = checked((int)SizeofResource(module, resource));
        var pointer = LockResource(LoadResource(module, resource));
        if (pointer == IntPtr.Zero || size == 0) throw new InvalidOperationException("Empty PE icon resource");
        var bytes = new byte[size];
        Marshal.Copy(pointer, bytes, 0, size);
        return bytes;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
