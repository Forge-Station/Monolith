using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.NpcCrew.Systems;

public sealed partial class WFCrewObjectiveSystem
{
    /// <summary>How near a simulated ship comes to dock with another.</summary>
    private const float VirtualDockReach = 60f;

    /// <summary>Whether an entity can hold a queue and be aimed at: a grid, or a simulated ship standing in for one.</summary>
    public bool IsShip(EntityUid uid)
    {
        return HasComp<MapGridComponent>(uid) || HasComp<WFCrewVirtualShipComponent>(uid);
    }

    /// <summary>Moves a crew's queue to another ship, as a simulated ship turns real or back; the task in hand starts over.</summary>
    public void MoveQueue(EntityUid from, EntityUid to, string group)
    {
        if (!_queues.Remove((from, group), out var state))
            return;

        state.Started = false;
        state.Pilot = null;
        state.Elapsed = 0;
        // A stopped queue keeps why it stopped, a failed dock or a lost target, for whoever runs it to deal with.
        if (!state.Paused)
            state.Status = "pending";
        _queues[(to, group)] = state;
        UpdateEscort(to, group, state);
    }

    /// <summary>Puts a task at the head of a crew's queue, ahead of the one in hand.</summary>
    public void Prepend(EntityUid grid, string group, WFCrewObjective objective)
    {
        if (!_queues.TryGetValue((grid, group), out var state))
            _queues[(grid, group)] = state = new QueueState();

        state.Items.Insert(0, objective);
        state.Started = false;
        state.Paused = false;
        state.Status = "pending";
    }

    /// <summary>Points every queued task aimed at one ship at another.</summary>
    public void Retarget(EntityUid from, EntityUid to)
    {
        var old = GetNetEntity(from);
        var replacement = GetNetEntity(to);
        foreach (var ((grid, group), state) in _queues)
        {
            var changed = false;
            for (var i = 0; i < state.Items.Count; i++)
            {
                if (state.Items[i].Target != old)
                    continue;

                state.Items[i] = new WFCrewObjective
                {
                    Kind = state.Items[i].Kind,
                    Target = replacement,
                    Position = state.Items[i].Position,
                    Range = state.Items[i].Range,
                    Duration = state.Items[i].Duration,
                };
                changed = true;
                if (i == 0)
                    state.Started = false;
            }

            if (changed)
                UpdateEscort(grid, group, state);
        }
    }

    /// <summary>The centre of a ship in map space: a grid's bounds, or a simulated ship's point.</summary>
    public Vector2 ShipCentre(EntityUid uid)
    {
        return TryComp<MapGridComponent>(uid, out var grid)
            ? _transform.ToMapCoordinates(new EntityCoordinates(uid, grid.LocalAABB.Center)).Position
            : _transform.GetWorldPosition(uid);
    }

    /// <summary>Flies a simulated ship's current task one step: travel at its speed, wait out a timed task, dock by reaching.</summary>
    private void StepVirtual(Entity<WFCrewVirtualShipComponent> ship, QueueState state, float elapsed)
    {
        ship.Comp.Velocity = Vector2.Zero;
        // Held for visitors or a hail when it was real: nobody is aboard a simulated ship to wait for, so it flies on.
        if (state.Paused && state.Status == "paused")
        {
            state.Paused = false;
            state.Status = "pending";
        }

        if (state.Paused || state.Items.Count == 0)
            return;

        var item = state.Items[0];
        var target = item.Target is { } net && TryGetEntity(net, out var resolved) ? resolved.Value : EntityUid.Invalid;
        // An attack is over once its target is gone.
        if (item.Kind == WFCrewObjectiveKind.Attack && (!target.IsValid() || TerminatingOrDeleted(target)))
        {
            Finish(state);
            return;
        }

        if (!Valid(ship, item))
        {
            state.Paused = true;
            state.Status = "target-lost";
            return;
        }

        var travels = item.Kind is WFCrewObjectiveKind.GoTo or WFCrewObjectiveKind.Undock or WFCrewObjectiveKind.Retreat;
        var from = _transform.GetWorldPosition(ship);
        if (!state.Started)
        {
            state.Started = true;
            state.Elapsed = 0;
            var distance = item.Kind == WFCrewObjectiveKind.GoTo ? (item.Position - from).Length()
                : target.IsValid() ? (ShipCentre(target) - from).Length() : 0f;
            state.Budget = item.Duration > 0 ? item.Duration : TravelSlack + 2f * distance / MathF.Max(ship.Comp.Speed, 1f);
        }

        state.Status = "running";
        state.Elapsed += elapsed;
        switch (item.Kind)
        {
            case WFCrewObjectiveKind.Hold:
            case WFCrewObjectiveKind.Repair:
                if (item.Duration > 0 && state.Elapsed >= item.Duration)
                    Finish(state);
                return;
            case WFCrewObjectiveKind.Undock:
                ship.Comp.DockedTo = null;
                Finish(state);
                return;
            case WFCrewObjectiveKind.GoTo:
                ship.Comp.DockedTo = null;
                if (Approach(ship, from, item.Position, item.Range, elapsed))
                    Finish(state);
                break;
            case WFCrewObjectiveKind.Retreat:
                ship.Comp.DockedTo = null;
                if (Approach(ship, from, ShipCentre(target), item.Range + 200f, elapsed))
                    Finish(state);
                break;
            case WFCrewObjectiveKind.Dock:
            case WFCrewObjectiveKind.Resupply:
            case WFCrewObjectiveKind.Salvage:
            case WFCrewObjectiveKind.Loot:
                if (ship.Comp.DockedTo == target || Approach(ship, from, ShipCentre(target), VirtualDockReach, elapsed))
                {
                    ship.Comp.DockedTo = target;
                    Finish(state);
                }
                return;
            case WFCrewObjectiveKind.Circle:
            case WFCrewObjectiveKind.Loiter:
                // Circling a point, as a patrol posted off a station does: out to the range, then round it.
                ship.Comp.DockedTo = null;
                Orbit(ship, from, ShipCentre(target), item.Range, elapsed);
                if (item.Duration > 0 && state.Elapsed >= item.Duration)
                    Finish(state);
                return;
            default:
                // Follow, escort and attack: close to the range and keep there, moving with the target.
                ship.Comp.DockedTo = null;
                if (Approach(ship, from, ShipCentre(target), item.Range, elapsed))
                    KeepStation(ship, target, elapsed);
                if (item.Duration > 0 && state.Elapsed >= item.Duration)
                    Finish(state);
                return;
        }

        if (travels && state.Items.FirstOrDefault() == item && state.Elapsed >= state.Budget)
        {
            state.Paused = true;
            state.Status = "target-lost";
        }
    }

    /// <summary>Moves a simulated ship towards a point; true once it is within reach.</summary>
    private bool Approach(Entity<WFCrewVirtualShipComponent> ship, Vector2 from, Vector2 to, float reach, float elapsed)
    {
        var offset = to - from;
        var distance = offset.Length();
        if (distance <= reach)
            return true;

        var step = MathF.Min(distance - reach, ship.Comp.Speed * elapsed);
        var direction = offset / distance;
        _transform.SetWorldPosition(ship, from + direction * step);
        // What it actually covered, not its top speed: the sector markers run it on between updates by this.
        ship.Comp.Velocity = elapsed > 0f ? direction * step / elapsed : Vector2.Zero;
        return distance - step <= reach;
    }

    /// <summary>How far off its circle a simulated ship may be and still fly round it rather than back out to it.</summary>
    private const float OrbitSlack = 50f;

    /// <summary>Flies a simulated ship round a point at a radius: to the circle first, then along it at its speed.</summary>
    private void Orbit(Entity<WFCrewVirtualShipComponent> ship, Vector2 from, Vector2 centre, float radius, float elapsed)
    {
        radius = MathF.Max(radius, 100f);
        var offset = from - centre;
        var distance = offset.Length();
        if (distance < 0.01f)
            offset = Vector2.UnitX;

        var outward = Vector2.Normalize(offset);
        if (MathF.Abs(distance - radius) > OrbitSlack)
        {
            Approach(ship, from, centre + outward * radius, 0f, elapsed);
            return;
        }

        // Anticlockwise along the circle: the arc it covers at its speed, and no further than that in all, so a ship a
        // little off the circle eases onto it rather than jumping there.
        var reach = ship.Comp.Speed * elapsed;
        var turn = new Angle(reach / radius);
        var next = centre + turn.RotateVec(outward) * radius;
        var step = next - from;
        if (step.Length() > reach)
            next = from + Vector2.Normalize(step) * reach;

        _transform.SetWorldPosition(ship, next);
        ship.Comp.Velocity = elapsed > 0f ? (next - from) / elapsed : Vector2.Zero;
    }

    /// <summary>Moves a simulated ship within range of its target along with it, so an escort keeps pace instead of stopping and catching up.</summary>
    private void KeepStation(Entity<WFCrewVirtualShipComponent> ship, EntityUid target, float elapsed)
    {
        var velocity = TryComp<WFCrewVirtualShipComponent>(target, out var leader) ? leader.Velocity
            : TryComp<PhysicsComponent>(target, out var body) ? body.LinearVelocity
            : Vector2.Zero;
        if (velocity.LengthSquared() <= 0.01f)
            return;

        // No faster than it can fly.
        if (velocity.Length() > ship.Comp.Speed)
            velocity = Vector2.Normalize(velocity) * ship.Comp.Speed;

        _transform.SetWorldPosition(ship, _transform.GetWorldPosition(ship) + velocity * elapsed);
        ship.Comp.Velocity = velocity;
    }

    private static void Finish(QueueState state)
    {
        state.Items.RemoveAt(0);
        state.Started = false;
        state.Elapsed = 0;
        state.Status = state.Items.Count == 0 ? "complete" : "pending";
    }

    /// <summary>Rows for the simulated ships' queues, which have no crew aboard to report them.</summary>
    private void AddVirtualRows(Dictionary<(EntityUid Grid, string Group), WFCrewSetupCrew> rows)
    {
        foreach (var ((grid, group), state) in _queues)
        {
            if (rows.ContainsKey((grid, group)) || !TryComp<WFCrewVirtualShipComponent>(grid, out var ship))
                continue;

            rows[(grid, group)] = new WFCrewSetupCrew
            {
                Grid = GetNetEntity(grid),
                Group = group,
                Objectives = state.Items.ToList(),
                Status = Loc.GetString($"wf-crew-objective-status-{state.Status}"),
                Members = ship.Crew,
                Alive = ship.Crew,
            };
        }
    }
}
