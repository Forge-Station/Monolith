using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;

namespace Content.Server._WF.Xenobiology.Effects;

/// <summary>
/// Runs <see cref="Effects"/> on the target after <see cref="Delay"/> seconds.
/// </summary>
public sealed partial class XenobioDelayed : EntityEffect
{
    [DataField]
    public float Delay = 5f;

    [DataField(required: true)]
    public List<EntityEffect> Effects = new();

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<XenobioTimedEffectSystem>()
            .Schedule(args.TargetEntity, TimeSpan.FromSeconds(Delay), Effects);
    }
}

/// <summary>
/// Makes the target invisible for <see cref="Duration"/> seconds.
/// </summary>
public sealed partial class XenobioStealth : EntityEffect
{
    [DataField]
    public float Duration = 30f;

    [DataField]
    public float Visibility;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<XenobioTimedEffectSystem>()
            .AddStealth(args.TargetEntity, TimeSpan.FromSeconds(Duration), Visibility);
    }
}

/// <summary>
/// Swaps the target's NPC factions for <see cref="NewFaction"/> for <see cref="Duration"/> seconds.
/// </summary>
public sealed partial class XenobioChangeFaction : EntityEffect
{
    [DataField(required: true)]
    public ProtoId<NpcFactionPrototype> NewFaction;

    [DataField]
    public float Duration = 30f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<XenobioTimedEffectSystem>()
            .ChangeFaction(args.TargetEntity, NewFaction, TimeSpan.FromSeconds(Duration));
    }
}

/// <summary>
/// Gives the target <see cref="Components"/> for <see cref="Duration"/> seconds; ones it already had are kept.
/// </summary>
public sealed partial class XenobioGrantComponents : EntityEffect
{
    [DataField(required: true)]
    public ComponentRegistry Components = new();

    [DataField]
    public float Duration = 60f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<XenobioTimedEffectSystem>()
            .GrantComponents(args.TargetEntity, Components, TimeSpan.FromSeconds(Duration));
    }
}

/// <summary>
/// Timers left behind by xenobiology effects on an entity.
/// </summary>
[RegisterComponent]
public sealed partial class XenobioTimedEffectsComponent : Component
{
    [ViewVariables]
    public List<(TimeSpan At, List<EntityEffect> Effects)> Delayed = new();

    [ViewVariables]
    public TimeSpan? StealthEnd;

    /// <summary>
    /// Whether the stealth was added by the effect, so it is removed when it ends.
    /// </summary>
    [ViewVariables]
    public bool AddedStealth;

    [ViewVariables]
    public TimeSpan? FactionEnd;

    [ViewVariables]
    public HashSet<ProtoId<NpcFactionPrototype>>? OldFactions;

    [ViewVariables]
    public List<(TimeSpan At, Type Component)> Granted = new();
}

/// <summary>
/// Runs and ends the timed xenobiology effects.
/// </summary>
public sealed partial class XenobioTimedEffectSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;

    private readonly List<List<EntityEffect>> _due = new();

    public void Schedule(EntityUid uid, TimeSpan delay, List<EntityEffect> effects)
    {
        EnsureComp<XenobioTimedEffectsComponent>(uid).Delayed.Add((_timing.CurTime + delay, effects));
    }

    public void AddStealth(EntityUid uid, TimeSpan duration, float visibility)
    {
        var comp = EnsureComp<XenobioTimedEffectsComponent>(uid);
        if (comp.StealthEnd == null)
            comp.AddedStealth = !HasComp<StealthComponent>(uid);

        var stealth = EnsureComp<StealthComponent>(uid);
        _stealth.SetVisibility(uid, visibility, stealth);
        comp.StealthEnd = _timing.CurTime + duration;
    }

    public void ChangeFaction(EntityUid uid, ProtoId<NpcFactionPrototype> faction, TimeSpan duration)
    {
        if (!TryComp<NpcFactionMemberComponent>(uid, out var member))
            return;

        var comp = EnsureComp<XenobioTimedEffectsComponent>(uid);
        comp.OldFactions ??= new HashSet<ProtoId<NpcFactionPrototype>>(member.Factions);
        _factions.ClearFactions((uid, member), false);
        _factions.AddFaction((uid, member), faction);
        comp.FactionEnd = _timing.CurTime + duration;
    }

    public void GrantComponents(EntityUid uid, ComponentRegistry components, TimeSpan duration)
    {
        var comp = EnsureComp<XenobioTimedEffectsComponent>(uid);
        var end = _timing.CurTime + duration;
        foreach (var (_, entry) in components)
        {
            var type = entry.Component.GetType();
            if (HasComp(uid, type))
                continue;

            var added = (Component) _serialization.CreateCopy(entry.Component, notNullableOverride: true);
            AddComp(uid, added);
            comp.Granted.Add((end, type));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<XenobioTimedEffectsComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            _due.Clear();
            for (var i = comp.Delayed.Count - 1; i >= 0; i--)
            {
                if (comp.Delayed[i].At > now)
                    continue;

                _due.Add(comp.Delayed[i].Effects);
                comp.Delayed.RemoveAt(i);
            }

            foreach (var effects in _due)
            {
                var args = new EntityEffectReagentArgs(uid, EntityManager, null, null, FixedPoint2.New(1), null, null, FixedPoint2.New(1));
                foreach (var effect in effects)
                {
                    XenobioEffectHelpers.Apply(effect, args);
                }
            }

            if (comp.StealthEnd is { } stealthEnd && stealthEnd <= now)
            {
                comp.StealthEnd = null;
                if (comp.AddedStealth)
                    RemComp<StealthComponent>(uid);
            }

            if (comp.FactionEnd is { } factionEnd && factionEnd <= now && comp.OldFactions is { } old
                && TryComp<NpcFactionMemberComponent>(uid, out var member))
            {
                _factions.ClearFactions((uid, member), false);
                _factions.AddFactions((uid, member), old);
                comp.FactionEnd = null;
                comp.OldFactions = null;
            }

            for (var i = comp.Granted.Count - 1; i >= 0; i--)
            {
                if (comp.Granted[i].At > now)
                    continue;

                RemComp(uid, comp.Granted[i].Component);
                comp.Granted.RemoveAt(i);
            }

            if (comp.Delayed.Count == 0 && comp.StealthEnd == null && comp.FactionEnd == null && comp.Granted.Count == 0)
                RemCompDeferred<XenobioTimedEffectsComponent>(uid);
        }
    }
}
