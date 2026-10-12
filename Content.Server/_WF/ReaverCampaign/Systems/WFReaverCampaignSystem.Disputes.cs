using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>Reports the cells of every campaign ship still in the fight to sector control, which marks the ones a player ship shares as disputed.</summary>
    private void ReportPresence(WFReaverCampaignPrototype campaign)
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

    /// <summary>Whether an encounter is a Reaver stronghold, reinforcement, patrol, raid or convoy.</summary>
    private bool IsCampaignEncounter(EntityUid uid)
    {
        return HasComp<WFReaverStrongholdComponent>(uid) || HasComp<WFReaverReinforcementComponent>(uid)
            || HasComp<WFReaverPatrolComponent>(uid) || HasComp<WFReaverRaidComponent>(uid)
            || HasComp<WFReaverConvoyComponent>(uid);
    }

    /// <summary>Whether a campaign encounter's lead ship is in a disputed cell.</summary>
    private bool IsDisputedAt(EntityUid encounter, string lead)
    {
        if (!TryComp<WFEncounterComponent>(encounter, out var comp))
            return false;

        var map = _territory.SectorMap;
        return _territory.IsDisputed(map, _territory.CellOf(new MapCoordinates(Position(comp, lead), map)));
    }

    /// <summary>Pushes the clocks of disputed strongholds, reinforcements and convoys forward by the time since the last tick, so they hold until the dispute ends.</summary>
    private void HoldDisputed(TimeSpan elapsed)
    {
        var map = _territory.SectorMap;
        foreach (var (_, comp, _) in Strongholds())
        {
            if (!_territory.IsDisputed(map, comp.Cell))
                continue;

            comp.NextReinforcement += elapsed;
            if (comp.Hits > 0)
                comp.LastHit += elapsed;
        }

        foreach (var (uid, comp, _) in Reinforcements())
        {
            if (IsDisputedAt(uid, comp.Lead))
            {
                comp.Overdue += elapsed;
                comp.NextOrders += elapsed;
            }
        }

        foreach (var (uid, comp, _) in Convoys())
        {
            if (IsDisputedAt(uid, comp.Lead))
                comp.Overdue += elapsed;
        }
    }
}
