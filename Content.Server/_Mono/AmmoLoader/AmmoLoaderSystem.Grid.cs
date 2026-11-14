using System.Numerics;
using Content.Server._Mono.SpaceArtillery.Components;
using Content.Shared._Lua.AmmoLoader;
using Content.Shared._Mono.AmmoLoader;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server._Mono.AmmoLoader;

public sealed partial class AmmoLoaderSystem
{
    private const float ReplenishInterval = 2f;

    private float _replenishAccumulator;

    // Forge-Change: ship guns on the loader's grid are assigned and topped up from storage.
    public override void Update(float frameTime)
    {
        _replenishAccumulator += frameTime;
        if (_replenishAccumulator < ReplenishInterval)
            return;

        _replenishAccumulator = 0f;
        var gunsByGrid = CollectGridGuns();

        var loaders = EntityQueryEnumerator<AmmoLoaderComponent, TransformComponent>();
        while (loaders.MoveNext(out var uid, out var comp, out var xform))
        {
            comp.GridGuns.Clear();
            if (!comp.AutoAssign || !xform.Anchored || xform.GridUid is not { } grid)
                continue;

            if (!gunsByGrid.TryGetValue(grid, out var guns))
                continue;

            var origin = _transform.GetWorldPosition(uid);
            guns.Sort((a, b) => DistanceSquared(origin, a).CompareTo(DistanceSquared(origin, b)));
            comp.GridGuns.AddRange(guns);

            if (comp.AutoReplenish && comp.Container is { } container && container.ContainedEntities.Count > 0)
                TryReplenish((uid, comp));
        }
    }

    /// <summary>Refreshes the grid list immediately, for the window opening before the next tick.</summary>
    public void RefreshGridGuns(Entity<AmmoLoaderComponent> loader)
    {
        loader.Comp.GridGuns.Clear();
        if (!loader.Comp.AutoAssign)
            return;

        var xform = Transform(loader);
        if (!xform.Anchored || xform.GridUid is not { } grid)
            return;

        var origin = _transform.GetWorldPosition(loader);
        var guns = new List<EntityUid>();
        var query = EntityQueryEnumerator<SpaceArtilleryComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var gunXform))
        {
            if (!gunXform.Anchored || gunXform.GridUid != grid)
                continue;

            guns.Add(uid);
        }

        guns.Sort((a, b) => DistanceSquared(origin, a).CompareTo(DistanceSquared(origin, b)));
        loader.Comp.GridGuns.AddRange(guns);
    }

    /// <summary>Fills assigned guns that are empty or short. Returns how many guns took ammo.</summary>
    public int TryReplenish(Entity<AmmoLoaderComponent> loader, EntityUid? user = null)
    {
        var fed = 0;
        foreach (var gun in GetLinkedArtillery(loader))
        {
            if (IsInfiniteSupply(gun))
                continue;

            if (!TryGetTurretAmmoState(gun, out _, out var count, out var capacity, out var canModify) || !canModify)
                continue;

            if (capacity > 0 && count >= capacity)
                continue;

            // A magazine that still has rounds stays in the gun. Only an empty slot gets a new one.
            if (IsMagazineGun(gun) && count > 0)
                continue;

            if (!TryFindBestAmmo(loader, gun, out var ammo))
                continue;

            var proto = MetaData(ammo).EntityPrototype?.ID;
            if (proto == null)
                continue;

            if (TryLoadAmmoToTurret(loader, gun, proto, user))
                fed++;
        }

        if (user != null)
        {
            var message = fed > 0
                ? Loc.GetString("ammo-loader-replenished", ("count", fed))
                : Loc.GetString("ammo-loader-replenish-none");
            _popup.PopupEntity(message, loader, user.Value);
        }

        return fed;
    }

    private void OnExamined(Entity<AmmoLoaderComponent> ent, ref ExaminedEvent args)
    {
        if (!ent.Comp.AutoAssign || !args.IsInDetailsRange)
            return;

        RefreshGridGuns(ent);
        args.PushMarkup(Loc.GetString("ammo-loader-examine-auto", ("count", ent.Comp.GridGuns.Count)));
    }

    private Dictionary<EntityUid, List<EntityUid>> CollectGridGuns()
    {
        var gunsByGrid = new Dictionary<EntityUid, List<EntityUid>>();
        var query = EntityQueryEnumerator<SpaceArtilleryComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (!xform.Anchored || xform.GridUid is not { } grid)
                continue;

            if (!gunsByGrid.TryGetValue(grid, out var list))
            {
                list = new List<EntityUid>();
                gunsByGrid[grid] = list;
            }

            list.Add(uid);
        }

        return gunsByGrid;
    }

    private float DistanceSquared(Vector2 origin, EntityUid uid)
    {
        var delta = _transform.GetWorldPosition(uid) - origin;
        return delta.LengthSquared();
    }

    private bool IsMagazineGun(EntityUid artillery)
    {
        return _gun.TryGetGun(artillery, out var gunUid, out _)
               && HasComp<MagazineAmmoProviderComponent>(gunUid);
    }

    private bool IsInfiniteSupply(EntityUid artillery)
    {
        if (!_gun.TryGetGun(artillery, out var gunUid, out _))
            return false;

        if (TryComp<BallisticAmmoProviderComponent>(gunUid, out var ballistic) && ballistic.InfiniteUnspawned)
            return true;

        if (!TryComp<ItemSlotsComponent>(gunUid, out var slots))
            return false;

        if (slots.Slots.GetValueOrDefault("gun_magazine")?.Item is not { } magazine)
            return false;

        return TryComp<BallisticAmmoProviderComponent>(magazine, out var magazineAmmo) && magazineAmmo.InfiniteUnspawned;
    }

    private bool TryFindBestAmmo(Entity<AmmoLoaderComponent> loader, EntityUid artillery, out EntityUid ammo)
    {
        ammo = default;
        var bestCount = -1;
        foreach (var contained in loader.Comp.Container.ContainedEntities)
        {
            if (AmmoLoaderCapacity.IsEmptyAmmoContainer(EntityManager, contained))
                continue;

            if (!IsAmmoCompatible(loader, artillery, contained))
                continue;

            var count = AmmoLoaderCapacity.GetStoredAmmoUnitCount(EntityManager, contained);
            if (count <= bestCount)
                continue;

            bestCount = count;
            ammo = contained;
        }

        return bestCount > 0;
    }
}
