using Content.Server._EinsteinEngines.Silicon.WeldingHealing;
using Content.Server.Medical.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Damage;

namespace Content.Server._WF.Wolfmed.Surgery;

/// <summary>
/// Playtest 3 IPC 2: the weld and rewire steps. Each pass is what the step's own tool does in a hand: the welder's
/// repair through the routing with the Mechanical capability (WeldingHealableSystem), the cable coil's heal through
/// WoundHealingSystem. The completion checks are shared (WolfmedSurgeryConditionSystem).
/// </summary>
public sealed class WolfmedChassisRepairSurgerySystem : EntitySystem
{
    private static readonly HashSet<TreatmentCapability> WeldCapabilities = new() { TreatmentCapability.Mechanical };

    [Dependency] private WolfmedSurgeryConditionSystem _conditions = default!;
    [Dependency] private WolfmedWoundDamageSyncSystem _damageSync = default!;
    [Dependency] private WoundDamageRoutingSystem _routing = default!;
    [Dependency] private WoundHealingSystem _woundHealing = default!;
    [Dependency] private WoundSystem _wounds = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedSurgeryWeldChassisEffectComponent, SurgeryStepEvent>(OnWeld);
        SubscribeLocalEvent<WolfmedSurgeryRewireChassisEffectComponent, SurgeryStepEvent>(OnRewire);
    }

    /// <summary>
    /// One welder pass. The damage it removes closes the wounds made of it, as by hand; once the part has none of the
    /// welder's damage types left, the pass works on the wounds themselves, as a topical does, so it always gets
    /// somewhere. The pod's welder is never lit and never spends fuel: it has no way to be refuelled.
    /// </summary>
    private void OnWeld(Entity<WolfmedSurgeryWeldChassisEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (!HasComp<WoundHostComponent>(args.Body) ||
            FindTool<WolfmedHullWeldComponent, WeldingHealingComponent>(args.Tools) is not { } welder)
            return;

        var body = args.Body;
        var part = args.Part;
        var user = args.User;
        var repair = welder.Comp.Damage;
        var dealt = new DamageSpecifier();
        _routing.WithTreatmentCapabilities(body, WeldCapabilities, () =>
            _routing.TryApplyPartDamage(body, part, repair, user, out dealt, ignoreResistances: true, healWounds: true));

        if (dealt.Empty)
            _wounds.TryHealWounds(part, repair);

        _damageSync.SyncPart(part);
    }

    /// <summary>A run of cable coil uses, each exactly a hand coil's, until the part's wiring faults are gone.</summary>
    private void OnRewire(Entity<WolfmedSurgeryRewireChassisEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (!HasComp<WoundHostComponent>(args.Body) ||
            FindTool<WolfmedServoKitComponent, HealingComponent>(args.Tools) is not { } coil)
            return;

        for (var use = 0; use < Math.Max(1, ent.Comp.Uses); use++)
        {
            if (!_conditions.HasAnyWound(args.Part, ent.Comp.Wounds))
                break;

            _woundHealing.TryApplyHealing(args.Body, args.Part, coil, args.User, out _, out _);
        }

        _damageSync.SyncPart(args.Part);
    }

    /// <summary>The first tool that is both the step's tool and carries the repair it does by hand.</summary>
    private Entity<TRepair>? FindTool<TStep, TRepair>(List<EntityUid> tools)
        where TStep : IComponent
        where TRepair : IComponent
    {
        foreach (var tool in tools)
        {
            if (HasComp<TStep>(tool) && TryComp(tool, out TRepair? repair))
                return (tool, repair);
        }

        return null;
    }
}
