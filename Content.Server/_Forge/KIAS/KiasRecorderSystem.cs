using System.Linq;
using Content.Shared._Forge.KIAS;
using Robust.Shared.Timing;

namespace Content.Server._Forge.KIAS;

public sealed class KiasRecorderSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private IGameTiming _timing = default!;
    private readonly KiasEmissionGate _controllerGate = new();
    private readonly KiasEmissionGate _gridGate = new();
    public void Record(EntityUid target, string message, string key)
    {
        if (!_kias.IsOnline(target) || !TryComp<KiasRecorderComponent>(target, out var recorder)
            || Transform(target).GridUid is not { } grid || !TryComp<KiasGridComponent>(grid, out var runtime)) return;
        var settings = runtime.Core is { } core && TryComp<KiasAudioComponent>(core, out var audio) ? audio : null;
        var severity = runtime.Core is { } uid && TryComp<KiasProtocolComponent>(uid, out var state) ? (int) state.Alert : 0;
        if (!_controllerGate.Allow(target, key, _timing.CurTime, settings?.LogCooldown ?? 2, severity, out var repeated)) return;
        message = message[..Math.Min(message.Length, 256)];
        var entry = $"[{_timing.CurTime:hh\\:mm\\:ss}] [P] {message}" + (repeated > 0 ? $" ×{repeated + 1}" : string.Empty);
        while (recorder.Entries.Count >= 64) recorder.Entries.RemoveAt(0);
        recorder.Entries.Add(entry);
        if (_gridGate.Allow(grid, key, _timing.CurTime, settings?.LogCooldown ?? 2, severity, out _))
        {
            while (runtime.Log.Count >= 64) runtime.Log.Dequeue();
            runtime.Log.Enqueue(entry);
        }
        EntityManager.System<KiasDisplaySystem>().RefreshOpen(grid);
    }
    public override void Initialize()
    {
        SubscribeLocalEvent<KiasRecorderComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<KiasTopologyChangedEvent>(OnTopology);
        SubscribeLocalEvent<KiasProtocolFiredEvent>(OnProtocol);
        SubscribeLocalEvent<KiasRecorderComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
    }

    private void OnShutdown(Entity<KiasRecorderComponent> ent, ref ComponentShutdown args) => _controllerGate.Remove(ent.Owner);
    private void OnGridRemoved(GridRemovalEvent args) => _gridGate.Remove(args.EntityUid);

    private void OnProtocol(ref KiasProtocolFiredEvent args)
    {
        if (!_kias.HasRole(args.Grid, KiasDeviceRole.Recorder)) return;
        var preset = Loc.TryGetString($"kias-preset-{args.PresetId}", out var title) ? title : (args.Index + 1).ToString();
        var critical = args.Trigger is KiasTrigger.CrewCritical or KiasTrigger.CrewDead or KiasTrigger.VesselCritical
            or KiasTrigger.Fire or KiasTrigger.AtmosDanger or KiasTrigger.Boarding;
        var text = $"[{_timing.CurTime:hh\\:mm\\:ss}] {(critical ? "[!] " : "")}[P] {preset}: {Loc.GetString($"kias-trigger-{args.Trigger.ToString().ToLowerInvariant()}")} / {args.Source}";
        foreach (var uid in Comp<KiasGridComponent>(args.Grid).Online)
        {
            if (!_kias.IsOnline(uid) || !TryComp<KiasRecorderComponent>(uid, out var recorder)) continue;
            while (recorder.ProtocolEvents.Count >= 64) recorder.ProtocolEvents.RemoveAt(0);
            recorder.ProtocolEvents.Add(text);
        }
    }

    private void OnMapInit(Entity<KiasRecorderComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Entries.Count > 64)
            ent.Comp.Entries.RemoveRange(0, ent.Comp.Entries.Count - 64);
        if (ent.Comp.ProtocolEvents.Count > 64) ent.Comp.ProtocolEvents.RemoveRange(0, ent.Comp.ProtocolEvents.Count - 64);
        for (var i = 0; i < ent.Comp.Entries.Count; i++)
            ent.Comp.Entries[i] = ent.Comp.Entries[i][..Math.Min(ent.Comp.Entries[i].Length, 512)];
        for (var i = 0; i < ent.Comp.ProtocolEvents.Count; i++)
            ent.Comp.ProtocolEvents[i] = ent.Comp.ProtocolEvents[i][..Math.Min(ent.Comp.ProtocolEvents[i].Length, 512)];
    }

    private void OnTopology(ref KiasTopologyChangedEvent args)
    {
        if (!TryComp<KiasGridComponent>(args.Grid, out var runtime) || runtime.Log.Count != 0)
            return;
        foreach (var uid in runtime.Online.OrderBy(uid => uid.Id))
        {
            if (!TryComp<KiasRecorderComponent>(uid, out var recorder))
                continue;
            foreach (var entry in recorder.Entries.TakeLast(64))
                runtime.Log.Enqueue(entry);
            if (runtime.Log.Count > 0)
                break;
        }
    }
}
