using Content.Shared._Forge.Overlay;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Player;

namespace Content.Client._Forge.Overlay;

[UsedImplicitly]
public sealed partial class TunnelVisionOverlayUIController : UIController
{
    [Dependency] private IOverlayManager _overlayManager = default!;

    private TunnelVisionOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new TunnelVisionOverlay();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnAttach);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnDetach);
    }

    private void OnAttach(LocalPlayerAttachedEvent args)
    {
        _overlayManager.AddOverlay(_overlay);
    }

    private void OnDetach(LocalPlayerDetachedEvent args)
    {
        _overlayManager.RemoveOverlay(_overlay);
    }
}
