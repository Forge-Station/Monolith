using Robust.Shared.Random;
using System.Linq;
using Content.Server.Worldgen.Components.Debris;
using Content.Shared._WF.Lattice;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Lattice;

/// <summary>A wreck the Unmooring has filled with Remade: a lost ship.</summary>
[RegisterComponent]
public sealed partial class WFLatticeLostShipComponent : Component;

/// <summary>
/// Now and then a wreck drifting in the Deep, with nobody near, fills with Remade and shows on radar as a lost ship. It
/// happens more as the Lattice's primary anchors fall.
/// </summary>
public sealed partial class WFLatticeSystem
{
    [Dependency] private TurfSystem _turf = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextLostShip = new();
    private readonly List<EntityUid> _wrecks = new();
    private readonly List<Vector2i> _floor = new();

    /// <summary>How many of the Lattice's primary anchors are gone.</summary>
    private int LostAnchors(WFLatticeProfilePrototype profile)
    {
        return Math.Max(0, profile.AnchorCount - _primaries);
    }

    /// <summary>The take rate's multiplier for the anchors lost.</summary>
    private float TakeScale(WFLatticeProfilePrototype profile)
    {
        return 1f + profile.TakeLostScale * LostAnchors(profile);
    }

    private void RollLostShip(Entity<WFLatticeFieldComponent> map, WFLatticeProfilePrototype profile)
    {
        if (profile.LostShipMobs.Count == 0)
            return;

        var now = _timing.CurTime;
        if (!_nextLostShip.TryGetValue(map, out var next))
        {
            _nextLostShip[map] = now + TimeSpan.FromSeconds(profile.LostShipInterval);
            return;
        }

        if (now < next)
            return;

        _nextLostShip[map] = now + TimeSpan.FromSeconds(profile.LostShipInterval * _random.NextFloat(0.75f, 1.25f));
        var chance = profile.LostShipChance + profile.LostShipChancePerAnchor * LostAnchors(profile);
        if (_random.Prob(Math.Clamp(chance, 0f, 1f)))
            TryLoseShip(map, profile);
    }

    /// <summary>Fills one unwatched wreck in the Deep with Remade. False if there was none.</summary>
    public bool TryLoseShip(Entity<WFLatticeFieldComponent> map, WFLatticeProfilePrototype profile)
    {
        _wrecks.Clear();
        var debris = EntityQueryEnumerator<OwnedDebrisComponent, MapGridComponent, MetaDataComponent, TransformComponent>();
        while (debris.MoveNext(out var uid, out _, out _, out var meta, out var xform))
        {
            if (xform.MapUid != map.Owner
                || HasComp<WFLatticeLostShipComponent>(uid)
                || meta.EntityPrototype?.ID.Contains("Wreck") != true)
                continue;

            var at = _transform.GetWorldPosition(xform);
            if (WFLatticeMath.Band(WFLatticeMath.Field(at, _anchors, profile.Reach), map.Comp.Thresholds) < profile.LostShipBand
                || Watched(xform.MapID, at, profile.LostShipUnwatched))
                continue;

            _wrecks.Add(uid);
        }

        foreach (var wreck in _wrecks.OrderBy(_ => _random.Next()))
        {
            if (Fill(wreck, profile))
                return true;
        }

        return false;
    }

    /// <summary>Whether a player is within a distance of a point.</summary>
    private bool Watched(Robust.Shared.Map.MapId mapId, System.Numerics.Vector2 at, float distance)
    {
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is { } mob
                && Transform(mob).MapID == mapId
                && (_transform.GetWorldPosition(mob) - at).LengthSquared() < distance * distance)
                return true;
        }

        return false;
    }

    private bool Fill(EntityUid wreck, WFLatticeProfilePrototype profile)
    {
        var grid = Comp<MapGridComponent>(wreck);
        _floor.Clear();
        foreach (var tile in _maps.GetAllTiles(wreck, grid))
        {
            if (!_turf.IsTileBlocked(wreck, tile.GridIndices, CollisionGroup.Impassable | CollisionGroup.MidImpassable, grid))
                _floor.Add(tile.GridIndices);
        }

        if (_floor.Count < profile.LostShipMinFloor)
            return false;

        var count = _random.Next(profile.LostShipCount.X, profile.LostShipCount.Y + 1);
        for (var i = 0; i < count; i++)
        {
            Spawn(_random.Pick(profile.LostShipMobs), _maps.GridTileToLocal(wreck, grid, _random.Pick(_floor)));
        }

        EnsureComp<WFLatticeLostShipComponent>(wreck);
        _meta.SetEntityName(wreck, Loc.GetString("wf-lattice-lost-ship-name"));
        _shuttle.RemoveIFFFlag(wreck, IFFFlags.HideLabel | IFFFlags.HideLabelAlways);
        return true;
    }
}
