using System.Linq;
using Content.Server._Forge.Access.Systems;
using Content.Server.Hands.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Roles;
using Content.Shared.StationRecords;
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
    private const string InjectorPrototypeId = "JobReassignmentInjector";

    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly JobReassignmentSystem _reassignment = default!;
    [Dependency] private readonly StationRecordsSystem _record = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JobPresetIdCardConsoleComponent, JobPresetIdCardConsoleApplyMessage>(OnApplyPresetMessage);
        SubscribeLocalEvent<JobPresetIdCardConsoleComponent, JobPresetIdCardConsoleCreateInjectorMessage>(OnCreateInjectorMessage);
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

    private void OnCreateInjectorMessage(
        EntityUid uid,
        JobPresetIdCardConsoleComponent component,
        JobPresetIdCardConsoleCreateInjectorMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        TryCreateInjector(uid, args.JobPrototype, player, component);
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

        if (!TryResolvePreset(component, newJobPrototype, out var jobData))
        {
            return;
        }

        var currentTags = targetAccess.Tags.ToHashSet();
        var privilegedAccess = _accessReader.FindAccessTags(privilegedId).ToHashSet();
        if (!_reassignment.HasRequiredAuthorizedTags(targetId, jobData, privilegedAccess, out _, targetAccess))
        {
            Log.Warning(
                $"User {ToPrettyString(player)} tried to apply preset '{newJobPrototype}' without covering all required accesses on {ToPrettyString(uid)}.");
            return;
        }

        if (_reassignment.IsIdCardStateMatching(targetId, jobData, targetCard, targetAccess))
        {
            return;
        }

        if (!_reassignment.TryApplyToIdCard(targetId, jobData, privilegedAccess, player, targetCard, targetAccess))
            return;

        var changedAccess = jobData.AccessTags
            .Union(currentTags)
            .Except(jobData.AccessTags.Intersect(currentTags))
            .OrderBy(tag => tag)
            .ToList();

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):player} has applied job preset {newJobPrototype} to {ToPrettyString(targetId):entity} with accesses [{string.Join(", ", jobData.AccessTags.OrderBy(tag => tag))}] and changed entries [{string.Join(", ", changedAccess)}]");
    }

    private void TryCreateInjector(
        EntityUid uid,
        ProtoId<JobPrototype> newJobPrototype,
        EntityUid player,
        JobPresetIdCardConsoleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (!TryResolveConsoleCard(component.TargetIdSlot.Item, out var targetId, out _, out var targetAccess)
            || !TryResolveConsoleCard(component.PrivilegedIdSlot.Item, out var privilegedId, out _, out _)
            || !PrivilegedIdIsAuthorized(uid, component))
        {
            return;
        }

        if (!TryResolvePreset(component, newJobPrototype, out var jobData))
            return;

        var privilegedAccess = _accessReader.FindAccessTags(privilegedId).ToHashSet();
        if (!_reassignment.HasRequiredAuthorizedTags(targetId, jobData, privilegedAccess, out var requiredAccess, targetAccess))
        {
            Log.Warning(
                $"User {ToPrettyString(player)} tried to create an injector for '{newJobPrototype}' without covering all required accesses on {ToPrettyString(uid)}.");
            return;
        }

        var injector = Spawn(InjectorPrototypeId, Transform(uid).Coordinates);
        if (!TryComp<JobReassignmentInjectorComponent>(injector, out var injectorComp))
        {
            QueueDel(injector);
            Log.Error($"Spawned injector '{InjectorPrototypeId}' without a {nameof(JobReassignmentInjectorComponent)}.");
            return;
        }

        injectorComp.JobPrototype = jobData.Job.ID;
        injectorComp.AuthorizedAccess = requiredAccess.OrderBy(tag => tag).ToList();
        injectorComp.BodyImplants = component.BodyImplants.ToList();

        _metaData.SetEntityName(injector,
            Loc.GetString("job-preset-id-card-console-injector-name", ("job", jobData.Job.LocalizedName)));
        _metaData.SetEntityDescription(injector,
            Loc.GetString("job-preset-id-card-console-injector-desc", ("job", jobData.Job.LocalizedName)));
        _hands.PickupOrDrop(player, injector, checkActionBlocker: false);

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):player} has created a reassignment injector for {newJobPrototype} with accesses [{string.Join(", ", requiredAccess.OrderBy(tag => tag))}]");
    }

    private bool TryResolvePreset(
        JobPresetIdCardConsoleComponent component,
        ProtoId<JobPrototype> jobPrototype,
        out JobReassignmentData data)
    {
        data = default;

        if (!component.RankPresets.Contains(jobPrototype))
        {
            Log.Warning($"Tried to apply job preset '{jobPrototype}' that is not present on the console.");
            return false;
        }

        if (!_reassignment.TryResolveJobData(jobPrototype, out data))
        {
            Log.Warning($"Invalid job preset '{jobPrototype}' on preset ID card console.");
            return false;
        }

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
