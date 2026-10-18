using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.SectorControl;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Content.Shared.Ghost;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>How far from the origin the first stronghold and a regrouped one are founded.</summary>
    private const float FirstMinDistance = 14000f;
    private const float FirstMaxDistance = 24000f;

    /// <summary>How far from any living player the first stronghold and a regrouped one are founded.</summary>
    private const float PlayerClearance = 8000f;

    /// <summary>Space free of grids around a stronghold's centre.</summary>
    private const float GridClearance = 600f;

    /// <summary>Distance kept from stations by the first stronghold and by later ones.</summary>
    private const float FirstStationClearance = 6000f;
    private const float LaterStationClearance = 3000f;

    /// <summary>How long after its founding a stronghold may send its first patrol.</summary>
    private static readonly TimeSpan FirstPatrolDelay = TimeSpan.FromMinutes(2);

    /// <summary>How long a hub may sit out of power unattacked before it is broken anyway: four restocks' worth.</summary>
    public static readonly TimeSpan StarvedGrace = TimeSpan.FromMinutes(20);

    /// <summary>Founds the first stronghold or a regrouped one far out, or sends a convoy from a standing one to found a later one; one founding at a time.</summary>
    private void TickFounding(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        var now = _timing.CurTime;
        var standing = Strongholds();
        var convoys = Convoys().Count;
        if (standing.Count == 0)
        {
            if (state.Founded > 0)
                state.NoStrongholdSince ??= now;

            if (convoys == 0 && !state.Finished && FoundingDue(campaign, state) is { } due && now >= due && now >= _nextFoundTry
                && !TryFound(out _))
                _nextFoundTry = now + FoundRetry;
            return;
        }

        // The citadel and its convoy stand outside the stronghold cap and the one-convoy rule.
        state.NoStrongholdSince = null;
        var ordinary = OrdinaryConvoys();
        if (ordinary > 0
            || state.Finished
            || standing.Count(stronghold => !stronghold.Comp1.Citadel) + ordinary >= campaign.MaxStrongholds
            || _territory.Held(_territory.SectorMap, campaign.Faction).Count < campaign.CellsPerStronghold * standing.Count
            || state.LastFounded is { } last && now < last + campaign.FoundInterval
            || now < _nextFoundTry)
            return;

        if (!TryLaunchConvoy(out _))
            _nextFoundTry = now + FoundRetry;
    }

    /// <summary>When a founding is due with none standing: the first after the stations generate, a regroup after the last stronghold fell; null if that clock has not started or the Reavers are finished.</summary>
    public TimeSpan? FoundingDue(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent? state)
    {
        if (state is { Finished: true })
            return null;

        if (state == null || state.Founded == 0)
            return _stationsAt is { } at ? at + campaign.FirstStrongholdDelay : null;

        return state.NoStrongholdSince is { } since ? since + campaign.RegroupDelay : null;
    }

    /// <summary>Founds a stronghold at once where the rules put it: a quiet cell far out with none standing, otherwise a held frontier cell; none once finished. The campaign sends a convoy for the latter.</summary>
    public bool TryFound(out EntityUid stronghold)
    {
        stronghold = default;
        if (Campaign is not { } campaign || State(true) is not { } state || state.Finished)
            return false;

        var standing = Strongholds();
        if (standing.Count == 0)
        {
            return TryFirstCell(campaign, out var first)
                && TryFoundAt(_territory.Centre(_territory.SectorMap, first), out stronghold);
        }

        return TryLaterCell(campaign, Occupied(standing), out var later)
            && TryFoundAt(_territory.Centre(_territory.SectorMap, later), out stronghold);
    }

    /// <summary>Founds a level 0 hub at a point, shields it, claims its cell and ring and announces it; none once finished.</summary>
    public bool TryFoundAt(MapCoordinates at, out EntityUid stronghold, ProtoId<WFEncounterPrototype>? prototype = null)
    {
        stronghold = default;
        if (Campaign is not { } campaign || State(true) is not { } state || state.Finished
            || !_prototypes.TryIndex(prototype ?? campaign.Stronghold, out var encounterPrototype)
            || encounterPrototype.Ships.Count == 0)
            return false;

        var regroup = state.Founded > 0 && Strongholds().Count == 0;
        if (!SpawnEncounter(encounterPrototype, at, out var uid))
            return false;

        ShieldHub(campaign, uid, encounterPrototype.Ships[0].Key);
        Establish(campaign, state, uid, encounterPrototype.Ships[0].Key, _territory.CellOf(at), at, regroup);
        stronghold = uid;
        return true;
    }

    /// <summary>Anchors a shield generator on the hub's lead hull if it has none.</summary>
    private void ShieldHub(WFReaverCampaignPrototype campaign, EntityUid encounter, string lead)
    {
        if (TryComp<WFEncounterComponent>(encounter, out var comp) && comp.Ships.TryGetValue(lead, out var ship))
            EnsureShield(campaign, ship.Grid);
    }

    /// <summary>Makes an encounter a stronghold, or the citadel, in a cell: claims the cell and its rings, counts the founding and announces it at a point.</summary>
    private void Establish(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state, EntityUid uid, string lead,
        WFSectorCell cell, MapCoordinates at, bool regroup, bool citadel = false)
    {
        var now = _timing.CurTime;
        var comp = EnsureComp<WFReaverStrongholdComponent>(uid);
        comp.Cell = cell;
        comp.Tier = state.Tier;
        comp.Lead = lead;
        comp.NextPatrol = now + FirstPatrolDelay;
        comp.NextReinforcement = now + campaign.ReinforcementInterval;
        comp.Citadel = citadel;
        if (citadel)
            comp.BaseName = Loc.GetString("wf-reaver-name-citadel", ("designation", WFSectorHex.Callsign(cell)));

        if (TryComp<WFEncounterComponent>(uid, out var encounter))
            SetLevel((uid, comp, encounter), 0);

        ClaimCells(campaign, at.MapId, cell, uid, citadel ? campaign.CitadelRings : 1);
        state.Founded++;
        state.NoStrongholdSince = null;
        state.Defeated = false;

        // The citadel stands outside the founding interval, so it does not hold back the next stronghold.
        if (citadel)
            state.Citadels++;
        else
            state.LastFounded = now;

        Recompute();
        var bounty = ByTier(campaign.StrongholdBounty, state.Tier, 0);
        AnnouncePlace(campaign, citadel ? "wf-reaver-citadel-raised" : regroup ? "wf-reaver-regrouped" : "wf-reaver-founded", true,
            at.MapId, at.Position,
            ("callsign", WFSectorHex.Callsign(cell)),
            ("bounty", citadel ? (int) (bounty * campaign.CitadelBountyScale) : bounty));
        if (citadel)
            PlayCapital(campaign);
    }

    /// <summary>Claims a stronghold's cell and its rings for it; a station cell among them is contested instead.</summary>
    private void ClaimCells(WFReaverCampaignPrototype campaign, MapId map, WFSectorCell cell, EntityUid stronghold, int rings = 1)
    {
        var cells = WFSectorHex.Within(cell, Math.Max(1, rings));
        foreach (var claimed in cells)
        {
            if (_territory.Owner(map, claimed) == null && BlockadeTargets(campaign, map, claimed) is { Count: > 0 } stations)
                TryBlockade(campaign, map, claimed, stronghold, stations[0]);
            else
                _territory.TryClaim(map, claimed, campaign.Faction, stronghold);
        }
    }

    /// <summary>Claims each standing stronghold's cells again after sector control was switched back on or resized, and re-aims the convoys.</summary>
    private void OnSectorReset(ref WFSectorResetEvent args)
    {
        if (Campaign is not { } campaign)
            return;

        var map = _territory.SectorMap;
        foreach (var (uid, comp, encounter) in Strongholds())
        {
            comp.Cell = _territory.CellOf(new MapCoordinates(Position(encounter, comp.Lead), map));
            ClaimCells(campaign, map, comp.Cell, uid, comp.Citadel ? campaign.CitadelRings : 1);
        }

        // A convoy's cell is found again from its point; the next tick contests it.
        foreach (var (_, comp, _) in Convoys())
        {
            comp.Target = _territory.CellOf(new MapCoordinates(comp.TargetPosition, map));
        }
    }

    /// <summary>A random quiet, claimable cell 14 to 24 km out, its centre clear and 8 km from any living player.</summary>
    private bool TryFirstCell(WFReaverCampaignPrototype campaign, out WFSectorCell cell)
    {
        cell = default;
        var map = _territory.SectorMap;
        var quietSince = _timing.CurTime - TimeSpan.FromMinutes(campaign.QuietMinutes);
        var players = PlayerPositions(map);
        var rings = (int) (FirstMaxDistance / (_territory.CellSize * 1.5f)) + 2;
        var candidates = new List<WFSectorCell>();
        foreach (var candidate in WFSectorHex.Within(WFSectorCell.Origin, rings))
        {
            var centre = WFSectorHex.Centre(candidate, _territory.CellSize);
            var distance = centre.Length();
            if (distance < FirstMinDistance || distance > FirstMaxDistance
                || _territory.Owner(map, candidate) != null || _territory.ContestOf(map, candidate) != null
                || !_activity.IsQuiet(map, candidate, quietSince)
                || players.Any(player => Vector2.DistanceSquared(player, centre) < PlayerClearance * PlayerClearance)
                || !_territory.CanClaim(map, candidate, campaign.Faction))
                continue;

            candidates.Add(candidate);
        }

        _random.Shuffle(candidates);
        foreach (var candidate in candidates)
        {
            if (!_scheduler.IsClearSpace(map, WFSectorHex.Centre(candidate, _territory.CellSize), GridClearance, FirstStationClearance))
                continue;

            cell = candidate;
            return true;
        }

        return false;
    }

    /// <summary>The cells of the standing strongholds and of the convoys' targets, which later strongholds keep two cells from.</summary>
    private List<WFSectorCell> Occupied(List<Entity<WFReaverStrongholdComponent, WFEncounterComponent>> standing)
    {
        var cells = standing.Select(stronghold => stronghold.Comp1.Cell).ToList();
        cells.AddRange(Convoys().Select(convoy => convoy.Comp1.Target));
        return cells;
    }

    /// <summary>A held cell on the edge of held space, two cells from every occupied one, with a clear centre; nearest the core first.</summary>
    private bool TryLaterCell(WFReaverCampaignPrototype campaign, List<WFSectorCell> occupied, out WFSectorCell cell)
    {
        cell = default;
        var map = _territory.SectorMap;
        var held = _territory.Held(map, campaign.Faction).ToHashSet();
        var candidates = new List<WFSectorCell>();
        foreach (var candidate in held)
        {
            var edge = false;
            for (var direction = 0; direction < 6; direction++)
            {
                if (!held.Contains(candidate.Neighbour(direction)))
                    edge = true;
            }

            if (edge && occupied.All(other => WFSectorHex.Distance(other, candidate) >= 2))
                candidates.Add(candidate);
        }

        _random.Shuffle(candidates);
        foreach (var candidate in candidates.OrderBy(entry => WFSectorHex.Distance(entry, WFSectorCell.Origin)))
        {
            if (!_scheduler.IsClearSpace(map, WFSectorHex.Centre(candidate, _territory.CellSize), GridClearance, LaterStationClearance))
                continue;

            cell = candidate;
            return true;
        }

        return false;
    }

    /// <summary>Where the living, non-ghost players on a map are.</summary>
    private List<Vector2> PlayerPositions(MapId map)
    {
        var positions = new List<Vector2>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid))
                positions.Add(_transform.GetWorldPosition(xform));
        }

        return positions;
    }

    /// <summary>Runs <see cref="TendStrongholds"/> once, as a tick does.</summary>
    public void CheckStrongholds()
    {
        if (State() is { } state)
            TendStrongholds(state, Crewed(), 1f);
    }

    /// <summary>Breaks strongholds whose lead is out of the fight, counts hits, marks the ships that land them, answers each fresh hit with a sortie pack and the patrols nearby, breaks reinforcements away from a hub under attack and brings them back, and times support.</summary>
    private void TendStrongholds(WFReaverCampaignComponent state, HashSet<EntityUid> ships, float elapsed)
    {
        foreach (var stronghold in Strongholds())
        {
            var (uid, comp, encounter) = stronghold;
            if (!encounter.Ships.TryGetValue(comp.Lead, out var lead) || !_encounters.InFight(lead))
            {
                // A hub that went dark with nobody shooting at it has not been beaten: its engineer and the restock
                // bring the power back. One that stays dark past the grace is broken all the same.
                if (lead != null && encounter.ShipHits.Count == 0 && comp.NpcHits.Count == 0 && _encounters.OutForWantOfPower(lead))
                {
                    if (!comp.Starved)
                    {
                        Log.Info($"Reaver stronghold {ToPrettyString(uid)} is out of power with no attack on it and is left standing.");
                        comp.StarvedSince = _timing.CurTime;
                    }

                    comp.Starved = true;
                    if (_timing.CurTime - comp.StarvedSince < StarvedGrace)
                        continue;

                    Log.Info($"Reaver stronghold {ToPrettyString(uid)} has been out of power past the grace and is broken.");
                }

                // Brought down by another campaign's guns, it is beaten all the same.
                _encounters.Resolve(uid, comp.NpcHits.Count > 0 ? WFEncounterResolution.Destroyed : LostResolution(encounter, comp.Lead));
                continue;
            }

            comp.Starved = false;
            var playerHits = encounter.ShipHits.Values.Sum();
            var hits = playerHits + comp.NpcHits.Values.Sum();
            if (hits > comp.Hits)
            {
                comp.LastHit = _timing.CurTime;
                var fresh = MarkAttackers(comp, encounter);
                fresh.AddRange(FreshNpcAttackers(comp));
                ScatterReinforcements(uid, fresh);
                RespondToHit(stronghold, fresh);
            }

            comp.Hits = hits;
            if (comp.Hits == 0)
                continue;

            if (comp.Scattered)
                RegroupReinforcements(uid, false);

            // Standing by only counts against a player assault, as only a player's hits pay.
            if (playerHits > 0 && _timing.CurTime - comp.LastHit <= SupportWindow)
                TrackSupport(comp, encounter, _transform.GetWorldPosition(lead.Grid), ships, elapsed);
        }
    }

    /// <summary>
    /// Counts a ship weapon hit by another campaign's ship on a stronghold's hub or one of its reinforcements, so the
    /// hub breaks its reinforcements away and sends a sortie at it as it would at a player ship. No bounty or mark.
    /// </summary>
    public bool RecordCampaignHit(EntityUid victim, EntityUid attacker)
    {
        if (!TryComp<WFEncounterGridComponent>(victim, out var marker) || TerminatingOrDeleted(attacker))
            return false;

        var owner = marker.Encounter;
        if (TryComp<WFReaverReinforcementComponent>(owner, out var reinforcement))
            owner = reinforcement.Stronghold;

        if (!TryComp<WFReaverStrongholdComponent>(owner, out var stronghold)
            || !TryComp<WFEncounterComponent>(owner, out var encounter) || encounter.Resolution != null)
            return false;

        stronghold.NpcHits[attacker] = stronghold.NpcHits.GetValueOrDefault(attacker) + 1;
        return true;
    }

    /// <summary>The other campaigns' ships that have hit a stronghold since it was last looked at.</summary>
    private List<EntityUid> FreshNpcAttackers(WFReaverStrongholdComponent stronghold)
    {
        var fresh = new List<EntityUid>();
        foreach (var (attacker, hits) in stronghold.NpcHits)
        {
            if (hits <= stronghold.AttackerHits.GetValueOrDefault(attacker))
                continue;

            stronghold.AttackerHits[attacker] = hits;
            fresh.Add(attacker);
        }

        return fresh;
    }

    /// <summary>Frees a stronghold's cells; broken, it pays its bounty and is announced, and a fallen citadel finishes the Reavers.</summary>
    private void StrongholdResolved(Entity<WFReaverStrongholdComponent> stronghold, WFEncounterResolution resolution)
    {
        var (uid, comp) = stronghold;
        var state = State();
        var campaign = CampaignFor(state);
        if (resolution != WFEncounterResolution.Destroyed || campaign == null || state == null
            || !TryComp<WFEncounterComponent>(uid, out var encounter))
        {
            SweepReinforcements(stronghold, false);
            _territory.Release(uid);
            AfterStrongholdGone(state);
            return;
        }

        SweepReinforcements(stronghold, true);
        var (bounty, paid) = PayStrongholdBounty(campaign, state, (uid, comp, encounter));
        var freed = _territory.Release(uid);
        state.Broken++;
        if (comp.Citadel)
        {
            state.CitadelsBroken++;
            state.Finished = true;
            Log.Info($"Reaver citadel {ToPrettyString(uid)} fell; the Reavers are finished for the round.");

            // A convoy under way would found a stronghold; it is recalled.
            foreach (var (convoy, _, _) in Convoys())
            {
                _encounters.End(convoy);
            }
        }

        AfterStrongholdGone(state);
        var position = Position(encounter, comp.Lead);
        AnnouncePlace(campaign, comp.Citadel ? "wf-reaver-citadel-fallen" : "wf-reaver-broken", false, Transform(uid).MapID, position,
            ("callsign", WFSectorHex.Callsign(comp.Cell)),
            ("bounty", bounty),
            ("paid", paid),
            ("cells", freed));
        AnnounceDefeat(campaign, state);
    }

    /// <summary>Announces the end of the campaign when the last stronghold has broken with no convoy out to found another; a finished campaign has had its ending.</summary>
    private void AnnounceDefeat(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        if (state.Finished || Strongholds().Count > 0 || Convoys().Count > 0)
            return;

        state.Defeated = true;
        state.Defeats++;
        Announce(campaign, Loc.GetString("wf-reaver-defeated"), false);
        Recompute();
    }

    /// <summary>Starts the regroup clock when the last stronghold goes, and rescores.</summary>
    private void AfterStrongholdGone(WFReaverCampaignComponent? state)
    {
        if (state == null)
            return;

        if (Strongholds().Count == 0 && state.Founded > 0)
            state.NoStrongholdSince ??= _timing.CurTime;

        if (Campaign != null)
            Recompute();
    }
}
