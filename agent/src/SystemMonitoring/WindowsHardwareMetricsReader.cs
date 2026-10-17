using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace StreamDeck.SystemMonitoring;

public sealed record HardwareMetricsSnapshot(double? GpuPercent, double? CpuTemperature, double? GpuTemperature, string GpuSource, string TemperatureSource);
public interface IHardwareMetricsReader : IDisposable { HardwareMetricsSnapshot Read(); }

[SupportedOSPlatform("windows")]
public sealed class WindowsHardwareMetricsReader : IHardwareMetricsReader
{
    private IntPtr query, counter;
    private readonly object gate = new();
    private Task? temperatureProbe;
    private DateTime nextProbe, nextGpuRetry;
    private bool disposed;
    private HardwareMetricsSnapshot temperature = new(null, null, null, "Windows GPU Engine", "Checking temperature sensors…");
    public WindowsHardwareMetricsReader() { OpenGpuQuery(); }
    private void OpenGpuQuery()
    {
        nextGpuRetry = DateTime.UtcNow.AddSeconds(30);
        if (PdhOpenQueryW(null, 0, out query) != 0) return;
        if (PdhAddEnglishCounterW(query, @"\GPU Engine(*)\Utilization Percentage", 0, out counter) != 0) { PdhCloseQuery(query); query = counter = IntPtr.Zero; return; }
        PdhCollectQueryData(query);
    }
    public HardwareMetricsSnapshot Read()
    {
        lock (gate) {
            if (disposed) return new(null, null, null, "GPU reader stopped", "Temperature reader stopped");
            if (query == IntPtr.Zero && DateTime.UtcNow >= nextGpuRetry) OpenGpuQuery();
            var gpu = ReadGpu();
            if ((temperatureProbe is null || temperatureProbe.IsCompleted) && DateTime.UtcNow >= nextProbe) {
                nextProbe = DateTime.UtcNow.AddSeconds(5);
                temperatureProbe = Task.Run(() => { var result = ReadTemperatures(); lock (gate) if (!disposed) temperature = result; });
            }
            return temperature with { GpuPercent = gpu, GpuSource = gpu.HasValue ? "Windows GPU Engine" : "GPU counter unavailable" };
        }
    }
    private double? ReadGpu()
    {
        if (query == IntPtr.Zero || PdhCollectQueryData(query) != 0) return null;
        uint bytes = 0, count;
        const uint moreData = 0x800007D2, format = 0x200;
        if (PdhGetFormattedCounterArrayW(counter, format, ref bytes, out count, IntPtr.Zero) != moreData || bytes == 0 || bytes > 8 * 1024 * 1024) return null;
        var memory = Marshal.AllocHGlobal((int)bytes);
        try {
            if (PdhGetFormattedCounterArrayW(counter, format, ref bytes, out count, memory) != 0) return null;
            var values = new List<(string Name, double Value)>(); var size = Marshal.SizeOf<CounterItem>();
            if (count > bytes / size) return null;
            for (var i = 0; i < count; ++i) {
                var item = Marshal.PtrToStructure<CounterItem>(memory + i * size);
                if (item.Value.Status > 1 || !double.IsFinite(item.Value.Number)) continue;
                values.Add((Marshal.PtrToStringUni(item.Name) ?? "", item.Value.Number));
            }
            return AggregateGpuEngines(values);
        } finally { Marshal.FreeHGlobal(memory); }
    }
    // Sum processes sharing the same physical engine, then use the busiest engine, as Task Manager does.
    public static double? AggregateGpuEngines(IEnumerable<(string Name, double Value)> values)
    {
        static string Engine(string name) { int start = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase), end = name.IndexOf("_engtype_", StringComparison.OrdinalIgnoreCase); return start >= 0 && end > start ? name[start..end] : name; }
        var engines = values.Where(v => double.IsFinite(v.Value) && v.Value >= 0).GroupBy(v => Engine(v.Name)).Select(g => Math.Clamp(g.Sum(v => v.Value), 0, 100)).ToArray();
        return engines.Length == 0 ? null : engines.Max();
    }
    private static HardwareMetricsSnapshot ReadTemperatures()
    {
        bool denied = false;
        foreach (var ns in new[] { @"root\LibreHardwareMonitor", @"root\OpenHardwareMonitor" }) {
            try {
                using var searcher = new ManagementObjectSearcher(new ManagementScope(ns), new ObjectQuery("SELECT Name,Parent,Identifier,Value FROM Sensor WHERE SensorType='Temperature'"), new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = false });
                using var objects = searcher.Get(); var cpu = new List<double>(); var gpu = new List<double>();
                foreach (ManagementObject sensor in objects) {
                    using (sensor) {
                        if (sensor["Value"] is null) continue; var value = Convert.ToDouble(sensor["Value"], System.Globalization.CultureInfo.InvariantCulture); if (!double.IsFinite(value) || value < 0 || value > 125) continue;
                        var identity = $"{sensor["Parent"]} {sensor["Identifier"]} {sensor["Name"]}";
                        if (identity.Contains("cpu", StringComparison.OrdinalIgnoreCase)) cpu.Add(value);
                        if (identity.Contains("gpu", StringComparison.OrdinalIgnoreCase)) gpu.Add(value);
                    }
                }
                if (cpu.Count > 0) return new(null, cpu.Max(), gpu.Count > 0 ? gpu.Max() : null, "Windows GPU Engine", ns.Contains("Libre") ? "LibreHardwareMonitor · CPU" : "OpenHardwareMonitor · CPU");
            } catch (Exception error) when (error is ManagementException or UnauthorizedAccessException or COMException) { denied |= error is UnauthorizedAccessException || error is ManagementException { ErrorCode: ManagementStatus.AccessDenied }; }
        }
        try {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\wmi"), new ObjectQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"), new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = false });
            using var objects = searcher.Get(); var zones = new List<double>();
            foreach (ManagementObject sensor in objects) using (sensor) { if (sensor["CurrentTemperature"] is null) continue; var value = Convert.ToDouble(sensor["CurrentTemperature"]) / 10 - 273.15; if (double.IsFinite(value) && value > 0 && value < 125) zones.Add(value); }
            if (zones.Count > 0) return new(null, zones.Max(), null, "Windows GPU Engine", "ACPI thermal zone");
        } catch (Exception error) when (error is ManagementException or UnauthorizedAccessException or COMException) { denied |= error is UnauthorizedAccessException || error is ManagementException { ErrorCode: ManagementStatus.AccessDenied }; }
        return new(null, null, null, "Windows GPU Engine", denied ? "Sensor access denied" : "Start LibreHardwareMonitor");
    }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; if (query != IntPtr.Zero) PdhCloseQuery(query); query = counter = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct CounterValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Number; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { public IntPtr Name; public CounterValue Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, nuint userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, nuint userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr data);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
