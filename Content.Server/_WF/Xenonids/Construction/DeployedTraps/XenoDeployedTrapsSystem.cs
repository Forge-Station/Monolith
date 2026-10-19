using Content.Server.Destructible;
using Content.Shared._WF.Xenonids.Construction.DeployedTraps;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared.Damage;

namespace Content.Server._WF.Xenonids.Construction.DeployedTraps;

public sealed partial class XenoDeployedTrapsSystem : EntitySystem
{
    [Dependency] private DestructibleSystem _destructible = default!;
    [Dependency] private SharedXenoHiveSystem _hive = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoDeployedTrapsComponent, DamageChangedEvent>(OnTrapDamaged);
    }

    private void OnTrapDamaged(Entity<XenoDeployedTrapsComponent> trap, ref DamageChangedEvent args)
    {
        if (args.Origin is { } origin && _hive.FromSameHive(origin, trap.Owner))
            return;

        if (args.DamageDelta == null)
            return;

        if (!_destructible.TryGetDestroyedAt(trap.Owner, out var totalHealth))
            return;

        if (args.Damageable.TotalDamage + args.DamageDelta.GetTotal() > totalHealth)
            QueueDel(trap);
    }
}
