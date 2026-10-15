using System.Linq;
using System.Numerics;
using Content.Shared._Forge.KIAS;
using Content.Shared.Hands.EntitySystems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client._Forge.KIAS;

public sealed class KiasCoverageSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    private CoverageOverlay? _overlay;
    private bool _requiresHeld;
    public EntityUid? Owner { get; private set; }
    public KiasCoverageGeometry? Geometry { get; private set; }

    public override void Initialize()
    {
        _overlay = new CoverageOverlay(this);
        _overlays.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        if (_overlay != null) _overlays.RemoveOverlay(_overlay);
        Clear();
    }

    public void Show(EntityUid owner, KiasCoverageGeometry? geometry, bool requiresHeld)
    {
        _requiresHeld = requiresHeld;
        Owner = geometry is { Large: false } ? owner : null;
        Geometry = geometry is { Large: false } ? geometry : null;
    }

    public void Clear(EntityUid? owner = null)
    {
        if (owner != null && owner != Owner) return;
        Owner = null;
        Geometry = null;
    }

    public override void FrameUpdate(float frameTime)
    {
        if (Geometry is not { } geometry || Owner is not { } owner) return;
        if (_players.LocalEntity is not { } player || TerminatingOrDeleted(owner)
            || !TryGetEntity(geometry.Target, out var target) || target is not { } uid || TerminatingOrDeleted(uid)
            || _requiresHeld && !_hands.EnumerateHeld(player).Contains(owner))
        {
            Clear();
            return;
        }
        var userPosition = _transform.GetMapCoordinates(player);
        var targetPosition = _transform.GetMapCoordinates(uid);
        if (userPosition.MapId != targetPosition.MapId || Vector2.DistanceSquared(userPosition.Position, targetPosition.Position) > 9)
            Clear();
    }

    private sealed class CoverageOverlay(KiasCoverageSystem system) : Robust.Client.Graphics.Overlay
    {
        public override OverlaySpace Space => OverlaySpace.WorldSpace;

        protected override void Draw(in OverlayDrawArgs args)
        {
            if (system.Geometry is not { } geometry || !system.TryGetEntity(geometry.Target, out var target) || target is not { } uid
                || !system.TryComp<TransformComponent>(uid, out var transform) || transform.MapID != args.MapId)
                return;
            var handle = args.WorldHandle;
            if (geometry.Shape is KiasCoverageShape.Data or KiasCoverageShape.Room)
            {
                if (!system.TryGetEntity(geometry.Grid, out var grid) || grid is not { } gridUid) return;
                handle.SetTransform(system._transform.GetWorldMatrix(gridUid));
                foreach (var cell in geometry.Cells)
                    handle.DrawRect(new Box2(cell.X, cell.Y, cell.X + 1, cell.Y + 1), Color.Cyan.WithAlpha(0.2f));
                foreach (var cell in geometry.BoundaryCells)
                    handle.DrawRect(new Box2(cell.X, cell.Y, cell.X + 1, cell.Y + 1), Color.Orange.WithAlpha(0.35f));
            }
            else
            {
                handle.SetTransform(system._transform.GetWorldMatrix(uid));
                handle.DrawCircle(Vector2.Zero, geometry.Radius, Color.Cyan.WithAlpha(0.15f));
                handle.DrawCircle(Vector2.Zero, geometry.Radius, Color.Cyan.WithAlpha(0.8f), false);
            }
            handle.SetTransform(Matrix3x2.Identity);
        }
    }
}
