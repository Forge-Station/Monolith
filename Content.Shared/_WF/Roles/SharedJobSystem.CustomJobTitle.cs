using System.Diagnostics.CodeAnalysis;
using Content.Shared._WF.Roles;

namespace Content.Shared.Roles.Jobs;

/// <summary>Custom job titles in mind job lookups.</summary>
public abstract partial class SharedJobSystem
{
    /// <summary>The mind's custom title, if it has one for this job.</summary>
    private bool TryGetCustomJobTitle(EntityUid mindId, JobPrototype job, [NotNullWhen(true)] out string? title)
    {
        title = null;
        if (!TryComp<CustomJobTitleComponent>(mindId, out var custom) || custom.Job != job.ID)
            return false;

        title = custom.Title;
        return true;
    }

    /// <summary>Job name for admin tools, e.g. "Vagrant (Bounty Hunter)", so the real job stays visible.</summary>
    public string MindGetAdminJobName(EntityUid? mindId)
    {
        if (!MindTryGetJob(mindId, out var job))
            return Loc.GetString("generic-unknown-title");

        return TryGetCustomJobTitle(mindId.Value, job, out var title)
            ? Loc.GetString("custom-job-title-join-name", ("job", job.LocalizedName), ("title", title))
            : job.LocalizedName;
    }
}
