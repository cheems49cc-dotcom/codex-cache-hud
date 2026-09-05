using System.Text.Json.Nodes;
using System.IO;
using Path = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CodexMonitorHud.App;
using CodexMonitorHud.Core.Configuration;
using CodexMonitorHud.Core.Models;
using CodexMonitorHud.Core.Sessions;
using CodexMonitorHud.Core.State;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "config.default.json")))!.AsObject();
        config["opacity"] = 0;
        config["transparencyMode"] = "background";
        var settings = HudSettings.From(config);
        using var view = new MainHudView(Path.Combine(root, "src/HudWindow.xaml"), Path.Combine(root, "src/TaskBubbleWindow.xaml"));
        var state = new SessionState { Path = "synthetic", Number = 1, StartedAt = DateTimeOffset.UtcNow,
            Reader = new IncrementalJsonlReader(0), TurnInProgress = true, TurnStartedAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        var empty = new Dictionary<string, string>();
        var label = (TextBlock)view.Window.FindName("CacheChartLatest");
        var line = (Polyline)view.Window.FindName("CacheHitLine");
        void Render(bool paused = false) => view.Render(settings, empty, empty, empty, empty, [state], [state], null,
            _ => "active", "active", paused, true, 1234, 1234, null, null, 0, 0, 0, 0);
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        foreach (var (cached, color) in new[] { (99, "#FF34C759"), (97, "#FFFFCC00"), (94, "#FFFF453A"), (89, "#FF8B0000") })
        {
            state.AddCallUsageSample(new CallUsageSample { Timestamp = DateTimeOffset.UtcNow, Input = 100, Cached = cached, TaskTotal = cached });
            Render();
            Render(); // Identical samples must not let layout overwrite the threshold color.
            Check(label.Text == $"{cached:0.0}%", "latest number matches curve endpoint");
            Check(label.Foreground.ToString() == color && line.Stroke.ToString() == color, "stable threshold color after repeated render");
        }
        state.TurnInProgress = false;
        Render();
        Check(label.Text == "--" && line.Opacity < 1, "idle result hidden; history dimmed");
        Check(label.ToolTip.ToString()!.Contains("空闲"), "hover distinguishes idle from a stalled monitor");
        state.TurnInProgress = true;
        state.TurnStartedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        Render();
        Check(label.Text == "--", "new turn waits for its first call");
        state.TurnStartedAt = DateTimeOffset.MinValue;
        for (var i = 0; i < 40; i++) state.AddCallUsageSample(new CallUsageSample { Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(i), Input = 100, Cached = 99, TaskTotal = 100 + i });
        Render();
        Check(line.Points.Count == 30, "chart retains latest thirty calls");
        Render(paused: true);
        Check(label.Text == "--" && label.ToolTip.ToString()!.Contains("已暂停"), "paused state is explicit");
        var title = (TextBlock)view.Window.FindName("CacheChartTitle");
        Check(title.Text == "1.2K tokens", "compact token value without a visible explanatory prefix");
        Check(title.ToolTip.ToString()!.Contains("不是计费消耗"), "metric explanation remains available on hover");
        Console.WriteLine("HUD rendering checks: OK (thresholds, repeated refresh, idle, waiting, latest thirty, pause, growth label)");
    }
}
