using Robust.Client.Graphics;

namespace Content.Client._WF.Xenonids.Screech;

/// <summary>Registers the screech shockwave overlay (RMC adds it from its client entry point).</summary>
public sealed partial class RMCXenoScreechShockWaveSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        if (!_overlay.HasOverlay<RMCXenoScreechShockWaveOverlay>())
            _overlay.AddOverlay(new RMCXenoScreechShockWaveOverlay());
    }

    public override void Shutdown()
    {
        _overlay.RemoveOverlay<RMCXenoScreechShockWaveOverlay>();
    }
}
