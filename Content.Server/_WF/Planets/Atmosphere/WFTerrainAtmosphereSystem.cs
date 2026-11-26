using Content.Server._WF.Planets.Flight;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.CCVar;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Layers;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Planets.Atmosphere;

/// <summary>Gives every planet layer with ground a terrain atmosphere, so a room built on a planet holds its own air.</summary>
public sealed partial class WFTerrainAtmosphereSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WFPlanetNetworkBuiltEvent>(OnNetworkBuilt);
    }

    private void OnNetworkBuilt(ref WFPlanetNetworkBuiltEvent args)
    {
        if (!_cfg.GetCVar(PlanetCVars.TerrainAtmosphere))
            return;

        AddTo(args.Ground, surface: true);

        foreach (var layer in args.Lower)
        {
            AddTo(layer, surface: false);
        }
    }

    private void AddTo(EntityUid map, bool surface)
    {
        if (!TryComp<MapGridComponent>(map, out var grid) || !TryComp<BiomeComponent>(map, out var biome))
            return;

        var ground = new HashSet<ProtoId<ContentTileDefinition>>();
        CollectTiles(biome.Layers, ground, new HashSet<string>());

        // A crashing hull scrapes the surface down to this.
        if (surface)
            ground.Add(WFFlightSystem.ScarTile);

        var ev = new WFTerrainOpenTilesEvent(map, ground);
        RaiseLocalEvent(ref ev);

        _atmos.WfAddTerrainAtmosphere((map, grid), OpenTiles(ground));
    }

    /// <summary>Every tile a biome's layers lay, through the templates they nest.</summary>
    private void CollectTiles(List<IBiomeLayer> layers, HashSet<ProtoId<ContentTileDefinition>> tiles, HashSet<string> seen)
    {
        foreach (var layer in layers)
        {
            switch (layer)
            {
                case BiomeTileLayer tile:
                    tiles.Add(tile.Tile);
                    break;
                case BiomeMetaLayer meta when seen.Add(meta.Template) && _proto.TryIndex<BiomeTemplatePrototype>(meta.Template, out var template):
                    CollectTiles(template.Layers, tiles, seen);
                    break;
            }
        }
    }

    /// <summary>The tile ids of the ground tiles and of the outdoor ground that digging turns them into.</summary>
    private HashSet<int> OpenTiles(HashSet<ProtoId<ContentTileDefinition>> ground)
    {
        var open = new HashSet<int>();

        foreach (var id in ground)
        {
            if (!_tileDefs.TryGetDefinition(id, out var definition))
                continue;

            var tile = (ContentTileDefinition) definition;

            while (open.Add(tile.TileId)
                   && !string.IsNullOrEmpty(tile.BaseTurf)
                   && _tileDefs.TryGetDefinition(tile.BaseTurf, out var under)
                   && under is ContentTileDefinition { Weather: true, MapAtmosphere: false } below)
            {
                tile = below;
            }
        }

        return open;
    }
}
