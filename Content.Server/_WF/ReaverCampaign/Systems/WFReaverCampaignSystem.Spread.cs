using Content.Server._WF.ReaverCampaign.Components;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>How long a station cell is contested before it is taken.</summary>
    private static readonly TimeSpan BlockadeTime = TimeSpan.FromMinutes(10);

    private void TickSpread(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        var now = _timing.CurTime;
        if (state.NextSpread is not { } next)
        {
            state.NextSpread = now + campaign.SpreadInterval;
            return;
        }

        if (now < next)
            return;

        state.NextSpread = now + campaign.SpreadInterval;
        TrySpreadOnce();
    }

    /// <summary>Takes one quiet, unvetoed frontier cell within support range of a stronghold; a station cell is contested as a blockade.</summary>
    public bool TrySpreadOnce()
    {
        if (Campaign is not { } campaign || State(true) is null)
            return false;

        var standing = Strongholds();
        if (standing.Count == 0)
            return false;

        var map = _territory.SectorMap;
        var quietSince = _timing.CurTime - TimeSpan.FromMinutes(campaign.QuietMinutes);
        var candidates = new List<((WFSectorCell Cell, EntityUid Source) Pick, float Weight)>();
        foreach (var cell in _territory.Frontier(map, campaign.Faction))
        {
            if (_territory.Owner(map, cell) != null || _territory.ContestOf(map, cell) != null || _territory.IsDisputed(map, cell))
                continue;

            EntityUid? source = null;
            var nearest = int.MaxValue;
            foreach (var stronghold in standing)
            {
                var distance = WFSectorHex.Distance(stronghold.Comp1.Cell, cell);
                if (distance > campaign.SupportRange || distance >= nearest)
                    continue;

                nearest = distance;
                source = stronghold.Owner;
            }

            if (source is not { } supporter || !_activity.IsQuiet(map, cell, quietSince)
                || !_territory.CanClaim(map, cell, campaign.Faction, supporter))
                continue;

            var ring = WFSectorHex.Distance(cell, WFSectorCell.Origin) + 1f;
            candidates.Add(((cell, supporter), 1f / (ring * ring)));
        }

        if (PickWeighted(candidates) is not { } pick)
            return false;

        if (BlockadeTargets(campaign, map, pick.Cell) is { Count: > 0 } stations)
            return TryBlockade(campaign, map, pick.Cell, pick.Source, stations[0]);

        return _territory.TryClaim(map, pick.Cell, campaign.Faction, pick.Source);
    }

    /// <summary>Contests a station's cell and warns the sector, naming the station.</summary>
    private bool TryBlockade(WFReaverCampaignPrototype campaign, MapId map, WFSectorCell cell, EntityUid source,
        Entity<MapGridComponent> station)
    {
        if (!_territory.Contest(map, cell, campaign.Faction, _timing.CurTime + BlockadeTime, source))
            return false;

        Announce(campaign, Loc.GetString("wf-reaver-blockade",
            ("station", Name(station)),
            ("callsign", WFSectorHex.Callsign(cell)),
            ("minutes", (int) BlockadeTime.TotalMinutes)), true);
        return true;
    }
}
