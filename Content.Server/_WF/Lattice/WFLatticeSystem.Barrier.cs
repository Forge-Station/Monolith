using System.Numerics;
using Content.Server._WF.Remade.Ward;
using Content.Server.Shuttles.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.Lattice;
using Content.Shared._WF.ShipShields;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.Lattice;

/// <summary>
/// An anchor's shield is a wall like a rooted Monolith's: while it stands no ship and nobody in a suit gets inside it. It
/// has to be brought down first.
/// </summary>
public sealed partial class WFLatticeSystem
{
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private WFWardSystem _ward = default!;

    private readonly HashSet<EntityUid> _barrierFriendly = new();

    /// <summary>Ships are held out every tick; people loose in space only this often, which is still plenty to push a jetpack back.</summary>
    private static readonly TimeSpan BarrierPeopleInterval = TimeSpan.FromSeconds(0.25);
    private TimeSpan _nextBarrierPeople;

    /// <summary>Each shield's oval, kept until its outline changes.</summary>
    private readonly Dictionary<EntityUid, (Vector2[][] Contours, Vector2 Centre, Vector2 Radii)> _ovals = new();

    private void HoldBarriers()
    {
        var people = _timing.CurTime >= _nextBarrierPeople;
        if (people)
            _nextBarrierPeople = _timing.CurTime + BarrierPeopleInterval;

        var shields = EntityQueryEnumerator<WFLatticeAnchorShieldComponent, ShipShieldEmitterComponent, TransformComponent>();
        while (shields.MoveNext(out _, out _, out var emitter, out var xform))
        {
            if (xform.GridUid is not { } station
                || emitter.Shield is not { } field
                || TerminatingOrDeleted(field)
                || TryComp<PhysicsComponent>(field, out var body) && !body.CanCollide
                || !TryOval(field, out var centre, out var radii))
                continue;

            _barrierFriendly.Clear();
            _shuttle.GetAllDockedShuttles(station, _barrierFriendly);
            _ward.HoldOut(station, centre, radii, _barrierFriendly, people);
        }
    }

    private bool TryOval(EntityUid field, out Vector2 centre, out Vector2 radii)
    {
        centre = radii = Vector2.Zero;
        if (!TryComp<WFShipShieldVisualsComponent>(field, out var visuals) || visuals.Contours.Length == 0)
            return false;

        if (_ovals.TryGetValue(field, out var known) && ReferenceEquals(known.Contours, visuals.Contours))
        {
            (centre, radii) = (known.Centre, known.Radii);
            return true;
        }

        if (!_ward.TryGetOval(field, out centre, out radii))
            return false;

        _ovals[field] = (visuals.Contours, centre, radii);
        return true;
    }
}
