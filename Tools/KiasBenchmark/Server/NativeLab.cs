using System.Diagnostics;
using System.Diagnostics.Tracing;
using EventSource = System.Diagnostics.Tracing.EventSource;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Power.EntitySystems;
using Content.Server.Administration.Logs;
using Content.Server.GameTicking;
using Content.Shared.Database;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Anomaly;
using Content.Shared.Anomaly.Components;
using Content.Shared.Radiation.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Console;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Timing;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.Benchmark;

public sealed class NativeLabRoundSystem : EntitySystem
{
    private bool _loading;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LoadingMapsEvent>(OnLoadingMaps);
    }

    private void OnLoadingMaps(LoadingMapsEvent args)
    {
        if (_loading) args.Maps.Clear();
    }

    public MapId StartRound()
    {
        var ticker = EntityManager.System<GameTicker>();
        if (ticker.RunLevel != GameRunLevel.PreRoundLobby || ticker.DummyTicker)
            throw new InvalidOperationException("Native laboratory requires a fresh, non-dummy lobby.");
        _loading = true;
        try
        {
            ticker.SetGamePreset("Sandbox");
            ticker.StartRound(true);
            if (ticker.RunLevel != GameRunLevel.InRound || ticker.RoundId <= 0)
                throw new InvalidOperationException("Native laboratory could not start a real sandbox round.");
            return ticker.DefaultMap;
        }
        finally { _loading = false; }
    }
}

public sealed class NativeLabCommand : IConsoleCommand
{
    public string Command => "native_lab_start";
    public string Description => "Start the isolated native gameplay replay laboratory.";
    public string Help => Command;
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        try { NativeLab.Start(shell); }
        catch (Exception error) { NativeLab.RecordFailure(error); throw; }
    }
}

public static partial class NativeLab
{
    public static Func<object>? Telemetry;
    public static Action<EntityUid, EntityUid>? RegisterCrew;
    public static Func<EntityUid, string, bool, object?>? KiasDriver;
    public static readonly Dictionary<EntityUid, uint> ExpectedOfflineGrids = new();
    public static bool TelemetryRetryRequired;
    private static uint _nextTelemetryCheck;
    private static uint? _unavailableSince;
    private static int _writtenSamples;
    private sealed record Input(string Id, uint Tick, string Ship, string Type, string? Prototype, float? FixtureRotationDegrees = null);
    private sealed record Ship(EntityUid Grid, EntityUid InitialActor, EntityUid PowerDevice, EntityUid Light, Vector2i FireTile)
    {
        public EntityUid Actor { get; set; } = InitialActor;
    }
    private static IEntityManager _em = default!;
    private static IGameTiming _timing = default!;
    private static readonly Dictionary<string, Ship> Ships = new();
    private static Input[] _inputs = Array.Empty<Input>();
    private static StreamWriter? _samples, _events, _networkHistory;
    private static uint _start, _warmup, _duration;
    private static int _next;
    private static long _clock, _alloc;
    private static string _output = string.Empty;
    private static readonly List<object> Bindings = new();
    private static IConsoleShell? _shell;
    private static readonly Dictionary<EntityUid, EntityUid> Radiation = new(), Anomalies = new();
    private static readonly Dictionary<EntityUid, EntityUid> Actors = new();
    private static TickSample[] _tickSamples = Array.Empty<TickSample>();
    private static LoopListener? _listener;
    private static readonly Process Process = Process.GetCurrentProcess();
    private static long _workingSet;
    private static double _cpu;
    private static Func<double>? _physicsMilliseconds;
    private static Func<long>? _physicsBytes;
    private struct TickSample
    {
        public uint EngineTick;
        public double WholeMs, ContentMs, Cpu, PauseMs, PhysicsMs, DriverMs, ObserverMs;
        public long DriverBytes, ObserverBytes;
        public long PhysicsBytes;
        public long WholeBytes, ContentBytes, ThreadBytes, WorkingSet;
        public int Gc0, Gc1, Gc2;
        public bool Captured;
    }
    private sealed class LoopListener : EventListener
    {
        private long _started, _bytes, _threadBytes;
        private TimeSpan _pause;
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Robust.GameLoop") EnableEvents(source, EventLevel.LogAlways);
        }
        protected override void OnEventWritten(EventWrittenEventArgs args)
        {
            if (_samples == null || _timing.CurTick.Value < _start + _warmup) return;
            var relative = _timing.CurTick.Value - _start - _warmup;
            if (relative >= _duration) return;
            if (args.EventId == 4)
            {
                ProfileBeginTick(relative);
                _bytes = GC.GetTotalAllocatedBytes(false);
                _threadBytes = GC.GetAllocatedBytesForCurrentThread();
                _pause = GC.GetTotalPauseDuration();
                _started = Stopwatch.GetTimestamp();
            }
            else if (args.EventId == 5 && _started != 0)
            {
                ref var sample = ref _tickSamples[relative];
                sample.WholeMs = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
                sample.WholeBytes = GC.GetTotalAllocatedBytes(false) - _bytes;
                sample.ThreadBytes = GC.GetAllocatedBytesForCurrentThread() - _threadBytes;
                sample.PauseMs = (GC.GetTotalPauseDuration() - _pause).TotalMilliseconds;
                sample.Gc0 = GC.CollectionCount(0); sample.Gc1 = GC.CollectionCount(1); sample.Gc2 = GC.CollectionCount(2);
                if (relative % 60 == 0)
                {
                    Process.Refresh(); _workingSet = Process.WorkingSet64; _cpu = Process.TotalProcessorTime.TotalSeconds;
                }
                sample.WorkingSet = _workingSet; sample.Cpu = _cpu;
                sample.EngineTick = _timing.CurTick.Value; sample.Captured = true;
                _started = 0;
                ProfileEndTick();
                try { FlushProfileOutput(); }
                catch (Exception error) { RecordFailure(error); }
            }
        }
    }

    public static void Start(IConsoleShell shell)
    {
        if (_samples != null) throw new InvalidOperationException("A laboratory replay is already running.");
        _output = Environment.GetEnvironmentVariable("NATIVE_LAB_OUTPUT") ?? throw new InvalidOperationException("NATIVE_LAB_OUTPUT missing.");
        var scenario = Environment.GetEnvironmentVariable("NATIVE_LAB_SCENARIO") ?? throw new InvalidOperationException("NATIVE_LAB_SCENARIO missing.");
        _warmup = uint.Parse(Environment.GetEnvironmentVariable("NATIVE_LAB_WARMUP") ?? "7200", CultureInfo.InvariantCulture);
        _duration = uint.Parse(Environment.GetEnvironmentVariable("NATIVE_LAB_TICKS") ?? "600", CultureInfo.InvariantCulture);
        var count = int.Parse(Environment.GetEnvironmentVariable("NATIVE_LAB_SHIPS") ?? "40", CultureInfo.InvariantCulture);
        _em = IoCManager.Resolve<IEntityManager>();
        _timing = IoCManager.Resolve<IGameTiming>();
        IoCManager.Resolve<IRobustRandom>().SetSeed(int.Parse(Environment.GetEnvironmentVariable("NATIVE_LAB_SEED") ?? "20261010", CultureInfo.InvariantCulture));
        _shell = shell;
        InitializeProfile();
        _physicsMilliseconds = typeof(Robust.Server.GameObjects.PhysicsSystem).GetProperty("DiagnosticMilliseconds")?.GetMethod?.CreateDelegate<Func<double>>();
        _physicsBytes = typeof(Robust.Server.GameObjects.PhysicsSystem).GetProperty("DiagnosticAllocatedBytes")?.GetMethod?.CreateDelegate<Func<long>>();
        if (_timing.TickRate != 60) throw new InvalidOperationException("Native laboratory requires the matched 60 TPS configuration.");
        _inputs = File.ReadLines(scenario).Select(line =>
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            float? rotation = null;
            if (root.TryGetProperty("fixtureRotationDegrees", out var angle))
            {
                if (!angle.TryGetSingle(out var degrees) || !float.IsFinite(degrees))
                    throw new InvalidOperationException("Fixture rotation must be a finite number.");
                rotation = degrees;
            }
            return new Input(root.GetProperty("id").GetString()!, root.GetProperty("tick").GetUInt32(), root.GetProperty("ship").GetString()!, root.GetProperty("type").GetString()!, root.TryGetProperty("prototype", out var prototype) ? prototype.GetString() : null, rotation);
        }).OrderBy(input => input.Tick).ThenBy(input => input.Id).ToArray();
        if (_inputs.Any(input => input.Tick >= _duration)) throw new InvalidOperationException("Replay input lies outside the measured interval.");
        Directory.CreateDirectory(_output);
        Ships.Clear();
        ResetGameDrivers();
        Bindings.Clear();
        var maps = _em.System<SharedMapSystem>();
        var mapId = _em.System<NativeLabRoundSystem>().StartRound();
        for (var i = 1; i <= count; i++)
        {
            if (!_em.System<MapLoaderSystem>().TryLoadGrid(mapId, new ResPath("/Maps/_Forge/Shuttles/Archive/Mercenary/labBriar.yml"), out var loaded)) throw new InvalidOperationException("Briar load failed.");
            var grid = loaded!.Value.Owner;
            _em.System<SharedTransformSystem>().SetWorldPosition(grid, new Vector2(i % 8 * 3000, i / 8 * 3000));
            EntityUid[] Native<T>() where T : Component
            {
                var found = new List<EntityUid>();
                var query = _em.AllEntityQueryEnumerator<T, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var transform)) if (transform.GridUid == grid) found.Add(uid);
                return found.OrderBy(uid => _em.GetComponent<TransformComponent>(uid).LocalPosition.X).ThenBy(uid => _em.GetComponent<TransformComponent>(uid).LocalPosition.Y).ToArray();
            }
            var actor = _em.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(-4.5f, .5f)));
            AttachTrackedMind(actor, $"ship-{i:0000}/crew");
            var power = Native<Content.Server.Atmos.Monitor.Components.AtmosMonitorComponent>().First(uid => _em.HasComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(uid));
            var light = Native<PoweredLightComponent>().First();
            Ships.Add($"ship-{i:0000}", new Ship(grid, actor, power, light, new Vector2i(-5, 0)));
            if (_inputs.Any(input => input.Ship == $"ship-{i:0000}" && input.Type.StartsWith("pdc.", StringComparison.Ordinal)))
                PreparePdcFixture(Ships[$"ship-{i:0000}"]);
            Bindings.Add(new { ship = $"ship-{i:0000}", gridPosition = _em.System<SharedTransformSystem>().GetWorldPosition(grid).ToString(),
                powerPrototype = _em.GetComponent<MetaDataComponent>(power).EntityPrototype?.ID,
                powerPosition = _em.GetComponent<TransformComponent>(power).LocalPosition.ToString(),
                lightPrototype = _em.GetComponent<MetaDataComponent>(light).EntityPrototype?.ID,
                lightPosition = _em.GetComponent<TransformComponent>(light).LocalPosition.ToString(), actorPosition = "-4.5,0.5", fireTile = "-5,0" });
        }
        if (!maps.IsInitialized(mapId)) maps.InitializeMap(mapId);
        foreach (var ship in Ships.Values)
        {
            var query = _em.AllEntityQueryEnumerator<BatteryComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var battery, out var transform))
                if (transform.GridUid == ship.Grid) _em.System<BatterySystem>().SetCharge(uid, battery.MaxCharge);
        }
        if (_inputs.Any(input => input.Type == "dock.dock"))
            foreach (var ship in Ships.Values) PrepareDockFixture(ship);
        File.WriteAllText(Path.Combine(_output, "bindings.json"), JsonSerializer.Serialize(Bindings));
        var logProbe = int.Parse(Environment.GetEnvironmentVariable("NATIVE_LAB_ADMINLOG_PROBE") ?? "0", CultureInfo.InvariantCulture);
        if (logProbe is < 0 or > 10000) throw new InvalidOperationException("Admin log probe must be between 0 and 10000.");
        var adminLogs = IoCManager.Resolve<IAdminLogManager>();
        for (var i = 0; i < logProbe; i++)
            adminLogs.Add(LogType.Action, LogImpact.Low, $"NativeLab admin-log regression probe {i}");
        var ticker = _em.System<GameTicker>();
        File.WriteAllText(Path.Combine(_output, "round.json"), JsonSerializer.Serialize(new
        {
            roundId = ticker.RoundId, runLevel = ticker.RunLevel.ToString(), preset = ticker.CurrentPreset?.ID,
            mapId = mapId.ToString(), adminLogProbe = logProbe,
        }));
        _samples = new StreamWriter(Path.Combine(_output, "ticks.csv"));
        _samples.WriteLine("relative_tick,engine_tick,content_engine_ms,allocated_bytes,gc0,gc1,gc2,working_set_bytes,cpu_seconds,whole_tick_ms,whole_allocated_bytes,thread_allocated_bytes,gc_pause_ms,physics_ms,physics_allocated_bytes,driver_ms,driver_allocated_bytes,observer_ms,observer_allocated_bytes");
        _events = new StreamWriter(Path.Combine(_output, "events.jsonl"), false, new UTF8Encoding(false), ProfileEnabled ? 1024 * 1024 : 1024);
        _outcomes = new StreamWriter(Path.Combine(_output, "native-outcomes.jsonl"), false, new UTF8Encoding(false), ProfileEnabled ? 1024 * 1024 : 1024);
        _networkHistory = new StreamWriter(Path.Combine(_output, "network-session-history.jsonl"), false, new UTF8Encoding(false), ProfileEnabled ? 1024 * 1024 : 1024);
        _start = _timing.CurTick.Value + 1;
        _next = 0; _writtenSamples = 0; _unavailableSince = null; TelemetryRetryRequired = false;
        _tickSamples = new TickSample[_duration];
        _listener = new LoopListener();
        File.WriteAllText(Path.Combine(_output, "started.json"), JsonSerializer.Serialize(new { status = "PARTIAL_NATIVE_DRY_RUN_RUNNING", ships = count, tickRate = _timing.TickRate, measuredTicks = _duration, warmupTicks = _warmup, plannedEvents = _inputs.Length, driverTypes = _inputs.Select(input => input.Type).Distinct().Order().ToArray(), scenarioSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(scenario))).ToLowerInvariant(), metricScope = "Robust.GameLoop TickStart to TickStop includes all tick handlers, replay dispatch and minute checkpoint/telemetry writes; excludes frame input/update outside Tick. Content interval also recorded. CSV checkpointed every minute and on telemetry retries, remainder written after measurement. Working set and CPU sampled every 60 ticks; GC pause is process-wide runtime cumulative delta." }));
        shell.WriteLine($"Native laboratory loaded {count} grids; warmup {_warmup}, measured {_duration} ticks.");
    }

    public static void PreTick()
    {
        try { PreTickCore(); }
        catch (Exception error) { RecordFailure(error); throw; }
    }

    internal static void RecordFailure(Exception error)
    {
        FinishProfile();
        _listener?.Dispose(); _listener = null;
        if (_samples != null)
        {
            var limit = _writtenSamples;
            while (limit < _tickSamples.Length && _tickSamples[limit].Captured) limit++;
            FlushSamples(limit);
        }
        _outcomes?.Dispose(); _outcomes = null;
        _networkHistory?.Dispose(); _networkHistory = null;
        _samples?.Dispose(); _events?.Dispose(); _samples = null; _events = null; _clock = 0;
        if (string.IsNullOrWhiteSpace(_output)) return;
        Directory.CreateDirectory(_output);
        File.WriteAllText(Path.Combine(_output, "failed.json"), JsonSerializer.Serialize(new { status = "FAILED_LOCAL_LAB_EXCEPTION", error = error.Message,
            exception = error.ToString(), errorType = error.GetType().FullName, engineTick = _timing?.CurTick.Value, deliveredEvents = _next }));
    }

    private static void PreTickCore()
    {
        if (_samples == null || _timing.CurTick.Value < _start) return;
        var elapsed = _timing.CurTick.Value - _start;
        var profileDriverBefore = ProfileNativeStart();
        var driverStamp = Stopwatch.GetTimestamp();
        var driverBytes = GC.GetAllocatedBytesForCurrentThread();
        if (elapsed % 60 == 0)
        {
            PrepareNetworkPlayers(elapsed >= _warmup);
            foreach (var fixture in DockChecks.Values)
            {
                _em.EnsureComponent<JointComponent>(_em.GetComponent<TransformComponent>(fixture.Own).GridUid!.Value);
                _em.EnsureComponent<JointComponent>(fixture.Grid);
            }
        }
        if (elapsed < _warmup)
        {
            FlushPendingProgress();
            if (!_crewRegistered && elapsed >= _warmup / 2) PrepareCrewRegistration();
            if (elapsed % 300 == 0) AtomicJson("progress.json", new { phase = "WARMUP", currentTick = elapsed,
                phaseTicks = _warmup, engineTick = _timing.CurTick.Value, ships = Ships.Count, utc = DateTime.UtcNow });
            return;
        }
        var tick = elapsed - _warmup;
        if (tick >= _duration)
        {
            FlushPendingProgress();
            if (_pendingProgressJson == null) Finish();
            return;
        }
        if (!_crewRegistered) PrepareCrewRegistration();
        if (!_physicsDiagnosticStarted) { BeginPhysicsDiagnostic(); _physicsDiagnosticStarted = true; }
        FlushPendingProgress();
        if (tick % 300 == 0 || (TelemetryRetryRequired && tick >= _nextTelemetryCheck))
        {
            if (ProfileEnabled) _profileFlushStreams = true;
            else
            {
                if (tick % 3600 == 0 || TelemetryRetryRequired) FlushSamples((int) tick);
                _events!.Flush(); _outcomes!.Flush();
            }
            var snapshot = new { phase = "MEASURE", currentTick = tick, phaseTicks = _duration, ships = Ships.Count, relativeTick = tick, measuredTicks = _duration, engineTick = _timing.CurTick.Value,
                progressWriteFailures = _progressWriteFailures, progressWritePending = _pendingProgressJson != null,
                utc = DateTime.UtcNow, entityCount = _em.EntityCount, deliveredEvents = _next,
                completedNative = _nativeCompleted, pendingNative = PendingGameDrivers.Count,
                nativeDeadlines = PendingGameDrivers.Select(driver => new { driver.Input.Id, driver.Input.Type, driver.Started, driver.Deadline }).ToArray(),
                physics = PhysicsSnapshot(), telemetry = Telemetry?.Invoke() };
            var progress = JsonSerializer.Serialize(snapshot);
            AtomicJson("progress.json", snapshot);
            AppendProgressLine(progress);
            _nextTelemetryCheck = tick + 60;
            if (TelemetryRetryRequired)
            {
                _unavailableSince ??= tick;
                if (tick - _unavailableSince.Value >= 120)
                    throw new InvalidOperationException($"Card availability did not recover within 120 ticks; see progress.json at tick {tick}.");
            }
            else _unavailableSince = null;
        }
        while (_next < _inputs.Length && _inputs[_next].Tick <= tick)
        {
            var input = _inputs[_next++];
            if (input.Tick != tick) throw new InvalidOperationException($"Skipped planned replay tick {input.Tick}, actual {tick}.");
            var ship = Ships[input.Ship];
            var nativeEffect = Apply(ship, input);
            _events!.WriteLine(JsonSerializer.Serialize(new { id = input.Id, ship = input.Ship, type = input.Type, scheduledTick = input.Tick, actualTick = tick, engineTick = _timing.CurTick.Value, nativeEffect }));
        }
        _tickSamples[tick].DriverMs = Stopwatch.GetElapsedTime(driverStamp).TotalMilliseconds;
        _tickSamples[tick].DriverBytes = GC.GetAllocatedBytesForCurrentThread() - driverBytes;
        ProfileNativeEnd(false, _tickSamples[tick].DriverMs, _tickSamples[tick].DriverBytes, profileDriverBefore);
        _alloc = GC.GetTotalAllocatedBytes(false);
        _clock = Stopwatch.GetTimestamp();
    }

    private static object Apply(Ship ship, Input input)
    {
        var type = input.Type;
        switch (type)
        {
            case "radiation.start":
            case "radiation.clear":
                return ExerciseRadiation(ship, input);
            case "anomaly.start":
                if (input.Prototype != "AnomalyFlesh") throw new InvalidOperationException("Anomaly input must explicitly select AnomalyFlesh.");
                var anomaly = _em.SpawnEntity(input.Prototype, new EntityCoordinates(ship.Grid, new Vector2(-4.5f, .5f)));
                var excluded = new[] { "ElectricityAnomalyComponent", "ElectrifiedComponent", "EmpOnTriggerComponent", "GravityAnomalyComponent", "GravityWellComponent", "RadiationSourceComponent", "RandomWalkComponent" };
                if (_em.GetComponents(anomaly).Any(value => excluded.Contains(value.GetType().Name)))
                    throw new InvalidOperationException("Anomaly fixture has an excluded electronics or physical hazard component.");
                var component = _em.GetComponent<AnomalyComponent>(anomaly);
                _em.System<SharedAnomalySystem>().ChangeAnomalyStability(anomaly, component.GrowthThreshold + .1f - component.Stability);
                Anomalies.Add(ship.Grid, anomaly);
                return new { active = component.Stability > component.GrowthThreshold, prototype = input.Prototype, excludedHazardsAbsent = true };
            case "anomaly.clear":
                if (AnomalyPulseEntities.Remove(ship.Grid, out var pulseEntities))
                    foreach (var pulseEntity in pulseEntities) if (_em.EntityExists(pulseEntity)) _em.DeleteEntity(pulseEntity);
                if (AnomalyFloors.Remove(ship.Grid, out var floors))
                    foreach (var (cell, floor) in floors)
                        _em.System<SharedMapSystem>().SetTile((ship.Grid, _em.GetComponent<MapGridComponent>(ship.Grid)), cell, floor);
                _em.DeleteEntity(Anomalies[ship.Grid]); Anomalies.Remove(ship.Grid);
                return new { active = false };
            case "hull.damage":
            case "hull.repair":
                return ExerciseStructure(ship, input);
            case "fire.clear" when FireFixtures.ContainsKey(ship.Grid):
                return ExerciseFire(ship, input);
            case "fire.start":
            case "fire.clear":
                var atmos = _em.System<AtmosphereSystem>();
                var gas = atmos.GetTileMixture((ship.Grid, null, null), null, ship.FireTile, true) ?? throw new InvalidOperationException("Fire fixture lacks real air.");
                gas.Clear(); gas.SetMoles(Gas.Oxygen, 21); gas.SetMoles(Gas.Nitrogen, 79);
                gas.Temperature = type == "fire.start" ? 1000 : 293.15f;
                if (type == "fire.start") { gas.SetMoles(Gas.Plasma, 5); atmos.HotspotExpose((ship.Grid, null), ship.FireTile, 1000, 100); }
                else atmos.HotspotExtinguish(ship.Grid, ship.FireTile);
                return new { burning = atmos.IsHotspotActive(ship.Grid, ship.FireTile), gas.Temperature };
            case "power.loss":
            case "power.restore":
                _em.System<SharedPowerReceiverSystem>().SetPowerDisabled(ship.PowerDevice, type == "power.loss");
                return new { disabled = _em.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(ship.PowerDevice).PowerDisabled };
            case "light.power_off":
            case "light.power_on":
            case "light.command_off":
            case "light.command_on":
                return ExerciseLight(ship, input);
            default: return ApplyGameDriver(ship, input);
        }
    }

    public static void PostTick()
    {
        try { PostTickCore(); }
        catch (Exception error) { RecordFailure(error); throw; }
    }

    private static void PostTickCore()
    {
        if (_samples == null || _clock == 0) return;
        var milliseconds = Stopwatch.GetElapsedTime(_clock).TotalMilliseconds;
        var bytes = GC.GetTotalAllocatedBytes(false) - _alloc;
        var tick = _timing.CurTick.Value - _start - _warmup;
        _tickSamples[tick].ContentMs = milliseconds;
        _tickSamples[tick].ContentBytes = bytes;
        _tickSamples[tick].PhysicsMs = _physicsMilliseconds?.Invoke() ?? double.NaN;
        _tickSamples[tick].PhysicsBytes = _physicsBytes?.Invoke() ?? -1;
        var profileObserverBefore = ProfileNativeStart();
        var observerStamp = Stopwatch.GetTimestamp();
        var observerBytes = GC.GetAllocatedBytesForCurrentThread();
        PollGameDrivers(tick);
        _tickSamples[tick].ObserverMs = Stopwatch.GetElapsedTime(observerStamp).TotalMilliseconds;
        _tickSamples[tick].ObserverBytes = GC.GetAllocatedBytesForCurrentThread() - observerBytes;
        ProfileNativeEnd(true, _tickSamples[tick].ObserverMs, _tickSamples[tick].ObserverBytes, profileObserverBefore);
        _clock = 0;
    }

    private static void Finish()
    {
        if (PendingGameDrivers.Count > 0) throw new InvalidOperationException("Native asynchronous postconditions remain pending at measurement end.");
        object? finalTelemetry = Telemetry?.Invoke();
        if (finalTelemetry != null)
        {
            using var final = JsonDocument.Parse(JsonSerializer.Serialize(finalTelemetry));
            var state = final.RootElement;
            if (state.GetProperty("activeGrids").GetInt32() != Ships.Count
                || state.GetProperty("runningCards").GetInt32() != Ships.Count * 26
                || state.GetProperty("faultedCards").GetInt32() != 0
                || state.GetProperty("expectedOfflineGrids").GetInt32() != 0
                || state.GetProperty("queues").EnumerateObject().Any(queue => queue.Value.GetInt32() != 0))
                throw new InvalidOperationException("Final KIAS state is not fully available with drained queues.");
        }
        AtomicJson("final-telemetry.json", finalTelemetry ?? new { baselineWithoutKias = true });
        AtomicJson("progress-writer.json", new { recoveredTransientFailures = _progressWriteFailures,
            pending = _pendingProgressJson != null, lastTransientError = _progressWriteError });
        FinishProfile();
        _listener?.Dispose(); _listener = null;
        FlushSamples(_tickSamples.Length);
        _outcomes!.Dispose(); _outcomes = null;
        _networkHistory!.Dispose(); _networkHistory = null;
        _samples!.Dispose(); _events!.Dispose(); _samples = null; _events = null;
        if (_next != _inputs.Length) throw new InvalidOperationException("Not all scheduled inputs were delivered.");
        File.WriteAllText(Path.Combine(_output, "physics-final.json"), JsonSerializer.Serialize(PhysicsSnapshot()));
        File.WriteAllText(Path.Combine(_output, "completed.json"), JsonSerializer.Serialize(new { status = "PARTIAL_NATIVE_DRY_RUN_COMPLETE", measuredTicks = _duration, deliveredEvents = _next, ships = Ships.Count, nativeCompleted = _nativeCompleted }));
        _shell!.WriteLine("Native laboratory completed. This is a partial driver dry run, not the full benchmark.");
    }

    private static void FlushSamples(int limit)
    {
        for (; _writtenSamples < limit; _writtenSamples++)
        {
            var tick = _writtenSamples;
            var sample = _tickSamples[tick];
            if (!sample.Captured) throw new InvalidOperationException($"Whole-tick sample missing at {tick}.");
            _samples!.WriteLine(FormattableString.Invariant($"{tick},{sample.EngineTick},{sample.ContentMs:R},{sample.ContentBytes},{sample.Gc0},{sample.Gc1},{sample.Gc2},{sample.WorkingSet},{sample.Cpu:R},{sample.WholeMs:R},{sample.WholeBytes},{sample.ThreadBytes},{sample.PauseMs:R},{sample.PhysicsMs:R},{sample.PhysicsBytes},{sample.DriverMs:R},{sample.DriverBytes},{sample.ObserverMs:R},{sample.ObserverBytes}"));
        }
        _samples!.Flush();
    }
}
