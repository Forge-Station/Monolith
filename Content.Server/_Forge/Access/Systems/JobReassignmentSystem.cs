using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Forge.Access.Components;
using Content.Server._NF.CryoSleep;
using Content.Server.Access.Components;
using Content.Server.Access.Systems;
using Content.Server.Jobs;
using Content.Server.Mind;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Roles.Jobs;
using Content.Server.StationRecords.Systems;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Database;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Roles;
using Content.Shared.StationRecords;
using Content.Shared.StatusIcon;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Access.Systems;

public readonly record struct JobReassignmentData(
    JobPrototype Job,
    JobIconPrototype Icon,
    HashSet<ProtoId<AccessLevelPrototype>> AccessTags,
    HashSet<ProtoId<DepartmentPrototype>> Departments);

public sealed class JobReassignmentSystem : EntitySystem
{
    [Dependency] private readonly AccessSystem _access = default!;
    [Dependency] private readonly IdCardSystem _idCard = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playtime = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly JobSystem _jobs = default!;
    [Dependency] private readonly StationRecordsSystem _record = default!;
    [Dependency] private readonly SharedSubdermalImplantSystem _implant = default!;

    public bool TryResolveJobData(ProtoId<JobPrototype> jobId, out JobReassignmentData data)
    {
        data = default;

        if (!_prototype.TryIndex(jobId, out JobPrototype? job))
            return false;

        if (!_prototype.TryIndex(job.Icon, out JobIconPrototype? icon))
            return false;

        data = new JobReassignmentData(
            job,
            icon,
            ResolveJobAccess(job),
            ResolveJobDepartments(job));

        return true;
    }

    public HashSet<ProtoId<AccessLevelPrototype>> GetRequiredAuthorizedTags(
        IEnumerable<ProtoId<AccessLevelPrototype>> currentTags,
        IEnumerable<ProtoId<AccessLevelPrototype>> targetTags)
    {
        var requiredTags = currentTags.ToHashSet();
        requiredTags.UnionWith(targetTags);
        return requiredTags;
    }

    public bool HasRequiredAuthorizedTags(
        EntityUid targetId,
        JobReassignmentData data,
        HashSet<ProtoId<AccessLevelPrototype>> authorizedTags,
        out HashSet<ProtoId<AccessLevelPrototype>> requiredTags,
        AccessComponent? access = null)
    {
        requiredTags = new HashSet<ProtoId<AccessLevelPrototype>>();

        if (!Resolve(targetId, ref access, false))
            return false;

        requiredTags = GetRequiredAuthorizedTags(access.Tags, data.AccessTags);
        return requiredTags.IsSubsetOf(authorizedTags);
    }

    public bool IsIdCardStateMatching(
        EntityUid targetId,
        JobReassignmentData data,
        IdCardComponent? idCard = null,
        AccessComponent? access = null)
    {
        if (!Resolve(targetId, ref idCard, false)
            || !Resolve(targetId, ref access, false))
        {
            return false;
        }

        return idCard.JobPrototype == data.Job.ID
               && idCard.LocalizedJobTitle == data.Job.LocalizedName
               && idCard.JobIcon == data.Job.Icon
               && access.Tags.SetEquals(data.AccessTags)
               && idCard.JobDepartments.ToHashSet().SetEquals(data.Departments);
    }

    public bool TryApplyToIdCard(
        EntityUid targetId,
        ProtoId<JobPrototype> jobId,
        HashSet<ProtoId<AccessLevelPrototype>>? authorizedTags = null,
        EntityUid? actor = null,
        IdCardComponent? idCard = null,
        AccessComponent? access = null)
    {
        if (!TryResolveJobData(jobId, out var data))
            return false;

        return TryApplyToIdCard(targetId, data, authorizedTags, actor, idCard, access);
    }

    public bool TryApplyToIdCard(
        EntityUid targetId,
        JobReassignmentData data,
        HashSet<ProtoId<AccessLevelPrototype>>? authorizedTags = null,
        EntityUid? actor = null,
        IdCardComponent? idCard = null,
        AccessComponent? access = null)
    {
        if (!Resolve(targetId, ref idCard, false)
            || !Resolve(targetId, ref access, false))
        {
            return false;
        }

        if (authorizedTags != null
            && !HasRequiredAuthorizedTags(targetId, data, authorizedTags, out _, access))
            return false;

        _idCard.TryChangeJobTitle(targetId, data.Job.LocalizedName, idCard, actor);
        _idCard.TryChangeJobIcon(targetId, data.Icon, idCard, actor);
        _idCard.TryChangeJobDepartment(targetId, data.Job, idCard);
        _access.TrySetTags(targetId, data.AccessTags.ToList(), access);

        idCard.JobPrototype = data.Job.ID;
        Dirty(targetId, idCard);

        UpdateStationRecord(targetId, data.Job);
        return true;
    }

    public bool TryApplyToEntity(
        EntityUid target,
        ProtoId<JobPrototype> jobId,
        HashSet<ProtoId<AccessLevelPrototype>>? authorizedTags = null,
        EntityUid? actor = null,
        bool syncCurrentIdCard = true,
        IEnumerable<EntProtoId>? extraImplants = null)
    {
        if (!TryResolveJobData(jobId, out var data))
            return false;

        return TryApplyToEntity(target, data, authorizedTags, actor, syncCurrentIdCard, extraImplants);
    }

    public bool TryApplyToEntity(
        EntityUid target,
        JobReassignmentData data,
        HashSet<ProtoId<AccessLevelPrototype>>? authorizedTags = null,
        EntityUid? actor = null,
        bool syncCurrentIdCard = true,
        IEnumerable<EntProtoId>? extraImplants = null)
    {
        if (!_mind.TryGetMind(target, out var mindId, out var mind))
            return false;

        if (syncCurrentIdCard
            && _idCard.TryFindIdCard(target, out var foundId)
            && !HasComp<AgentIDCardComponent>(foundId.Owner))
        {
            // The body job change is the primary action. Updating the currently carried ID is best-effort.
            TryApplyToIdCard(foundId.Owner, data, authorizedTags, actor, foundId.Comp);
        }

        _jobs.MindAddJob(mindId, data.Job.ID);

        if (_mind.TryGetSession(mind, out var session))
            _playtime.QueueRefreshTrackers(session);

        var playerJob = EnsureComp<PlayerJobComponent>(target);
        if (playerJob.JobPrototype != data.Job.ID)
        {
            playerJob.JobPrototype = data.Job.ID;
            Dirty(target, playerJob);
        }

        EnsureJobImplants(target, data.Job, extraImplants);

        return true;
    }

    private HashSet<ProtoId<AccessLevelPrototype>> ResolveJobAccess(JobPrototype job)
    {
        var tags = job.Access.ToHashSet();

        foreach (var accessGroupId in job.AccessGroups)
        {
            if (!_prototype.TryIndex(accessGroupId, out AccessGroupPrototype? group))
                continue;

            tags.UnionWith(group.Tags);
        }

        return tags;
    }

    private HashSet<ProtoId<DepartmentPrototype>> ResolveJobDepartments(JobPrototype job)
    {
        var departments = new HashSet<ProtoId<DepartmentPrototype>>();

        foreach (var department in _prototype.EnumeratePrototypes<DepartmentPrototype>())
        {
            if (department.Roles.Contains(job.ID))
                departments.Add(department.ID);
        }

        return departments;
    }

    private void UpdateStationRecord(EntityUid targetId, JobPrototype job)
    {
        if (!TryComp<StationRecordKeyStorageComponent>(targetId, out var keyStorage)
            || keyStorage.Key is not { } key
            || !_record.TryGetRecord<GeneralStationRecord>(key, out var record))
        {
            return;
        }

        record.JobTitle = job.LocalizedName;
        record.JobPrototype = job.ID;
        record.JobIcon = job.Icon;
        record.DisplayPriority = job.RealDisplayWeight;

        _record.Synchronize(key);
    }

    private void EnsureJobImplants(EntityUid target, JobPrototype job, IEnumerable<EntProtoId>? extraImplants)
    {
        var requiredImplants = job.Special
            .OfType<AddImplantSpecial>()
            .SelectMany(special => special.Implants)
            .Where(implantId => !string.IsNullOrWhiteSpace(implantId))
            .ToHashSet(StringComparer.Ordinal);

        if (extraImplants != null)
        {
            foreach (var implantId in extraImplants)
            {
                var implantIdString = implantId.ToString();
                if (!string.IsNullOrWhiteSpace(implantIdString))
                    requiredImplants.Add(implantIdString);
            }
        }

        var existingImplants = new HashSet<string>(StringComparer.Ordinal);
        var implantsToRemove = new List<EntityUid>();

        if (TryComp<ImplantedComponent>(target, out var implanted))
        {
            foreach (var implant in implanted.ImplantContainer.ContainedEntities)
            {
                if (MetaData(implant).EntityPrototype?.ID is not { } implantId)
                    continue;

                existingImplants.Add(implantId);

                if (HasComp<JobReassignmentManagedImplantComponent>(implant)
                    && !requiredImplants.Contains(implantId))
                {
                    implantsToRemove.Add(implant);
                }
            }
        }

        foreach (var implant in implantsToRemove)
        {
            _implant.ForceRemove(target, implant);
        }

        if (requiredImplants.Count == 0)
            return;

        foreach (var implantId in requiredImplants)
        {
            if (existingImplants.Contains(implantId))
                continue;

            if (_implant.AddImplant(target, implantId) is { } implant)
                EnsureComp<JobReassignmentManagedImplantComponent>(implant);
        }
    }
}
