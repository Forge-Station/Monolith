using Content.Server.Station.Components;
using Content.Shared._WF.SectorControl;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;

namespace Content.Server._WF.SectorControl.Systems;

/// <summary>Vetoes claims on the origin cell and its ring and on cells holding a station named in wf.sector.protected_stations.</summary>
public sealed partial class WFSectorProtectionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;

    /// <summary>How many rings around the origin cell are protected.</summary>
    public const int CoreRadius = 1;

    private readonly List<string> _protected = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, SectorControlCVars.ProtectedStations, value =>
        {
            _protected.Clear();
            foreach (var fragment in value.Split(','))
            {
                if (fragment.Trim() is { Length: > 0 } name)
                    _protected.Add(name);
            }
        }, true);
        SubscribeLocalEvent<WFSectorClaimAttemptEvent>(OnClaimAttempt);
    }

    private void OnClaimAttempt(ref WFSectorClaimAttemptEvent args)
    {
        if (!args.Cancelled && IsProtected(args.Map, args.Cell))
            args.Cancelled = true;
    }

    /// <summary>Whether a cell is in the protected core or holds a protected station.</summary>
    public bool IsProtected(MapId map, WFSectorCell cell)
    {
        if (WFSectorHex.Distance(cell, WFSectorCell.Origin) <= CoreRadius)
            return true;

        if (_protected.Count == 0)
            return false;

        foreach (var station in _territory.Stations(map))
        {
            if (_territory.GridCell(station) == cell && Named(station))
                return true;
        }

        return false;
    }

    /// <summary>Whether a station's grid name, station name or station id contains a protected fragment.</summary>
    private bool Named(EntityUid station)
    {
        var name = Name(station);
        var id = CompOrNull<BecomesStationComponent>(station)?.Id ?? string.Empty;
        var owner = TryComp<StationMemberComponent>(station, out var member) && !TerminatingOrDeleted(member.Station)
            ? Name(member.Station)
            : string.Empty;
        foreach (var fragment in _protected)
        {
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                || id.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                || owner.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
