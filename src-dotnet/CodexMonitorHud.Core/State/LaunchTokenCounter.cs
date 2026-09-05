namespace CodexMonitorHud.Core.State;

/// <summary>Counts local Codex token deltas for this HUD process only.</summary>
public sealed class LaunchTokenCounter(DateTimeOffset launchedAt)
{
    private readonly Dictionary<string, Observation> _last = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset LaunchedAt { get; } = launchedAt;
    public long Input { get; private set; }
    public long Cached { get; private set; }
    public long Output { get; private set; }
    public long Total { get; private set; }
    public long ContextGrowth { get; private set; }
    public double CacheHitPercent => Input <= 0 ? 0 : Math.Clamp(Cached, 0, Input) * 100.0 / Input;

    public void EstablishBaseline(IEnumerable<SessionState> states)
    {
        _last.Clear();
        foreach (var state in states)
        {
            _last[state.Path] = Observe(state, countFromZero: false);
        }
    }

    public bool Update(IEnumerable<SessionState> states)
    {
        var changed = false;
        foreach (var state in states)
        {
            var current = Counters(state);
            if (_last.TryGetValue(state.Path, out var previous))
            {
                if (!previous.Initialized)
                {
                    if (current.HasValue && previous.CountFromZero)
                    {
                        Add(current.Value);
                        changed |= current.Value.Total > 0 || current.Value.Input > 0 ||
                                   current.Value.Cached > 0 || current.Value.Output > 0 ||
                                   current.Value.Context > 0;
                    }
                    _last[state.Path] = current.HasValue
                        ? new Observation(current.Value.Input, current.Value.Cached, current.Value.Output, current.Value.Total, current.Value.Context, true, previous.CountFromZero)
                        : previous;
                    continue;
                }

                if (!current.HasValue)
                {
                    continue;
                }

                var observed = current.Value;
                var inputDelta = Math.Max(0, observed.Input - previous.Input);
                var cachedDelta = Math.Max(0, observed.Cached - previous.Cached);
                var outputDelta = Math.Max(0, observed.Output - previous.Output);
                var totalDelta = Math.Max(0, observed.Total - previous.Total);
                var contextDelta = Math.Max(0, observed.Context - previous.Context);
                if (inputDelta > 0 || cachedDelta > 0 || outputDelta > 0 || totalDelta > 0 || contextDelta > 0)
                {
                    Input += inputDelta;
                    Cached += cachedDelta;
                    Output += outputDelta;
                    Total += totalDelta;
                    ContextGrowth += contextDelta;
                    changed = true;
                }
            }
            else
            {
                var countFromZero = state.StartedAt >= LaunchedAt;
                if (current.HasValue && countFromZero)
                {
                    Add(current.Value);
                    changed |= current.Value.Total > 0;
                }
            }

            _last[state.Path] = current.HasValue
                ? new Observation(current.Value.Input, current.Value.Cached, current.Value.Output, current.Value.Total, current.Value.Context, true, state.StartedAt >= LaunchedAt)
                : new Observation(0, 0, 0, 0, 0, false, state.StartedAt >= LaunchedAt);
        }

        return changed;
    }

    private void Add((long Input, long Cached, long Output, long Total, long Context) value)
    {
        Input += value.Input;
        Cached += value.Cached;
        Output += value.Output;
        Total += value.Total;
        ContextGrowth += value.Context;
    }

    private static Observation Observe(SessionState state, bool countFromZero)
    {
        var counters = Counters(state);
        return counters.HasValue
            ? new Observation(counters.Value.Input, counters.Value.Cached, counters.Value.Output, counters.Value.Total, counters.Value.Context, true, countFromZero)
            : new Observation(0, 0, 0, 0, 0, false, countFromZero);
    }

    private static (long Input, long Cached, long Output, long Total, long Context)? Counters(SessionState state) => state.Snapshot is { } snapshot
        ? (Math.Max(0, snapshot.TaskInput), Math.Clamp(snapshot.TaskCached, 0, Math.Max(0, snapshot.TaskInput)), Math.Max(0, snapshot.TaskOutput), Math.Max(0, snapshot.TaskTotal), Math.Max(0, snapshot.CallTotal))
        : null;

    private readonly record struct Observation(long Input, long Cached, long Output, long Total, long Context, bool Initialized, bool CountFromZero);
}
