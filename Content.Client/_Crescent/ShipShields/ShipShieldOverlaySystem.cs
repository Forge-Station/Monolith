using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._Crescent.ShipShields;

public sealed partial class ShipShieldOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IMapManager _mapManager = default!; // Forge-Change

    public override void Initialize()
    {
        base.Initialize();
        _overlayManager.AddOverlay(new ShipShieldOverlay(EntityManager, _prototypeManager, _resourceCache)); // Forge-Change
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<ShipShieldOverlay>();
    }
}
