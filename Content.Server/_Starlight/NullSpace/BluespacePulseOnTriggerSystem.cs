using Content.Server.Explosion.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.Map;

namespace Content.Server._Starlight.NullSpace;

/// <summary>
/// Crystals: same effect but only on manual activation (TriggerOnUse/TriggerOnActivate).
/// </summary>
public sealed class BluespacePulseOnTriggerSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BluespacePulseOnTriggerComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<BluespacePulseOnTriggerComponent, TriggerEvent>(OnCrystalActivated);
    }

    private void OnAnchorChanged(Entity<BluespacePulseOnTriggerComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored && ent.Comp.CurrentDome is null)
        {
            var newDome = SpawnAttachedTo(ent.Comp.Dome, Transform(ent).Coordinates);
            _transform.SetParent(newDome, ent);

            ent.Comp.CurrentDome = newDome;
        }
        else
        {
            if (ent.Comp.CurrentDome is { } dome && !TerminatingOrDeleted(dome))
                Del(dome);

            ent.Comp.CurrentDome = null;
        }
    }

    private void OnCrystalActivated(Entity<BluespacePulseOnTriggerComponent> ent, ref TriggerEvent args)
    {
        if (TryComp<StackComponent>(ent, out var stack))
            _stack.Use(ent, 1, stack);

        // Do NOT call _trigger.Trigger(uid) here — TriggerOnUse/TriggerOnActivate already fired TriggerEvent
        // to get us here, so calling Trigger again would cause infinite recursion.
        DoNullSpaceShuntEffect(_transform.ToCoordinates(_transform.GetMapCoordinates(ent)), ent.Comp.Radius);
        args.Handled = true;
    }

    private void DoNullSpaceShuntEffect(EntityCoordinates coordinates, float range)
    {
        foreach (var uid in _lookup.GetEntitiesInRange(coordinates, range))
        {
            var ev = new NullSpaceShuntEvent();
            RaiseLocalEvent(uid, ref ev);
        }
    }
}
