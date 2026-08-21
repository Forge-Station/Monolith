using System.Numerics;
using Content.Client._Forge.Atmos;
using Content.Shared._Forge.Atmos.Components;
using Content.Shared._Forge.Atmos.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client._Forge.Atmos.Systems;

public sealed partial class GasCloudSystem : SharedGasCloudSystem
{
    public const string SmokeLayer = "gascloud-smoke";

    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private GasCloudOverlay? _cloudOverlay;

    public override void Initialize()
    {
        base.Initialize();
        _cloudOverlay = new GasCloudOverlay(EntityManager);
        _overlay.AddOverlay(_cloudOverlay);

        SubscribeLocalEvent<GasCloudComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GasCloudComponent, AfterAutoHandleStateEvent>(OnState);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        if (_cloudOverlay != null)
            _overlay.RemoveOverlay(_cloudOverlay);
    }

    private void OnStartup(Entity<GasCloudComponent> ent, ref ComponentStartup args)
    {
        SyncSmokeSprite(ent);
    }

    private void OnState(Entity<GasCloudComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        SyncSmokeSprite(ent);
    }

    private void SyncSmokeSprite(Entity<GasCloudComponent> ent)
    {
        if (!_sprite.LayerMapTryGet(ent.Owner, SmokeLayer, out var layer, false))
            return;

        // Native chemsmoke is 3 tiles; keep a denser core so the deposit still reads as the source.
        var scale = Math.Clamp(ent.Comp.CurrentRadius * 0.55f, 1.2f, 12f);
        _sprite.LayerSetScale(ent.Owner, layer, new Vector2(scale, scale));
        _sprite.LayerSetColor(ent.Owner, layer, ent.Comp.Color.WithAlpha(0.85f));
    }
}
