// WOLFGATE (AUTODOC): the analyzer's own scan payload, built on demand instead of sent down its own UI key,
// so the autodoc window can show the same doll and the same wound cards without a second payload type.

using Content.Server.Body.Components;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.MedicalScanner;
using Content.Shared.Medical;
using Content.Server.Temperature.Components;

namespace Content.Server.Medical;

public sealed partial class HealthAnalyzerSystem
{
    /// <summary>
    /// The message <see cref="UpdateScannedUser"/> would send for this body. Same builders, same fields; the
    /// only difference is that the caller decides where it goes.
    /// </summary>
    public HealthAnalyzerScannedUserMessage? WolfmedBuildScanMessage(EntityUid target)
    {
        if (!HasComp<DamageableComponent>(target))
            return null;

        var temperature = TryComp(target, out TemperatureComponent? temp) ? temp.CurrentTemperature : float.NaN;
        var blood = float.NaN;
        var bleeding = false;

        if (TryComp(target, out BloodstreamComponent? bloodstream) &&
            _solutionContainerSystem.ResolveSolution(target, bloodstream.BloodSolutionName,
                ref bloodstream.BloodSolution, out var bloodSolution))
        {
            blood = bloodSolution.FillFraction;
            bleeding = bloodstream.BleedAmount > 0;
        }

        Dictionary<TargetBodyPart, TargetIntegrity>? body = null;
        if (HasComp<BodyComponent>(target))
            body = _bodySystem.GetBodyPartStatus(target);

        return new HealthAnalyzerScannedUserMessage(
            GetNetEntity(target),
            temperature,
            blood,
            true,
            bleeding,
            false,
            false,
            body,
            null,
            BuildWoundDiagnostics(target),
            BuildOrganInfo(target),
            BuildChemicalInfo(target, bloodstream),
            BuildVitalDamage(target));
    }
}
