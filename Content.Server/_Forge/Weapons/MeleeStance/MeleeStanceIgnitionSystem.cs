using Content.Server.Atmos.EntitySystems;
using Content.Shared._Forge.Weapons.MeleeStance;
using Content.Shared.Atmos.Components;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._Forge.Weapons.MeleeStance;

public sealed class MeleeStanceIgnitionSystem : EntitySystem
{
    [Dependency] private FlammableSystem _flammable = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MeleeStanceComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(Entity<MeleeStanceComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit ||
            ent.Comp.CurrentMode < 0 ||
            ent.Comp.CurrentMode >= ent.Comp.Modes.Count)
        {
            return;
        }

        var fireStacks = ent.Comp.Modes[ent.Comp.CurrentMode].FireStacks ?? 0f;
        if (fireStacks == 0f)
            return;

        foreach (var target in args.HitEntities)
        {
            if (!TryComp<FlammableComponent>(target, out var flammable))
                continue;

            _flammable.AdjustFireStacks(target, fireStacks, flammable);
            if (fireStacks > 0f)
                _flammable.Ignite(target, ent.Owner, flammable, args.User);
        }
    }
}