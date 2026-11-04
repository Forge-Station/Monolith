using System.Linq;
using Content.Server._NF.Trade;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Station.Systems;
using Content.Shared._NF.CCVar;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCargoHubs;
using Content.Shared._WF.PlanetCities;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetCargoHubs;

/// <summary>
/// Moves cargo depots A and B onto planets some rounds: rolls which depots go down and onto which worlds, adds the hub
/// site to that world's sites, makes the depot's station over the ground once it is built, and tells the space depot
/// spawner to skip that letter.
/// </summary>
public sealed partial class WFPlanetCargoHubSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private WFPlanetPoiSystem _pois = default!;

    /// <summary>Depots that may go planetside: A and B.</summary>
    public const int MaxHubs = 2;

    /// <summary>The space depot's game map and station, whose station config the hub's station copies.</summary>
    private const string DepotGameMap = "CargoDepot";

    /// <summary>This round's planetside depots by index: the world and the hub layout.</summary>
    private readonly Dictionary<int, (ProtoId<WFPlanetSurfacePrototype> Surface, ProtoId<WFPlanetCargoHubPrototype> Hub)> _plan = new();

    private bool _rolled;

    /// <summary>Set once the space depots are spawned: no hub is planned or built after.</summary>
    private bool _closed;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlanetPoiExtraSitesEvent>(OnExtraSites);
        SubscribeLocalEvent<WFPlanetPreloadedEvent>(OnPreloaded);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<WFPlanetCargoHubsComponent, ComponentShutdown>(OnShutdown);
        InitializeNotice();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev) => ResetPlan();

    /// <summary>Forgets this round's plan; the next world to place its sites rolls again.</summary>
    public void ResetPlan()
    {
        _plan.Clear();
        _rolled = false;
        _closed = false;
    }

    /// <summary>A world's hub stations go with it.</summary>
    private void OnShutdown(Entity<WFPlanetCargoHubsComponent> ent, ref ComponentShutdown args)
    {
        foreach (var station in ent.Comp.Stations)
        {
            if (!TerminatingOrDeleted(station))
                QueueDel(station);
        }
    }

    /// <summary>
    /// Called by the space depot spawner for each depot it would spawn; closes this round's planning. True when that
    /// depot already stands on a planet, so it gets no space station.
    /// </summary>
    public bool IsPlanetside(int index)
    {
        _closed = true;
        return HubStation(index) != null;
    }

    /// <summary>The station of planetside depot <paramref name="index"/>, if it has been built.</summary>
    public EntityUid? HubStation(int index)
    {
        var query = EntityQueryEnumerator<WFPlanetCargoHubComponent>();
        while (query.MoveNext(out var uid, out var hub))
        {
            if (hub.Index == index && !TerminatingOrDeleted(uid))
                return uid;
        }

        return null;
    }

    /// <summary>This round's planned hubs, by depot index.</summary>
    public IReadOnlyDictionary<int, (ProtoId<WFPlanetSurfacePrototype> Surface, ProtoId<WFPlanetCargoHubPrototype> Hub)> Plan => _plan;

    /// <summary>The site name of depot <paramref name="index"/>'s hub.</summary>
    public static LocId SiteName(int index) => WFPlanetCargoHubSites.SiteName(index);

    /// <summary>
    /// Rolls each depot's chance to go planetside and picks it a world among those built so far: one that allows the
    /// hub, has roads and no fixed city, and has no other hub.
    /// </summary>
    private void Roll()
    {
        _rolled = true;
        var hubs = _proto.EnumeratePrototypes<WFPlanetCargoHubPrototype>().OrderBy(hub => hub.ID).ToList();
        if (hubs.Count == 0)
            return;

        var worlds = new List<ProtoId<WFPlanetSurfacePrototype>>();
        var networks = EntityQueryEnumerator<WFPlanetNetworkComponent>();
        while (networks.MoveNext(out _, out var network))
        {
            if (!worlds.Contains(network.Surface) && Suitable(network.Surface))
                worlds.Add(network.Surface);
        }

        worlds.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        var chance = _cfg.GetCVar(PlanetCargoHubCVars.Chance);
        var count = Math.Min(MaxHubs, _cfg.GetCVar(NFCCVars.CargoDepots));
        for (var index = 0; index < count; index++)
        {
            if (!_random.Prob(Math.Clamp(chance, 0f, 1f)))
                continue;

            TryPlan(index, hubs, worlds);
        }

        // At least one depot goes down whenever a world can take it; a chance of 0 turns hubs off.
        if (_plan.Count == 0 && chance > 0f && count > 0)
        {
            var order = Enumerable.Range(0, count).ToList();
            _random.Shuffle(order);
            foreach (var index in order)
            {
                if (TryPlan(index, hubs, worlds))
                    break;
            }
        }
    }

    /// <summary>Plans depot <paramref name="index"/> on a random world that suits its hub and has no other hub.</summary>
    private bool TryPlan(int index, List<WFPlanetCargoHubPrototype> hubs, List<ProtoId<WFPlanetSurfacePrototype>> worlds)
    {
        var hub = hubs[index % hubs.Count];
        var candidates = worlds.Where(world => hub.Surfaces.Contains(world) && _plan.Values.All(planned => planned.Surface != world)).ToList();
        if (candidates.Count == 0)
            return false;

        var world = _random.Pick(candidates);
        _plan[index] = (world, hub.ID);
        Log.Info($"Cargo depot {(char) ('A' + index)} is built on {world} this round ({hub.ID}).");
        return true;
    }

    /// <summary>A world with roads, whose ground no fixed city's station already holds.</summary>
    private bool Suitable(ProtoId<WFPlanetSurfacePrototype> surface)
    {
        if (!_proto.TryIndex(surface, out var proto) || !proto.Preload
            || proto.Pois is not { } pois || !_proto.TryIndex(pois, out var table) || table.Roads == null)
            return false;

        return !_cfg.GetCVar(PlanetCVars.FixedCities)
               || _proto.EnumeratePrototypes<WFFixedCityPrototype>().All(city => city.Surface != surface);
    }

    private void OnExtraSites(ref WFPlanetPoiExtraSitesEvent args)
    {
        if (_closed)
            return;

        if (!_rolled)
            Roll();

        foreach (var (index, (surface, hubId)) in _plan.OrderBy(pair => pair.Key))
        {
            if (surface != args.Surface || HubStation(index) != null || !_proto.TryIndex(hubId, out var hub))
                continue;

            var site = hub.Site;
            args.Sites.Add(new WFPlanetPoiEntry
            {
                MapPath = site.MapPath,
                Footprint = site.Footprint,
                Connection = site.Connection,
                Sockets = site.Sockets,
                Name = SiteName(index),
                Expeditions = false,
                Difficulty = site.Difficulty,
                MinCount = 1,
                MaxCount = 1,
                Priority = true,
            });
        }
    }

    /// <summary>Makes a depot's station over the world's ground for each hub placed on it, and shows the hub from orbit.</summary>
    private void OnPreloaded(ref WFPlanetPreloadedEvent args)
    {
        if (!TryComp<WFPlanetPoiComponent>(args.Ground, out var record))
            return;

        foreach (var site in record.Sites)
        {
            if (!TryIndex(site.Name, out var index) || HubStation(index) != null)
                continue;

            if (_closed)
            {
                Log.Warning($"{site.Name} was built on {ToPrettyString(args.Ground)} after the space depots were spawned; it gets no station.");
                continue;
            }

            MakeStation(args.Ground, site, index);
        }
    }

    private static bool TryIndex(LocId name, out int index)
    {
        return WFPlanetCargoHubSites.TryIndex(name.Id, out index) && index < MaxHubs;
    }

    private void MakeStation(EntityUid ground, WFPlanetPoiSite site, int index)
    {
        if (!_proto.TryIndex<GameMapPrototype>(DepotGameMap, out var gameMap)
            || !gameMap.Stations.TryGetValue(DepotGameMap, out var config))
        {
            Log.Error($"No {DepotGameMap} game map station to make the planetside depot's station from.");
            return;
        }

        var letter = (char) ('A' + index);
        // Station setup names its grids after the station; the world's ground keeps its own name.
        var groundName = MetaData(ground).EntityName;
        var station = _station.InitializeNewStation(config, new[] { ground },
            Loc.GetString("wf-planet-cargo-hub-station", ("letter", letter)));
        _meta.SetEntityName(ground, groundName);

        if (TryComp<TradeCrateDestinationComponent>(station, out var destination))
            destination.DestinationProto = $"Cargo{letter}";

        var hub = EnsureComp<WFPlanetCargoHubComponent>(station);
        hub.Index = index;
        hub.Ground = ground;
        hub.Origin = site.Origin;
        hub.World = groundName;
        EnsureComp<WFPlanetCargoHubsComponent>(ground).Stations.Add(station);

        // A depot is somewhere people are meant to find: it shows from orbit like a city.
        if (TryComp<WFPoiSignalComponent>(site.Signal, out var signal) && !signal.Revealed)
            _pois.Reveal((site.Signal, signal), null);

        Log.Info($"Cargo depot {letter} stands on {ToPrettyString(ground)} at {site.Origin}.");
        OnHubMade((station, hub));
    }
}
