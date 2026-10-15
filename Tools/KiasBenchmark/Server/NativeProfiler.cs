using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Robust.Shared.GameObjects;

namespace Content.Server.Benchmark;

public static partial class NativeLab
{
    public static readonly bool DisableCrewTypeFilter = Environment.GetEnvironmentVariable("NATIVE_LAB_ABLATION") == "CrewFilterOff";
    public static readonly bool DisableCoverageReuse = Environment.GetEnvironmentVariable("NATIVE_LAB_ABLATION") == "CoveragePassOff";
    public static readonly bool DisableRuntimeIdleFastPath = Environment.GetEnvironmentVariable("NATIVE_LAB_ABLATION") == "RuntimeIdleOff";
    private const int ProfilePhaseBase = 1024, ProfileDriver = 1088, ProfileObserver = 1089, ProfileSlots = 1090;
    private struct ProfileValue { public long Calls, Inclusive, Exclusive, Bytes; }
    private static readonly Dictionary<string, int> ProfileSystems = new();
    private static readonly string[] ProfileSystemNames = new string[ProfilePhaseBase];
    public static string[] ProfilePhaseNames = Array.Empty<string>();
    public static Func<object>? ProfilePhaseTotals;
    private static object? _profileWarmPhases;
    private static ProfileValue[]? _profileValues;
    private static readonly Dictionary<string, string> ProfileJsonWrites = new();
    private static readonly List<(string Name, string Line)> ProfileProgressLines = new();
    private static bool _profileFlushStreams;
    private static int _profileTick = -1, _profileSystem = -1;
    private static long _profilePhaseTicks, _profilePhaseBytes, _profileSystemPhaseTicks, _profileSystemPhaseBytes;
    private static bool ProfileEnabled => Environment.GetEnvironmentVariable("NATIVE_LAB_PROFILE") == "1";

    private static void InitializeProfile()
    {
        if (!ProfileEnabled) return;
        if (_duration > 7200) throw new InvalidOperationException("Per-system profiling is limited to 7200 ticks; long ABBA uses bounded normal telemetry.");
        ProfileSystems.Clear(); Array.Clear(ProfileSystemNames);
        ProfileJsonWrites.Clear(); ProfileProgressLines.Clear(); _profileFlushStreams = false;
        _profileTick = -1; _profileSystem = -1;
        _profileValues = new ProfileValue[checked((int) _duration * ProfileSlots)];
        var manager = typeof(EntitySystemManager);
        (manager.GetField("DiagnosticSystemFailure", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing isolated engine failure hook.")).SetValue(null, (Action<Exception>) RecordFailure);
        if (Environment.GetEnvironmentVariable("NATIVE_LAB_SYSTEM_COUNTERS") != "0")
        {
            (manager.GetField("DiagnosticBeforeSystem", BindingFlags.Static | BindingFlags.Public)
                ?? throw new InvalidOperationException("Missing isolated engine system profiler hook.")).SetValue(null, (Action<string>) ProfileBeforeSystem);
            (manager.GetField("DiagnosticAfterSystem", BindingFlags.Static | BindingFlags.Public)
                ?? throw new InvalidOperationException("Missing isolated engine system profiler hook.")).SetValue(null, (Action<string, long, long>) ProfileAfterSystem);
        }
        AtomicJson("profile-config.json", new { enabled = true,
            phaseCounters = Environment.GetEnvironmentVariable("NATIVE_LAB_PHASE_COUNTERS") != "0",
            systemCounters = Environment.GetEnvironmentVariable("NATIVE_LAB_SYSTEM_COUNTERS") != "0",
            scope = "Inclusive and exclusive elapsed wall scopes on the server thread, including preemption and GC; nested KIAS scopes subtracted from containing systems/native drivers. Process CPU is sampled separately. Instrument overhead requires ON/OFF calibration. CSV serialization occurs after measurement.",
            stopwatchFrequency = Stopwatch.Frequency, fixedSlots = ProfileSlots });
    }

    private static void ProfileBeginTick(uint tick)
    {
        if (_profileValues == null) return;
        if (tick == 0) _profileWarmPhases = ProfilePhaseTotals?.Invoke();
        _profileTick = (int) tick; _profilePhaseTicks = 0; _profilePhaseBytes = 0; _profileSystem = -1;
    }

    private static void ProfileEndTick() => _profileTick = -1;

    private static bool DeferProfileJson(string name, object value)
    {
        if (!ProfileEnabled || _profileTick < 0) return false;
        ProfileJsonWrites[name] = JsonSerializer.Serialize(value);
        return true;
    }

    private static void AppendProgressLine(string line)
    {
        AppendLabLine("progress.jsonl", line);
    }

    private static void AppendLabLine(string name, string line)
    {
        if (ProfileEnabled && _profileTick >= 0) ProfileProgressLines.Add((name, line));
        else File.AppendAllText(Path.Combine(_output, name), line + Environment.NewLine);
    }

    private static void FlushProfileOutput()
    {
        if (!ProfileEnabled) return;
        foreach (var (name, json) in ProfileJsonWrites)
        {
            if (name == "progress.json") { _pendingProgressJson = json; continue; }
            var path = Path.Combine(_output, name);
            File.WriteAllText(path + ".tmp", json);
            File.Move(path + ".tmp", path, true);
        }
        ProfileJsonWrites.Clear();
        FlushPendingProgress(force: true);
        if (ProfileProgressLines.Count > 0)
        {
            foreach (var (name, line) in ProfileProgressLines)
                File.AppendAllText(Path.Combine(_output, name), line + Environment.NewLine);
            ProfileProgressLines.Clear();
        }
        if (_profileFlushStreams)
        {
            _events?.Flush(); _outcomes?.Flush(); _networkHistory?.Flush(); _profileFlushStreams = false;
        }
    }

    private static void ProfileBeforeSystem(string name)
    {
        if (!ProfileSystems.TryGetValue(name, out var index))
        {
            index = ProfileSystems.Count;
            if (index >= ProfilePhaseBase) throw new InvalidOperationException("Too many profiled entity systems.");
            ProfileSystems.Add(name, index); ProfileSystemNames[index] = name;
        }
        _profileSystem = index;
        _profileSystemPhaseTicks = _profilePhaseTicks;
        _profileSystemPhaseBytes = _profilePhaseBytes;
    }

    private static void ProfileAfterSystem(string name, long ticks, long bytes)
    {
        if (_profileTick >= 0 && _profileSystem >= 0)
            ProfileRecord(_profileSystem, ticks, ticks - (_profilePhaseTicks - _profileSystemPhaseTicks),
                bytes - (_profilePhaseBytes - _profileSystemPhaseBytes));
        _profileSystem = -1;
    }

    public static void ProfilePhaseObserved(int phase, long inclusive, long exclusive, long bytes, long exclusiveBytes)
    {
        if (_profileValues == null || _profileTick < 0) return;
        if (phase < 0 || phase >= ProfileDriver - ProfilePhaseBase) throw new InvalidOperationException("Too many profiled KIAS phases.");
        ProfileRecord(ProfilePhaseBase + phase, inclusive, exclusive, exclusiveBytes);
        _profilePhaseTicks += exclusive; _profilePhaseBytes += exclusiveBytes;
    }

    private static (long Ticks, long Bytes) ProfileNativeStart() => (_profilePhaseTicks, _profilePhaseBytes);

    private static void ProfileNativeEnd(bool observer, double milliseconds, long bytes, (long Ticks, long Bytes) before)
    {
        if (_profileValues == null || _profileTick < 0) return;
        var ticks = (long) Math.Round(milliseconds * Stopwatch.Frequency / 1000d);
        ProfileRecord(observer ? ProfileObserver : ProfileDriver, ticks,
            ticks - (_profilePhaseTicks - before.Ticks), bytes - (_profilePhaseBytes - before.Bytes));
    }

    private static void ProfileRecord(int slot, long inclusive, long exclusive, long bytes)
    {
        if (exclusive < 0 || bytes < 0) throw new InvalidOperationException("Negative exclusive profiler cost; overlapping scopes invalidate attribution.");
        ref var row = ref _profileValues![_profileTick * ProfileSlots + slot];
        row.Calls++; row.Inclusive += inclusive; row.Exclusive += exclusive; row.Bytes += bytes;
    }

    private static void FinishProfile()
    {
        if (_profileValues == null) return;
        using var writer = new StreamWriter(Path.Combine(_output, "system-phases.csv"));
        writer.WriteLine("relative_tick,kind,name,calls,inclusive_ms,exclusive_ms,exclusive_allocated_bytes");
        for (var tick = 0; tick < _duration; tick++)
        for (var slot = 0; slot < ProfileSlots; slot++)
        {
            var row = _profileValues[tick * ProfileSlots + slot];
            if (row.Calls == 0) continue;
            var kind = slot < ProfilePhaseBase ? "system" : slot < ProfileDriver ? "phase" : "native";
            var name = slot < ProfilePhaseBase ? ProfileSystemNames[slot]
                : slot < ProfileDriver ? ProfilePhaseNames[slot - ProfilePhaseBase]
                : slot == ProfileDriver ? "Driver" : "Observer";
            writer.WriteLine(FormattableString.Invariant($"{tick},{kind},{name},{row.Calls},{row.Inclusive * 1000d / Stopwatch.Frequency:R},{row.Exclusive * 1000d / Stopwatch.Frequency:R},{row.Bytes}"));
        }
        AtomicJson("phase-histograms.json", new { before = _profileWarmPhases, after = ProfilePhaseTotals?.Invoke(), scope = "Inclusive per-invocation duration histogram deltas; powers-of-two stopwatch buckets, separate from exclusive per-tick CSV" });
        _profileValues = null; _profileTick = -1; _profileSystem = -1;
    }
}
