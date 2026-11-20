using System.Linq;
using Content.Server._Mono.Company;
using Content.Shared._Forge.Company;
using Content.Shared._Mono.Company;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Pinpointer;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Company;

public sealed class CompanyDoorAccessConsoleSystem : EntitySystem
{
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private CompanyManager _company = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CompanySystem _companySystem = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _configureCooldowns = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CompanyDoorAccessConsoleComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<CompanyDoorAccessConsoleComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<CompanyDoorAccessConsoleComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<CompanyDoorAccessConsoleComponent, AfterActivatableUIOpenEvent>(OnUiOpen);
        SubscribeLocalEvent<CompanyDoorAccessConsoleComponent, CompanyDoorAccessConsoleSetMessage>(OnSetAccess);
    }

    private void OnInit(EntityUid uid, CompanyDoorAccessConsoleComponent component, ComponentInit args)
    {
        RefreshBoundCompany(uid, component);
    }

    private void OnParentChanged(EntityUid uid, CompanyDoorAccessConsoleComponent component, ref EntParentChangedMessage args)
    {
        RefreshBoundCompany(uid, component);
    }

    private void RefreshBoundCompany(EntityUid uid, CompanyDoorAccessConsoleComponent component)
    {
        var grid = _transform.GetGrid(uid);
        if (grid == null || !TryComp<CompanyComponent>(grid.Value, out var company) || company.CompanyName == "None")
        {
            component.BoundCompany = "None";
            Dirty(uid, component);
            return;
        }

        component.BoundCompany = company.CompanyName;
        Dirty(uid, component);
    }

    private void OnOpenAttempt(EntityUid uid, CompanyDoorAccessConsoleComponent component, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        RefreshBoundCompany(uid, component);

        if (!CanAccessConsole(args.User, component.BoundCompany))
        {
            args.Cancel();
            _popup.PopupEntity(Loc.GetString("company-door-console-access-denied"), uid, args.User);
        }
    }

    private bool CanAccessConsole(EntityUid user, ProtoId<CompanyPrototype> boundCompany)
    {
        if (boundCompany == "None")
            return false;

        // Require membership of the currently selected character — not just CompanyComponent affiliation.
        if (!TryComp<ActorComponent>(user, out var actor))
            return false;

        if (!_company.TryGetCharacterContext(actor.PlayerSession.UserId, out var slot, out _))
            return false;

        var membership = _company.GetMemberForCharacter(actor.PlayerSession.UserId, slot);
        return membership != null && membership.Value.Company == boundCompany;
    }

    private void OnUiOpen(EntityUid uid, CompanyDoorAccessConsoleComponent component, AfterActivatableUIOpenEvent args)
    {
        RefreshBoundCompany(uid, component);

        if (!CanAccessConsole(args.User, component.BoundCompany))
        {
            _ui.CloseUi(uid, CompanyDoorAccessConsoleUiKey.Key, args.User);
            return;
        }

        if (TryComp<ActorComponent>(args.User, out var actor) &&
            component.BoundCompany != "None")
        {
            _companySystem.ApplyCompanyAccessTags(args.User, component.BoundCompany, actor.PlayerSession);
        }

        UpdateUi(uid, component, args.User);
    }

    private void OnSetAccess(EntityUid uid, CompanyDoorAccessConsoleComponent component, CompanyDoorAccessConsoleSetMessage args)
    {
        RefreshBoundCompany(uid, component);

        if (!CanAccessConsole(args.Actor, component.BoundCompany))
            return;

        if (!TryComp<ActorComponent>(args.Actor, out var actor))
            return;

        var session = actor.PlayerSession;
        if (!_company.HasPermission(session, component.BoundCompany, CompanyPermission.ConfigureDoors) &&
            !_company.IsOwnerSession(session, component.BoundCompany))
            return;

        var userTier = _company.GetAccessTier(session.UserId, component.BoundCompany);
        if (userTier == CompanyAccessTier.None)
            return;

        var now = _timing.CurTime;
        if (_configureCooldowns.TryGetValue(uid, out var readyAt) && now < readyAt)
            return;
        _configureCooldowns[uid] = now + TimeSpan.FromSeconds(CompanyOrgLimits.DoorConfigureCooldownSeconds);

        var consoleGrid = _transform.GetGrid(uid);
        if (consoleGrid == null || args.Doors.Count == 0)
            return;

        var doorCount = 0;
        foreach (var netDoor in args.Doors)
        {
            if (doorCount >= CompanyOrgLimits.MaxDoorsPerSetMessage)
                break;

            var door = GetEntity(netDoor);
            if (!HasComp<AirlockComponent>(door))
                continue;

            var doorGrid = _transform.GetGrid(door);
            if (doorGrid == null || doorGrid != consoleGrid)
                continue;

            if (!_accessReader.GetMainAccessReader(door, out var readerEnt))
                continue;

            doorCount++;
            var readerUid = readerEnt.Value.Owner;
            var reader = readerEnt.Value.Comp;
            var currentMode = ResolveMode(reader, component.BoundCompany);

            if (!CompanyAccessHelper.CanConfigureDoorMode(userTier, currentMode, args.Mode))
                continue;

            switch (args.Mode)
            {
                case CompanyDoorAccessMode.Open:
                    _accessReader.SetAccesses(readerUid, reader, new List<ProtoId<AccessLevelPrototype>>());
                    SetRequiredCompany(door, readerUid, reader, null);
                    break;

                case CompanyDoorAccessMode.Low:
                case CompanyDoorAccessMode.Medium:
                case CompanyDoorAccessMode.High:
                    var tier = args.Mode switch
                    {
                        CompanyDoorAccessMode.Low => CompanyAccessTier.Low,
                        CompanyDoorAccessMode.Medium => CompanyAccessTier.Medium,
                        _ => CompanyAccessTier.High,
                    };
                    var levelId = CompanyAccessHelper.GetAccessLevelId(component.BoundCompany, tier);
                    _accessReader.SetAccesses(readerUid, reader, new List<ProtoId<AccessLevelPrototype>> { levelId });
                    SetRequiredCompany(door, readerUid, reader, component.BoundCompany);
                    break;
            }
        }

        UpdateUi(uid, component, args.Actor);
    }

    private void SetRequiredCompany(
        EntityUid door,
        EntityUid readerUid,
        AccessReaderComponent reader,
        ProtoId<CompanyPrototype>? company)
    {
        var companyId = company?.Id;
        reader.RequiredCompany = companyId;
        Dirty(readerUid, reader);

        // Keep the door's own reader in sync — IsAllowed may check either depending on board state.
        if (readerUid != door && TryComp<AccessReaderComponent>(door, out var doorReader))
        {
            doorReader.RequiredCompany = companyId;
            Dirty(door, doorReader);
        }
    }

    private void UpdateUi(EntityUid uid, CompanyDoorAccessConsoleComponent component, EntityUid user)
    {
        var state = new CompanyDoorAccessConsoleBoundUserInterfaceState();
        RefreshBoundCompany(uid, component);

        if (component.BoundCompany == "None" || !_proto.TryIndex(component.BoundCompany, out var companyProto))
        {
            state.Active = false;
            state.StatusMessage = Loc.GetString("company-door-console-no-owner");
            _ui.SetUiState(uid, CompanyDoorAccessConsoleUiKey.Key, state);
            return;
        }

        state.Active = true;
        state.BoundCompanyId = component.BoundCompany;
        state.BoundCompanyName = companyProto.Name;

        var canConfigure = false;
        var maxTier = CompanyAccessTier.None;
        if (TryComp<ActorComponent>(user, out var actor))
        {
            canConfigure = _company.IsOwnerSession(actor.PlayerSession, component.BoundCompany) ||
                           _company.HasPermission(actor.PlayerSession, component.BoundCompany, CompanyPermission.ConfigureDoors);
            if (canConfigure)
                maxTier = _company.GetAccessTier(actor.PlayerSession.UserId, component.BoundCompany);
        }

        state.CanConfigure = canConfigure;
        state.MaxAccessTier = maxTier;
        state.ConsoleCoordinates = GetNetCoordinates(_transform.GetMoverCoordinates(uid));

        var grid = _transform.GetGrid(uid);
        if (grid == null)
        {
            _ui.SetUiState(uid, CompanyDoorAccessConsoleUiKey.Key, state);
            return;
        }

        // Required for NavMapControl to draw the grid.
        EnsureComp<NavMapComponent>(grid.Value);
        state.GridEntity = GetNetEntity(grid.Value);

        var query = EntityQueryEnumerator<AirlockComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var doorUid, out _, out var xform, out var meta))
        {
            if (xform.GridUid != grid)
                continue;

            if (!_accessReader.GetMainAccessReader(doorUid, out var readerEnt))
                continue;

            state.Doors.Add(new CompanyDoorAccessEntry
            {
                Door = GetNetEntity(doorUid),
                Name = meta.EntityName,
                Mode = ResolveMode(readerEnt.Value.Comp, component.BoundCompany),
                Coordinates = GetNetCoordinates(_transform.GetMoverCoordinates(doorUid)),
            });
        }

        state.Doors = state.Doors.OrderBy(d => d.Name).ToList();
        _ui.SetUiState(uid, CompanyDoorAccessConsoleUiKey.Key, state);
    }

    private static CompanyDoorAccessMode ResolveMode(AccessReaderComponent reader, ProtoId<CompanyPrototype> company)
    {
        if (reader.AccessLists.Count == 0)
            return CompanyDoorAccessMode.Open;

        if (reader.RequiredCompany != company.Id)
            return CompanyDoorAccessMode.Open;

        var flat = reader.AccessLists.SelectMany(s => s).ToHashSet();
        if (flat.Contains(CompanyAccessHelper.GetAccessLevelId(company, CompanyAccessTier.High)))
            return CompanyDoorAccessMode.High;
        if (flat.Contains(CompanyAccessHelper.GetAccessLevelId(company, CompanyAccessTier.Medium)))
            return CompanyDoorAccessMode.Medium;
        if (flat.Contains(CompanyAccessHelper.GetAccessLevelId(company, CompanyAccessTier.Low)))
            return CompanyDoorAccessMode.Low;

        return CompanyDoorAccessMode.Open;
    }
}
