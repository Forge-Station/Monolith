using Content.Shared.FixedPoint;

namespace Content.Shared._Onyx.Wounds;

// A fracture made outright rather than rolled from a hit: an execution breaks the skull every time.
public sealed partial class WoundFractureSystem
{
    /// <summary>
    /// Breaks the part's bone at this grade, or worsens the break it has up to it. Null where the part has no
    /// bones to break (slime, plant, chassis) or its profile has no such grade.
    /// </summary>
    public EntityUid? Break(Entity<WoundableComponent?> part, FractureGrade grade)
    {
        if (!_net.IsServer || grade == FractureGrade.None || !TryGetProfile(part, out var profile) ||
            !profile.Grades.TryGetValue(grade, out var settings) || settings.Threshold <= FixedPoint2.Zero)
            return null;

        if (GetFracture(part) is { } existing)
        {
            var missing = settings.Threshold - existing.Comp1.Severity;
            if (missing <= FixedPoint2.Zero)
                return existing.Owner;

            if (profile.ResetTreatmentOnDamage)
                ResetTreatment(existing);

            _wounds.ChangeSeverity(existing.Owner, missing);
            return existing.Owner;
        }

        if (_wounds.CreateOrMergeWound(part, profile.Wound, settings.Threshold) is not { } wound ||
            !TryComp(wound, out WoundComponent? core))
            return null;

        var component = EnsureComp<WoundFractureComponent>(wound);
        SetGrade((wound, core, component), GetGrade(profile, core.Severity));
        return wound;
    }
}
