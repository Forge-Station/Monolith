using Content.Shared.StatusEffect;
using Robust.Client.Graphics;
using Robust.Shared.Player;

namespace Content.Client.Overlays;

public sealed partial class TunnelVisionSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private ISharedPlayerManager _playerMan = default!;
    private TunnelVisionOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TunnelVisionComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<TunnelVisionComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<TunnelVisionComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<TunnelVisionComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);

        _overlay = new();
    }

    private void OnInit(EntityUid uid, TunnelVisionComponent component, ComponentInit args)
    {
        if (uid == _playerMan.LocalEntity)
            _overlayMan.AddOverlay(_overlay);
    }

    private void OnShutdown(EntityUid uid, TunnelVisionComponent component, ComponentShutdown args)
    {
        if (uid == _playerMan.LocalEntity)
            _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnPlayerAttached(EntityUid uid, TunnelVisionComponent component, LocalPlayerAttachedEvent args)
    {
        _overlayMan.AddOverlay(_overlay);
    }

    private void OnPlayerDetached(EntityUid uid, TunnelVisionComponent component, LocalPlayerDetachedEvent args)
    {
        _overlayMan.RemoveOverlay(_overlay);
    }
}
