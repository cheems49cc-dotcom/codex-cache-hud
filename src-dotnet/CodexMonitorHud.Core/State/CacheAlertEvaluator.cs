using CodexMonitorHud.Core.Models;

namespace CodexMonitorHud.Core.State;

/// <summary>Deterministic cache-health classification for meaningful model calls only.</summary>
public static class CacheAlertEvaluator
{
    // Small prompts frequently have no reusable prefix.  They are not useful
    // cache-health evidence and must not create a red HUD state.
    public const long MinimumMeaningfulInputTokens = 2_000;
    public const double GoodCacheHitPercent = 90;
    public const double LowCacheHitPercent = 70;
    public const double VeryLowCacheHitPercent = 40;
    public const double SuddenDropPoints = 35;
    public const int StuckCallCount = 3;

    public static AlertState Evaluate(IReadOnlyList<CallUsageSample> history)
    {
        if (history.Count == 0)
        {
            return AlertState.OK;
        }

        var current = history[^1];
        if (current.Input < MinimumMeaningfulInputTokens)
        {
            return current.CacheHitPercent >= GoodCacheHitPercent ? AlertState.GOOD : AlertState.OK;
        }

        var hit = current.CacheHitPercent;
        if (hit == 0)
        {
            return HasConsecutiveMeaningfulMisses(history) ? AlertState.CACHE_STUCK : AlertState.CACHE_MISS;
        }

        if (HasSuddenDrop(history, current))
        {
            return AlertState.SUDDEN_CACHE_DROP;
        }

        if (hit < VeryLowCacheHitPercent)
        {
            return AlertState.VERY_LOW_CACHE;
        }

        if (hit < LowCacheHitPercent)
        {
            return AlertState.LOW_CACHE;
        }

        return hit >= GoodCacheHitPercent ? AlertState.GOOD : AlertState.OK;
    }

    private static bool HasConsecutiveMeaningfulMisses(IReadOnlyList<CallUsageSample> history)
    {
        var meaningfulMisses = 0;
        for (var index = history.Count - 1; index >= 0; index--)
        {
            var sample = history[index];
            if (sample.Input < MinimumMeaningfulInputTokens)
            {
                continue;
            }

            if (sample.CacheHitPercent != 0)
            {
                return false;
            }

            if (++meaningfulMisses >= StuckCallCount)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSuddenDrop(IReadOnlyList<CallUsageSample> history, CallUsageSample current)
    {
        for (var index = history.Count - 2; index >= 0; index--)
        {
            var previous = history[index];
            if (previous.Input < MinimumMeaningfulInputTokens)
            {
                continue;
            }

            return previous.CacheHitPercent >= GoodCacheHitPercent &&
                   previous.CacheHitPercent - current.CacheHitPercent >= SuddenDropPoints;
        }

        return false;
    }
}
