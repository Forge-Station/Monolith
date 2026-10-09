using Content.Shared._Forge.OrePipe;
using Content.Shared.Damage;

namespace Content.Server._Forge.OrePipe;

/// <summary>
/// Remembers which ship drill last damaged an ore crab/golem so loot can go into the ore-pipe buffer
/// (see <see cref="Content.Server.Destructible.Thresholds.Behaviors.SpawnEntitiesBehavior"/>).
/// </summary>
public sealed partial class OreDrillHarvestSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OreDrillHarvestTargetComponent, DamageChangedEvent>(OnDamageChanged);
    }

    private void OnDamageChanged(EntityUid uid, OreDrillHarvestTargetComponent component, DamageChangedEvent args)
    {
        if (args.Origin is not { } origin)
            return;

        if (HasComp<OrePipeBufferComponent>(origin))
            component.LastDrill = origin;
    }
}
