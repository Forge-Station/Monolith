using Robust.Client.Graphics;

namespace Content.Client._WF.Xenonids.HiveLeader;

public sealed partial class HiveLeaderUISystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        if (!_overlay.HasOverlay<HiveLeaderOverlay>())
            _overlay.AddOverlay(new HiveLeaderOverlay());
    }

    public override void Shutdown()
    {
        _overlay.RemoveOverlay<HiveLeaderOverlay>();
    }
}
