using System.Linq;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WF.Vehicles.Npc;

/// <summary>
/// An NPC that climbs into vehicles to get at whoever is inside: a hostile one near an occupied vehicle walks to its
/// nearest door and, unlocked, climbs in and fights in the cabin, and climbs out again once nobody he's after is left
/// inside. A locked vehicle it just attacks from outside.
/// </summary>
[RegisterComponent]
public sealed partial class WFVehicleBoarderComponent : Component
{
    /// <summary>How far off he notices a vehicle worth boarding.</summary>
    [DataField]
    public float Range = 12f;

    /// <summary>The vehicle he's making for, if any.</summary>
    [ViewVariables]
    public EntityUid? Vehicle;

    /// <summary>When he gives up getting in.</summary>
    [ViewVariables]
    public TimeSpan GiveUpAt;

    /// <summary>When he next looks for a vehicle to board.</summary>
    [ViewVariables]
    public TimeSpan NextLook;

    /// <summary>Until when he's climbing in, so the door isn't tried again mid-climb.</summary>
    [ViewVariables]
    public TimeSpan ClimbingUntil;

    /// <summary>Whether he climbs back out once nobody he's after is left in the cabin; a squatter stays put.</summary>
    [DataField]
    public bool Leaves = true;

    /// <summary>When he climbs out of a cabin with nobody left inside to fight.</summary>
    [ViewVariables]
    public TimeSpan? LeaveAt;
}

public sealed class WFNpcBoardingSystem : EntitySystem
{
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VehicleInteriorSystem _interiors = default!;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LookInterval = TimeSpan.FromSeconds(1);

    /// <summary>Longer than any vehicle's climb-in do-after.</summary>
    private static readonly TimeSpan ClimbTime = TimeSpan.FromSeconds(4);

    /// <summary>How long he waits in a cabin with nobody left to fight before he climbs out.</summary>
    private static readonly TimeSpan LeaveDelay = TimeSpan.FromSeconds(3);

    /// <summary>Further than any cabin is long.</summary>
    private const float CabinRange = 32f;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        UpdateInside(now);
        var query = EntityQueryEnumerator<WFVehicleBoarderComponent, ActiveNPCComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var boarder, out _, out var htn))
        {
            if (now < boarder.NextLook)
                continue;
            boarder.NextLook = now + TimeSpan.FromSeconds(0.5);

            if (!_mobs.IsAlive(uid) || HasComp<VehicleInteriorOccupantComponent>(uid))
            {
                Stop((uid, boarder), htn);
                continue;
            }

            if (boarder.Vehicle is { } vehicle)
            {
                Board((uid, boarder), htn, vehicle, now);
                continue;
            }

            // Only one already out for blood goes looking for a way in.
            if (!htn.Enabled || FindVehicle((uid, boarder)) is not { } target)
            {
                boarder.NextLook = now + LookInterval;
                continue;
            }

            boarder.Vehicle = target;
            boarder.GiveUpAt = now + Patience;
            _htn.SetHTNEnabled((uid, htn), false);
        }
    }

    private void Board(Entity<WFVehicleBoarderComponent> ent, HTNComponent htn, EntityUid vehicle, TimeSpan now)
    {
        if (TerminatingOrDeleted(vehicle) || now >= ent.Comp.GiveUpAt || _interiors.IsLockedAgainst(vehicle, ent)
            || !HasComp<WFOccupiedVehicleComponent>(vehicle)
            || !_interiors.TryGetNearestEntry(vehicle, ent, out var door, out var doorRadius) || door.MapId != Transform(ent).MapID)
        {
            Stop(ent, htn);
            ent.Comp.NextLook = now + Patience;
            return;
        }

        // Right up to the doorway: the door only lets in someone standing within its radius.
        var here = _transform.GetMapCoordinates(ent);
        if ((here.Position - door.Position).Length() > doorRadius * 0.7f)
        {
            // A rocking hull moves its door a little; re-planning the path for that would keep him from ever arriving.
            if (TryComp(ent, out NPCSteeringComponent? steering)
                && (_transform.ToMapCoordinates(steering.Coordinates).Position - door.Position).Length() < 0.3f)
            {
                // A path that came back empty stays that way for the same target: ask again from scratch.
                if (steering.Status != SteeringStatus.NoPath)
                    return;
                _steering.Unregister(ent);
            }
            if (Transform(ent).GridUid is { } grid)
                _steering.Register(ent, _transform.ToCoordinates(grid, door));
            else if (Transform(ent).MapUid is { } map)
                _steering.Register(ent, _transform.ToCoordinates(map, door));
            return;
        }

        _steering.Unregister(ent);
        if (now >= ent.Comp.ClimbingUntil && _interiors.TryNpcEnter(vehicle, ent))
            ent.Comp.ClimbingUntil = now + ClimbTime;
    }

    /// <summary>
    /// A boarder in a cabin climbs out a few seconds after the last of his enemies inside is gone or dead, to go after
    /// them outside or back to his business.
    /// </summary>
    private void UpdateInside(TimeSpan now)
    {
        var inside = EntityQueryEnumerator<WFVehicleBoarderComponent, VehicleInteriorOccupantComponent>();
        while (inside.MoveNext(out var uid, out var boarder, out var occupant))
        {
            if (!boarder.Leaves || !_mobs.IsAlive(uid) || !TryComp(occupant.Vehicle, out VehicleInteriorComponent? interior))
                continue;

            // Whoever he'd fight, by grudge or by faction, still alive in here with him.
            var passengers = interior.Passengers;
            if (_factions.GetNearbyHostiles(uid, CabinRange).Any(hostile => passengers.Contains(hostile) && _mobs.IsAlive(hostile)))
            {
                boarder.LeaveAt = null;
                continue;
            }

            boarder.LeaveAt ??= now + LeaveDelay;
            if (now < boarder.LeaveAt)
                continue;
            boarder.LeaveAt = null;
            _steering.Unregister(uid);
            _interiors.TryExitVehicle(occupant.Vehicle, uid);
        }
    }

    /// <summary>Back to fighting on his own, inside or out.</summary>
    private void Stop(Entity<WFVehicleBoarderComponent> ent, HTNComponent htn)
    {
        if (ent.Comp.Vehicle == null)
            return;
        ent.Comp.Vehicle = null;
        _steering.Unregister(ent);
        _htn.SetHTNEnabled((ent, htn), true);
    }

    /// <summary>The nearest unlocked vehicle with someone inside he's hostile to, or the vehicle itself is.</summary>
    private EntityUid? FindVehicle(Entity<WFVehicleBoarderComponent> ent)
    {
        var hostiles = _factions.GetNearbyHostiles(ent.Owner, ent.Comp.Range).ToHashSet();
        if (hostiles.Count == 0 && !HasComp<FactionExceptionComponent>(ent))
            return null;

        var here = _transform.GetMapCoordinates(ent);
        EntityUid? best = null;
        var bestDistance = ent.Comp.Range;
        var query = EntityQueryEnumerator<WFOccupiedVehicleComponent, VehicleInteriorComponent, TransformComponent>();
        while (query.MoveNext(out var vehicle, out _, out var interior, out var xform))
        {
            if (xform.MapID != here.MapId)
                continue;
            var distance = (_transform.GetWorldPosition(xform) - here.Position).Length();
            if (distance >= bestDistance || _interiors.IsLockedAgainst(vehicle, ent))
                continue;
            if (!hostiles.Contains(vehicle) && !interior.Passengers.Any(hostiles.Contains))
                continue;
            best = vehicle;
            bestDistance = distance;
        }
        return best;
    }
}
