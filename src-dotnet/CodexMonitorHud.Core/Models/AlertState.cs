namespace CodexMonitorHud.Core.Models;

public enum AlertState
{
    GOOD,
    OK,
    LOW_CACHE,
    CACHE_MISS,
    VERY_LOW_CACHE,
    SUDDEN_CACHE_DROP,
    CACHE_STUCK
}
