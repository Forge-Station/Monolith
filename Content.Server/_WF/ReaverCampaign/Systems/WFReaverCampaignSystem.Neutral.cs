using System.Linq;
using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>Whether the campaign's faction leaves a ship alone: no warning, fire, intercept, raid, mark or presence.</summary>
    private bool Neutral(EntityUid grid)
    {
        return CampaignFor(State()) is { } campaign && _activity.IsNeutralTo(campaign.Faction, grid);
    }

    /// <summary>Whether the campaign's faction leaves a station alone.</summary>
    private bool NeutralStation(EntityUid station)
    {
        return CampaignFor(State()) is { } campaign && _activity.IsNeutralStation(campaign.Faction, station);
    }

    /// <summary>The crewed ships on the sector map the campaign deals with: those of a neutral company or station left out.</summary>
    private HashSet<EntityUid> Crewed()
    {
        var map = _territory.SectorMap;
        return CampaignFor(State()) is { } campaign ? _activity.CrewedShips(map, campaign.Faction) : _activity.CrewedShips(map);
    }

    /// <summary>The stations in a cell the campaign may blockade; a neutral station is left out, so its cell is claimed like open space.</summary>
    private List<Entity<MapGridComponent>> BlockadeTargets(WFReaverCampaignPrototype campaign, MapId map, WFSectorCell cell)
    {
        var stations = _territory.StationsIn(map, cell);
        stations.RemoveAll(station => _activity.IsNeutralStation(campaign.Faction, station));
        return stations;
    }

    /// <summary>Spawns an encounter for the campaign, telling its ships which companies to leave alone.</summary>
    private bool SpawnEncounter(WFEncounterPrototype prototype, MapCoordinates at, out EntityUid uid, bool offBudget = false)
    {
        if (!_encounters.TrySpawn(prototype, at, out uid, offBudget: offBudget))
            return false;

        if (CampaignFor(State()) is { } campaign && _prototypes.TryIndex(campaign.Faction, out var faction)
            && faction.NeutralCompanies.Count > 0 && TryComp<WFEncounterComponent>(uid, out var encounter))
        {
            foreach (var ship in encounter.Ships.Values)
            {
                ship.NeutralCompanies = faction.NeutralCompanies.Select(company => company.Id).ToList();
            }
        }

        return true;
    }
}
