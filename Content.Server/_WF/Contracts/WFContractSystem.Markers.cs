using System.Linq;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.Waypoints;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Contracts;
using Content.Shared._WF.Waypoints;
using Robust.Shared.Map;

namespace Content.Server._WF.Contracts;

/// <summary>Claim labels, crew waypoints and the interference penalty.</summary>
public sealed partial class WFContractSystem
{
    [Dependency] private WFWaypointSystem _waypoints = default!;

    /// <summary>Standing lost for firing on someone else's claimed target.</summary>
    private const int InterferencePenalty = 2;

    /// <summary>Labels a bounty's ships or a wreck with who holds the contract, as they appear.</summary>
    private void Claim(WFContract contract)
    {
        var claimant = Claimant(contract);
        if (contract.WreckGrid is { } wreck && contract.Claimed.Add(wreck) && !TerminatingOrDeleted(wreck))
            _meta.SetEntityName(wreck, Loc.GetString("wf-contract-claimed-name", ("name", Name(wreck)), ("claimant", claimant)));

        foreach (var twin in contract.ExtraWrecks)
        {
            if (contract.Claimed.Add(twin) && !TerminatingOrDeleted(twin))
                _meta.SetEntityName(twin, Loc.GetString("wf-contract-claimed-name", ("name", Name(twin)), ("claimant", claimant)));
        }

        if (_proto.Index(contract.Prototype).Objective is not WFContractBounty
            || contract.EncounterEntity is not { } encounter || !TryComp<WFEncounterComponent>(encounter, out var comp))
            return;

        foreach (var ship in comp.Ships.Values)
        {
            if (ship.Virtual || TerminatingOrDeleted(ship.Grid) || !contract.Claimed.Add(ship.Grid))
                continue;

            _meta.SetEntityName(ship.Grid, Loc.GetString("wf-contract-claimed-name", ("name", Name(ship.Grid)), ("claimant", claimant)));
        }
    }

    /// <summary>The holder's crew name, else the holder.</summary>
    private string Claimant(WFContract contract)
    {
        return contract.User is { } user && _crews.CrewNameOf(user) is { } crew ? crew : contract.Character;
    }

    /// <summary>Where a live contract's target is now: a ship, a wreck or cargo waiting for pickup.</summary>
    private MapCoordinates? MarkerPosition(WFContract contract)
    {
        switch (_proto.Index(contract.Prototype).Objective)
        {
            case WFContractBounty:
            case WFContractEscort:
                return TargetPosition(contract) is { } target && contract.Site is { } site ? new MapCoordinates(target, site.MapId) : contract.Site;
            case WFContractRecovery:
            case WFContractSurvey:
            case WFContractTow:
                return contract.WreckGrid is { } wreck && !TerminatingOrDeleted(wreck) ? _transform.GetMapCoordinates(wreck) : contract.Site;
            case WFContractCourier when contract.Origin is { } pickup:
                return contract.Items.FirstOrDefault(i => !TerminatingOrDeleted(i) && OnStation(i, pickup)) is { Valid: true } waiting
                    ? _transform.GetMapCoordinates(waiting)
                    : null;
            case WFContractSweep:
                return NextPoint(contract);
            case WFContractClear:
            case WFContractRepair:
            case WFContractDownload:
            case WFContractInspect:
            case WFContractOperation:
                return contract.WreckGrid is { } derelict && !TerminatingOrDeleted(derelict)
                    ? _transform.GetMapCoordinates(derelict)
                    : contract.Site;
        }

        return null;
    }

    /// <summary>Puts or moves each live contract's waypoint on its crew's ships; deleted ones stay deleted.</summary>
    private void UpdateMarkers()
    {
        foreach (var contract in _contracts.Values)
        {
            if (contract.Status != WFContractStatus.Active || contract.User is not { } user)
                continue;

            Claim(contract);
            if (MarkerPosition(contract) is not { } at)
                continue;

            var name = MarkerName(contract);
            var bodies = _crews.CrewInRound(user).Select(member => member.Body).ToList();
            if (MobOf(new WFContractHolder(user, contract.Character)) is { } holder)
                bodies.Add(holder);

            foreach (var body in bodies)
            {
                if (Transform(body).GridUid is not { } ship || !HasComp<ShuttleDeedComponent>(ship))
                    continue;

                var known = contract.Markers.TryGetValue(ship, out var id);
                if (known && id == null)
                    continue;

                var placed = _waypoints.Place(ship, id, name, at);
                if (placed != null || known)
                    contract.Markers[ship] = placed;
            }
        }
    }

    private string MarkerName(WFContract contract) => Loc.GetString("wf-contract-marker-name", ("id", contract.Id));

    /// <summary>A company's side: its contract faction, else itself; null for none.</summary>
    private string? SideOf(string? company)
    {
        if (string.IsNullOrEmpty(company) || company == "None")
            return null;

        foreach (var faction in _proto.EnumeratePrototypes<WFContractFactionPrototype>())
        {
            if (faction.Companies.Any(c => c.Id == company))
                return faction.ID;
        }

        return company;
    }

    /// <summary>Removes a contract's waypoints, from split-off pieces of its ships too.</summary>
    private void ClearMarkers(WFContract contract)
    {
        if (contract.Markers.Count == 0)
            return;

        var name = WFWaypointRules.CleanName(MarkerName(contract));
        var query = EntityQueryEnumerator<WFWaypointsComponent>();
        while (query.MoveNext(out var grid, out var waypoints))
        {
            foreach (var waypoint in waypoints.Waypoints.Where(w => w.Name == name).ToList())
            {
                _waypoints.Remove(grid, waypoint.Id);
            }
        }

        contract.Markers.Clear();
    }

    /// <summary>On a destroyed claimed target, everyone outside the crew and its sides who fought it loses standing.</summary>
    private void ClaimantsInterfered(WFContract contract, EntityUid encounter)
    {
        if (contract.User is not { } user || !TryComp<WFEncounterComponent>(encounter, out var comp))
            return;

        var crew = _crews.CrewMembers(user).ToHashSet();
        var contractor = _proto.Index(contract.Prototype).Contractor;
        var friends = new HashSet<string>();
        if (SideOf(contract.Company) is { } holderSide)
            friends.Add(holderSide);
        foreach (var company in _proto.Index(contractor).Companies)
        {
            if (SideOf(company.Id) is { } side)
                friends.Add(side);
        }

        foreach (var (attacker, hits) in comp.Hits)
        {
            // The hit count encounter rewards use; a stray shot is not interference.
            if (crew.Contains(attacker) || hits.Values.Sum() < WFEncounterRewardSystem.MinimumHits
                || !_mind.TryGetMind(attacker, out var mind) || mind.Value.Comp.OwnedEntity is not { } body
                || HolderOf(body) is not { } meddler
                || SideOf(CompOrNull<CompanyComponent>(body)?.CompanyName.Id) is { } side && friends.Contains(side))
                continue;

            AddStanding(meddler, contractor, -InterferencePenalty);
            Tell(meddler, Loc.GetString("wf-contract-interfered", ("contractor", Loc.GetString(_proto.Index(contractor).Name)),
                ("claimant", Claimant(contract)), ("points", InterferencePenalty)));
        }
    }
}
