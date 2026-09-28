using System.Numerics;
using Content.Client.Pinpointer.UI;
using Content.Shared._WF.Shuttles;
using Robust.Client.Graphics;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Client._WF.Shuttles.UI;

/// <summary>
/// Nav map of a single ship, framed so the whole hull fits in the control, with hull telemetry drawn
/// over it as line work so it reads against the wireframe underneath.
/// </summary>
public class ShipViewControl : NavMapControl
{
    /// <summary>
    /// Spare room left around the hull when fitting.
    /// </summary>
    private const float FitMargin = 1.08f;

    /// <summary>
    /// Pressure below this counts as vented rather than merely off-nominal.
    /// </summary>
    private const float VentedPressure = 20f;

    private static readonly Color FireColor = Color.FromHex("#ff8038");
    private static readonly Color LowPressureColor = Color.FromHex("#3fc8ff");
    private static readonly Color HighPressureColor = Color.FromHex("#ff5ce0");
    private static readonly Color UnpoweredColor = Color.FromHex("#ffc23f");

    /// <summary>
    /// Condition bands, worst first. Drawing is batched per band so a wrecked ship still costs four
    /// draw calls rather than one per tile.
    /// </summary>
    private static readonly (float Above, Color Color)[] DamageBands =
    {
        (0.75f, Color.FromHex("#e8e04a")),
        (0.50f, Color.FromHex("#ffae3d")),
        (0.25f, Color.FromHex("#ff6a2b")),
        (0.00f, Color.FromHex("#ff3030")),
    };

    private EntityUid? _grid;
    private EntityUid? _console;

    /// <summary>
    /// Set until the hull has been framed. Damage doesn't re-trigger it, so combat can't yank the view
    /// out from under the pilot.
    /// </summary>
    private bool _needsFit = true;

    private readonly List<ShipTileStatus> _status = new();

    /// <summary>
    /// Line buffers reused every frame: one per damage band, then fire, low and high pressure, and
    /// unpowered. Rebuilding these each frame is fine, reallocating them is not.
    /// </summary>
    private readonly ValueList<Vector2>[] _damageLines = new ValueList<Vector2>[DamageBands.Length];
    private ValueList<Vector2> _fireLines;
    private ValueList<Vector2> _lowPressureLines;
    private ValueList<Vector2> _highPressureLines;
    private ValueList<Vector2> _unpoweredLines;

    public bool ShowDamage { get; set; } = true;
    public bool ShowFire { get; set; } = true;
    public bool ShowPressure { get; set; }
    public bool ShowPower { get; set; }

    /// <summary>
    /// What this console is drawing, so the server can skip sending the rest.
    /// </summary>
    public ShipOverlays Overlays
    {
        get
        {
            var flags = ShipOverlays.None;

            if (ShowDamage)
                flags |= ShipOverlays.Damage;

            if (ShowFire)
                flags |= ShipOverlays.Fire;

            if (ShowPressure)
                flags |= ShipOverlays.Pressure;

            if (ShowPower)
                flags |= ShipOverlays.Power;

            return flags;
        }
    }

    public ShipViewControl()
    {
        // Ships run from one-room shuttles to capitals, so the station map's zoom limits are too narrow.
        WorldMinRange = 4f;
        WorldMaxRange = 512f;

        // Sits between the hull lines and the tracked blips, so telemetry never hides the console marker.
        PostWallDrawingAction += DrawStatus;
    }

    public virtual void SetGrid(EntityUid? grid)
    {
        if (_grid == grid)
            return;

        _grid = grid;
        MapUid = grid;
        _needsFit = true;
        _status.Clear();
        ForceNavMapUpdate();
    }

    public void SetConsole(EntityUid? console)
    {
        if (_console == console)
            return;

        _console = console;
        TrackedCoordinates.Clear();

        if (console != null)
            TrackedCoordinates.Add(new EntityCoordinates(console.Value, Vector2.Zero), (true, Color.Lime));
    }

    public void SetStatus(IEnumerable<ShipTileStatus> tiles)
    {
        _status.Clear();
        _status.AddRange(tiles);
    }

    public void ClearStatus()
    {
        _status.Clear();
    }

    /// <summary>
    /// Re-frames the whole hull, discarding the user's zoom and panning.
    /// </summary>
    public void FitToShip()
    {
        _needsFit = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        TryFit();
        base.Draw(handle);
    }

    private void TryFit()
    {
        if (!_needsFit || !EntManager.TryGetComponent<MapGridComponent>(_grid, out var grid))
            return;

        var aabb = grid.LocalAABB;

        // The grid may not have arrived yet, in which case keep waiting for it.
        if (aabb.Width <= 0f || aabb.Height <= 0f)
            return;

        _needsFit = false;

        // Draw offsets are measured from the grid's centre of mass, not its origin.
        var center = aabb.Center;

        if (EntManager.TryGetComponent<PhysicsComponent>(_grid, out var body))
            center -= body.LocalCenter;

        Offset = center;
        TargetOffset = center;
        Recentering = false;

        var range = MathF.Max(aabb.Width, aabb.Height) / 2f * FitMargin;
        WorldRange = Math.Clamp(range, WorldMinRange, WorldMaxRange);
        ActualRadarRange = WorldRange;
    }

    private void DrawStatus(DrawingHandleScreen handle)
    {
        if (_status.Count == 0)
            return;

        var offset = GetOffset();

        // Fire blinks so it pulls the eye even on a busy hull.
        const float blinkPeriod = 1f;
        var lit = Timing.RealTime.TotalSeconds % blinkPeriod > blinkPeriod / 2f;
        var drawFire = ShowFire && lit;

        for (var i = 0; i < _damageLines.Length; i++)
        {
            _damageLines[i].Clear();
        }

        _fireLines.Clear();
        _lowPressureLines.Clear();
        _highPressureLines.Clear();
        _unpoweredLines.Clear();

        // One pass over the tiles, bucketed by what each needs drawn.
        foreach (var tile in _status)
        {
            if (ShowDamage && (tile.Flags & ShipTileFlags.Damaged) != 0)
                AddBox(ref _damageLines[BandFor(tile.Integrity / 255f)], tile.Index, 0.14f, offset);

            if (drawFire && (tile.Flags & ShipTileFlags.Fire) != 0)
                AddCross(ref _fireLines, tile.Index, 0.22f, offset);

            if (ShowPressure && (tile.Flags & ShipTileFlags.Pressure) != 0)
            {
                const float inset = 0.2f;

                if (tile.Pressure * 2f < VentedPressure * 5f)
                {
                    // Backslash for thin air.
                    _lowPressureLines.Add(Project(new Vector2(tile.Index.X + inset, tile.Index.Y + 1f - inset), offset));
                    _lowPressureLines.Add(Project(new Vector2(tile.Index.X + 1f - inset, tile.Index.Y + inset), offset));
                }
                else
                {
                    // Forward slash for over-pressure.
                    _highPressureLines.Add(Project(new Vector2(tile.Index.X + inset, tile.Index.Y + inset), offset));
                    _highPressureLines.Add(Project(new Vector2(tile.Index.X + 1f - inset, tile.Index.Y + 1f - inset), offset));
                }
            }

            if (ShowPower && (tile.Flags & ShipTileFlags.Unpowered) != 0)
            {
                // A flat bar, so it doesn't read as either the damage box or the fire cross.
                const float inset = 0.25f;
                _unpoweredLines.Add(Project(new Vector2(tile.Index.X + inset, tile.Index.Y + 0.5f), offset));
                _unpoweredLines.Add(Project(new Vector2(tile.Index.X + 1f - inset, tile.Index.Y + 0.5f), offset));
            }
        }

        for (var i = 0; i < _damageLines.Length; i++)
        {
            Flush(handle, ref _damageLines[i], DamageBands[i].Color);
        }

        Flush(handle, ref _lowPressureLines, LowPressureColor);
        Flush(handle, ref _highPressureLines, HighPressureColor);
        Flush(handle, ref _unpoweredLines, UnpoweredColor);
        Flush(handle, ref _fireLines, FireColor);
    }

    private static int BandFor(float integrity)
    {
        for (var i = 0; i < DamageBands.Length; i++)
        {
            if (integrity >= DamageBands[i].Above)
                return i;
        }

        return DamageBands.Length - 1;
    }

    private static void Flush(DrawingHandleScreen handle, ref ValueList<Vector2> lines, Color color)
    {
        if (lines.Count > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.LineList, lines.Span, Color.ToSrgb(color));
    }

    private void AddCross(ref ValueList<Vector2> lines, Vector2i index, float inset, Vector2 offset)
    {
        lines.Add(Project(new Vector2(index.X + inset, index.Y + inset), offset));
        lines.Add(Project(new Vector2(index.X + 1f - inset, index.Y + 1f - inset), offset));
        lines.Add(Project(new Vector2(index.X + inset, index.Y + 1f - inset), offset));
        lines.Add(Project(new Vector2(index.X + 1f - inset, index.Y + inset), offset));
    }

    private void AddBox(ref ValueList<Vector2> lines, Vector2i index, float inset, Vector2 offset)
    {
        var left = index.X + inset;
        var right = index.X + 1f - inset;
        var bottom = index.Y + inset;
        var top = index.Y + 1f - inset;

        var bl = Project(new Vector2(left, bottom), offset);
        var br = Project(new Vector2(right, bottom), offset);
        var tr = Project(new Vector2(right, top), offset);
        var tl = Project(new Vector2(left, top), offset);

        lines.Add(bl);
        lines.Add(br);
        lines.Add(br);
        lines.Add(tr);
        lines.Add(tr);
        lines.Add(tl);
        lines.Add(tl);
        lines.Add(bl);
    }

    /// <summary>
    /// Grid-local position to a point on the control, matching how the nav map draws its own lines.
    /// </summary>
    private Vector2 Project(Vector2 local, Vector2 offset)
    {
        var p = local - offset;
        return ScalePosition(new Vector2(p.X, -p.Y));
    }
}
