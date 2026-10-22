using Robust.Client.Graphics;

namespace Content.Client._Mono.PersonalShield;

public sealed partial class PersonalShieldSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayMan = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlayMan.AddOverlay(new PersonalShieldOverlay());
        //_overlayMan.AddOverlay(new PersonalShieldInfoOverlay()); // Forge-Change debug overlay
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayMan.RemoveOverlay<PersonalShieldOverlay>();
        //_overlayMan.RemoveOverlay<PersonalShieldInfoOverlay>(); // Forge-Change debug overlay
    }
}
