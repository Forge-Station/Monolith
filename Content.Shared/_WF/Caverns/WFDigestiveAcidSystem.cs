using Content.Shared.Damage.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._WF.Caverns;

/// <summary>
/// Digests whoever wades into a <see cref="WFDigestiveAcidComponent"/> pool with the contact damage that
/// <c>DamageContactsSystem</c> deals each second, sparing the pool's factions and anyone on a catwalk over it.
/// </summary>
public sealed partial class WFDigestiveAcidSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFDigestiveAcidComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<WFDigestiveAcidComponent, EndCollideEvent>(OnEndCollide);
    }

    private void OnStartCollide(Entity<WFDigestiveAcidComponent> ent, ref StartCollideEvent args)
    {
        var other = args.OtherEntity;

        if (HasComp<DamagedByContactComponent>(other) || !Digests(ent, other))
            return;

        EnsureComp<DamagedByContactComponent>(other).Damage = ent.Comp.Damage;
    }

    /// <summary>Stops the damage once the entity touches no other pool that digests it and nothing else that hurts on contact.</summary>
    private void OnEndCollide(Entity<WFDigestiveAcidComponent> ent, ref EndCollideEvent args)
    {
        var other = args.OtherEntity;

        if (!HasComp<DamagedByContactComponent>(other) || !TryComp<PhysicsComponent>(other, out var body))
            return;

        foreach (var contact in _physics.GetContactingEntities(other, body))
        {
            if (contact == ent.Owner)
                continue;

            if (HasComp<DamageContactsComponent>(contact)
                || TryComp<WFDigestiveAcidComponent>(contact, out var pool) && Digests((contact, pool), other))
                return;
        }

        RemComp<DamagedByContactComponent>(other);
    }

    /// <summary>Whether a pool digests an entity: one it can, outside its factions, and not standing on a cover over it.</summary>
    public bool Digests(Entity<WFDigestiveAcidComponent> pool, EntityUid target)
    {
        if (!_whitelist.IsWhitelistPassOrNull(pool.Comp.Whitelist, target)
            || _faction.IsMemberOfAny(target, pool.Comp.IgnoreFactions))
            return false;

        var xform = Transform(pool);
        if (pool.Comp.CoveredBy == null || xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return true;

        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, _map.LocalToTile(gridUid, grid, xform.Coordinates));
        while (anchored.MoveNext(out var cover))
        {
            if (cover != pool.Owner && _whitelist.IsWhitelistPass(pool.Comp.CoveredBy, cover.Value))
                return false;
        }

        return true;
    }
}
