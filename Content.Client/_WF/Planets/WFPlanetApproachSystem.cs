using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Planets;

/// <summary>Owns the planet approach overlay; everything it needs rides the hull as WFPlanetApproachComponent.</summary>
public sealed partial class WFPlanetApproachSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WFPlanetApproachOverlay(EntityManager, _proto, _player, _timing));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WFPlanetApproachOverlay>();
    }
}
