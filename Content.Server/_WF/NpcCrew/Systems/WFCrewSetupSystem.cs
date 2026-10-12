using System.Linq;
using System.Numerics;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Dataset;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Radio;
using Content.Shared.Roles;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Validates crew plans and applies missions for both admin tools and encounter callers.</summary>
public sealed partial class WFCrewSetupSystem : EntitySystem
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private WFCrewAccessSystem _crewAccess = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFCrewPlannerSystem _planner = default!;
    [Dependency] private WFCrewObjectiveSystem _objectives = default!;
    [Dependency] private WFCrewWorkSystem _work = default!;
    [Dependency] private WFPilotDutySystem _pilots = default!;
    [Dependency] private WFRadioOperatorSystem _radio = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private AdminVesselSpawnSystem _vessels = default!;
    [Dependency] private Robust.Shared.Random.IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFCrewSetupRequest>(OnRequest);
        SubscribeLocalEvent<WFCrewComponent, GetVerbsEvent<Verb>>(OnVerbs);
    }

    private void OnVerbs(EntityUid uid, WFCrewComponent crew, GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor)
            || !_admins.HasAdminFlag(actor.PlayerSession, AdminFlags.Spawn) || CrewGrid(uid, crew) is not { } grid)
            return;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wf-crew-setup-title"), Category = VerbCategory.Admin,
            Act = () =>
            {
                if (_admins.HasAdminFlag(actor.PlayerSession, AdminFlags.Spawn) && !TerminatingOrDeleted(grid))
                    RaiseNetworkEvent(new WFCrewSetupOpenEvent(GetNetEntity(grid), crew.Group), Filter.SinglePlayer(actor.PlayerSession));
            },
        });
    }

    /// <summary>The ship a crewman belongs to: his current job's ship, else his post's grid, else the grid he stands on.</summary>
    public EntityUid? CrewGrid(EntityUid uid, WFCrewComponent crew, TransformComponent? xform = null)
    {
        if (_work.HomeGrid(uid) is { } home)
            return home;
        if (crew.Post is { } post && post.EntityId.IsValid() && HasComp<MapGridComponent>(post.EntityId))
            return post.EntityId;
        return (xform ?? Transform(uid)).GridUid;
    }

    /// <summary>Every crewman of a crew, wherever he currently stands.</summary>
    private List<EntityUid> Members(EntityUid grid, string group)
    {
        var members = new List<EntityUid>();
        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var xform))
        {
            if (crew.Group == group && CrewGrid(uid, crew, xform) == grid)
                members.Add(uid);
        }
        return members;
    }

    /// <summary>
    /// Cancels a crew's queue and work and deletes its crewmen. Returns how many were removed. With
    /// <paramref name="keepCorpses"/>, the dead lying on some other grid are left there as ordinary bodies.
    /// </summary>
    public int ClearCrew(EntityUid grid, string group, bool keepCorpses = false)
    {
        var members = Members(grid, group);
        _objectives.Cancel(grid, group);
        var removed = 0;
        foreach (var member in members)
        {
            if (keepCorpses && LeaveCorpse(member, grid))
                continue;
            QueueDel(member);
            removed++;
        }
        return removed;
    }

    /// <summary>
    /// A dead crewman somebody has carried off his ship is theirs to keep: he stops being crew and stays where he lies.
    /// </summary>
    private bool LeaveCorpse(EntityUid uid, EntityUid? ship)
    {
        if (!_mobs.IsDead(uid) || Transform(uid).GridUid is not { } lying || lying == ship)
            return false;

        RemComp<WFCrewComponent>(uid);
        return true;
    }

    /// <summary>Deletes a group on every ship and cancels its queues and work. Returns how many crewmen were removed.</summary>
    public int ClearGroup(string group, bool keepCorpses = false)
    {
        var members = new List<EntityUid>();
        var ships = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var xform))
        {
            if (crew.Group != group)
                continue;
            members.Add(uid);
            if (CrewGrid(uid, crew, xform) is { } ship)
                ships.Add(ship);
        }
        foreach (var ship in ships)
            _objectives.Cancel(ship, group);
        var removed = 0;
        foreach (var member in members)
        {
            if (keepCorpses && LeaveCorpse(member, null))
                continue;
            QueueDel(member);
            removed++;
        }
        return removed;
    }

    /// <summary>Whether a grid is too large to scan for posts without stalling the server.</summary>
    public bool IsTooLarge(EntityUid grid)
    {
        if (!TryComp<MapGridComponent>(grid, out var map))
            return false;
        var box = map.LocalAABB;
        return box.Width * box.Height > WFCrewLimits.MaxPlanArea;
    }

    /// <summary>Builds the same roster used by commands, optionally adding a captain to a spare deck post.</summary>
    public List<WFCrewSetupPost> Plan(EntityUid grid, int deckhands, bool captain)
    {
        return Plan(grid, deckhands, captain, out _);
    }

    /// <summary>Plans a roster capped at the window's row limit; total is the uncapped post count.</summary>
    public List<WFCrewSetupPost> Plan(EntityUid grid, int deckhands, bool captain, out int total)
    {
        var posts = _planner.Plan(grid, Math.Clamp(deckhands, 0, 32) + (captain ? 1 : 0));
        if (captain && posts.All(post => post.Role != WFCrewRoles.Captain))
        {
            var index = posts.FindLastIndex(post => post.Kind == WFCrewPostKind.Deck);
            if (index >= 0)
                posts[index] = posts[index] with { Role = WFCrewRoles.Captain };
        }
        total = posts.Count;
        return posts.Take(WFCrewLimits.MaxListItems)
            .Select(post => new WFCrewSetupPost { Role = post.Role.Id, Position = post.Coordinates.Position }).ToList();
    }

    /// <summary>Checks an entire plan before creating anything, including posts, prototypes and mission targets.</summary>
    public bool Validate(EntityUid grid, List<WFCrewSetupPost> posts, WFCrewMission mission)
    {
        if (!ValidateMission(grid, mission) || !TryComp<MapGridComponent>(grid, out var map)
            || posts is null || posts.Count is < 1 or > WFCrewLimits.MaxListItems)
            return false;
        foreach (var post in posts)
        {
            if (post is null || !Bounded(post.Position) || post.Role is null || post.Loadout is null
                || !_prototypes.HasIndex<WFCrewRolePrototype>(post.Role)
                || post.Engagement is { } engagement && !Enum.IsDefined(engagement)
                || post.Loadout.Length > 0 && !_prototypes.HasIndex<StartingGearPrototype>(post.Loadout))
                return false;
            var coordinates = new EntityCoordinates(grid, post.Position);
            var tile = _maps.TileIndicesFor(grid, map, coordinates);
            // A post without air is allowed; the crewman put there is spawned in a void kit.
            if (!_planner.IsStandable(grid, tile, map))
                return false;
        }
        return true;
    }

    private bool ValidateMission(EntityUid grid, WFCrewMission mission)
    {
        if (mission is null || !HasComp<MapGridComponent>(grid) || Transform(grid).MapUid == null || Transform(grid).MapID == MapId.Nullspace
            || !Fits(mission.Group, WFCrewLimits.MaxGroup) || mission.Group.Length == 0
            || !Fits(mission.Callsign, WFCrewLimits.MaxCallsign) || !Fits(mission.Battlegroup, WFCrewLimits.MaxBattlegroup)
            || !Enum.IsDefined(mission.Order)
            || !Enum.IsDefined(mission.BoardingResponse) || !Enum.IsDefined(mission.DockingResponse)
            || !Enum.IsDefined(mission.Disengage) || !Enum.IsDefined(mission.Skill)
            || !string.IsNullOrEmpty(mission.Profile) && !Known<WFCrewProfilePrototype>(mission.Profile)
            || !float.IsFinite(mission.DisengageRange) || mission.DisengageRange is < 50 or > WFCrewLimits.MaxRange
            || mission.Navigation == null || !mission.Navigation.IsValid()
            || !float.IsFinite(mission.Range) || mission.Range is < 1 or > WFCrewLimits.MaxRange
            || !Bounded(mission.Destination) || !Known<NpcFactionPrototype>(mission.Faction)
            || !Known<RadioChannelPrototype>(mission.LocalChannel)
            || !Known<RadioChannelPrototype>(mission.AlertChannel)
            || mission.Company is null || mission.Company.Length > 0 && !Known<CompanyPrototype>(mission.Company))
            return false;
        if (mission.Order is WFPilotOrder.Follow or WFPilotOrder.Dock
            && (mission.Target is not { } target || !TryGetEntity(target, out var other) || other == grid
                || !HasComp<MapGridComponent>(other) || Transform(other.Value).MapID != Transform(grid).MapID))
            return false;
        return true;
    }

    private bool Known<T>(string? id) where T : class, IPrototype => id != null && _prototypes.HasIndex<T>(id);

    private static bool Fits(string? text, int max) => text != null && text.Length <= max;

    private static bool Bounded(Vector2 point) => float.IsFinite(point.X) && float.IsFinite(point.Y)
        && MathF.Abs(point.X) <= WFCrewLimits.MaxCoordinate && MathF.Abs(point.Y) <= WFCrewLimits.MaxCoordinate;

    /// <summary>Encounter entry point: spawns the validated roster through the ordinary crew factory.</summary>
    public bool TrySpawn(EntityUid grid, List<WFCrewSetupPost> posts, WFCrewMission mission, out List<EntityUid> spawned)
    {
        return TrySpawn(grid, posts, mission, true, out spawned);
    }

    /// <summary>
    /// As the other overload. With <paramref name="profileSkills"/> off, the mission's skill stays as set and the
    /// profile's skill pool is not drawn from, so a ship's own skill setting is not overridden.
    /// </summary>
    public bool TrySpawn(EntityUid grid, List<WFCrewSetupPost> posts, WFCrewMission mission, bool profileSkills,
        out List<EntityUid> spawned)
    {
        spawned = new List<EntityUid>();
        if (!Validate(grid, posts, mission))
            return false;
        _objectives.Cancel(grid, mission.Group);
        ApplyCompany(grid, mission.Company);
        _factions.ClearFactions(grid);
        _factions.AddFaction(grid, mission.Faction);
        // A profile rolls each crewman's body and loadout, and one skill for the crew.
        _prototypes.TryIndex<WFCrewProfilePrototype>(mission.Profile ?? string.Empty, out var profile);
        if (profileSkills && profile != null && profile.Skills.Count > 0)
            mission.Skill = _random.Pick(profile.Skills);
        foreach (var post in posts)
        {
            var loadout = post.Loadout.Length > 0 ? new ProtoId<StartingGearPrototype>(post.Loadout) : (ProtoId<StartingGearPrototype>?) null;
            if (loadout == null && profile != null && profile.Loadouts.TryGetValue(post.Role, out var pool) && pool.Count > 0)
                loadout = _random.Pick(pool);
            // A role the profile has no loadout for keeps its own mob and gear, so nobody is spawned naked.
            var body = loadout != null && profile != null && profile.Bodies.Count > 0 ? _random.Pick(profile.Bodies) : (EntProtoId?) null;
            var uid = _crew.SpawnCrewman(post.Role, new EntityCoordinates(grid, post.Position), mission.Group, loadout, body,
                profile != null ? FlavourName(profile) : null);
            if (uid is not { } mob)
                continue;
            if (profile != null && profile.Components.Count > 0)
                EntityManager.AddComponents(mob, profile.Components);
            if (post.Engagement is { } engagement)
                _crew.SetEngagement(mob, engagement);
            else if (profile != null && profile.Engagement.TryGetValue(post.Role, out var manner))
                _crew.SetEngagement(mob, manner);
            // A coward never fights, even when struck: he takes shelter instead.
            if (post.Engagement == null && profile != null && profile.Cowards.TryGetValue(post.Role, out var chance)
                && _random.Prob(Math.Clamp(chance, 0f, 1f)))
                _crew.SetEngagement(mob, WFCrewEngagement.Never);
            ApplyMission(mob, grid, mission);
            spawned.Add(mob);
        }
        return spawned.Count == posts.Count;
    }

    /// <summary>A name rolled from a profile's datasets, or null for a profile that leaves crews the names their bodies roll.</summary>
    private string? FlavourName(WFCrewProfilePrototype profile)
    {
        if (profile.FirstNames is not { } firstId || profile.LastNames is not { } lastId
            || !_prototypes.TryIndex(firstId, out var first) || !_prototypes.TryIndex(lastId, out var last)
            || first.Values.Count == 0 || last.Values.Count == 0)
            return null;

        return $"{_random.Pick(first.Values)} {_random.Pick(last.Values)}";
    }

    /// <summary>Sets the company, or removes it when the company is empty.</summary>
    private void ApplyCompany(EntityUid uid, string company)
    {
        if (company.Length == 0)
        {
            RemComp<CompanyComponent>(uid);
            return;
        }
        var component = EnsureComp<CompanyComponent>(uid);
        component.CompanyName = company;
        Dirty(uid, component);
    }

    /// <summary>Updates an existing crew without requiring a new spawn roster.</summary>
    public bool TryApplyMission(EntityUid grid, WFCrewMission mission)
    {
        if (!ValidateMission(grid, mission))
            return false;
        _objectives.Cancel(grid, mission.Group);
        ApplyCompany(grid, mission.Company);
        _factions.ClearFactions(grid);
        _factions.AddFaction(grid, mission.Faction);
        foreach (var uid in Members(grid, mission.Group))
            ApplyMission(uid, grid, mission);
        return true;
    }

    /// <summary>Reissues a validated mission without changing membership or issuing new credentials.</summary>
    private void ApplySettings(EntityUid mob, WFCrewMission mission)
    {
        if (TryComp<WFCrewComponent>(mob, out var crew))
        {
            crew.Navigation = mission.Navigation.Clone();
            crew.Battlegroup = mission.Battlegroup.Trim();
            crew.Disengage = mission.Disengage;
            crew.Skill = mission.Skill;
            crew.DisengageRange = mission.DisengageRange;
            crew.CallsForHelp = mission.CallsForHelp;
            if (_crew.HomeGrid(mob, crew) is { } home)
                EntityManager.System<WFCrewShipStatusSystem>().SetPolicy(home, mission.Disengage, mission.DisengageRange);
            EntityManager.System<WFCrewEscortSystem>().Invalidate();
        }
        if (TryComp<WFPilotDutyComponent>(mob, out var pilot))
        {
            pilot.ReactToAttacks = mission.HeaveTo;
            pilot.FaceThreats = mission.FaceThreats;
            _pilots.SetNavigation(mob, mission.Navigation);
        }
        EntityManager.System<WFCrewSecuritySystem>().Reset(mob);
        var security = EnsureComp<WFCrewSecurityComponent>(mob);
        security.Boarding = mission.BoardingResponse;
        security.Docking = mission.DockingResponse;
        ApplyCompany(mob, mission.Company);
        _factions.ClearFactions(mob);
        _factions.AddFaction(mob, mission.Faction);
        if (TryComp<WFCaptainComponent>(mob, out var captain))
            captain.HeaveTo = mission.HeaveTo;
        if (TryComp<WFRadioOperatorComponent>(mob, out var radio))
        {
            _radio.SetCallsign((mob, radio), mission.Callsign);
            radio.LocalChannel = mission.LocalChannel;
            radio.AlertChannel = mission.AlertChannel;
            // Raiders under a borrowed flag don't get on that company's channel.
            radio.FactionChannel = mission.Faction == "PirateNF" ? null : WFCrewCommsSystem.FactionChannel(mission.Company);
            radio.CallsForHelp = mission.CallsForHelp;
        }
    }

    /// <summary>Updates crew settings and issues the selected immediate flight order.</summary>
    public void ApplyMission(EntityUid mob, EntityUid grid, WFCrewMission mission)
    {
        ApplySettings(mob, mission);
        if (!TryComp<WFCrewComponent>(mob, out var crew) || crew.Duty != WFCrewDuties.Pilot)
            return;
        var destination = new EntityCoordinates(Transform(grid).MapUid!.Value, mission.Destination);
        switch (mission.Order)
        {
            case WFPilotOrder.Hold: _pilots.Hold(mob); break;
            case WFPilotOrder.GoTo: _pilots.GoTo(mob, new List<EntityCoordinates> { destination }); break;
            case WFPilotOrder.Loiter: _pilots.Loiter(mob, destination, mission.Range); break;
            case WFPilotOrder.Follow: _pilots.Follow(mob, GetEntity(mission.Target!.Value), mission.Range); break;
            case WFPilotOrder.Dock: _pilots.Dock(mob, GetEntity(mission.Target!.Value)); break;
            case WFPilotOrder.Undock: _pilots.Undock(mob); break;
        }
    }

    /// <summary>
    /// Fills null fields, trims text and rejects oversize input. Returns the Fluent key of the reason, or null if the request is usable.
    /// </summary>
    private static string? Sanitize(WFCrewSetupRequest request)
    {
        request.Vessel = request.Vessel?.Trim() ?? string.Empty;
        request.Posts ??= new List<WFCrewSetupPost>();
        request.Objectives ??= new List<WFCrewObjective>();
        request.Mission ??= new WFCrewMission();
        var mission = request.Mission;
        mission.Group = mission.Group?.Trim() ?? string.Empty;
        mission.Callsign = mission.Callsign?.Trim() ?? string.Empty;
        mission.Battlegroup = mission.Battlegroup?.Trim() ?? string.Empty;
        mission.Company ??= string.Empty;
        mission.Faction ??= string.Empty;
        mission.LocalChannel ??= string.Empty;
        mission.AlertChannel ??= string.Empty;
        if (mission.Group.Length > WFCrewLimits.MaxGroup)
            return "wf-crew-setup-group-too-long";
        if (mission.Callsign.Length > WFCrewLimits.MaxCallsign)
            return "wf-crew-setup-callsign-too-long";
        if (mission.Battlegroup.Length > WFCrewLimits.MaxBattlegroup)
            return "wf-crew-setup-battlegroup-too-long";
        if (request.Posts.Count > WFCrewLimits.MaxListItems)
            return "wf-crew-setup-too-many-posts";
        if (request.Objectives.Count > WFCrewLimits.MaxListItems)
            return "wf-crew-setup-too-many-objectives";
        foreach (var post in request.Posts)
        {
            if (post is null)
                return "wf-crew-setup-invalid";
            post.Role = post.Role?.Trim() ?? string.Empty;
            post.Loadout = post.Loadout?.Trim() ?? string.Empty;
        }
        foreach (var objective in request.Objectives)
        {
            if (objective is null || !Bounded(objective.Position))
                return "wf-crew-setup-bad-coordinates";
        }
        if (request.Action is WFCrewSetupAction.Clear or WFCrewSetupAction.Objectives or WFCrewSetupAction.AppendObjective
                or WFCrewSetupAction.Pause or WFCrewSetupAction.Resume or WFCrewSetupAction.Skip or WFCrewSetupAction.Rules
                or WFCrewSetupAction.Orders or WFCrewSetupAction.Spawn or WFCrewSetupAction.Preview
            && mission.Group.Length == 0)
            return "wf-crew-setup-group-required";
        return null;
    }

    private void OnRequest(WFCrewSetupRequest request, EntitySessionEventArgs args)
    {
        if (Handle(request, args.SenderSession) is not { } response)
            return;
        RaiseNetworkEvent(response, Filter.SinglePlayer(args.SenderSession));
        EntityManager.System<WFCrewUiDiagnosticsSystem>().Reply("crew", args.SenderSession, response.Crews.Count, response.Grid);
    }

    /// <summary>Sanitises and runs one setup request. Returns the reply, or null when the caller lacks permission.</summary>
    public WFCrewSetupResponse? Handle(WFCrewSetupRequest request, ICommonSession session)
    {
        if (!_admins.HasAdminFlag(session, AdminFlags.Spawn))
            return null;
        var response = new WFCrewSetupResponse { RequestId = request.RequestId, Action = request.Action, Grid = request.Grid };
        var refreshed = false;
        if (!Enum.IsDefined(request.Action))
            response.Message = Loc.GetString("wf-crew-setup-invalid");
        else if (Sanitize(request) is { } rejection)
            response.Message = Loc.GetString(rejection);
        else
            refreshed = Run(request, session, response);
        if (!refreshed)
            response.Crews = _objectives.Snapshot();
        return response;
    }

    /// <summary>Runs a sanitised request into the response. Returns true when the response already carries a fresh crew snapshot.</summary>
    private bool Run(WFCrewSetupRequest request, ICommonSession session, WFCrewSetupResponse response)
    {
        var action = request.Action;
        var group = request.Mission.Group;
        if (action == WFCrewSetupAction.Crews)
            return false;
        if (action == WFCrewSetupAction.List)
        {
            var query = EntityQueryEnumerator<MapGridComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out var map, out var meta))
            {
                var box = map.LocalAABB;
                response.Grids.Add(new WFCrewSetupGrid(GetNetEntity(uid), meta.EntityName, box.Width * box.Height > WFCrewLimits.MaxPlanArea));
            }
            var current = session.AttachedEntity is { } player ? Transform(player).GridUid : null;
            response.Grids = response.Grids.OrderBy(info => GetEntity(info.Id) == current ? 0 : 1).ThenBy(info => info.Name).ToList();
            return false;
        }
        if (action == WFCrewSetupAction.SpawnVessel)
        {
            if (session.AttachedEntity is { } actor && _prototypes.TryIndex<VesselPrototype>(request.Vessel, out var vessel)
                && !vessel.Abstract && Transform(actor).MapID != MapId.Nullspace
                && _vessels.TrySpawnVessel(vessel, Transform(actor).MapID, _transform.GetWorldPosition(actor), actor, out var created))
                response.Grid = GetNetEntity(created.Value);
            else
                response.Message = Loc.GetString("wf-crew-setup-invalid");
            return false;
        }
        if (request.Grid is not { } net || !TryGetEntity(net, out var found) || found is not { } grid || !HasComp<MapGridComponent>(grid))
        {
            response.Message = Loc.GetString("wf-crew-setup-invalid");
            return false;
        }

        if (action == WFCrewSetupAction.Plan)
        {
            if (IsTooLarge(grid))
            {
                response.Message = Loc.GetString("wf-crew-setup-grid-too-large");
                return false;
            }
            response.Posts = Plan(grid, request.Deckhands, request.Captain, out var total);
            if (response.Posts.Count == 0)
                response.Message = Loc.GetString("wf-crew-setup-no-safe-posts");
            else if (total > response.Posts.Count)
                response.Message = Loc.GetString("wf-crew-setup-plan-capped", ("shown", response.Posts.Count), ("total", total));
        }
        else if (action == WFCrewSetupAction.Clear)
        {
            var count = ClearCrew(grid, group);
            if (count == 0)
                response.Message = Loc.GetString("wf-crew-setup-no-crew");
            else
                Audit(session, grid, group, $"cleared {count} crew members from");
        }
        else if (action is WFCrewSetupAction.Objectives or WFCrewSetupAction.AppendObjective)
        {
            if (!_objectives.SetQueue(grid, group, request.Objectives, action == WFCrewSetupAction.AppendObjective))
                response.Message = Loc.GetString("wf-crew-setup-invalid");
            else
                Audit(session, grid, group, $"{(action == WFCrewSetupAction.AppendObjective ? "appended" : "replaced")} {request.Objectives.Count} objectives for");
        }
        else if (action is WFCrewSetupAction.Pause or WFCrewSetupAction.Resume or WFCrewSetupAction.Skip)
        {
            var queued = _objectives.Snapshot().FirstOrDefault(row => row.Grid == net && row.Group == group)?.Objectives.Count > 0;
            if (!queued)
            {
                response.Message = Loc.GetString("wf-crew-setup-no-queue");
                return false;
            }
            _objectives.Control(grid, group, action);
            var verb = action switch
            {
                WFCrewSetupAction.Pause => "paused the objective queue of",
                WFCrewSetupAction.Resume => "resumed the objective queue of",
                _ => "skipped the current objective of",
            };
            Audit(session, grid, group, verb);
            response.Crews = _objectives.Snapshot();
            return true;
        }
        else if (action == WFCrewSetupAction.Rules)
        {
            request.Mission.Order = WFPilotOrder.Hold;
            var members = Members(grid, group);
            if (members.Count == 0)
                response.Message = Loc.GetString("wf-crew-setup-no-crew");
            else if (!ValidateMission(grid, request.Mission))
                response.Message = Loc.GetString("wf-crew-setup-invalid");
            else
            {
                ApplyCompany(grid, request.Mission.Company);
                _factions.ClearFactions(grid);
                _factions.AddFaction(grid, request.Mission.Faction);
                foreach (var member in members)
                    ApplySettings(member, request.Mission);
                Audit(session, grid, group, "changed the rules of");
            }
        }
        else if (action == WFCrewSetupAction.Orders)
        {
            var members = Members(grid, group);
            if (members.Count == 0)
                response.Message = Loc.GetString("wf-crew-setup-no-crew");
            else if (!members.Any(member => TryComp<WFCrewComponent>(member, out var crew) && crew.Duty == WFCrewDuties.Pilot && _mobs.IsAlive(member)))
                response.Message = Loc.GetString("wf-crew-setup-no-pilot");
            else if (!TryApplyMission(grid, request.Mission))
                response.Message = Loc.GetString("wf-crew-setup-invalid");
            else
                Audit(session, grid, group, $"gave {request.Mission.Order} orders to");
        }
        else if (action is WFCrewSetupAction.Spawn && IsTooLarge(grid))
            response.Message = Loc.GetString("wf-crew-setup-grid-too-large");
        else if (!Validate(grid, request.Posts, request.Mission))
            response.Message = Loc.GetString("wf-crew-setup-invalid");
        else if (action == WFCrewSetupAction.Spawn)
        {
            TrySpawn(grid, request.Posts, request.Mission, out var spawned);
            response.Message = Loc.GetString("wf-crew-setup-spawned", ("count", spawned.Count));
            Audit(session, grid, group, $"spawned {spawned.Count} crew members on");
        }
        else if (action == WFCrewSetupAction.Preview)
            response.Posts = request.Posts;
        else if (action == WFCrewSetupAction.Teleport)
        {
            if (!_admins.HasAdminFlag(session, AdminFlags.Admin))
                response.Message = Loc.GetString("wf-crew-setup-teleport-denied");
            else if (session.AttachedEntity is not { } actor)
                response.Message = Loc.GetString("wf-crew-setup-teleport-no-body");
            else
            {
                _transform.SetCoordinates(actor, new EntityCoordinates(grid, request.Posts[0].Position));
                _adminLog.Add(LogType.Teleport, LogImpact.Medium,
                    $"{session.Name} teleported to a crew post on {ToPrettyString(grid):entity} from the crew setup window");
            }
        }
        return false;
    }

    private void Audit(ICommonSession session, EntityUid grid, string group, string what)
    {
        _adminLog.Add(LogType.AdminCommands, LogImpact.Medium,
            $"{session.Name} {what} crew group {group} on {ToPrettyString(grid):entity}");
    }
}
