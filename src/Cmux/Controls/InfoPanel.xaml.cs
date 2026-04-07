using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Cmux.Core.Services;
using Cmux.ViewModels;

namespace Cmux.Controls;

public partial class InfoPanel : UserControl
{
    private readonly DispatcherTimer _refreshTimer;

    public InfoPanel()
    {
        InitializeComponent();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
            {
                Refresh();
                _refreshTimer.Start();
            }
            else
            {
                _refreshTimer.Stop();
            }
        };
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        var vm = DataContext as MainViewModel;
        if (vm == null) return;

        var perf = PerformanceMonitor.Instance;

        // Summary line
        SummaryText.Text = perf.FormatSummary();

        // Performance section
        PerfSummary.Text = $"Performance  {perf.Fps:F0} fps  {perf.MemoryMb}MB";
        var perfLines = $"FPS: {perf.Fps:F1}\nAvg render: {perf.AvgRenderMs:F2}ms\nMemory: {perf.MemoryMb}MB\nPanes: {perf.PaneCount}";
        if (perf.OutlierPaneId != null)
            perfLines += $"\nOutlier: {perf.OutlierPaneId[..8]}... ({perf.OutlierRenderMs:F1}ms)";

        var paneMetrics = perf.GetPaneMetrics();
        foreach (var (id, m) in paneMetrics)
        {
            var shortId = id.Length > 8 ? id[..8] : id;
            perfLines += $"\n  {shortId}: {m.LastRenderMs:F1}ms avg={m.AvgRenderMs:F1}ms";
        }
        PerfDetails.Text = perfLines;

        // Ports section
        PortsList.Children.Clear();
        var allPorts = new List<(PortScanner.PortInfo port, string workspace)>();
        foreach (var ws in vm.Workspaces)
        {
            foreach (var surface in ws.Surfaces)
            {
                if (surface.ShellPid is not int pid || pid <= 0) continue;
                try
                {
                    var ports = PortScanner.GetListeningPortsWithInfo(pid);
                    foreach (var p in ports)
                        allPorts.Add((p, ws.Name));
                }
                catch { }
            }
        }

        PortsSummary.Text = allPorts.Count > 0 ? $"Ports  ({allPorts.Count})" : "Ports  (none)";
        foreach (var (port, ws) in allPorts)
        {
            var pill = new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 2, 4, 2),
                Background = new SolidColorBrush(Color.FromArgb(40, 0x63, 0x66, 0xF1)),
            };
            var text = new TextBlock
            {
                Text = $":{port.Port}  {port.ProcessName}  [{ws}]",
                FontSize = 11,
                FontFamily = new FontFamily("Cascadia Mono"),
                Foreground = (Brush)FindResource("ForegroundBrush"),
            };
            pill.Child = text;
            PortsList.Children.Add(pill);
        }

        // Sessions section
        SessionsList.Children.Clear();
        int totalPanes = 0;
        int daemonPanes = 0;
        foreach (var ws in vm.Workspaces)
        {
            foreach (var surface in ws.Surfaces)
            {
                var leaves = surface.RootNode.GetLeaves();
                foreach (var leaf in leaves)
                {
                    if (leaf.PaneId == null) continue;
                    totalPanes++;
                    var session = surface.GetSession(leaf.PaneId);
                    var title = surface.GetPaneTitle(leaf.PaneId, session?.Title);
                    var mode = surface.IsDaemonPane(leaf.PaneId) ? "daemon" : "local";
                    if (surface.IsDaemonPane(leaf.PaneId)) daemonPanes++;
                    var cwd = session?.WorkingDirectory ?? "";
                    if (cwd.Length > 30) cwd = "..." + cwd[^27..];

                    var line = new TextBlock
                    {
                        Text = $"{title}  ({mode})  {cwd}",
                        FontSize = 10,
                        Foreground = (Brush)FindResource("ForegroundDimBrush"),
                        Margin = new Thickness(0, 1, 0, 1),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    SessionsList.Children.Add(line);
                }
            }
        }
        SessionsSummary.Text = $"Sessions  {totalPanes} panes  {(daemonPanes > 0 ? $"{daemonPanes} daemon" : "all local")}";

        // Git section
        GitList.Children.Clear();
        foreach (var ws in vm.Workspaces)
        {
            if (string.IsNullOrEmpty(ws.GitBranch)) continue;
            var line = new TextBlock
            {
                Text = $"{ws.Name}: {ws.GitBranch}",
                FontSize = 10,
                Foreground = (Brush)FindResource("ForegroundDimBrush"),
                Margin = new Thickness(0, 1, 0, 1),
            };
            GitList.Children.Add(line);
        }
        GitSummary.Text = $"Git  {vm.Workspaces.Count(w => !string.IsNullOrEmpty(w.GitBranch))} repos";
    }
}
