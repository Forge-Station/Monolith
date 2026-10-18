using Content.Server._WF.AdsIncursion.Components;
using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.AdsIncursion;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;

namespace Content.Server._WF.AdsIncursion.Systems;

public sealed partial class WFAdsCampaignSystem
{
    /// <summary>Reports the cells of every campaign ship still in the fight to sector control: disputes, and the presence that decides challenges.</summary>
    private void ReportPresence(WFAdsCampaignPrototype campaign)
    {
        var map = _territory.SectorMap;
        var cells = new HashSet<WFSectorCell>();
        var query = EntityQueryEnumerator<WFEncounterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var encounter, out var xform))
        {
            if (encounter.Resolution != null || xform.MapID != map || !IsCampaignEncounter(uid))
                continue;

            foreach (var ship in encounter.Ships.Values)
            {
                // A hulk out of the fight holds no ground.
                if (!TerminatingOrDeleted(ship.Grid) && _encounters.InFight(ship))
                    cells.Add(_territory.CellOf(new MapCoordinates(_transform.GetWorldPosition(ship.Grid), map)));
            }
        }

        _territory.SetPresence(campaign.Faction, map, cells);
    }

    /// <summary>Whether a campaign encounter's lead ship is in a disputed cell.</summary>
    private bool IsDisputedAt(EntityUid encounter, string lead)
    {
        if (!TryComp<WFEncounterComponent>(encounter, out var comp))
            return false;

        var map = _territory.SectorMap;
        return _territory.IsDisputed(map, _territory.CellOf(new MapCoordinates(Position(comp, lead), map)));
    }

    /// <summary>Pushes the sentinel clocks of disputed nodes and the deadlines of disputed sentinels and deployments forward, so they hold until the dispute ends.</summary>
    private void HoldDisputed(TimeSpan elapsed)
    {
        var map = _territory.SectorMap;
        foreach (var (_, comp, _) in Nodes())
        {
            if (!_territory.IsDisputed(map, comp.Cell))
                continue;

            comp.NextSentinel += elapsed;
            if (comp.Hits > 0)
                comp.LastHit += elapsed;
        }

        foreach (var (uid, comp, _) in Sentinels())
        {
            if (comp.Phase == WFAdsSentinelPhase.Inbound && IsDisputedAt(uid, comp.Lead))
                comp.Overdue += elapsed;
        }

        foreach (var (uid, comp, _) in Deployments())
        {
            if (IsDisputedAt(uid, comp.Lead))
                comp.Overdue += elapsed;
        }
    }
}
