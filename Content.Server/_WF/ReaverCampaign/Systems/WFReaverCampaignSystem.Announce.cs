using System.Globalization;
using System.Numerics;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    private static readonly ProtoId<RadioChannelPrototype> WarningChannel = "Common";

    /// <summary>A sector-wide announcement from the sector watch: red with the rising sound, or teal with the falling one.</summary>
    private void Announce(WFReaverCampaignPrototype campaign, string message, bool rising, bool chime = true)
    {
        _chat.DispatchGlobalAnnouncement(message, Loc.GetString("wf-reaver-watch"), playSound: chime,
            announcementSound: chime ? rising ? campaign.RisingSound : campaign.FallingSound : null,
            colorOverride: rising ? campaign.RisingColor : campaign.FallingColor);
    }

    /// <summary>Announces something at a place, with the station, bearing and range from the nearest station added as arguments.</summary>
    private void AnnouncePlace(WFReaverCampaignPrototype campaign, string key, bool rising, MapId map, Vector2 position,
        params (string, object)[] args)
    {
        var (station, bearing, range) = Place(map, position);
        var all = new List<(string, object)>(args) { ("station", station), ("bearing", bearing), ("range", range) };
        var chime = State()?.TierAnnounced != _timing.CurTime;
        Announce(campaign, Loc.GetString(key, all.ToArray()), rising, chime);
    }

    /// <summary>The nearest station to a point, with the bearing and range from it to the point; the origin if there is none.</summary>
    private (string Station, string Bearing, string Range) Place(MapId map, Vector2 position)
    {
        string? name = null;
        var from = Vector2.Zero;
        var best = float.MaxValue;
        foreach (var station in _territory.Stations(map))
        {
            var at = _transform.GetWorldPosition(station);
            var distance = Vector2.DistanceSquared(at, position);
            if (distance >= best)
                continue;

            best = distance;
            from = at;
            name = Name(station);
        }

        return (name ?? Loc.GetString("wf-reaver-place-origin"), Bearing(from, position), Kilometres(from, position));
    }

    /// <summary>The compass bearing from one point to another as three digits, north 000, clockwise.</summary>
    public static string Bearing(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        var degrees = (int) MathF.Round(MathF.Atan2(delta.X, delta.Y) * 180f / MathF.PI);
        return ((degrees % 360 + 360) % 360).ToString("000", CultureInfo.InvariantCulture);
    }

    /// <summary>The distance between two points in kilometres, to one decimal.</summary>
    public static string Kilometres(Vector2 from, Vector2 to)
    {
        return (Vector2.Distance(from, to) / 1000f).ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>Warns a ship over its PA and in chat to those aboard, and the sector on the common channel, with the bearing and range from it to the Reavers.</summary>
    private void WarnShip(WFReaverCampaignPrototype campaign, MapId map, EntityUid ship, Vector2 reavers, string shipKey, string radioKey)
    {
        var at = _transform.GetWorldPosition(ship);
        var bearing = Bearing(at, reavers);
        var range = Kilometres(at, reavers);
        _notices.Tell(ship, Loc.GetString(shipKey, ("ship", Name(ship)), ("bearing", bearing), ("range", range)),
            Loc.GetString("wf-reaver-watch"), campaign.RisingColor, true);
        Radio(map, Loc.GetString(radioKey, ("ship", Name(ship)), ("bearing", bearing), ("range", range)));
    }

    /// <summary>Warns a raided station over its PA, if it has one, and the sector on the common channel.</summary>
    private void WarnStation(WFReaverCampaignPrototype campaign, MapId map, Entity<MapGridComponent> station, Vector2 raiders)
    {
        var at = _transform.GetWorldPosition(station);
        var bearing = Bearing(at, raiders);
        var range = Kilometres(at, raiders);
        var message = Loc.GetString("wf-reaver-raid-station-warning", ("station", Name(station)), ("bearing", bearing), ("range", range));
        _shipPa.Announce(station, message, sender: Loc.GetString("wf-reaver-watch"), color: campaign.RisingColor);
        Radio(map, message);
    }

    /// <summary>Says something on the common channel as the sector watch.</summary>
    private void Radio(MapId map, string message)
    {
        if (State() is not { } state || Watch(state, map) is not { } watch)
            return;

        _radio.SendRadioMessage(watch, message, WarningChannel, watch);
    }

    /// <summary>The sector watch's radio: an entity on the map, named for it and heard without a telecom server.</summary>
    private EntityUid? Watch(WFReaverCampaignComponent state, MapId map)
    {
        if (state.Watch is { } existing && !TerminatingOrDeleted(existing))
            return existing;

        if (!_map.TryGetMap(map, out var mapUid) || mapUid is not { } uid)
            return null;

        var watch = Spawn(null, new EntityCoordinates(uid, Vector2.Zero));
        _meta.SetEntityName(watch, Loc.GetString("wf-reaver-watch"));
        EnsureComp<TelecomExemptComponent>(watch);
        state.Watch = watch;
        return watch;
    }
}
