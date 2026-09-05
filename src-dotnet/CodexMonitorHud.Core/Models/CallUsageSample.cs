namespace CodexMonitorHud.Core.Models;

/// <summary>A single, real model invocation retained for per-session cache diagnostics.</summary>
public sealed record CallUsageSample
{
    public DateTimeOffset Timestamp { get; init; }
    public long Input { get; init; }
    public long Cached { get; init; }
    public long CacheWrite { get; init; }
    public long Uncached { get; init; }
    public long Output { get; init; }
    public long Reasoning { get; init; }
    public long Total { get; init; }
    public long ContextTokens { get; init; }
    public long ContextWindow { get; init; }
    public double ContextPercent { get; init; }
    public AlertState AlertState { get; init; } = AlertState.OK;
    public long TaskInput { get; init; }
    public long TaskCached { get; init; }
    public long TaskCacheWrite { get; init; }
    public long TaskOutput { get; init; }
    public long TaskReasoning { get; init; }
    public long TaskTotal { get; init; }

    public double CacheHitPercent => Input <= 0 ? 0 : Math.Clamp(Cached, 0, Input) * 100.0 / Input;

    public static CallUsageSample FromRecord(HudRecord record) => new()
    {
        Timestamp = record.Timestamp,
        Input = record.Input,
        Cached = record.Cached,
        CacheWrite = record.CacheWrite,
        Uncached = record.Uncached,
        Output = record.Output,
        Reasoning = record.Reasoning,
        Total = record.CallTotal,
        ContextTokens = record.CallTotal,
        ContextWindow = record.ContextWindow,
        ContextPercent = record.ContextPercent,
        TaskInput = record.TaskInput,
        TaskCached = record.TaskCached,
        TaskCacheWrite = record.TaskCacheWrite,
        TaskOutput = record.TaskOutput,
        TaskReasoning = record.TaskReasoning,
        TaskTotal = record.TaskTotal
    };
}
