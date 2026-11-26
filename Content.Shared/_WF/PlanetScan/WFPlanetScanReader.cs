using System.Numerics;
using Content.Shared._DV.Planet;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared._WF.PlanetMap;
using Content.Shared._WF.Planets;
using Content.Shared._WF.PlanetSignals;
using Content.Shared.Atmos;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetScan;

/// <summary>What a radar's planet scan reads off one sector body.</summary>
public sealed class WFPlanetScanReading
{
    public EntityUid Body;
    public MapId Map;
    public Vector2 Centre;
    /// <summary>The body's radius as the scope draws it, in metres.</summary>
    public float Radius;
    public string Name = string.Empty;
    /// <summary>Distance from the pilot to the body's limb, in metres; null when the pilot is elsewhere.</summary>
    public float? Range;
    /// <summary>Whether the body has a surface to land on; everything below is empty without one.</summary>
    public bool Surface;
    public bool Sanctioned = true;
    public LocId? Climate;
    public float Gravity;
    /// <summary>Surface air pressure in kPa and temperature in kelvin.</summary>
    public float Pressure;
    public float Temperature;
    public bool Breathable;
    public readonly List<LocId> Hazards = new();
    /// <summary>Whether the world's map has reached the client, so its sites and landing zone are known.</summary>
    public bool Surveyed;
    public int Sites;
    /// <summary>Known sites by kind, indexed by <see cref="WFPlanetMapSiteKind"/>.</summary>
    public readonly int[] SiteKinds = new int[WFPlanetScanReader.SiteKindCount];
    /// <summary>The Fluent names of the cargo depots built on the world.</summary>
    public readonly List<LocId> Depots = new();
    /// <summary>Radius of the clear ground round the world's centre, in metres; zero when none is kept.</summary>
    public float LandingRadius;
    public int Vessels;
    public int People;
}

/// <summary>Finds the sector body under a point on a radar and reads it for the planet scan. Runs on the client.</summary>
public sealed class WFPlanetScanReader
{
    /// <summary>Scope radius per Earth radius of the body, in metres.</summary>
    public const float MetresPerEarthRadius = 300f;

    /// <summary>Share of a world's orbit range its scope disc may fill: the gravity well's capture line.</summary>
    public const float LimbShare = 0.35f;

    /// <summary>Largest scope radius of a body without a Wolfgate surface, in metres.</summary>
    public const float MaxRadius = 1500f;

    public const int SiteKindCount = (int) WFPlanetMapSiteKind.Depot + 1;

    /// <summary>Partial pressures that make air unbreathable or toxic, in kPa.</summary>
    private const float MinOxygen = 16f;
    private const float MaxCarbonDioxide = 5f;
    private const float MaxToxic = 0.5f;

    /// <summary>Surface temperatures past which the scan warns, in kelvin.</summary>
    private const float Freezing = 243.15f;
    private const float Scorching = 323.15f;

    /// <summary>Surface gravity past which the scan warns, in gees.</summary>
    private const float HeavyGravity = 1.5f;

    private static readonly Gas[] ToxicGases = { Gas.Plasma, Gas.Tritium, Gas.Ammonia, Gas.Frezon, Gas.NitrousOxide };

    private readonly IEntityManager _entities;
    private readonly IPrototypeManager _prototypes;
    private readonly SharedMapSystem _maps;
    private readonly EntityQuery<StarSystemMapComponent> _systems;
    private readonly EntityQuery<TransformComponent> _xforms;
    private readonly List<(EntityUid Body, Planet Planet, float Radius)> _bodies = new();

    public WFPlanetScanReader(IEntityManager entities, IPrototypeManager prototypes)
    {
        _entities = entities;
        _prototypes = prototypes;
        _maps = entities.System<SharedMapSystem>();
        _systems = entities.GetEntityQuery<StarSystemMapComponent>();
        _xforms = entities.GetEntityQuery<TransformComponent>();
    }

    /// <summary>A body's radius on the scope: its real radius scaled up, short of where its gravity well captures hulls.</summary>
    public static float ScopeRadius(Planet planet, WFPlanetSurfacePrototype? surface)
    {
        var limit = surface != null ? surface.OrbitRange * LimbShare : MaxRadius;
        return MathF.Min(planet.Radius * MetresPerEarthRadius, limit);
    }

    /// <summary>
    /// The bodies of a map's star system with their scope radius. A body whose entity hasn't reached this side is left
    /// out.
    /// </summary>
    public IReadOnlyList<(EntityUid Body, Planet Planet, float Radius)> Bodies(MapId map)
    {
        _bodies.Clear();
        var mapUid = _maps.GetMapOrInvalid(map);
        if (!_systems.TryComp(mapUid, out var system) || system.StarSystem is not { } stars || stars.Planets.Count == 0)
            return _bodies;

        var query = _entities.AllEntityQueryEnumerator<IFFComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.ParentUid != mapUid || _entities.HasComponent<MapGridComponent>(uid))
                continue;

            foreach (var planet in stars.Planets)
            {
                if ((planet.Position - xform.LocalPosition).LengthSquared() >= 1f)
                    continue;

                _bodies.Add((uid, planet, ScopeRadius(planet, SurfaceOf(uid))));
                break;
            }
        }

        return _bodies;
    }

    /// <summary>
    /// The body under a point: the one whose disc, at least <paramref name="minRadius"/> metres across, the point is
    /// nearest inside of, within <paramref name="slack"/> metres of its limb.
    /// </summary>
    public bool TryFind(MapId map, Vector2 point, float slack, float minRadius, out EntityUid body)
    {
        body = default;
        var best = float.MaxValue;
        foreach (var (uid, planet, radius) in Bodies(map))
        {
            var gap = Vector2.Distance(planet.Position, point) - MathF.Max(radius, minRadius);
            if (gap > slack || gap >= best)
                continue;
            best = gap;
            body = uid;
        }

        return best < float.MaxValue;
    }

    /// <summary>Reads a body; <paramref name="pilot"/> is where the range is measured from, when on the body's map.</summary>
    public bool TryRead(EntityUid body, MapCoordinates? pilot, WFPlanetScanReading reading)
    {
        if (!_xforms.TryComp(body, out var xform) || !_systems.TryComp(xform.MapUid, out var system) ||
            system.StarSystem is not { } stars)
            return false;

        Planet? planet = null;
        foreach (var candidate in stars.Planets)
        {
            if ((candidate.Position - xform.LocalPosition).LengthSquared() < 1f)
                planet = candidate;
        }

        if (planet == null)
            return false;

        var surface = SurfaceOf(body);
        reading.Body = body;
        reading.Map = xform.MapID;
        reading.Centre = planet.Position;
        reading.Radius = ScopeRadius(planet, surface);
        reading.Name = _entities.GetComponent<MetaDataComponent>(body).EntityName;
        reading.Range = pilot is { } from && from.MapId == xform.MapID
            ? MathF.Max(0f, Vector2.Distance(from.Position, planet.Position) - reading.Radius)
            : null;
        reading.Surface = surface != null;
        reading.Sanctioned = !_entities.TryGetComponent<WFSectorPlanetComponent>(body, out var sector) || sector.Sanctioned;
        reading.Climate = surface?.Climate;
        reading.Gravity = surface?.Gravity ?? 0f;
        reading.Pressure = 0f;
        reading.Temperature = 0f;
        reading.Breathable = false;
        reading.Hazards.Clear();
        reading.Surveyed = false;
        reading.Sites = 0;
        for (var i = 0; i < SiteKindCount; i++)
        {
            reading.SiteKinds[i] = 0;
        }
        reading.Depots.Clear();
        reading.LandingRadius = 0f;
        reading.Vessels = 0;
        reading.People = 0;

        if (_entities.TryGetComponent<WFPlanetSignalsComponent>(body, out var signals))
        {
            reading.People = signals.People;
            foreach (var signal in signals.Signals)
            {
                if (signal.Kind == WFPlanetSignalKind.Vessel)
                    reading.Vessels++;
            }
        }

        if (surface == null)
            return true;

        ReadAir(surface, reading);
        if (!reading.Sanctioned)
            reading.Hazards.Add("wf-planet-scan-hazard-unsanctioned");
        reading.Hazards.AddRange(surface.Hazards);

        if (sector?.OrbitMap is { } orbitNet && _entities.TryGetEntity(orbitNet, out var orbit) &&
            _entities.TryGetComponent<WFPlanetMapComponent>(orbit, out var map))
        {
            reading.Surveyed = true;
            reading.LandingRadius = map.LandingRadius;
            foreach (var site in map.Sites)
            {
                reading.Sites++;
                var kind = (int) site.Kind;
                if (kind >= 0 && kind < SiteKindCount)
                    reading.SiteKinds[kind]++;
                if (site.Kind == WFPlanetMapSiteKind.Depot && site.Name.Length > 0)
                    reading.Depots.Add(site.Name);
            }
        }

        return true;
    }

    /// <summary>The ground's air and heat, with the hazards they give away, and heavy gravity.</summary>
    private void ReadAir(WFPlanetSurfacePrototype surface, WFPlanetScanReading reading)
    {
        if (_prototypes.TryIndex(surface.Ground, out PlanetPrototype? ground))
        {
            var air = ground.Atmosphere;
            reading.Temperature = air.Temperature;
            reading.Pressure = air.Volume > 0f ? air.Pressure : 0f;

            var total = air.TotalMoles;
            float Partial(Gas gas) => total > 0f ? reading.Pressure * air.GetMoles(gas) / total : 0f;

            var toxic = false;
            foreach (var gas in ToxicGases)
            {
                toxic |= Partial(gas) > MaxToxic;
            }

            reading.Breathable = Partial(Gas.Oxygen) >= MinOxygen && Partial(Gas.CarbonDioxide) <= MaxCarbonDioxide && !toxic;
            if (toxic)
                reading.Hazards.Add("wf-planet-scan-hazard-toxic");
            else if (!reading.Breathable)
                reading.Hazards.Add("wf-planet-scan-hazard-air");

            if (reading.Temperature < Freezing)
                reading.Hazards.Add("wf-planet-scan-hazard-cold");
            else if (reading.Temperature > Scorching)
                reading.Hazards.Add("wf-planet-scan-hazard-heat");
        }

        if (surface.Gravity >= HeavyGravity)
            reading.Hazards.Add("wf-planet-scan-hazard-gravity");
    }

    private WFPlanetSurfacePrototype? SurfaceOf(EntityUid body)
    {
        return _entities.TryGetComponent<WFSectorPlanetComponent>(body, out var sector) && sector.Surface is { } id &&
            _prototypes.TryIndex(id, out var surface)
            ? surface
            : null;
    }
}
