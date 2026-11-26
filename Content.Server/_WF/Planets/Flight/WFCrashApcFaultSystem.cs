using Content.Server.Power.Components;
using Content.Server.Wires;
using Content.Server.Power.EntitySystems;
using Content.Shared._WF.Planets.Flight;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Random;

namespace Content.Server._WF.Planets.Flight;

/// <summary>Crash-damaged APCs flicker their output until repaired with a multitool.</summary>
public sealed partial class WFCrashApcFaultSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedToolSystem _tools = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(PowerNetSystem));
        SubscribeLocalEvent<WFCrashApcFaultComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<WFCrashApcFaultComponent, InteractUsingEvent>(OnInteract, before: new[] { typeof(WiresSystem) });
        SubscribeLocalEvent<WFCrashApcFaultComponent, WFApcRepairDoAfterEvent>(OnRepair);
        SubscribeLocalEvent<WFCrashApcFaultComponent, ComponentShutdown>(OnShutdown);
    }

    /// <summary>Faults every APC on the grid that feeds a power-limited engine.</summary>
    public void DamageOverloadedApcs(EntityUid grid)
    {
        var circuits = new HashSet<Content.Server.Power.Pow3r.PowerState.Network>();
        var query = EntityQueryEnumerator<WFAtmosphereThrusterComponent, ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out var engine, out var receiver))
            if (engine.PowerLimited && Transform(uid).GridUid == grid && receiver.Provider?.Net is { } network)
                circuits.Add(network.NetworkNode);

        // Receivers commonly attach to extension cables, not directly to the APC entity.
        var apcs = EntityQueryEnumerator<ApcComponent>();
        while (apcs.MoveNext(out var uid, out var apc))
        {
            if (Transform(uid).GridUid != grid || apc.Net is not { } network || !circuits.Contains(network.NetworkNode))
                continue;
            var fault = EnsureComp<WFCrashApcFaultComponent>(uid);
            fault.Interrupted = true;
            fault.NextFlicker = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(2f, 5f));
        }
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WFCrashApcFaultComponent, ApcComponent, PowerNetworkBatteryComponent>();
        while (query.MoveNext(out _, out var fault, out var apc, out var battery))
        {
            if (_timing.CurTime >= fault.NextFlicker)
            {
                fault.Interrupted = !fault.Interrupted;
                fault.NextFlicker = _timing.CurTime + TimeSpan.FromSeconds(fault.Interrupted ? _random.NextFloat(2f, 5f) : _random.NextFloat(1.2f, 3.2f));
            }
            // Preserve the user's main breaker and never manufacture supply or bypass a disabled battery.
            battery.CanDischarge = apc.MainBreakerEnabled && !fault.Interrupted;
        }
    }

    private void OnExamine(Entity<WFCrashApcFaultComponent> ent, ref ExaminedEvent args) =>
        args.PushText(Loc.GetString("wf-crash-apc-fault-examine"));

    private void OnInteract(Entity<WFCrashApcFaultComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tools.HasQuality(args.Used, "Pulsing"))
            return;
        args.Handled = _tools.UseTool(args.Used, args.User, ent.Owner, 3f, "Pulsing", new WFApcRepairDoAfterEvent());
    }

    private void OnRepair(Entity<WFCrashApcFaultComponent> ent, ref WFApcRepairDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;
        args.Handled = true;
        RemComp<WFCrashApcFaultComponent>(ent.Owner);
        _popup.PopupEntity(Loc.GetString("wf-crash-apc-fault-repaired"), ent.Owner, args.User, PopupType.Medium);
    }

    private void OnShutdown(Entity<WFCrashApcFaultComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<ApcComponent>(ent.Owner, out var apc) && TryComp<PowerNetworkBatteryComponent>(ent.Owner, out var battery))
            battery.CanDischarge = apc.MainBreakerEnabled;
    }
}
