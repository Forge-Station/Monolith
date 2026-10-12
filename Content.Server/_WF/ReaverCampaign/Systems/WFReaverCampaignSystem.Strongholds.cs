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

            if (convoys == 0 && FoundingDue(campaign, state) is { } due && now >= due && now >= _nextFoundTry && !TryFound(out _))
                _nextFoundTry = now + FoundRetry;
            return;
        }

        state.NoStrongholdSince = null;
        if (convoys > 0
            || standing.Count + convoys >= campaign.MaxStrongholds
            || _territory.Held(_territory.SectorMap, campaign.Faction).Count < campaign.CellsPerStronghold * standing.Count
            || state.LastFounded is { } last && now < last + campaign.FoundInterval
            || now < _nextFoundTry)
            return;

        if (!TryLaunchConvoy(out _))
            _nextFoundTry = now + FoundRetry;
    }

    /// <summary>When a founding is due with none standing: the first after the stations generate, a regroup after the last stronghold fell; null if that clock has not started.</summary>
    public TimeSpan? FoundingDue(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent? state)
    {
        if (state == null || state.Founded == 0)
            return _stationsAt is { } at ? at + campaign.FirstStrongholdDelay : null;

        return state.NoStrongholdSince is { } since ? since + campaign.RegroupDelay : null;
    }

    /// <summary>Founds a stronghold at once where the rules put it: a quiet cell far out with none standing, otherwise a held frontier cell. The campaign sends a convoy for the latter.</summary>
    public bool TryFound(out EntityUid stronghold)
    {
        stronghold = default;
        if (Campaign is not { } campaign || State(true) is not { } state)
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

    /// <summary>Founds a level 0 hub at a point, shields it, claims its cell and ring and announces it.</summary>
    public bool TryFoundAt(MapCoordinates at, out EntityUid stronghold, ProtoId<WFEncounterPrototype>? prototype = null)
    {
        stronghold = default;
        if (Campaign is not { } campaign || State(true) is not { } state
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

    /// <summary>Makes an encounter a stronghold in a cell: claims the cell and ring, counts the founding and announces it at a point.</summary>
    private void Establish(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state, EntityUid uid, string lead,
        WFSectorCell cell, MapCoordinates at, bool regroup)
    {
        var now = _timing.CurTime;
        var comp = EnsureComp<WFReaverStrongholdComponent>(uid);
        comp.Cell = cell;
        comp.Tier = state.Tier;
        comp.Lead = lead;
        comp.NextPatrol = now + FirstPatrolDelay;
        comp.NextReinforcement = now + campaign.ReinforcementInterval;
        if (TryComp<WFEncounterComponent>(uid, out var encounter))
            SetLevel((uid, comp, encounter), 0);

        ClaimCells(campaign, at.MapId, cell, uid);
        state.Founded++;
        state.LastFounded = now;
        state.NoStrongholdSince = null;
        state.Defeated = false;
        Recompute();
        AnnouncePlace(campaign, regroup ? "wf-reaver-regrouped" : "wf-reaver-founded", true, at.MapId, at.Position,
            ("callsign", WFSectorHex.Callsign(cell)),
            ("bounty", ByTier(campaign.StrongholdBounty, state.Tier, 0)));
    }

    /// <summary>Claims a stronghold's cell and ring for it; a station cell in the ring is contested instead.</summary>
    private void ClaimCells(WFReaverCampaignPrototype campaign, MapId map, WFSectorCell cell, EntityUid stronghold)
    {
        var cells = new List<WFSectorCell> { cell };
        cells.AddRange(WFSectorHex.Ring(cell, 1));
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
            ClaimCells(campaign, map, comp.Cell, uid);
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
                _encounters.Resolve(uid, LostResolution(encounter, comp.Lead));
                continue;
            }

            var hits = encounter.ShipHits.Values.Sum();
            if (hits > comp.Hits)
            {
                comp.LastHit = _timing.CurTime;
                var fresh = MarkAttackers(comp, encounter);
                ScatterReinforcements(uid, fresh);
                RespondToHit(stronghold, fresh);
            }

            comp.Hits = hits;
            if (comp.Hits == 0)
                continue;

            if (comp.Scattered)
                RegroupReinforcements(uid, false);

            if (_timing.CurTime - comp.LastHit <= SupportWindow)
                TrackSupport(comp, encounter, _transform.GetWorldPosition(lead.Grid), ships, elapsed);
        }
    }

    /// <summary>Frees a stronghold's cells; broken, it pays its bounty and is announced.</summary>
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
        AfterStrongholdGone(state);
        var position = Position(encounter, comp.Lead);
        AnnouncePlace(campaign, "wf-reaver-broken", false, Transform(uid).MapID, position,
            ("callsign", WFSectorHex.Callsign(comp.Cell)),
            ("bounty", bounty),
            ("paid", paid),
            ("cells", freed));
        AnnounceDefeat(campaign, state);
    }

    /// <summary>Announces the end of the campaign when the last stronghold has broken with no convoy out to found another.</summary>
    private void AnnounceDefeat(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        if (Strongholds().Count > 0 || Convoys().Count > 0)
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
