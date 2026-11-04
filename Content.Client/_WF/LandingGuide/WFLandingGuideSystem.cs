using System.Numerics;
using Content.Shared._WF.LandingGuide;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._WF.LandingGuide;

/// <summary>
/// Surveys the ground under the hull the local player pilots while it flies a planet's air, for
/// <see cref="WFLandingGuideOverlay"/>. Reads the ground grid the pilot's z-level eye already streams in.
/// </summary>
public sealed partial class WFLandingGuideSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFLandingSurveySystem _survey = default!;

    /// <summary>Longest a survey is kept while the hull holds still.</summary>
    public static readonly TimeSpan SurveyInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>Shortest gap between surveys while the hull moves.</summary>
    public static readonly TimeSpan MinSurveyGap = TimeSpan.FromSeconds(0.1);

    /// <summary>How long one ground tile's reading is reused.</summary>
    public static readonly TimeSpan TileExpiry = TimeSpan.FromSeconds(0.5);

    /// <summary>How far the hull turns before its footprint is surveyed again.</summary>
    private static readonly Angle TurnThreshold = Angle.FromDegrees(3);

    /// <summary>Whether the guide shows while piloting. On by default; the toggle key turns it off.</summary>
    public bool Enabled { get; private set; } = true;

    /// <summary>The hull being surveyed, or null when the guide shows nothing.</summary>
    public EntityUid? Hull { get; private set; }

    /// <summary>The ground layer under <see cref="Hull"/>.</summary>
    public EntityUid? Ground { get; private set; }

    /// <summary>Every ground tile under the hull with what it holds.</summary>
    public readonly List<(Vector2i Index, WFLandingTile Status)> Tiles = new();

    /// <summary>The hull's outline as edges in its own coordinates.</summary>
    public readonly List<(Vector2 From, Vector2 To)> Outline = new();

    /// <summary>Tiles under the hull that would refuse or crush.</summary>
    public int Blocked { get; private set; }

    /// <summary>Tiles under the hull with no ground known.</summary>
    public int Unknown { get; private set; }

    private readonly Dictionary<Vector2i, (WFLandingTile Status, TimeSpan Until)> _cache = new();
    private readonly HashSet<Vector2i> _footprint = new();
    private readonly HashSet<Vector2i> _hullTiles = new();
    private Vector2 _surveyedAt;
    private Angle _surveyedRotation;
    private TimeSpan _lastSurvey;
    private GameTick _outlineTick;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WFLandingGuideOverlay());

        CommandBinds.Builder
            .Bind(WFLandingGuideKeys.WFToggleLandingGuide, InputCmdHandler.FromDelegate(_ => Toggle(), handle: true,
                outsidePrediction: true))
            .Register<WFLandingGuideSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WFLandingGuideSystem>();
        _overlays.RemoveOverlay<WFLandingGuideOverlay>();
    }

    /// <summary>Turns the guide off or on and says which.</summary>
    public void Toggle()
    {
        Enabled = !Enabled;
        _popup.PopupCursor(Loc.GetString(Enabled ? "wf-landing-guide-on" : "wf-landing-guide-off"));
        if (!Enabled)
            Reset();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!Enabled || PilotedHull() is not { } hull || !_survey.TryGetLandingGround(hull, out var ground)
            || !TryComp<MapGridComponent>(hull, out var hullGrid) || !TryComp<MapGridComponent>(ground, out var groundGrid))
        {
            Reset();
            return;
        }

        var now = _timing.RealTime;
        var (position, rotation) = _transform.GetWorldPositionRotation(hull);
        var fresh = hull != Hull || ground != Ground;

        if (fresh)
        {
            Reset();
            Hull = hull;
            Ground = ground;
        }

        var moved = (position - _surveyedAt).LengthSquared() >= groundGrid.TileSize * groundGrid.TileSize
                    || Math.Abs(Angle.ShortestDistance(rotation, _surveyedRotation).Theta) >= TurnThreshold.Theta;

        if (!fresh && (now < _lastSurvey + MinSurveyGap || !moved && now < _lastSurvey + SurveyInterval))
            return;

        _surveyedAt = position;
        _surveyedRotation = rotation;
        _lastSurvey = now;

        if (fresh || hullGrid.LastTileModifiedTick != _outlineTick)
            BuildOutline((hull, hullGrid));

        Survey((hull, hullGrid), (ground, groundGrid), now);
    }

    /// <summary>The grid whose helm the local player holds, or null.</summary>
    private EntityUid? PilotedHull()
    {
        if (!TryComp<PilotComponent>(_player.LocalEntity, out var pilot) || pilot.Console is not { } console
            || TerminatingOrDeleted(console))
            return null;

        return Transform(console).GridUid;
    }

    /// <summary>Reads every ground tile under the hull, reusing recent readings of each.</summary>
    private void Survey(Entity<MapGridComponent> hull, Entity<MapGridComponent> ground, TimeSpan now)
    {
        _survey.GetFootprint(hull, ground, _footprint);
        Tiles.Clear();
        Blocked = 0;
        Unknown = 0;

        foreach (var index in _footprint)
        {
            if (!_cache.TryGetValue(index, out var cached) || now >= cached.Until)
            {
                cached = (_survey.Classify(ground, index), now + TileExpiry);
                _cache[index] = cached;
            }

            Tiles.Add((index, cached.Status));
            switch (cached.Status)
            {
                case WFLandingTile.Blocked:
                case WFLandingTile.Grid:
                    Blocked++;
                    break;
                case WFLandingTile.NoGround:
                    Unknown++;
                    break;
            }
        }

        // Readings of tiles the hull has left behind are no use.
        if (_cache.Count > _footprint.Count * 4)
            _cache.Clear();
    }

    /// <summary>Collects every hull tile edge that borders open space.</summary>
    private void BuildOutline(Entity<MapGridComponent> hull)
    {
        _outlineTick = hull.Comp.LastTileModifiedTick;
        Outline.Clear();
        _hullTiles.Clear();

        var tiles = _map.GetAllTiles(hull, hull.Comp);
        while (tiles.MoveNext(out var tile))
        {
            _hullTiles.Add(tile.Value.GridIndices);
        }

        var size = hull.Comp.TileSize;
        foreach (var index in _hullTiles)
        {
            var min = new Vector2(index.X, index.Y) * size;
            var max = min + new Vector2(size);

            if (!_hullTiles.Contains(index + new Vector2i(-1, 0)))
                Outline.Add((min, new Vector2(min.X, max.Y)));
            if (!_hullTiles.Contains(index + new Vector2i(1, 0)))
                Outline.Add((new Vector2(max.X, min.Y), max));
            if (!_hullTiles.Contains(index + new Vector2i(0, -1)))
                Outline.Add((min, new Vector2(max.X, min.Y)));
            if (!_hullTiles.Contains(index + new Vector2i(0, 1)))
                Outline.Add((new Vector2(min.X, max.Y), max));
        }
    }

    private void Reset()
    {
        Hull = null;
        Ground = null;
        Tiles.Clear();
        Outline.Clear();
        _cache.Clear();
        Blocked = 0;
        Unknown = 0;
        _outlineTick = GameTick.Zero;
    }
}
