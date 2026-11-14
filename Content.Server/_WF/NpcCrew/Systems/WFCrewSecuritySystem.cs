using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._Mono.Company;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.PAI;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Checks boarding and docking authorization independently of nearby players or radio operators.</summary>
public sealed partial class WFCrewSecuritySystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private Robust.Shared.Random.IRobustRandom _random = default!;
    [Dependency] private WFCrewSystem _crews = default!;
    [Dependency] private WFCrewWeaponSystem _weapons = default!;
    [Dependency] private WFCrewCommsSystem _comms = default!;
    private float _timer;

    // Scratch collections for the 1 Hz sweep, cleared at the start of each pass.
    private readonly HashSet<(EntityUid Crew, EntityUid Visitor)> _present = new();
    private readonly HashSet<(EntityUid Grid, string Group, EntityUid Visitor)> _strangers = new();
    private readonly HashSet<(EntityUid Grid, string Group, EntityUid Visitor)> _seen = new();
    private readonly List<(EntityUid Uid, EntityUid Grid, string Group, WFCrewSecurityComponent Rules)> _guards = new();
    private readonly Dictionary<EntityUid, List<EntityUid>> _people = new();
    private readonly Stack<List<EntityUid>> _peopleLists = new();
    private readonly List<KeyValuePair<(EntityUid Grid, string Group, EntityUid Visitor), TimeSpan>> _dockScratch = new();
    private readonly List<(EntityUid Crew, EntityUid Visitor)> _staleHostiles = new();
    private readonly HashSet<(EntityUid Crew, EntityUid Visitor)> _boarders = new();
    private readonly HashSet<(EntityUid Crew, EntityUid Visitor)> _ownedHostiles = new();
    private readonly Dictionary<(EntityUid Grid, string Group, EntityUid Visitor), TimeSpan> _hostileDocks = new();

    /// <summary>Strangers a Warn crew has noticed aboard: when their time to leave runs out, and whether it has.</summary>
    private readonly Dictionary<(EntityUid Grid, string Group, EntityUid Visitor), (TimeSpan Deadline, bool Turned)> _warned = new();

    /// <summary>Whether a warned stranger stayed past his time and the crew's fighters turned on him.</summary>
    public bool OverstayedWarning(EntityUid grid, string group, EntityUid visitor) =>
        _warned.TryGetValue((grid, group, visitor), out var warning) && warning.Turned;

    /// <summary>Whether the crew member has a visitor explicitly marked hostile by its boarding rule.</summary>
    public bool HasThreat(EntityUid crew) => _ownedHostiles.Any(pair => pair.Crew == crew);

    /// <summary>Whether the current security rule explicitly targets this visitor.</summary>
    public bool IsHostileVisitor(EntityUid crew, EntityUid visitor) => _ownedHostiles.Contains((crew, visitor));

    /// <summary>Explicit docking hostility overrides faction friendship for the current ship threat.</summary>
    public bool IsHostileDockingTarget(EntityUid grid, string group, EntityUid visitor) =>
        _hostileDocks.TryGetValue((grid, group, visitor), out var until) && _timing.CurTime < until;

    /// <summary>Living physical visitors can board; portable AI minds and ship AI infrastructure cannot.</summary>
    public bool IsBoardingCandidate(EntityUid visitor) => IsBoardingCandidate(visitor, null);

    private bool IsBoardingCandidate(EntityUid visitor, HashSet<EntityUid>? aiRemotes)
    {
        // AI devices use MobState for possession even though they are equipment, not boarding bodies.
        if (TerminatingOrDeleted(visitor) || !HasComp<MobStateComponent>(visitor) || !_mobs.IsAlive(visitor)
            || HasComp<PAIComponent>(visitor) || HasComp<BorgBrainComponent>(visitor)
            || HasComp<StationAiHeldComponent>(visitor) || HasComp<StationAiCoreComponent>(visitor))
            return false;
        return !(aiRemotes ?? AiRemotes()).Contains(visitor);
    }

    /// <summary>Every station AI core's remote body.</summary>
    private HashSet<EntityUid> AiRemotes()
    {
        var remotes = new HashSet<EntityUid>();
        var cores = EntityQueryEnumerator<StationAiCoreComponent>();
        while (cores.MoveNext(out _, out var core))
        {
            if (core.RemoteEntity is { } remote)
                remotes.Add(remote);
        }
        return remotes;
    }

    /// <summary>Acts on a personally observed or received contact, preserving the crew's boarding policy.</summary>
    public void ReceiveSighting(EntityUid uid, EntityUid visitor) => ReceiveSighting(uid, visitor, null);

    private void ReceiveSighting(EntityUid uid, EntityUid visitor, HashSet<EntityUid>? aiRemotes)
    {
        if (!TryComp<WFCrewComponent>(uid, out var crew) || !TryComp<WFCrewSecurityComponent>(uid, out var rules)
            || !_mobs.IsAlive(uid) || !IsBoardingCandidate(visitor, aiRemotes) || IsAuthorized(uid, visitor)
            || Transform(uid).GridUid is not { } grid
            || crew.Post is { } post && post.EntityId != grid || Transform(visitor).GridUid != grid)
            return;
        if (rules.Boarding == WFCrewSecurityResponse.Hostile)
        {
            if (!TryComp<FactionExceptionComponent>(uid, out var exceptions) || !exceptions.Hostiles.Any(target => target == visitor))
                _ownedHostiles.Add((uid, visitor));
            _factions.AggroEntity(uid, visitor);
        }
        // A person told to leave has a while to go before the crew's fighters turn on him; animals are let be.
        if (rules.Boarding == WFCrewSecurityResponse.Warn
            && (HasComp<ActorComponent>(visitor) || HasComp<HumanoidAppearanceComponent>(visitor)))
        {
            _warned.TryAdd((grid, crew.Group, visitor), (_timing.CurTime + rules.WarnTime, false));
        }
        // The crew's cowards don't wait to see how it turns out: a stranger seen aboard is alarm enough, for as long
        // as he is seen.
        if (rules.Boarding != WFCrewSecurityResponse.Ignore
            && (HasComp<ActorComponent>(visitor) || HasComp<HumanoidAppearanceComponent>(visitor)))
            EntityManager.System<WFCrewShelterSystem>().Shelter(grid, crew.Group);
        if (_boarders.Add((uid, visitor)) && HasComp<WFRadioOperatorComponent>(uid))
            Respond(grid, crew.Group, visitor, rules.Boarding, docking: false);
    }

    /// <summary>Re-evaluates visitors after a policy edit without retaining hostility introduced by the old rule.</summary>
    public void Reset(EntityUid crew)
    {
        foreach (var pair in _ownedHostiles.Where(pair => pair.Crew == crew).ToArray())
        {
            _factions.DeAggroEntity(crew, pair.Visitor);
            _ownedHostiles.Remove(pair);
        }
        _boarders.RemoveWhere(pair => pair.Crew == crew);
        if (TryComp<WFCrewComponent>(crew, out var member) && Transform(crew).GridUid is { } grid)
        {
            foreach (var key in _hostileDocks.Keys.Where(key => key.Grid == grid && key.Group == member.Group).ToArray())
                _hostileDocks.Remove(key);
            foreach (var key in _warned.Keys.Where(key => key.Grid == grid && key.Group == member.Group).ToArray())
                _warned.Remove(key);
            // The gunners keep their chosen target between selections; a docking threat withdrawn here is dropped at once.
            EntityManager.System<WFGunnerDutySystem>().Reselect(grid, member.Group);
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DockEvent>(OnDock);
        _timer = _random.NextFloat(); // random phase so the 1 Hz crew sweeps don't share a tick
    }

    /// <summary>Orders to dock authorize that destination, not other ships arriving at this crew.</summary>
    public bool IsOutboundDock(EntityUid grid, string group, EntityUid destination)
    {
        var query = EntityQueryEnumerator<WFCrewComponent, WFPilotDutyComponent, TransformComponent>();
        while (query.MoveNext(out _, out var crew, out var pilot, out var xform))
        {
            if (xform.GridUid == grid && crew.Group == group && pilot.Orders == WFPilotOrder.Dock
                && pilot.DockTarget == destination)
                return true;
        }
        return false;
    }

    /// <summary>Authorization requires a company of the same faction or an overlapping NPC faction.</summary>
    public bool IsAuthorized(EntityUid crew, EntityUid other)
    {
        if (TryComp<CompanyComponent>(crew, out var company) && company.CompanyName.Id is not ("" or "None")
            && TryComp<CompanyComponent>(other, out var visitorCompany))
            return WFCompanyFamily.Same(company.CompanyName, visitorCompany.CompanyName);
        return TryComp<NpcFactionMemberComponent>(crew, out var faction)
            && TryComp<NpcFactionMemberComponent>(other, out var visitorFaction)
            && faction.Factions.Intersect(visitorFaction.Factions).Any();
    }

    private void OnDock(DockEvent args)
    {
        Arrived(args.GridAUid, args.GridBUid);
        Arrived(args.GridBUid, args.GridAUid);
    }

    private void Arrived(EntityUid grid, EntityUid visitor)
    {
        var reported = new HashSet<string>();
        var query = EntityQueryEnumerator<WFCrewComponent, WFCrewSecurityComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var rules, out var xform))
        {
            if (xform.GridUid != grid || HasComp<ActorComponent>(uid) || !_mobs.IsAlive(uid)
                || IsAuthorized(uid, visitor) || IsOutboundDock(grid, crew.Group, visitor))
                continue;
            if (reported.Add(crew.Group))
                Respond(grid, crew.Group, visitor, rules.Docking, docking: true);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 1f)
            return;
        _timer = 0;
        if (_hostileDocks.Count > 0)
        {
            _dockScratch.Clear();
            _dockScratch.AddRange(_hostileDocks);
            foreach (var (key, until) in _dockScratch)
            {
                if (_timing.CurTime >= until || TerminatingOrDeleted(key.Grid) || TerminatingOrDeleted(key.Visitor))
                    _hostileDocks.Remove(key);
            }
        }
        var present = _present;
        var strangers = _strangers;
        var seen = _seen;
        var guards = _guards;
        present.Clear();
        strangers.Clear();
        seen.Clear();
        guards.Clear();
        var query = EntityQueryEnumerator<WFCrewComponent, WFCrewSecurityComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var rules, out var xform))
        {
            if (xform.GridUid is not { } grid || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid)
                || crew.Post is { } post && post.EntityId != grid)
                continue;
            guards.Add((uid, grid, crew.Group, rules));
        }
        // Visitors are bucketed by grid once, and only on grids that some crew guards.
        var people = _people;
        foreach (var list in people.Values)
        {
            list.Clear();
            _peopleLists.Push(list);
        }
        people.Clear();
        foreach (var guard in guards)
        {
            if (!people.ContainsKey(guard.Grid))
                people[guard.Grid] = _peopleLists.Count > 0 ? _peopleLists.Pop() : new List<EntityUid>();
        }
        if (guards.Count > 0)
        {
            var aiRemotes = AiRemotes();
            var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
            while (mobs.MoveNext(out var person, out _, out var transform))
            {
                if (transform.GridUid is { } grid && people.TryGetValue(grid, out var aboard)
                    && IsBoardingCandidate(person, aiRemotes))
                    aboard.Add(person);
            }
            var crews = _crews;
            foreach (var (uid, grid, group, rules) in guards)
            {
                foreach (var visitor in people[grid])
                {
                    if (visitor == uid || IsAuthorized(uid, visitor) || crews.SameCrew(uid, visitor))
                        continue;
                    present.Add((uid, visitor));
                    strangers.Add((grid, group, visitor));
                    if (!_weapons.CanSee(uid, visitor))
                        continue;
                    seen.Add((grid, group, visitor));
                    ReceiveSighting(uid, visitor, aiRemotes);
                    if (rules.Boarding != WFCrewSecurityResponse.Ignore)
                        _comms.Report(uid, visitor);
                }
            }
        }
        CheckWarnings(strangers, seen);
        if (_ownedHostiles.Count > 0)
        {
            _staleHostiles.Clear();
            foreach (var pair in _ownedHostiles)
            {
                if (!present.Contains(pair))
                    _staleHostiles.Add(pair);
            }
            foreach (var pair in _staleHostiles)
            {
                if (!TerminatingOrDeleted(pair.Crew))
                    _factions.DeAggroEntity(pair.Crew, pair.Visitor);
                _ownedHostiles.Remove(pair);
            }
        }
        _boarders.RemoveWhere(pair => !present.Contains(pair));
    }

    /// <summary>
    /// Turns a crew's fighters on each warned stranger still aboard once his time runs out, and again whenever one of
    /// the crew sees him after that. Strangers who left are forgotten, so coming back starts a new warning.
    /// </summary>
    private void CheckWarnings(HashSet<(EntityUid Grid, string Group, EntityUid Visitor)> aboard,
        HashSet<(EntityUid Grid, string Group, EntityUid Visitor)> seen)
    {
        var now = _timing.CurTime;
        foreach (var (key, warning) in _warned.ToArray())
        {
            if (!aboard.Contains(key) || TerminatingOrDeleted(key.Visitor) || TerminatingOrDeleted(key.Grid))
            {
                _warned.Remove(key);
                continue;
            }

            if (now < warning.Deadline || warning.Turned && !seen.Contains(key))
                continue;

            EntityManager.System<WFCrewSystem>().AnswerAttack(key.Grid, key.Group, key.Visitor);
            if (warning.Turned)
                continue;

            _warned[key] = (warning.Deadline, true);
            Respond(key.Grid, key.Group, key.Visitor, WFCrewSecurityResponse.Hostile, docking: false);
        }
    }

    private void Respond(EntityUid grid, string group, EntityUid visitor, WFCrewSecurityResponse response, bool docking)
    {
        if (response == WFCrewSecurityResponse.Ignore)
            return;
        if (docking && response == WFCrewSecurityResponse.Hostile)
        {
            _hostileDocks[(grid, group, visitor)] = _timing.CurTime + TimeSpan.FromSeconds(60);
            _alerts.ReportDockingThreat(grid, group, visitor);
        }
        var ev = new WFCrewSecurityIncidentEvent(grid, group, visitor, response, docking);
        RaiseLocalEvent(grid, ref ev, true);
    }
}

/// <summary>One unauthorized arrival reported to the crew's radio officer.</summary>
[ByRefEvent]
public readonly record struct WFCrewSecurityIncidentEvent(EntityUid Grid, string Group, EntityUid Visitor,
    WFCrewSecurityResponse Response, bool Docking);
