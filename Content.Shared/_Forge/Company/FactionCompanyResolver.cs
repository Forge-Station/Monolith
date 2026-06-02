using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Company;

/// <summary>
/// Resolves which company a player should have based on their spawned job and saved profile.
/// Faction companies are only applied when the active job assigns one; civilian roles use unemployed.
/// </summary>
public static class FactionCompanyResolver
{
    public static readonly HashSet<string> FactionCompanyIds = new(StringComparer.Ordinal)
    {
        "TSF",
        "Imperial",
        "Renegates",
        "Colonial",
        "MD",
    };

    public static bool IsFactionCompany(string? companyId) =>
        !string.IsNullOrEmpty(companyId) && FactionCompanyIds.Contains(companyId);

    public static bool JobForcesCompany(JobPrototype job) =>
        job.AssignedCompany != default && job.AssignedCompany != "None";

    public static string? GetForcedCompanyForJob(JobPrototype job) =>
        JobForcesCompany(job) ? job.AssignedCompany.ToString() : null;

    /// <summary>
    /// Faction company implied by job priorities (lobby / saved profile).
    /// If several jobs share the top priority and any of them is civilian, no faction is forced —
    /// so you can keep a faction role at High alongside Contractor/Mercenary at High and stay unemployed.
    /// </summary>
    public static string? GetForcedCompanyFromProfile(
        HumanoidCharacterProfile profile,
        IPrototypeManager prototypes)
    {
        var bestPriority = JobPriority.Never;

        foreach (var (_, priority) in profile.JobPriorities)
        {
            if (priority == JobPriority.Never)
                continue;

            if (priority > bestPriority)
                bestPriority = priority;
        }

        if (bestPriority == JobPriority.Never)
            return null;

        var hasCivilianAtTop = false;
        string? forcedFaction = null;

        foreach (var (jobId, priority) in profile.JobPriorities)
        {
            if (priority != bestPriority)
                continue;

            if (!prototypes.TryIndex(jobId, out JobPrototype? job))
                continue;

            if (!JobForcesCompany(job))
            {
                hasCivilianAtTop = true;
                continue;
            }

            forcedFaction ??= GetForcedCompanyForJob(job);
        }

        if (hasCivilianAtTop)
            return null;

        return forcedFaction;
    }

    public static string ResolveSpawnCompany(JobPrototype job, string profileCompany)
    {
        if (JobForcesCompany(job))
            return job.AssignedCompany.ToString();

        if (IsFactionCompany(profileCompany))
            return "None";

        return string.IsNullOrEmpty(profileCompany) ? "None" : profileCompany;
    }

    public static string SanitizeProfileCompany(HumanoidCharacterProfile profile, IPrototypeManager prototypes)
    {
        var forced = GetForcedCompanyFromProfile(profile, prototypes);
        if (forced != null)
            return forced;

        return IsFactionCompany(profile.Company) ? "None" : profile.Company;
    }
}
