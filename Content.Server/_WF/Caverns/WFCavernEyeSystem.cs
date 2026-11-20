using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server.Parallax;
using Content.Shared._CE.ZLevels.Core.Components;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Caverns;

/// <summary>Gives a viewer standing on the ground with one of its holes in view a z-level eye on the cavern under it.</summary>
// Everyone else stays capped at the ground, so the cavern only generates and streams where a hole shows it.
public sealed partial class WFCavernEyeSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private CEZLevelsSystem _zLevels = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private BiomeSystem _biome = default!;

    /// <summary>Tiles past the viewer's view of the ground at which a hole brings the cavern in.</summary>
    public const float EnterMargin = 4f;

    /// <summary>Tiles past the viewer's view of the ground every hole must be before the cavern goes again.</summary>
    public const float LeaveMargin = 12f;

    /// <summary>How often viewers are measured against the holes.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>Half the side of the square an eye sees at view scale 1; net.pvs_range is the whole side.</summary>
    private float _viewHalf;
    private TimeSpan _nextCheck;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CVars.NetMaxUpdateRange, size => _viewHalf = size / 2f, true);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;

        var query = EntityQueryEnumerator<CEZLevelViewerComponent, ActorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            var seeing = CompOrNull<WFCavernViewerComponent>(uid)?.Ground;

            if (_zLevels.WfGroundInView(uid) is not { } view)
            {
                if (seeing != null)
                    RemComp<WFCavernViewerComponent>(uid);

                continue;
            }

            // A change rebuilds the viewer's eyes, which asks SeesCavern again and records the answer.
            if (Sees(uid, view.Ground, _transform.GetWorldPosition(xform), view.Scale) != (seeing == view.Ground))
                _zLevels.WfQueueViewerUpdate(uid);
        }
    }

    /// <summary>Measures every viewer against the holes on the next tick, as when a hole has just opened or closed.</summary>
    public void CheckSoon()
    {
        _nextCheck = TimeSpan.Zero;
    }

    /// <summary>Whether a viewer whose eyes reach this ground gets an eye on its cavern; records the answer.</summary>
    // A viewer who has the cavern keeps it out to LeaveMargin.
    public bool SeesCavern(EntityUid viewer, EntityUid ground, Vector2 position, float viewScale)
    {
        if (!Sees(viewer, ground, position, viewScale))
        {
            RemComp<WFCavernViewerComponent>(viewer);
            return false;
        }

        EnsureComp<WFCavernViewerComponent>(viewer).Ground = ground;
        return true;
    }

    private bool Sees(EntityUid viewer, EntityUid ground, Vector2 position, float viewScale)
    {
        // Only a viewer standing on the ground looks into the cavern; from the air or orbit a hole shows dark.
        if (Transform(viewer).MapUid != ground)
            return false;

        // A ghost that may not generate terrain would generate the cavern through its eye.
        if (!_biome.WfCanLoad(viewer))
            return false;

        if (!TryComp<WFCavernGroundComponent>(ground, out var comp) || TerminatingOrDeleted(comp.Cavern))
            return false;

        var seeing = CompOrNull<WFCavernViewerComponent>(viewer)?.Ground == ground;
        var reach = _viewHalf * viewScale + (seeing ? LeaveMargin : EnterMargin);
        return AnyHoleWithin(comp, position, reach);
    }

    /// <summary>Whether an open hole tile lies within a square of this half-size around a world position.</summary>
    // Each shade marks one open hole tile, and the ground grid is its own map, so its tiles are world tiles.
    public static bool AnyHoleWithin(WFCavernGroundComponent ground, Vector2 position, float reach)
    {
        foreach (var tile in ground.Shades.Keys)
        {
            var dx = MathF.Max(MathF.Max(tile.X - position.X, position.X - tile.X - 1f), 0f);
            var dy = MathF.Max(MathF.Max(tile.Y - position.Y, position.Y - tile.Y - 1f), 0f);

            if (dx <= reach && dy <= reach)
                return true;
        }

        return false;
    }
}
