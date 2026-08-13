using System.Numerics;
using System.Linq;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Forge.Bss;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Forge.Bss;

/// <summary>
/// Resolves runtime gate grids against declarative BSS networks and starts gate-limited FTL travel.
/// </summary>
public sealed class BssGateSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

#if DEBUG
    private EntityUid? _debugSourceGate;
    private EntityUid? _debugDestinationGate;
    private MapId? _debugDestinationMap;
#endif

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BssGateComponent, ComponentStartup>(OnGateChanged);
        SubscribeLocalEvent<BssGateComponent, ComponentShutdown>(OnGateChanged);
    }

    private void OnGateChanged<T>(Entity<BssGateComponent> ent, ref T args)
    {
        EntityManager.System<ShuttleConsoleSystem>().RefreshShuttleConsoles();
    }

    public BssSystemMapState GetSystemMap(EntityUid shuttle)
    {
        if (!TryGetSectorForMap(shuttle, out var networkId, out var currentSector) ||
            !_prototypes.TryIndex(networkId, out var network))
        {
            return BssSystemMapState.Empty("shuttle-console-bss-no-network");
        }

        var nodes = new List<BssSystemMapNode>(network.Sectors.Count);
        foreach (var sector in network.Sectors)
        {
            nodes.Add(new BssSystemMapNode(
                sector.Id,
                sector.Name,
                sector.Position,
                sector.Id == currentSector,
                IsAdjacent(network, currentSector, sector.Id),
                IsSectorOnline(networkId, sector.Id),
                sector.Color));
        }

        var edges = new List<BssSystemMapEdge>(network.Links.Count);
        foreach (var link in network.Links)
        {
            edges.Add(new BssSystemMapEdge(
                link.From.Sector,
                link.To.Sector,
                link.Bidirectional,
                TryFindGate(networkId, link.From, out _) &&
                TryFindGate(networkId, link.To, out _)));
        }

        return new BssSystemMapState(networkId.Id, currentSector, nodes, edges);
    }

    public List<BssGateRadarState> GetRadarGates(EntityUid? mapUid)
    {
        var result = new List<BssGateRadarState>();
        if (mapUid == null)
            return result;

        var query = AllEntityQuery<BssGateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var gate, out var xform))
        {
            if (!gate.Enabled || xform.MapUid != mapUid)
                continue;

            result.Add(new BssGateRadarState(
                GetGridWorldCenter(uid),
                gate.JumpRange,
                gate.Sector));
        }

        return result;
    }

    public bool TryJump(EntityUid shuttle, string destinationSector, out string reason)
    {
        reason = "shuttle-console-bss-unavailable";

        if (!TryComp(shuttle, out ShuttleComponent? shuttleComponent) ||
            !shuttleComponent.Enabled ||
            !_shuttle.CanFTL(shuttle, out _))
        {
            return false;
        }

        if (!TryFindNearbyGate(shuttle, destinationSector, out _, out var sourceGate) ||
            !_prototypes.TryIndex(sourceGate.Network, out var network))
        {
            reason = "shuttle-console-bss-not-near-gate";
            return false;
        }

        if (!TryResolveDestination(network, sourceGate, destinationSector, out var destination) ||
            !TryFindGate(sourceGate.Network, destination, out var destinationUid) ||
            !TryComp(destinationUid, out BssGateComponent? destinationGate) ||
            !destinationGate.Enabled)
        {
            reason = "shuttle-console-bss-route-offline";
            return false;
        }

        var destinationXform = Transform(destinationUid);
        if (destinationXform.MapUid is not { Valid: true } destinationMap)
        {
            return false;
        }

        if (!_maps.IsInitialized(destinationXform.MapID))
            _maps.InitializeMap(destinationXform.MapID);
        if (_maps.IsPaused(destinationXform.MapID))
            _maps.SetPaused(destinationXform.MapID, false);

        var destinationPosition =
            _transform.GetWorldPosition(destinationXform) +
            _transform.GetWorldRotation(destinationUid).RotateVec(destinationGate.ArrivalOffset);
        var target = new EntityCoordinates(destinationMap, destinationPosition);
        var angle = _transform.GetWorldRotation(shuttle);

        _shuttle.FTLToCoordinates(shuttle, shuttleComponent, target, angle);
        reason = string.Empty;
        return true;
    }

#if DEBUG
    public bool SetupDebugRoute(EntityUid shuttle, out string result)
    {
        var shuttleXform = Transform(shuttle);
        if (shuttleXform.MapUid is not { Valid: true })
        {
            result = "The selected entity is not on an initialized map.";
            return false;
        }

        if (_debugSourceGate is { } oldSource)
            QueueDel(oldSource);
        if (_debugDestinationGate is { } oldDestination)
            QueueDel(oldDestination);
        if (_debugDestinationMap is { } oldMap && _maps.MapExists(oldMap))
            _maps.DeleteMap(oldMap);

        var sourcePosition = GetGridWorldCenter(shuttle) + new Vector2(60f, 0f);
        _debugSourceGate = Spawn(
            "AnomalyCoreBluespace",
            new MapCoordinates(sourcePosition, shuttleXform.MapID));

        var destinationMapUid = _maps.CreateMap(out var destinationMap);
        _debugDestinationMap = destinationMap;
        _debugDestinationGate = Spawn(
            "AnomalyCoreBluespace",
            new EntityCoordinates(destinationMapUid, Vector2.Zero));

        ConfigureDebugGate(_debugSourceGate.Value, "central", "central-frontier");
        ConfigureDebugGate(_debugDestinationGate.Value, "frontier", "frontier-central");
        EntityManager.System<ShuttleConsoleSystem>().RefreshShuttleConsoles();

        result =
            $"Created source gate {_debugSourceGate.Value} 60 metres east of shuttle {shuttle} " +
            $"and destination gate {_debugDestinationGate.Value} on map {destinationMap}.";
        return true;
    }

    private void ConfigureDebugGate(EntityUid uid, string sector, string gateId)
    {
        var gate = EnsureComp<BssGateComponent>(uid);
        gate.Network = "ForgeExampleBssNetwork";
        gate.Sector = sector;
        gate.Gate = gateId;
        gate.JumpRange = 45f;
        gate.ArrivalOffset = new Vector2(35f, 0f);
        gate.Enabled = true;
        Dirty(uid, gate);
    }
#endif

    private bool TryGetSectorForMap(
        EntityUid shuttle,
        out ProtoId<BssNetworkPrototype> network,
        out string sector)
    {
        network = default;
        sector = string.Empty;
        var mapUid = Transform(shuttle).MapUid;
        if (mapUid == null)
            return false;

        if (TryComp(mapUid.Value, out BssSectorMapComponent? sectorMap) &&
            !string.IsNullOrEmpty(sectorMap.Sector))
        {
            network = sectorMap.Network;
            sector = sectorMap.Sector;
            return true;
        }

        var query = AllEntityQuery<BssGateComponent, TransformComponent>();
        while (query.MoveNext(out _, out var gate, out var xform))
        {
            if (!gate.Enabled || xform.MapUid != mapUid)
                continue;

            network = gate.Network;
            sector = gate.Sector;
            return true;
        }

        return false;
    }

    private bool TryFindNearbyGate(
        EntityUid shuttle,
        string? destinationSector,
        out EntityUid gateUid,
        out BssGateComponent gate)
    {
        gateUid = default;
        gate = default!;
        var shuttleXform = Transform(shuttle);
        if (shuttleXform.MapUid == null)
            return false;

        var shuttlePosition = GetGridWorldCenter(shuttle);
        var bestDistance = float.MaxValue;
        var query = AllEntityQuery<BssGateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var candidate, out var xform))
        {
            if (!candidate.Enabled || xform.MapUid != shuttleXform.MapUid)
                continue;

            var distance = Vector2.Distance(shuttlePosition, GetGridWorldCenter(uid));
            if (distance > candidate.JumpRange || distance >= bestDistance)
                continue;

            if (destinationSector != null)
            {
                if (!_prototypes.TryIndex(candidate.Network, out var network) ||
                    !TryResolveDestination(network, candidate, destinationSector, out _))
                {
                    continue;
                }
            }

            bestDistance = distance;
            gateUid = uid;
            gate = candidate;
        }

        return gateUid.IsValid();
    }

    private Vector2 GetGridWorldCenter(EntityUid gridUid)
    {
        if (!TryComp(gridUid, out MapGridComponent? grid))
            return _transform.GetWorldPosition(gridUid);

        return _transform.ToMapCoordinates(
            new EntityCoordinates(gridUid, grid.LocalAABB.Center)).Position;
    }

    private bool TryFindGate(
        ProtoId<BssNetworkPrototype> network,
        BssGateEndpoint endpoint,
        out EntityUid uid)
    {
        var query = AllEntityQuery<BssGateComponent>();
        while (query.MoveNext(out var candidateUid, out var gate))
        {
            if (gate.Enabled &&
                gate.Network == network &&
                gate.Sector == endpoint.Sector &&
                gate.Gate == endpoint.Gate)
            {
                uid = candidateUid;
                return true;
            }
        }

        uid = default;
        return false;
    }

    private bool IsSectorOnline(ProtoId<BssNetworkPrototype> network, string sector)
    {
        var query = AllEntityQuery<BssGateComponent>();
        while (query.MoveNext(out _, out var gate))
        {
            if (gate.Enabled && gate.Network == network && gate.Sector == sector)
                return true;
        }

        return false;
    }

    private static bool IsAdjacent(BssNetworkPrototype network, string current, string destination)
    {
        if (current == destination)
            return false;

        return network.Links.Any(link =>
            link.From.Sector == current && link.To.Sector == destination ||
            link.Bidirectional && link.To.Sector == current && link.From.Sector == destination);
    }

    private static bool TryResolveDestination(
        BssNetworkPrototype network,
        BssGateComponent source,
        string destinationSector,
        out BssGateEndpoint destination)
    {
        foreach (var link in network.Links)
        {
            if (link.From.Sector == source.Sector &&
                link.From.Gate == source.Gate &&
                link.To.Sector == destinationSector)
            {
                destination = link.To;
                return true;
            }

            if (link.Bidirectional &&
                link.To.Sector == source.Sector &&
                link.To.Gate == source.Gate &&
                link.From.Sector == destinationSector)
            {
                destination = link.From;
                return true;
            }
        }

        destination = default!;
        return false;
    }
}
