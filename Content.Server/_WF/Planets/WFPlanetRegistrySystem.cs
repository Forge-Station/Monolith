using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Planets;

/// <summary>Registers sector bodies that have a Wolfgate surface and builds round-start networks.</summary>
public sealed partial class WFPlanetRegistrySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private WFPlanetNetworkSystem _networks = default!;

    private readonly Dictionary<ProtoId<PlanetTypePrototype>, WFPlanetSurfacePrototype> _surfaces = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        foreach (var surface in _proto.EnumeratePrototypes<WFPlanetSurfacePrototype>())
        {
            if (_surfaces.TryGetValue(surface.PlanetType, out var existing))
            {
                Log.Warning($"Planet type \"{surface.PlanetType}\" has two surfaces, \"{existing.ID}\" and \"{surface.ID}\"; keeping the first.");
                continue;
            }

            _surfaces[surface.PlanetType] = surface;
        }

        SubscribeLocalEvent<WFSectorPlanetComponent, ComponentShutdown>(OnSectorPlanetShutdown);
    }

    /// <summary>Registers a freshly spawned sector body and builds its network if the surface asks for it.</summary>
    public void RegisterPlanet(Entity<StarSystemMapComponent> map, EntityUid planetEntity, Planet planet)
    {
        if (!_cfg.GetCVar(PlanetCVars.PlanetNetworks))
            return;

        if (map.Comp.System is not { } systemId || !_proto.TryIndex(systemId, out var systemProto))
            return;

        // The body has no prototype id; match its entry by recomputing the builder's exact spawn position.
        StarSystemPlanet? entry = null;

        foreach (var candidate in systemProto.Planets)
        {
            var position = new Vector2(MathF.Cos(candidate.Angle), MathF.Sin(candidate.Angle)) * candidate.Distance;

            if (position != planet.Position)
                continue;

            entry = candidate;
            break;
        }

        if (entry is null)
        {
            foreach (var candidate in systemProto.Planets)
            {
                if (!_proto.TryIndex(candidate.Planet, out var typeProto) || typeProto.Name != planet.Name)
                    continue;

                entry = candidate;
                Log.Warning($"Sector body {ToPrettyString(planetEntity)} matched no {systemId} entry by position; matched \"{planet.Name}\" by name instead.");
                break;
            }
        }

        if (entry is null)
        {
            Log.Error($"Sector body {ToPrettyString(planetEntity)} matches no planet entry of star system \"{systemId}\".");
            return;
        }

        if (!_surfaces.TryGetValue(entry.Planet, out var surface))
            return;

        var body = ApplySurface(planetEntity, surface);

        if (surface.BuildAtRoundStart)
            _networks.TryBuildNetwork(body, out _);
    }

    /// <summary>Copies a surface's id and sanctioned flag onto a sector body.</summary>
    public Entity<WFSectorPlanetComponent> ApplySurface(EntityUid body, WFPlanetSurfacePrototype surface)
    {
        var comp = EnsureComp<WFSectorPlanetComponent>(body);
        comp.Surface = surface.ID;
        comp.Sanctioned = surface.Sanctioned;
        Dirty(body, comp);

        return (body, comp);
    }

    /// <summary>Every registered sector body this round.</summary>
    public List<Entity<WFSectorPlanetComponent>> GetPlanets()
    {
        var planets = new List<Entity<WFSectorPlanetComponent>>();
        var query = AllEntityQuery<WFSectorPlanetComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            planets.Add((uid, comp));
        }

        return planets;
    }

    /// <summary>Finds a registered sector body by its display name, case-insensitively.</summary>
    public bool TryGetPlanetByName(string name, out Entity<WFSectorPlanetComponent> planet)
    {
        var query = AllEntityQuery<WFSectorPlanetComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (!string.Equals(MetaData(uid).EntityName, name, StringComparison.OrdinalIgnoreCase))
                continue;

            planet = (uid, comp);
            return true;
        }

        planet = default;
        return false;
    }

    /// <summary>Finds the surface definition registered for a sector body type, if there is one.</summary>
    public bool TryGetSurface(ProtoId<PlanetTypePrototype> planetType, [NotNullWhen(true)] out WFPlanetSurfacePrototype? surface)
    {
        return _surfaces.TryGetValue(planetType, out surface);
    }

    /// <summary>Deletes the body's network along with it.</summary>
    private void OnSectorPlanetShutdown(Entity<WFSectorPlanetComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Network is not { } network || !TryGetEntity(network, out var networkUid))
            return;

        _networks.DeleteNetwork(networkUid.Value);
    }
}
