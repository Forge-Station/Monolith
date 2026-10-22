using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>Orders an admin gives one encounter ship from the encounter window.</summary>
public sealed partial class WFEncounterSystem
{
    /// <summary>How far off its target a ship sent to follow or attack keeps.</summary>
    private const float AdminFollowRange = 400f;

    private const float AdminAttackRange = 600f;

    /// <summary>
    /// Has a ship of an encounter carry out an admin's order. <paramref name="target"/> is the ship it is aimed at, and
    /// <paramref name="here"/> where the admin is. Returns why it could not, or null.
    /// </summary>
    public string? AdminOrder(Entity<WFEncounterComponent> encounter, string key, WFEncounterAdminOrder order, EntityUid? target,
        MapCoordinates? here)
    {
        var comp = encounter.Comp;
        if (comp.Resolution != null && order != WFEncounterAdminOrder.SendAway)
            return Loc.GetString("wf-encounter-admin-order-resolved");
        if (!comp.Ships.TryGetValue(key, out var ship) || TerminatingOrDeleted(ship.Grid))
            return Loc.GetString("wf-encounter-admin-gone");
        if (ship.Building)
            return Loc.GetString("wf-encounter-admin-order-building");

        var needsTarget = order is WFEncounterAdminOrder.Extort or WFEncounterAdminOrder.Board or WFEncounterAdminOrder.Attack
            or WFEncounterAdminOrder.Follow;
        EntityUid mark = default;
        if (needsTarget)
        {
            if (target is not { } chosen || TerminatingOrDeleted(chosen) || !HasComp<MapGridComponent>(chosen) || chosen == ship.Grid
                || Transform(chosen).MapID != Transform(ship.Grid).MapID)
                return Loc.GetString("wf-encounter-admin-order-no-target");
            mark = chosen;
        }

        // An encounter still waiting for a player is begun: the admin's order is the start it was waiting for.
        if (!comp.Begun && order is not (WFEncounterAdminOrder.SpawnNow or WFEncounterAdminOrder.SendAway) && !Begin(comp))
            return Loc.GetString("wf-encounter-admin-order-failed");

        switch (order)
        {
            case WFEncounterAdminOrder.Extort:
                if (ship.Extort is not { } extort)
                    return Loc.GetString("wf-encounter-admin-order-not-extorter");
                if (extort.Phase == WFExtortPhase.Board)
                    return Loc.GetString("wf-encounter-admin-order-boarding");
                EndExtortCall(extort);
                extort.Spared.Remove(mark);
                extort.Mark = mark;
                extort.Attempts = 0;
                extort.DeadSince = null;
                SetExtortPhase(extort, WFExtortPhase.Close);
                extort.NextMark = _timing.CurTime + ExtortInterval(extort.Config);
                if (!Shadow(ship, extort, mark))
                    return Loc.GetString("wf-encounter-admin-order-failed");
                break;

            case WFEncounterAdminOrder.Board:
                if (!ship.Hunt || ship.Extort != null)
                    return Loc.GetString("wf-encounter-admin-order-not-hunter");
                ship.Prey = mark;
                ship.Raided = false;
                ship.RaidEnds = null;
                if (_huntRecords.TryGetValue(ship.Grid, out var record))
                {
                    record.Shunned.Remove(mark);
                    record.Misses = 0;
                }
                // Simulated, it flies at its prey as a point; spawned on the way, its crew take the run up.
                if (!SetOrders(encounter.AsNullable(), key, new List<WFCrewObjective>
                    {
                        new() { Kind = WFCrewObjectiveKind.Follow, Target = GetNetEntity(mark), Range = AdminFollowRange },
                    }))
                    return Loc.GetString("wf-encounter-admin-order-failed");
                break;

            case WFEncounterAdminOrder.Attack:
            case WFEncounterAdminOrder.Follow:
                // An extorter or boarder set on something else lets its own run go.
                if (ship.Extort is { } extorting)
                {
                    EndExtortCall(extorting);
                    extorting.Mark = null;
                    SetExtortPhase(extorting, WFExtortPhase.Roam);
                    extorting.NextMark = _timing.CurTime + ExtortInterval(extorting.Config);
                }

                ship.Prey = null;
                var attack = order == WFEncounterAdminOrder.Attack;
                if (!SetOrders(encounter.AsNullable(), key, new List<WFCrewObjective>
                    {
                        new()
                        {
                            Kind = attack ? WFCrewObjectiveKind.Attack : WFCrewObjectiveKind.Follow,
                            Target = GetNetEntity(mark),
                            Range = attack ? AdminAttackRange : AdminFollowRange,
                        },
                        new() { Kind = WFCrewObjectiveKind.Hold },
                    }))
                    return Loc.GetString("wf-encounter-admin-order-failed");
                break;

            case WFEncounterAdminOrder.ComeToMe:
                if (here is not { } point || point.MapId != Transform(ship.Grid).MapID)
                    return Loc.GetString("wf-encounter-admin-order-not-here");
                if (!SetOrders(encounter.AsNullable(), key, new List<WFCrewObjective>
                    {
                        new() { Kind = WFCrewObjectiveKind.GoTo, Position = point.Position, Range = 300f },
                        new() { Kind = WFCrewObjectiveKind.Hold },
                    }))
                    return Loc.GetString("wf-encounter-admin-order-failed");
                break;

            case WFEncounterAdminOrder.Hold:
                if (!SetOrders(encounter.AsNullable(), key, new List<WFCrewObjective> { new() { Kind = WFCrewObjectiveKind.Hold } }))
                    return Loc.GetString("wf-encounter-admin-order-failed");
                break;

            case WFEncounterAdminOrder.Resume:
                if (!_prototypes.TryIndex(comp.Prototype, out var prototype)
                    || prototype.Ships.Find(entry => entry.Key == key) is not { } entry
                    || !IssueShipOrders(comp, prototype, entry, ship))
                    return Loc.GetString("wf-encounter-admin-order-failed");
                break;

            case WFEncounterAdminOrder.SpawnNow:
                if (!ship.Virtual)
                    return Loc.GetString("wf-encounter-admin-order-not-simulated");
                _pending.RemoveAll(entry => entry.Encounter == encounter.Owner && entry.Key == key);
                _pending.Insert(0, (encounter.Owner, key, true));
                break;

            case WFEncounterAdminOrder.SendAway:
                // An admin's own encounter is kept until ended; sent away, it goes like any other.
                comp.Pinned = false;
                if (comp.Resolution == null)
                    Resolve(encounter.AsNullable(), WFEncounterResolution.Completed);
                comp.JumpAt = _timing.CurTime;
                break;
        }

        return null;
    }
}
