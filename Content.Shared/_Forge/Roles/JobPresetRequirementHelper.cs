using System.Diagnostics.CodeAnalysis;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.StationRecords;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Forge.Roles;

/// <summary>
/// Validates <see cref="JobPrototype"/> requirements against station-record / appearance demographics
/// for job preset consoles and reassignment tools.
/// </summary>
public static class JobPresetRequirementHelper
{
    public static Sex GenderToSex(Gender gender) => gender switch
    {
        Gender.Male => Sex.Male,
        Gender.Female => Sex.Female,
        _ => Sex.Unsexed,
    };

    public static HumanoidCharacterProfile ProfileFromStationRecord(GeneralStationRecord record)
    {
        return HumanoidCharacterProfile.DefaultWithSpecies(record.Species)
            .WithAge(record.Age)
            .WithSex(GenderToSex(record.Gender));
    }

    public static HumanoidCharacterProfile ProfileFromAppearance(
        ProtoId<SpeciesPrototype> species,
        int age,
        Sex sex)
    {
        return HumanoidCharacterProfile.DefaultWithSpecies(species)
            .WithAge(age)
            .WithSex(sex);
    }

    public static bool TryCheckJobRequirements(
        JobPrototype job,
        HumanoidCharacterProfile? profile,
        IEntityManager entManager,
        IPrototypeManager protoManager,
        IReadOnlyDictionary<string, TimeSpan> playTimes,
        [NotNullWhen(false)] out FormattedMessage? reason,
        bool enforcePlaytimeRequirements = false,
        bool ignoreDemographicRequirements = false)
    {
        var sys = entManager.System<SharedRoleSystem>();
        var requirements = sys.GetJobRequirement(job);
        reason = null;
        if (requirements == null)
            return true;

        var success = true;
        foreach (var requirement in requirements)
        {
            if (!enforcePlaytimeRequirements && requirement.IsPlaytimeRequirement)
                continue;

            if (ignoreDemographicRequirements && IsDemographicRequirement(requirement))
                continue;

            if (!requirement.Check(entManager, protoManager, profile, playTimes, out reason))
            {
                success = false;
                break;
            }
        }

        if (success)
            return true;

        var altRequirementsSets = job.AlternateRequirementSets ?? new();
        foreach (var requirementSet in altRequirementsSets.Values)
        {
            success = true;
            foreach (var requirement in requirementSet)
            {
                if (!enforcePlaytimeRequirements && requirement.IsPlaytimeRequirement)
                    continue;

                if (ignoreDemographicRequirements && IsDemographicRequirement(requirement))
                    continue;

                if (!requirement.Check(entManager, protoManager, profile, playTimes, out _))
                {
                    success = false;
                    break;
                }
            }

            if (success)
                return true;
        }

        if (reason == null)
            reason = FormattedMessage.FromMarkupPermissive(Loc.GetString("role-timer-no-reason-given"));

        return false;
    }

    private static bool IsDemographicRequirement(JobRequirement requirement) =>
        requirement is AgeRequirement or SpeciesRequirement or SexRequirement;

    public static string FormatReason(FormattedMessage reason)
    {
        return FormattedMessage.RemoveMarkupOrThrow(reason.ToMarkup());
    }
}
