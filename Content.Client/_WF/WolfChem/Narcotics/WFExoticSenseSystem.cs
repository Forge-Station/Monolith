using Robust.Client.Graphics;

namespace Content.Client._WF.WolfChem.Narcotics;

/// <summary>Draws the exotic matter sense for the local player.</summary>
public sealed class WFExoticSenseSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    private WFExoticSenseOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new WFExoticSenseOverlay();
        _overlays.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_overlay);
    }
}
