using Content.Shared._WF.Xenonids.Pheromones;
using Robust.Client.Graphics;

namespace Content.Client._WF.Xenonids.Pheromones;

public sealed partial class XenoPheromonesSystem : SharedXenoPheromonesSystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlays.AddOverlay(new XenoPheromonesOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay<XenoPheromonesOverlay>();
    }
}
