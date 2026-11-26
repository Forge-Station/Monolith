using Content.Client._WF.Encounters;
using Content.Shared.Shuttles.UI.MapObjects;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public sealed partial class MapScreen
{
    /// <summary>Adds the sector's visible encounters to a map's objects, one each, at its first ship as it was at the ping.</summary>
    private void WfAddEncounters(MapId mapId, EntityUid mapUid)
    {
        var markers = _entManager.System<WFEncounterMarkerClientSystem>();
        var map = _entManager.GetNetEntity(mapUid);
        var listed = new HashSet<NetEntity>();
        foreach (var marker in markers.Markers)
        {
            if (marker.Map != mapId || !listed.Add(marker.Encounter))
                continue;

            var position = markers.GetPosition(marker);
            var name = marker.Threat is { } threat
                ? Loc.GetString("wf-encounter-map-object-ranked", ("name", marker.Name), ("threat", threat))
                : Loc.GetString("wf-encounter-map-object", ("name", marker.Name));
            _pendingMapObjects.Add((mapId, new ShuttleBeaconObject(NetEntity.Invalid, new NetCoordinates(map, position), name)));
        }
    }
}
