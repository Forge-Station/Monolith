using Content.Shared._Goobstation.DoAfter;
using Content.Shared._Onyx.Wounds;

namespace Content.Shared._WF.Wolfmed.DoAfter;

/// <summary>Feeds Onyx's fracture manipulation multiplier into Wolfgate's do-after delay event.</summary>
public sealed class WolfmedFractureDoAfterSystem : EntitySystem
{
    [Dependency] private FractureEffectSystem _fractureEffects = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        // Onyx raises its own GetManipulationDurationMultiplierEvent from SharedDoAfterSystem; PLAN §3 keeps
        // that file untouched, so we hang off Wolfgate's existing Goobstation multiplier event instead.
        // Pair verified free: the event is otherwise held by DoAfterDelayMultiplierComponent and BodyComponent.
        SubscribeLocalEvent<WoundHostComponent, GetDoAfterDelayMultiplierEvent>(OnGetDelayMultiplier);
    }

    private void OnGetDelayMultiplier(Entity<WoundHostComponent> ent, ref GetDoAfterDelayMultiplierEvent args)
    {
        // Wolfgate's event carries no Used item, so the active hand decides the symmetry.
        args.Multiplier *= _fractureEffects.GetDurationMultiplier(ent.Owner);
    }
}
