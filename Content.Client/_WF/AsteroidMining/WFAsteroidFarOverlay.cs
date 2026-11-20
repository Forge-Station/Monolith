using System.Numerics;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.AsteroidMining;

/// <summary>
/// Draws asteroid rocks the client hasn't been sent, from their grid's copy of the rock's look. Grids reach every client
/// but rocks only reach those in view range, so without this a zoomed-out view would show asteroids popping in and out.
/// </summary>
public sealed class WFAsteroidFarOverlay : Overlay
{
    /// <summary>How far the largest rock's sprite reaches past its grid's tiles.</summary>
    private const float SpriteMargin = 4f;

    /// <summary>Sprite state names by variant, so drawing makes no strings.</summary>
    private static readonly string[] RockStates = States("rock");
    private static readonly string[] VeinStates = States("veins");

    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _transform;
    private readonly WFAsteroidVisualsSystem _visuals;
    private readonly EntityQuery<WFAsteroidGridVisualsComponent> _gridVisuals;
    private readonly EntityQuery<WFAsteroidComponent> _rocks;
    private readonly EntityQuery<TransformComponent> _xforms;
    private List<Entity<MapGridComponent>> _grids = new();

    // Below entities, so the rock is lit like the sprite it stands in for.
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public WFAsteroidFarOverlay(IEntityManager entities, WFAsteroidVisualsSystem visuals)
    {
        _map = entities.System<SharedMapSystem>();
        _transform = entities.System<SharedTransformSystem>();
        _visuals = visuals;
        _gridVisuals = entities.GetEntityQuery<WFAsteroidGridVisualsComponent>();
        _rocks = entities.GetEntityQuery<WFAsteroidComponent>();
        _xforms = entities.GetEntityQuery<TransformComponent>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return;

        _grids.Clear();
        _map.FindGridsIntersecting(args.MapId, args.WorldAABB.Enlarged(SpriteMargin), ref _grids);
        var handle = args.WorldHandle;
        foreach (var (uid, _) in _grids)
        {
            if (!_gridVisuals.TryComp(uid, out var visuals) || visuals.Variant >= RockStates.Length || HasRock(uid) ||
                _visuals.FarRsi(visuals) is not { } rsi)
                continue;

            // The rock sits on the centre of tile (0, 0) and turns with its grid.
            var (position, rotation) = _transform.GetWorldPositionRotation(uid);
            handle.SetTransform(position + rotation.RotateVec(new Vector2(0.5f, 0.5f)), rotation);
            DrawState(handle, rsi, RockStates[visuals.Variant], visuals.RockColor);
            DrawState(handle, rsi, VeinStates[visuals.Variant], visuals.VeinColor);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>Whether the rock itself is here to draw its own sprite; out of view range it sits detached in nullspace.</summary>
    private bool HasRock(EntityUid grid)
    {
        var children = _xforms.GetComponent(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (_rocks.HasComp(child))
                return true;
        }

        return false;
    }

    private static void DrawState(DrawingHandleWorld handle, RSI rsi, string state, Color color)
    {
        if (!rsi.TryGetState(state, out var rsiState))
            return;

        var texture = rsiState.Frame0;
        var half = (Vector2) texture.Size / (2f * EyeManager.PixelsPerMeter);
        handle.DrawTextureRect(texture, new Box2(-half, half), color);
    }

    private static string[] States(string prefix)
    {
        var states = new string[8];
        for (var i = 0; i < states.Length; i++)
        {
            states[i] = prefix + i;
        }

        return states;
    }
}
