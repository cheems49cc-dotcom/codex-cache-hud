using System.Text.Json.Nodes;
using System.IO;
using Path = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        var subagent = new SessionState { Path = "synthetic-child", Number = 2, StartedAt = DateTimeOffset.UtcNow,
            Reader = new IncrementalJsonlReader(0), IsInternalSession = true, TurnInProgress = true };
        for (var i = 0; i < 40; i++) subagent.AddCallUsageSample(new CallUsageSample {
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(1).AddMilliseconds(i),
            Input = 100, Cached = 1, TaskTotal = 1000 + i });
        var label = (TextBlock)view.Window.FindName("CacheChartLatest");
        var line = (Polyline)view.Window.FindName("CacheHitLine");
        void Render(bool paused = false) => view.Render(settings, empty, empty, empty, empty, [state], [state, subagent], null,
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
        Check(label.Text == "99.0%" && line.Points.All(point => Math.Abs(point.Y - 1.58) < 0.001),
            "newer subagent calls neither replace main endpoint nor crowd out main history");
        Check(label.ToolTip.ToString()!.Contains("仅主 Agent"), "cache tooltip states main-only scope");
        Render(paused: true);
        Check(label.Text == "--" && label.ToolTip.ToString()!.Contains("已暂停"), "paused state is explicit");
        var title = (TextBlock)view.Window.FindName("CacheChartTitle");
        Check(title.Text == "1.2K tokens", "compact token value without a visible explanatory prefix");
        Check(title.ToolTip.ToString()!.Contains("不是计费消耗"), "metric explanation remains available on hover");
        view.Render(settings, empty, empty, empty, empty, [], [subagent], null,
            _ => "active", "active", false, true, 1234, 1234, null, null, 0, 0, 0, 0);
        Check(label.Text == "--" && line.Points.Count == 0 && label.ToolTip.ToString()!.Contains("空闲"),
            "subagent-only activity produces no main cache result");
        Check(title.Text == "1.2K tokens", "excluding subagents from cache does not change token display");
        var speed = (TextBlock)view.Window.FindName("TurnOutputRate");
        var rateStart = DateTimeOffset.UtcNow.AddSeconds(-20);
        state.TurnStartedAt = rateStart;
        state.ConversationLabel = "Synthetic speed check";
        state.OutputRate.Observe(new HudRecord { Kind = HudRecordKind.Started, Timestamp = rateStart, TurnId = "rate" });
        state.OutputRate.Observe(new HudRecord { Kind = HudRecordKind.Usage, Timestamp = rateStart.AddSeconds(10),
            Output = 1_000, Reasoning = 500, TaskOutput = 1_000, TaskTotal = 2_000 });
        view.RefreshOutputRate([state, subagent], false, rateStart.AddSeconds(20));
        Check(speed.Text == $"{50d:0.0} tok/s", "turn average is visible in transparent mode");
        Check(speed.ToolTip.ToString()!.Contains("Synthetic speed check") &&
            speed.ToolTip.ToString()!.Contains("不是实时生成速度"), "tooltip identifies the turn and wall-clock semantics");
        view.RefreshOutputRate([state, subagent], false, rateStart.AddSeconds(40));
        Check(speed.Text == $"{25d:0.0} tok/s", "average refreshes without new token data");
        view.RefreshOutputRate([state], true, rateStart.AddSeconds(40));
        Check(speed.Text == "-- tok/s" && speed.ToolTip.ToString()!.Contains("已暂停"), "pause does not expose a stale rate");
        var pending = new SessionState { Path = "pending", Number = 3, StartedAt = rateStart,
            Reader = new IncrementalJsonlReader(0), TurnInProgress = true, TurnStartedAt = rateStart.AddSeconds(1) };
        view.RefreshOutputRate([state, pending], false, rateStart.AddSeconds(40));
        Check(speed.Text == "-- tok/s", "new active turn without usage cannot inherit an older turn's rate");
        state.OutputRate.Observe(new HudRecord { Kind = HudRecordKind.Completed, Timestamp = rateStart.AddSeconds(25), TurnId = "rate" });
        state.TurnInProgress = false;
        view.RefreshOutputRate([state, subagent], false, rateStart.AddMinutes(5));
        Check(speed.Text == $"{40d:0.0} tok/s" && speed.ToolTip.ToString()!.Contains("本轮已结束"), "idle retains final main-agent average");
        view.RefreshOutputRate([subagent], false, rateStart.AddMinutes(5));
        Check(speed.Text == "-- tok/s", "subagent-only activity never produces main-agent speed");
        state.IsReadBlocked = true;
        view.RefreshOutputRate([state], false, rateStart.AddMinutes(5));
        Check(speed.Text == "-- tok/s", "locked session cannot advertise stale speed");
        state.IsReadBlocked = false;
        config["scale"] = 0.4;
        settings = HudSettings.From(config);
        Render();
        var surface = (FrameworkElement)view.Window.Content;
        surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        surface.Arrange(new Rect(surface.DesiredSize));
        surface.UpdateLayout();
        Check(speed.Visibility == Visibility.Visible && speed.ActualWidth > 0, "speed is laid out in the compact top strip");
        var hitStrip = (FrameworkElement)view.Window.FindName("TransparentControlHitStrip");
        var speedOrigin = speed.TranslatePoint(new Point(0, 0), hitStrip);
        Check(speedOrigin.Y >= 0 && speedOrigin.Y + speed.ActualHeight <= hitStrip.Height, "speed stays inside existing clickable strip");
        Check(title.TranslatePoint(new Point(title.ActualWidth, 0), hitStrip).X <= speedOrigin.X,
            "growth value and average speed do not overlap");
        if (args.Length > 1)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),
                (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.GetFullPath(args[1]));
            encoder.Save(output);
        }
        Console.WriteLine("HUD rendering checks: OK (cache thresholds, main-only scope, idle/waiting/pause, average speed, compact layout)");
    }
}
