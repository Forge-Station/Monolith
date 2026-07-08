using System.Linq;
using System.Numerics;
using Content.Client.Clickable;
using Content.Client.Decals;
using Content.Shared._Forge.WallPaint;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Placement;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client._Forge.WallPaint;

public sealed partial class WallPaintPlacementSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlacementManager _placement = default!;
    [Dependency] private InputSystem _inputSystem = default!;
    [Dependency] private DecalPlacementSystem _decals = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly ClickableEntityComparer _comparer = new();

    private Color _color = Color.FromHex("#8B0000CC");
    private bool _active;
    private bool _erase;

    public Color Color => _color;
    public bool Erase => _erase;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(EngineKeyFunctions.EditorPlaceObject, new PointerInputCmdHandler(HandlePaint, outsidePrediction: true))
            .Register<WallPaintPlacementSystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<WallPaintPlacementSystem>();
        base.Shutdown();
    }

    public void SetActive(bool active)
    {
        if (_active == active)
            return;

        _active = active;

        if (_active)
        {
            _placement.Clear();
            _decals.SetActive(false);
            _input.Contexts.SetActiveContext("editor");
        }
        else
        {
            _inputSystem.SetEntityContextActive();
        }
    }

    public void SetColor(Color color)
    {
        _color = color;
    }

    public void SetErase(bool erase)
    {
        _erase = erase;
    }

    private bool HandlePaint(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (!_active)
            return false;

        var target = GetPaintTarget(coords, uid);
        if (target == null)
            return true;

        RaiseNetworkEvent(new WallPaintRequestEvent(GetNetEntity(target.Value), _color, _erase));
        return true;
    }

    private EntityUid? GetPaintTarget(EntityCoordinates coords, EntityUid uid)
    {
        if (uid.IsValid() && HasComp<PaintableWallComponent>(uid))
            return uid;

        foreach (var entity in GetClickableEntities(_transform.ToMapCoordinates(coords)))
        {
            if (HasComp<PaintableWallComponent>(entity))
                return entity;
        }

        return null;
    }

    private IEnumerable<EntityUid> GetClickableEntities(MapCoordinates coordinates)
    {
        if (_eye.CurrentEye == null)
            return Array.Empty<EntityUid>();

        var spriteTree = EntityManager.System<SpriteTreeSystem>();
        var entities = spriteTree.QueryAabb(coordinates.MapId, Box2.CenteredAround(coordinates.Position, new Vector2(1, 1)));
        var found = new List<(EntityUid Uid, int DrawDepth, uint RenderOrder, float Bottom)>(entities.Count);
        var clickQuery = GetEntityQuery<ClickableComponent>();
        var clickables = EntityManager.System<ClickableSystem>();

        foreach (var entity in entities)
        {
            if (clickQuery.TryGetComponent(entity.Uid, out var component) &&
                clickables.CheckClick((entity.Uid, component, entity.Component, entity.Transform), coordinates.Position, _eye.CurrentEye, out var drawDepth, out var renderOrder, out var bottom))
            {
                found.Add((entity.Uid, drawDepth, renderOrder, bottom));
            }
        }

        if (found.Count == 0)
            return Array.Empty<EntityUid>();

        found.Sort(_comparer);
        return found.Select(entity => entity.Uid);
    }

    private sealed class ClickableEntityComparer : IComparer<(EntityUid Uid, int DrawDepth, uint RenderOrder, float Bottom)>
    {
        public int Compare(
            (EntityUid Uid, int DrawDepth, uint RenderOrder, float Bottom) x,
            (EntityUid Uid, int DrawDepth, uint RenderOrder, float Bottom) y)
        {
            var cmp = y.DrawDepth.CompareTo(x.DrawDepth);
            if (cmp != 0)
                return cmp;

            cmp = y.RenderOrder.CompareTo(x.RenderOrder);
            if (cmp != 0)
                return cmp;

            cmp = -y.Bottom.CompareTo(x.Bottom);
            if (cmp != 0)
                return cmp;

            return y.Uid.CompareTo(x.Uid);
        }
    }
}
