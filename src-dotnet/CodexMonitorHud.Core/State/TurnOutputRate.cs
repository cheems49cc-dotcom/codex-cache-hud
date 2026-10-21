using CodexMonitorHud.Core.Models;

namespace CodexMonitorHud.Core.State;

/// <summary>Reported output per wall-clock second of one observed turn, not model generation speed.</summary>
public sealed class TurnOutputRate
{
    private (long, long, long, long, long, long)? _lastUsage;
    private DateTimeOffset _lastUsageAt = DateTimeOffset.MinValue;
    private bool _valid = true;

    public string TurnId { get; private set; } = string.Empty;
    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.MinValue;
    public DateTimeOffset? EndedAt { get; private set; }
    public long OutputTokens { get; private set; }
    public bool HasUsage { get; private set; }
    public bool Aborted { get; private set; }

    public void Observe(HudRecord record)
    {
        if (record.Kind == HudRecordKind.Started)
        {
            if (_valid && record.Timestamp < StartedAt) return;
            if (StartedAt != DateTimeOffset.MinValue && record.TurnId == TurnId &&
                record.Timestamp == StartedAt)
                return;

            TurnId = record.TurnId;
            StartedAt = record.Timestamp;
            EndedAt = null;
            OutputTokens = 0;
            HasUsage = false;
            Aborted = false;
            _valid = !record.TimestampIsEstimated;
        }
        else if (record.Kind is HudRecordKind.Completed or HudRecordKind.CompletedSilent or HudRecordKind.Aborted)
        {
            if (StartedAt == DateTimeOffset.MinValue || !record.MatchesTurn(TurnId) || EndedAt.HasValue)
                return;

            EndedAt = record.Timestamp;
            Aborted = record.Kind == HudRecordKind.Aborted;
            _valid &= !record.TimestampIsEstimated;
        }
        else if (record.Kind == HudRecordKind.Usage && !record.IsCompactionEstimate)
        {
            // Hydration may be followed by replay from an older reader offset.
            if (record.Timestamp < _lastUsageAt) return;
            var signature = (record.TaskInput, record.TaskCached, record.TaskCacheWrite,
                record.TaskOutput, record.TaskReasoning, record.TaskTotal);
            if (_lastUsage == signature) return;
            _lastUsage = signature;
            _lastUsageAt = record.Timestamp;
            if (StartedAt == DateTimeOffset.MinValue || EndedAt.HasValue) return;
            if (record.Timestamp < StartedAt || record.Output < 0 || record.Output > long.MaxValue - OutputTokens)
            {
                _valid = false;
                return;
            }
            // output_tokens already includes reasoning; do not add it again.
            OutputTokens += record.Output;
            HasUsage = true;
        }
    }

    public double? AverageAt(DateTimeOffset now)
    {
        var seconds = ((EndedAt ?? now) - StartedAt).TotalSeconds;
        return _valid && HasUsage && StartedAt != DateTimeOffset.MinValue && seconds > 0
            ? OutputTokens / seconds
            : null;
    }
}
