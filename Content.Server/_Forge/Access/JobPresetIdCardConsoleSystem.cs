using System.Linq;
using Content.Server.Access.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Roles;
using Content.Shared.StationRecords;
using Content.Shared.StatusIcon;
using Content.Shared._Forge.Access;
using Content.Shared._Forge.Access.Components;
using Content.Shared._Forge.Access.Systems;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Access;

[UsedImplicitly]
public sealed class JobPresetIdCardConsoleSystem : SharedJobPresetIdCardConsoleSystem
{
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly AccessSystem _access = default!;
    [Dependency] private readonly IdCardSystem _idCard = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly StationRecordsSystem _record = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JobPresetIdCardConsoleComponent, JobPresetIdCardConsoleApplyMessage>(OnApplyPresetMessage);
        SubscribeLocalEvent<JobPresetIdCardConsoleComponent, ComponentStartup>(UpdateUserInterface);
        SubscribeLocalEvent<JobPresetIdCardConsoleComponent, EntInsertedIntoContainerMessage>(UpdateUserInterface);
        SubscribeLocalEvent<JobPresetIdCardConsoleComponent, EntRemovedFromContainerMessage>(UpdateUserInterface);
    }

    private void OnApplyPresetMessage(
        EntityUid uid,
        JobPresetIdCardConsoleComponent component,
        JobPresetIdCardConsoleApplyMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        TryApplyPreset(uid, args.JobPrototype, player, component);
        UpdateUserInterface(uid, component, args);
    }

    private void UpdateUserInterface(
        EntityUid uid,
        JobPresetIdCardConsoleComponent component,
        EntityEventArgs args)
    {
        if (!component.Initialized)
            return;

        var privilegedIdName = string.Empty;
        List<ProtoId<AccessLevelPrototype>>? allowedModifyAccessList = null;
        var isPrivilegedIdAuthorized = PrivilegedIdIsAuthorized(uid, component);

        if (TryResolveConsoleCard(component.PrivilegedIdSlot.Item, out var privilegedId, out _, out _))
        {
            privilegedIdName = Name(privilegedId);
            allowedModifyAccessList = _accessReader.FindAccessTags(privilegedId).ToList();
        }

        JobPresetIdCardConsoleBoundUserInterfaceState state;
        if (!TryResolveConsoleCard(component.TargetIdSlot.Item, out var targetId, out var targetCard, out var targetAccess))
        {
            state = new JobPresetIdCardConsoleBoundUserInterfaceState(
                component.PrivilegedIdSlot.HasItem,
                isPrivilegedIdAuthorized,
                false,
                null,
                allowedModifyAccessList,
                string.Empty,
                privilegedIdName,
                string.Empty);
        }
        else
        {
            state = new JobPresetIdCardConsoleBoundUserInterfaceState(
                component.PrivilegedIdSlot.HasItem,
                isPrivilegedIdAuthorized,
                true,
                targetAccess.Tags.ToList(),
                allowedModifyAccessList,
                GetCardJobPrototype(targetId, targetCard),
                privilegedIdName,
                Name(targetId));
        }

        _userInterface.SetUiState(uid, JobPresetIdCardConsoleUiKey.Key, state);
    }

    private void TryApplyPreset(
        EntityUid uid,
        ProtoId<JobPrototype> newJobPrototype,
        EntityUid player,
        JobPresetIdCardConsoleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (!TryResolveConsoleCard(component.TargetIdSlot.Item, out var targetId, out var targetCard, out var targetAccess)
            || !TryResolveConsoleCard(component.PrivilegedIdSlot.Item, out var privilegedId, out _, out _)
            || !PrivilegedIdIsAuthorized(uid, component))
        {
            return;
        }

        if (!TryResolvePreset(component, newJobPrototype, out var job, out var newTags, out var jobDepartments, out var jobIcon))
        {
            return;
        }

        var currentTags = targetAccess.Tags.ToHashSet();
        var requiredPrivileges = ResolveRequiredPrivileges(currentTags, newTags);
        var privilegedAccess = _accessReader.FindAccessTags(privilegedId).ToHashSet();
        if (!requiredPrivileges.IsSubsetOf(privilegedAccess))
        {
            Log.Warning(
                $"User {ToPrettyString(player)} tried to apply preset '{newJobPrototype}' without covering all required accesses on {ToPrettyString(uid)}.");
            return;
        }

        if (IsPresetAlreadyApplied(targetCard, targetAccess, job, newTags, jobDepartments))
        {
            return;
        }

        _idCard.TryChangeJobTitle(targetId, job.LocalizedName, targetCard, player);
        _idCard.TryChangeJobIcon(targetId, jobIcon, targetCard, player);
        _idCard.TryChangeJobDepartment(targetId, job, targetCard);
        _access.TrySetTags(targetId, newTags, targetAccess);

        targetCard.JobPrototype = job.ID;
        Dirty(targetId, targetCard);

        UpdateStationRecord(targetId, job);

        var changedAccess = newTags
            .Union(currentTags)
            .Except(newTags.Intersect(currentTags))
            .OrderBy(tag => tag)
            .ToList();

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):player} has applied job preset {newJobPrototype} to {ToPrettyString(targetId):entity} with accesses [{string.Join(", ", newTags.OrderBy(tag => tag))}] and changed entries [{string.Join(", ", changedAccess)}]");
    }

    private bool TryResolvePreset(
        JobPresetIdCardConsoleComponent component,
        ProtoId<JobPrototype> jobPrototype,
        out JobPrototype job,
        out HashSet<ProtoId<AccessLevelPrototype>> accessTags,
        out HashSet<ProtoId<DepartmentPrototype>> departments,
        out JobIconPrototype jobIcon)
    {
        accessTags = new HashSet<ProtoId<AccessLevelPrototype>>();
        departments = new HashSet<ProtoId<DepartmentPrototype>>();
        job = default!;
        jobIcon = default!;

        if (!component.RankPresets.Contains(jobPrototype))
        {
            Log.Warning($"Tried to apply job preset '{jobPrototype}' that is not present on the console.");
            return false;
        }

        if (!_prototype.TryIndex(jobPrototype, out JobPrototype? resolvedJob))
        {
            Log.Warning($"Invalid job preset '{jobPrototype}' on preset ID card console.");
            return false;
        }

        job = resolvedJob;

        if (!_prototype.TryIndex(job.Icon, out JobIconPrototype? resolvedJobIcon))
        {
            Log.Warning($"Invalid job icon '{job.Icon}' on job preset '{jobPrototype}'.");
            return false;
        }

        jobIcon = resolvedJobIcon;
        accessTags = ResolveJobAccess(job);
        departments = ResolveJobDepartments(job);
        return true;
    }

    private bool TryResolveConsoleCard(
        EntityUid? uid,
        out EntityUid cardUid,
        out IdCardComponent idCard,
        out AccessComponent access)
    {
        cardUid = EntityUid.Invalid;
        idCard = default!;
        access = default!;

        if (uid is not { Valid: true } card
            || !TryComp(card, out IdCardComponent? resolvedIdCard)
            || !TryComp(card, out AccessComponent? resolvedAccess))
        {
            return false;
        }

        cardUid = card;
        idCard = resolvedIdCard;
        access = resolvedAccess;
        return true;
    }

    private ProtoId<JobPrototype> GetCardJobPrototype(EntityUid targetId, IdCardComponent? idCard = null)
    {
        if (Resolve(targetId, ref idCard, false)
            && idCard.JobPrototype is { } cardJobPrototype
            && !string.IsNullOrEmpty(cardJobPrototype))
        {
            return cardJobPrototype;
        }

        if (TryComp<StationRecordKeyStorageComponent>(targetId, out var keyStorage)
            && keyStorage.Key is { } key
            && _record.TryGetRecord<GeneralStationRecord>(key, out var record)
            && !string.IsNullOrEmpty(record.JobPrototype))
        {
            return record.JobPrototype;
        }

        return string.Empty;
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

    private HashSet<ProtoId<AccessLevelPrototype>> ResolveRequiredPrivileges(
        HashSet<ProtoId<AccessLevelPrototype>> currentTags,
        HashSet<ProtoId<AccessLevelPrototype>> newTags)
    {
        var required = currentTags.ToHashSet();
        required.UnionWith(newTags);
        return required;
    }

    private bool IsPresetAlreadyApplied(
        IdCardComponent targetCard,
        AccessComponent targetAccess,
        JobPrototype job,
        HashSet<ProtoId<AccessLevelPrototype>> newTags,
        HashSet<ProtoId<DepartmentPrototype>> jobDepartments)
    {
        return targetCard.JobPrototype == job.ID
               && targetCard.LocalizedJobTitle == job.LocalizedName
               && targetCard.JobIcon == job.Icon
               && targetAccess.Tags.SetEquals(newTags)
               && targetCard.JobDepartments.ToHashSet().SetEquals(jobDepartments);
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

    private bool PrivilegedIdIsAuthorized(EntityUid uid, JobPresetIdCardConsoleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return true;

        if (!TryComp<AccessReaderComponent>(uid, out var reader))
            return true;

        var privilegedId = component.PrivilegedIdSlot.Item;
        return privilegedId != null && _accessReader.IsAllowed(privilegedId.Value, uid, reader);
    }
}
