using System.Collections.Concurrent;
using System.Diagnostics;

namespace Cmux.Core.Services;

/// <summary>
/// Global performance monitoring for cmux. Tracks FPS, render times, memory usage.
/// TerminalControls report their render durations; the monitor aggregates globally.
/// </summary>
public sealed class PerformanceMonitor
{
    public static PerformanceMonitor Instance { get; } = new();

    private readonly ConcurrentDictionary<string, PaneMetrics> _paneMetrics = new();
    private long _totalFrames;
    private long _lastFpsFrames;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private DateTime _lastFpsCalc = DateTime.UtcNow;

    // Global stats (updated every second)
    public double Fps { get; private set; }
    public double AvgRenderMs { get; private set; }
    public long MemoryMb { get; private set; }
    public int PaneCount => _paneMetrics.Count;
    public string? OutlierPaneId { get; private set; }
    public double OutlierRenderMs { get; private set; }

    /// <summary>Called by TerminalControl after each render.</summary>
    public void ReportRender(string paneId, double renderMs)
    {
        Interlocked.Increment(ref _totalFrames);

        var metrics = _paneMetrics.GetOrAdd(paneId, _ => new PaneMetrics());
        metrics.LastRenderMs = renderMs;
        metrics.TotalRenders++;
        metrics.TotalRenderMs += renderMs;

        // Refresh global stats roughly every second
        var now = DateTime.UtcNow;
        if ((now - _lastFpsCalc).TotalMilliseconds >= 1000)
        {
            var frames = Interlocked.Read(ref _totalFrames);
            var delta = frames - _lastFpsFrames;
            var elapsed = (now - _lastFpsCalc).TotalSeconds;
            Fps = elapsed > 0 ? delta / elapsed : 0;
            _lastFpsFrames = frames;
            _lastFpsCalc = now;

            MemoryMb = GC.GetTotalMemory(false) / (1024 * 1024);

            // Calculate average and find outlier
            double totalMs = 0;
            int count = 0;
            string? worstPane = null;
            double worstMs = 0;

            foreach (var (id, m) in _paneMetrics)
            {
                if (m.LastRenderMs > worstMs)
                {
                    worstMs = m.LastRenderMs;
                    worstPane = id;
                }
                totalMs += m.LastRenderMs;
                count++;
            }

            AvgRenderMs = count > 0 ? totalMs / count : 0;

            // Flag outlier if > 16ms (below 60fps threshold)
            if (worstMs > 16)
            {
                OutlierPaneId = worstPane;
                OutlierRenderMs = worstMs;
            }
            else
            {
                OutlierPaneId = null;
                OutlierRenderMs = 0;
            }
        }
    }

    public void UnregisterPane(string paneId)
    {
        _paneMetrics.TryRemove(paneId, out _);
    }

    public IReadOnlyDictionary<string, PaneMetrics> GetPaneMetrics() => _paneMetrics;

    public string FormatSummary() =>
        $"{Fps:F0} fps  {AvgRenderMs:F1}ms  {MemoryMb}MB  {PaneCount} panes";

    public sealed class PaneMetrics
    {
        public double LastRenderMs { get; set; }
        public long TotalRenders { get; set; }
        public double TotalRenderMs { get; set; }
        public double AvgRenderMs => TotalRenders > 0 ? TotalRenderMs / TotalRenders : 0;
    }
}
