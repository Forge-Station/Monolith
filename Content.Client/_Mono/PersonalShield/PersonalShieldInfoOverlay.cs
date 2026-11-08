using System.Numerics;
using Content.Client.Resources;
using Content.Shared._Mono.PersonalShield;
using Content.Shared.Inventory;
using Content.Shared.StatusIcon.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;

namespace Content.Client._Mono.PersonalShield;

public sealed partial class PersonalShieldInfoOverlay : Overlay
{
    [Dependency] private IEntityManager _entManager = null!;
    [Dependency] private IResourceCache _resourceCache = default!;

    private const int FontSize = 10;
    private readonly SharedTransformSystem _transform;
    private readonly SpriteSystem _sprite;
    private readonly InventorySystem _inventory;
    private readonly Font _font;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public PersonalShieldInfoOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entManager.System<SharedTransformSystem>();
        _sprite = _entManager.System<SpriteSystem>();
        _inventory = _entManager.System<InventorySystem>();
        _font = _resourceCache.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", FontSize);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.ViewportControl == null)
            return;

        var aabb = args.WorldAABB;
        var bounds = args.ViewportBounds;
        var bw = bounds.Right - bounds.Left;
        var bh = bounds.Bottom - bounds.Top;

        if (bw <= 0 || bh <= 0 || aabb.Width <= 0f || aabb.Height <= 0f)
            return;

        var handle = args.ScreenHandle;
        var eyeMap = args.Viewport.Eye?.Position.MapId;

        var query = _entManager.EntityQueryEnumerator<PersonalShieldComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var shield, out var spriteComponent))
        {
            if (shield.Runtime.Form <= 0f && shield.Runtime.Shatter <= 0f)
                continue;

            if (!_inventory.TryGetContainingEntity(uid, out var wearer))
                continue;

            if (!_entManager.TryGetComponent(wearer, out SpriteComponent? sprite) || !sprite.Visible)
                continue;

            if (!_entManager.TryGetComponent(wearer, out TransformComponent? xform))
                continue;

            if (eyeMap != null && xform.MapID != eyeMap.Value)
                continue;

            var worldPos = _transform.GetWorldPosition(xform);
            var screenPos = args.ViewportControl.WorldToScreen(worldPos);

            var bounds1 = _entManager.GetComponentOrNull<StatusIconComponent>(uid)?.Bounds ?? spriteComponent.Bounds;
            var yOffset = -bounds1.Height * EyeManager.PixelsPerMeter / 2 - 10f;

            var text = $"{shield.Runtime.Charge:F0} / {shield.Shield.MaxCharge:F0}";

            var dims = handle.GetDimensions(_font, text, 1);
            yOffset -= dims.Y+30;
            handle.DrawString(_font, screenPos + new Vector2(-dims.X / 2, yOffset), text, Color.Red);
        }
    }
}
