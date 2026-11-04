using System.Numerics;
using Content.Server._WF.Planets;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Mind.Components;
using Content.Shared._WF.Planets;
using Content.Shared._WF.Vehicles.Core;
using Robust.Shared.Enums;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.Dormancy;

/// <summary>
/// Says whether a spot on a planet is near a player, so systems can leave planet ground nobody is on alone.
/// Off-planet, and on a planet's orbit layer, everything is always active.
/// </summary>
public sealed class WFDormancySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Side of an activity chunk, in metres.</summary>
    public const float ChunkSize = 16f;

    /// <summary>How far round a player, in metres, a planet stays awake; past what a player sees, so things wake before they come into view.</summary>
    public const float Range = 48f;

    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(0.5);

    private readonly HashSet<(EntityUid Network, Vector2i Chunk)> _active = new();
    private readonly HashSet<EntityUid> _activeNetworks = new();
    private readonly Dictionary<EntityUid, List<Vector2>> _viewers = new();
    private readonly Dictionary<EntityUid, EntityUid?> _networkOf = new();
    private TimeSpan _nextRefresh;

    private EntityQuery<CEZMapComponent> _zMapQuery;
    private EntityQuery<WFPlanetNetworkComponent> _networkQuery;
    private EntityQuery<WFOrbitLayerComponent> _orbitQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<VehicleInteriorOccupantComponent> _occupantQuery;
    private EntityQuery<OrganComponent> _organQuery;
    private EntityQuery<MindContainerComponent> _mindQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;

    public override void Initialize()
    {
        base.Initialize();

        _zMapQuery = GetEntityQuery<CEZMapComponent>();
        _networkQuery = GetEntityQuery<WFPlanetNetworkComponent>();
        _orbitQuery = GetEntityQuery<WFOrbitLayerComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _occupantQuery = GetEntityQuery<VehicleInteriorOccupantComponent>();
        _organQuery = GetEntityQuery<OrganComponent>();
        _mindQuery = GetEntityQuery<MindContainerComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        SubscribeLocalEvent<PlayerAttachedEvent>(_ => _nextRefresh = TimeSpan.Zero);
    }

    /// <summary>Whether an entity is somewhere that should run: off-planet, or on a planet near a player.</summary>
    public bool IsActive(EntityUid uid, TransformComponent? xform = null)
    {
        if (!_xformQuery.Resolve(uid, ref xform, false) || xform.MapUid is not { } map || NetworkOf(map) is not { } network)
            return true;

        Refresh();

        return _activeNetworks.Contains(network)
            && _active.Contains((network, ChunkOf(_transform.GetWorldPosition(xform))));
    }

    /// <summary>Whether a spot on a map should run: off-planet, or on a planet near a player.</summary>
    public bool IsActiveAt(EntityUid map, Vector2 worldPosition)
    {
        if (NetworkOf(map) is not { } network)
            return true;

        Refresh();
        return _activeNetworks.Contains(network) && _active.Contains((network, ChunkOf(worldPosition)));
    }

    /// <summary>
    /// Whether a body, or an organ in one, may skip its life upkeep (breath, blood, metabolism): an NPC on planet ground
    /// no player is near. A body with a mind keeps going wherever it is.
    /// </summary>
    public bool BodySleeps(EntityUid uid)
    {
        if (_organQuery.TryComp(uid, out var organ) && organ.Body is { } body)
            uid = body;

        return !IsActive(uid) && !(_mindQuery.TryComp(uid, out var mind) && mind.HasMind);
    }

    /// <summary>Whether a mover's body is at rest on planet ground no player is near, so its movement needs no handling.</summary>
    public bool MoverSleeps(EntityUid uid)
    {
        return _physicsQuery.TryComp(uid, out var body) && !body.Awake && !IsActive(uid);
    }

    /// <summary>Whether any player is anywhere on the planet a map belongs to; true off-planet.</summary>
    public bool IsMapActive(EntityUid map)
    {
        if (NetworkOf(map) is not { } network)
            return true;

        Refresh();
        return _activeNetworks.Contains(network);
    }

    /// <summary>
    /// Where on a planet anyone can see from: players' bodies (a vehicle, for someone in its cabin) and the eyes they
    /// watch through. Positions are shared by every layer of the planet.
    /// </summary>
    public IReadOnlyList<Vector2> Viewers(EntityUid network)
    {
        Refresh();
        return _viewers.TryGetValue(network, out var viewers) ? viewers : Array.Empty<Vector2>();
    }

    /// <summary>The planet a map is a layer of, or null off-planet and on an orbit layer.</summary>
    public EntityUid? PlanetOf(EntityUid map)
    {
        return NetworkOf(map);
    }

    /// <summary>Drops the cached player positions, so the next question sees where everyone is now.</summary>
    public void Invalidate()
    {
        _nextRefresh = TimeSpan.Zero;
    }

    private EntityUid? NetworkOf(EntityUid map)
    {
        if (_networkOf.TryGetValue(map, out var cached))
            return cached;

        EntityUid? network = null;
        if (_zMapQuery.TryComp(map, out var zMap)
            && _networkQuery.HasComp(zMap.NetworkUid)
            && !_orbitQuery.HasComp(map))
        {
            network = zMap.NetworkUid;
        }

        _networkOf[map] = network;
        return network;
    }

    private void Refresh()
    {
        if (_timing.CurTime < _nextRefresh)
            return;

        _nextRefresh = _timing.CurTime + RefreshEvery;
        _active.Clear();
        _activeNetworks.Clear();
        _networkOf.Clear();
        foreach (var viewers in _viewers.Values)
        {
            viewers.Clear();
        }

        var reach = (int) MathF.Ceiling(Range / ChunkSize);
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } body)
                continue;

            AddEyes(session);

            // In a vehicle's cabin a player is wherever the vehicle is.
            if (_occupantQuery.TryComp(body, out var occupant) && Exists(occupant.Vehicle))
                body = occupant.Vehicle;

            if (!_xformQuery.TryComp(body, out var xform) || xform.MapUid is not { } map || NetworkOf(map) is not { } network)
                continue;

            _activeNetworks.Add(network);
            var position = _transform.GetWorldPosition(xform);
            _viewers.GetOrNew(network).Add(position);
            var centre = ChunkOf(position);
            for (var x = -reach; x <= reach; x++)
            for (var y = -reach; y <= reach; y++)
            {
                _active.Add((network, centre + new Vector2i(x, y)));
            }
        }
    }

    /// <summary>Notes the eyes a player watches through, such as a camera, as viewers of their planet.</summary>
    private void AddEyes(ICommonSession session)
    {
        foreach (var eye in session.ViewSubscriptions)
        {
            if (_xformQuery.TryComp(eye, out var xform) && xform.MapUid is { } map && NetworkOf(map) is { } network)
                _viewers.GetOrNew(network).Add(_transform.GetWorldPosition(xform));
        }
    }

    private static Vector2i ChunkOf(Vector2 position)
    {
        return new Vector2i((int) MathF.Floor(position.X / ChunkSize), (int) MathF.Floor(position.Y / ChunkSize));
    }
}
