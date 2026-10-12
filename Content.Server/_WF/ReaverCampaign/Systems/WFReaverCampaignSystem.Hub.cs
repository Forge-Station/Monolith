using System.Linq;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ReaverCampaign;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>The farthest a shield generator may stand from a power provider and still be fed by it, in tiles.</summary>
    private const float ProviderReach = 3f;

    /// <summary>The shield generator of a hull, or null if it has none.</summary>
    public EntityUid? ShieldOf(EntityUid grid)
    {
        var query = EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && !TerminatingOrDeleted(uid))
                return uid;
        }

        return null;
    }

    /// <summary>Gives a hub hull a shield generator: the one its map carries, else one anchored on a free tile within reach of a power provider.</summary>
    private bool EnsureShield(WFReaverCampaignPrototype campaign, EntityUid grid)
    {
        if (ShieldOf(grid) != null)
            return true;

        if (!TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        foreach (var tile in ShieldTiles((grid, gridComp)))
        {
            var emitter = Spawn(campaign.HubShield, _map.GridTileToLocal(grid, gridComp, tile));
            if (Transform(emitter).Anchored || _transform.AnchorEntity(emitter))
                return true;

            QueueDel(emitter);
        }

        Log.Warning($"Reaver hub {ToPrettyString(grid)} has no free tile in reach of a power provider; it stands without a shield.");
        return false;
    }

    /// <summary>The free interior tiles of a grid within reach of a power provider, nearest first.</summary>
    private List<Vector2i> ShieldTiles(Entity<MapGridComponent> grid)
    {
        var providers = new List<Vector2i>();
        var query = EntityQueryEnumerator<ExtensionCableProviderComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == grid.Owner && xform.Anchored)
                providers.Add(_map.TileIndicesFor(grid, grid.Comp, xform.Coordinates));
        }

        var reach = (int) ProviderReach;
        var best = new Dictionary<Vector2i, float>();
        foreach (var provider in providers)
        {
            for (var x = -reach; x <= reach; x++)
            {
                for (var y = -reach; y <= reach; y++)
                {
                    var distance = MathF.Sqrt(x * x + y * y);
                    var tile = provider + new Vector2i(x, y);
                    if (distance > ProviderReach || distance < 1f || !IsFreeInterior(grid, tile))
                        continue;

                    if (!best.TryGetValue(tile, out var known) || distance < known)
                        best[tile] = distance;
                }
            }
        }

        return best.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToList();
    }

    /// <summary>Whether a tile and its four neighbours are floor with nothing solid anchored on them, so a machine there cannot plug a corridor or doorway.</summary>
    private bool IsFreeInterior(Entity<MapGridComponent> grid, Vector2i tile)
    {
        foreach (var offset in new[] { Vector2i.Zero, Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right })
        {
            var at = tile + offset;
            if (_map.GetTileRef(grid, at).Tile.IsEmpty || HasSolidAnchored(grid, at))
                return false;
        }

        return true;
    }

    /// <summary>Whether something solid is anchored on a tile.</summary>
    private bool HasSolidAnchored(Entity<MapGridComponent> grid, Vector2i tile)
    {
        foreach (var anchored in _map.GetAnchoredEntities(grid, tile))
        {
            if (TryComp<PhysicsComponent>(anchored, out var body) && body.CanCollide && body.Hard)
                return true;
        }

        return false;
    }

    /// <summary>Sets a stronghold's level and shows it in the encounter's name on the map.</summary>
    private void SetLevel(Entity<WFReaverStrongholdComponent, WFEncounterComponent> stronghold, int level)
    {
        var (uid, comp, encounter) = stronghold;
        comp.Level = level;
        if (comp.BaseName.Length == 0)
            comp.BaseName = encounter.Name;

        encounter.Name = Loc.GetString("wf-reaver-name-level", ("name", comp.BaseName), ("level", level));
        _meta.SetEntityName(uid, encounter.Name);
    }

    /// <summary>The cap on a stronghold's level at the current tier, and the length of the reinforcement list.</summary>
    private int LevelCap(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        return Math.Min(ByTier(campaign.MaxLevelByTier, state.Tier, 0), campaign.Reinforcements.Count);
    }

    /// <summary>The factor a stronghold's bounty is scaled by for its level.</summary>
    private static float LevelFactor(WFReaverCampaignPrototype campaign, int level)
    {
        return 1f + campaign.LevelBountyScale * level;
    }

    /// <summary>The levels of the standing strongholds as text, highest first, for the map legend.</summary>
    private string LevelsText()
    {
        var levels = Strongholds().Select(stronghold => stronghold.Comp1.Level).OrderByDescending(level => level).ToList();
        if (levels.Count == 0)
            return string.Empty;

        return Loc.GetString("wf-reaver-legend-levels", ("count", levels.Count), ("levels", string.Join("/", levels)));
    }
}
