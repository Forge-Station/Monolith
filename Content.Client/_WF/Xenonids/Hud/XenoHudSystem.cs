using Robust.Client.Graphics;

namespace Content.Client._WF.Xenonids.Hud;

public sealed partial class XenoHudSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        if (!_overlay.HasOverlay<XenoHudOverlay>())
            _overlay.AddOverlay(new XenoHudOverlay());
    }

    public override void Shutdown()
    {
        _overlay.RemoveOverlay<XenoHudOverlay>();
    }
}
