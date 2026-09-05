using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexMonitorHud.Core.Configuration;
using CodexMonitorHud.Core.Models;
using CodexMonitorHud.Core.Parsing;
using CodexMonitorHud.Core.Presentation;
using CodexMonitorHud.Core.Pricing;
using CodexMonitorHud.Core.Sessions;
using CodexMonitorHud.Core.State;

var repositoryRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepositoryRoot();
var tests = new (string Name, Action Run)[]
{
    ("record parsing", TestRecordParsing),
    ("call history and cache health", TestCallHistoryAndCacheHealth),
    ("tokens since HUD launch", TestLaunchTokenCounter),
    ("JSON line splitting", TestLineSplitting),
    ("incremental title index", TestTitleIndex),
    ("bounded tail snapshot", TestBoundedTail),
    ("multi-date discovery and cap", TestDiscovery),
    ("snapshot aggregation", TestAggregation),
    ("task number cooldown", TestTaskNumbers),
    ("session state engine", TestSessionEngine),
    ("locked long-running session recovery", TestLockedSessionRecovery),
    ("state database long-running session heartbeat", TestStateDatabaseHeartbeat),
    ("desktop, VS Code, and CLI source identity", TestSessionSources),
    ("formatting and deep links", TestFormatting),
    ("direct HUD placement", TestPlacement),
    ("surface effect adaptation", TestSurfaceEffects),
    ("structural config recovery", TestConfiguration),
    ("macOS path and watcher portability", TestMacPortability),
    ("pricing", TestPricing)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"Core {test.Name}: OK");
}

Console.WriteLine($"CodexMonitorHud.Core tests: OK ({tests.Length})");
return;

void TestRecordParsing()
{
    var context = HudRecordParser.Parse("""{"type":"turn_context","payload":{"cwd":"C:\\Synthetic\\pro-workspace","model":"gpt-test"}}""");
    Equal(HudRecordKind.Context, context?.Kind, "context kind");
    Equal("pro-workspace", context?.Workspace, "privacy-safe workspace leaf");
    Equal("gpt-test", context?.Model, "model");

    var completed = HudRecordParser.Parse("""{"timestamp":"2026-07-13T08:00:00Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"visible","last_agent_message":"Done."}}""");
    var silent = HudRecordParser.Parse("""{"timestamp":"2026-07-13T08:00:00Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"silent","last_agent_message":""}}""");
    Equal(HudRecordKind.Completed, completed?.Kind, "visible completion");
    Equal(HudRecordKind.Completed, silent?.Kind, "completion never reads assistant-message content");

    var usage = HudRecordParser.Parse("""{"timestamp":"2026-07-13T08:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":100,"cached_input_tokens":60,"output_tokens":20,"reasoning_output_tokens":5,"total_tokens":120},"total_token_usage":{"input_tokens":1000,"cached_input_tokens":600,"output_tokens":200,"total_tokens":1200},"model_context_window":1000},"rate_limits":{"primary":{"used_percent":14,"window_minutes":300},"secondary":{"used_percent":18,"window_minutes":10080}}}}""");
    NotNull(usage, "usage record");
    Equal(40L, usage!.Uncached, "uncached input");
    Equal(120L, usage.CallTotal, "call total");
    Equal(12d, usage.ContextPercent, "context percent uses last total");
    Equal(86d, usage.FiveHourRemainingPercent, "five-hour allowance");
    Equal(82d, usage.WeeklyRemainingPercent, "weekly allowance");
    IsTrue(HudSnapshot.FromUsage(usage).AccountingIsValid, "accounting invariant");
    var usageWithoutLimits = HudRecordParser.Parse("""{"timestamp":"2026-07-13T08:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":null,"info":{"last_token_usage":{"input_tokens":100,"cached_input_tokens":60,"output_tokens":20},"total_token_usage":{"input_tokens":100,"cached_input_tokens":60,"output_tokens":20,"total_tokens":120},"model_context_window":1000}}}""");
    Equal(HudRecordKind.Usage, usageWithoutLimits?.Kind, "null rate limits do not hide usage");
    Equal(120L, usageWithoutLimits?.CallTotal, "null rate limits keep call totals");
    var cacheWrite = HudRecordParser.Parse("""{"type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":10000,"cached_input_tokens":9600,"cache_write_tokens":33,"output_tokens":200,"reasoning_output_tokens":21,"total_tokens":10821},"total_token_usage":{"input_tokens":10000,"cached_input_tokens":9600,"cache_write_tokens":33,"output_tokens":200,"reasoning_output_tokens":21,"total_tokens":10821},"model_context_window":200000}}}""");
    Equal(33L, cacheWrite?.CacheWrite, "cache-write accounting is parsed");
    Equal(10821L, cacheWrite?.CallTotal, "last total is canonical call total");
    Equal(10821d / 2000d, Math.Round(cacheWrite!.ContextPercent, 4), "context uses last total rather than input");
    var compaction = HudRecordParser.Parse("""{"type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":0,"cached_input_tokens":0,"cache_write_tokens":0,"output_tokens":0,"reasoning_output_tokens":0,"total_tokens":180000},"total_token_usage":{"total_tokens":180000},"model_context_window":200000}}}""");
    IsTrue(compaction?.IsCompactionEstimate == true, "all-zero breakdown is a compaction estimate");
    Equal<HudRecord?>(null, HudRecordParser.Parse("""{"type":"response_item","payload":{"text":"private"}}"""), "irrelevant content is rejected");
    var padded = HudRecordParser.Parse("{" + new string(' ', 4096) + "\"timestamp\":\"2026-07-13T08:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"padded\"}}");
    Equal(HudRecordKind.Started, padded?.Kind, "relevant type beyond the old 1 KiB prefix is accepted");
    var largeCompletion = HudRecordParser.Parse("{\"timestamp\":\"2026-07-13T08:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"large\",\"last_agent_message\":\"" + new string('x', 512 * 1024) + "\"}}");
    Equal(HudRecordKind.Completed, largeCompletion?.Kind, "large completion message is reduced to lifecycle state");
}

void TestCallHistoryAndCacheHealth()
{
    WithTemporaryDirectory(root =>
    {
        var sessionsRoot = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sessionsRoot);
        var indexPath = Path.Combine(root, "session_index.jsonl");
        var firstPath = Path.Combine(sessionsRoot, "first.jsonl");
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        File.WriteAllText(firstPath, string.Join('\n', new[]
        {
            """{"type":"session_meta","payload":{"id":"history-one","cwd":"C:\\Synthetic\\history","originator":"Codex Desktop","source":"vscode"}}""",
            """{"type":"turn_context","payload":{"cwd":"C:\\Synthetic\\history","model":"gpt-test"}}""",
            UsageLine(now, 10_000, 9_600, 10_250, 10_000, 9_600, 10_250)
        }) + '\n', new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(firstPath, now.UtcDateTime);

        var engine = new SessionMonitorEngine(sessionsRoot, indexPath, new HudRuntimeOptions { ActiveWindowMinutes = 60 });
        IsTrue(engine.RefreshActiveSessions(now), "history seed session discovery");
        var first = engine.GetVisibleStates(now).Single();
        Equal(1, first.CallHistory.Count, "bounded tail seeds the first real call");
        Equal(AlertState.GOOD, first.AlertState, "96 percent cache hit is good");
        Equal(AlertState.GOOD, first.CallHistory[0].AlertState, "history retains each call's alert state");
        Equal(10_250L, first.CallHistory[0].ContextTokens, "history retains per-call context tokens");
        Equal(200_000L, first.CallHistory[0].ContextWindow, "history retains per-call context window");
        Equal(5.125d, first.CallHistory[0].ContextPercent, "history retains per-call context percent");

        File.AppendAllText(firstPath, UsageLine(now.AddSeconds(1), 10_000, 9_600, 10_250, 10_000, 9_600, 10_250, usedPercent: 25) + '\n', new UTF8Encoding(false));
        IsTrue(engine.Poll(now.AddSeconds(1)), "duplicate cumulative usage is consumed for rate limits");
        Equal(1, first.CallHistory.Count, "same total signature does not add history");
        Equal(75d, first.FiveHourRemainingPercent, "same total signature still updates rate limits");

        File.AppendAllText(firstPath, UsageLine(now.AddSeconds(2), 2_432, 0, 2_500, 12_432, 9_600, 12_750) + '\n', new UTF8Encoding(false));
        IsTrue(engine.Poll(now.AddSeconds(2)), "cache-miss call is consumed");
        Equal(2, first.CallHistory.Count, "true cumulative growth adds history");
        Equal(AlertState.CACHE_MISS, first.AlertState, "meaningful 2432-token miss is flagged");

        File.AppendAllText(firstPath, UsageLine(now.AddSeconds(3), 2_432, 0, 2_500, 14_864, 9_600, 15_250) + '\n' +
            UsageLine(now.AddSeconds(4), 2_432, 0, 2_500, 17_296, 9_600, 17_750) + '\n', new UTF8Encoding(false));
        IsTrue(engine.Poll(now.AddSeconds(4)), "repeated misses are consumed");
        Equal(AlertState.CACHE_STUCK, first.AlertState, "three meaningful misses are cache stuck");

        File.AppendAllText(firstPath, UsageLine(now.AddSeconds(5), 8_000, 7_840, 8_100, 25_296, 17_440, 25_850) + '\n' +
            UsageLine(now.AddSeconds(6), 5_000, 1_000, 5_100, 30_296, 18_440, 30_950) + '\n', new UTF8Encoding(false));
        IsTrue(engine.Poll(now.AddSeconds(6)), "recovery and drop calls are consumed");
        Equal(AlertState.SUDDEN_CACHE_DROP, first.AlertState, "large drop after a healthy cache is flagged");

        var historyCount = first.CallHistory.Count;
        File.AppendAllText(firstPath,
            """{"timestamp":"2026-08-28T12:00:07Z","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":0,"cached_input_tokens":0,"cache_write_tokens":0,"output_tokens":0,"reasoning_output_tokens":0,"total_tokens":180000},"total_token_usage":{"total_tokens":180000},"model_context_window":200000}}}""" + '\n',
            new UTF8Encoding(false));
        IsTrue(engine.Poll(now.AddSeconds(7)), "compaction estimate is consumed");
        Equal(historyCount, first.CallHistory.Count, "compaction does not add a false call");
        Equal(90d, first.Snapshot?.ContextPercent, "compaction updates context from last total");
        Equal(AlertState.SUDDEN_CACHE_DROP, first.AlertState, "compaction does not create a cache alert");

        var partial = UsageLine(now.AddSeconds(8), 5_000, 4_800, 5_100, 35_296, 23_240, 36_050);
        File.AppendAllText(firstPath, partial[..^2], new UTF8Encoding(false));
        _ = engine.Poll(now.AddSeconds(8));
        Equal(historyCount, first.CallHistory.Count, "partial JSON does not add history");
        File.AppendAllText(firstPath, partial[^2..] + '\n', new UTF8Encoding(false));
        IsTrue(engine.Poll(now.AddSeconds(8.1)), "completed partial JSON is consumed once");
        Equal(historyCount + 1, first.CallHistory.Count, "completed partial JSON adds one real call");

        var replacement = string.Join('\n', new[]
        {
            """{"type":"session_meta","payload":{"id":"history-one","cwd":"C:\\Synthetic\\history","originator":"Codex Desktop","source":"vscode"}}""",
            """{"type":"turn_context","payload":{"cwd":"C:\\Synthetic\\history","model":"gpt-test"}}""",
            UsageLine(now.AddSeconds(9), 4_000, 3_800, 4_100, 39_296, 27_040, 40_150)
        }) + '\n';
        File.WriteAllText(firstPath, replacement, new UTF8Encoding(false));
        IsTrue(engine.Poll(now.AddSeconds(9)), "truncated rollout restarts incremental parsing");
        Equal(historyCount + 2, first.CallHistory.Count, "truncation retains an independently new call");

        var ring = string.Concat(Enumerable.Range(0, 55).Select(index => UsageLine(
            now.AddSeconds(10 + index), 3_000, 2_940, 3_100, 42_296 + index * 3_000, 29_980 + index * 2_940, 43_250 + index * 3_100) + '\n'));
        File.AppendAllText(firstPath, ring, new UTF8Encoding(false));
        IsTrue(engine.Poll(now.AddMinutes(1)), "ring-history calls are consumed");
        Equal(50, first.CallHistory.Count, "per-session call history is capped at fifty");

        var secondPath = Path.Combine(sessionsRoot, "second.jsonl");
        File.WriteAllText(secondPath, string.Join('\n', new[]
        {
            """{"type":"session_meta","payload":{"id":"history-two","cwd":"C:\\Synthetic\\second","originator":"Codex Desktop","source":"vscode"}}""",
            UsageLine(now, 2_432, 0, 2_500, 2_432, 0, 2_500)
        }) + '\n', new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(secondPath, now.UtcDateTime);
        IsTrue(engine.RefreshActiveSessions(now.AddMinutes(1)), "second session is discovered");
        var second = engine.GetVisibleStates(now.AddMinutes(1)).Single(state => state.SessionId == "history-two");
        Equal(1, second.CallHistory.Count, "second session has independent history");
        Equal(AlertState.CACHE_MISS, second.AlertState, "second session cache state is isolated");
        Equal(50, first.CallHistory.Count, "second session does not mutate first history");

        Equal(AlertState.OK, CacheAlertEvaluator.Evaluate(new[]
        {
            new CallUsageSample { Input = 100, Cached = 0, Total = 100 }
        }), "short misses do not cause false cache alerts");
    });
}

void TestLaunchTokenCounter()
{
    var launchedAt = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
    var existing = TokenState("existing", launchedAt.AddHours(-1), 1_000, 800, 100);
    var counter = new LaunchTokenCounter(launchedAt);
    counter.EstablishBaseline(new[] { existing });
    Equal(0L, counter.Total, "startup baseline is zero");

    existing.Snapshot = existing.Snapshot! with { TaskInput = 1_400, TaskCached = 1_180, TaskOutput = 160, TaskTotal = 1_560, CallTotal = 1_300 };
    IsTrue(counter.Update(new[] { existing }), "positive usage delta is observed");
    Equal(460L, counter.Total, "input plus output delta is counted once");
    Equal(200L, counter.ContextGrowth, "context growth starts from the launch baseline");

    var newSession = TokenState("new", launchedAt.AddMinutes(1), 200, 180, 30);
    IsTrue(counter.Update(new[] { existing, newSession }), "new post-launch session is counted from zero");
    Equal(690L, counter.Total, "new-session totals are included");
    Equal(430L, counter.ContextGrowth, "new post-launch session context is counted from zero");
    Equal(93.3333d, Math.Round(counter.CacheHitPercent, 4), "cache hit aggregates all observed session input");

    existing.Snapshot = existing.Snapshot! with { TaskInput = 10, TaskOutput = 5, TaskTotal = 15, CallTotal = 15 };
    IsTrue(!counter.Update(new[] { existing, newSession }), "counter resets never subtract or add false usage");
    Equal(690L, counter.Total, "counter remains monotonic");
    Equal(430L, counter.ContextGrowth, "context compaction never subtracts prior growth");

    var lateExisting = TokenState("late-existing", launchedAt.AddHours(-2), 0, 0, 0);
    lateExisting.Snapshot = null;
    var lateCounter = new LaunchTokenCounter(launchedAt);
    lateCounter.EstablishBaseline(new[] { lateExisting });
    lateExisting.Snapshot = new HudSnapshot { Timestamp = launchedAt, TaskInput = 9_000, TaskOutput = 500, TaskTotal = 9_500 };
    IsTrue(!lateCounter.Update(new[] { lateExisting }), "late hydration of an old session only establishes its baseline");
    Equal(0L, lateCounter.Total, "old history is not counted after launch");
}

SessionState TokenState(string name, DateTimeOffset startedAt, long input, long cached, long output) => new()
{
    Path = name,
    Number = 1,
    StartedAt = startedAt,
    Reader = new IncrementalJsonlReader(0),
    Snapshot = new HudSnapshot
    {
        Timestamp = startedAt,
        TaskInput = input,
        TaskCached = cached,
        TaskOutput = output,
        TaskTotal = input + output,
        CallTotal = input + output
    }
};

string UsageLine(
    DateTimeOffset timestamp,
    long input,
    long cached,
    long total,
    long taskInput,
    long taskCached,
    long taskTotal,
    double? usedPercent = null)
{
    var last = new
    {
        input_tokens = input,
        cached_input_tokens = cached,
        output_tokens = 100L,
        reasoning_output_tokens = 10L,
        total_tokens = total
    };
    var cumulative = new
    {
        input_tokens = taskInput,
        cached_input_tokens = taskCached,
        output_tokens = Math.Max(0, taskTotal - taskInput),
        reasoning_output_tokens = 10L,
        total_tokens = taskTotal
    };
    var limits = usedPercent.HasValue
        ? new { primary = new { used_percent = usedPercent.Value, window_minutes = 300 } }
        : null;
    return JsonSerializer.Serialize(new
    {
        timestamp = timestamp.ToString("O"),
        type = "event_msg",
        payload = new
        {
            type = "token_count",
            info = new { last_token_usage = last, total_token_usage = cumulative, model_context_window = 200_000 },
            rate_limits = limits
        }
    });
}

void TestLineSplitting()
{
    var complete = JsonLineSplitter.Split(string.Empty, "{\"type\":\"event_msg\"}");
    Equal(1, complete.CompleteLines.Count, "complete no-newline JSON");
    Equal(string.Empty, complete.PendingText, "no pending complete JSON");
    var partial = JsonLineSplitter.Split(string.Empty, "{\"type\":\"event_msg\"");
    Equal(0, partial.CompleteLines.Count, "partial record count");
    IsTrue(partial.PendingText.Length > 0, "partial record retained");
    WithTemporaryDirectory(root =>
    {
        var path = Path.Combine(root, "burst.jsonl");
        var payload = string.Join('\n', Enumerable.Range(0, 5000).Select(index => $"{{\"index\":{index}}}")) + "\n";
        File.WriteAllText(path, payload, new UTF8Encoding(false));
        var reader = new IncrementalJsonlReader(0);
        var lines = reader.ReadAppended(path);
        Equal(5000, lines.Count, "streaming burst line count");
        Equal(new FileInfo(path).Length, reader.Offset, "streaming burst offset");

        reader.Reset();
        var budgetedLines = new List<string>();
        do
        {
            budgetedLines.AddRange(reader.ReadAppended(path, 4096));
        } while (reader.HasUnreadData);
        Equal(5000, budgetedLines.Count, "budgeted streaming burst line count");
        Equal(new FileInfo(path).Length, reader.Offset, "budgeted streaming burst offset");
    });
}

void TestTitleIndex()
{
    WithTemporaryDirectory(root =>
    {
        var path = Path.Combine(root, "session_index.jsonl");
        var encoding = new UTF8Encoding(false);
        File.WriteAllText(path, "{\"id\":\"one\",\"thread_name\":\"First title\"}\n", encoding);
        var index = new SessionTitleIndex();
        IsTrue(index.Refresh(path), "initial title index refresh");
        Equal("First title", index.GetTitle("one"), "initial title");

        File.AppendAllText(path, "{\"id\":\"one\",\"thread_name\":\"Updated title\"}\n{\"id\":\"two\",\"thread_name\":\"Second title\"}\n", encoding);
        IsTrue(index.Refresh(path), "appended title index refresh");
        Equal("Updated title", index.GetTitle("one"), "appended title replaces prior value");
        Equal("Second title", index.GetTitle("two"), "appended title is added");
        IsTrue(!index.Refresh(path), "unchanged title index is skipped");

        File.WriteAllText(path, "{\"id\":\"three\",\"thread_name\":\"Replacement title\"}\n", encoding);
        IsTrue(index.Refresh(path), "truncated title index rebuild");
        Equal(string.Empty, index.GetTitle("one"), "truncation removes stale title");
        Equal("Replacement title", index.GetTitle("three"), "truncation replacement title");

        var burst = string.Concat(Enumerable.Range(0, 30_000).Select(value =>
            $"{{\"id\":\"burst-{value}\",\"thread_name\":\"Burst title {value}\"}}\n"));
        File.WriteAllText(path, burst, encoding);
        IsTrue(index.Refresh(path), "large title index starts a bounded refresh");
        IsTrue(index.HasBacklog, "large title index exposes unread backlog");
        for (var pass = 0; pass < 8 && index.HasBacklog; pass++)
        {
            _ = index.Refresh(path);
        }
        IsTrue(!index.HasBacklog, "large title index backlog drains across bounded passes");
        Equal(string.Empty, index.GetTitle("three"), "larger replacement removes stale title");
        Equal("Burst title 29999", index.GetTitle("burst-29999"), "last title survives bounded backlog");
    });
}

void TestBoundedTail()
{
    WithTemporaryDirectory(root =>
    {
        var path = Path.Combine(root, "tail.jsonl");
        var records = new[]
        {
            """{"timestamp":"2026-07-13T08:00:00Z","type":"turn_context","payload":{"cwd":"C:\\Synthetic\\tail-workspace","model":"gpt-test"}}""",
            """{"timestamp":"2026-07-13T08:00:01Z","type":"event_msg","payload":{"type":"task_started","turn_id":"turn-1"}}""",
            """{"timestamp":"2026-07-13T08:00:02Z","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":200,"cached_input_tokens":150,"output_tokens":30,"reasoning_output_tokens":7,"total_tokens":230},"total_token_usage":{"total_tokens":2000},"model_context_window":2000}}}""",
            """{"timestamp":"2026-07-13T08:00:03Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-1","last_agent_message":"Done."}}"""
        };
        File.WriteAllText(path, string.Join('\n', records), new UTF8Encoding(false));
        var snapshot = BoundedTailReader.ReadLatestSnapshot(path);
        NotNull(snapshot, "tail snapshot");
        Equal("tail-workspace", snapshot!.Workspace, "tail workspace");
        Equal("gpt-test", snapshot.Model, "tail model");
        Equal("completed", snapshot.TerminalStatus, "terminal status");
        IsTrue(!snapshot.TurnInProgress, "completed tail is not an active turn");
        Equal(2000L, snapshot.TaskTotal, "task total");

        var runningPath = Path.Combine(root, "running-tail.jsonl");
        File.WriteAllText(runningPath, string.Join('\n', records[..^1]), new UTF8Encoding(false));
        var runningSnapshot = BoundedTailReader.ReadLatestSnapshot(runningPath);
        IsTrue(runningSnapshot?.TurnInProgress == true, "latest task_started tail is active");
        Equal(DateTimeOffset.Parse("2026-07-13T08:00:01Z"), runningSnapshot?.TurnStartedAt, "active turn start time");
        File.AppendAllText(runningPath, "\n" + """{"timestamp":"2026-07-13T08:00:04Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"older-turn"}}""" + "\n", new UTF8Encoding(false));
        IsTrue(BoundedTailReader.ReadLatestSnapshot(runningPath)?.TurnInProgress == true, "stale completion cannot close hydrated active turn");
        IsTrue(BoundedTailReader.ReadLatestTurnState(runningPath).InProgress, "waiting-turn hydration shares completion matching");

        var silentPath = Path.Combine(root, "silent-tail.jsonl");
        File.WriteAllText(silentPath, string.Join('\n', records[..^1]) + "\n" +
            """{"timestamp":"2026-07-13T08:00:03Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-1","last_agent_message":""}}""",
            new UTF8Encoding(false));
        var silentSnapshot = BoundedTailReader.ReadLatestSnapshot(silentPath);
        Equal("completed", silentSnapshot?.TerminalStatus, "completion remains semantic without reading assistant-message content");

        var unicodePath = Path.Combine(root, "unicode-tail.jsonl");
        var oldPadding = Enumerable.Range(0, 2500).Select(index => $"{{\"ignored\":{index}}}");
        var unicodeRecords = oldPadding.Concat(new[]
        {
            """{"timestamp":"2026-07-13T08:00:00Z","type":"turn_context","payload":{"cwd":"C:\\Synthetic\\中文项目","model":"gpt-test"}}""",
            """{"timestamp":"2026-07-13T08:00:02Z","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":20,"cached_input_tokens":10,"output_tokens":3,"total_tokens":23},"total_token_usage":{"total_tokens":23},"model_context_window":200}}}"""
        });
        File.WriteAllText(unicodePath, string.Join('\n', unicodeRecords), new UTF8Encoding(false));
        var unicodeSnapshot = BoundedTailReader.ReadLatestSnapshot(unicodePath);
        NotNull(unicodeSnapshot, "reverse chunk tail snapshot");
        Equal("中文项目", unicodeSnapshot!.Workspace, "UTF-8 tail survives chunk boundaries and no final newline");

        var largeTrailingPath = Path.Combine(root, "large-trailing-response.jsonl");
        var usage = """{"timestamp":"2026-07-17T08:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":321,"cached_input_tokens":120,"output_tokens":45,"total_tokens":366},"total_token_usage":{"input_tokens":321,"cached_input_tokens":120,"output_tokens":45,"total_tokens":366},"model_context_window":1000}}}""";
        var manyTrailingPath = Path.Combine(root, "many-trailing-records.jsonl");
        var irrelevantRecords = string.Concat(Enumerable.Repeat("{\"type\":\"response_item\",\"payload\":{}}\n", 2_001));
        File.WriteAllText(manyTrailingPath, usage + "\n" + irrelevantRecords, new UTF8Encoding(false));
        var deepSnapshot = BoundedTailReader.ReadLatestSnapshot(manyTrailingPath);
        Equal(366L, deepSnapshot?.TaskTotal, "many irrelevant trailing records do not hide latest token snapshot");
        var oversizedIrrelevantRecord = "{\"type\":\"response_item\",\"payload\":\"" + new string('x', checked((int)BoundedTailReader.MaximumTailBytes + 1)) + "\"}";
        File.WriteAllText(largeTrailingPath, usage + "\n" + oversizedIrrelevantRecord + "\n", new UTF8Encoding(false));
        var recoveredSnapshot = BoundedTailReader.ReadLatestSnapshot(largeTrailingPath);
        Equal(366L, recoveredSnapshot?.TaskTotal, "oversized irrelevant trailing record does not hide latest token snapshot");
    });
}

void TestDiscovery()
{
    WithTemporaryDirectory(root =>
    {
        var now = DateTime.UtcNow;
        for (var index = 0; index < 70; index++)
        {
            var folder = Path.Combine(root, "2026", "07", (index % 3 + 1).ToString("00"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"session-{index:00}.jsonl");
            File.WriteAllText(path, "{}", Encoding.UTF8);
            File.SetLastWriteTimeUtc(path, now.AddSeconds(-index));
        }

        var files = SessionDiscovery.GetActiveFiles(root, 30, 64, now);
        Equal(64, files.Count, "64-file cap");
        IsTrue(files.Zip(files.Skip(1), static (left, right) => left.LastWriteTimeUtc >= right.LastWriteTimeUtc).All(static ordered => ordered), "discovery order");
    });
}

void TestAggregation()
{
    var first = NewSnapshot(100, 60, 20, 1000, "model-a", DateTimeOffset.Parse("2026-07-13T08:00:00Z"));
    var second = NewSnapshot(200, 150, 30, 2000, "model-b", DateTimeOffset.Parse("2026-07-13T08:01:00Z"));
    var aggregate = SnapshotAggregator.Merge(new[] { first, second }, "{tasks} tasks / {models} models");
    NotNull(aggregate, "aggregate");
    Equal(300L, aggregate!.Input, "aggregate input");
    Equal(210L, aggregate.Cached, "aggregate cached");
    Equal(90L, aggregate.Uncached, "aggregate uncached");
    Equal(350L, aggregate.CallTotal, "aggregate call total");
    Equal(3000L, aggregate.TaskTotal, "aggregate task total");
    Equal(2, aggregate.ActiveTasks, "aggregate active task count");
    Equal("2 tasks / 2 models", aggregate.Model, "aggregate label");
}

void TestTaskNumbers()
{
    var pool = new TaskNumberPool();
    var now = DateTimeOffset.Parse("2026-07-13T08:00:00Z");
    Equal(1, pool.Acquire(now), "first task number");
    pool.Release(1, 120, now);
    Equal(2, pool.Acquire(now.AddSeconds(119)), "cooldown prevents early reuse");
    Equal(1, pool.Acquire(now.AddSeconds(120)), "released number is reused");
    for (var index = 0; index < 10_000; index++)
    {
        var number = pool.Acquire(now.AddHours(index + 1));
        pool.Release(number, 0, now.AddHours(index + 1));
    }
    IsTrue(pool.ReleasedCount <= pool.MaximumReleased, "released number queue remains bounded");
}

void TestSessionEngine()
{
    WithTemporaryDirectory(root =>
    {
        var profile = Path.Combine(root, "profile");
        var sessions = Path.Combine(profile, ".codex", "sessions", "2026", "07", "17");
        Directory.CreateDirectory(sessions);
        var indexPath = Path.Combine(profile, ".codex", "session_index.jsonl");
        var sessionPath = Path.Combine(sessions, "session.jsonl");
        var now = DateTimeOffset.Parse("2026-07-17T08:00:00Z");
        var initial = new[]
        {
            """{"timestamp":"2026-07-17T07:59:00Z","type":"session_meta","payload":{"id":"session-1","cwd":"C:\\Synthetic\\engine-workspace","originator":"Codex Desktop","source":"vscode"}}""",
            """{"timestamp":"2026-07-17T07:59:01Z","type":"turn_context","payload":{"cwd":"C:\\Synthetic\\engine-workspace","model":"gpt-test"}}""",
            """{"timestamp":"2026-07-17T07:59:02Z","type":"event_msg","payload":{"type":"task_started","turn_id":"turn-1"}}""",
            """{"timestamp":"2026-07-17T07:59:03Z","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":100,"cached_input_tokens":60,"output_tokens":20,"reasoning_output_tokens":5,"total_tokens":120},"total_token_usage":{"total_tokens":1000},"model_context_window":1000}}}"""
        };
        File.WriteAllText(sessionPath, string.Join('\n', initial) + "\n", new UTF8Encoding(false));
        File.WriteAllText(
            indexPath,
            "{\"id\":\"session-1\",\"thread_name\":\"Synthetic engine title\",\"updated_at\":\"2026-07-17T08:00:00Z\"}\n",
            new UTF8Encoding(false));

        var engine = new SessionMonitorEngine(
            Path.Combine(profile, ".codex", "sessions"),
            indexPath,
            new HudRuntimeOptions
            {
                ActiveWindowMinutes = 60,
                CompletionGraceSeconds = 8,
                TerminalHoldSeconds = 0,
                AttentionOnCompleted = false,
                AttentionOnSettled = true
            });
        IsTrue(engine.RefreshActiveSessions(now), "initial engine discovery");
        var state = engine.GetVisibleStates(now).Single();
        Equal("session-1", state.SessionId, "engine session id");
        Equal("Synthetic engine title", state.ConversationLabel, "official session index title");
        Equal("engine-workspace", state.Workspace, "engine workspace");

        var waitingPath = Path.Combine(sessions, "waiting-session.jsonl");
        File.WriteAllText(waitingPath, string.Join('\n', new[]
        {
            """{"timestamp":"2026-07-17T08:00:00Z","type":"session_meta","payload":{"id":"waiting-1","cwd":"C:\\Synthetic\\waiting-workspace","originator":"Codex Desktop","source":"vscode"}}""",
            """{"timestamp":"2026-07-17T08:00:01Z","type":"turn_context","payload":{"cwd":"C:\\Synthetic\\waiting-workspace","model":"gpt-test"}}""",
            """{"timestamp":"2026-07-17T08:00:02Z","type":"event_msg","payload":{"type":"task_started","turn_id":"waiting-turn"}}"""
        }) + '\n', new UTF8Encoding(false));
        IsTrue(engine.RefreshActiveSessions(now), "identity-confirmed waiting session discovery");
        var waiting = engine.GetVisibleStates(now).Single(item => item.SessionId == "waiting-1");
        Equal<HudSnapshot?>(null, waiting.Snapshot, "waiting session has no token snapshot yet");
        IsTrue(waiting.TurnInProgress, "task_started without tokens is still active");
        Equal("waiting-workspace", waiting.Workspace, "waiting session keeps metadata workspace");
        File.AppendAllText(indexPath, "{\"id\":\"session-1\",\"thread_name\":\"Updated engine title\",\"updated_at\":\"2026-07-17T08:00:01Z\"}\n", new UTF8Encoding(false));
        IsTrue(engine.RefreshTitles(), "incremental engine title refresh");
        Equal("Updated engine title", state.ConversationLabel, "incremental official title update");

        File.AppendAllText(sessionPath, """{"timestamp":"2026-07-17T08:00:01Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-1","last_agent_message":"Done."}}""", new UTF8Encoding(false));
        IsTrue(engine.Poll(now), "completion record consumed");
        IsTrue(!state.TurnInProgress, "completion immediately clears active turn result");
        Equal(DateTimeOffset.MinValue, state.TerminalAt, "completion grace retained");
        engine.HoldTerminalExits = true;
        IsTrue(engine.Poll(now.AddSeconds(8)), "completion grace advanced");
        Equal("completed", state.TerminalStatus, "completion confirmed");
        IsTrue(!state.TerminalExitStarted, "quiet task layout holds terminal exit");
        engine.HoldTerminalExits = false;
        IsTrue(engine.Poll(now.AddSeconds(8.1)), "terminal exit starts after quiet layout expands");
        IsTrue(state.TerminalExitStarted, "terminal exit released");

        File.AppendAllText(sessionPath, "\n" + """{"timestamp":"2026-07-17T08:00:09Z","type":"event_msg","payload":{"type":"task_started","turn_id":"turn-2"}}""", new UTF8Encoding(false));
        IsTrue(engine.Poll(now.AddSeconds(9)), "continuation consumed");
        IsTrue(state.TurnInProgress, "new task_started marks active turn");
        Equal(string.Empty, state.TerminalStatus, "new turn clears terminal state");
        Equal("active", engine.GetStatus(state, paused: false, now.AddSeconds(9)), "new turn is active");
        File.AppendAllText(sessionPath, "\n" + """{"timestamp":"2026-07-17T08:00:09.1Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-1"}}""" + "\n" +
            """{"timestamp":"2026-07-17T08:00:09.2Z","type":"event_msg","payload":{"type":"turn_aborted","turn_id":"turn-1"}}""" + "\n", new UTF8Encoding(false));
        _ = engine.Poll(now.AddSeconds(9.2));
        IsTrue(state.TurnInProgress, "stale completion and abort leave new turn active");
        Equal(DateTimeOffset.MinValue, state.PendingCompletionAt, "stale completion cannot schedule exit");

        File.AppendAllText(sessionPath, "\n" + """{"timestamp":"2026-07-17T08:00:09.5Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-2","last_agent_message":""}}""", new UTF8Encoding(false));
        IsTrue(engine.Poll(now.AddSeconds(9.5)), "content-independent completion record consumed");
        IsTrue(!state.TurnInProgress, "silent completion clears active turn result");
        Equal(DateTimeOffset.MinValue, state.TerminalAt, "completion remains in the normal grace window");

        var irrelevant = "{\"timestamp\":\"2026-07-17T08:00:10Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"internal_progress\"}}\n";
        var burst = "\n" + "{\"timestamp\":\"2026-07-17T08:00:10Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-3\"}}\n" +
            string.Concat(Enumerable.Repeat(irrelevant, 5_000)) +
            "{\"timestamp\":\"2026-07-17T08:00:11Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{\"input_tokens\":700,\"cached_input_tokens\":500,\"output_tokens\":77,\"total_tokens\":777},\"total_token_usage\":{\"total_tokens\":7777},\"model_context_window\":1000}}}";
        File.AppendAllText(sessionPath, burst, new UTF8Encoding(false));
        _ = engine.Poll(now.AddSeconds(11));
        IsTrue(engine.HasBacklog, "large appended burst is deferred by the per-session budget");
        for (var pass = 0; pass < 8 && engine.HasBacklog; pass++)
        {
            _ = engine.PollBacklog(now.AddSeconds(11));
        }
        IsTrue(!engine.HasBacklog, "session backlog drains across responsive passes");
        Equal(7777L, state.Snapshot?.TaskTotal, "record after bounded burst is eventually consumed");
        IsTrue(engine.AdvanceLifecycleOnly(now.AddSeconds(200)), "per-task active-to-idle transition is material");
        Equal("idle", engine.GetStatus(state, paused: false, now.AddSeconds(200)), "lifecycle-only transition reaches idle");
        Equal("settled", state.AttentionReason, "settled transition records targeted attention");
        IsTrue(engine.AdvanceLifecycleOnly(now.AddSeconds(207)), "expired attention is material");
        Equal(string.Empty, state.AttentionReason, "expired attention restores the normal surface");

        var recipe = new AgentAnimationRecipe(new[] { "glow", "flow" }, "#FF7C3AED", 0.8, 420, 3, 34, 1.03, "right-to-left");
        IsTrue(engine.AcceptAgentNotice(state.Number, "Synthetic notice", 12, recipe, now.AddSeconds(208)), "agent notice accepted");
        Equal(recipe, state.AgentNoticeRecipe, "expressive recipe retained by platform-neutral state");
    });
}

void TestSessionSources()
{
    var desktopIdentity = SessionIdentityReader.ParseLine("""{"type":"session_meta","payload":{"id":"desktop","cwd":"C:\\Synthetic\\desktop","originator":"Codex Desktop","source":"vscode","model_provider":"openai"}}""");
    Equal("desktop", desktopIdentity.ClientSurface, "desktop source classification");
    Equal("openai", desktopIdentity.ModelProvider, "desktop provider classification");
    var vsCodeIdentity = SessionIdentityReader.ParseLine("""{"type":"session_meta","payload":{"id":"vscode","cwd":"C:\\Synthetic\\vscode","originator":"codex_vscode","source":"vscode","model_provider":"openai"}}""");
    Equal("vscode", vsCodeIdentity.ClientSurface, "VS Code source classification");
    Equal("openai", vsCodeIdentity.ModelProvider, "VS Code provider classification");
    var cliIdentity = SessionIdentityReader.ParseLine("""{"type":"session_meta","payload":{"id":"cli","cwd":"C:\\Synthetic\\cli","originator":"codex-tui","source":"cli","model_provider":"openai"}}""");
    Equal("cli", cliIdentity.ClientSurface, "CLI source classification");
    Equal("openai", cliIdentity.ModelProvider, "CLI OpenAI provider classification");
    var deepSeekIdentity = SessionIdentityReader.ParseLine("""{"type":"session_meta","payload":{"id":"deepseek","cwd":"C:\\Synthetic\\deepseek","originator":"codex-tui","source":"cli","model_provider":"deepseek"}}""");
    Equal("cli", deepSeekIdentity.ClientSurface, "DeepSeek remains a CLI task");
    Equal("deepseek", deepSeekIdentity.ModelProvider, "DeepSeek provider classification");
    var internalIdentity = SessionIdentityReader.ParseLine("""{"type":"session_meta","payload":{"id":"internal","source":{"subagent":{"kind":"review"}},"originator":"codex-tui","model_provider":"deepseek"}}""");
    IsTrue(internalIdentity.IsInternalSession, "CLI subagent remains hidden from the task UI");

    WithTemporaryDirectory(root =>
    {
        var defaultRoot = Path.Combine(root, ".codex");
        var deepSeekRoot = Path.Combine(root, ".codex-deepseek");
        var defaultSessions = Path.Combine(defaultRoot, "sessions", "2026", "08", "10");
        var deepSeekSessions = Path.Combine(deepSeekRoot, "sessions", "2026", "08", "10");
        Directory.CreateDirectory(defaultSessions);
        Directory.CreateDirectory(deepSeekSessions);
        var now = DateTimeOffset.Parse("2026-08-10T08:00:00Z");

        static string Session(string id, string workspace, string originator, string source, string provider, string model) => string.Join('\n', new[]
        {
            $"{{\"timestamp\":\"2026-08-10T07:59:00Z\",\"type\":\"session_meta\",\"payload\":{{\"id\":\"{id}\",\"cwd\":\"C:\\\\Synthetic\\\\{workspace}\",\"originator\":\"{originator}\",\"source\":\"{source}\",\"model_provider\":\"{provider}\"}}}}",
            $"{{\"timestamp\":\"2026-08-10T07:59:01Z\",\"type\":\"turn_context\",\"payload\":{{\"cwd\":\"C:\\\\Synthetic\\\\{workspace}\",\"model\":\"{model}\"}}}}",
            "{\"timestamp\":\"2026-08-10T07:59:02Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{\"input_tokens\":100,\"cached_input_tokens\":60,\"output_tokens\":20,\"total_tokens\":120},\"total_token_usage\":{\"total_tokens\":120},\"model_context_window\":1000}}}"
        }) + '\n';

        var desktopPath = Path.Combine(defaultSessions, "desktop.jsonl");
        var vsCodePath = Path.Combine(defaultSessions, "vscode.jsonl");
        var cliPath = Path.Combine(defaultSessions, "cli.jsonl");
        var subagentPath = Path.Combine(defaultSessions, "subagent.jsonl");
        var deepSeekPath = Path.Combine(deepSeekSessions, "deepseek.jsonl");
        File.WriteAllText(desktopPath, Session("desktop-1", "desktop-project", "Codex Desktop", "vscode", "openai", "gpt-future"), new UTF8Encoding(false));
        File.WriteAllText(vsCodePath, Session("vscode-1", "vscode-project", "codex_vscode", "vscode", "openai", "gpt-future"), new UTF8Encoding(false));
        File.WriteAllText(cliPath, Session("cli-1", "cli-project", "codex-tui", "cli", "openai", "gpt-next"), new UTF8Encoding(false));
        File.WriteAllText(subagentPath, string.Join('\n', new[]
        {
            "{\"timestamp\":\"2026-08-10T07:59:00Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\"subagent-1\",\"cwd\":\"C:\\\\Synthetic\\\\agent-project\",\"originator\":\"codex-tui\",\"source\":{\"subagent\":{\"kind\":\"review\"}},\"model_provider\":\"openai\"}}",
            "{\"timestamp\":\"2026-08-10T07:59:01Z\",\"type\":\"turn_context\",\"payload\":{\"cwd\":\"C:\\\\Synthetic\\\\agent-project\",\"model\":\"gpt-agent\"}}",
            "{\"timestamp\":\"2026-08-10T07:59:02Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{\"input_tokens\":200,\"cached_input_tokens\":180,\"output_tokens\":30,\"total_tokens\":230},\"total_token_usage\":{\"input_tokens\":200,\"cached_input_tokens\":180,\"output_tokens\":30,\"total_tokens\":230},\"model_context_window\":1000}}}"
        }) + '\n', new UTF8Encoding(false));
        File.WriteAllText(deepSeekPath, Session("deepseek-1", "deepseek-project", "codex-tui", "cli", "deepseek", "deepseek-next"), new UTF8Encoding(false));
        foreach (var path in new[] { desktopPath, vsCodePath, cliPath, subagentPath, deepSeekPath }) File.SetLastWriteTimeUtc(path, now.UtcDateTime);

        var profiles = new[]
        {
            new SessionProfile("codex", "Codex", Path.Combine(defaultRoot, "sessions"), Path.Combine(defaultRoot, "session_index.jsonl"), "unknown", string.Empty),
            new SessionProfile("deepseek", "DeepSeek", Path.Combine(deepSeekRoot, "sessions"), Path.Combine(deepSeekRoot, "session_index.jsonl"), "cli", "deepseek")
        };
        var options = new HudRuntimeOptions { ActiveWindowMinutes = 60, DesktopSessionsEnabled = true, VsCodeSessionsEnabled = true, DefaultCliSessionsEnabled = true, DeepSeekCliSessionsEnabled = true };
        var engine = new SessionMonitorEngine(profiles, options);
        IsTrue(engine.RefreshActiveSessions(now), "multi-profile discovery");
        var states = engine.GetVisibleStates(now);
        Equal(4, states.Count, "desktop, VS Code, OpenAI CLI and DeepSeek CLI are all visible");
        Equal(5, engine.GetUsageStates().Count, "usage scope includes the hidden subagent");
        var subagent = engine.GetUsageStates().Single(state => state.SessionId == "subagent-1");
        IsTrue(subagent.IsInternalSession, "subagent usage state stays hidden");
        Equal(1, subagent.CallHistory.Count, "subagent token_count contributes real call history");
        Equal(230L, subagent.Snapshot?.TaskTotal, "subagent cumulative tokens are parsed");
        Equal(4, states.Select(static state => state.Number).Distinct().Count(), "task numbering is global across profiles");
        Equal("desktop", states.Single(state => state.SessionId == "desktop-1").ClientSurface, "desktop state source");
        Equal("vscode", states.Single(state => state.SessionId == "vscode-1").ClientSurface, "VS Code state source");
        Equal("openai", states.Single(state => state.SessionId == "cli-1").ModelProvider, "default CLI provider");
        Equal("deepseek", states.Single(state => state.SessionId == "deepseek-1").ModelProvider, "DeepSeek profile provider");

        engine.UpdateOptions(options with { DefaultCliSessionsEnabled = false });
        Equal(3, engine.GetVisibleStates(now).Count, "default CLI filter does not hide desktop, VS Code, or DeepSeek");
        engine.UpdateOptions(options with { DeepSeekCliSessionsEnabled = false });
        Equal(3, engine.GetVisibleStates(now).Count, "DeepSeek filter does not hide default-profile tasks");
        engine.UpdateOptions(options with { DesktopSessionsEnabled = false, VsCodeSessionsEnabled = false, DefaultCliSessionsEnabled = true, DeepSeekCliSessionsEnabled = false });
        var cliOnly = engine.GetVisibleStates(now);
        Equal(1, cliOnly.Count, "source filters isolate default CLI");
        Equal("cli-1", cliOnly.Single().SessionId, "correct default CLI task remains");
    });
}

void TestLockedSessionRecovery()
{
    WithTemporaryDirectory(root =>
    {
        const string sessionId = "00000000-0000-4000-8000-000000000001";
        const string unknownId = "00000000-0000-4000-8000-000000000002";
        const string activeId = "00000000-0000-4000-8000-000000000003";
        var profile = Path.Combine(root, ".codex");
        var sessionsRoot = Path.Combine(profile, "sessions");
        var sessions = Path.Combine(sessionsRoot, "2026", "08", "10");
        Directory.CreateDirectory(sessions);
        var indexPath = Path.Combine(profile, "session_index.jsonl");
        var sessionPath = Path.Combine(sessions, $"rollout-2026-08-10T08-00-00-{sessionId}.jsonl");
        var unknownPath = Path.Combine(sessions, $"rollout-2026-08-10T07-59-00-{unknownId}.jsonl");
        var activePath = Path.Combine(sessions, $"rollout-2026-08-10T09-29-00-{activeId}.jsonl");
        var now = DateTimeOffset.Parse("2026-08-10T09:30:00Z");

        static string Session(string id, string workspace) => string.Join('\n', new[]
        {
            $"{{\"timestamp\":\"2026-08-10T08:00:00Z\",\"type\":\"session_meta\",\"payload\":{{\"id\":\"{id}\",\"cwd\":\"C:\\\\Synthetic\\\\{workspace}\",\"originator\":\"Codex Desktop\",\"source\":\"vscode\",\"model_provider\":\"openai\"}}}}",
            $"{{\"timestamp\":\"2026-08-10T08:00:01Z\",\"type\":\"turn_context\",\"payload\":{{\"cwd\":\"C:\\\\Synthetic\\\\{workspace}\",\"model\":\"gpt-future\"}}}}",
            "{\"timestamp\":\"2026-08-10T08:00:02Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-1\"}}",
            "{\"timestamp\":\"2026-08-10T08:00:03Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{\"input_tokens\":100,\"cached_input_tokens\":60,\"output_tokens\":20,\"total_tokens\":120},\"total_token_usage\":{\"total_tokens\":1000},\"model_context_window\":1000}}}"
        }) + '\n';

        File.WriteAllText(sessionPath, Session(sessionId, "locked-project"), new UTF8Encoding(false));
        File.WriteAllText(unknownPath, Session(unknownId, "unindexed-project"), new UTF8Encoding(false));
        File.WriteAllText(activePath, Session(activeId, "foreground-project"), new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(sessionPath, now.UtcDateTime.AddMinutes(-90));
        File.SetLastWriteTimeUtc(unknownPath, now.UtcDateTime.AddMinutes(-91));
        File.SetLastWriteTimeUtc(activePath, now.UtcDateTime);
        File.WriteAllText(
            indexPath,
            $"{{\"id\":\"{sessionId}\",\"thread_name\":\"Long running indexed task\",\"updated_at\":\"2026-08-10T08:00:00Z\"}}\n" +
            $"{{\"id\":\"{activeId}\",\"thread_name\":\"Foreground active task\",\"updated_at\":\"2026-08-10T09:29:00Z\"}}\n",
            new UTF8Encoding(false));

        var engine = new SessionMonitorEngine(
            sessionsRoot,
            indexPath,
            new HudRuntimeOptions { ActiveWindowMinutes = 30, MaximumFiles = 64 });

        using (var locked = new FileStream(sessionPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var unknownLocked = new FileStream(unknownPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var discovery = SessionDiscovery.GetActiveFiles(sessionsRoot, 30, 64, now.UtcDateTime);
            IsTrue(discovery.Any(file => file.FullName == sessionPath && file.ReadBlocked), "old sharing-blocked file bypasses write window");
            IsTrue(engine.RefreshActiveSessions(now), "locked session engine discovery");
            var visible = engine.GetVisibleStates(now);
            Equal(2, visible.Count, "readable foreground and locked background tasks coexist");
            IsTrue(visible.Any(state => state.SessionId == activeId && !state.IdentityProvisional), "normal active task remains visible");
            IsTrue(visible.All(state => state.SessionId != unknownId), "unindexed locked file stays behind the privacy boundary");
            var provisional = visible.Single(state => state.SessionId == sessionId);
            Equal(sessionId, provisional.SessionId, "rollout filename supplies provisional session id");
            Equal("Long running indexed task", provisional.ConversationLabel, "official index supplies provisional title");
            IsTrue(provisional.IdentityProvisional, "locked identity remains explicitly provisional");
            IsTrue(provisional.IsReadBlocked, "sharing lock is retained in state");
            Equal<HudSnapshot?>(null, provisional.Snapshot, "locked task does not fabricate usage");
            Equal("listening", engine.GetStatus(provisional, paused: false, now), "locked task renders as listening");
        }

        IsTrue(engine.RefreshActiveSessions(now.AddSeconds(1)), "unlock refresh resolves provisional task");
        var resolved = engine.GetVisibleStates(now.AddSeconds(1)).Single(state => state.SessionId == sessionId);
        IsTrue(!resolved.IdentityProvisional, "real session metadata replaces provisional identity");
        IsTrue(!resolved.IsReadBlocked, "unlock clears sharing-block state");
        Equal("desktop", resolved.ClientSurface, "resolved task restores desktop source");
        Equal("locked-project", resolved.Workspace, "resolved task restores workspace");
        Equal(1000L, resolved.Snapshot?.TaskTotal, "bounded tail hydration restores usage");
    });
}

void TestStateDatabaseHeartbeat()
{
    WithTemporaryDirectory(root =>
    {
        var now = DateTimeOffset.UtcNow;
        var codexRoot = Path.Combine(root, ".codex");
        var sessionsRoot = Path.Combine(codexRoot, "sessions");
        var dateRoot = Path.Combine(sessionsRoot, "2026", "08", "10");
        Directory.CreateDirectory(dateRoot);
        var indexPath = Path.Combine(codexRoot, "session_index.jsonl");
        var activeId = "00000000-0000-4000-8000-000000000004";
        var internalId = "00000000-0000-4000-8000-000000000005";
        var staleId = "00000000-0000-4000-8000-000000000006";
        var activePath = Path.Combine(dateRoot, $"rollout-2026-08-10T04-08-21-{activeId}.jsonl");
        var internalPath = Path.Combine(dateRoot, $"rollout-2026-08-10T04-28-29-{internalId}.jsonl");
        var stalePath = Path.Combine(dateRoot, $"rollout-2026-08-10T03-12-33-{staleId}.jsonl");
        var encoding = new UTF8Encoding(false);

        File.WriteAllText(activePath,
            $"{{\"timestamp\":\"{now.AddHours(-10):O}\",\"type\":\"session_meta\",\"payload\":{{\"id\":\"{activeId}\",\"cwd\":\"C:\\\\Synthetic\\\\heartbeat-project\",\"originator\":\"Codex Desktop\",\"source\":\"vscode\",\"model_provider\":\"openai\"}}}}\n" +
            $"{{\"timestamp\":\"{now.AddHours(-10):O}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"last_token_usage\":{{\"input_tokens\":100,\"cached_input_tokens\":80,\"output_tokens\":5,\"total_tokens\":105}},\"total_token_usage\":{{\"input_tokens\":1000,\"cached_input_tokens\":800,\"output_tokens\":50,\"total_tokens\":1050}},\"model_context_window\":200000}}}}}}\n" +
            $"{{\"timestamp\":\"{now.AddHours(-9):O}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_complete\",\"turn_id\":\"old-turn\",\"last_agent_message\":\"done\"}}}}\n",
            encoding);
        File.WriteAllText(internalPath, "{}\n", encoding);
        File.WriteAllText(stalePath, "{}\n", encoding);
        File.SetLastWriteTimeUtc(activePath, now.AddHours(-9).UtcDateTime);
        File.SetLastWriteTimeUtc(internalPath, now.AddMinutes(-1).UtcDateTime);
        File.SetLastWriteTimeUtc(stalePath, now.AddHours(-8).UtcDateTime);
        File.WriteAllText(indexPath,
            $"{{\"id\":\"{activeId}\",\"thread_name\":\"Background parent task\"}}\n" +
            $"{{\"id\":\"{staleId}\",\"thread_name\":\"Stale task\"}}\n",
            encoding);

        var activitySource = new SyntheticSessionActivitySource(new[]
        {
            new SessionActivity(activeId, @"\\?\" + activePath, now.AddSeconds(-20)),
            new SessionActivity(internalId, internalPath, now.AddSeconds(-10)),
            new SessionActivity(staleId, stalePath, now.AddHours(-8))
        });

        var profile = new SessionProfile(
            SessionProfile.DefaultId,
            "Codex",
            sessionsRoot,
            indexPath,
            "unknown",
            string.Empty);
        var engine = new SessionMonitorEngine(
            new[] { profile },
            new HudRuntimeOptions { ActiveWindowMinutes = 30, MaximumFiles = 64 },
            activitySource: activitySource);
        IsTrue(engine.RefreshActiveSessions(now), "database heartbeat discovers old parent rollout");
        var visible = engine.GetVisibleStates(now);
        Equal(1, visible.Count, "guardian and stale rows stay hidden");
        Equal(activeId, visible[0].SessionId, "old parent remains visible");
        Equal("active", engine.GetStatus(visible[0], paused: false, now), "runtime heartbeat overrides stale completed tail");
        Equal(string.Empty, visible[0].TerminalStatus, "stale terminal state is cleared without fabricating completion");

        var fastEngine = new SessionMonitorEngine(
            new[] { profile },
            new HudRuntimeOptions { ActiveWindowMinutes = 30, MaximumFiles = 64 },
            activitySource: activitySource);
        IsTrue(fastEngine.RefreshRuntimeSessions(now), "fast runtime reconciliation discovers a missing active task");
        Equal(activeId, fastEngine.GetVisibleStates(now).Single().SessionId, "fast runtime discovery preserves identity");

        IsTrue(!engine.RefreshActiveSessions(now.AddMinutes(4)), "aged runtime activity remains discoverable inside the configured window");
        Equal(1, engine.GetVisibleStates(now.AddMinutes(4)).Count, "long-running resumed task is not dropped after the three-minute active-color window");
        Equal("idle", engine.GetStatus(engine.GetVisibleStates(now.AddMinutes(4)).Single(), paused: false, now.AddMinutes(4)), "aged runtime activity is retained without staying falsely active");

        var agedFastEngine = new SessionMonitorEngine(
            new[] { profile },
            new HudRuntimeOptions { ActiveWindowMinutes = 30, MaximumFiles = 64 },
            activitySource: activitySource);
        IsTrue(agedFastEngine.RefreshRuntimeSessions(now.AddMinutes(4)), "fast runtime reconciliation discovers a resumed task older than three minutes");
        Equal(activeId, agedFastEngine.GetVisibleStates(now.AddMinutes(4)).Single().SessionId, "aged fast discovery preserves the resumed task");

        IsTrue(engine.RefreshActiveSessions(now.AddMinutes(31)), "expired discovery window removes old parent");
        Equal(0, engine.GetVisibleStates(now.AddMinutes(31)).Count, "expired discovery activity does not leave a ghost task");
    });
}

void TestFormatting()
{
    Equal("999,999", HudFormatting.FormatNumber(999_999, "auto"), "auto exact threshold");
    Equal("1M", HudFormatting.FormatNumber(1_000_000, "auto"), "compact million");
    Equal("~$1.15", HudFormatting.FormatCost(1.15), "cost format");
    Equal("60%", HudFormatting.FormatCacheHitRate(100, 60), "cache hit rate");
    Equal("99.99%", HudFormatting.FormatCacheHitRate(1_000_000, 999_999), "near-perfect cache hit does not round to 100 percent");
    Equal("99.99%", HudFormatting.FormatPercent(99.9999), "near-full context does not round to 100 percent");
    Equal("--", HudFormatting.FormatCacheHitRate(0, 0), "cache hit rate without input");
    Equal("100%", HudFormatting.FormatCacheHitRate(100, 120), "cache hit rate clamps malformed cached input");
    Equal("#FF34C759", HudFormatting.GetCacheHitColor(98), "98 percent stays green");
    Equal("#FFFFCC00", HudFormatting.GetCacheHitColor(97.9), "below 98 percent is yellow");
    Equal("#FFFFCC00", HudFormatting.GetCacheHitColor(95), "95 percent stays yellow");
    Equal("#FFFF453A", HudFormatting.GetCacheHitColor(94.9), "below 95 percent is red");
    Equal("#FFFF453A", HudFormatting.GetCacheHitColor(90), "90 percent stays red");
    Equal("#FF8B0000", HudFormatting.GetCacheHitColor(89.9), "below 90 percent is dark red");
    var futureMetrics = HudFormatting.GetMetrics(
        new HudSnapshot { Input = 200, Cached = 150, Model = "gpt-9.9-nebula" },
        new Dictionary<string, bool> { ["model"] = true, ["cacheHitRate"] = true },
        new Dictionary<string, string> { ["model"] = "Model", ["cacheHitRate"] = "Cache hit rate" },
        "exact");
    Equal("gpt-9.9-nebula", futureMetrics.Single(metric => metric.Key == "model").Value, "unknown future model remains monitorable");
    Equal("75%", futureMetrics.Single(metric => metric.Key == "cacheHitRate").Value, "unknown future model keeps generic token metrics");
    var metrics = HudFormatting.GetMetrics(
        new HudSnapshot { FiveHourRemainingPercent = 86, WeeklyRemainingPercent = 82 },
        new Dictionary<string, bool> { ["fiveHourRemaining"] = true },
        new Dictionary<string, string> { ["fiveHourRemaining"] = "5-hour remaining" },
        "exact");
    Equal("86%", metrics.Single().Value, "five-hour allowance formatting");
    var hierarchySnapshot = new HudSnapshot
    {
        Input = 10_000,
        Cached = 9_500,
        Uncached = 500,
        Output = 250,
        CallTotal = 10_250,
        TaskTotal = 50_000,
        ContextPercent = 25,
        ContextWindow = 40_000,
        Model = "deepseek-chat"
    };
    var labels = new Dictionary<string, string>();
    var compact = HudFormatting.GetTaskListMetrics(hierarchySnapshot, "compact", labels, "exact");
    Equal("context,model,cacheHitRate", string.Join(',', compact.Primary.Select(static metric => metric.Key)), "compact tier keeps context, model, and cache efficiency");
    var balanced = HudFormatting.GetTaskListMetrics(hierarchySnapshot, "balanced", labels, "exact");
    Equal("context,model,cacheHitRate,callTotal", string.Join(',', balanced.Primary.Select(static metric => metric.Key)), "balanced tier adds the current call total");
    var detailed = HudFormatting.GetTaskListMetrics(hierarchySnapshot, "detailed", labels, "exact");
    IsTrue(detailed.Diagnostics.Any(static metric => metric.Key == "contextWindow"), "detailed tier exposes provider-specific context capacity");
    IsTrue(detailed.Diagnostics.Any(static metric => metric.Key == "taskTotal"), "detailed tier keeps cumulative task accounting off the primary row");
    var allFields = new Dictionary<string, bool>
    {
        ["input"] = true,
        ["callTotal"] = true,
        ["activeTasks"] = true,
        ["cacheHitRate"] = true,
        ["taskTotal"] = true,
        ["context"] = true,
        ["model"] = true,
        ["updated"] = true
    };
    var summary = HudFormatting.GetSummaryMetrics(hierarchySnapshot, allFields, labels, "exact");
    IsTrue(summary.Any(static metric => metric.Key == "cacheHitRate"), "summary leads with cache efficiency");
    IsTrue(summary.Any(static metric => metric.Key == "context"), "summary exposes current context");
    IsTrue(summary.All(static metric => metric.Key is not ("taskTotal" or "model" or "updated")), "summary excludes cumulative and identity-only metrics");
    Equal(3, HudFormatting.GetContextAlertLevel(98, new[] { 75d, 90d, 98d }), "context level");
    Equal("75,90,98", string.Join(',', HudFormatting.ParseContextAlertThresholds(new[] { "98", "75", "90" })!), "threshold normalization");
    NotNull(HudFormatting.GetTaskDeepLink("00000000-0000-4000-8000-000000000007"), "safe deep link");
    Equal<string?>(null, HudFormatting.GetTaskDeepLink("../unsafe"), "unsafe deep link rejected");
}

void TestPlacement()
{
    var topRight = HudPlacement.GetPreset("top-right", 0, 0, 1920, 1040, 700, 80, 18);
    Equal(1238d, topRight.Left, "top-right window offsets transparent chrome");
    Equal(-18d, topRight.Top, "top edge offsets transparent chrome");
    Equal(1920d, topRight.Left + 700 - 18, "visible shell touches right edge");
    Equal(0d, topRight.Top + 18, "visible shell touches top edge");
    var custom = HudPlacement.ClampCustom(-1000, 5000, 0, 0, 1920, 1040, 700, 80, 18);
    Equal(-18d, custom.Left, "custom position clamps to visible left edge");
    Equal(978d, custom.Top, "custom position clamps to visible bottom edge");
    var unsnapped = HudPlacement.ClampCustom(12 - 18, 15 - 18, 0, 0, 1920, 1040, 700, 80, 18);
    Equal(-6d, unsnapped.Left, "near-left custom placement is not snapped");
    Equal(-3d, unsnapped.Top, "near-top custom placement is not snapped");
}

void TestSurfaceEffects()
{
    var dark = SurfaceEffects.Create("#EE111827", "#FFF8FAFC");
    var light = SurfaceEffects.Create("#F4F8FAFC", "#FF172033");
    Equal("dark", dark.Tone, "dark classification");
    Equal("light", light.Tone, "light classification");
    IsTrue(dark.PeakOpacity > light.PeakOpacity, "dark peak compensation");
    IsTrue(dark.Blur > light.Blur, "dark blur compensation");
}

void TestConfiguration()
{
    WithTemporaryDirectory(root =>
    {
        var pluginRoot = Path.Combine(root, "plugin");
        var localRoot = Path.Combine(root, "local");
        var home = Path.Combine(root, "home");
        Directory.CreateDirectory(pluginRoot);
        Directory.CreateDirectory(Path.Combine(pluginRoot, "locales"));
        File.Copy(Path.Combine(repositoryRoot, "config.default.json"), Path.Combine(pluginRoot, "config.default.json"));
        File.Copy(Path.Combine(repositoryRoot, "locales", "en.json"), Path.Combine(pluginRoot, "locales", "en.json"));
        var paths = HudPaths.Create(pluginRoot, localRoot, home);
        Directory.CreateDirectory(paths.StateRoot);
        File.WriteAllText(paths.ConfigPath, """{"multiTask":"corrupt","behavior":{"contextAlerts":"corrupt"},"opacity":-0.01,"scale":2.0,"alwaysOnTop":"yes","fontSize":"large","fields":{"context":"yes"},"statusColors":{"active":17}}""");
        var config = HudConfigStore.Load(paths);
        Equal("summary", config["multiTask"]!["displayMode"]!.GetValue<string>(), "object/scalar corruption recovery");
        Equal(0d, config["opacity"]!.GetValue<double>(), "opacity clamp");
        Equal(1d, config["scale"]!.GetValue<double>(), "HUD scale clamp");
        var settings = HudSettings.From(config);
        Equal("summary", settings.MultiTask.DisplayMode, "typed settings projection");
        Equal(true, settings.SessionSources.VsCode, "missing VS Code source setting defaults to enabled");
        Equal("always", settings.MultiTask.NameMode, "conversation subtitle is visible by default");
        Equal(0d, settings.Opacity, "typed numeric settings projection");
        Equal(1d, settings.Scale, "typed HUD scale projection");
        Equal(true, settings.AlwaysOnTop, "wrong scalar type retains default boolean");
        Equal(14d, settings.FontSize, "wrong scalar type retains default number");
        Equal(true, settings.Fields["context"], "wrong nested scalar type retains cache-HUD default");
        Equal("#FF34C759", settings.StatusColors["active"], "wrong dictionary scalar type retains default");
        ((JsonObject)config["agentNotifications"]!)["enabled"] = true;
        ((JsonObject)config["agentNotifications"]!)["permission"] = "expressive";
        settings = HudSettings.From(config);
        Equal(true, settings.AgentNotifications.Enabled, "typed agent-notification boolean projection");
        Equal("expressive", settings.AgentNotifications.Permission, "typed agent-notification permission projection");
        HudConfigStore.Save(paths, config);
        NotNull(JsonNode.Parse(File.ReadAllText(paths.ConfigPath)), "saved config JSON");
        var reloaded = HudSettings.From(HudConfigStore.Load(paths));
        Equal(true, reloaded.AgentNotifications.Enabled, "saved agent-notification boolean survives config merge");
        Equal("expressive", reloaded.AgentNotifications.Permission, "saved agent-notification permission survives config merge");
        config["scale"] = 0.1;
        config["transparencyMode"] = "background";
        HudConfigStore.Normalize(config, paths.LocaleRoot);
        Equal(0.2d, config["scale"]!.GetValue<double>(), "HUD supports a twenty-percent minimum scale");
        Equal("background", config["transparencyMode"]!.GetValue<string>(), "background-only opacity mode is preserved");
        Equal(0, Directory.EnumerateFiles(paths.StateRoot, "settings.json.*.tmp").Count(), "atomic config save leaves no temporary file");
    });
}

void TestMacPortability()
{
    WithTemporaryDirectory(root =>
    {
        var pluginRoot = Path.Combine(root, "CodexMonitorHUD.app", "Contents", "MacOS");
        var syntheticHome = Path.Combine(root, "Users", "synthetic-user");
        Directory.CreateDirectory(pluginRoot);
        Directory.CreateDirectory(syntheticHome);
        var paths = HudPaths.CreateForPlatform(pluginRoot, HudPlatform.MacOS, syntheticHome);
        Equal(
            Path.Combine(syntheticHome, "Library", "Application Support", "CodexMonitorHUD"),
            paths.StateRoot,
            "macOS state root");
        Equal(Path.Combine(syntheticHome, ".codex", "sessions"), paths.SessionsRoot, "macOS sessions root");

        var unixContext = HudRecordParser.Parse(
            """{"type":"turn_context","payload":{"cwd":"/Users/synthetic-user/项目/portable-workspace","model":"gpt-test"}}""");
        Equal("portable-workspace", unixContext?.Workspace, "Unix workspace leaf");

        var codexRoot = Path.Combine(syntheticHome, ".codex");
        var missingSessionsRoot = Path.Combine(codexRoot, "sessions");
        Directory.CreateDirectory(codexRoot);
        using var tracker = new SessionChangeTracker(
            missingSessionsRoot,
            pathComparer: StringComparer.Ordinal);
        _ = tracker.ConsumeDirty();
        _ = tracker.ConsumeStructural();
        var dateRoot = Path.Combine(missingSessionsRoot, "2026", "07", "19");
        Directory.CreateDirectory(dateRoot);
        var firstPath = Path.Combine(dateRoot, "CaseSensitive.jsonl");
        var secondPath = Path.Combine(dateRoot, "casesensitive.jsonl");
        File.WriteAllText(firstPath, "{}\n", new UTF8Encoding(false));
        File.WriteAllText(secondPath, "{}\n", new UTF8Encoding(false));
        SpinWait.SpinUntil(() => tracker.ConsumeDirty(), TimeSpan.FromSeconds(3));
        var changed = tracker.DrainChangedPaths();
        IsTrue(changed.Count >= 1, "watcher observes nested creation below a missing sessions root");
        Equal(2, new HashSet<string>(new[] { firstPath, secondPath }, StringComparer.Ordinal).Count, "case-sensitive path identity");

        var indexPath = Path.Combine(codexRoot, "session_index.jsonl");
        File.WriteAllText(indexPath, "{\"id\":\"mac-one\",\"thread_name\":\"初始标题\"}\n", new UTF8Encoding(false));
        var index = new SessionTitleIndex();
        IsTrue(index.Refresh(indexPath), "macOS UTF-8 title index loads");
        File.Move(indexPath, indexPath + ".old");
        File.WriteAllText(indexPath, "{\"id\":\"mac-two\",\"thread_name\":\"替换标题\"}\n", new UTF8Encoding(false));
        IsTrue(index.Refresh(indexPath), "atomic title-index replacement is reconciled");
        Equal(string.Empty, index.GetTitle("mac-one"), "replacement drops stale title");
        Equal("替换标题", index.GetTitle("mac-two"), "replacement keeps UTF-8 title");
    });
}

void TestPricing()
{
    var catalog = PricingCatalog.Load(repositoryRoot);
    IsTrue(catalog.Loaded, "built-in pricing loaded");
    var snapshot = NewSnapshot(100, 50, 20, 1_100_000, "gpt-5.6-luna", DateTimeOffset.Now) with
    {
        TaskInput = 1_000_000,
        TaskCached = 500_000,
        TaskOutput = 100_000
    };
    var estimate = catalog.Estimate(snapshot);
    NotNull(estimate, "known model estimate");
    Equal(1.15d, Math.Round(estimate!.CostUsd, 6), "current cached-input pricing");
    var datedEstimate = catalog.Estimate(snapshot with { Model = "gpt-5.6-luna-2026-08-10" });
    NotNull(datedEstimate, "dated model snapshot estimate");
    Equal("gpt-5.6-luna", datedEstimate!.PricedAs, "dated snapshot resolves to catalog model");
    Equal(1.15d, Math.Round(datedEstimate.CostUsd, 6), "dated snapshot inherits matching catalog price");
    Equal<CostEstimate?>(null, catalog.Estimate(snapshot with { Model = "not-priced" }), "unknown model is not guessed");
}

HudSnapshot NewSnapshot(long input, long cached, long output, long taskTotal, string model, DateTimeOffset timestamp) => new()
{
    Timestamp = timestamp,
    Input = input,
    Cached = cached,
    Uncached = input - cached,
    Output = output,
    CallTotal = input + output,
    TaskInput = input,
    TaskCached = cached,
    TaskUncached = input - cached,
    TaskOutput = output,
    TaskTotal = taskTotal,
    ContextWindow = 200_000,
    ContextPercent = input * 100.0 / 200_000,
    Model = model
};

void WithTemporaryDirectory(Action<string> action)
{
    var root = Path.Combine(Path.GetTempPath(), "CodexMonitorHud.Core.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        action(root);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

string FindRepositoryRoot()
{
    var current = new DirectoryInfo(Environment.CurrentDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "config.default.json")))
        {
            return current.FullName;
        }
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Repository root was not found.");
}

void IsTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + message);
    }
}

void NotNull<T>(T? value, string message)
{
    if (value is null)
    {
        throw new InvalidOperationException("Assertion failed: " + message);
    }
}

void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Assertion failed: {message}. Expected '{expected}', actual '{actual}'.");
    }
}

sealed class SyntheticSessionActivitySource(IEnumerable<SessionActivity> activities) : ISessionActivitySource
{
    private readonly IReadOnlyList<SessionActivity> _activities = activities.ToArray();

    public IReadOnlyList<SessionActivity> GetRecentUserSessions(
        SessionProfile profile,
        DateTimeOffset cutoff,
        int maximumRows) => _activities
            .Where(activity => activity.UpdatedAt >= cutoff)
            .Take(Math.Max(1, maximumRows))
            .ToArray();
}
