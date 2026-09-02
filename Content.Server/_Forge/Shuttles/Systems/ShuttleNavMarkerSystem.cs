using Content.Server.Popups;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._Forge.Shuttles.Components;
using Content.Shared._Forge.Shuttles.Events;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using System.Numerics;

namespace Content.Server._Forge.Shuttles.Systems;

public sealed class ShuttleNavMarkerSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private static readonly Color[] Palette =
    [
        Color.Cyan,
        Color.Yellow,
        Color.Magenta,
        Color.Orange,
        Color.LimeGreen,
    ];

    private float _syncAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<AddShuttleNavCoordinateMarkerMessage>(OnAddCoordinate);
            subs.Event<AddShuttleNavEntityMarkerMessage>(OnAddEntity);
            subs.Event<RemoveShuttleNavMarkerMessage>(OnRemove);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _syncAccumulator += frameTime;
        if (_syncAccumulator < 0.25f)
            return;

        _syncAccumulator = 0f;

        var query = EntityQueryEnumerator<ShuttleNavMarkerComponent>();
        while (query.MoveNext(out var uid, out var markers))
        {
            var changed = false;
            foreach (var marker in markers.Markers)
            {
                if (marker.Kind != ShuttleNavMarkerKind.Entity || marker.Target is not { } net)
                    continue;

                if (!TryGetEntity(net, out var target) || !Exists(target.Value))
                    continue;

                var mapPos = _transform.GetMapCoordinates(target.Value);
                if ((mapPos.Position - marker.Coordinates).LengthSquared() < 0.25f &&
                    marker.MapId == (int) mapPos.MapId)
                    continue;

                marker.Coordinates = mapPos.Position;
                marker.MapId = (int) mapPos.MapId;
                changed = true;
            }

            if (changed)
                Dirty(uid, markers);
        }
    }

    private void OnAddCoordinate(EntityUid uid, ShuttleConsoleComponent component, AddShuttleNavCoordinateMarkerMessage args)
    {
        if (!TryGetShuttleGrid(uid, out var grid) || args.Actor is not { Valid: true } actor)
            return;

        if (!float.IsFinite(args.X) || !float.IsFinite(args.Y) ||
            MathF.Abs(args.X) > 1_000_000f || MathF.Abs(args.Y) > 1_000_000f)
        {
            _popup.PopupEntity(Loc.GetString("shuttle-console-marker-invalid"), uid, actor, PopupType.SmallCaution);
            return;
        }

        var markers = EnsureComp<ShuttleNavMarkerComponent>(grid);
        if (markers.Markers.Count >= ShuttleNavMarkerComponent.MaxMarkers)
        {
            _popup.PopupEntity(Loc.GetString("shuttle-console-marker-full"), uid, actor, PopupType.SmallCaution);
            return;
        }

        var mapId = _transform.GetMapId(uid);
        var marker = CreateMarker(
            markers,
            Loc.GetString("shuttle-console-marker-coord-name", ("id", markers.NextId)),
            ShuttleNavMarkerKind.Coordinates,
            new Vector2(args.X, args.Y),
            (int) mapId,
            null);

        markers.Markers.Add(marker);
        markers.NextId++;
        Dirty(grid, markers);
    }

    private void OnAddEntity(EntityUid uid, ShuttleConsoleComponent component, AddShuttleNavEntityMarkerMessage args)
    {
        if (!TryGetShuttleGrid(uid, out var grid) || args.Actor is not { Valid: true } actor)
            return;

        if (!TryGetEntity(args.Target, out var target) || !Exists(target.Value))
            return;

        var markers = EnsureComp<ShuttleNavMarkerComponent>(grid);
        var existing = markers.Markers.FindIndex(m => m.Kind == ShuttleNavMarkerKind.Entity && m.Target == args.Target);
        if (existing >= 0)
        {
            markers.Markers.RemoveAt(existing);
            Dirty(grid, markers);
            return;
        }

        if (markers.Markers.Count >= ShuttleNavMarkerComponent.MaxMarkers)
        {
            _popup.PopupEntity(Loc.GetString("shuttle-console-marker-full"), uid, actor, PopupType.SmallCaution);
            return;
        }

        var mapPos = _transform.GetMapCoordinates(target.Value);
        var name = Name(target.Value);
        if (string.IsNullOrWhiteSpace(name))
            name = Loc.GetString("shuttle-console-unknown");

        var marker = CreateMarker(
            markers,
            Loc.GetString("shuttle-console-marker-entity-name", ("name", name)),
            ShuttleNavMarkerKind.Entity,
            mapPos.Position,
            (int) mapPos.MapId,
            args.Target);

        markers.Markers.Add(marker);
        markers.NextId++;
        Dirty(grid, markers);
    }

    private void OnRemove(EntityUid uid, ShuttleConsoleComponent component, RemoveShuttleNavMarkerMessage args)
    {
        if (!TryGetShuttleGrid(uid, out var grid))
            return;

        if (!TryComp(grid, out ShuttleNavMarkerComponent? markers))
            return;

        var removed = markers.Markers.RemoveAll(m => m.Id == args.Id);
        if (removed > 0)
            Dirty(grid, markers);
    }

    private bool TryGetShuttleGrid(EntityUid console, out EntityUid grid)
    {
        grid = default;
        var ev = new ConsoleShuttleEvent { Console = console };
        RaiseLocalEvent(console, ref ev);
        var entity = ev.Console ?? console;

        if (!TryComp(entity, out TransformComponent? xform) || xform.GridUid is not { } gridUid)
            return false;

        grid = gridUid;
        return true;
    }

    private static ShuttleNavMarker CreateMarker(
        ShuttleNavMarkerComponent markers,
        string name,
        ShuttleNavMarkerKind kind,
        Vector2 coordinates,
        int mapId,
        NetEntity? target)
    {
        var color = Palette[(markers.NextId - 1) % Palette.Length];
        return new ShuttleNavMarker
        {
            Id = markers.NextId,
            Name = name,
            Kind = kind,
            Coordinates = coordinates,
            MapId = mapId,
            Target = target,
            Color = color,
        };
    }
}
