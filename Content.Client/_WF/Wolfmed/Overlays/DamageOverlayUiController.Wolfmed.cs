using Content.Shared._Onyx.Wounds;

namespace Content.Client.UserInterface.Systems.DamageOverlays;

public sealed partial class DamageOverlayUiController
{
    /// <summary>True when Wolfmed's pain level, not brute damage, drives the vignette for this entity.</summary>
    private bool WolfmedPainOwnsVignette(EntityUid entity) => EntityManager.HasComponent<PainComponent>(entity);
}
