using Content.Server._NF.Worldgen.Components.Debris;
using Content.Server.Worldgen.Components.Debris;

namespace Content.Server.Worldgen.Systems.Debris;

public sealed partial class DebrisFeaturePlacerSystem
{
    /// <summary>The chunk controller that owns a piece of debris, if any.</summary>
    public EntityUid? GetDebrisOwner(EntityUid debris)
    {
        return TryComp<OwnedDebrisComponent>(debris, out var owned) &&
               HasComp<DebrisFeaturePlacerControllerComponent>(owned.OwningController)
            ? owned.OwningController
            : null;
    }

    /// <summary>Takes debris off its chunk without deleting it, freeing its slot for whatever is spawned there next.</summary>
    public void ReleaseDebris(EntityUid debris)
    {
        if (!TryComp<OwnedDebrisComponent>(debris, out var owned))
            return;

        var controller = owned.OwningController;
        var key = owned.LastKey;
        RemComp<OwnedDebrisComponent>(debris);
        // Shutdown leaves a null slot for debris that lives on, and a chunk with any slot left never places debris again.
        if (TryComp<DebrisFeaturePlacerControllerComponent>(controller, out var placer) &&
            placer.OwnedDebris.TryGetValue(key, out var held) && held == null)
            placer.OwnedDebris.Remove(key);
    }

    /// <summary>Gives debris spawned from other debris to the same chunk, so it unloads and is collected with it.</summary>
    public void AdoptDebris(EntityUid controller, EntityUid child)
    {
        if (!TryComp<DebrisFeaturePlacerControllerComponent>(controller, out var placer))
            return;

        var key = _xformSys.GetWorldPosition(child);
        placer.OwnedDebris[key] = child;
        var owned = EnsureComp<OwnedDebrisComponent>(child);
        owned.OwningController = controller;
        owned.LastKey = key;
        EnsureComp<SpaceDebrisComponent>(child);
    }
}
