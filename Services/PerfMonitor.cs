using System.Diagnostics;
using static DynamicIsland.Native;

namespace DynamicIsland;

/// <summary>Нагрузка процессора, памяти и видеокарты. Опрашивается только пока открыта вкладка «Система».</summary>
public static class PerfMonitor
{
    public const int Points = 40;
    public static readonly double[] Cpu = new double[Points];
    public static readonly double[] Ram = new double[Points];
    public static readonly double[] Gpu = new double[Points];
    public static bool GpuAvailable { get; private set; } = true;

    static long _idle, _kernel, _user;
    static List<PerformanceCounter>? _gpu;
    static DateTime _gpuScan;
    static readonly object _lock = new();

    public static async Task SampleAsync()
    {
        double cpu = 0;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            long di = idle - _idle, dk = kernel - _kernel, du = user - _user;
            long total = dk + du;
            if (_kernel != 0 && total > 0) cpu = Math.Clamp((total - di) * 100.0 / total, 0, 100);
            _idle = idle; _kernel = kernel; _user = user;
        }

        var m = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        double ram = GlobalMemoryStatusEx(ref m) ? m.dwMemoryLoad : 0;

        double gpu = await Task.Run(ReadGpu);

        Push(Cpu, cpu); Push(Ram, ram); Push(Gpu, gpu);
    }

    static double ReadGpu()
    {
        lock (_lock)
        {
            if (!GpuAvailable) return 0;
            try
            {
                if (_gpu == null || DateTime.Now - _gpuScan > TimeSpan.FromSeconds(10))
                {
                    _gpu?.ForEach(c => c.Dispose());
                    var cat = new PerformanceCounterCategory("GPU Engine");
                    _gpu = cat.GetInstanceNames()
                        .Where(n => n.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase))
                        .Select(n => new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true))
                        .ToList();
                    foreach (var c in _gpu) { try { c.NextValue(); } catch { } }
                    _gpuScan = DateTime.Now;
                    return Gpu[^1];
                }
                double sum = 0;
                foreach (var c in _gpu) { try { sum += c.NextValue(); } catch { } }
                return Math.Clamp(sum, 0, 100);
            }
            catch { GpuAvailable = false; return 0; }
        }
    }

    public static void Release()
    {
        lock (_lock) { _gpu?.ForEach(c => c.Dispose()); _gpu = null; }
    }

    static void Push(double[] a, double v)
    {
        // Первый замер: заполняем историю им, чтобы график не прыгал от нуля
        if (a.All(x => x == 0)) { Array.Fill(a, v); return; }
        Array.Copy(a, 1, a, 0, a.Length - 1);
        a[^1] = v;
    }
}
