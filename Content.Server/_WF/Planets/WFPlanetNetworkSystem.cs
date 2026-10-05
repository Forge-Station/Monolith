using System.Linq;
using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._DV.Planet;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Parallax;
using Content.Server.Shuttles.Events;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos;
using Content.Shared.Parallax.Biomes;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Planets;

/// <summary>Builds and tears down planet z-map networks: ground, air, cloud and orbit layers.</summary>
public sealed partial class WFPlanetNetworkSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private CEZLevelsSystem _zLevels = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PlanetSystem _planet = default!;
    [Dependency] private TransformSystem _transform = default!;

    // Marks the planet centre on the orbit layer.
    private const string OrbitMarkerProto = "WFOrbitBeacon";

    private readonly List<(Vector2i Index, Tile Tile)> _reservedTiles = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // Broadcast, not directed: legal alongside the force-anchor, salvage and nukeops handlers.
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnConsoleFTLAttempt);
    }

    /// <summary>Refuses console FTL while a hull is riding a transit map between layers.</summary>
    private void OnConsoleFTLAttempt(ref ConsoleFTLAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (Transform(args.Uid).MapUid is not { } mapUid || !HasComp<CEZTransitMapComponent>(mapUid))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("wf-shuttle-console-in-transit");
    }

    /// <summary>Builds the network for a registered sector body and records it on the body.</summary>
    public bool TryBuildNetwork(Entity<WFSectorPlanetComponent> planet, out EntityUid network)
    {
        network = EntityUid.Invalid;

        if (planet.Comp.Surface is not { } surfaceId)
            return false;

        if (!_proto.TryIndex(surfaceId, out var surface))
        {
            Log.Error($"Sector body {ToPrettyString(planet.Owner)} names unknown surface \"{surfaceId}\".");
            return false;
        }

        if (planet.Comp.Network is { } already && TryGetEntity(already, out _))
        {
            Log.Warning($"Sector body {ToPrettyString(planet.Owner)} already has a planet network.");
            return false;
        }

        // World frame: SpawnAtPosition may have parented the body to a grid.
        var centre = _transform.GetWorldPosition(planet.Owner);
        var displayName = MetaData(planet.Owner).EntityName;

        if (BuildNetwork(surface, centre, displayName, planet.Owner) is not { } built)
            return false;

        network = built;
        planet.Comp.Network = GetNetEntity(built);
        planet.Comp.OrbitMap = GetNetEntity(Comp<WFPlanetNetworkComponent>(built).OrbitMap);
        Dirty(planet);
        return true;
    }

    /// <summary>Builds a planet z-stack at a world position; returns its network, or null on failure.</summary>
    public EntityUid? BuildNetwork(WFPlanetSurfacePrototype surface, Vector2 centre, string displayName, EntityUid? planetEntity)
    {
        if (!_cfg.GetCVar(PlanetCVars.PlanetNetworks))
        {
            Log.Warning($"Refused to build a planet network for \"{surface.ID}\": wf.planet_networks is off.");
            return null;
        }

        var groundProto = _proto.Index(surface.Ground);

        // Every map stays uninitialised until InitializeZNetwork, so the network registry lands on all of them at once.
        var ground = _planet.SpawnPlanet(surface.Ground, runMapInit: false);

        // Set the seed before any chunk loads; EnsurePlanet otherwise rolls a random one.
        if (surface.Seed is { } biomeSeed && TryComp<BiomeComponent>(ground, out var groundBiome))
            _biome.SetSeed(ground, groundBiome, biomeSeed);

        // Before the ground grid or any chunk loads, so marker layers other modules add keep a stable order.
        var spawned = new WFPlanetGroundSpawnedEvent(ground, surface);
        RaiseLocalEvent(ref spawned);

        if (surface.GroundGrid is { } gridPath)
        {
            var groundMapId = Comp<MapComponent>(ground).MapId;

            if (!_mapLoader.TryLoadGrid(groundMapId, gridPath, out var grid, offset: centre))
            {
                Log.Error($"Failed to load ground grid {gridPath} for planet surface \"{surface.ID}\".");
                Del(ground);
                return null;
            }

            // Don't let the biome plant rocks inside the hand-made base.
            _reservedTiles.Clear();
            var aabb = Comp<MapGridComponent>(grid.Value.Owner).LocalAABB.Translated(centre);
            _biome.ReserveTiles(ground, aabb.Enlarged(0.2f), _reservedTiles);
        }

        var layers = new List<EntityUid> { ground };

        for (var i = 0; i < surface.AirLayers; i++)
        {
            layers.Add(_map.CreateMap(out _, runMapInit: false));
        }

        var cloud = EntityUid.Invalid;
        if (surface.CloudLayer)
        {
            cloud = _map.CreateMap(out _, runMapInit: false);
            layers.Add(cloud);
        }

        var orbit = _map.CreateMap(out _, runMapInit: false);
        layers.Add(orbit);

        var lower = new List<EntityUid>();
        var lowerEv = new WFPlanetLowerLayersEvent(ground, surface, centre, lower);
        RaiseLocalEvent(ref lowerEv);

        var network = _zLevels.CreateMapNetwork(surface.NetworkComponents);
        _meta.SetEntityName(network, Loc.GetString("wf-planet-network-name", ("planet", displayName)));

        // One depth per call from 0 upward; the network's sorted cache needs contiguous depths starting at 0.
        for (var depth = 0; depth < layers.Count; depth++)
        {
            if (_zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int> { { layers[depth], depth } }))
                continue;

            Log.Error($"Failed to add depth {depth} to the planet network for \"{surface.ID}\"; unwinding.");
            UnwindBuild(network, layers, lower);
            return null;
        }

        // Below ground only once depth 0 exists: QuickApiCache indexes past its list if -1 arrives before 0.
        for (var i = 0; i < lower.Count; i++)
        {
            var depth = -(i + 1);
            if (_zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int> { { lower[i], depth } }))
                continue;

            Log.Error($"Failed to add depth {depth} to the planet network for \"{surface.ID}\"; unwinding.");
            UnwindBuild(network, layers, lower);
            return null;
        }

        _zLevels.InitializeZNetwork(network);

        // MapInit overwrote every layer with the network registry, so per-layer components go on after it.
        var networkNet = GetNetEntity(network);

        foreach (var layer in layers.Concat(lower))
        {
            var marker = EnsureComp<WFPlanetLayerComponent>(layer);
            marker.Network = networkNet;
            marker.Gravity = surface.Gravity;
            marker.MaxSpeed = surface.AirMaxSpeed;
            marker.LinearDamping = surface.AirDamping;
            Dirty(layer, marker);
        }

        WFPlanetAmbiencePrototype? ambience = null;
        foreach (var candidate in _proto.EnumeratePrototypes<WFPlanetAmbiencePrototype>())
        {
            if (candidate.PlanetType != surface.PlanetType)
                continue;

            ambience = candidate;
            break;
        }

        if (ambience != null)
        {
            var orbitDepth = layers.Count - 1;
            for (var depth = 0; depth < layers.Count; depth++)
            {
                var layerAmbience = EnsureComp<WFPlanetAmbienceComponent>(layers[depth]);
                layerAmbience.Profile = ambience.ID;
                layerAmbience.VolumeOffset = WFPlanetAmbience.LayerVolumeOffset(depth, orbitDepth);
                Dirty(layers[depth], layerAmbience);
            }
        }

        _atmos.SetMapAtmosphere(ground, false, groundProto.Atmosphere);

        if (surface.GroundComponents is { } groundComponents)
            EntityManager.AddComponents(ground, groundComponents, removeExisting: false);

        if (surface.AirComponents is { } airComponents)
        {
            for (var depth = 1; depth <= surface.AirLayers; depth++)
            {
                EntityManager.AddComponents(layers[depth], airComponents, removeExisting: false);
            }
        }

        if (surface.CloudLayer)
        {
            if (surface.CloudComponents is { } cloudComponents)
                EntityManager.AddComponents(cloud, cloudComponents, removeExisting: false);

            EnsureComp<CEZCloudLayerComponent>(cloud);
        }

        _atmos.SetMapAtmosphere(orbit, true, GasMixture.SpaceGas);
        RemComp<MapLightComponent>(orbit);

        if (surface.OrbitComponents is { } orbitComponents)
            EntityManager.AddComponents(orbit, orbitComponents, removeExisting: false);

        var orbitLayer = EnsureComp<WFOrbitLayerComponent>(orbit);
        orbitLayer.Planet = planetEntity is { } planetUid ? GetNetEntity(planetUid) : null;
        orbitLayer.Range = surface.OrbitRange;
        orbitLayer.MaxSpeed = surface.OrbitMaxSpeed;
        orbitLayer.LinearDamping = surface.OrbitDamping;
        orbitLayer.Network = networkNet;
        orbitLayer.RadarGround = GetNetEntity(ground);
        if (TryComp<BiomeComponent>(ground, out var radarBiome))
        {
            orbitLayer.RadarLayers = new(radarBiome.Layers);
            orbitLayer.RadarSeed = radarBiome.Seed;
        }
        Dirty(orbit, orbitLayer);

        _meta.SetEntityName(orbit, Loc.GetString(surface.OrbitMapName, ("planet", displayName)));

        // Not an FTL destination; orbit is entered from the console button. The marker is a warp point and radar label.
        var orbitMarker = SpawnAtPosition(OrbitMarkerProto, new EntityCoordinates(orbit, centre));
        _meta.SetEntityName(orbitMarker, Loc.GetString(surface.OrbitMarkerName, ("planet", displayName)));

        // A mapgrid on the orbit layer disables every arriving hull and blocks climbs into it.
        if (HasComp<MapGridComponent>(orbit))
            Log.Error($"The orbit layer of \"{surface.ID}\" has a MapGridComponent; arriving shuttles will be disabled and climbs will be blocked.");

        var comp = EnsureComp<WFPlanetNetworkComponent>(network);
        comp.Planet = planetEntity;
        comp.GroundMap = ground;
        comp.OrbitMap = orbit;
        comp.Layers = layers;
        comp.LowerLayers = lower;
        comp.Surface = surface.ID;
        comp.Centre = centre;
        EntityManager.System<WFPlanetWeatherSystem>().Configure(network, surface, displayName);

        var built = new WFPlanetNetworkBuiltEvent(network, ground, surface, lower);
        RaiseLocalEvent(ref built);

        return network.Owner;
    }

    /// <summary>Deletes a half-built network and every map spawned for it.</summary>
    private void UnwindBuild(EntityUid network, List<EntityUid> layers, List<EntityUid> lower)
    {
        _zLevels.DeleteMapNetwork(network);

        foreach (var layer in layers.Concat(lower))
        {
            QueueDel(layer);
        }
    }

    /// <summary>Deletes a network, its layers and the transit maps between them; clears the body's record.</summary>
    public void DeleteNetwork(EntityUid network)
    {
        if (TryComp<WFPlanetNetworkComponent>(network, out var comp))
        {
            if (comp.Planet is { } planetUid && TryComp<WFSectorPlanetComponent>(planetUid, out var sector))
            {
                sector.Network = null;
                sector.OrbitMap = null;

                if (!TerminatingOrDeleted(planetUid))
                    Dirty(planetUid, sector);
            }

            // CE only deletes the layers, so a hull caught mid-fall would outlive the planet on its transit map.
            var transits = EntityQueryEnumerator<CEZTransitMapComponent>();
            while (transits.MoveNext(out var uid, out var transit))
            {
                if (transit.LowerMap is { } lower && (comp.Layers.Contains(lower) || comp.LowerLayers.Contains(lower))
                    || transit.UpperMap is { } upper && (comp.Layers.Contains(upper) || comp.LowerLayers.Contains(upper)))
                {
                    QueueDel(uid);
                }
            }
        }

        _zLevels.DeleteMapNetwork(network);
    }
}
