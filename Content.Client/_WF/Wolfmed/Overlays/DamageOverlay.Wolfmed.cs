using Content.Client._WF.Wolfmed.Overlays;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;

namespace Content.Client.UserInterface.Systems.DamageOverlays.Overlays;

public sealed partial class DamageOverlay
{
    /// <summary>
    /// True once Wolfmed draws this player's damage view itself: the grey dead screen after death, or, for
    /// a mechanical body, the synthetic diagnostics readout, which replaces the vignette outright (HUD).
    /// </summary>
    private bool WolfmedOwnsScreen()
    {
        if (_playerManager.LocalEntity is { } local &&
            _entityManager.System<WolfmedSyntheticHudOverlaySystem>().OwnsView(local))
            return true;

        return _entityManager.System<WolfmedDyingEffectsSystem>().OwnsDeadScreen;
    }

    /// <summary>Overrides the brute vignette with Onyx's pain level on wound hosts.</summary>
    private void TryApplyWolfmedPain()
    {
        // Onyx replaces the brute-damage vignette with a pain vignette
        // (ONYX Content.Shared/DamageOverlay/SharedDamageOverlaySystem.cs:93-97). Wolfgate has no shared damage
        // overlay and RT 277 has no [SubscribeLocalEvent], so this reads the networked PainComponent directly -
        // PainSystem.GetPain is a pure read of AutoNetworkedFields. The system is resolved here, not in a
        // constructor: the overlay is built from DamageOverlayUiController.Initialize(), which can run before
        // entity systems exist.
        if (State == MobState.Dead ||
            _playerManager.LocalEntity is not { } local ||
            !_entityManager.TryGetComponent(local, out PainComponent? pain) ||
            pain.SoftPainCap <= FixedPoint2.Zero)
            return;

        var painLevel = FixedPoint2
            .Min(1f, _entityManager.System<PainSystem>().GetPain((local, pain)) / pain.SoftPainCap)
            .Float();

        // Pain alone only gets loud close to the end, so physical damage on the way to crit counts too, and the
        // curve is bent so the red shows from the first real injuries instead of the last ones.
        var level = painLevel;
        if (_entityManager.TryGetComponent(local, out DamageableComponent? damageable) &&
            _entityManager.System<MobThresholdSystem>().TryGetIncapThreshold(local, out var crit) &&
            crit.Value > FixedPoint2.Zero)
        {
            var hurt = damageable.DamagePerGroup.GetValueOrDefault("Brute") + damageable.DamagePerGroup.GetValueOrDefault("Burn");
            level = Math.Max(level, FixedPoint2.Min(1f, hurt / crit.Value).Float());
        }

        BruteLevel = level < 0.04f ? 0f : MathF.Pow(level, 0.6f);
    }
}
