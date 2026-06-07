// Forge-Change
using System.Linq;
using System.Numerics;
using Content.Server.Access.Systems;
using Content.Server.Body.Components;
using Content.Server.CrewManifest;
using Content.Server.Medical.SuitSensors;
using Content.Server.DeviceLinking.Components;
using Content.Server.Medical.CrewMonitoring;
using Content.Server.Pinpointer;
using Content.Shared.CrewManifest;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.SecApartment;
using Content.Shared.Security.Components;
using Content.Server.Station.Systems;
using Content.Shared.Station.Components;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.SecApartment;

public sealed partial class SecApartmentSystem : EntitySystem
{
    private static readonly HashSet<string> DebugAssignableOffStationJobs = new()
    {
        "Mercenary",
        "Pilot",
        "Contractor",
        "Adventurer"
    };

    private static readonly HashSet<string> DebugSquadDepartments = new()
    {
        "TSF",
        "NanoTrasen",
        "Empire",
        "UnionOfSovietSocialistPlanets",
        "Renegates"
    };

    private const int MaxSquadNameLength = 16;
    private const int MaxSquadDescriptionLength = 512;

    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly CrewManifestSystem _crewManifest = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly NavMapSystem _navMap = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IdCardSystem _idCard = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    private readonly Dictionary<EntityUid, StationData> _stationData = new();
    private readonly Dictionary<NetEntity, TimeSpan> _finishedTimers = new();

    private readonly Dictionary<string, HashSet<string>> _departmentJobs = new();
    private TimeSpan _lastSensorUpdate = TimeSpan.Zero;
    private TimeSpan _lastTimerUpdate = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();

        InitializeDepartmentJobs();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypeReload);

        SubscribeLocalEvent<SecApartmentComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SecApartmentComponent, ActivatableUIOpenAttemptEvent>(OnUIOpenAttempt);
        SubscribeLocalEvent<SecApartmentComponent, BoundUIOpenedEvent>(OnUIOpened);

        SubscribeLocalEvent<SecApartmentComponent, CreateSquadMessage>(OnCreateSquad);
        SubscribeLocalEvent<SecApartmentComponent, DeleteSquadMessage>(OnDeleteSquad);
        SubscribeLocalEvent<SecApartmentComponent, RenameSquadMessage>(OnRenameSquad);
        SubscribeLocalEvent<SecApartmentComponent, ChangeSquadIconMessage>(OnChangeSquadIcon);
        SubscribeLocalEvent<SecApartmentComponent, UpdateSquadDescriptionMessage>(OnUpdateSquadDescription);
        SubscribeLocalEvent<SecApartmentComponent, AddMemberToSquadMessage>(OnAddMemberToSquad);
        SubscribeLocalEvent<SecApartmentComponent, RemoveMemberFromSquadMessage>(OnRemoveMemberFromSquad);
        SubscribeLocalEvent<SecApartmentComponent, ChangeSquadStatusMessage>(OnChangeSquadStatus);
        SubscribeLocalEvent<SecApartmentComponent, RemoveTimerMessage>(OnRemoveTimer);
        SubscribeLocalEvent<SecApartmentComponent, RefreshSecApartmentMessage>(OnRefreshUi);

        SubscribeLocalEvent<SignalTimerComponent, AfterInteractUsingEvent>(OnLinkTimer);
        SubscribeLocalEvent<ActiveSignalTimerComponent, ComponentStartup>(OnLinkedTimerStarted);
        SubscribeLocalEvent<SignalTimerComponent, ComponentShutdown>(OnTimerComponentShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = _gameTiming.CurTime;
        if (currentTime - _lastSensorUpdate >= TimeSpan.FromSeconds(5))
        {
            _lastSensorUpdate = currentTime;

            var query = EntityQueryEnumerator<SecApartmentComponent>();
            while (query.MoveNext(out var uid, out var comp))
                UpdateSensorStatuses(uid, comp);
        }

        if (currentTime - _lastTimerUpdate >= TimeSpan.FromSeconds(1))
        {
            _lastTimerUpdate = currentTime;
            UpdateAllTimerStates();
        }
    }

    private void InitializeDepartmentJobs()
    {
        _departmentJobs.Clear();
        foreach (var department in _prototype.EnumeratePrototypes<DepartmentPrototype>())
        {
            var roles = new HashSet<string>();
            foreach (var role in department.Roles)
                roles.Add(role);

            _departmentJobs[department.ID] = roles;
        }
    }

    private bool IsJobInDepartment(string departmentId, string jobId)
    {
        return _departmentJobs.TryGetValue(departmentId, out var jobs) && jobs.Contains(jobId);
    }

    private bool IsJobVisibleForTablet(SecApartmentComponent comp, string jobId)
    {
        return _prototype.TryIndex<JobPrototype>(jobId, out var job) && IsJobVisibleForTablet(comp, job);
    }

    private bool IsJobVisibleForTablet(SecApartmentComponent comp, JobPrototype job)
    {
        if (comp.VisibleCompanies.Count > 0)
            return comp.VisibleCompanies.Contains(job.AssignedCompany);

        return IsJobInDepartment(comp.Department, job.ID);
    }

    private static bool IsDebugAssignableOffStationJob(string jobId)
    {
        return DebugAssignableOffStationJobs.Contains(jobId);
    }

    private string GenerateSquadId(StationData stationData)
    {
        string squadId;
        do
        {
            squadId = $"squad_{_random.Next(1000, 9999)}";
        } while (stationData.Squads.Any(squad => squad.SquadId == squadId));

        return squadId;
    }

    private static string SanitizeUiText(string text, int maxLength)
    {
        var sanitized = FormattedMessage.RemoveMarkupPermissive(text).Trim();
        return sanitized.Length > maxLength
            ? sanitized[..maxLength]
            : sanitized;
    }

    private static string GetSquadIconPrefix(string department, string fallback)
    {
        return department switch
        {
            "TSF" => "TSFSquadIcon",
            "Empire" => "EmpireSquadIcon",
            "UnionOfSovietSocialistPlanets" => "USSPSquadIcon",
            "Renegates" => "RenegateSquadIcon",
            _ => fallback
        };
    }

    private void OnPrototypeReload(PrototypesReloadedEventArgs obj)
    {
        if (obj.WasModified<JobPrototype>())
            InitializeDepartmentJobs();

        if (obj.WasModified<DepartmentPrototype>())
            InitializeDepartmentJobs();
    }

    private void UpdateSensorStatuses(EntityUid uid, SecApartmentComponent comp)
    {
        if (comp.Station == null)
            return;

        var securityCrew = GetAssignableCrew(uid, comp, comp.Station.Value);
        var statusDict = new Dictionary<string, SuitSensorStatus?>();
        var squadLocations = new Dictionary<string, (string Location, bool HasLocation)>();

        var squads = _stationData.TryGetValue(comp.Station.Value, out var stationData)
            ? GetVisibleSquads(stationData, comp)
            : new List<Squad>();

        foreach (var squad in squads)
        {
            UpdateAndCollectSquadData(uid, squad, securityCrew, statusDict);

            var location = GetSquadApproximateLocation(squad, securityCrew);
            squadLocations[squad.SquadId] = location;
        }

        if (!_ui.IsUiOpen(uid, SecApartmentUiKey.Key))
            return;

        var statusUpdate = new SensorStatusUpdateState(statusDict, squadLocations);
        _ui.ServerSendUiMessage(uid, SecApartmentUiKey.Key, statusUpdate);
    }

    private void UpdateAndCollectSquadData(EntityUid tablet, Squad squad, List<CrewMemberInfo> securityCrew,
        Dictionary<string, SuitSensorStatus?> statusDict)
    {
        var iconId = SecApartmentIcons.GetPrototypeId(squad.IconId, squad.SquadIconPrefix);

        for (var i = 0; i < squad.Members.Count; i++)
        {
            var squadMember = squad.Members[i];
            var currentMember = FindCrewMember(squadMember, securityCrew);
            if (currentMember == null)
            {
                currentMember = RefreshStoredMemberFromEntity(tablet, squadMember);
                if (currentMember == null)
                    continue;
            }

            currentMember = NormalizeMemberFromOwner(tablet, currentMember);

            if (squadMember.OwnerUid != currentMember.OwnerUid)
            {
                if (squadMember.OwnerUid != null)
                {
                    var oldEntityUid = GetEntity(squadMember.OwnerUid.Value);
                    if (Exists(oldEntityUid) && !Terminating(oldEntityUid))
                        RemComp<SquadMemberComponent>(oldEntityUid);
                }
            }

            squad.Members[i] = currentMember;
            statusDict[currentMember.MemberId] = currentMember.SensorStatus;

            if (currentMember.OwnerUid == null)
                continue;

            var entityUid = GetEntity(currentMember.OwnerUid.Value);
            if (!Exists(entityUid) || Terminating(entityUid))
                continue;

            EnsureComp<SquadMemberComponent>(entityUid, out var comp);
            if (comp.StatusIcon == iconId)
                continue;

            comp.StatusIcon = iconId;
            Dirty(entityUid, comp);
        }
    }

    private CrewMemberInfo? FindCrewMember(CrewMemberInfo squadMember, List<CrewMemberInfo> crew)
    {
        var exactMatch = crew.FirstOrDefault(c => c.MemberId == squadMember.MemberId)
            ?? (squadMember.OwnerUid != null
                ? crew.FirstOrDefault(c => c.OwnerUid == squadMember.OwnerUid || IsSameCrewMember(c, squadMember))
                : null);
        if (exactMatch != null)
            return exactMatch;

        if (squadMember.OwnerUid == null || !MemberOwnerExists(squadMember))
        {
            var activeMatch = crew.FirstOrDefault(c =>
                c.Name == squadMember.Name &&
                c.JobTitle == squadMember.JobTitle &&
                IsCrewMemberAlive(c));
            if (activeMatch != null)
                return activeMatch;
        }

        return squadMember.OwnerUid == null
            ? crew.FirstOrDefault(c => c.Name == squadMember.Name && c.JobTitle == squadMember.JobTitle)
            : null;
    }

    private bool MemberOwnerExists(CrewMemberInfo member)
    {
        if (member.OwnerUid == null)
            return false;

        var entity = GetEntity(member.OwnerUid.Value);
        return Exists(entity) && !Terminating(entity);
    }

    private bool IsSameCrewMember(CrewMemberInfo first, CrewMemberInfo second)
    {
        if (first.MemberId == second.MemberId)
            return true;

        if (first.OwnerUid != null && first.OwnerUid == second.OwnerUid)
            return true;

        return TryGetMemberMind(first, out var firstMind) &&
               TryGetMemberMind(second, out var secondMind) &&
               firstMind == secondMind ||
               IsSameDeadCrewRecord(first, second);
    }

    private bool IsSameDeadCrewRecord(CrewMemberInfo first, CrewMemberInfo second)
    {
        if (first.OwnerUid != null &&
            second.OwnerUid != null &&
            first.OwnerUid != second.OwnerUid)
        {
            return false;
        }

        return first.Name == second.Name &&
               first.JobTitle == second.JobTitle &&
               !IsCrewMemberAlive(first) &&
               !IsCrewMemberAlive(second);
    }

    private bool IsCrewMemberAlive(CrewMemberInfo member)
    {
        if (member.SensorStatus?.IsAlive == true)
            return true;

        if (member.OwnerUid == null)
            return false;

        var entity = GetEntity(member.OwnerUid.Value);
        if (!Exists(entity) || Terminating(entity))
            return false;

        return !IsDeadForSecApartment(entity);
    }

    private bool TryGetMemberMind(CrewMemberInfo member, out NetEntity mindNet)
    {
        mindNet = default;

        if (member.OwnerUid == null)
            return false;

        var entity = GetEntity(member.OwnerUid.Value);
        if (!Exists(entity) ||
            Terminating(entity) ||
            !TryComp<MindContainerComponent>(entity, out var mindContainer) ||
            mindContainer.Mind == null)
        {
            return false;
        }

        mindNet = GetNetEntity(mindContainer.Mind.Value);
        return true;
    }

    private static List<Squad> GetVisibleSquads(StationData stationData, SecApartmentComponent tablet)
    {
        if (tablet.Debug)
            return stationData.Squads;

        return stationData.Squads
            .Where(squad => squad.Department == tablet.Department)
            .ToList();
    }

    private void OnMapInit(Entity<SecApartmentComponent> ent, ref MapInitEvent args)
    {
        var station = _station.GetStationInMap(Transform(ent).MapID);
        ent.Comp.Station = station;
        Dirty(ent.Owner, ent.Comp);
    }

    private void OnUIOpenAttempt(Entity<SecApartmentComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (ent.Comp.Station == null)
        {
            args.Cancel();
            return;
        }

        if (CanUseTablet(args.User, ent.Owner, ent.Comp))
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("lock-comp-has-user-access-fail"), ent.Owner, args.User);
    }

    private bool CanUseTablet(EntityUid user, EntityUid tablet, SecApartmentComponent comp)
    {
        if (!Exists(user) || Terminating(user) || !Exists(tablet) || Terminating(tablet))
            return false;

        if (!UserHasTabletInActiveHand(user, tablet))
            return false;

        if (comp.RequireUserDepartment && !UserCanAccessTablet(user, comp))
            return false;

        return _access.IsAllowed(user, tablet);
    }

    private bool UserHasTabletInActiveHand(EntityUid user, EntityUid tablet)
    {
        return TryComp<HandsComponent>(user, out var hands) &&
               _hands.TryGetActiveItem((user, hands), out var activeItem) &&
               activeItem == tablet;
    }

    private bool UserCanAccessTablet(EntityUid user, SecApartmentComponent comp)
    {
        if (!TryComp<MindContainerComponent>(user, out var mind) || !mind.HasMind)
            return false;

        return _jobs.MindTryGetJob(mind.Mind, out var job) && IsJobVisibleForTablet(comp, job);
    }

    private void OnUIOpened(Entity<SecApartmentComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent.Owner, ent.Comp, args.Actor);
        UpdateTimerStateForTablet(ent.Owner);
    }

    #region UI Message Handlers

    private void OnRefreshUi(EntityUid uid, SecApartmentComponent component, RefreshSecApartmentMessage msg)
    {
        if (!CanUseTablet(msg.Actor, uid, component))
            return;

        UpdateUi(uid, component, msg.Actor);
        UpdateTimerStateForTablet(uid);
    }

    private void OnCreateSquad(EntityUid uid, SecApartmentComponent component, CreateSquadMessage msg)
    {
        if (!CanUseTablet(msg.Actor, uid, component))
            return;

        var squadName = SanitizeUiText(msg.SquadName, MaxSquadNameLength);
        if (string.IsNullOrWhiteSpace(squadName) || component.Station == null)
            return;

        if (!_stationData.TryGetValue(component.Station.Value, out var stationData))
        {
            stationData = new StationData();
            _stationData[component.Station.Value] = stationData;
        }

        var department = component.Department;
        if (component.Debug &&
            !string.IsNullOrWhiteSpace(msg.Department) &&
            DebugSquadDepartments.Contains(msg.Department))
        {
            department = msg.Department;
        }

        var squadId = GenerateSquadId(stationData);
        var squad = new Squad(squadId, squadName, department, GetSquadIconPrefix(department, component.SquadIconPrefix));
        stationData.Squads.Add(squad);

        UpdateAllTabletsOnStation(component.Station.Value);
    }

    private void OnDeleteSquad(EntityUid uid, SecApartmentComponent component, DeleteSquadMessage msg)
    {
        if (!CanUseTablet(msg.Actor, uid, component))
            return;

        if (component.Station == null || !_stationData.TryGetValue(component.Station.Value, out var stationData))
            return;

        var squad = GetVisibleSquads(stationData, component).FirstOrDefault(s => s.SquadId == msg.SquadId);
        if (squad == null)
            return;

        foreach (var member in squad.Members)
        {
            RemoveSquadMemberComponent(member);
        }

        stationData.Squads.Remove(squad);
        UpdateAllTabletsOnStation(component.Station.Value);
    }

    private void OnRenameSquad(EntityUid uid, SecApartmentComponent component, RenameSquadMessage msg)
    {
        if (!CanUseTablet(msg.Actor, uid, component))
            return;

        var newName = SanitizeUiText(msg.NewName, MaxSquadNameLength);
        if (component.Station == null || !_stationData.TryGetValue(component.Station.Value, out var stationData)
            || string.IsNullOrWhiteSpace(newName))
            return;

        var squad = GetVisibleSquads(stationData, component).FirstOrDefault(s => s.SquadId == msg.SquadId);
        if (squad == null)
            return;

        squad.Name = newName;
        UpdateAllTabletsOnStation(component.Station.Value);
    }

    private void OnChangeSquadIcon(EntityUid uid, SecApartmentComponent component, ChangeSquadIconMessage msg)
    {
        if (!CanUseTablet(msg.Actor, uid, component))
            return;

        if (component.Station == null || !_stationData.TryGetValue(component.Station.Value, out var stationData))
            return;

        var squad = GetVisibleSquads(stationData, component).FirstOrDefault(s => s.SquadId == msg.SquadId);
        if (squad == null)
            return;

        squad.IconId = msg.IconId;
        UpdateAllTabletsOnStation(component.Station.Value);

        UpdateSquadMemberIcons(squad, uid, component.Station.Value);
    }

    private void OnUpdateSquadDescription(EntityUid uid, SecApartmentComponent component, UpdateSquadDescriptionMessage msg)
    {
        if (!CanUseTablet(msg.Actor, uid, component))
            return;

        if (component.Station == null || !_stationData.TryGetValue(component.Station.Value, out var stationData))
            return;

        var squad = GetVisibleSquads(stationData, component).FirstOrDefault(s => s.SquadId == msg.SquadId);
        if (squad == null)
            return;

        squad.Description = SanitizeUiText(msg.Description, MaxSquadDescriptionLength);
        UpdateAllTabletsOnStation(component.Station.Value);
    }

    private void OnAddMemberToSquad(EntityUid uid, SecApartmentComponent component, AddMemberToSquadMessage msg)
    {
        if (!CanUseTablet(msg.Actor, uid, component))
            return;

        if (component.Station == null || !_stationData.TryGetValue(component.Station.Value, out var stationData))
            return;

        var squad = GetVisibleSquads(stationData, component).FirstOrDefault(s => s.SquadId == msg.SquadId);
        if (squad == null)
            return;

        var securityCrew = GetAssignableCrew(uid, component, component.Station.Value);
        var member = securityCrew.FirstOrDefault(c => c.MemberId == msg.MemberId)
            ?? securityCrew.FirstOrDefault(c =>
                c.OwnerUid != null && GenerateMobMemberId(c.OwnerUid.Value) == msg.MemberId);
        if (member == null)
            return;

        foreach (var otherSquad in GetVisibleSquads(stationData, component))
        {
            var removedMembers = otherSquad.Members
                .Where(m => m.MemberId == msg.MemberId || IsSameCrewMember(m, member))
                .ToList();

            foreach (var removed in removedMembers)
                RemoveSquadMemberComponent(removed);

            otherSquad.Members.RemoveAll(m => removedMembers.Contains(m));
        }

        if (!squad.Members.Any(m => m.MemberId == msg.MemberId || IsSameCrewMember(m, member)))
        {
            squad.Members.Add(member);
            AddSquadMemberComponent(member, SecApartmentIcons.GetPrototypeId(squad.IconId, squad.SquadIconPrefix));
        }

        UpdateAllTabletsOnStation(component.Station.Value);
    }

    private void OnRemoveMemberFromSquad(EntityUid uid, SecApartmentComponent component, RemoveMemberFromSquadMessage msg)
    {
        if (!CanUseTablet(msg.Actor, uid, component))
            return;

        if (component.Station == null || !_stationData.TryGetValue(component.Station.Value, out var stationData))
            return;

        var squad = GetVisibleSquads(stationData, component).FirstOrDefault(s => s.SquadId == msg.SquadId);
        if (squad == null)
            return;

        var targetMember = squad.Members.FirstOrDefault(m => m.MemberId == msg.MemberId);
        var removedMembers = squad.Members
            .Where(m => m.MemberId == msg.MemberId || targetMember != null && IsSameCrewMember(m, targetMember))
            .ToList();

        squad.Members.RemoveAll(m => removedMembers.Contains(m));

        foreach (var member in removedMembers)
            RemoveSquadMemberComponent(member);

        UpdateAllTabletsOnStation(component.Station.Value);
    }

    private void OnChangeSquadStatus(EntityUid uid, SecApartmentComponent component, ChangeSquadStatusMessage msg)
    {
        if (!CanUseTablet(msg.Actor, uid, component))
            return;

        if (component.Station == null || !_stationData.TryGetValue(component.Station.Value, out var stationData))
            return;

        var squad = GetVisibleSquads(stationData, component).FirstOrDefault(s => s.SquadId == msg.SquadId);
        if (squad == null)
            return;

        squad.Status = msg.Status;
        UpdateAllTabletsOnStation(component.Station.Value);
    }

    private void OnRemoveTimer(EntityUid uid, SecApartmentComponent component, RemoveTimerMessage msg)
    {
        if (!CanUseTablet(msg.Actor, uid, component))
            return;

        RemoveTimerFromTablet(uid, msg.TimerUid, component);
    }

    #endregion

    private void UpdateSquadMemberIcons(Squad squad, EntityUid tabletUid, EntityUid station)
    {
        if (!TryComp<SecApartmentComponent>(tabletUid, out var tabletComp))
            return;

        var securityCrew = GetAssignableCrew(tabletUid, tabletComp, station);

        for (var i = 0; i < squad.Members.Count; i++)
        {
            var currentMember = FindCrewMember(squad.Members[i], securityCrew);
            if (currentMember == null)
                continue;

            squad.Members[i] = currentMember;
            AddSquadMemberComponent(currentMember, SecApartmentIcons.GetPrototypeId(squad.IconId, squad.SquadIconPrefix));
        }
    }

    private void AddSquadMemberComponent(CrewMemberInfo member, string iconId)
    {
        if (member.OwnerUid == null)
            return;

        var entityUid = GetEntity(member.OwnerUid.Value);
        if (!Exists(entityUid) || Terminating(entityUid))
            return;

        EnsureComp<SquadMemberComponent>(entityUid, out var comp);
        comp.StatusIcon = iconId;
        Dirty(entityUid, comp);
    }

    private void RemoveSquadMemberComponent(CrewMemberInfo member)
    {
        if (member.OwnerUid == null)
            return;

        var entityUid = GetEntity(member.OwnerUid.Value);
        if (!Exists(entityUid) || Terminating(entityUid))
            return;

        RemComp<SquadMemberComponent>(entityUid);
    }

    private void UpdateUi(EntityUid uid, SecApartmentComponent comp, EntityUid? openedBy = null)
    {
        if (!_ui.HasUi(uid, SecApartmentUiKey.Key) || comp.Station == null)
            return;

        var stationName = MetaData(comp.Station.Value).EntityName;
        var securityCrew = GetAssignableCrew(uid, comp, comp.Station.Value);
        AddOpeningUser(uid, comp, openedBy, securityCrew);

        var squads = _stationData.TryGetValue(comp.Station.Value, out var stationData)
            ? GetVisibleSquads(stationData, comp)
            : new List<Squad>();

        var assignedMemberIds = new HashSet<string>();
        var assignedOwners = new HashSet<NetEntity>();
        var assignedMinds = new HashSet<NetEntity>();
        foreach (var squad in squads)
        {
            SyncSquadMembers(uid, squad, securityCrew);
            foreach (var member in squad.Members)
            {
                AddSquadMemberComponent(member, SecApartmentIcons.GetPrototypeId(squad.IconId, squad.SquadIconPrefix));
                assignedMemberIds.Add(member.MemberId);
                if (member.OwnerUid != null)
                    assignedOwners.Add(member.OwnerUid.Value);
                if (TryGetMemberMind(member, out var mind))
                    assignedMinds.Add(mind);
            }
        }

        var unassignedSecurity = securityCrew
            .Where(member =>
                !assignedMemberIds.Contains(member.MemberId) &&
                (member.OwnerUid == null || !assignedOwners.Contains(member.OwnerUid.Value)) &&
                (!TryGetMemberMind(member, out var mind) || !assignedMinds.Contains(mind)) &&
                !squads.Any(squad => squad.Members.Any(assigned => IsSameCrewMember(assigned, member))))
            .ToList();

        var state = new SecApartmentUpdateState(
            stationName,
            comp.Department,
            comp.Debug,
            securityCrew,
            unassignedSecurity,
            squads
        );

        _ui.SetUiState(uid, SecApartmentUiKey.Key, state);
    }

    private void AddOpeningUser(EntityUid tablet, SecApartmentComponent comp, EntityUid? user, List<CrewMemberInfo> crew)
    {
        if (user == null)
            return;

        if (!TryComp<MindContainerComponent>(user.Value, out var mind) || !mind.HasMind)
            return;

        var mob = ResolveMindMob(user.Value, mind);
        if (mob == null)
            return;

        if (!comp.Debug && (!_jobs.MindTryGetJob(mind.Mind, out var job) || !IsJobVisibleForTablet(comp, job)))
            return;

        var userNet = GetNetEntity(mob.Value);
        if (crew.Any(member => member.OwnerUid == userNet))
            return;

        var member = CreateCrewMemberFromMob(tablet, mob.Value, mind.Mind);
        var manifestIndex = crew.FindIndex(existing =>
            existing.OwnerUid == null &&
            existing.Name == member.Name &&
            existing.JobTitle == member.JobTitle);

        if (manifestIndex >= 0)
            crew[manifestIndex] = member;
        else
            crew.Add(member);
    }

    private void UpdateAllTabletsOnStation(EntityUid station)
    {
        var query = EntityQueryEnumerator<SecApartmentComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Station == station)
                UpdateUi(uid, comp);
        }
    }

    private List<CrewMemberInfo> GetAssignableCrew(EntityUid tablet, SecApartmentComponent comp, EntityUid station)
    {
        var result = new List<CrewMemberInfo>();
        var seenMemberIds = new HashSet<string>();
        var seenOwners = new HashSet<NetEntity>();
        var seenMinds = new HashSet<NetEntity>();

        var (_, manifest) = _crewManifest.GetCrewManifest(station);
        if (manifest != null)
        {
            foreach (var entry in manifest.Entries)
            {
                if (!comp.Debug && !IsJobVisibleForTablet(comp, entry.JobPrototype))
                    continue;

                var member = CreateCrewMemberInfo(tablet, entry);
                if (!TryAddCrewMember(result, seenMemberIds, seenOwners, seenMinds, member))
                    continue;
            }
        }

        AppendStationPlayers(
            tablet,
            station,
            result,
            seenMemberIds,
            seenOwners,
            seenMinds,
            comp.Debug ? null : comp,
            comp.Debug);

        return result;
    }

    private void AppendStationPlayers(
        EntityUid tablet,
        EntityUid station,
        List<CrewMemberInfo> result,
        HashSet<string> seenMemberIds,
        HashSet<NetEntity> seenOwners,
        HashSet<NetEntity> seenMinds,
        SecApartmentComponent? tabletComp,
        bool debug)
    {
        var query = EntityQueryEnumerator<MindContainerComponent>();
        while (query.MoveNext(out var uid, out var mindContainer))
        {
            if (!mindContainer.HasMind)
                continue;

            var mob = ResolveMindMob(uid, mindContainer);
            if (mob == null)
                continue;

            if (tabletComp != null &&
                (!_jobs.MindTryGetJob(mindContainer.Mind, out var job) || !IsJobVisibleForTablet(tabletComp, job)))
                continue;

            if (!IsPlayerOnStation(mob.Value, station))
            {
                if (!debug ||
                    !_jobs.MindTryGetJob(mindContainer.Mind, out var offStationJob) ||
                    !IsDebugAssignableOffStationJob(offStationJob.ID))
                {
                    continue;
                }
            }

            var ownerUid = GetNetEntity(mob.Value);
            if (seenOwners.Contains(ownerUid))
                continue;

            var member = CreateCrewMemberFromMob(tablet, mob.Value, mindContainer.Mind);
            if (TryGetMemberMind(member, out var memberMind) && seenMinds.Contains(memberMind))
                continue;

            var manifestIndex = result.FindIndex(existing =>
                existing.OwnerUid == null &&
                existing.Name == member.Name &&
                existing.JobTitle == member.JobTitle);

            if (manifestIndex >= 0)
            {
                seenMemberIds.Remove(result[manifestIndex].MemberId);
                result[manifestIndex] = member;
                MarkCrewMemberSeen(seenMemberIds, seenOwners, seenMinds, member);
                continue;
            }

            if (!TryAddCrewMember(result, seenMemberIds, seenOwners, seenMinds, member))
                continue;
        }
    }

    private bool TryAddCrewMember(
        List<CrewMemberInfo> result,
        HashSet<string> seenMemberIds,
        HashSet<NetEntity> seenOwners,
        HashSet<NetEntity> seenMinds,
        CrewMemberInfo member)
    {
        if (seenMemberIds.Contains(member.MemberId))
            return false;

        if (member.OwnerUid != null && seenOwners.Contains(member.OwnerUid.Value))
            return false;

        if (TryGetMemberMind(member, out var mind) && seenMinds.Contains(mind))
            return false;

        seenMemberIds.Add(member.MemberId);

        if (member.OwnerUid != null)
            seenOwners.Add(member.OwnerUid.Value);

        if (TryGetMemberMind(member, out mind))
            seenMinds.Add(mind);

        result.Add(member);
        return true;
    }

    private void MarkCrewMemberSeen(
        HashSet<string> seenMemberIds,
        HashSet<NetEntity> seenOwners,
        HashSet<NetEntity> seenMinds,
        CrewMemberInfo member)
    {
        seenMemberIds.Add(member.MemberId);

        if (member.OwnerUid != null)
            seenOwners.Add(member.OwnerUid.Value);

        if (TryGetMemberMind(member, out var mind))
            seenMinds.Add(mind);
    }

    private EntityUid? ResolveMindMob(EntityUid container, MindContainerComponent mindContainer)
    {
        if (mindContainer.Mind == null || !TryComp<MindComponent>(mindContainer.Mind.Value, out var mind))
            return container;

        if (HasComp<BrainComponent>(container))
            return container;

        if (mind.CurrentEntity == container)
            return container;

        if (mind.OwnedEntity != null && Exists(mind.OwnedEntity.Value) && !Terminating(mind.OwnedEntity.Value))
            return mind.OwnedEntity.Value;

        var current = mind.CurrentEntity;
        if (current != null && Exists(current.Value) && !Terminating(current.Value))
            return current.Value;

        return container;
    }

    private CrewMemberInfo CreateCrewMemberFromMob(EntityUid tablet, EntityUid mob, EntityUid? mind)
    {
        var ownerUid = GetNetEntity(mob);
        var (name, jobTitle, jobIcon) = GetCrewMemberIdentity(mob);

        var sensorStatus = GetCrewMemberSensorStatus(tablet, mob, name, jobTitle, jobIcon);

        return new CrewMemberInfo(
            GenerateMobMemberId(ownerUid),
            ownerUid,
            name,
            jobTitle,
            jobIcon,
            sensorStatus);
    }

    private (string Name, string JobTitle, string JobIcon) GetCrewMemberIdentity(EntityUid mob)
    {
        if (TryGetWornIdCard(mob, out var card))
        {
            var name = card.Comp.FullName ?? Loc.GetString("suit-sensor-component-unknown-name");
            var jobTitle = card.Comp.LocalizedJobTitle ?? card.Comp.JobTitle ?? Loc.GetString("suit-sensor-component-unknown-job");
            return (name, jobTitle, card.Comp.JobIcon);
        }

        return (
            Loc.GetString("suit-sensor-component-unknown-name"),
            Loc.GetString("suit-sensor-component-unknown-job"),
            "JobIconNoId");
    }

    private bool TryGetWornIdCard(EntityUid mob, out Entity<IdCardComponent> card)
    {
        // Do not use TryFindIdCard here: it also checks held items, which would let a stolen ID rename the thief.
        if (_idCard.TryGetIdCard(mob, out card))
            return true;

        if (_inventory.TryGetSlotEntity(mob, "id", out var idUid) &&
            _idCard.TryGetIdCard(idUid.Value, out card))
        {
            return true;
        }

        card = default;
        return false;
    }

    private SuitSensorStatus? GetCrewMemberSensorStatus(
        EntityUid tablet,
        EntityUid mob,
        string name,
        string jobTitle,
        string jobIcon)
    {
        var sensorStatus = TryGetSensorStatusForMob(tablet, mob);

        if (IsDeadForSecApartment(mob))
            return CreateStatusOverride(mob, name, jobTitle, jobIcon, sensorStatus, MobState.Dead);

        if (HasComp<BrainComponent>(mob))
            return CreateStatusOverride(mob, name, jobTitle, jobIcon, sensorStatus, MobState.Dead);

        if (TryComp<MobStateComponent>(mob, out var mobState))
            return mobState.CurrentState switch
            {
                MobState.Dead => CreateStatusOverride(mob, name, jobTitle, jobIcon, sensorStatus, MobState.Dead),
                MobState.Critical => CreateStatusOverride(mob, name, jobTitle, jobIcon, sensorStatus, MobState.Critical),
                _ => sensorStatus ?? CreateStatusOverride(mob, name, jobTitle, jobIcon, null, MobState.Alive)
            };

        return sensorStatus ?? CreateStatusOverride(mob, name, jobTitle, jobIcon, null, MobState.Alive);
    }

    private SuitSensorStatus CreateStatusOverride(
        EntityUid mob,
        string name,
        string jobTitle,
        string jobIcon,
        SuitSensorStatus? existing,
        MobState state)
    {
        var status = new SuitSensorStatus(
            existing?.SuitSensorUid ?? GetNetEntity(mob),
            existing?.Name ?? name,
            existing?.Job ?? jobTitle,
            existing?.JobIcon ?? jobIcon,
            existing?.JobDepartments ?? new List<string>(),
            existing?.LocationName ?? Loc.GetString("sec-apartment-unknown"))
        {
            Timestamp = existing?.Timestamp ?? _gameTiming.CurTime,
            Coordinates = existing?.Coordinates,
            IsAlive = state != MobState.Dead,
            TotalDamage = existing?.TotalDamage,
            TotalDamageThreshold = existing?.TotalDamageThreshold
        };

        if (state == MobState.Critical)
        {
            status.TotalDamage = Math.Max(status.TotalDamage ?? 0, 125);
            status.TotalDamageThreshold = Math.Max(status.TotalDamageThreshold ?? 0, 100);
        }

        return status;
    }

    private bool IsPlayerOnStation(EntityUid uid, EntityUid station)
    {
        if (_station.GetOwningStation(uid) == station)
            return true;

        if (!TryComp<StationDataComponent>(station, out var data))
            return false;

        var map = Transform(uid).MapID;
        foreach (var grid in data.Grids)
        {
            if (Transform(grid).MapID == map)
                return true;
        }

        return false;
    }

    private void SyncSquadMembers(EntityUid tablet, Squad squad, List<CrewMemberInfo> crew)
    {
        for (var i = 0; i < squad.Members.Count; i++)
        {
            var member = squad.Members[i];
            var updated = FindCrewMember(member, crew) ?? RefreshStoredMemberFromEntity(tablet, member);
            if (updated != null)
                updated = NormalizeMemberFromOwner(tablet, updated);

            if (updated != null)
                squad.Members[i] = updated;
        }
    }

    private CrewMemberInfo NormalizeMemberFromOwner(EntityUid tablet, CrewMemberInfo member)
    {
        if (member.OwnerUid == null)
            return member;

        var entity = GetEntity(member.OwnerUid.Value);
        if (!Exists(entity) || Terminating(entity))
            return CreateStoredDeadMember(member) ?? member;

        if (TryComp<MindContainerComponent>(entity, out var mindContainer))
        {
            var resolved = ResolveMindMob(entity, mindContainer);
            if (resolved != null && resolved.Value != entity)
                return CreateCrewMemberFromMob(tablet, resolved.Value, mindContainer.Mind);
        }

        var (name, jobTitle, jobIcon) = GetCrewMemberIdentity(entity);
        var status = GetCrewMemberSensorStatus(tablet, entity, name, jobTitle, jobIcon);
        if (IsDeadForSecApartment(entity))
            status = CreateStatusOverride(entity, name, jobTitle, jobIcon, status ?? member.SensorStatus, MobState.Dead);

        return new CrewMemberInfo(
            GenerateMobMemberId(member.OwnerUid.Value),
            member.OwnerUid,
            name,
            jobTitle,
            jobIcon,
            status);
    }

    private CrewMemberInfo? RefreshStoredMemberFromEntity(EntityUid tablet, CrewMemberInfo member)
    {
        if (member.OwnerUid == null)
            return null;
        return NormalizeMemberFromOwner(tablet, member);
    }

    private bool IsDeadForSecApartment(EntityUid entity)
    {
        if (HasComp<BrainComponent>(entity))
            return true;

        var hasMobState = TryComp<MobStateComponent>(entity, out var mobState);
        if (hasMobState && mobState != null && mobState.CurrentState == MobState.Dead)
            return true;

        if (hasMobState && HasComp<ActorComponent>(entity))
            return false;

        if (!TryComp<MindContainerComponent>(entity, out var mindContainer) || !mindContainer.HasMind)
            return true;

        if (mindContainer.Mind == null || !TryComp<MindComponent>(mindContainer.Mind.Value, out var mind))
            return true;

        if (mind.OwnedEntity != null && mind.OwnedEntity != entity)
            return true;

        if (hasMobState)
            return false;

        return mind.TimeOfDeath != null;
    }

    private CrewMemberInfo? CreateStoredDeadMember(CrewMemberInfo member)
    {
        if (member.OwnerUid == null && member.SensorStatus == null)
            return null;

        var status = member.SensorStatus == null
            ? null
            : new SuitSensorStatus(
                member.SensorStatus.SuitSensorUid,
                member.SensorStatus.Name,
                member.SensorStatus.Job,
                member.SensorStatus.JobIcon,
                member.SensorStatus.JobDepartments,
                member.SensorStatus.LocationName)
            {
                Timestamp = _gameTiming.CurTime,
                Coordinates = member.SensorStatus.Coordinates,
                IsAlive = false,
                TotalDamage = member.SensorStatus.TotalDamage,
                TotalDamageThreshold = member.SensorStatus.TotalDamageThreshold
            };

        return new CrewMemberInfo(
            member.MemberId,
            member.OwnerUid,
            member.Name,
            member.JobTitle,
            member.JobIcon,
            status);
    }

    private CrewMemberInfo CreateCrewMemberInfo(EntityUid tablet, CrewManifestEntry entry)
    {
        var mob = TryResolveMobFromManifestEntry(tablet, entry);
        if (mob != null)
            return CreateCrewMemberFromMob(tablet, mob.Value, null);

        var status = TryGetSensorStatusByName(tablet, entry.Name, entry.JobTitle, out var ownerUid);
        if (ownerUid != null)
        {
            var owner = GetEntity(ownerUid.Value);
            if (Exists(owner) && !Terminating(owner))
                status = GetCrewMemberSensorStatus(tablet, owner, entry.Name, entry.JobTitle, entry.JobIcon);
        }

        var memberId = ownerUid != null
            ? GenerateMobMemberId(ownerUid.Value)
            : GenerateManifestMemberId(entry);

        return new CrewMemberInfo(
            memberId,
            ownerUid,
            entry.Name,
            entry.JobTitle,
            entry.JobIcon,
            status);
    }

    private EntityUid? TryResolveMobFromManifestEntry(EntityUid tablet, CrewManifestEntry entry)
    {
        if (TryComp<SecApartmentComponent>(tablet, out var apartment) && apartment.Station != null)
        {
            var mindQuery = EntityQueryEnumerator<MindContainerComponent>();
            while (mindQuery.MoveNext(out var uid, out var mindContainer))
            {
                if (mindContainer.Mind == null ||
                    !TryComp<MindComponent>(mindContainer.Mind.Value, out var mind) ||
                    mind.CharacterName != entry.Name)
                {
                    continue;
                }

                var mob = ResolveMindMob(uid, mindContainer);
                if (mob != null &&
                    (IsPlayerOnStation(mob.Value, apartment.Station.Value) ||
                     apartment.Debug && IsDebugAssignableOffStationJob(entry.JobPrototype)))
                {
                    return mob.Value;
                }
            }
        }

        if (!TryComp<CrewMonitoringConsoleComponent>(tablet, out var monitoring))
            return null;

        foreach (var sensor in monitoring.ConnectedSensors.Values)
        {
            if (sensor.Name != entry.Name || sensor.Job != entry.JobTitle)
                continue;

            var sensorEntity = GetEntity(sensor.SuitSensorUid);
            if (TryComp<SuitSensorComponent>(sensorEntity, out var sensorComp) && sensorComp.User != null)
                return sensorComp.User.Value;
        }

        return null;
    }

    private SuitSensorStatus? TryGetSensorStatusForMob(EntityUid tablet, EntityUid mob)
    {
        if (!TryComp<CrewMonitoringConsoleComponent>(tablet, out var monitoring))
            return null;

        foreach (var sensor in monitoring.ConnectedSensors.Values)
        {
            var sensorEntity = GetEntity(sensor.SuitSensorUid);
            if (TryComp<SuitSensorComponent>(sensorEntity, out var sensorComp) && sensorComp.User == mob)
                return sensor;
        }

        return null;
    }

    private SuitSensorStatus? TryGetSensorStatusByName(
        EntityUid tablet,
        string name,
        string jobTitle,
        out NetEntity? ownerUid)
    {
        ownerUid = null;

        if (!TryComp<CrewMonitoringConsoleComponent>(tablet, out var monitoring))
            return null;

        var sensor = monitoring.ConnectedSensors.Values
            .FirstOrDefault(s => s.Name == name && s.Job == jobTitle);

        if (sensor == null)
            return null;

        var sensorEntity = GetEntity(sensor.SuitSensorUid);
        if (TryComp<SuitSensorComponent>(sensorEntity, out var sensorComp) && sensorComp.User != null)
            ownerUid = GetNetEntity(sensorComp.User.Value);

        return sensor;
    }

    private static string GenerateMobMemberId(NetEntity mob) => $"mob:{mob}";

    private static string GenerateManifestMemberId(CrewManifestEntry entry)
    {
        return $"{entry.Name.GetHashCode():X8}_{entry.JobPrototype}_{entry.JobTitle.GetHashCode():X8}";
    }

    private (string Location, bool HasLocation) GetSquadApproximateLocation(Squad squad, List<CrewMemberInfo> securityCrew)
    {
        var trackedPositions = new List<Vector2>();
        var trackedGrids = new Dictionary<EntityUid, int>();
        var mapId = MapId.Nullspace;

        foreach (var squadMember in squad.Members)
        {
            var memberInfo = FindCrewMember(squadMember, securityCrew) ?? squadMember;

            if (memberInfo.OwnerUid == null)
                continue;

            var ownerUid = GetEntity(memberInfo.OwnerUid.Value);
            if (!Exists(ownerUid) || Terminating(ownerUid))
                continue;

            var memberTransform = Transform(ownerUid);
            if (memberTransform.GridUid == null)
                continue;

            trackedGrids[memberTransform.GridUid.Value] = trackedGrids.GetValueOrDefault(memberTransform.GridUid.Value) + 1;

            var mapPos = _transform.GetMapCoordinates(ownerUid);

            trackedPositions.Add(mapPos.Position);
            mapId = mapPos.MapId;
        }

        if (trackedGrids.Count > 0)
        {
            var grid = trackedGrids
                .OrderByDescending(pair => pair.Value)
                .First()
                .Key;

            var gridName = MetaData(grid).EntityName;
            if (!string.IsNullOrWhiteSpace(gridName))
                return (gridName, true);
        }

        if (trackedPositions.Count == 0)
            return (Loc.GetString("sec-apartment-unknown"), false);

        var averagePos = Vector2.Zero;
        foreach (var pos in trackedPositions)
        {
            averagePos += pos;
        }
        averagePos /= trackedPositions.Count;

        try
        {
            var mapCoords = new MapCoordinates(averagePos, mapId);
            var locationText = _navMap.GetNearestBeaconString(mapCoords, onlyName: true);
            return (locationText, true);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to get squad location: {ex}");
            return (Loc.GetString("sec-apartment-unknown"), false);
        }
    }

    #region Timers
    private void OnLinkTimer(EntityUid uid, SignalTimerComponent component, AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null)
            return;

        if (!TryComp<SecApartmentComponent>(args.Used, out var tablet))
            return;

        if (!CanUseTablet(args.User, args.Used, tablet))
        {
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("lock-comp-has-user-access-fail"), args.Used, args.User);
            return;
        }

        args.Handled = true;
        LinkTimerToTablet(args.User, args.Used, uid, tablet);
    }

    private void LinkTimerToTablet(EntityUid user, EntityUid tablet, EntityUid timer, SecApartmentComponent comp)
    {
        var netTimer = GetNetEntity(timer);

        if (comp.TrackedTimers.Contains(netTimer))
        {
            _popup.PopupEntity(Loc.GetString("sec-apartment-timer-already-linked"), tablet, user);
            return;
        }

        comp.TrackedTimers.Add(netTimer);
        _finishedTimers.Remove(netTimer);
        Dirty(tablet, comp);

        _popup.PopupEntity(Loc.GetString("sec-apartment-timer-linked"), tablet, user);
        UpdateTimerStateForTablet(tablet);
    }

    private void OnLinkedTimerStarted(EntityUid uid, ActiveSignalTimerComponent component, ComponentStartup args)
    {
        var netTimer = GetNetEntity(uid);
        _finishedTimers.Remove(netTimer);

        var query = EntityQueryEnumerator<SecApartmentComponent>();
        while (query.MoveNext(out var tablet, out var comp))
        {
            if (!comp.TrackedTimers.Contains(netTimer))
                continue;

            UpdateTimerStateForTablet(tablet);
        }
    }

    private void OnTimerComponentShutdown(EntityUid uid, SignalTimerComponent component, ComponentShutdown args)
    {
        var netTimer = GetNetEntity(uid);
        _finishedTimers.Remove(netTimer);

        var query = EntityQueryEnumerator<SecApartmentComponent>();
        while (query.MoveNext(out var tablet, out var comp))
        {
            if (!comp.TrackedTimers.Remove(netTimer))
                continue;

            Dirty(tablet, comp);
            UpdateTimerStateForTablet(tablet);
        }
    }

    private void RemoveTimerFromTablet(EntityUid tablet, NetEntity timerNet, SecApartmentComponent comp)
    {
        if (!comp.TrackedTimers.Remove(timerNet))
            return;

        _finishedTimers.Remove(timerNet);
        Dirty(tablet, comp);
        UpdateTimerStateForTablet(tablet);
    }

    private void UpdateAllTimerStates()
    {
        var query = EntityQueryEnumerator<SecApartmentComponent>();
        while (query.MoveNext(out var tablet, out var comp))
        {
            if (comp.TrackedTimers.Count > 0)
                UpdateTimerStateForTablet(tablet);
        }
    }

    private void UpdateTimerStateForTablet(EntityUid tablet)
    {
        if (!TryComp<SecApartmentComponent>(tablet, out var comp))
            return;

        var timers = BuildTimerEntries(comp);

        if (!_ui.IsUiOpen(tablet, SecApartmentUiKey.Key))
            return;

        _ui.ServerSendUiMessage(tablet, SecApartmentUiKey.Key, new TimerUpdateState(timers));
    }

    private List<TimerEntry> BuildTimerEntries(SecApartmentComponent comp)
    {
        var timers = new List<TimerEntry>();

        foreach (var netEntity in comp.TrackedTimers.ToList())
        {
            var timerUid = GetEntity(netEntity);
            if (!Exists(timerUid))
            {
                comp.TrackedTimers.Remove(netEntity);
                continue;
            }

            if (!TryComp<SignalTimerComponent>(timerUid, out var timerComp))
            {
                comp.TrackedTimers.Remove(netEntity);
                continue;
            }

            var total = TimeSpan.FromSeconds(timerComp.Delay);
            TimeSpan remaining;

            if (TryComp<ActiveSignalTimerComponent>(timerUid, out var activeComp))
            {
                remaining = activeComp.TriggerTime - _gameTiming.CurTime;
            }
            else if (_finishedTimers.TryGetValue(netEntity, out var finishedTime))
            {
                remaining = finishedTime - _gameTiming.CurTime;
            }
            else
            {
                remaining = total;
            }

            timers.Add(new TimerEntry(netEntity, timerComp.Label, remaining, total));
        }

        return timers;
    }
    #endregion
}

public sealed class StationData
{
    public List<Squad> Squads { get; } = new();
}
