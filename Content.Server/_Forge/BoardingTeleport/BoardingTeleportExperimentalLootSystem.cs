using Content.Shared._Forge.BoardingTeleport.Components;
using Content.Shared.Mobs;
using Robust.Shared.Random;

namespace Content.Server._Forge.BoardingTeleport;

public sealed class BoardingTeleportExperimentalLootSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BoardingTeleportExperimentalLootComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(Entity<BoardingTeleportExperimentalLootComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || _random.NextFloat() > ent.Comp.DropChance)
            return;

        Spawn(ent.Comp.DiskPrototype, Transform(ent.Owner).Coordinates);
    }
}
