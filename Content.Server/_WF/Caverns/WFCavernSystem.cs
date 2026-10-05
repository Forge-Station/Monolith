using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._DV.Planet;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Atmosphere;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Parallax;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Light.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Parallax;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Caverns;

/// <summary>Puts a cavern below every planet whose surface has one, and fits it out once the network is built.</summary>
public sealed partial class WFCavernSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private PlanetSystem _planet = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCavernMouthSystem _mouths = default!;

    /// <summary>How often each cavern takes the ground's clock and soundscape.</summary>
    private static readonly TimeSpan MirrorInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextMirror;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFPlanetLowerLayersEvent>(OnLowerLayers);
        SubscribeLocalEvent<WFPlanetNetworkBuiltEvent>(OnNetworkBuilt);
        SubscribeLocalEvent<WFTerrainOpenTilesEvent>(OnTerrainOpenTiles);
        SubscribeLocalEvent<WFPlanetWildlifeComponent, CEZLevelFallMapEvent>(OnWildlifeFell);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextMirror)
            return;

        _nextMirror = _timing.CurTime + MirrorInterval;

        var caverns = EntityQueryEnumerator<WFCavernLayerComponent>();
        while (caverns.MoveNext(out var uid, out var layer))
        {
            if (!TerminatingOrDeleted(layer.Ground) && _proto.TryIndex(layer.Cavern, out var cavern))
                Mirror(uid, layer.Ground, cavern);
        }
    }

    /// <summary>
    /// Gives a cavern the ground's clock, with the weather read as underground, and its soundscape: the cavern's own
    /// when it names one, otherwise the ground's day and night ambience, quieter and muffled through the rock.
    /// </summary>
    private void Mirror(EntityUid map, EntityUid ground, WFCavernPrototype cavern)
    {
        if (TryComp<WFPlanetEnvironmentComponent>(ground, out var above))
        {
            var environment = EnsureComp<WFPlanetEnvironmentComponent>(map);
            var weather = Loc.GetString("wf-cavern-weather-underground");

            if (environment.PlanetName != above.PlanetName || environment.MinuteOfDay != above.MinuteOfDay || environment.Weather != weather)
            {
                environment.PlanetName = above.PlanetName;
                environment.MinuteOfDay = above.MinuteOfDay;
                environment.Weather = weather;
                Dirty(map, environment);
            }
        }

        ProtoId<WFPlanetAmbiencePrototype> profile;
        float volume = 0f, occlusion = 0f;

        if (cavern.Ambience is { } own)
        {
            profile = own;
        }
        else if (TryComp<WFPlanetAmbienceComponent>(ground, out var surface))
        {
            profile = surface.Profile;
            volume = surface.VolumeOffset + cavern.SurfaceAmbienceVolume;
            occlusion = cavern.SurfaceAmbienceOcclusion;
        }
        else
        {
            RemComp<WFPlanetAmbienceComponent>(map);
            return;
        }

        var ambience = EnsureComp<WFPlanetAmbienceComponent>(map);
        if (ambience.Profile == profile && ambience.VolumeOffset.Equals(volume) && ambience.Occlusion.Equals(occlusion))
            return;

        ambience.Profile = profile;
        ambience.VolumeOffset = volume;
        ambience.Occlusion = occlusion;
        Dirty(map, ambience);
    }

    /// <summary>The cavern under a surface, if it has one.</summary>
    public bool TryGetCavern(ProtoId<WFPlanetSurfacePrototype> surface, [NotNullWhen(true)] out WFCavernPrototype? cavern)
    {
        foreach (var candidate in _proto.EnumeratePrototypes<WFCavernPrototype>())
        {
            if (candidate.Surface != surface)
                continue;

            cavern = candidate;
            return true;
        }

        cavern = null;
        return false;
    }

    /// <summary>Spawns the cavern map, uninitialised, as depth -1.</summary>
    private void OnLowerLayers(ref WFPlanetLowerLayersEvent args)
    {
        if (!_cfg.GetCVar(CavernCVars.Caverns) || !TryGetCavern(args.Surface.ID, out var cavern))
            return;

        var level = _planet.SpawnPlanet(cavern.Level, runMapInit: false);

        // Before any chunk loads, so the same world always has the same cavern.
        if (TryComp<BiomeComponent>(level, out var biome))
        {
            var surfaceSeed = args.Surface.Seed ?? CompOrNull<BiomeComponent>(args.Ground)?.Seed ?? 0;
            _biome.SetSeed(level, biome, unchecked(surfaceSeed + cavern.SeedOffset));
        }

        var layer = EnsureComp<WFCavernLayerComponent>(level);
        layer.Cavern = cavern.ID;
        layer.Ground = args.Ground;
        Dirty(level, layer);

        args.Lower.Add(level);
    }

    /// <summary>The pad under a mouth is bare ground to atmos, like the rest of the cavern floor.</summary>
    private void OnTerrainOpenTiles(ref WFTerrainOpenTilesEvent args)
    {
        if (TryComp<WFCavernLayerComponent>(args.Map, out var layer) && _proto.TryIndex(layer.Cavern, out var cavern))
            args.Tiles.Add(cavern.Mouths.LandingTile);
    }

    /// <summary>Gives each cavern its own air, takes away the sky, links the ground to it and cuts the gate.</summary>
    private void OnNetworkBuilt(ref WFPlanetNetworkBuiltEvent args)
    {
        var centre = CompOrNull<WFPlanetNetworkComponent>(args.Network)?.Centre ?? Vector2.Zero;

        foreach (var map in args.Lower)
        {
            if (!TryComp<WFCavernLayerComponent>(map, out var layer) || !_proto.TryIndex(layer.Cavern, out var cavern))
                continue;

            // InitializeZNetwork stamped the surface's network air over every member.
            _atmos.SetMapAtmosphere(map, false, _proto.Index(cavern.Level).Atmosphere);

            RemComp<LightCycleComponent>(map);
            RemComp<SunShadowComponent>(map);
            RemComp<SunShadowCycleComponent>(map);
            RemComp<ParallaxComponent>(map);

            var roof = EnsureComp<RoofComponent>(map);
            roof.Color = cavern.RoofColor;
            Dirty(map, roof);

            var ground = EnsureComp<WFCavernGroundComponent>(args.Ground);
            ground.Cavern = map;
            ground.Prototype = cavern.ID;
            ground.Centre = centre;

            _mouths.ClaimGate((args.Ground, ground));
        }
    }

    /// <summary>Deletes surface wildlife dropped into a cavern by an unloaded ground chunk, where it would idle forever as Protected.</summary>
    private void OnWildlifeFell(Entity<WFPlanetWildlifeComponent> ent, ref CEZLevelFallMapEvent args)
    {
        var xform = Transform(ent);

        if (!TryComp<WFCavernLayerComponent>(xform.MapUid, out var layer)
            || !TryComp<BiomeComponent>(layer.Ground, out var biome)
            || !TryComp<MapGridComponent>(layer.Ground, out var grid)
            || TryComp<MindContainerComponent>(ent, out var mind) && mind.HasMind)
            return;

        var index = _map.WorldToTile(layer.Ground, grid, _transform.GetWorldPosition(xform));

        // Solid ground above, a pinned hole or a loaded chunk: it fell through a real opening.
        if (_map.TryGetTileRef(layer.Ground, grid, index, out var tile) && !tile.Tile.IsEmpty
            || _biome.WfIsPinned((layer.Ground, biome), index)
            || _biome.WfIsChunkLoaded((layer.Ground, biome), index))
            return;

        // Raised mid z-physics pass, so the delete waits for the end of the tick.
        QueueDel(ent);
    }
}
