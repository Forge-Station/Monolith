using Content.Shared._WF.WolfChem.Narcotics;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Wolfmed.Overlays;

public sealed partial class WolfmedSyntheticHudOverlaySystem
{
    [Dependency] private IPrototypeManager _wfPrototypes = default!;

    /// <summary>Intensity at which a program tears the screen like heavy damage does.</summary>
    private const float WFNeurowareGlitchAt = 0.6f;

    /// <summary>How hard running neuroware drives the readout, 0-1: the strongest active tier, or half a crash.</summary>
    private float WFNeurowareIntensity(EntityUid local)
    {
        if (!TryComp<WFIntoxicationComponent>(local, out var intox))
            return 0f;

        var intensity = 0f;
        foreach (var (profile, level) in intox.Levels)
        {
            if (!_wfPrototypes.TryIndex(profile, out var proto))
                continue;

            var tier = proto.TierAt(level);
            if (tier >= 0)
                intensity = MathF.Max(intensity, MathF.Max(proto.Tiers[tier].Overlay, 0.25f));
        }

        foreach (var profile in intox.Crashes.Keys)
        {
            if (_wfPrototypes.TryIndex(profile, out var proto) && proto.Crash != null)
                intensity = MathF.Max(intensity, proto.Crash.Overlay * 0.5f);
        }

        return Math.Clamp(intensity, 0f, 1f);
    }
}
