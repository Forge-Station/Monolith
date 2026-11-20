using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.TractorBeam;

public sealed partial class TractorBeamVisualizerSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new TractorBeamOverlay(EntityManager, _prototypes, _timing));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<TractorBeamOverlay>();
        base.Shutdown();
    }
}
