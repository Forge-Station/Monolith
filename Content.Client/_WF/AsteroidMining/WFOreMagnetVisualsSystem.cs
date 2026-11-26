using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.AsteroidMining;

/// <summary>Registers the overlay that draws ore magnets pulling ore.</summary>
public sealed partial class WFOreMagnetVisualsSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WFOreMagnetOverlay(EntityManager, _prototypes, _timing));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<WFOreMagnetOverlay>();
        base.Shutdown();
    }
}
