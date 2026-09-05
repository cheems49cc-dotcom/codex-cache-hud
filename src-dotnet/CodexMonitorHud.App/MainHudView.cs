using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;
using CodexMonitorHud.Core.Configuration;
using CodexMonitorHud.Core.Models;
using CodexMonitorHud.Core.Presentation;
using CodexMonitorHud.Core.Sessions;
using CodexMonitorHud.Core.State;

namespace CodexMonitorHud.App;

internal sealed class MainHudView : IDisposable
{
    private static readonly Brush TransparentHitTestBrush = BrushFactory.Convert("#01000000", "#01000000");
    private static readonly FontFamily ChartDisplayFont = new("Segoe UI Variable Display, Microsoft YaHei UI");
    private static readonly FontFamily ChartTextFont = new("Segoe UI Variable Text, Microsoft YaHei UI");
    private readonly string _taskBubbleXaml;
    private readonly BrushFactory _brushes = new();
    private readonly Grid _scaleHost;
    private readonly Border _shell;
    private readonly StackPanel _contentPanel;
    private readonly Ellipse _statusDot;
    private readonly Button _taskListToggle;
    private readonly WrapPanel _metricsPanel;
    private readonly TextBlock _cacheChartTitle;
    private readonly TextBlock _cacheChartCaption;
    private readonly TextBlock _cacheChartLatest;
    private readonly Canvas _cacheChartCanvas;
    private readonly Polyline _cacheHitLine;
    private readonly Ellipse _cacheLatestDot;
    private readonly TextBlock _cacheChartStart;
    private readonly TextBlock _cacheChartStatus;
    private readonly TextBlock _cacheChartEnd;
    private readonly TextBlock _launchTokenUsage;
    private readonly Border _cacheChartFrame;
    private readonly RowDefinition _cacheChartFooterRow;
    private readonly Border _cacheChartTopLine;
    private readonly Border _cacheChartMiddleLine;
    private readonly Border _cacheChartBottomLine;
    private readonly TextBlock _cacheChartTopLabel;
    private readonly TextBlock _cacheChartMiddleLabel;
    private readonly TextBlock _cacheChartBottomLabel;
    private readonly Border _transparentControlHitStrip;
    private readonly Button _backgroundOpacityButton;
    private readonly Button _windowSizeButton;
    private readonly Border _taskListDivider;
    private readonly ScrollViewer _taskListScroller;
    private readonly StackPanel _taskListPanel;
    private readonly Border _callHistoryDivider;
    private readonly Grid _callHistoryHeader;
    private readonly ScrollViewer _callHistoryScroller;
    private readonly StackPanel _callHistoryPanel;
    private readonly StackPanel _quietPanel;
    private readonly Grid _quietOverallHost;
    private readonly Ellipse _quietOverallRing;
    private readonly Ellipse _quietOverallDot;
    private readonly Border _quietSeparator;
    private readonly StackPanel _quietTasks;
    private readonly Dictionary<string, TaskBubbleView> _bubbles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _detached = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenStatePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MetricControl> _metricControls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _lastListAttentionRevisions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lastListExitRevisions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TaskListLiveControls> _taskListLive = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _animatedListSurfaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _animatedListContexts = new(StringComparer.OrdinalIgnoreCase);
    private Border? _summaryNoticeCard;
    private TextBlock? _summaryNoticeText;
    private nint _handle;
    private HwndSource? _windowSource;
    private int _baseStyle;
    private string _metricsSignature = string.Empty;
    private string _listSignature = string.Empty;
    private string _historySignature = string.Empty;
    private string _appearanceSignature = string.Empty;
    private string _quietSignature = string.Empty;
    private string _lastUpdateAnimationSignature = string.Empty;
    private int _lastSummaryAttentionRevision;
    private bool _summaryVisualActive;
    private FrameworkElement? _summaryAnimatedContextTarget;
    private Brush? _summaryBaseBorderBrush;
    private Thickness _summaryBaseBorderThickness;
    private bool _isMainIndicatorCollapsed;
    // This is intentionally independent from displayMode. A user can retract
    // the embedded task list without merging any detached task bubbles.
    private bool? _taskListVisibilityOverride;
    private bool _closing;
    private bool _mousePassthrough;
    private bool _hasSynchronizedStates;
    private double _currentScale = 0.78;
    private double _currentBackgroundOpacity = 0.9;
    private string _currentTransparencyMode = "uniform";
    private bool _fullyTransparentMode;

    public MainHudView(string hudXamlPath, string taskBubbleXamlPath)
    {
        _taskBubbleXaml = taskBubbleXamlPath;
        Window = XamlLoader.LoadWindow(hudXamlPath);
        _scaleHost = XamlLoader.Require<Grid>(Window, "HudScaleHost");
        _shell = XamlLoader.Require<Border>(Window, "HudShell");
        _contentPanel = XamlLoader.Require<StackPanel>(Window, "HudContentPanel");
        _statusDot = XamlLoader.Require<Ellipse>(Window, "StatusDot");
        _taskListToggle = XamlLoader.Require<Button>(Window, "TaskListToggleButton");
        _metricsPanel = XamlLoader.Require<WrapPanel>(Window, "MetricsPanel");
        _cacheChartTitle = XamlLoader.Require<TextBlock>(Window, "CacheChartTitle");
        _cacheChartCaption = XamlLoader.Require<TextBlock>(Window, "CacheChartCaption");
        _cacheChartLatest = XamlLoader.Require<TextBlock>(Window, "CacheChartLatest");
        _cacheChartCanvas = XamlLoader.Require<Canvas>(Window, "CacheChartCanvas");
        _cacheHitLine = XamlLoader.Require<Polyline>(Window, "CacheHitLine");
        _cacheLatestDot = XamlLoader.Require<Ellipse>(Window, "CacheLatestDot");
        _cacheChartStart = XamlLoader.Require<TextBlock>(Window, "CacheChartStart");
        _cacheChartStatus = XamlLoader.Require<TextBlock>(Window, "CacheChartStatus");
        _cacheChartEnd = XamlLoader.Require<TextBlock>(Window, "CacheChartEnd");
        _launchTokenUsage = XamlLoader.Require<TextBlock>(Window, "LaunchTokenUsage");
        _cacheChartFrame = XamlLoader.Require<Border>(Window, "CacheChartFrame");
        _cacheChartFooterRow = XamlLoader.Require<RowDefinition>(Window, "CacheChartFooterRow");
        _cacheChartTopLine = XamlLoader.Require<Border>(Window, "CacheChartTopLine");
        _cacheChartMiddleLine = XamlLoader.Require<Border>(Window, "CacheChartMiddleLine");
        _cacheChartBottomLine = XamlLoader.Require<Border>(Window, "CacheChartBottomLine");
        _cacheChartTopLabel = XamlLoader.Require<TextBlock>(Window, "CacheChartTopLabel");
        _cacheChartMiddleLabel = XamlLoader.Require<TextBlock>(Window, "CacheChartMiddleLabel");
        _cacheChartBottomLabel = XamlLoader.Require<TextBlock>(Window, "CacheChartBottomLabel");
        _transparentControlHitStrip = XamlLoader.Require<Border>(Window, "TransparentControlHitStrip");
        _backgroundOpacityButton = XamlLoader.Require<Button>(Window, "BackgroundOpacityButton");
        _windowSizeButton = XamlLoader.Require<Button>(Window, "WindowSizeButton");
        _taskListDivider = XamlLoader.Require<Border>(Window, "TaskListDivider");
        _taskListScroller = XamlLoader.Require<ScrollViewer>(Window, "TaskListScroller");
        _taskListPanel = XamlLoader.Require<StackPanel>(Window, "TaskListPanel");
        _callHistoryDivider = XamlLoader.Require<Border>(Window, "CallHistoryDivider");
        _callHistoryHeader = XamlLoader.Require<Grid>(Window, "CallHistoryHeader");
        _callHistoryScroller = XamlLoader.Require<ScrollViewer>(Window, "CallHistoryScroller");
        _callHistoryPanel = XamlLoader.Require<StackPanel>(Window, "CallHistoryPanel");
        _quietPanel = XamlLoader.Require<StackPanel>(Window, "QuietIndicatorPanel");
        _quietOverallHost = XamlLoader.Require<Grid>(Window, "QuietOverallHost");
        _quietOverallRing = XamlLoader.Require<Ellipse>(Window, "QuietOverallRing");
        _quietOverallDot = XamlLoader.Require<Ellipse>(Window, "QuietOverallDot");
        _quietSeparator = XamlLoader.Require<Border>(Window, "QuietIndicatorSeparator");
        _quietTasks = XamlLoader.Require<StackPanel>(Window, "QuietTaskIndicators");

        _taskListToggle.Click += (_, args) =>
        {
            ToggleListRequested?.Invoke();
            args.Handled = true;
        };
        _windowSizeButton.Click += (_, args) =>
        {
            ShowSizeMenu();
            args.Handled = true;
        };
        _backgroundOpacityButton.Click += (_, args) =>
        {
            ShowBackgroundOpacityMenu();
            args.Handled = true;
        };
        Window.MouseEnter += (_, _) => UpdateTransparentControlVisibility(pointerInside: true);
        Window.MouseLeave += (_, _) => UpdateTransparentControlVisibility(pointerInside: false);
        Window.MouseLeftButtonDown += OnWindowMouseLeftButtonDown;
        Window.SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(Window).Handle;
            _windowSource = HwndSource.FromHwnd(_handle);
            _windowSource?.AddHook(OnWindowMessage);
            _baseStyle = _handle == 0 ? 0 : NativeMethods.GetWindowLong(_handle, NativeMethods.GwlExStyle);
            SetMousePassthrough(_mousePassthrough);
        };
        Window.Closing += (_, args) =>
        {
            if (!_closing)
            {
                args.Cancel = true;
                ExitRequested?.Invoke();
            }
        };
    }

    public Window Window { get; }
    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public event Action? ToggleListRequested;
    public event Action<string>? DismissRequested;
    public event Action<string>? OpenRequested;
    public event Action<double, double>? PositionChanged;
    public event Action<double>? ScaleChanged;
    public event Action<double>? BackgroundOpacityChanged;
    public event Action<string, bool>? DetachedChanged;
    public event Action<string, string, string>? AttentionPresented;
    public bool HoldTerminalExits =>
        _isMainIndicatorCollapsed && (_lastIdleIndicatorLayout is "horizontal" or "vertical");

    private string _lastIdleIndicatorLayout = "overall";

    public void Render(
        HudSettings settings,
        IReadOnlyDictionary<string, string> locale,
        IReadOnlyDictionary<string, string> settingsLocale,
        IReadOnlyDictionary<string, string> zhLocale,
        IReadOnlyDictionary<string, string> enLocale,
        IReadOnlyList<SessionState> states,
        IReadOnlyList<SessionState> usageStates,
        HudSnapshot? snapshot,
        Func<SessionState, string> statusFor,
        string overallStatus,
        bool paused,
        bool initialScanComplete,
        long launchContextTokens,
        long currentContextTokens,
        double? weeklyUsedPercent,
        double? fiveHourUsedPercent,
        long launchTotalTokens,
        long launchInputTokens,
        long launchCachedTokens,
        long launchOutputTokens)
    {
        var now = DateTimeOffset.Now;
        var hasAttention = states.Any(state => state.AttentionUntil > now);
        ApplyAppearance(settings, overallStatus, hasAttention);
        var contextText = $"{FormatTokenCount(launchContextTokens)} tokens";
        _launchTokenUsage.Text = contextText;
        _launchTokenUsage.ToolTip = $"HUD 打开后上下文正增长 {launchContextTokens:N0} tokens（主/子 Agent 与同时打开项目）\n启动从零累计；上下文压缩不倒扣；关闭后清零。此数值不是计费消耗。\n当前上下文合计 {currentContextTokens:N0}\nOpenAI/Codex 周额度已用 {(weeklyUsedPercent.HasValue ? $"{weeklyUsedPercent.Value:0.#}%" : "--")} · 5小时已用 {(fiveHourUsedPercent.HasValue ? $"{fiveHourUsedPercent.Value:0.#}%" : "--")}\nHUD 打开后原始 tokens {launchTotalTokens:N0}\n输入 {launchInputTokens:N0} · 缓存 {launchCachedTokens:N0} · 输出 {launchOutputTokens:N0}";
        ApplyTransparentLayout(settings, contextText);
        RenderCacheChart(settings, usageStates, paused);
        _taskListToggle.Visibility = Visibility.Collapsed;
        _taskListDivider.Visibility = Visibility.Collapsed;
        _taskListScroller.Visibility = Visibility.Collapsed;
        _callHistoryDivider.Visibility = Visibility.Collapsed;
        _callHistoryHeader.Visibility = Visibility.Collapsed;
        _callHistoryScroller.Visibility = Visibility.Collapsed;
        _quietPanel.Visibility = Visibility.Collapsed;
        UpdatePosition(settings);
    }

    private void RenderCacheChart(
        HudSettings settings,
        IReadOnlyList<SessionState> states,
        bool paused)
    {
        var samples = states
            .SelectMany(static state => state.CallHistory)
            .OrderBy(static sample => sample.Timestamp)
            .TakeLast(30)
            .ToArray();
        var hasActiveTurn = states.Any(state => state.TurnInProgress);
        var hasCurrentResult = !paused && states.Any(state => state.TurnInProgress &&
            state.CallHistory.Any(sample => sample.Timestamp >= state.TurnStartedAt));
        var status = paused ? "已暂停" : !hasActiveTurn ? "空闲" : hasCurrentResult ? "监控中" : "等待调用数据";
        var latestAt = samples.Length > 0 ? samples[^1].Timestamp : (DateTimeOffset?)null;
        _cacheChartLatest.ToolTip = latestAt.HasValue
            ? $"{status} · 最近调用距今 {Math.Max(0, (DateTimeOffset.Now - latestAt.Value).TotalSeconds):0} 秒\n最后一次调用：{latestAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n命中率 = 该次缓存输入 / 该次全部输入；包含主/子 Agent。\n曲线每点代表一次调用，不是固定时间间隔。"
            : $"{status} · 尚无调用数据";

        if (samples.Length == 0)
        {
            _cacheHitLine.Points = [];
            _cacheLatestDot.Visibility = Visibility.Collapsed;
            _cacheChartLatest.Text = "--";
            _cacheChartStart.Text = "--:--:--";
            _cacheChartEnd.Text = "--:--:--";
            _cacheChartStatus.Text = status;
            _cacheChartCaption.Text = "最近 30 次调用 · 实时";
            return;
        }

        const double width = 620;
        const double height = 158;
        var points = new PointCollection(samples.Length);
        for (var index = 0; index < samples.Length; index++)
        {
            var x = samples.Length == 1 ? width : index * width / (samples.Length - 1);
            var y = (100 - Math.Clamp(samples[index].CacheHitPercent, 0, 100)) * height / 100;
            points.Add(new Point(x, y));
        }
        _cacheHitLine.Points = points;

        var latest = samples[^1];
        var color = HudFormatting.GetCacheHitColor(latest.CacheHitPercent);
        var stroke = _brushes.Create(color, color, BrushRole.Status, settings, "listening", latest.AlertState is not AlertState.GOOD and not AlertState.OK);
        _cacheHitLine.Stroke = stroke;
        _cacheHitLine.Opacity = hasCurrentResult ? 1 : 0.35;
        _cacheLatestDot.Fill = stroke;
        _cacheLatestDot.Visibility = hasCurrentResult ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(_cacheLatestDot, points[^1].X - _cacheLatestDot.Width / 2);
        Canvas.SetTop(_cacheLatestDot, points[^1].Y - _cacheLatestDot.Height / 2);
        _cacheChartLatest.Text = !hasCurrentResult || latest.Input <= 0
            ? "--"
            : $"{latest.CacheHitPercent:0.0}%";
        _cacheChartLatest.Foreground = hasCurrentResult
            ? stroke
            : _brushes.Create(settings.Muted, "#FF667085", BrushRole.Secondary, settings, "idle", false);
        _cacheChartStart.Text = samples[0].Timestamp.ToLocalTime().ToString("HH:mm:ss");
        _cacheChartEnd.Text = latest.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        _cacheChartStatus.Text = $"{status} · {samples.Length}/30";
        _cacheChartCaption.Text = "最近 30 次调用 · 缓存命中率";
    }

    private void ApplyTransparentLayout(HudSettings settings, string contextText)
    {
        var enabled = IsFullyTransparent(settings);
        _fullyTransparentMode = enabled;
        _scaleHost.Background = null;
        _transparentControlHitStrip.Background = enabled ? TransparentHitTestBrush : Brushes.Transparent;
        _cacheChartTitle.Text = enabled
            ? contextText
            : "Codex 缓存命中率";
        _cacheChartTitle.FontSize = enabled ? 28 : 15;
        _cacheChartTitle.FontFamily = enabled ? ChartDisplayFont : ChartTextFont;
        _cacheChartTitle.Opacity = 1;
        _cacheChartTitle.Foreground = enabled
            ? _brushes.Create("#FF34C759", "#FF34C759", BrushRole.Status, settings, "listening", false)
            : _brushes.Create(settings.Foreground, "#FF111827", BrushRole.Primary, settings, "listening", false);
        _cacheChartTitle.ToolTip = enabled ? _launchTokenUsage.ToolTip : null;
        _cacheChartCaption.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _launchTokenUsage.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _cacheChartStart.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _cacheChartStatus.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _cacheChartEnd.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _cacheChartTopLine.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _cacheChartMiddleLine.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _cacheChartBottomLine.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _cacheChartTopLabel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _cacheChartMiddleLabel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _cacheChartBottomLabel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _cacheChartFooterRow.Height = enabled ? new GridLength(0) : new GridLength(22);
        _cacheChartFrame.Background = enabled ? Brushes.Transparent : BrushFactory.Convert("#060A84FF", "#060A84FF");
        _cacheChartFrame.BorderBrush = enabled ? Brushes.Transparent : BrushFactory.Convert("#180A84FF", "#180A84FF");
        _cacheChartFrame.BorderThickness = enabled ? new Thickness(0) : new Thickness(1);
        if (enabled)
        {
            _shell.Background = Brushes.Transparent;
            _shell.BorderThickness = new Thickness(0);
        }
        UpdateTransparentControlVisibility(Window.IsMouseOver);
    }

    private void UpdateTransparentControlVisibility(bool pointerInside)
    {
        var visibility = !_fullyTransparentMode || pointerInside ? Visibility.Visible : Visibility.Collapsed;
        _backgroundOpacityButton.Visibility = visibility;
        _windowSizeButton.Visibility = visibility;
    }

    private static bool IsFullyTransparent(HudSettings settings) =>
        settings.TransparencyMode == "background" && settings.Opacity <= 0.001;

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int wmNcHitTest = 0x0084;
        const int htTransparent = -1;
        if (!_fullyTransparentMode || message != wmNcHitTest)
        {
            return 0;
        }

        var packed = lParam.ToInt64();
        var point = Window.PointFromScreen(new Point(
            unchecked((short)(packed & 0xFFFF)),
            unchecked((short)((packed >> 16) & 0xFFFF))));
        var origin = _transparentControlHitStrip.TranslatePoint(new Point(0, 0), Window);
        var insideTopStrip = point.X >= origin.X &&
                             point.X <= origin.X + _transparentControlHitStrip.ActualWidth &&
                             point.Y >= origin.Y &&
                             point.Y <= origin.Y + _transparentControlHitStrip.ActualHeight;
        if (!insideTopStrip)
        {
            handled = true;
            return htTransparent;
        }
        return 0;
    }

    public void MergeAll()
    {
        foreach (var bubble in _bubbles.Values)
        {
            bubble.Dispose();
        }
        _bubbles.Clear();
        _detached.Clear();
        _taskListVisibilityOverride = null;
        _listSignature = string.Empty;
        _historySignature = string.Empty;
    }

    public void SplitAll(IReadOnlyList<SessionState> states, int maximum)
    {
        foreach (var state in states.OrderByDescending(static state => state.LastWriteTimeUtc).Take(maximum))
        {
            _detached.Add(state.Path);
        }
        _hasSynchronizedStates = true;
        foreach (var state in states) _seenStatePaths.Add(state.Path);
        _listSignature = string.Empty;
        _historySignature = string.Empty;
    }

    public void ToggleTaskListVisibility(HudSettings settings)
    {
        _taskListVisibilityOverride = !IsTaskListVisible(settings);
        _listSignature = string.Empty;
        _historySignature = string.Empty;
    }

    public void ResetTaskListVisibility()
    {
        _taskListVisibilityOverride = null;
        _listSignature = string.Empty;
        _historySignature = string.Empty;
    }

    public void RemoveState(string path)
    {
        _detached.Remove(path);
        _lastListAttentionRevisions.Remove(path);
        _lastListExitRevisions.Remove(path);
        if (_bubbles.Remove(path, out var bubble))
        {
            bubble.Dispose();
        }
        _listSignature = string.Empty;
        _historySignature = string.Empty;
    }

    public void SetMousePassthrough(bool enabled)
    {
        _mousePassthrough = enabled;
        if (_handle != 0)
        {
            var current = NativeMethods.GetWindowLong(_handle, NativeMethods.GwlExStyle);
            var next = enabled
                ? current | NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate
                : (current & ~NativeMethods.WsExTransparent & ~NativeMethods.WsExNoActivate) |
                  (_baseStyle & (NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate));
            if (next != current)
            {
                _ = NativeMethods.SetWindowLong(_handle, NativeMethods.GwlExStyle, next);
            }
        }
        foreach (var bubble in _bubbles.Values)
        {
            bubble.SetMousePassthrough(enabled);
        }
    }

    public void RefreshQuietMode(
        HudSettings settings,
        IReadOnlyDictionary<string, string> locale,
        IReadOnlyList<SessionState> states,
        Func<SessionState, string> statusFor,
        string overallStatus,
        DateTimeOffset now)
    {
        UpdateQuietMode(settings, locale, states, statusFor, overallStatus, now);
        PositionBubbles(settings);
        UpdatePosition(settings);
    }

    public void Dispose()
    {
        _closing = true;
        _windowSource?.RemoveHook(OnWindowMessage);
        _windowSource = null;
        MergeAll();
        Window.Close();
    }

    private void ApplyAppearance(HudSettings settings, string status, bool hasAttention)
    {
        var signature = string.Join('|',
            settings.Preset,
            settings.Layout,
            settings.Background,
            settings.Foreground,
            settings.Muted,
            settings.Border,
            settings.Accent,
            settings.FontSize,
            settings.Scale,
            settings.CornerRadius,
            settings.Opacity,
            settings.TransparencyMode,
            settings.AlwaysOnTop,
            settings.MousePassthrough,
            settings.ShowStatusDot,
            settings.ThemeStyle,
            status,
            hasAttention);
        if (_appearanceSignature == signature)
        {
            return;
        }
        _appearanceSignature = signature;
        _metricsSignature = string.Empty;
        _listSignature = string.Empty;
        Window.Topmost = settings.AlwaysOnTop;
        ApplyScale(settings.Scale);
        _currentBackgroundOpacity = settings.Opacity;
        _currentTransparencyMode = settings.TransparencyMode;
        _backgroundOpacityButton.ToolTip = settings.TransparencyMode == "background"
            ? $"调整背景可见度（当前 {settings.Opacity:P0}）"
            : "调整背景可见度";
        Window.Opacity = settings.TransparencyMode == "uniform" ? settings.Opacity : 1;
        try
        {
            Window.FontFamily = new FontFamily(settings.ThemeStyle.FontFamily);
        }
        catch (ArgumentException)
        {
        }
        _shell.CornerRadius = new CornerRadius(settings.CornerRadius);
        _shell.BorderBrush = _brushes.Create(settings.Border, "#22FFFFFF", BrushRole.Decoration, settings, status, hasAttention);
        _shell.BorderThickness = new Thickness(settings.ThemeStyle.BorderWidth);
        _summaryBaseBorderBrush = _shell.BorderBrush;
        _summaryBaseBorderThickness = _shell.BorderThickness;
        _shell.Background = _brushes.CreateSurface(settings, status, hasAttention);
        _statusDot.Fill = _brushes.Create(StatusColor(settings, status), "#FF8E8E93", BrushRole.Status, settings, status, hasAttention);
        _statusDot.Width = settings.ThemeStyle.StatusDotSize;
        _statusDot.Height = settings.ThemeStyle.StatusDotSize;
        _statusDot.Visibility = settings.ShowStatusDot ? Visibility.Visible : Visibility.Collapsed;
        _metricsPanel.Orientation = settings.Layout == "stacked" ? Orientation.Vertical : Orientation.Horizontal;
        _shell.Padding = settings.Layout == "stacked" ? new Thickness(16, 13, 16, 13) : new Thickness(14, 10, 14, 10);
        SetMousePassthrough(settings.MousePassthrough);
        if (!hasAttention && _summaryVisualActive)
        {
            ResetSummaryAttentionVisual();
        }
    }

    private void RenderMetrics(
        HudSettings settings,
        IReadOnlyDictionary<string, string> locale,
        IReadOnlyList<SessionState> states,
        HudSnapshot? snapshot,
        bool paused,
        bool initialScanComplete)
    {
        var metrics = paused || snapshot is null
            ? Array.Empty<HudMetric>()
            : HudFormatting.GetSummaryMetrics(snapshot, settings.Fields, locale, settings.NumberFormat).ToArray();
        metrics = AddSourceBreakdown(metrics, states, locale);
        if (metrics.Length == 0)
        {
            var text = paused
                ? Get(locale, "paused")
                : initialScanComplete && states.Count == 0
                    ? Get(locale, "noActiveTasks")
                    : Get(locale, "waiting");
            var signature = "waiting|" + text + '|' + settings.Layout + '|' + settings.FontSize + '|' + _appearanceSignature;
            if (_metricsSignature == signature && _metricControls.TryGetValue("__waiting", out var existing))
            {
                existing.Value.Text = text;
                return;
            }
            _metricsPanel.Children.Clear();
            _summaryNoticeCard = null;
            _summaryNoticeText = null;
            _metricControls.Clear();
            var waiting = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI"),
                FontSize = settings.FontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = _brushes.Create(settings.Foreground, "#FFFFFFFF", BrushRole.Primary, settings, "idle", false),
                VerticalAlignment = VerticalAlignment.Center
            };
            _metricsPanel.Children.Add(waiting);
            _metricControls["__waiting"] = new MetricControl(waiting, waiting, null);
            _metricsSignature = signature;
            return;
        }

        var structure = string.Join(';', metrics.Select(metric => metric.Key + ':' + metric.Label));
        var metricsSignature = structure + '|' + settings.Layout + '|' + settings.FontSize + '|' + _appearanceSignature;
        if (_metricsSignature == metricsSignature && metrics.All(metric => _metricControls.ContainsKey(metric.Key)))
        {
            foreach (var metric in metrics)
            {
                _metricControls[metric.Key].Label.Text = metric.Label;
                _metricControls[metric.Key].Value.Text = metric.Value;
            }
            return;
        }

        _metricsPanel.Children.Clear();
        _summaryNoticeCard = null;
        _summaryNoticeText = null;
        _metricControls.Clear();
        foreach (var metric in metrics)
        {
            _metricControls[metric.Key] = AddMetric(metric, settings);
        }
        _metricsSignature = metricsSignature;
    }

    private void RenderSummaryNotice(
        HudSettings settings,
        IReadOnlyDictionary<string, string> locale,
        IReadOnlyList<SessionState> states,
        DateTimeOffset now)
    {
        var state = settings.MultiTask.DisplayMode == "summary"
            ? states.Where(state => state.AgentNoticeUntil > now && !string.IsNullOrWhiteSpace(state.AgentNoticeText))
                .OrderByDescending(static state => state.AttentionRevision)
                .FirstOrDefault()
            : null;
        if (state is null)
        {
            if (_summaryNoticeCard is not null)
            {
                _metricsPanel.Children.Remove(_summaryNoticeCard);
                _summaryNoticeCard = null;
                _summaryNoticeText = null;
            }
            return;
        }

        if (_summaryNoticeCard is null || !_metricsPanel.Children.Contains(_summaryNoticeCard))
        {
            _summaryNoticeText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 430,
                FontWeight = FontWeights.SemiBold
            };
            _summaryNoticeCard = new Border
            {
                CornerRadius = new CornerRadius(Math.Max(8, settings.CornerRadius - 8)),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(7, 0, 0, 0),
                BorderThickness = new Thickness(1),
                Child = _summaryNoticeText
            };
            _metricsPanel.Children.Add(_summaryNoticeCard);
        }
        _summaryNoticeCard.CornerRadius = new CornerRadius(Math.Max(8, settings.CornerRadius - 8));
        _summaryNoticeCard.BorderBrush = BrushFactory.Convert(settings.AgentNotifications.Color, "#FF7C3AED");
        _summaryNoticeCard.Background = _brushes.Create("#167C3AED", "#167C3AED", BrushRole.Decoration, settings, "active", true);
        _summaryNoticeText!.Text = $"\u2726 {Get(locale, "agentNotificationBadge")} #{state.Number}  {state.AgentNoticeText}";
        _summaryNoticeText.Foreground = _brushes.Create(settings.Foreground, "#FFF7FBFF", BrushRole.Primary, settings, "active", true);
    }

    private MetricControl AddMetric(HudMetric metric, HudSettings settings)
    {
        if (settings.Layout == "inline" && _metricsPanel.Children.Count > 0)
        {
            _metricsPanel.Children.Add(new TextBlock
            {
                Text = settings.Separator == "bar" ? "|" : "\u00B7",
                Margin = new Thickness(7, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = _brushes.Create(settings.Muted, "#FF8A94A6", BrushRole.Secondary, settings, "idle", false),
                FontSize = settings.FontSize
            });
        }
        var label = new TextBlock
        {
            Text = metric.Label,
            FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI"),
            FontSize = Math.Max(10, settings.FontSize - 2),
            Foreground = _brushes.Create(settings.Muted, "#FF8A94A6", BrushRole.Secondary, settings, "idle", false),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = settings.Layout == "cards" ? new Thickness(0, 0, 0, 2) : new Thickness(0, 0, 6, 0)
        };
        var value = new TextBlock
        {
            Text = metric.Value,
            FontFamily = label.FontFamily,
            FontSize = settings.FontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = _brushes.Create(settings.Foreground, "#FFFFFFFF", BrushRole.Primary, settings, "idle", false),
            VerticalAlignment = VerticalAlignment.Center
        };
        var content = new StackPanel
        {
            Orientation = settings.Layout == "cards" ? Orientation.Vertical : Orientation.Horizontal
        };
        content.Children.Add(label);
        content.Children.Add(value);
        var container = new Border { Child = content, VerticalAlignment = VerticalAlignment.Center };
        var accent = ParseColor(settings.Accent, "#FF0A84FF");
        switch (settings.Layout)
        {
            case "chips":
                container.Background = ColorBrush(accent, 24);
                container.CornerRadius = new CornerRadius(Math.Max(8, settings.CornerRadius - 10));
                container.Padding = new Thickness(10, 6, 10, 6);
                container.Margin = new Thickness(0, 0, 6, 0);
                break;
            case "compact":
                container.Background = ColorBrush(accent, 18);
                container.CornerRadius = new CornerRadius(Math.Max(7, settings.CornerRadius - 12));
                container.Padding = new Thickness(7, 4, 7, 4);
                container.Margin = new Thickness(0, 0, 4, 0);
                break;
            case "outline":
                container.Background = ColorBrush(accent, 8);
                container.BorderBrush = ColorBrush(accent, 82);
                container.BorderThickness = new Thickness(1);
                container.CornerRadius = new CornerRadius(Math.Max(8, settings.CornerRadius - 10));
                container.Padding = new Thickness(9, 5, 9, 5);
                container.Margin = new Thickness(0, 0, 6, 0);
                break;
            case "cards":
                container.Background = ColorBrush(accent, 16);
                container.BorderBrush = ColorBrush(accent, 42);
                container.BorderThickness = new Thickness(1);
                container.CornerRadius = new CornerRadius(Math.Max(9, settings.CornerRadius - 8));
                container.Padding = new Thickness(11, 8, 11, 8);
                container.Margin = new Thickness(0, 0, 6, 0);
                break;
            case "stacked":
                container.Padding = new Thickness(4, 3, 4, 3);
                container.Margin = new Thickness(0, 0, 0, 2);
                break;
        }
        _metricsPanel.Children.Add(container);
        return new MetricControl(label, value, container);
    }

    private void RenderTaskList(
        HudSettings settings,
        IReadOnlyDictionary<string, string> locale,
        IReadOnlyDictionary<string, string> metricLocale,
        IReadOnlyList<SessionState> states,
        Func<SessionState, string> statusFor,
        DateTimeOffset now,
        bool visible)
    {
        var stateSignature = visible
            ? string.Join(';', states.Select(state => string.Join(':',
                state.SessionId.Length > 0 ? state.SessionId : state.Path,
                state.Number,
                statusFor(state),
                _detached.Contains(state.Path),
                state.Workspace,
                state.ConversationLabel,
                state.ProfileId,
                state.ClientSurface,
                state.ModelProvider)))
            : string.Empty;
        var signature = string.Join('|', visible, settings.MultiTask.ListStyle, settings.MultiTask.ListDensity,
            settings.MultiTask.ListDetail, settings.MultiTask.NameMode,
            _appearanceSignature, stateSignature);
        if (_listSignature == signature)
        {
            UpdateTaskListLive(settings, locale, metricLocale, states, statusFor, now);
            return;
        }
        _listSignature = signature;
        _taskListPanel.Children.Clear();
        _taskListLive.Clear();
        _animatedListSurfaces.Clear();
        _animatedListContexts.Clear();
        _lastListAttentionRevisions.Clear();
        _lastListExitRevisions.Clear();
        _taskListScroller.Visibility = visible && states.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _taskListDivider.Visibility = _taskListScroller.Visibility;
        if (!visible)
        {
            return;
        }

        var density = Density(settings.MultiTask.ListDensity);
        foreach (var state in states.OrderBy(static state => state.Number))
        {
            var status = statusFor(state);
            var row = new Grid
            {
                Margin = density.RowMargin,
                Background = _brushes.Create("#08000000", "#08000000", BrushRole.Decoration, settings, status, state.AttentionUntil > now)
            };
            foreach (var width in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            }
            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Margin = density.DotMargin,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = _brushes.Create(StatusColor(settings, status), "#FF8E8E93", BrushRole.Status, settings, status, state.AttentionUntil > now)
            };
            Grid.SetColumn(dot, 0);
            row.Children.Add(dot);

            var sourceLabel = GetSourceLabel(state, locale);
            var sourceColor = GetSourceColor(state, settings);
            var sourceIcon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(GetSourceGeometry(state)),
                Stroke = _brushes.Create(sourceColor, "#FF64748B", BrushRole.Primary, settings, status, false),
                StrokeThickness = string.Equals(state.ClientSurface, "vscode", StringComparison.OrdinalIgnoreCase) ? 0.45 : 1.45,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            if (string.Equals(state.ClientSurface, "vscode", StringComparison.OrdinalIgnoreCase))
            {
                sourceIcon.Fill = sourceIcon.Stroke;
            }
            var sourceViewbox = new Viewbox { Width = 14, Height = 14, Child = sourceIcon };
            var sourceBadge = new Border
            {
                CornerRadius = new CornerRadius(density.BadgeRadius),
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(0, 1, 6, 1),
                Background = ColorBrush(ParseColor(sourceColor, "#FF64748B"), 24),
                BorderBrush = ColorBrush(ParseColor(sourceColor, "#FF64748B"), 72),
                BorderThickness = new Thickness(1),
                ToolTip = sourceLabel,
                Child = sourceViewbox
            };
            Grid.SetColumn(sourceBadge, 1);
            row.Children.Add(sourceBadge);

            var badgeText = new TextBlock
            {
                Text = $"#{state.Number}",
                FontWeight = FontWeights.SemiBold,
                Foreground = _brushes.Create(settings.Accent, "#FF0A84FF", BrushRole.Primary, settings, status, false)
            };
            var badge = new Border
            {
                CornerRadius = new CornerRadius(density.BadgeRadius),
                Background = BrushFactory.Convert("#120A84FF", "#120A84FF"),
                Padding = density.BadgePadding,
                Margin = density.BadgeMargin,
                ToolTip = GetDisplayName(state, settings, locale, includeNumber: true),
                Child = badgeText
            };
            Grid.SetColumn(badge, 2);
            row.Children.Add(badge);

            var projectName = ProjectName(state, locale);
            var collapsedSubtitle = state.StartedAt.ToLocalTime().ToString("HH:mm");
            var expandedSubtitle = string.IsNullOrWhiteSpace(state.ConversationLabel) || settings.MultiTask.NameMode == "hidden"
                ? collapsedSubtitle
                : state.ConversationLabel + " \u00B7 " + collapsedSubtitle;
            var name = new TextBlock
            {
                Text = projectName,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = _brushes.Create(settings.Foreground, "#FF111827", BrushRole.Primary, settings, status, false)
            };
            var subtitle = new TextBlock
            {
                Text = settings.MultiTask.NameMode == "hidden" ? collapsedSubtitle : expandedSubtitle,
                Margin = new Thickness(0, 1, 0, 0),
                FontSize = Math.Max(9, settings.FontSize - 3),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = _brushes.Create(settings.Muted, "#FF667085", BrushRole.Secondary, settings, status, false)
            };
            var identity = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 12, 2),
                MaxWidth = 280,
                ToolTip = GetDisplayName(state, settings, locale)
            };
            identity.Children.Add(name);
            identity.Children.Add(subtitle);
            Grid.SetColumn(identity, 3);
            row.Children.Add(identity);
            var listMetrics = WithAgentNotice(
                state,
                GetTaskMetricsText(state, settings, metricLocale, status, listPreset: true),
                locale,
                now,
                !_detached.Contains(state.Path));
            var metricsText = new TextBlock
            {
                Text = listMetrics,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = _brushes.Create(settings.Muted, "#FF667085", BrushRole.Secondary, settings, status, false),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            metricsText.ToolTip = metricsText.Text;
            var metricsHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
            metricsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            metricsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Border? contextMetric = null;
            TextBlock? contextTextControl = null;
            {
                var contextValue = state.Snapshot is null
                    ? Get(locale, "waiting")
                    : state.Snapshot.ContextWindow > 0
                        ? HudFormatting.FormatPercent(state.Snapshot.ContextPercent)
                        : "--";
                var contextText = new TextBlock
                {
                    Text = state.Snapshot is null ? contextValue : $"{Get(locale, "context")} {contextValue}",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = _brushes.Create(settings.Foreground, "#FF111827", BrushRole.Primary, settings, status, false)
                };
                contextTextControl = contextText;
                contextMetric = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 7, 0),
                    BorderThickness = new Thickness(1),
                    BorderBrush = _brushes.Create("#330A84FF", "#330A84FF", BrushRole.Decoration, settings, status, false),
                    Background = _brushes.Create("#0D0A84FF", "#0D0A84FF", BrushRole.Decoration, settings, status, false),
                    ToolTip = BuildContextTooltip(state, locale),
                    Child = contextText
                };
                Grid.SetColumn(contextMetric, 0);
                metricsHost.Children.Add(contextMetric);
            }
            Grid.SetColumn(metricsText, 1);
            metricsHost.Children.Add(metricsText);
            Grid.SetColumn(metricsHost, 4);
            row.Children.Add(metricsHost);

            var detached = _detached.Contains(state.Path);
            var action = NewIconButton(
                detached ? MergeGeometry : DetachGeometry,
                settings.Accent,
                density.ActionSize,
                density.ActionMargin,
                detached ? Get(locale, "mergeTask") : Get(locale, "detachTask"));
            action.Click += (_, _) => SetDetached(state.Path, !detached, settings.MultiTask.MaxSplitBubbles);
            Grid.SetColumn(action, 5);
            row.Children.Add(action);
            var dismiss = NewIconButton(
                DismissGeometry,
                settings.Muted,
                density.ActionSize,
                new Thickness(1, 0, 2, 0),
                Get(locale, "dismissTask"));
            dismiss.Click += (_, _) => DismissRequested?.Invoke(state.Path);
            Grid.SetColumn(dismiss, 6);
            row.Children.Add(dismiss);

            var detailedLayout = settings.MultiTask.ListDetail == "detailed";
            if (detailedLayout)
            {
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                foreach (var control in new FrameworkElement[] { dot, sourceBadge, badge, action, dismiss })
                {
                    Grid.SetRowSpan(control, 2);
                }
                Grid.SetRow(metricsHost, 1);
                Grid.SetColumn(metricsHost, 3);
                Grid.SetColumnSpan(metricsHost, 2);
                metricsHost.Margin = density.MetricsMargin;
                metricsText.TextWrapping = TextWrapping.Wrap;
                metricsText.TextTrimming = TextTrimming.None;
            }

            FrameworkElement listItem = row;
            if (settings.MultiTask.ListStyle == "cards")
            {
                row.Background = Brushes.Transparent;
                if (!detailedLayout)
                {
                    row.RowDefinitions.Add(new RowDefinition());
                    row.RowDefinitions.Add(new RowDefinition());
                    Grid.SetRowSpan(dot, 2);
                    Grid.SetRow(metricsHost, 1);
                    Grid.SetColumn(metricsHost, 1);
                    Grid.SetColumnSpan(metricsHost, 4);
                    metricsHost.Margin = density.MetricsMargin;
                    metricsText.TextWrapping = TextWrapping.Wrap;
                }
                listItem = new Border
                {
                    CornerRadius = new CornerRadius(density.CardRadius),
                    Padding = density.CardPadding,
                    Margin = density.CardMargin,
                    Background = _brushes.Create("#0D0A84FF", "#0D0A84FF", BrushRole.Decoration, settings, status, false),
                    BorderBrush = _brushes.Create("#220A84FF", "#220A84FF", BrushRole.Decoration, settings, status, false),
                    BorderThickness = new Thickness(1),
                    Child = row
                };
            }
            else if (settings.MultiTask.ListStyle == "rail")
            {
                row.Background = Brushes.Transparent;
                dot.Visibility = Visibility.Collapsed;
                var railGrid = new Grid
                {
                    Margin = density.RailMargin,
                    Background = _brushes.Create("#08000000", "#08000000", BrushRole.Decoration, settings, status, false)
                };
                railGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                railGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var rail = new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Margin = density.RailInnerMargin,
                    Background = _brushes.Create(StatusColor(settings, status), "#FF8E8E93", BrushRole.Status, settings, status, state.AttentionUntil > now)
                };
                railGrid.Children.Add(rail);
                row.Margin = new Thickness(density.RailContentLeft, 0, 0, 0);
                Grid.SetColumn(row, 1);
                railGrid.Children.Add(row);
                listItem = railGrid;
            }

            var attentionSurface = new Border
            {
                CornerRadius = new CornerRadius(Math.Max(9, density.CardRadius)),
                BorderThickness = new Thickness(0),
                Child = listItem,
                ToolTip = settings.Behavior.OpenTaskOnDoubleClick && CanOpenTask(state)
                    ? Get(locale, "openTaskTooltip")
                    : null
            };
            attentionSurface.MouseLeftButtonDown += (_, args) =>
            {
                if (args.ClickCount >= 2 && CanOpenTask(state))
                {
                    OpenRequested?.Invoke(state.Path);
                    args.Handled = true;
                }
            };
            _taskListPanel.Children.Add(attentionSurface);
            var live = new TaskListLiveControls(dot, name, subtitle, metricsText, contextTextControl, contextMetric, attentionSurface);
            _taskListLive[state.Path] = live;
            UpdateTaskListLiveEntry(settings, locale, metricLocale, state, status, live, now);
        }
    }

    private bool IsTaskListVisible(HudSettings settings) =>
        _taskListVisibilityOverride ?? settings.MultiTask.DisplayMode == "list";

    private void RenderCallHistory(
        HudSettings settings,
        IReadOnlyList<SessionState> states,
        bool expanded)
    {
        var state = states
            .Where(static item => item.CallHistory.Count > 0)
            .OrderByDescending(static item => item.LastUsageAt)
            .FirstOrDefault();
        var samples = state?.CallHistory.TakeLast(20).Reverse().ToArray() ?? [];
        var visible = expanded && samples.Length > 0;
        var signature = visible
            ? string.Join('|', new[]
            {
                state!.Path,
                state.CallHistory.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.Join(';', samples.Select(sample => string.Join(':',
                    sample.Timestamp.ToUnixTimeMilliseconds(), sample.Input, sample.Cached, sample.CacheWrite,
                    sample.Output, sample.ContextTokens, sample.ContextWindow, sample.AlertState)))
            })
            : string.Empty;
        if (_historySignature == signature)
        {
            return;
        }
        _historySignature = signature;
        _callHistoryPanel.Children.Clear();
        _callHistoryDivider.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _callHistoryHeader.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _callHistoryScroller.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
        {
            return;
        }

        foreach (var sample in samples)
        {
            var row = new Grid { Margin = new Thickness(3, 2, 3, 2) };
            foreach (var width in new[]
                     {
                         new GridLength(100), new GridLength(125), new GridLength(145),
                         new GridLength(120), new GridLength(120), new GridLength(115),
                         new GridLength(1, GridUnitType.Star)
                     })
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            }

            var alertColor = sample.AlertState switch
            {
                AlertState.CACHE_MISS or AlertState.VERY_LOW_CACHE or AlertState.CACHE_STUCK => "#FFFF453A",
                AlertState.SUDDEN_CACHE_DROP or AlertState.LOW_CACHE => "#FFFF9F0A",
                _ => "#FF34C759"
            };
            var values = new[]
            {
                sample.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                sample.Input <= 0 ? "--" : $"{sample.CacheHitPercent:0.0}%",
                $"{HudFormatting.FormatNumber(sample.Cached, settings.NumberFormat)} / {HudFormatting.FormatNumber(sample.Input, settings.NumberFormat)}",
                HudFormatting.FormatNumber(sample.Uncached, settings.NumberFormat),
                HudFormatting.FormatNumber(sample.CacheWrite, settings.NumberFormat),
                sample.ContextWindow <= 0 ? "--" : $"{sample.ContextPercent:0.0}%",
                sample.AlertState.ToString().Replace('_', ' ')
            };
            for (var column = 0; column < values.Length; column++)
            {
                var text = new TextBlock
                {
                    Text = values[column],
                    FontSize = Math.Max(10, settings.FontSize - 2),
                    FontWeight = column is 1 or 6 ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = _brushes.Create(
                        column is 1 or 6 ? alertColor : settings.Muted,
                        column is 1 or 6 ? alertColor : "#FF667085",
                        column is 1 or 6 ? BrushRole.Status : BrushRole.Secondary,
                        settings,
                        "listening",
                        column is 1 or 6 && sample.AlertState is not AlertState.GOOD and not AlertState.OK)
                };
                Grid.SetColumn(text, column);
                row.Children.Add(text);
            }
            _callHistoryPanel.Children.Add(row);
        }
        _callHistoryScroller.ScrollToTop();
    }

    private void ApplyScale(double scale)
    {
        _currentScale = Math.Clamp(scale, 0.20, 1);
        _scaleHost.LayoutTransform = new ScaleTransform(_currentScale, _currentScale);
        _windowSizeButton.ToolTip = $"调整悬浮窗大小（当前 {_currentScale:P0}）";
    }

    private void ShowSizeMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = _windowSizeButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };
        foreach (var preset in new[]
                 {
                     (Label: "超小 · 20%", Scale: 0.20),
                     (Label: "较小 · 40%", Scale: 0.40),
                     (Label: "小 · 60%", Scale: 0.60),
                     (Label: "中 · 78%", Scale: 0.78),
                     (Label: "大 · 100%", Scale: 1.00)
                 })
        {
            var item = new MenuItem
            {
                Header = preset.Label,
                IsCheckable = true,
                IsChecked = Math.Abs(_currentScale - preset.Scale) < 0.01
            };
            item.Click += (_, _) => ScaleChanged?.Invoke(preset.Scale);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void ShowBackgroundOpacityMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = _backgroundOpacityButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };
        foreach (var preset in new[]
                 {
                     (Label: "全透明 · 仅曲线 / 命中 / Tokens", Opacity: 0.00),
                     (Label: "背景可见度 · 20%", Opacity: 0.20),
                     (Label: "背景可见度 · 40%", Opacity: 0.40),
                     (Label: "背景可见度 · 60%", Opacity: 0.60),
                     (Label: "背景可见度 · 80%", Opacity: 0.80),
                     (Label: "背景可见度 · 100%", Opacity: 1.00)
                 })
        {
            var item = new MenuItem
            {
                Header = preset.Label,
                IsCheckable = true,
                IsChecked = _currentTransparencyMode == "background" &&
                            Math.Abs(_currentBackgroundOpacity - preset.Opacity) < 0.01
            };
            item.Click += (_, _) => BackgroundOpacityChanged?.Invoke(preset.Opacity);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private static string FormatTokenCount(long value) => value switch
    {
        >= 1_000_000 => $"{value / 1_000_000d:0.00}M",
        >= 1_000 => $"{value / 1_000d:0.0}K",
        _ => value.ToString("N0")
    };

    private void SynchronizeBubbles(
        HudSettings settings,
        IReadOnlyDictionary<string, string> locale,
        IReadOnlyDictionary<string, string> metricLocale,
        IReadOnlyList<SessionState> states,
        Func<SessionState, string> statusFor,
        DateTimeOffset now)
    {
        var visible = states.Select(static state => state.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _seenStatePaths.IntersectWith(visible);
        _detached.IntersectWith(visible);
        if (settings.MultiTask.DisplayMode == "split" && !_hasSynchronizedStates)
        {
            foreach (var state in states.OrderByDescending(static state => state.LastWriteTimeUtc).Take(settings.MultiTask.MaxSplitBubbles))
            {
                _detached.Add(state.Path);
            }
        }
        else if (settings.MultiTask.DisplayMode == "split" && settings.MultiTask.AutoSplitNewTasks)
        {
            foreach (var state in states.Where(state => !_seenStatePaths.Contains(state.Path)).OrderByDescending(static state => state.LastWriteTimeUtc))
            {
                if (_detached.Count >= settings.MultiTask.MaxSplitBubbles) break;
                _detached.Add(state.Path);
            }
        }
        _hasSynchronizedStates = true;
        foreach (var state in states) _seenStatePaths.Add(state.Path);
        var allowedDetached = states
            .Where(state => _detached.Contains(state.Path))
            .Take(settings.MultiTask.MaxSplitBubbles)
            .Select(static state => state.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _bubbles.Keys.Where(path => !visible.Contains(path) || !allowedDetached.Contains(path)).ToArray())
        {
            if (_bubbles.Remove(path, out var bubble))
            {
                bubble.Dispose();
            }
        }
        foreach (var state in states.Where(state => allowedDetached.Contains(state.Path)))
        {
            if (!_bubbles.TryGetValue(state.Path, out var bubble))
            {
                bubble = new TaskBubbleView(_taskBubbleXaml, state, _brushes);
                bubble.DismissRequested += path => SetDetached(path, false, settings.MultiTask.MaxSplitBubbles);
                bubble.MergeRequested += path => SetDetached(path, false, settings.MultiTask.MaxSplitBubbles);
                bubble.OpenRequested += path => OpenRequested?.Invoke(path);
                bubble.AttentionPresented += (path, reason) => AttentionPresented?.Invoke("bubble", path, reason);
                _bubbles[state.Path] = bubble;
                bubble.Window.Show();
            }
            var status = statusFor(state);
            bubble.Render(
                state,
                settings,
                locale,
                status,
                GetDisplayName(state, settings, locale),
                GetSourceLabel(state, locale),
                GetSourceColor(state, settings),
                WithAgentNotice(
                    state,
                    GetTaskMetricsText(state, settings, metricLocale, status, listPreset: false),
                    locale,
                    now,
                    true),
                state.AttentionUntil > now);
        }
    }

    private void UpdateTaskListLive(
        HudSettings settings,
        IReadOnlyDictionary<string, string> locale,
        IReadOnlyDictionary<string, string> metricLocale,
        IReadOnlyList<SessionState> states,
        Func<SessionState, string> statusFor,
        DateTimeOffset now)
    {
        foreach (var state in states)
        {
            if (_taskListLive.TryGetValue(state.Path, out var live))
            {
                UpdateTaskListLiveEntry(settings, locale, metricLocale, state, statusFor(state), live, now);
            }
        }
    }

    private void UpdateTaskListLiveEntry(
        HudSettings settings,
        IReadOnlyDictionary<string, string> locale,
        IReadOnlyDictionary<string, string> metricLocale,
        SessionState state,
        string status,
        TaskListLiveControls live,
        DateTimeOffset now)
    {
        var hasAttention = state.AttentionUntil > now;
        var terminalExitActive = state.TerminalExitStarted && !state.TerminalExitCompleted && state.TerminalExitUntil > now;
        if (!hasAttention && !terminalExitActive && _animatedListSurfaces.Remove(state.Path))
        {
            HudAnimations.ResetAttention(live.Surface, live.Dot, clearContainerBorder: true);
        }
        if (state.ContextAlertUntil <= now && live.ContextMetric is not null && _animatedListContexts.Remove(state.Path))
        {
            HudAnimations.ResetAttention(live.ContextMetric);
        }
        live.Dot.Fill = _brushes.Create(StatusColor(settings, status), "#FF8E8E93", BrushRole.Status, settings, status, hasAttention);
        live.Name.Foreground = _brushes.Create(settings.Foreground, "#FF111827", BrushRole.Primary, settings, status, hasAttention);
        live.Subtitle.Foreground = _brushes.Create(settings.Muted, "#FF667085", BrushRole.Secondary, settings, status, hasAttention);
        live.Metrics.Text = WithAgentNotice(
            state,
            GetTaskMetricsText(state, settings, metricLocale, status, listPreset: true),
            locale,
            now,
            !_detached.Contains(state.Path));
        live.Metrics.ToolTip = live.Metrics.Text;
        live.Metrics.Foreground = _brushes.Create(settings.Muted, "#FF667085", BrushRole.Secondary, settings, status, hasAttention);
        if (live.ContextText is not null)
        {
            live.ContextText.Text = state.Snapshot is null
                ? Get(locale, "waiting")
                : $"{Get(locale, "context")} {(state.Snapshot.ContextWindow > 0 ? HudFormatting.FormatPercent(state.Snapshot.ContextPercent) : "--")}";
            if (live.ContextMetric is not null)
            {
                live.ContextMetric.ToolTip = BuildContextTooltip(state, locale);
            }
            live.ContextText.Foreground = _brushes.Create(settings.Foreground, "#FF111827", BrushRole.Primary, settings, status, hasAttention);
        }

        var previousAttention = _lastListAttentionRevisions.GetValueOrDefault(state.Path);
        if (state.AttentionRevision > previousAttention)
        {
            _lastListAttentionRevisions[state.Path] = state.AttentionRevision;
            if (!_detached.Contains(state.Path) && state.AttentionUntil > now)
            {
                if (_animatedListSurfaces.Remove(state.Path))
                {
                    HudAnimations.ResetAttention(live.Surface, live.Dot, clearContainerBorder: true);
                }
                if (live.ContextMetric is not null && _animatedListContexts.Remove(state.Path))
                {
                    HudAnimations.ResetAttention(live.ContextMetric);
                }
                if (state.AttentionReason == "agent")
                {
                    HudAnimations.StartAgent(live.Surface, state.AgentNoticeRecipe, settings);
                    _animatedListSurfaces.Add(state.Path);
                }
                else if (state.AttentionReason == "context" && live.ContextMetric is not null)
                {
                    HudAnimations.StartContext(live.ContextMetric, state.ContextAlertLevel, settings);
                    _animatedListContexts.Add(state.Path);
                }
                else
                {
                    HudAnimations.StartAttention(live.Dot, live.Surface, settings.Attention.ListMode, settings);
                    _animatedListSurfaces.Add(state.Path);
                }
                AttentionPresented?.Invoke("list", state.Path, state.AttentionReason);
            }
        }
        var previousExit = _lastListExitRevisions.GetValueOrDefault(state.Path);
        if (state.TerminalExitRevision > previousExit && state.TerminalExitUntil > now)
        {
            _lastListExitRevisions[state.Path] = state.TerminalExitRevision;
            HudAnimations.StartTerminalExit(
                live.Surface,
                settings.StatusTiming.TerminalExitMode,
                StatusColor(settings, status),
                settings,
                state.TerminalExitUntil - now);
            _animatedListSurfaces.Add(state.Path);
        }
    }

    private void PositionBubbles(HudSettings settings)
    {
        var entries = _bubbles.Values.Where(static bubble => bubble.Window.IsVisible).OrderBy(static bubble => bubble.TaskNumber).ToArray();
        if (entries.Length == 0)
        {
            return;
        }
        Window.UpdateLayout();
        var work = SystemParameters.WorkArea;
        const double gap = 8;
        var isBottom = settings.Position.StartsWith("bottom", StringComparison.Ordinal) ||
                       settings.Position == "custom" && Window.Top + Window.ActualHeight / 2 > work.Top + work.Height / 2;
        var isLeft = settings.Position.EndsWith("left", StringComparison.Ordinal) ||
                     settings.Position == "custom" && Window.Left + Window.ActualWidth / 2 < work.Left + work.Width / 2;
        var mainShellLeft = Window.Left + MainChromeInset;
        var mainShellRight = Window.Left + Window.ActualWidth - MainChromeInset;
        var mainShellTop = Window.Top + MainChromeInset;
        var mainShellBottom = Window.Top + Window.ActualHeight - MainChromeInset;
        var cursorY = isBottom ? mainShellTop - gap : mainShellBottom + gap;
        var columnOffset = 0d;
        var columnWidth = 0d;
        foreach (var entry in entries)
        {
            entry.Window.UpdateLayout();
            var width = Math.Max(220, entry.Window.ActualWidth);
            var height = Math.Max(54, entry.Window.ActualHeight);
            columnWidth = Math.Max(columnWidth, width);
            double top;
            if (isBottom)
            {
                top = cursorY - height + TaskBubbleChromeInset;
                if (top < work.Top - TaskBubbleChromeInset)
                {
                    columnOffset += columnWidth + gap;
                    columnWidth = width;
                    cursorY = work.Bottom;
                    top = cursorY - height + TaskBubbleChromeInset;
                }
                cursorY = top + TaskBubbleChromeInset - gap;
            }
            else
            {
                top = cursorY - TaskBubbleChromeInset;
                if (top + height > work.Bottom + TaskBubbleChromeInset)
                {
                    columnOffset += columnWidth + gap;
                    columnWidth = width;
                    cursorY = work.Top;
                    top = cursorY - TaskBubbleChromeInset;
                }
                cursorY = top + height - TaskBubbleChromeInset + gap;
            }
            var left = isLeft
                ? mainShellLeft - TaskBubbleChromeInset + columnOffset
                : mainShellRight - width + TaskBubbleChromeInset - columnOffset;
            var clamped = HudPlacement.ClampCustom(
                left,
                top,
                work.Left,
                work.Top,
                work.Width,
                work.Height,
                width,
                height,
                TaskBubbleChromeInset);
            entry.Window.Left = clamped.Left;
            entry.Window.Top = clamped.Top;
        }
    }

    private void UpdateQuietMode(
        HudSettings settings,
        IReadOnlyDictionary<string, string> locale,
        IReadOnlyList<SessionState> states,
        Func<SessionState, string> statusFor,
        string overallStatus,
        DateTimeOffset now)
    {
        _lastIdleIndicatorLayout = settings.Behavior.IdleIndicator.Layout;
        var taskLayout = settings.Behavior.IdleIndicator.Layout is "horizontal" or "vertical";
        var keepTerminalLights = _isMainIndicatorCollapsed && taskLayout;
        var shouldCollapse = settings.Behavior.IdleIndicator.Enabled &&
                             states.Count > 0 &&
                             !Window.IsMouseOver &&
                             states.All(state => IsQuiet(state, statusFor(state), settings, now, keepTerminalLights));
        var transition = shouldCollapse != _isMainIndicatorCollapsed;
        _isMainIndicatorCollapsed = shouldCollapse;
        _contentPanel.Visibility = shouldCollapse ? Visibility.Collapsed : Visibility.Visible;
        _quietPanel.Visibility = shouldCollapse ? Visibility.Visible : Visibility.Collapsed;
        if (shouldCollapse)
        {
            _shell.Padding = settings.Behavior.IdleIndicator.Layout == "vertical"
                ? new Thickness(9, 10, 9, 9)
                : new Thickness(10, 9, 10, 9);
            _shell.CornerRadius = new CornerRadius(16);
        }
        else if (transition)
        {
            _shell.Padding = settings.Layout == "stacked" ? new Thickness(16, 13, 16, 13) : new Thickness(14, 10, 14, 10);
            _shell.CornerRadius = new CornerRadius(settings.CornerRadius);
            _appearanceSignature = string.Empty;
            _listSignature = string.Empty;
        }

        var stateByPath = states.ToDictionary(static state => state.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var (path, bubble) in _bubbles)
        {
            if (!stateByPath.TryGetValue(path, out var state))
            {
                continue;
            }
            var allowTerminal = taskLayout && bubble.IsIndicatorCollapsed;
            var bubbleShouldCollapse = settings.Behavior.IdleIndicator.Enabled &&
                                       settings.Behavior.IdleIndicator.IncludeTaskBubbles &&
                                       !bubble.IsMouseOver &&
                                       IsQuiet(state, statusFor(state), settings, now, allowTerminal);
            bubble.SetIndicatorCollapsed(bubbleShouldCollapse);
        }

        if (!shouldCollapse)
        {
            _quietSignature = string.Empty;
            return;
        }
        _quietOverallDot.Fill = _brushes.Create(StatusColor(settings, overallStatus), "#FF8E8E93", BrushRole.Status, settings, overallStatus, false);
        _quietOverallRing.Stroke = _brushes.Create(settings.Accent, "#FF0A84FF", BrushRole.Decoration, settings, overallStatus, false);
        _quietOverallHost.Visibility = taskLayout ? Visibility.Collapsed : Visibility.Visible;
        _quietSeparator.Visibility = taskLayout && states.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _quietTasks.Visibility = taskLayout ? Visibility.Visible : Visibility.Collapsed;
        _quietTasks.Orientation = settings.Behavior.IdleIndicator.Layout == "vertical" ? Orientation.Vertical : Orientation.Horizontal;
        _quietPanel.Orientation = _quietTasks.Orientation;
        if (settings.Behavior.IdleIndicator.Layout == "vertical")
        {
            _quietSeparator.Width = 30;
            _quietSeparator.Height = 1;
            _quietSeparator.Margin = new Thickness(0, 8, 0, 6);
            _quietSeparator.HorizontalAlignment = HorizontalAlignment.Center;
        }
        else
        {
            _quietSeparator.Width = 1;
            _quietSeparator.Height = 20;
            _quietSeparator.Margin = new Thickness(11, 0, 8, 0);
            _quietSeparator.VerticalAlignment = VerticalAlignment.Center;
        }
        var quietSignature = string.Join('|',
            settings.Behavior.IdleIndicator.Layout,
            settings.Behavior.IdleIndicator.TaskStyle,
            overallStatus,
            string.Join(';', states.OrderBy(static state => state.Number).Select(state => $"{state.Number}:{statusFor(state)}")));
        if (_quietSignature == quietSignature)
        {
            return;
        }
        _quietSignature = quietSignature;
        _quietTasks.Children.Clear();
        if (taskLayout)
        {
            var ordered = states.OrderBy(static state => state.Number).ToArray();
            foreach (var state in ordered.Take(12))
            {
                var status = statusFor(state);
                var text = new TextBlock
                {
                    Text = state.Number.ToString(),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = _brushes.Create(settings.Foreground, "#FFFFFFFF", BrushRole.Primary, settings, status, false),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var indicator = new Border
                {
                    CornerRadius = new CornerRadius(settings.Behavior.IdleIndicator.TaskStyle == "bar" ? 7 : 10),
                    MinWidth = settings.Behavior.IdleIndicator.TaskStyle == "bar" ? 26 : 18,
                    Height = 18,
                    Margin = settings.Behavior.IdleIndicator.Layout == "vertical" ? new Thickness(0, 2, 0, 2) : new Thickness(2, 0, 2, 0),
                    Background = _brushes.Create(StatusColor(settings, status), "#FF8E8E93", BrushRole.Status, settings, status, false),
                    Child = text,
                    ToolTip = GetDisplayName(state, settings, locale)
                };
                _quietTasks.Children.Add(indicator);
            }
            if (ordered.Length > 12)
            {
                _quietTasks.Children.Add(new TextBlock
                {
                    Text = $"+{ordered.Length - 12}",
                    Margin = settings.Behavior.IdleIndicator.Layout == "vertical"
                        ? new Thickness(6, 3, 0, 2)
                        : new Thickness(5, 0, 1, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = _brushes.Create(settings.Muted, "#FF667085", BrushRole.Secondary, settings, overallStatus, false),
                    ToolTip = Get(locale, "activeTasks")
                });
            }
        }
    }

    private static bool IsQuiet(
        SessionState state,
        string status,
        HudSettings settings,
        DateTimeOffset now,
        bool allowTerminal)
    {
        if (allowTerminal && status is "completed" or "aborted")
        {
            return true;
        }
        if (status != "idle" || state.AttentionUntil > now || state.AgentNoticeUntil > now || state.ContextAlertUntil > now)
        {
            return false;
        }
        var reference = state.LastUsageAt != DateTimeOffset.MinValue
            ? state.LastUsageAt
            : state.Snapshot?.Timestamp ?? now;
        return (now - reference).TotalMinutes >= settings.Behavior.IdleIndicator.AfterMinutes;
    }

    private Rect GetCurrentScreenBounds()
    {
        if (_handle != 0)
        {
            var bounds = Forms.Screen.FromHandle(_handle).Bounds;
            var dpi = VisualTreeHelper.GetDpi(Window);
            return new Rect(
                bounds.Left / dpi.DpiScaleX,
                bounds.Top / dpi.DpiScaleY,
                bounds.Width / dpi.DpiScaleX,
                bounds.Height / dpi.DpiScaleY);
        }

        return new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
    }

    private void UpdatePosition(HudSettings settings)
    {
        var screen = GetCurrentScreenBounds();
        Window.MaxWidth = Math.Max(480, screen.Width + MainChromeInset * 2);
        Window.UpdateLayout();
        var width = Math.Max(1, Window.ActualWidth);
        var height = Math.Max(1, Window.ActualHeight);
        if (settings.Position == "custom")
        {
            // customLeft/customTop represent the visible shell, not the
            // transparent shadow canvas around it.  Old top=0 values therefore
            // migrate naturally to a shell that actually touches the edge.
            var desiredShellLeft = settings.CustomLeft ?? Window.Left + MainChromeInset;
            var desiredShellTop = settings.CustomTop ?? Window.Top + MainChromeInset;
            var point = HudPlacement.ClampCustom(
                desiredShellLeft - MainChromeInset,
                desiredShellTop - MainChromeInset,
                screen.Left,
                screen.Top,
                screen.Width,
                screen.Height,
                width,
                height,
                MainChromeInset);
            Window.Left = point.Left;
            Window.Top = point.Top;
            return;
        }
        var preset = HudPlacement.GetPreset(
            settings.Position,
            screen.Left,
            screen.Top,
            screen.Width,
            screen.Height,
            width,
            height,
            MainChromeInset);
        Window.Left = preset.Left;
        Window.Top = preset.Top;
    }

    private void StartSummaryAttention(HudSettings settings, IReadOnlyList<SessionState> states, DateTimeOffset now)
    {
        if (settings.MultiTask.DisplayMode != "summary")
        {
            if (_summaryVisualActive)
            {
                ResetSummaryAttentionVisual();
            }
            return;
        }
        var state = states
            .Where(state => state.AttentionUntil > now)
            .OrderByDescending(static state => state.AttentionRevision)
            .FirstOrDefault();
        if (state is null || state.AttentionRevision <= _lastSummaryAttentionRevision)
        {
            return;
        }
        _lastSummaryAttentionRevision = state.AttentionRevision;
        if (_summaryVisualActive)
        {
            ResetSummaryAttentionVisual();
        }
        if (state.AttentionReason == "agent")
        {
            HudAnimations.StartAgent(_shell, state.AgentNoticeRecipe, settings);
            _summaryVisualActive = true;
        }
        else if (state.AttentionReason == "context")
        {
            var target = _metricControls.GetValueOrDefault("context")?.Container ?? _shell;
            HudAnimations.StartContext(target, state.ContextAlertLevel, settings);
            _summaryAnimatedContextTarget = target;
            _summaryVisualActive = true;
        }
        else
        {
            HudAnimations.StartAttention(_statusDot, _shell, settings.Attention.SummaryMode, settings);
            _summaryVisualActive = true;
        }
        AttentionPresented?.Invoke("summary", state.Path, state.AttentionReason);
    }

    private void ResetSummaryAttentionVisual()
    {
        HudAnimations.ResetAttention(_shell, _statusDot, _summaryAnimatedContextTarget);
        _shell.BorderBrush = _summaryBaseBorderBrush;
        _shell.BorderThickness = _summaryBaseBorderThickness;
        _summaryAnimatedContextTarget = null;
        _summaryVisualActive = false;
    }

    private void StartUpdateAnimation(
        HudSettings settings,
        IReadOnlyList<SessionState> states,
        Func<SessionState, string> statusFor,
        string overallStatus,
        DateTimeOffset now)
    {
        var phaseSignature = string.Join(';', states.OrderBy(static state => state.Number).Select(state =>
            $"{state.Number}:{(state.SessionId.Length > 0 ? state.SessionId : state.Path)}:{statusFor(state)}:{_detached.Contains(state.Path)}"));
        var signature = $"{settings.MultiTask.DisplayMode}|{states.Count}|{overallStatus}|{phaseSignature}";
        var attentionActive = states.Any(state => state.AttentionUntil > now);
        var shouldAnimate = settings.AnimateUpdates &&
                            _lastUpdateAnimationSignature.Length > 0 &&
                            _lastUpdateAnimationSignature != signature &&
                            !attentionActive;
        _lastUpdateAnimationSignature = signature;
        if (shouldAnimate)
        {
            _shell.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(160))
            {
                FillBehavior = FillBehavior.Stop
            });
        }
    }

    private void SetDetached(string path, bool detached, int maximum)
    {
        if (detached)
        {
            if (_detached.Count >= maximum)
            {
                return;
            }
            _detached.Add(path);
        }
        else
        {
            _detached.Remove(path);
            if (_bubbles.Remove(path, out var bubble))
            {
                bubble.Dispose();
            }
        }
        _listSignature = string.Empty;
        DetachedChanged?.Invoke(path, detached);
    }

    private void OnWindowMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (args.ClickCount >= 2)
        {
            SettingsRequested?.Invoke();
            return;
        }
        if (args.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                Window.DragMove();
                var screen = GetCurrentScreenBounds();
                var screenRight = screen.Left + screen.Width;
                var screenBottom = screen.Top + screen.Height;
                var pixelBounds = Forms.Screen.FromHandle(_handle).Bounds;
                if (NativeMethods.GetCursorPos(out var cursor) &&
                    Window.Left >= screen.Left && cursor.X <= pixelBounds.Left + PhysicalEdgeTolerance)
                {
                    Window.Left = screen.Left - MainChromeInset;
                }
                else if (cursor.X >= pixelBounds.Right - PhysicalEdgeTolerance &&
                         Window.Left + Window.ActualWidth <= screenRight)
                {
                    Window.Left = screenRight - Window.ActualWidth + MainChromeInset;
                }
                if (cursor.Y <= pixelBounds.Top + PhysicalEdgeTolerance && Window.Top >= screen.Top)
                {
                    Window.Top = screen.Top - MainChromeInset;
                }
                else if (cursor.Y >= pixelBounds.Bottom - PhysicalEdgeTolerance &&
                         Window.Top + Window.ActualHeight <= screenBottom)
                {
                    Window.Top = screenBottom - Window.ActualHeight + MainChromeInset;
                }
                PositionChanged?.Invoke(Window.Left + MainChromeInset, Window.Top + MainChromeInset);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private Button NewIconButton(string geometry, string color, double size, Thickness margin, string tooltip)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Width = 13,
            Height = 13,
            Stretch = Stretch.Uniform,
            Stroke = BrushFactory.Convert(color, "#FF0A84FF"),
            StrokeThickness = 1.35,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Data = Geometry.Parse(geometry)
        };
        return new Button
        {
            Content = icon,
            Style = (Style)Window.FindResource("HudIconButton"),
            Width = size,
            Height = size,
            Margin = margin,
            ToolTip = tooltip
        };
    }

    private static string GetTaskMetricsText(
        SessionState state,
        HudSettings settings,
        IReadOnlyDictionary<string, string> locale,
        string status,
        bool listPreset)
    {
        if (state.Snapshot is null)
        {
            return Get(locale, "waiting");
        }
        var snapshot = state.Snapshot;
        var parts = new List<string> { Get(locale, StatusKey(status)) };
        if (listPreset)
        {
            var metrics = HudFormatting.GetTaskListMetrics(
                snapshot,
                settings.MultiTask.ListDetail,
                locale,
                settings.NumberFormat);
            parts.AddRange(metrics.Primary
                .Where(static metric => metric.Key != "context")
                .Select(FormatTaskMetric));
            if (metrics.Diagnostics.Count > 0)
            {
                var diagnosticText = string.Join(" \u00B7 ", metrics.Diagnostics.Select(FormatTaskMetric));
                return string.Join(" \u00B7 ", parts) + Environment.NewLine + diagnosticText;
            }
        }
        else
        {
            var fields = settings.MultiTask.BubbleFields;
            if (fields.Model && !string.IsNullOrWhiteSpace(snapshot.Model)) parts.Add(snapshot.Model);
            if (fields.CallTotal) parts.Add($"{Get(locale, "callTotal")} {HudFormatting.FormatNumber(snapshot.CallTotal, settings.NumberFormat)}");
            if (fields.CacheHitRate) parts.Add($"{Get(locale, "cacheHitRate")} {HudFormatting.FormatCacheHitRate(snapshot.Input, snapshot.Cached)}");
            if (fields.TaskTotal) parts.Add($"{Get(locale, "taskTotal")} {HudFormatting.FormatNumber(snapshot.TaskTotal, settings.NumberFormat)}");
            if (fields.EstimatedCost) parts.Add($"{Get(locale, "estimatedCost")} {HudFormatting.FormatCost(snapshot.EstimatedCostUsd)}");
            if (fields.Updated) parts.Add(snapshot.Timestamp.ToString("HH:mm:ss"));
        }
        return string.Join(" \u00B7 ", parts);
    }

    private static string FormatTaskMetric(HudMetric metric) =>
        metric.Key == "model" ? metric.Value : $"{metric.Label} {metric.Value}";

    private static string BuildContextTooltip(SessionState state, IReadOnlyDictionary<string, string> locale)
    {
        if (state.Snapshot is null || state.Snapshot.ContextWindow <= 0)
        {
            return Get(locale, "contextUnavailable");
        }

        var model = string.IsNullOrWhiteSpace(state.Snapshot.Model) ? Get(locale, "modelUnknown") : state.Snapshot.Model;
        return $"{model} \u00B7 {Get(locale, "contextWindow")} {HudFormatting.FormatNumber(state.Snapshot.ContextWindow, "auto")}";
    }

    private static string WithAgentNotice(
        SessionState state,
        string metrics,
        IReadOnlyDictionary<string, string> locale,
        DateTimeOffset now,
        bool show)
    {
        if (!show || state.AgentNoticeUntil <= now || string.IsNullOrWhiteSpace(state.AgentNoticeText))
        {
            return metrics;
        }
        return $"\u2726 {Get(locale, "agentNotificationBadge")} #{state.Number}  {state.AgentNoticeText}  \u00B7  {metrics}";
    }

    private static string GetDisplayName(SessionState state, HudSettings settings, IReadOnlyDictionary<string, string> locale, bool includeNumber = false)
    {
        var workspace = ProjectName(state, locale);
        var identity = settings.MultiTask.NameMode != "hidden" && !string.IsNullOrWhiteSpace(state.ConversationLabel)
            ? workspace + " \u00B7 " + state.ConversationLabel
            : workspace;
        var name = identity + " \u00B7 " + state.StartedAt.ToLocalTime().ToString("HH:mm");
        return includeNumber ? $"#{state.Number} \u00B7 {name}" : name;
    }

    private static HudMetric[] AddSourceBreakdown(
        HudMetric[] metrics,
        IReadOnlyList<SessionState> states,
        IReadOnlyDictionary<string, string> locale)
    {
        if (metrics.Length == 0 || states.Count == 0)
        {
            return metrics;
        }
        var groups = states
            .GroupBy(state => GetSourceLabel(state, locale), StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Label = group.Key, Count = group.Count() })
            .OrderByDescending(static group => group.Count)
            .ThenBy(static group => group.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (groups.Length == 1 && states.All(static state => !string.Equals(state.ClientSurface, "cli", StringComparison.OrdinalIgnoreCase)))
        {
            return metrics;
        }
        var suffix = string.Join(" \u00B7 ", groups.Select(static group => $"{group.Label} {group.Count}"));
        for (var index = 0; index < metrics.Length; index++)
        {
            if (string.Equals(metrics[index].Key, "activeTasks", StringComparison.OrdinalIgnoreCase))
            {
                metrics[index] = metrics[index] with { Value = metrics[index].Value + " \u00B7 " + suffix };
                break;
            }
        }
        return metrics;
    }

    private static string GetSourceLabel(SessionState state, IReadOnlyDictionary<string, string> locale)
    {
        if (string.Equals(state.ClientSurface, "vscode", StringComparison.OrdinalIgnoreCase))
        {
            return Get(locale, "sourceVsCode");
        }
        if (string.Equals(state.ClientSurface, "desktop", StringComparison.OrdinalIgnoreCase))
        {
            return Get(locale, "sourceDesktop");
        }
        if (string.Equals(state.ClientSurface, "cli", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state.ProfileId, SessionProfile.DeepSeekId, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(state.ModelProvider, "deepseek", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state.ProfileId, SessionProfile.DeepSeekId, StringComparison.OrdinalIgnoreCase))
            {
                return Get(locale, "sourceCliDeepSeek");
            }
            if (string.IsNullOrWhiteSpace(state.ModelProvider) ||
                string.Equals(state.ModelProvider, "openai", StringComparison.OrdinalIgnoreCase))
            {
                return Get(locale, "sourceCliOpenAI");
            }
            return $"{Get(locale, "sourceCli")} \u00B7 {ShortProvider(state.ModelProvider)}";
        }
        return Get(locale, "sourceUnknown");
    }

    private static string GetSourceColor(SessionState state, HudSettings settings)
    {
        if (string.Equals(state.ClientSurface, "vscode", StringComparison.OrdinalIgnoreCase))
        {
            return "#FF007ACC";
        }
        if (string.Equals(state.ClientSurface, "desktop", StringComparison.OrdinalIgnoreCase))
        {
            return settings.Accent;
        }
        if (string.Equals(state.ModelProvider, "deepseek", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state.ProfileId, SessionProfile.DeepSeekId, StringComparison.OrdinalIgnoreCase))
        {
            return "#FF00A7B5";
        }
        if (string.Equals(state.ClientSurface, "cli", StringComparison.OrdinalIgnoreCase))
        {
            return "#FF8B5CF6";
        }
        return settings.Muted;
    }

    private static string ShortProvider(string provider)
    {
        var value = provider.Trim();
        return value.Length <= 18 ? value : value[..18];
    }

    private static bool CanOpenTask(SessionState state) =>
        string.Equals(state.ClientSurface, "desktop", StringComparison.OrdinalIgnoreCase);

    private static string GetSourceGeometry(SessionState state)
    {
        if (string.Equals(state.ClientSurface, "vscode", StringComparison.OrdinalIgnoreCase))
        {
            // Official VS Code ribbon silhouette with an even-odd center cutout.
            return "M11.52,0.29 A0.98,0.98 0 0 0 10.82,0.33 L4.21,3.33 L1.5,1.29 A1,1 0 0 0 0,2.09 L0,13.91 A1,1 0 0 0 1.5,14.71 L4.21,12.68 L10.82,15.67 A0.98,0.98 0 0 0 11.52,15.71 L15,14.11 A1,1 0 0 0 15.6,13 L15.6,3 A1,1 0 0 0 15,2.09 Z M11,11.26 L5.73,8 L11,4.74 Z";
        }
        if (string.Equals(state.ClientSurface, "desktop", StringComparison.OrdinalIgnoreCase))
        {
            return "M1.4,2.1 L12.6,2.1 Q13,2.1 13,2.5 L13,11.5 Q13,11.9 12.6,11.9 L1.4,11.9 Q1,11.9 1,11.5 L1,2.5 Q1,2.1 1.4,2.1 Z M1.4,4.8 L12.6,4.8 M3,3.45 L3.08,3.45 M4.75,3.45 L4.83,3.45";
        }
        if (string.Equals(state.ModelProvider, "deepseek", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state.ProfileId, SessionProfile.DeepSeekId, StringComparison.OrdinalIgnoreCase))
        {
            return "M1.4,2.1 L12.6,2.1 Q13,2.1 13,2.5 L13,11.5 Q13,11.9 12.6,11.9 L1.4,11.9 Q1,11.9 1,11.5 L1,2.5 Q1,2.1 1.4,2.1 Z M2.8,8.4 C4.1,5.7 5.55,10.4 7.05,7.65 C8.15,5.65 9.3,7.25 11.2,5.75";
        }
        return "M1.4,2.1 L12.6,2.1 Q13,2.1 13,2.5 L13,11.5 Q13,11.9 12.6,11.9 L1.4,11.9 Q1,11.9 1,11.5 L1,2.5 Q1,2.1 1.4,2.1 Z M3,5.15 L5.85,7.15 L3,9.15 M7.15,9.15 L10.65,9.15";
    }

    private static string ProjectName(SessionState state, IReadOnlyDictionary<string, string> locale) =>
        string.IsNullOrWhiteSpace(state.Workspace) ? Get(locale, "unnamedWorkspace") : state.Workspace;

    private static string StatusKey(string status) => status switch
    {
        "active" => "statusActive",
        "listening" => "statusListening",
        "paused" => "statusPaused",
        "error" => "statusError",
        "completed" => "statusCompleted",
        "aborted" => "statusAborted",
        _ => "statusIdle"
    };

    private static string StatusColor(HudSettings settings, string status) =>
        settings.StatusColors.TryGetValue(status, out var color) ? color : "#FF8E8E93";

    private static string Get(IReadOnlyDictionary<string, string> locale, string key) =>
        locale.TryGetValue(key, out var value) ? value : key;

    private static Color ParseColor(string value, string fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch (FormatException) { return (Color)ColorConverter.ConvertFromString(fallback); }
    }

    private static Brush ColorBrush(Color color, byte alpha)
    {
        color.A = alpha;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static DensityMetrics Density(string density) => density switch
    {
        "relaxed" => new DensityMetrics(new Thickness(0, 2, 0, 2), new Thickness(8, 0, 8, 0), new Thickness(7, 4, 7, 4), new Thickness(0, 4, 7, 4), 8, 30, new Thickness(8, 3, 4, 3), new Thickness(7, 4, 7, 4), new Thickness(0, 4, 0, 4), 13, new Thickness(0, 0, 8, 7), new Thickness(0, 3, 0, 3), new Thickness(0, 3, 0, 3), 7),
        "balanced" => new DensityMetrics(new Thickness(0, 1, 0, 1), new Thickness(7, 0, 7, 0), new Thickness(6, 3, 6, 3), new Thickness(0, 2, 6, 2), 8, 28, new Thickness(6, 1, 3, 1), new Thickness(6, 3, 6, 3), new Thickness(0, 2, 0, 2), 12, new Thickness(0, 0, 7, 5), new Thickness(0, 2, 0, 2), new Thickness(0, 2, 0, 2), 6),
        _ => new DensityMetrics(new Thickness(), new Thickness(6, 0, 6, 0), new Thickness(6, 2, 6, 2), new Thickness(0, 1, 6, 1), 7, 26, new Thickness(5, 0, 2, 0), new Thickness(5, 2, 5, 2), new Thickness(0, 1, 0, 1), 11, new Thickness(0, 0, 6, 3), new Thickness(0, 1, 0, 1), new Thickness(0, 2, 0, 2), 6)
    };

    private sealed record DensityMetrics(
        Thickness RowMargin,
        Thickness DotMargin,
        Thickness BadgePadding,
        Thickness BadgeMargin,
        double BadgeRadius,
        double ActionSize,
        Thickness ActionMargin,
        Thickness CardPadding,
        Thickness CardMargin,
        double CardRadius,
        Thickness MetricsMargin,
        Thickness RailMargin,
        Thickness RailInnerMargin,
        double RailContentLeft);

    private sealed record MetricControl(TextBlock Label, TextBlock Value, Border? Container);

    private sealed record TaskListLiveControls(
        Ellipse Dot,
        TextBlock Name,
        TextBlock Subtitle,
        TextBlock Metrics,
        TextBlock? ContextText,
        Border? ContextMetric,
        Border Surface);

    private const double MainChromeInset = 18;
    private const double TaskBubbleChromeInset = 16;
    private const double PhysicalEdgeTolerance = 1;

    private const string DetachGeometry = "M1.5,4.5 L1.5,10.5 L7.5,10.5 M5.2,1.5 L10.5,1.5 L10.5,6.8 M10.2,1.8 L4.5,7.5";
    private const string MergeGeometry = "M1.5,1.5 L10.5,1.5 L10.5,10.5 L1.5,10.5 Z M9.1,2.9 L4.1,7.9 M4.1,4.8 L4.1,7.9 L7.2,7.9";
    private const string DismissGeometry = "M2.5,2.5 L9.5,9.5 M9.5,2.5 L2.5,9.5";
}
