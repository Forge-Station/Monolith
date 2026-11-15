using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    protected override void InitializeBallistic()
    {
        base.InitializeBallistic();
        SubscribeLocalEvent<BallisticAmmoProviderComponent, UpdateAmmoCounterEvent>(OnBallisticAmmoCount);
    }

    private void OnBallisticAmmoCount(EntityUid uid, BallisticAmmoProviderComponent component, UpdateAmmoCounterEvent args)
    {
        if (args.Control is DefaultStatusControl control)
        {
            control.Update(GetBallisticShots(component), component.Capacity);
        }
    }

    protected override void Cycle(EntityUid uid, BallisticAmmoProviderComponent component, MapCoordinates coordinates)
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        EntityUid? ent = null;

        // Forge-Change-start: Entities is local-only — sync from container before cycle.
        while (component.Entities.Count > 0 &&
               (!Exists(component.Entities[^1]) || !component.Container.Contains(component.Entities[^1])))
        {
            component.Entities.RemoveAt(component.Entities.Count - 1);
        }

        foreach (var contained in component.Container.ContainedEntities)
        {
            if (!component.Entities.Contains(contained))
                component.Entities.Add(contained);
        }
        // Forge-Change-end

        // TODO: Combine with TakeAmmo
        if (component.Entities.Count > 0)
        {
            var existing = component.Entities[^1];
            component.Entities.RemoveAt(component.Entities.Count - 1);

            Containers.Remove(existing, component.Container);
            EnsureShootable(existing);
        }
        else if (component.UnspawnedCount > 0 && !component.InfiniteUnspawned)
        {
            component.UnspawnedCount--;
            // PredictedSpawn so chamber slot inserts don't warn in DEBUG.
            ent = PredictedSpawn(component.Proto, coordinates);
            EnsureShootable(ent.Value);
        }

        if (ent != null && IsClientSide(ent.Value))
            Del(ent.Value);

        var cycledEvent = new GunCycledEvent();
        RaiseLocalEvent(uid, ref cycledEvent);
    }
}
