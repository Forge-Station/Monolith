using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Systems;

namespace Content.Shared.HealthExaminable;

// WOLFGATE(Wolfmed): LOOK: unused with the readout above it; the same pain bands and the same
// health-examinable-pain-* keys live on in WolfmedVisualInspectionSystem's self lines.
public sealed partial class HealthExaminableSystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private PainSystem _pain = default!;

    private string? GetPainLevel(EntityUid part)
    {
        if (!TryComp(part, out PainComponent? pain))
            return null;

        var value = _pain.GetPain((part, pain));
        return value >= 50 ? "agony"
            : value >= 30 ? "terrible"
            : value >= 15 ? "strong"
            : value > 0 ? "light"
            : null;
    }
}
