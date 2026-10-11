using System.Numerics;
using Content.Client._Mono.Radar;
using Content.Client._WF.CombatConsole;
using Content.Shared._Mono.FireControl;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Client._WF.Cockpit;

/// <summary>One preview line, from a weapon to the aim point.</summary>
public readonly struct WFCockpitAimSegment
{
    public readonly Vector2 From;
    public readonly Vector2 To;
    public readonly Color Color;

    public WFCockpitAimSegment(Vector2 from, Vector2 to, Color color)
    {
        From = from;
        To = to;
        Color = color;
    }
}

/// <summary>The aim lines the NAV plot and the ship cam share, in world space, kept by the server's line-of-sight rule.</summary>
public sealed class WFCockpitAimPreview
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const float MinLength = 0.01f;
    private static readonly TimeSpan ColorRefresh = TimeSpan.FromSeconds(0.5);

    private static readonly Func<EntityUid, Ignored, bool> IgnoreOtherGrids = static (uid, ignored) => ignored.Contains(uid);

    private readonly Func<EntityCoordinates?> _aim;
    private readonly Func<NetEntity, bool> _selected;
    private readonly SharedTransformSystem _transform;
    private readonly SharedPhysicsSystem _physics;
    private readonly RadarBlipsSystem _blips;
    private readonly EntityQuery<TransformComponent> _xforms;
    private readonly List<WFCockpitAimSegment> _segments = new();
    private readonly Dictionary<NetEntity, Color> _colors = new();
    private FireControllableEntry[] _weapons = Array.Empty<FireControllableEntry>();
    private TimeSpan _colorsAt;
    private bool _colorsValid;

    /// <summary>The map the last <see cref="Refresh"/> aimed on, or nullspace when it drew nothing.</summary>
    public MapId Map { get; private set; }

    /// <summary>The lines the last <see cref="Refresh"/> found, in world coordinates.</summary>
    public IReadOnlyList<WFCockpitAimSegment> Segments => _segments;

    public WFCockpitAimPreview(Func<EntityCoordinates?> aim, Func<NetEntity, bool> selected)
    {
        IoCManager.InjectDependencies(this);
        _aim = aim;
        _selected = selected;
        _transform = _entities.System<SharedTransformSystem>();
        _physics = _entities.System<SharedPhysicsSystem>();
        _blips = _entities.System<RadarBlipsSystem>();
        _xforms = _entities.GetEntityQuery<TransformComponent>();
    }

    /// <summary>Replaces the linked console's weapon list; null clears it.</summary>
    public void UpdateWeapons(FireControllableEntry[]? weapons)
    {
        _weapons = weapons ?? Array.Empty<FireControllableEntry>();
        _colorsValid = false;
    }

    /// <summary>Recomputes the lines from the current weapons, selection and aim point.</summary>
    public IReadOnlyList<WFCockpitAimSegment> Refresh()
    {
        _segments.Clear();
        Map = MapId.Nullspace;
        if (_weapons.Length == 0 || _aim() is not { } aim || !aim.IsValid(_entities))
            return _segments;

        var target = _transform.ToMapCoordinates(aim);
        if (target.MapId == MapId.Nullspace || !float.IsFinite(target.Position.X) || !float.IsFinite(target.Position.Y))
            return _segments;

        Map = target.MapId;
        var colorsChecked = false;
        foreach (var weapon in _weapons)
        {
            if (!_selected(weapon.NetEntity) || !TryGetOrigin(weapon, target.MapId, out var origin, out var own))
                continue;

            var offset = target.Position - origin;
            var length = offset.Length();
            if (!float.IsFinite(length) || length < MinLength)
                continue;
            if (!weapon.IgnoresLos && IsBlocked(target.MapId, origin, offset / length, length, own))
                continue;

            if (!colorsChecked)
            {
                RefreshColors();
                colorsChecked = true;
            }
            _segments.Add(new WFCockpitAimSegment(origin, target.Position, ColorOf(weapon.NetEntity)));
        }

        return _segments;
    }

    /// <summary>Where the weapon is on the aim point's map: its live position if the client has the entity, else the state's coordinates.</summary>
    private bool TryGetOrigin(FireControllableEntry weapon, MapId map, out Vector2 origin, out Ignored own)
    {
        origin = default;
        own = default;
        if (_entities.TryGetEntity(weapon.NetEntity, out var entity) &&
            _xforms.TryGetComponent(entity.Value, out var xform) &&
            xform.MapID != MapId.Nullspace)
        {
            if (xform.MapID != map)
                return false;

            origin = _transform.GetWorldPosition(xform);
            own = new Ignored(entity.Value, xform.GridUid, _xforms);
            return true;
        }

        var coordinates = _entities.GetCoordinates(weapon.Coordinates);
        if (!coordinates.IsValid(_entities))
            return false;

        var mapCoordinates = _transform.ToMapCoordinates(coordinates);
        if (mapCoordinates.MapId != map)
            return false;

        origin = mapCoordinates.Position;
        own = new Ignored(EntityUid.Invalid, _transform.GetGrid(coordinates), _xforms);
        return true;
    }

    /// <summary>Whether anything on the weapon's own grid lies on the ray, as the server's fire check sees it; stops at the first hit.</summary>
    private bool IsBlocked(MapId map, Vector2 origin, Vector2 direction, float length, Ignored own)
    {
        var ray = new CollisionRay(origin, direction, (int) (CollisionGroup.Opaque | CollisionGroup.Impassable));
        var hits = _physics.IntersectRayWithPredicate(map, ray, own, IgnoreOtherGrids, length, returnOnFirstHit: true);
        if (hits is IReadOnlyCollection<RayCastResults> collection)
            return collection.Count > 0;

        foreach (var _ in hits)
            return true;

        return false;
    }

    /// <summary>Re-reads the weapons' radar blip colours at most twice a second, or after the weapon list changes.</summary>
    private void RefreshColors()
    {
        var now = _timing.RealTime;
        if (_colorsValid && now - _colorsAt < ColorRefresh)
            return;

        _colorsValid = true;
        _colorsAt = now;
        _colors.Clear();
        foreach (var blip in _blips.GetCurrentBlips())
            _colors[blip.NetUid] = blip.Config.Color;
    }

    private Color ColorOf(NetEntity weapon) =>
        _colors.TryGetValue(weapon, out var color) ? color : WFInstrumentTheme.Accent;

    /// <summary>The entities a weapon's ray passes through: the weapon itself and, if it is on a grid, everything off that grid.</summary>
    private readonly struct Ignored
    {
        private readonly EntityUid _weapon;
        private readonly EntityUid? _grid;
        private readonly EntityQuery<TransformComponent> _xforms;

        public Ignored(EntityUid weapon, EntityUid? grid, EntityQuery<TransformComponent> xforms)
        {
            _weapon = weapon;
            _grid = grid;
            _xforms = xforms;
        }

        public bool Contains(EntityUid uid)
        {
            if (uid == _weapon)
                return true;
            if (_grid is not { } grid)
                return false;

            return !_xforms.TryGetComponent(uid, out var xform) || xform.GridUid != grid;
        }
    }
}
