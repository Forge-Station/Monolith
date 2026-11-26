using System.Numerics;
using Content.Client._WF.Shuttles.UI;
using Content.Shared._WF.LandingGuide;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.LandingGuide;

/// <summary>
/// Draws the landing guide over the pilot's own picture: every ground tile under the hull filled by what it holds, the
/// hull's outline and a one-line summary above it. Drawn at the ground's world position on the pilot's own layer, as
/// the hull hides the ground drawn under it.
/// </summary>
public sealed partial class WFLandingGuideOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private readonly WFLandingGuideSystem _guide;
    private readonly SharedTransformSystem _transform;
    private readonly Font _font;

    public static readonly Color ClearColor = new(0.25f, 0.85f, 0.35f, 0.28f);
    public static readonly Color BlockedColor = new(0.95f, 0.2f, 0.2f, 0.45f);
    public static readonly Color UnknownColor = new(1f, 0.7f, 0.1f, 0.32f);
    public static readonly Color OutlineColor = new(0.85f, 0.95f, 1f, 0.9f);
    private static readonly Color ShadowColor = new(0f, 0f, 0f, 0.8f);

    /// <summary>Gap left round each filled tile, in tiles, so neighbouring tiles read apart.</summary>
    private const float Inset = 0.04f;

    /// <summary>Space between the hull's top edge and the summary, in UI pixels.</summary>
    private const float LabelGap = 12f;

    public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;

    public WFLandingGuideOverlay()
    {
        IoCManager.InjectDependencies(this);
        _guide = _entities.System<WFLandingGuideSystem>();
        _transform = _entities.System<SharedTransformSystem>();
        _font = new VectorFont(_resources.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 11);
        ZIndex = 20;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_guide.Hull is not { } hull || _guide.Ground == null || _guide.Tiles.Count == 0
            || !_entities.TryGetComponent<EyeComponent>(_player.LocalEntity, out var eye) || eye.Eye == null)
            return false;

        // Once, on the pilot's own picture, not on every level drawn under it or on other viewports.
        if (!ShuttleHullRoofOverlay.IsPilotPass(args.Viewport.Eye, eye.Eye, primaryOnly: true))
            return false;

        return args.Space == OverlaySpace.ScreenSpace || _entities.GetComponent<TransformComponent>(hull).MapID == args.MapId;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_guide.Hull is not { } hull || _guide.Ground is not { } ground
            || !_entities.TryGetComponent<MapGridComponent>(ground, out var groundGrid))
            return;

        if (args.Space == OverlaySpace.WorldSpace)
            DrawFootprint(args.WorldHandle, hull, ground, groundGrid);
        else
            DrawSummary(args.ScreenHandle, hull);
    }

    private void DrawFootprint(DrawingHandleWorld handle, EntityUid hull, EntityUid ground, MapGridComponent groundGrid)
    {
        handle.SetTransform(_transform.GetWorldMatrix(ground));
        var size = groundGrid.TileSize;
        foreach (var (index, status) in _guide.Tiles)
        {
            var box = new Box2(index * size, (index + Vector2i.One) * size).Enlarged(-Inset);
            handle.DrawRect(box, TileColor(status));
        }

        handle.SetTransform(_transform.GetWorldMatrix(hull));
        foreach (var (from, to) in _guide.Outline)
        {
            handle.DrawLine(from, to, OutlineColor);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    private void DrawSummary(DrawingHandleScreen handle, EntityUid hull)
    {
        if (!_entities.TryGetComponent<MapGridComponent>(hull, out var hullGrid))
            return;

        string text;
        Color color;
        if (_guide.Blocked > 0)
        {
            text = Loc.GetString("wf-landing-guide-blocked", ("count", _guide.Blocked));
            color = BlockedColor;
        }
        else if (_guide.Unknown > 0)
        {
            text = Loc.GetString("wf-landing-guide-unknown", ("count", _guide.Unknown));
            color = UnknownColor;
        }
        else
        {
            text = Loc.GetString("wf-landing-guide-clear");
            color = ClearColor;
        }

        // Above whichever corner of the hull is highest on screen, as the view turns with the ship.
        var matrix = _transform.GetWorldMatrix(hull);
        var local = hullGrid.LocalAABB;
        var top = float.MaxValue;
        var centre = Vector2.Zero;
        foreach (var corner in new[] { local.BottomLeft, local.BottomRight, local.TopLeft, local.TopRight })
        {
            var point = _eye.WorldToScreen(Vector2.Transform(corner, matrix));
            top = MathF.Min(top, point.Y);
            centre += point / 4f;
        }

        var scale = _ui.RootControl.UIScale;
        var dimensions = handle.GetDimensions(_font, text, scale);
        var position = new Vector2(centre.X - dimensions.X / 2f, top - dimensions.Y - LabelGap * scale);

        handle.DrawString(_font, position + new Vector2(scale), text, scale, ShadowColor);
        handle.DrawString(_font, position, text, scale, color.WithAlpha(1f));
    }

    /// <summary>The fill for one ground tile.</summary>
    public static Color TileColor(WFLandingTile status)
    {
        return status switch
        {
            WFLandingTile.Clear => ClearColor,
            WFLandingTile.NoGround => UnknownColor,
            _ => BlockedColor,
        };
    }
}
