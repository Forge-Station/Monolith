using Content.Client.Resources;
using Content.Shared._Crescent.ShipShields;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Client.Physics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Content.Client._Crescent.ShipShields;

public sealed class ShipShieldOverlay : Overlay
{
    private readonly FixtureSystem _fixture;
    private readonly SharedPhysicsSystem _physics;
    private readonly IMapManager _mapManager;
    private readonly IEntityManager _entManager;
    private readonly ShaderInstance _unshadedShader;
    private readonly List<DrawVertexUV2D> _verts = new(128);
    private readonly Texture _shieldTexture;
    // Forge-Change: grids already drawn from a PVS-local bubble entity this frame.
    private readonly HashSet<EntityUid> _drawnGrids = new();
    private List<Entity<MapGridComponent>> _gridBuffer = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public ShipShieldOverlay(
        IEntityManager entityManager,
        IPrototypeManager prototypeManager,
        IResourceCache resourceCache,
        IMapManager mapManager)
    {
        _entManager = entityManager;
        _mapManager = mapManager;
        _fixture = _entManager.EntitySysManager.GetEntitySystem<FixtureSystem>();
        _physics = _entManager.EntitySysManager.GetEntitySystem<Robust.Client.Physics.PhysicsSystem>();
        _shieldTexture = resourceCache.GetTexture("/Textures/_Crescent/ShipShields/shieldtex.png");
        _unshadedShader = prototypeManager.Index<ShaderPrototype>("unshaded").Instance();
        ZIndex = 8;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        handle.UseShader(_unshadedShader);
        _drawnGrids.Clear();

        // Forge-Change-Start: Pass 1 — textured bubble when streamed via PVS (close range).
        var enumerator = _entManager.AllEntityQueryEnumerator<ShipShieldVisualsComponent, FixturesComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var visuals, out var fixtures, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            if (xform.GridUid is { } gridUid)
                _drawnGrids.Add(gridUid);

            var fixture = _fixture.GetFixtureOrNull(uid, "shield", fixtures);
            if (fixture is not { Shape: ChainShape chain })
                continue;

            DrawShieldChain(handle, uid, chain, xform, _shieldTexture, visuals.ShieldColor, _verts);
            _verts.Clear();
        }

        // Pass 2 — draw from replicated grid snapshot when the bubble is not in client PVS (scanner range).
        var bounds = args.WorldBounds.Enlarged(128f);
        _gridBuffer.Clear();
        _mapManager.FindGridsIntersecting(args.MapId, bounds, ref _gridBuffer, approx: true, includeMap: false);

        foreach (var grid in _gridBuffer)
        {
            var gUid = grid.Owner;
            if (_drawnGrids.Contains(gUid))
                continue;

            if (!_entManager.TryGetComponent<ShipShieldGridStateComponent>(gUid, out var shieldState)
                || !shieldState.HasEmitter
                || !shieldState.Online)
            {
                continue;
            }

            if (_entManager.HasComponent<FTLComponent>(gUid))
                continue;

            if (!_entManager.TryGetComponent<TransformComponent>(gUid, out var gridXform))
                continue;

            var outline = ShipShieldOutline.GetVertices(grid.Comp, shieldState.Padding);
            DrawShieldOutline(handle, gUid, grid.Comp, gridXform, outline, _shieldTexture, shieldState.ShieldColor, _verts);
            _verts.Clear();
        }
        // Forge-Change-End
    }

    private void DrawShieldChain(
        DrawingHandleWorld handle,
        EntityUid uid,
        ChainShape chain,
        TransformComponent xform,
        Texture tex,
        Color color,
        List<DrawVertexUV2D> verts)
    {
        var localPos = xform.LocalPosition;
        var transform = _physics.GetPhysicsTransform(uid);

        for (var i = 0; i < chain.Count; i++)
        {
            var next = (i + 1) % chain.Count;

            var leftVertex = VertexToWorldPos(chain.Vertices[i], transform);
            var rightVertex = VertexToWorldPos(chain.Vertices[next], transform);
            var leftCorner = Corner(localPos, leftVertex, transform);
            var rightCorner = Corner(localPos, rightVertex, transform);

            verts.Add(new DrawVertexUV2D(leftVertex, new Vector2(0, 1)));
            verts.Add(new DrawVertexUV2D(rightVertex, new Vector2(1, 1)));
            verts.Add(new DrawVertexUV2D(leftCorner, Vector2.Zero));

            verts.Add(new DrawVertexUV2D(rightVertex, new Vector2(1, 1)));
            verts.Add(new DrawVertexUV2D(leftCorner, Vector2.Zero));
            verts.Add(new DrawVertexUV2D(rightCorner, new Vector2(1, 0)));
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, texture: tex, CollectionsMarshal.AsSpan(verts), color);
    }

    // Forge-Change: world draw from grid-local outline when bubble entity is not PVS-streamed.
    private void DrawShieldOutline(
        DrawingHandleWorld handle,
        EntityUid gridUid,
        MapGridComponent mapGrid,
        TransformComponent gridXform,
        Vector2[] outline,
        Texture tex,
        Color color,
        List<DrawVertexUV2D> verts)
    {
        var transform = _physics.GetPhysicsTransform(gridUid);
        var localPos = mapGrid.LocalAABB.Center;

        for (var i = 0; i < outline.Length - 1; i++)
        {
            var leftLocal = outline[i];
            var rightLocal = outline[i + 1];

            var leftVertex = Vector2.Transform(leftLocal, gridXform.WorldMatrix);
            var rightVertex = Vector2.Transform(rightLocal, gridXform.WorldMatrix);
            var leftCorner = Corner(localPos, leftVertex, transform);
            var rightCorner = Corner(localPos, rightVertex, transform);

            verts.Add(new DrawVertexUV2D(leftVertex, new Vector2(0, 1)));
            verts.Add(new DrawVertexUV2D(rightVertex, new Vector2(1, 1)));
            verts.Add(new DrawVertexUV2D(leftCorner, Vector2.Zero));

            verts.Add(new DrawVertexUV2D(rightVertex, new Vector2(1, 1)));
            verts.Add(new DrawVertexUV2D(leftCorner, Vector2.Zero));
            verts.Add(new DrawVertexUV2D(rightCorner, new Vector2(1, 0)));
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, texture: tex, CollectionsMarshal.AsSpan(verts), color);
    }

    private static Vector2 VertexToWorldPos(Vector2 vertexPos, Transform transform)
    {
        return Transform.Mul(transform, vertexPos);
    }

    private static Vector2 Corner(Vector2 localPos, Vector2 vertexPos, Transform transform, float radius = 1.3f)
    {
        var localXform = Transform.Mul(transform, localPos);
        var cornerPos = Vector2.Subtract(vertexPos, localXform);
        cornerPos.Normalize();
        cornerPos *= radius;

        return Vector2.Subtract(vertexPos, cornerPos);
    }
}
