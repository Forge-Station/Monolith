using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._WF.Caverns;
using Content.Server._DV.Planet;
using Content.Server._WF.Planets;
using Content.Server.Parallax;
using Content.Shared.Parallax.Biomes;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCities;
using Content.Shared.Maps;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetCities;

/// <summary>Gives a world with a fixed city as many underground layers as the city has levels below the street.</summary>
public sealed class WFPlanetCitySystem : EntitySystem
{
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private PlanetSystem _planet = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private WFCavernSystem _caverns = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlanetLowerLayersEvent>(OnLowerLayers, after: new[] { typeof(WFCavernSystem) });
        SubscribeLocalEvent<WFPlanetCitiesComponent, ComponentShutdown>(OnShutdown);
    }

    /// <summary>A world's city stations go with it.</summary>
    private void OnShutdown(Entity<WFPlanetCitiesComponent> ent, ref ComponentShutdown args)
    {
        foreach (var station in ent.Comp.Stations)
        {
            if (!TerminatingOrDeleted(station))
                QueueDel(station);
        }
    }

    /// <summary>
    /// Adds layers under whatever cavern the world already has, down to its cities' deepest level: the world's cavern
    /// rock without the cavern's mouths where it has one, otherwise empty.
    /// </summary>
    private void OnLowerLayers(ref WFPlanetLowerLayersEvent args)
    {
        if (!_cfg.GetCVar(PlanetCVars.FixedCities))
            return;

        var needed = 0;
        foreach (var city in _proto.EnumeratePrototypes<WFFixedCityPrototype>())
        {
            if (city.Surface != args.Surface.ID || !_proto.TryIndex<GameMapPrototype>(city.GameMap, out var gameMap)
                || !gameMap.Stations.TryGetValue(city.Station, out var config)
                || !config.StationComponentOverrides.TryGetComponent<CEStationZLevelsComponent>(_factory, out var zLevels))
                continue;

            needed = Math.Max(needed, zLevels.MapsBelow.Count);
        }

        while (args.Lower.Count < needed)
        {
            if (args.Lower.Count == 0 && _caverns.TryGetCavern(args.Surface.ID, out var cavern))
            {
                var level = _planet.SpawnPlanet(cavern.Level, runMapInit: false);
                if (TryComp<BiomeComponent>(level, out var biome))
                    _biome.SetSeed(level, biome, unchecked((args.Surface.Seed ?? 0) + cavern.SeedOffset));
                args.Lower.Add(level);
                continue;
            }

            args.Lower.Add(_maps.CreateMap(out _, runMapInit: false));
        }
    }
}
