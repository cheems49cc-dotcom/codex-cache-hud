using CodexMonitorHud.Core.Models;
using CodexMonitorHud.Core.Sessions;

namespace CodexMonitorHud.Core.State;

public sealed class SessionState
{
    public required string Path { get; init; }
    public required int Number { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required IncrementalJsonlReader Reader { get; init; }
    public string Model { get; set; } = string.Empty;
    public string Workspace { get; set; } = string.Empty;
    public HudSnapshot? Snapshot { get; set; }
    /// <summary>Newest fifty real calls, in chronological order.</summary>
    public IReadOnlyList<CallUsageSample> CallHistory => _callHistory;
    public AlertState AlertState { get; set; } = AlertState.OK;
    public string LastTotalUsageSignature { get; set; } = string.Empty;
    public DateTimeOffset? AllowanceTimestamp { get; set; }
    public double? WeeklyRemainingPercent { get; set; }
    public double? FiveHourRemainingPercent { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    public DateTimeOffset LastUsageAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastReadErrorAt { get; set; } = DateTimeOffset.MinValue;
    public string LastRenderedStatus { get; set; } = string.Empty;
    public string TerminalStatus { get; set; } = string.Empty;
    public DateTimeOffset TerminalAt { get; set; } = DateTimeOffset.MinValue;
    public bool TerminalSilent { get; set; }
    public bool TerminalExitStarted { get; set; }
    public bool TerminalExitCompleted { get; set; }
    public DateTimeOffset TerminalExitUntil { get; set; } = DateTimeOffset.MinValue;
    public int TerminalExitRevision { get; set; }
    public bool HasObservedActivity { get; set; }
    public int AttentionRevision { get; set; }
    public string AttentionReason { get; set; } = string.Empty;
    public DateTimeOffset AttentionUntil { get; set; } = DateTimeOffset.MinValue;
    public string AgentNoticeText { get; set; } = string.Empty;
    public DateTimeOffset AgentNoticeUntil { get; set; } = DateTimeOffset.MinValue;
    public AgentAnimationRecipe? AgentNoticeRecipe { get; set; }
    public int ContextAlertLevel { get; set; }
    public double ContextAlertPercent { get; set; }
    public DateTimeOffset ContextAlertUntil { get; set; } = DateTimeOffset.MinValue;
    public string ActiveTurnId { get; set; } = string.Empty;
    public bool TurnInProgress { get; set; }
    public DateTimeOffset TurnStartedAt { get; set; } = DateTimeOffset.MinValue;
    public string PendingCompletionTurnId { get; set; } = string.Empty;
    public DateTimeOffset PendingCompletionAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset PendingCompletionDueAt { get; set; } = DateTimeOffset.MinValue;
    public bool IsInternalSession { get; set; }
    public bool IdentityMetadataFound { get; set; }
    public bool IdentityProvisional { get; set; }
    public bool NeedsSnapshotHydration { get; set; }
    public bool IsReadBlocked { get; set; }
    public DateTimeOffset LastLockObservedAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset RuntimeActivityAt { get; set; } = DateTimeOffset.MinValue;
    public string SessionId { get; set; } = string.Empty;
    public string ConversationLabel { get; set; } = string.Empty;
    public string ProfileId { get; init; } = SessionProfile.DefaultId;
    public string ProfileLabel { get; init; } = "Codex";
    public string ClientSurface { get; set; } = "unknown";
    public string ModelProvider { get; set; } = string.Empty;
    public bool Dismissed { get; set; }
    public double BubbleWidth { get; set; }
    public double BubbleHeight { get; set; }

    private readonly List<CallUsageSample> _callHistory = [];

    internal bool AddCallUsageSample(CallUsageSample sample)
    {
        if (_callHistory.Count > 0 && _callHistory[^1].TaskTotal == sample.TaskTotal &&
            _callHistory[^1].TaskInput == sample.TaskInput &&
            _callHistory[^1].TaskCached == sample.TaskCached &&
            _callHistory[^1].TaskCacheWrite == sample.TaskCacheWrite &&
            _callHistory[^1].TaskOutput == sample.TaskOutput &&
            _callHistory[^1].TaskReasoning == sample.TaskReasoning)
        {
            return false;
        }

        if (_callHistory.Count == 50)
        {
            _callHistory.RemoveAt(0);
        }

        _callHistory.Add(sample);
        return true;
    }

    internal void SetLatestCallAlert(AlertState alertState)
    {
        if (_callHistory.Count > 0)
        {
            _callHistory[^1] = _callHistory[^1] with { AlertState = alertState };
        }
    }
}
