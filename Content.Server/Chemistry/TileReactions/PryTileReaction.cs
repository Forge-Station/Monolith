using Content.Server.Maps;
using Content.Server.Salvage.Expeditions; // Forge-Change
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Construction.Components; // Mono
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Map.Components; // Mono

namespace Content.Server.Chemistry.TileReactions;

[UsedImplicitly]
[DataDefinition]
public sealed partial class PryTileReaction : ITileReaction
{
    public FixedPoint2 TileReact(TileRef tile,
        ReagentPrototype reagent,
        FixedPoint2 reactVolume,
        IEntityManager entityManager,
        List<ReagentData>? data)
    {
        var sys = entityManager.System<TileSystem>();
        var mapSys = entityManager.System<SharedMapSystem>(); // Mono

        // Forge-Change-Start: xeno blood is this acid. On an expedition it deconstructs cave floor straight to space.
        if (reagent.ID == "FluorosulfuricAcid" &&
            entityManager.HasComponent<SalvageExpeditionComponent>(tile.GridUid))
        {
            return FixedPoint2.Zero;
        }
        // Forge-Change-End

        // Mono
        var grid = tile.GridUid;
        foreach (var ent in mapSys.GetAnchoredEntities((grid, entityManager.GetComponent<MapGridComponent>(grid)), tile.GridIndices))
        {
            // if we're not unanchorable, refuse to pry tile
            if (!entityManager.TryGetComponent<AnchorableComponent>(ent, out var anch))
                return FixedPoint2.Zero;

            if ((anch.Flags & AnchorableFlags.Unanchorable) == 0)
                return FixedPoint2.Zero;
        }

        sys.DeconstructTile(tile); // Mono - change from PryTile
        return reactVolume;
    }
}
