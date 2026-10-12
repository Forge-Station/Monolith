using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>How far from the origin a reinforcement convoy appears, on the side of the sector nearest its hub.</summary>
    private const float EdgeMin = 25000f;
    private const float EdgeMax = 28000f;

    /// <summary>How far, in radians, an edge point may stray from the line from the origin through the hub.</summary>
    private const float EdgeSpread = 0.35f;

    /// <summary>How far from the hub a reinforcement appears if the sector edge has no clear point.</summary>
    private const float NearMin = 3000f;
    private const float NearMax = 5000f;

    /// <summary>How near the hub the reinforcement flies before it docks, and how near counts as arrived.</summary>
    private const float HubStandoff = 600f;
    private const float HubArrival = 900f;

    /// <summary>How near the hub a reinforcement that failed to dock holds.</summary>
    private const float HoldRange = 300f;

    /// <summary>How long past its deadline a reinforcement at the hub is given to finish docking.</summary>
    private static readonly TimeSpan DockPatience = TimeSpan.FromMinutes(5);

    /// <summary>How soon a reinforcement whose crew gave up the flight may be ordered to dock again.</summary>
    private static readonly TimeSpan ReorderDelay = TimeSpan.FromSeconds(30);

    /// <summary>How soon a hub tries again after a reinforcement that could not be sent.</summary>
    private static readonly TimeSpan ReinforceRetry = TimeSpan.FromMinutes(1);

    /// <summary>How long after the last hit, with no attacker near, reinforcements that broke away come back.</summary>
    private static readonly TimeSpan ReturnDelay = TimeSpan.FromMinutes(3);

    private const float ReturnClearance = 2500f;

    /// <summary>How far from the hub a ship may be and still be picked as the one that is attacking it.</summary>
    private const float AttackerLeash = 5000f;

    /// <summary>The reinforcements out or docked on the sector map.</summary>
    public List<Entity<WFReaverReinforcementComponent, WFEncounterComponent>> Reinforcements()
    {
        var map = _territory.SectorMap;
        var reinforcements = new List<Entity<WFReaverReinforcementComponent, WFEncounterComponent>>();
        var query = EntityQueryEnumerator<WFReaverReinforcementComponent, WFEncounterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var reinforcement, out var encounter, out var xform))
        {
            if (encounter.Resolution == null && xform.MapID == map && !TerminatingOrDeleted(uid))
                reinforcements.Add((uid, reinforcement, encounter));
        }

        return reinforcements;
    }

    /// <summary>Sends the next reinforcement from each hub that has stood its interval, is not under fire and is under its tier's level cap.</summary>
    private void TickReinforce(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        var now = _timing.CurTime;
        foreach (var stronghold in Strongholds())
        {
            var comp = stronghold.Comp1;
            if (comp.Inbound != null || comp.Scattered || UnderFire(comp) || now < comp.NextReinforcement || comp.Level >= LevelCap(campaign, state))
                continue;

            if (!TryReinforce(stronghold.Owner))
                comp.NextReinforcement = now + ReinforceRetry;
        }
    }

    /// <summary>Sends a stronghold's next reinforcement now, if it is under its tier's level cap and none is on its way: a convoy from the sector edge that flies to the hub and docks.</summary>
    public bool TryReinforce(EntityUid stronghold)
    {
        if (Campaign is not { } campaign || State(true) is not { } state
            || !TryComp<WFReaverStrongholdComponent>(stronghold, out var comp)
            || !TryComp<WFEncounterComponent>(stronghold, out var encounter) || encounter.Resolution != null
            || comp.Inbound != null || comp.Level >= LevelCap(campaign, state)
            || !encounter.Ships.TryGetValue(comp.Lead, out var hub) || TerminatingOrDeleted(hub.Grid)
            || !_prototypes.TryIndex(campaign.Reinforcements[comp.Level], out var prototype) || prototype.Ships.Count == 0)
            return false;

        var map = Transform(stronghold).MapID;
        var hubAt = _transform.GetWorldPosition(hub.Grid);
        if (!TryEdgePoint(map, hubAt, out var start) && !TryClearPoint(map, hubAt, NearMin, NearMax, SpawnClearance, out start))
            return false;

        if (!SpawnEncounter(prototype, new MapCoordinates(start, map), out var uid, offBudget: true))
            return false;

        var lead = prototype.Ships[0].Key;
        if (!_encounters.SetOrders(uid, lead, DockOrders(hub.Grid, hubAt)))
        {
            _encounters.End(uid);
            return false;
        }

        var reinforcement = AddComp<WFReaverReinforcementComponent>(uid);
        reinforcement.Stronghold = stronghold;
        reinforcement.Level = comp.Level + 1;
        reinforcement.Lead = lead;
        reinforcement.Launched = _timing.CurTime;
        reinforcement.Overdue = _timing.CurTime + ConvoyEta(start, hubAt) * 2 + ConvoySlack;
        comp.Inbound = uid;

        var minutes = Math.Max(1, (int) Math.Ceiling(Vector2.Distance(start, hubAt) / ConvoySpeed / 60f));
        Announce(campaign, Loc.GetString("wf-reaver-reinforcement-launched",
            ("callsign", WFSectorHex.Callsign(comp.Cell)),
            ("minutes", minutes)), true, chime: false);
        Log.Info($"Reaver reinforcement {ToPrettyString(uid)} (level {reinforcement.Level}) launched for hub {ToPrettyString(stronghold)}.");
        return true;
    }

    /// <summary>Whether a hub took a hit from a player ship within the support window.</summary>
    private bool UnderFire(WFReaverStrongholdComponent home)
    {
        return home.Hits > 0 && _timing.CurTime - home.LastHit < SupportWindow;
    }

    /// <summary>Marks a reinforcement whose crew ran out of docking attempts, so it holds beside the hub.</summary>
    private void OnDockFailed(ref WFPilotDockFailedEvent args)
    {
        foreach (var (_, comp, encounter) in Reinforcements())
        {
            if (comp.Phase is WFReaverReinforcementPhase.Inbound or WFReaverReinforcementPhase.Returning
                && encounter.Ships.TryGetValue(comp.Lead, out var lead) && lead.Grid == args.Grid)
                comp.DockFailed = true;
        }
    }

    /// <summary>A clear point 25 to 28 km from the origin on the side the hub is on.</summary>
    private bool TryEdgePoint(MapId map, Vector2 hub, out Vector2 point)
    {
        var bearing = hub.LengthSquared() < 1000f * 1000f ? _random.NextFloat(MathF.Tau) : MathF.Atan2(hub.Y, hub.X);
        for (var i = 0; i < 8; i++)
        {
            var angle = bearing + _random.NextFloat(-EdgeSpread, EdgeSpread);
            point = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * _random.NextFloat(EdgeMin, EdgeMax);
            if (_scheduler.IsClearSpace(map, point, SpawnClearance, 0f))
                return true;
        }

        point = default;
        return false;
    }

    /// <summary>The orders that fly a reinforcement to the hub's standoff and dock it with the hub.</summary>
    private List<WFCrewObjective> DockOrders(EntityUid hubGrid, Vector2 hubAt)
    {
        return new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = hubAt, Range = HubStandoff },
            new() { Kind = WFCrewObjectiveKind.Dock, Target = GetNetEntity(hubGrid) },
        };
    }

    /// <summary>Runs <see cref="TendReinforcements"/> once, as a tick does.</summary>
    public void CheckReinforcements()
    {
        if (State() != null)
            TendReinforcements();
    }

    /// <summary>
    /// Stops reinforcements whose lead is out of the fight, joins those that have docked with their hub, holds
    /// those whose crew gave up docking beside it, orders again those whose flight ended short, ends those lost on
    /// the way, sends back to dock any that came apart and calls in any that chased an attacker too far from the hub.
    /// </summary>
    private void TendReinforcements()
    {
        var now = _timing.CurTime;
        foreach (var reinforcement in Reinforcements())
        {
            var (uid, comp, encounter) = reinforcement;
            if (!TryComp<WFReaverStrongholdComponent>(comp.Stronghold, out var home)
                || !TryComp<WFEncounterComponent>(comp.Stronghold, out var hubEncounter) || hubEncounter.Resolution != null)
            {
                _encounters.End(uid);
                continue;
            }

            if (!encounter.Ships.TryGetValue(comp.Lead, out var lead) || !_encounters.InFight(lead))
            {
                _encounters.Resolve(uid, WFEncounterResolution.Destroyed);
                continue;
            }

            // A hub with no hull left is broken by the stronghold check.
            if (!hubEncounter.Ships.TryGetValue(home.Lead, out var hub) || TerminatingOrDeleted(hub.Grid))
                continue;

            var docked = _docking.AreGridsDocked(lead.Grid, hub.Grid);
            switch (comp.Phase)
            {
                case WFReaverReinforcementPhase.Inbound:
                case WFReaverReinforcementPhase.Returning:
                    if (docked)
                    {
                        CompleteDock(uid);
                        break;
                    }

                    if (comp.DockFailed)
                        HoldBeside(uid);
                    else
                        TendArrival(reinforcement, lead, hub.Grid, now);
                    break;
                case WFReaverReinforcementPhase.Docked when !docked && !home.Scattered:
                    Return(reinforcement, hub.Grid);
                    break;
                case WFReaverReinforcementPhase.Sortie
                    when Vector2.Distance(_transform.GetWorldPosition(lead.Grid), _transform.GetWorldPosition(hub.Grid)) > SortieLeash:
                    Return(reinforcement, hub.Grid);
                    break;
            }
        }
    }

    /// <summary>
    /// Keeps a reinforcement that has not docked on its way: orders it again once its crew gave up the flight, gives
    /// one at the hub more time to finish docking, and ends one still short of the hub past its deadline.
    /// </summary>
    private void TendArrival(Entity<WFReaverReinforcementComponent, WFEncounterComponent> reinforcement,
        WFEncounterShipState lead, EntityUid hubGrid, TimeSpan now)
    {
        var (uid, comp, _) = reinforcement;
        var at = _transform.GetWorldPosition(lead.Grid);
        var hubAt = _transform.GetWorldPosition(hubGrid);
        if (lead.Flown)
        {
            if (now >= comp.NextOrders)
                Reorder(reinforcement, hubGrid, at, hubAt);

            return;
        }

        if (now < comp.Overdue)
            return;

        // At the hub it is still docking; the pilot's own attempts end in a failure that holds it.
        if (Vector2.Distance(at, hubAt) <= HubArrival)
        {
            comp.Overdue = now + DockPatience;
            return;
        }

        if (comp.Phase == WFReaverReinforcementPhase.Returning)
        {
            Reorder(reinforcement, hubGrid, at, hubAt);
            return;
        }

        Log.Info($"Reaver reinforcement {ToPrettyString(uid)} ran past its time short of its hub and was ended.");
        _encounters.End(uid);
    }

    /// <summary>Orders a reinforcement to fly to the hub and dock again, and moves its deadline to its reckoned arrival.</summary>
    private void Reorder(Entity<WFReaverReinforcementComponent, WFEncounterComponent> reinforcement, EntityUid hubGrid, Vector2 at, Vector2 hubAt)
    {
        var (uid, comp, _) = reinforcement;
        var now = _timing.CurTime;
        comp.NextOrders = now + ReorderDelay;
        var due = now + ConvoyEta(at, hubAt) * 2;
        if (due > comp.Overdue)
            comp.Overdue = due;

        _encounters.SetOrders(uid, comp.Lead, DockOrders(hubGrid, hubAt));
        Log.Info($"Reaver reinforcement {ToPrettyString(uid)} was ordered to dock again.");
    }

    /// <summary>Records that a reinforcement has docked with its hub: the hub grows to its level, the clock restarts and it is announced.</summary>
    public bool CompleteDock(EntityUid reinforcement)
    {
        return Land(reinforcement, WFReaverReinforcementPhase.Docked);
    }

    /// <summary>Sends a reinforcement whose crew gave up docking to hold beside the hub; it counts all the same.</summary>
    private bool HoldBeside(EntityUid reinforcement)
    {
        if (!TryComp<WFReaverReinforcementComponent>(reinforcement, out var comp)
            || !TryComp<WFEncounterComponent>(comp.Stronghold, out var hubEncounter)
            || !TryComp<WFReaverStrongholdComponent>(comp.Stronghold, out var home)
            || !hubEncounter.Ships.TryGetValue(home.Lead, out var hub) || TerminatingOrDeleted(hub.Grid))
            return false;

        _encounters.SetOrders(reinforcement, comp.Lead, new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = _transform.GetWorldPosition(hub.Grid), Range = HoldRange },
            new() { Kind = WFCrewObjectiveKind.Hold },
        });
        Log.Info($"Reaver reinforcement {ToPrettyString(reinforcement)} could not dock and holds beside its hub.");
        return Land(reinforcement, WFReaverReinforcementPhase.Holding);
    }

    /// <summary>Makes a reinforcement part of its stronghold in a phase; a stronghold under attack sends it out at once.</summary>
    private bool Land(EntityUid reinforcement, WFReaverReinforcementPhase phase)
    {
        if (State() is not { } state || CampaignFor(state) is not { } campaign
            || !TryComp<WFReaverReinforcementComponent>(reinforcement, out var comp)
            || !TryComp<WFEncounterComponent>(reinforcement, out var encounter) || encounter.Resolution != null
            || !TryComp<WFReaverStrongholdComponent>(comp.Stronghold, out var home)
            || !TryComp<WFEncounterComponent>(comp.Stronghold, out var hubEncounter))
            return false;

        comp.Phase = phase;
        if (comp.Landed)
            return true;

        comp.Landed = true;
        if (home.Inbound == reinforcement)
            home.Inbound = null;

        home.Reinforcements.Add(reinforcement);
        home.NextReinforcement = _timing.CurTime + campaign.ReinforcementInterval;
        SetLevel((comp.Stronghold, home, hubEncounter), comp.Level);
        Announce(campaign, Loc.GetString("wf-reaver-grown",
            ("callsign", WFSectorHex.Callsign(home.Cell)),
            ("level", home.Level)), true, chime: false);
        Log.Info($"Reaver stronghold {ToPrettyString(comp.Stronghold)} grew to level {home.Level}.");
        Recompute();

        // Arriving mid-fight, it goes straight out.
        if ((home.Scattered || UnderFire(home)) && hubEncounter.Ships.TryGetValue(home.Lead, out var hub) && !TerminatingOrDeleted(hub.Grid)
            && PickAttacker(home, hubEncounter, hub.Grid, null) is { } attacker && InAttackerReach(attacker, hub.Grid))
        {
            home.Attacker = attacker;
            home.Scattered = true;
            Sortie((reinforcement, comp, encounter), attacker);
        }

        return true;
    }

    /// <summary>
    /// The player ship a hub's reinforcements go for: of the <paramref name="fresh"/> ships that just hit it, the one
    /// with most hits. With none fresh, the last one chosen if it is still near the hub, else the top attacker near
    /// it, else the top attacker anywhere.
    /// </summary>
    private EntityUid? PickAttacker(WFReaverStrongholdComponent home, WFEncounterComponent encounter, EntityUid hubGrid, IReadOnlyCollection<EntityUid>? fresh)
    {
        var hits = new Dictionary<EntityUid, int>();
        foreach (var ((attacker, _), count) in encounter.ShipHits)
        {
            if (!TerminatingOrDeleted(attacker))
                hits[attacker] = hits.GetValueOrDefault(attacker) + count;
        }

        var pool = fresh == null ? new List<KeyValuePair<EntityUid, int>>() : hits.Where(pair => fresh.Contains(pair.Key)).ToList();
        if (pool.Count == 0)
        {
            var hubAt = _transform.GetWorldPosition(hubGrid);
            bool Near(EntityUid ship) => Vector2.Distance(_transform.GetWorldPosition(ship), hubAt) <= AttackerLeash;
            if (home.Attacker is { } last && hits.ContainsKey(last) && Near(last))
                return last;

            pool = hits.Where(pair => Near(pair.Key)).ToList();
            if (pool.Count == 0)
                pool = hits.ToList();
        }

        return pool.Count == 0 ? null : pool.OrderByDescending(pair => pair.Value).First().Key;
    }

    /// <summary>Whether a ship is near enough its target hub to be sent after, so a reinforcement is not sent out only to be called back by its leash.</summary>
    private bool InAttackerReach(EntityUid attacker, EntityUid hubGrid)
    {
        return Vector2.Distance(_transform.GetWorldPosition(attacker), _transform.GetWorldPosition(hubGrid)) <= AttackerLeash;
    }

    /// <summary>Breaks the reinforcements of a hub under attack away: each undocks and attacks the ship that is hitting the hub; ones already out turn on a new attacker.</summary>
    public void ScatterReinforcements(EntityUid stronghold, IReadOnlyCollection<EntityUid>? fresh = null)
    {
        if (!TryComp<WFReaverStrongholdComponent>(stronghold, out var home)
            || !TryComp<WFEncounterComponent>(stronghold, out var hubEncounter)
            || !hubEncounter.Ships.TryGetValue(home.Lead, out var hub) || TerminatingOrDeleted(hub.Grid)
            || PickAttacker(home, hubEncounter, hub.Grid, fresh) is not { } attacker || !InAttackerReach(attacker, hub.Grid))
            return;

        home.Attacker = attacker;
        foreach (var uid in home.Reinforcements.ToList())
        {
            if (!TryComp<WFReaverReinforcementComponent>(uid, out var comp) || !TryComp<WFEncounterComponent>(uid, out var encounter)
                || encounter.Resolution != null)
                continue;

            if (comp.Phase != WFReaverReinforcementPhase.Sortie)
            {
                Sortie((uid, comp, encounter), attacker);
                home.Scattered = true;
            }
            else if (comp.Target != attacker)
            {
                comp.Target = attacker;
                _encounters.SetOrders(uid, comp.Lead, new List<WFCrewObjective>
                {
                    new() { Kind = WFCrewObjectiveKind.Attack, Target = GetNetEntity(attacker) },
                });
            }
        }
    }

    /// <summary>Undocks a reinforcement and sets it on a ship.</summary>
    private void Sortie(Entity<WFReaverReinforcementComponent, WFEncounterComponent> reinforcement, EntityUid attacker)
    {
        var (uid, comp, _) = reinforcement;
        comp.Phase = WFReaverReinforcementPhase.Sortie;
        comp.Target = attacker;
        _encounters.SetOrders(uid, comp.Lead, new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.Undock },
            new() { Kind = WFCrewObjectiveKind.Attack, Target = GetNetEntity(attacker) },
        });
        Log.Info($"Reaver reinforcement {ToPrettyString(uid)} breaks away to attack {ToPrettyString(attacker)}.");
    }

    /// <summary>
    /// Brings back the reinforcements that broke away once the hub has gone three minutes with no hit and no ship
    /// that hit it within 2.5 km; with <paramref name="force"/>, at once.
    /// </summary>
    public void RegroupReinforcements(EntityUid stronghold, bool force)
    {
        if (!TryComp<WFReaverStrongholdComponent>(stronghold, out var home) || !home.Scattered
            || !TryComp<WFEncounterComponent>(stronghold, out var hubEncounter)
            || !hubEncounter.Ships.TryGetValue(home.Lead, out var hub) || TerminatingOrDeleted(hub.Grid))
            return;

        if (!force)
        {
            var hubAt = _transform.GetWorldPosition(hub.Grid);
            if (_timing.CurTime - home.LastHit < ReturnDelay
                || hubEncounter.ShipHits.Keys.Select(key => key.Attacker).Distinct().Any(attacker => !TerminatingOrDeleted(attacker)
                    && Vector2.Distance(_transform.GetWorldPosition(attacker), hubAt) <= ReturnClearance))
                return;
        }

        home.Scattered = false;
        foreach (var uid in home.Reinforcements.ToList())
        {
            if (TryComp<WFReaverReinforcementComponent>(uid, out var comp) && TryComp<WFEncounterComponent>(uid, out var encounter)
                && encounter.Resolution == null && comp.Phase == WFReaverReinforcementPhase.Sortie)
                Return((uid, comp, encounter), hub.Grid);
        }
    }

    /// <summary>Sends a reinforcement back to the hub's standoff to dock again.</summary>
    private void Return(Entity<WFReaverReinforcementComponent, WFEncounterComponent> reinforcement, EntityUid hubGrid)
    {
        var (uid, comp, encounter) = reinforcement;
        var hubAt = _transform.GetWorldPosition(hubGrid);
        comp.Phase = WFReaverReinforcementPhase.Returning;
        comp.Target = null;
        comp.DockFailed = false;
        comp.NextOrders = _timing.CurTime + ReorderDelay;
        comp.Overdue = _timing.CurTime + (encounter.Ships.TryGetValue(comp.Lead, out var lead)
            ? ConvoyEta(_transform.GetWorldPosition(lead.Grid), hubAt) * 2 + ConvoySlack
            : ConvoySlack);
        _encounters.SetOrders(uid, comp.Lead, DockOrders(hubGrid, hubAt));
        Log.Info($"Reaver reinforcement {ToPrettyString(uid)} returns to its hub.");
    }

    /// <summary>Ends the reinforcements of a hub that is gone; with <paramref name="fold"/>, the hits on them count toward the hub's bounty.</summary>
    private void SweepReinforcements(Entity<WFReaverStrongholdComponent> stronghold, bool fold)
    {
        var (uid, comp) = stronghold;
        foreach (var reinforcement in Reinforcements().Where(entry => entry.Comp1.Stronghold == uid))
        {
            if (fold && TryComp<WFEncounterComponent>(uid, out var hub))
                FoldHits(hub, reinforcement.Comp2);

            _encounters.End(reinforcement.Owner);
        }

        comp.Reinforcements.Clear();
        comp.Inbound = null;
    }

    /// <summary>Adds the hits a reinforcement took to a hub's, so those who shot at either are paid for the stronghold.</summary>
    private static void FoldHits(WFEncounterComponent hub, WFEncounterComponent reinforcement)
    {
        foreach (var (user, bySide) in reinforcement.Hits)
        {
            if (!hub.Hits.TryGetValue(user, out var into))
                hub.Hits[user] = into = new Dictionary<string, int>();

            foreach (var (side, hits) in bySide)
            {
                into[side] = into.GetValueOrDefault(side) + hits;
            }
        }

        foreach (var (key, hits) in reinforcement.ShipHits)
        {
            hub.ShipHits[key] = hub.ShipHits.GetValueOrDefault(key) + hits;
        }
    }

    /// <summary>Drops a reinforcement from its hub; one lost on the way lets the hub try again after the interval; destroyed, it pays the patrol bounty.</summary>
    private void ReinforcementResolved(Entity<WFReaverReinforcementComponent> reinforcement, WFEncounterResolution resolution)
    {
        var (uid, comp) = reinforcement;
        var state = State();
        var campaign = CampaignFor(state);
        WFReaverStrongholdComponent? home = null;
        if (TryComp(comp.Stronghold, out home))
        {
            home.Reinforcements.Remove(uid);
            if (home.Inbound == uid)
            {
                home.Inbound = null;
                home.NextReinforcement = _timing.CurTime + (campaign?.ReinforcementInterval ?? ReinforceRetry);
            }
        }

        if (resolution != WFEncounterResolution.Destroyed || campaign == null || state == null || home == null
            || !TryComp<WFEncounterComponent>(uid, out var encounter))
            return;

        Mark(encounter.ShipHits.Keys.Select(key => key.Attacker));
        var (bounty, paid) = PayReinforcementBounty(campaign, uid);
        Announce(campaign, Loc.GetString("wf-reaver-reinforcement-destroyed",
            ("callsign", WFSectorHex.Callsign(home.Cell)),
            ("bounty", bounty),
            ("paid", paid)), false, chime: false);
    }
}
