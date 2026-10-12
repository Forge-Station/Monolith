using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.SectorControl.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>One raid out per this many players, within the cap.</summary>
    private const int PlayersPerRaid = 8;
    private const int MaxRaids = 4;

    /// <summary>Every this many raids, one goes for a station.</summary>
    private const int StationRaidEvery = 3;

    /// <summary>The tier from which station raids go for the core.</summary>
    private const int CoreRaidTier = 4;

    /// <summary>How long a raided ship or station is left alone.</summary>
    private static readonly TimeSpan RaidCooldown = TimeSpan.FromMinutes(45);

    /// <summary>How near a raider pack closes on its target before it attacks.</summary>
    private const float RaidApproach = 600f;

    /// <summary>How far off a station's hull a station raid stands, and how far round it the raiders prowl.</summary>
    private const float StationStandoff = 600f;
    private const float ProwlMin = 600f;
    private const float ProwlMax = 1500f;
    private const int ProwlLegs = 4;

    /// <summary>The campaign-wide raid clock: from the raid tier, one raid each interval, within the cap.</summary>
    private void TickRaids(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        var now = _timing.CurTime;
        if (state.Tier < campaign.RaidTier || campaign.RaidInterval.Count == 0)
        {
            state.NextRaid = null;
            return;
        }

        var interval = ByTier(campaign.RaidInterval, state.Tier - campaign.RaidTier, TimeSpan.FromMinutes(40));
        if (state.NextRaid is not { } next)
        {
            state.NextRaid = now + interval;
            return;
        }

        if (now < next)
            return;

        state.NextRaid = now + interval;
        if (ActiveRaids() >= Math.Clamp(Players() / PlayersPerRaid, 1, MaxRaids))
            return;

        TryLaunchRaid();
    }

    /// <summary>Launches one raid now, whatever the clock and cap; false if nothing qualifies.</summary>
    public bool TryLaunchRaid()
    {
        if (Campaign is not { } campaign || State(true) is not { } state)
            return false;

        var standing = Strongholds();
        if (standing.Count == 0)
            return false;

        var map = _territory.SectorMap;
        var held = _territory.Held(map, campaign.Faction).ToHashSet();
        if (held.Count == 0)
            return false;

        if (state.Raids % StationRaidEvery == StationRaidEvery - 1 && TryStationRaid(campaign, state, standing, map, held))
            return true;

        return TryShipRaid(campaign, state, standing, map, held);
    }

    private bool TryShipRaid(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state,
        List<Entity<WFReaverStrongholdComponent, WFEncounterComponent>> standing, MapId map, HashSet<WFSectorCell> held)
    {
        var now = _timing.CurTime;
        var docked = DockedGrids();
        var crews = Crews(Crewed());
        var candidates = new List<(EntityUid Ship, float Weight)>();
        foreach (var (ship, crew) in crews)
        {
            // A marked ship is hunted wherever it is on the map.
            var marked = IsMarked(ship);
            if (docked.Contains(ship) || _scheduler.IsSecretPlace(ship) || state.Raided.TryGetValue(ship, out var last) && now - last < RaidCooldown
                || !marked && !InOrBeside(held, _territory.GridCell(ship)))
                continue;

            candidates.Add((ship, marked ? crew * MarkWeight : crew));
        }

        if (PickWeighted(candidates) is not { } target)
            return false;

        var players = Players();
        var kinds = new List<ProtoId<WFEncounterPrototype>>();
        foreach (var id in new[] { campaign.RaidPack, campaign.RaidBoarders })
        {
            if (_prototypes.TryIndex(id, out var kind) && kind.Ships.Count > 0 && players >= kind.MinPlayers)
                kinds.Add(id);
        }

        if (kinds.Count == 0 || !_prototypes.TryIndex(_random.Pick(kinds), out var prototype))
            return false;

        var at = _transform.GetWorldPosition(target);
        if (!TryLaunchFrom(prototype, standing, map, at, out var uid, out var from))
            return false;

        var hunters = false;
        foreach (var ship in uid.Comp.Ships.Values)
        {
            if (!ship.Hunt)
                continue;

            // A hunter goes for this ship, not whichever is nearest its stronghold.
            ship.Prey = target;
            hunters = true;
        }

        if (!hunters && !_encounters.SetOrders(uid.Owner, prototype.Ships[0].Key, new List<WFCrewObjective>
            {
                new() { Kind = WFCrewObjectiveKind.GoTo, Position = at, Range = RaidApproach },
                new() { Kind = WFCrewObjectiveKind.Attack, Target = GetNetEntity(target) },
            }))
        {
            _encounters.End(uid.Owner);
            return false;
        }

        Raided(state, uid.Owner, target, from.Owner, prototype.Ships[0].Key, false);
        var hunted = IsMarked(target);
        WarnShip(campaign, map, target, _transform.GetWorldPosition(uid.Comp.Ships[prototype.Ships[0].Key].Grid),
            hunted ? "wf-reaver-raid-hunting" : "wf-reaver-raid-warning-pa",
            hunted ? "wf-reaver-raid-hunting" : "wf-reaver-raid-warning-radio");
        return true;
    }

    private bool TryStationRaid(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state,
        List<Entity<WFReaverStrongholdComponent, WFEncounterComponent>> standing, MapId map, HashSet<WFSectorCell> held)
    {
        if (!_prototypes.TryIndex(campaign.RaidStation, out var prototype) || prototype.Ships.Count == 0
            || Players() < prototype.MinPlayers)
            return false;

        if (StationTarget(state, map, held) is not { } station)
            return false;

        var at = _transform.GetWorldPosition(station);
        if (!TryLaunchFrom(prototype, standing, map, at, out var uid, out var from))
            return false;

        var lead = prototype.Ships[0].Key;
        var start = _transform.GetWorldPosition(uid.Comp.Ships[lead].Grid);
        var hull = station.Comp.LocalAABB.Size.Length() / 2f;
        var away = start - at;
        var standoff = at + (away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : Vector2.UnitY) * (hull + StationStandoff);
        var queue = new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = standoff, Range = RouteRange },
        };
        for (var i = 0; i < ProwlLegs; i++)
        {
            for (var tries = 0; tries < 4; tries++)
            {
                var point = at + _random.NextAngle().ToVec() * (hull + _random.NextFloat(ProwlMin, ProwlMax));
                if (!_scheduler.IsClearSpace(map, point, SpawnClearance, 0f))
                    continue;

                queue.Add(new WFCrewObjective { Kind = WFCrewObjectiveKind.GoTo, Position = point, Range = RouteRange });
                break;
            }
        }

        if (!_encounters.SetOrders(uid.Owner, lead, queue))
        {
            _encounters.End(uid.Owner);
            return false;
        }

        Raided(state, uid.Owner, station, from.Owner, lead, true);
        WarnStation(campaign, map, station, start);
        return true;
    }

    /// <summary>The station a station raid goes for: nearest held space, outside protected cells, a core station from the core tier.</summary>
    private Entity<MapGridComponent>? StationTarget(WFReaverCampaignComponent state, MapId map, HashSet<WFSectorCell> held)
    {
        var now = _timing.CurTime;
        var centres = held.Select(cell => WFSectorHex.Centre(cell, _territory.CellSize)).ToList();
        Entity<MapGridComponent>? best = null;
        var bestDistance = float.MaxValue;
        var bestCore = false;
        foreach (var station in _territory.Stations(map))
        {
            if (state.Raided.TryGetValue(station, out var last) && now - last < RaidCooldown || NeutralStation(station))
                continue;

            var cell = _territory.GridCell(station);
            var core = state.Tier >= CoreRaidTier
                && WFSectorHex.Distance(cell, WFSectorCell.Origin) <= WFSectorProtectionSystem.CoreRadius;
            if (!core && _protection.IsProtected(map, cell))
                continue;

            var position = _transform.GetWorldPosition(station);
            var distance = centres.Min(centre => Vector2.DistanceSquared(centre, position));
            // A core station, once the tier allows, beats any other; then the nearest.
            if (bestCore && !core || bestCore == core && distance >= bestDistance)
                continue;

            best = station;
            bestDistance = distance;
            bestCore = core;
        }

        return best;
    }

    /// <summary>Spawns a raid in clear space by the stronghold nearest its target.</summary>
    private bool TryLaunchFrom(WFEncounterPrototype prototype, List<Entity<WFReaverStrongholdComponent, WFEncounterComponent>> standing,
        MapId map, Vector2 target, out Entity<WFEncounterComponent> raid, out Entity<WFReaverStrongholdComponent, WFEncounterComponent> from)
    {
        raid = default;
        from = standing.MinBy(stronghold => Vector2.DistanceSquared(Position(stronghold.Comp2, stronghold.Comp1.Lead), target));
        if (!TryClearPoint(map, Position(from.Comp2, from.Comp1.Lead), PatrolSpawnMin, PatrolSpawnMax, SpawnClearance, out var spawn)
            || !SpawnEncounter(prototype, new MapCoordinates(spawn, map), out var uid, offBudget: true)
            || !TryComp<WFEncounterComponent>(uid, out var encounter))
            return false;

        raid = (uid, encounter);
        return true;
    }

    /// <summary>Marks a launched raid, its target's cooldown and the tallies.</summary>
    private void Raided(WFReaverCampaignComponent state, EntityUid raid, EntityUid target, EntityUid stronghold, string lead, bool station)
    {
        var comp = AddComp<WFReaverRaidComponent>(raid);
        comp.Lead = lead;
        comp.Target = target;
        comp.Station = station;
        comp.Stronghold = stronghold;
        state.Raids++;
        state.Raided[target] = _timing.CurTime;
        Log.Info($"Reaver raid {ToPrettyString(raid)} launched at {ToPrettyString(target)}.");
    }

    /// <summary>Breaks raids whose lead is out of the fight, so a wing does not escort a wreck and the raid cap is freed.</summary>
    private void TendRaids()
    {
        var query = EntityQueryEnumerator<WFReaverRaidComponent, WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var raid, out var encounter))
        {
            if (encounter.Resolution == null && !TerminatingOrDeleted(uid)
                && (!encounter.Ships.TryGetValue(raid.Lead, out var lead) || !_encounters.InFight(lead)))
                BreakLead(uid, encounter, raid.Lead);
        }
    }

    /// <summary>Whether a cell is held or next to a held one.</summary>
    private static bool InOrBeside(HashSet<WFSectorCell> held, WFSectorCell cell)
    {
        if (held.Contains(cell))
            return true;

        for (var direction = 0; direction < 6; direction++)
        {
            if (held.Contains(cell.Neighbour(direction)))
                return true;
        }

        return false;
    }

    /// <summary>The grids with a docking port docked to anything.</summary>
    private HashSet<EntityUid> DockedGrids()
    {
        var docked = new HashSet<EntityUid>();
        var docks = EntityQueryEnumerator<DockingComponent, TransformComponent>();
        while (docks.MoveNext(out _, out var dock, out var xform))
        {
            if (dock.DockedWith != null && xform.GridUid is { } grid)
                docked.Add(grid);
        }

        return docked;
    }
}
