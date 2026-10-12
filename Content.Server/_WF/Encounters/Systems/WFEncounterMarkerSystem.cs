using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>Tells every player where the ships of the visible, unresolved encounters are, for the sector markers on radars.</summary>
public sealed partial class WFEncounterMarkerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>Side colours for an encounter with several sides, in the order its sides first appear.</summary>
    private static readonly Color[] SideColors =
    {
        Color.FromHex("#ff5c5c"),
        Color.FromHex("#6fb6ff"),
        Color.FromHex("#7fe0c8"),
        Color.FromHex("#ffae3d"),
        Color.FromHex("#c792ff"),
        Color.FromHex("#e6e66a"),
    };

    private TimeSpan _next;
    private bool _sentAny;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        var ev = new WFEncounterMarkersEvent();
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            if (encounter.Hidden || encounter.Resolution != null)
                continue;

            // A marker per ship, each at its centre of mass: the velocity belongs to that point, not to the grid origin.
            var net = GetNetEntity(uid);
            foreach (var ship in encounter.Ships.Values)
            {
                // A ship lying in wait has no marker until it shows itself.
                if (TerminatingOrDeleted(ship.Grid) || ship.Lurking)
                    continue;

                TryComp<PhysicsComponent>(ship.Grid, out var body);
                // A disabled ship keeps its zones on the scope no longer: only the armed and manned ones are drawn.
                var disabled = !ship.Derelict && _status.IsDisabled(ship.Grid);
                var position = _transform.ToMapCoordinates(new EntityCoordinates(ship.Grid, body?.LocalCenter ?? Vector2.Zero));
                ev.Markers.Add(new WFEncounterMarker
                {
                    Encounter = net,
                    Name = encounter.Name,
                    Ship = MetaData(ship.Grid).EntityName,
                    Side = ship.Side,
                    Category = encounter.Category,
                    Icon = ship.Icon != WFEncounterIcon.Category ? ship.Icon : encounter.Icon,
                    Grid = GetNetEntity(ship.Grid),
                    Color = ShipColor(encounter, ship),
                    Map = position.MapId,
                    Position = position.Position,
                    Velocity = body?.LinearVelocity ?? Vector2.Zero,
                    WarnRange = disabled ? 0f : ship.WarnRange,
                    AttackRange = disabled ? 0f : ship.AttackRange,
                });
            }
        }

        // One empty update after the last marker goes, then silence.
        if (ev.Markers.Count == 0 && !_sentAny)
            return;

        _sentAny = ev.Markers.Count > 0;
        RaiseNetworkEvent(ev, Filter.Broadcast());
    }

    /// <summary>
    /// The colour a ship is marked in: its IFF colour once one was set, else its side's colour when the encounter has
    /// several sides, else none, which draws by the encounter's category.
    /// </summary>
    public Color? ShipColor(WFEncounterComponent encounter, WFEncounterShipState ship)
    {
        if (ship.Color is { } assigned)
            return assigned;

        var iff = CompOrNull<IFFComponent>(ship.Grid)?.Color;
        if (iff is { } set && set != IFFComponent.IFFColor)
            return set;

        var sides = new List<string>();
        foreach (var other in encounter.Ships.Values)
        {
            if (!TerminatingOrDeleted(other.Grid) && !sides.Contains(other.Side))
                sides.Add(other.Side);
        }

        if (sides.Count < 2)
            return null;

        return WFEncounterSystem.SideColors[Math.Max(0, sides.IndexOf(ship.Side)) % WFEncounterSystem.SideColors.Count];
    }
}
