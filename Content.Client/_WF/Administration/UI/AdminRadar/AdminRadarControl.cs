using System.Numerics;
using Content.Client._WF.Administration.AdminRadar;
using Content.Client.Shuttles.Systems;
using Content.Client.Shuttles.UI;
using Content.Shared._Mono.Detection;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Map;

namespace Content.Client._WF.Administration.UI.AdminRadar;

/// <summary>
/// The mass scanner display for admins: it fills whatever its window gives it, zooms out far past a console's range,
/// and can show every grid and planet site under its real IFF and name and mark the players.
/// </summary>
public sealed class AdminRadarControl : ShuttleNavControl
{
    /// <summary>
    /// Furthest zoom, measured from the centre to the edge of the longer side. Grids past it aren't looked up.
    /// </summary>
    public const float AdminMaxRange = 25600f;

    private const float AdminMinRange = 64f;
    private const float StartRange = 256f;

    /// <summary>How far out an off-screen grid still gets a label at the edge, as on a console.</summary>
    private const float EdgeIffRange = 3000f;

    /// <summary>The nav control's own range rings stop here.</summary>
    private const float LastNavRing = 4096f;

    /// <summary>Players closer together than this on screen share one label.</summary>
    private const float PlayerMergeDistance = 10f;

    private static readonly Color PlayerColor = Color.FromHex("#5CE1E6");

    private readonly AdminRadarSystem _adminRadar;
    private readonly ShuttleSystem _shuttle;
    private readonly SharedTransformSystem _xform;

    private readonly List<(IFFComponent Iff, IFFFlags Flags)> _hiddenIff = new();
    private readonly AdminRadarSwappedSites _swappedSites = new();
    private readonly List<(Vector2 Position, string Name, int Others)> _playerLabels = new();

    public AdminRadarControl() : base(AdminMinRange, AdminMaxRange, StartRange)
    {
        _adminRadar = EntManager.System<AdminRadarSystem>();
        _shuttle = EntManager.System<ShuttleSystem>();
        _xform = EntManager.System<SharedTransformSystem>();

        // The nav control fixes itself to a square; here the window sets the size and the drawing follows it.
        SetSize = new Vector2(float.NaN, float.NaN);
    }

    /// <summary>Drawing centres on the control rather than on the nav control's fixed square.</summary>
    protected override Vector2 MidPointVector => new Vector2(PixelWidth, PixelHeight) / 2f;

    /// <summary>The range spans the longer side, so the nav control's square culling covers the whole view.</summary>
    protected override int ScaledMinimapRadius => Math.Max(1, (int) (MathF.Max(PixelWidth, PixelHeight) / 2f));

    /// <summary>
    /// Takes a scanner state, then lifts the console's range limit again.
    /// </summary>
    public void UpdateAdminState(NavInterfaceState state)
    {
        // The nav control clamps the zoom to the console's range.
        var range = ActualRadarRange;
        UpdateState(state);
        WorldMaxRange = AdminMaxRange;
        WorldMinRange = AdminMinRange;
        ActualRadarRange = Math.Clamp(range, WorldMinRange, WorldMaxRange);
    }

    /// <summary>
    /// Drops any panning and centres on the scanner's own entity again.
    /// </summary>
    public void Recentre()
    {
        if (_consoleEntity is not { } console || !EntManager.EntityExists(console))
            return;

        Offset = TargetOffset = Vector2.Zero;
        _coordinates = new EntityCoordinates(console, Vector2.Zero);
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        var offset = Offset;
        base.MouseMove(args);

        // The nav control pans as if it were still its fixed square; redo the step at this control's scale.
        if (_draggin && MinimapScale > 0f)
            Offset = offset - new Vector2(args.Relative.X, -args.Relative.Y) * UIScale / MinimapScale;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        // Label everything in view, and keep the edge pointers to what a console would show.
        MaximumIFFDistance = MathF.Max(EdgeIffRange, CornerRadarRange);

        // The nav control reads IFF flags, detection and planet sites straight off the components, so all are lifted
        // for this one draw and put back before anything else can look at them.
        DetectionRangeMultiplierComponent? detection = null;
        var alwaysDetect = false;
        if (_adminRadar.TrueIff)
        {
            _shuttle.WfRevealIff(_hiddenIff);
            _adminRadar.RequestSites();
            _adminRadar.RevealSites(_swappedSites);
            if (_consoleEntity is { } console && EntManager.EntityExists(console))
            {
                detection = EntManager.EnsureComponent<DetectionRangeMultiplierComponent>(console);
                alwaysDetect = detection.AlwaysDetect;
                detection.AlwaysDetect = true;
            }
        }

        try
        {
            base.Draw(handle);
        }
        finally
        {
            _shuttle.WfRestoreIff(_hiddenIff);
            _adminRadar.RestoreSites(_swappedSites);
            if (detection != null)
                detection.AlwaysDetect = alwaysDetect;
        }

        if (_coordinates == null || _rotation == null)
            return;

        DrawFarCircles(handle);

        if (_adminRadar.ShowPlayers)
            DrawPlayers(handle, _coordinates.Value, _rotation.Value);
    }

    /// <summary>
    /// Marks every player on the viewed map with a dot and a name. Players bunched together share a label.
    /// </summary>
    private void DrawPlayers(DrawingHandleScreen handle, EntityCoordinates coordinates, Angle rotation)
    {
        var centre = _xform.ToMapCoordinates(coordinates).Offset(rotation.RotateVec(Offset));
        var worldToView = Matrix3Helpers.CreateTranslation(-centre.Position)
                          * Matrix3Helpers.CreateRotation(-rotation)
                          * Matrix3x2.CreateScale(new Vector2(MinimapScale, -MinimapScale))
                          * Matrix3x2.CreateTranslation(MidPointVector);
        var mergeDistance = PlayerMergeDistance * UIScale;

        _playerLabels.Clear();
        foreach (var player in _adminRadar.Players)
        {
            if (player.Ghost)
                continue;

            var spot = EntManager.GetCoordinates(player.Coordinates);
            if (!spot.IsValid(EntManager))
                continue;

            var world = _xform.ToMapCoordinates(spot);
            if (world.MapId != centre.MapId)
                continue;

            var position = Vector2.Transform(world.Position, worldToView);
            if (position.X < 0f || position.Y < 0f || position.X > PixelWidth || position.Y > PixelHeight)
                continue;

            handle.DrawCircle(position, 3f * UIScale, PlayerColor);

            var merged = false;
            for (var i = 0; i < _playerLabels.Count; i++)
            {
                var label = _playerLabels[i];
                if (Vector2.Distance(label.Position, position) > mergeDistance)
                    continue;

                _playerLabels[i] = (label.Position, label.Name, label.Others + 1);
                merged = true;
                break;
            }

            if (!merged)
                _playerLabels.Add((position, player.Name, 0));
        }

        foreach (var (position, name, others) in _playerLabels)
        {
            var text = others == 0
                ? name
                : Loc.GetString("wf-admin-radar-player-group", ("name", name), ("count", others));
            handle.DrawString(Font, position + new Vector2(5f, -8f) * UIScale, text, UIScale * 0.7f, PlayerColor);
        }
    }

    /// <summary>
    /// Continues the nav control's range rings out to the furthest zoom.
    /// </summary>
    private void DrawFarCircles(DrawingHandleScreen handle)
    {
        var cornerDistance = MathF.Sqrt(2f) * WorldRange;
        var color = Color.ToSrgb(Color.LightGray).WithAlpha(0.05f);

        for (var radius = LastNavRing * 2f; radius <= cornerDistance; radius *= 2f)
        {
            // A string, so Fluent doesn't group the digits unlike the nav control's own rings.
            var text = Loc.GetString("wf-admin-radar-ring", ("metres", $"{radius:0}"));
            var textDimensions = handle.GetDimensions(Font, text, UIScale);

            handle.DrawCircle(MidPointVector, MinimapScale * radius, color, false);
            handle.DrawString(Font, ScalePosition(new Vector2(0f, -radius)) - new Vector2(0f, textDimensions.Y), text, UIScale, color);
        }
    }
}
