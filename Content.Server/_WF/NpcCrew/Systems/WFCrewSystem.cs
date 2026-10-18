using Content.Server._WF.NpcCrew.Components;
using Content.Server.Humanoid.Systems;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Components;
using Content.Shared.Damage;
using Content.Shared.Access.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Roles;
using Content.Server.NPC.Systems;
using Content.Server.Medical.SuitSensors;
using Content.Server.Radiation.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.SSDIndicator;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Spawns crew, keeps the HTN blackboard in step with <see cref="WFCrewComponent"/>, puts role titles on names and
/// reports crew going down.
/// </summary>
public sealed class WFCrewSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private WFCrewAccessSystem _crewAccess = default!;
    [Dependency] private NPCRetaliationSystem _retaliation = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SuitSensorSystem _suitSensors = default!;
    [Dependency] private InventorySystem _inventory = default!;

    private readonly List<EntityUid> _expired = new();
    private readonly List<EntityUid> _kept = new();
    private TimeSpan _nextKeep;

    /// <summary>How long an attacker stays remembered after he was last aboard and in the crew's sight or knowledge.</summary>
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(30);

    /// <summary>How long a blow is kept in <see cref="WFCrewComponent.Struck"/>.</summary>
    private static readonly TimeSpan StruckKept = TimeSpan.FromSeconds(30);

    /// <summary>Blackboard key holding the duty name; selects the duty branch of the crew HTN root.</summary>
    public const string DutyKey = "WFCrewDuty";

    /// <summary>Blackboard key holding the post coordinates.</summary>
    public const string PostKey = "WFCrewPost";

    /// <summary>Blackboard key holding how close to the post counts as being there.</summary>
    public const string PostRangeKey = "WFCrewPostRange";

    public override void Initialize()
    {
        base.Initialize();

        // After the random name so the title goes in front of the final name. Every map-init subscription a system
        // makes must share one ordering, so the spawn point carries the same constraint.
        SubscribeLocalEvent<WFCrewComponent, MapInitEvent>(OnCrewMapInit, after: [typeof(RandomHumanoidAppearanceSystem)]);
        SubscribeLocalEvent<WFCrewSpawnPointComponent, MapInitEvent>(OnSpawnPointMapInit, after: [typeof(RandomHumanoidAppearanceSystem)]);
        SubscribeLocalEvent<WFCrewComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<WFCrewComponent, WFStripAttemptEvent>(OnGettingStripped);
        SubscribeLocalEvent<NPCRetaliationComponent, BeforeDamageChangedEvent>(OnBeforeCrewDamage,
            before: [typeof(Content.Shared._Onyx.Wounds.WoundDamageRoutingSystem)]);
        // Upstream de-aggroes expired memories every tick without removing them; prune right after it runs.
        UpdatesAfter.Add(typeof(NPCRetaliationSystem));
    }

    /// <summary>The grid a crewman serves: its post's grid, or the grid it stands on without a post.</summary>
    public EntityUid? HomeGrid(EntityUid uid, WFCrewComponent? crew = null)
    {
        if (!Resolve(uid, ref crew, false))
            return null;
        return crew.Post?.EntityId ?? Transform(uid).GridUid;
    }

    /// <summary>One crew is the same group label serving the same home grid.</summary>
    public bool SameCrew(EntityUid first, EntityUid second)
    {
        return TryComp<WFCrewComponent>(first, out var a) && TryComp<WFCrewComponent>(second, out var b)
            && a.Group == b.Group && HomeGrid(first, a) is { } home && HomeGrid(second, b) == home;
    }

    /// <summary>
    /// The mob behind a damage origin. Hitscan names the gun as the origin, so a carried gun resolves to whoever
    /// holds it; mounted guns and everything else come back unchanged.
    /// </summary>
    public EntityUid Wielder(EntityUid origin)
    {
        if (TerminatingOrDeleted(origin) || HasComp<MobStateComponent>(origin)
            || !HasComp<Content.Shared.Weapons.Ranged.Components.GunComponent>(origin))
            return origin;

        var holder = Transform(origin).ParentUid;
        return HasComp<MobStateComponent>(holder) ? holder : origin;
    }

    /// <summary>Records crew attacks before body-part routing loses the damage origin.</summary>
    private void OnBeforeCrewDamage(Entity<NPCRetaliationComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !TryComp<WFCrewComponent>(ent.Owner, out var crew)
            || !args.Damage.AnyPositive()
            || args.Origin is not { } source
            || source == ent.Owner
            || TerminatingOrDeleted(source))
        {
            return;
        }

        var attacker = Wielder(source);
        // A mounted gun or a blast is not somebody to go after; the ship it came from is answered as a ship.
        if (attacker == ent.Owner || !HasComp<MobStateComponent>(attacker))
            return;

        if (!_retaliation.TryRetaliate(ent, attacker))
        {
            if (!HasComp<MobStateComponent>(attacker) || SameCrew(ent, attacker))
                return;

            // Upstream never remembers a friendly-faction attacker; crew still answer and stop protecting it.
            Remember(ent.Owner, ent.Comp, attacker);
        }

        if (SameCrew(ent, attacker))
            return;

        crew.Struck[attacker] = _timing.CurTime;
        if (HomeGrid(ent.Owner, crew) is { } home)
            AnswerAttack(home, crew.Group, attacker);
    }

    /// <summary>
    /// Going through a living crewman's pockets is an attack on him and his crew. A thief nobody notices gets away
    /// with it, and the dead and the downed can't object.
    /// </summary>
    private void OnGettingStripped(Entity<WFCrewComponent> ent, ref WFStripAttemptEvent args)
    {
        var thief = args.User;
        if (args.Stealth || thief == ent.Owner || !_mobs.IsAlive(ent) || HasComp<ActorComponent>(ent)
            || !HasComp<MobStateComponent>(thief) || SameCrew(ent, thief))
            return;

        if (TryComp<NPCRetaliationComponent>(ent, out var retaliation))
            Remember(ent, retaliation, thief);

        ent.Comp.Struck[thief] = _timing.CurTime;
        if (HomeGrid(ent, ent.Comp) is { } home)
            AnswerAttack(home, ent.Comp.Group, thief);
    }

    /// <summary>Makes a crewman hostile to an attacker and remembers the attack for his memory's length.</summary>
    private void Remember(EntityUid uid, NPCRetaliationComponent retaliation, EntityUid attacker)
    {
        _factions.AggroEntity(uid, attacker);
        if (retaliation.AttackMemoryLength is not { } length)
            return;

        var memories = retaliation.AttackMemories;
        var until = _timing.CurTime + length;
        if (!memories.TryGetValue(attacker, out var known) || known < until)
            memories[attacker] = until;
    }

    /// <summary>
    /// Whether a crewman takes on whoever attacks his crew or will not leave his ship: everyone but the cowards, who
    /// take shelter instead, and the hands at the helm and the guns, which stay manned.
    /// </summary>
    public static bool IsFighter(WFCrewComponent crew)
    {
        return crew.Engagement != WFCrewEngagement.Never
               && crew.Duty != WFCrewDuties.Pilot && crew.Duty != WFCrewDuties.Gunnery;
    }

    /// <summary>
    /// An attack on one crewman, or a stranger who would not leave, is the whole crew's business: its fighters
    /// remember him as their own attacker and its non-combatants take shelter.
    /// </summary>
    public void AnswerAttack(EntityUid grid, string group, EntityUid attacker)
    {
        if (TerminatingOrDeleted(attacker) || !HasComp<MobStateComponent>(attacker))
            return;

        var query = EntityQueryEnumerator<WFCrewComponent, NPCRetaliationComponent>();
        while (query.MoveNext(out var uid, out var member, out var retaliation))
        {
            if (uid == attacker || member.Group != group || !IsFighter(member) || HomeGrid(uid, member) != grid
                || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid) || SameCrew(uid, attacker))
                continue;

            Remember(uid, retaliation, attacker);
        }

        EntityManager.System<WFCrewShelterSystem>().Shelter(grid, group);
        if (!_wanted.TryGetValue((grid, group), out var wanted))
            _wanted[(grid, group)] = wanted = new Dictionary<EntityUid, TimeSpan>();
        wanted[attacker] = _timing.CurTime + WantedTime;
        ReportShip(grid, group, attacker);
    }

    /// <summary>How long after his last attack on a crew an attacker's ship is taken for an enemy vessel.</summary>
    public static readonly TimeSpan WantedTime = TimeSpan.FromMinutes(3);

    private readonly Dictionary<(EntityUid Grid, string Group), Dictionary<EntityUid, TimeSpan>> _wanted = new();
    private readonly List<(EntityUid Grid, string Group)> _settled = new();

    /// <summary>
    /// The ship a crew's attacker is aboard has attacked the crew's ship as surely as if it had fired on it. For an
    /// attacker with a crew of his own that is his own ship, wherever he stands.
    /// </summary>
    private void ReportShip(EntityUid grid, string group, EntityUid attacker)
    {
        var ship = TryComp<WFCrewComponent>(attacker, out var crew) && !HasComp<ActorComponent>(attacker)
            ? HomeGrid(attacker, crew)
            : Transform(attacker).GridUid;
        if (ship is { } vessel && vessel != grid)
            EntityManager.System<WFCrewAlertSystem>().ReportShipThreat(grid, group, vessel);
    }

    /// <summary>Keeps the ships of a crew's recent attackers reported, and forgets attackers whose time is up.</summary>
    private void UpdateWanted(TimeSpan now)
    {
        _settled.Clear();
        foreach (var (key, wanted) in _wanted)
        {
            _expired.Clear();
            foreach (var (attacker, until) in wanted)
            {
                if (now >= until || TerminatingOrDeleted(attacker) || !_mobs.IsAlive(attacker) || TerminatingOrDeleted(key.Grid))
                    _expired.Add(attacker);
                else
                    ReportShip(key.Grid, key.Group, attacker);
            }
            foreach (var attacker in _expired)
                wanted.Remove(attacker);
            if (wanted.Count == 0)
                _settled.Add(key);
        }
        foreach (var key in _settled)
            _wanted.Remove(key);
    }

    /// <summary>Whether an attacker is still aboard the crewman's ship and in his sight or his crew's knowledge.</summary>
    private bool StillAboard(EntityUid uid, WFCrewComponent crew, EntityUid attacker)
    {
        if (TerminatingOrDeleted(attacker) || !_mobs.IsAlive(attacker) || HomeGrid(uid, crew) is not { } home
            || Transform(attacker).GridUid != home)
            return false;

        return EntityManager.System<WFCrewWeaponSystem>().CanSee(uid, attacker)
               || EntityManager.System<WFCrewCommsSystem>().Knows(uid, attacker);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        // Once a second, memories of attackers still aboard and known are kept from lapsing.
        var keep = now >= _nextKeep;
        if (keep)
        {
            _nextKeep = now + TimeSpan.FromSeconds(1);
            UpdateWanted(now);
        }
        var query = EntityQueryEnumerator<WFCrewComponent, NPCRetaliationComponent>();
        while (query.MoveNext(out var uid, out var crew, out var retaliation))
        {
            if (keep && crew.Struck.Count > 0)
            {
                _expired.Clear();
                foreach (var (attacker, at) in crew.Struck)
                {
                    if (now >= at + StruckKept || TerminatingOrDeleted(attacker))
                        _expired.Add(attacker);
                }
                foreach (var attacker in _expired)
                    crew.Struck.Remove(attacker);
            }

            var memories = retaliation.AttackMemories;
            if (memories.Count == 0)
                continue;
            if (keep && _mobs.IsAlive(uid))
            {
                _kept.Clear();
                foreach (var (attacker, until) in memories)
                {
                    if (now < until && until < now + Linger && StillAboard(uid, crew, attacker))
                        _kept.Add(attacker);
                }
                foreach (var attacker in _kept)
                    memories[attacker] = now + Linger;
            }
            _expired.Clear();
            foreach (var (attacker, until) in memories)
            {
                if (now >= until || TerminatingOrDeleted(attacker))
                    _expired.Add(attacker);
            }
            foreach (var attacker in _expired)
            {
                memories.Remove(attacker);
                // Upstream just dropped this hostility; restore it where a boarding rule or shared alert still owns it.
                if (!TerminatingOrDeleted(attacker)
                    && (EntityManager.System<WFCrewSecuritySystem>().IsHostileVisitor(uid, attacker)
                        || EntityManager.System<WFCrewAlertSystem>().SharesHostile(uid, attacker)))
                    _factions.AggroEntity(uid, attacker);
            }
        }
    }

    private void OnCrewMapInit(EntityUid uid, WFCrewComponent component, MapInitEvent args)
    {
        Apply((uid, component));
    }

    private void OnMobStateChanged(EntityUid uid, WFCrewComponent component, MobStateChangedEvent args)
    {
        EntityManager.System<WFCrewEscortSystem>().Invalidate();
        if (args.NewMobState == MobState.Alive)
            return;

        EntityManager.System<WFCrewWorkSystem>().CancelWorker(uid);
        if (args.NewMobState == MobState.Dead && HasComp<WFCrewRepairComponent>(uid))
        {
            RemComp<Content.Shared._Mono.ShipRepair.Components.ShipRepairToolComponent>(uid);
            RemComp<Content.Shared.Tools.Components.ToolComponent>(uid);
            RemComp<WFCrewRepairComponent>(uid);
        }

        var ev = new WFCrewMemberDownEvent(uid, component.Group, component.Role, args.NewMobState == MobState.Dead);
        RaiseLocalEvent(uid, ref ev, true);
    }

    private void OnSpawnPointMapInit(EntityUid uid, WFCrewSpawnPointComponent component, MapInitEvent args)
    {
        if (component.Spawned != null)
            return;

        component.Spawned = SpawnCrewman(component.Role, Transform(uid).Coordinates, component.Group);
    }

    /// <summary>Spawns one crewman of a role with its post at the coordinates. Null when the role is unknown.</summary>
    public EntityUid? SpawnCrewman(ProtoId<WFCrewRolePrototype> roleId, EntityCoordinates post, string group,
        ProtoId<StartingGearPrototype>? loadout = null, EntProtoId? body = null, string? name = null,
        ProtoId<WFCrewProfilePrototype>? profile = null)
    {
        if (!_prototypes.TryIndex(roleId, out var role))
        {
            Log.Error($"Unknown crew role {roleId}");
            return null;
        }

        if (loadout is { } gear && !_prototypes.HasIndex(gear))
            return null;
        EntityUid uid;
        if (body != null && !_prototypes.HasIndex<EntityPrototype>(body.Value))
            return null;
        if (loadout != null || body != null)
        {
            uid = EntityManager.CreateEntityUninitialized(body ?? role.Mob, post);
            // A profile's body is bare: the role's own parts come from its kit.
            if (body != null && role.Kit is { } kit && _prototypes.TryIndex<EntityPrototype>(kit, out var parts))
                EntityManager.AddComponents(uid, parts.Components);
            if (loadout is { } selected)
                EnsureComp<LoadoutComponent>(uid).StartingGear = new List<ProtoId<StartingGearPrototype>> { selected };
            EntityManager.InitializeAndStartEntity(uid);
        }
        else
            uid = Spawn(role.Mob, post);
        EntityManager.AddComponents(uid, role.Components);
        var crew = EnsureComp<WFCrewComponent>(uid);
        crew.Role = roleId;
        crew.Profile = profile;
        crew.Duty = role.Duty;
        crew.Engagement = role.Engagement;
        crew.Group = group;
        crew.Post = post;
        // A name given here replaces the body's before the title goes on and the card and records are written.
        if (name != null)
            _meta.SetEntityName(uid, name);
        // A post in vacuum, such as an open cockpit, is manned in a pressure suit whose tank never empties. A body
        // built immune, such as an automaton, needs none.
        if (post.EntityId is { Valid: true } deck && !EntityManager.System<WFCrewEvaSystem>().IsImmune(uid)
            && TryComp<Robust.Shared.Map.Components.MapGridComponent>(deck, out var deckGrid)
            && !EntityManager.System<WFCrewPlannerSystem>().IsSafePost(deck,
                EntityManager.System<SharedMapSystem>().TileIndicesFor(deck, deckGrid, post), deckGrid))
            EntityManager.System<WFCrewEvaSystem>().GiveVoidKit(uid);
        Apply((uid, crew));
        _crewAccess.RegisterSpawnShip(uid);
        if (Transform(uid).GridUid is { } ship && !HasComp<Content.Shared._Mono.ShipRepair.Components.ShipRepairDataComponent>(ship))
            EntityManager.System<Content.Server._Mono.ShipRepair.ShipRepairSystem>().GenerateRepairData(ship);
        return uid;
    }

    /// <summary>Spawns every post of a plan as one crew. Returns the crew spawned.</summary>
    public List<EntityUid> SpawnCrew(IEnumerable<WFCrewPost> plan, string group)
    {
        var crew = new List<EntityUid>();
        foreach (var post in plan)
        {
            if (SpawnCrewman(post.Role, post.Coordinates, group) is { } uid)
                crew.Add(uid);
        }

        return crew;
    }

    /// <summary>Deletes every crewman of a group. Returns how many.</summary>
    public int ClearGroup(string group)
    {
        var count = 0;
        var query = EntityQueryEnumerator<WFCrewComponent>();
        while (query.MoveNext(out var uid, out var crew))
        {
            if (crew.Group != group)
                continue;

            QueueDel(uid);
            count++;
        }

        return count;
    }

    public void SetDuty(Entity<WFCrewComponent?> ent, string duty)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.Duty = duty;
        Apply((ent, ent.Comp));
    }

    public void SetPost(Entity<WFCrewComponent?> ent, EntityCoordinates? post)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.Post = post;
        Apply((ent, ent.Comp));
    }

    public void SetEngagement(Entity<WFCrewComponent?> ent, WFCrewEngagement engagement)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.Engagement = engagement;
        Apply((ent, ent.Comp));
    }

    /// <summary>Writes the component to the blackboard, replans, and titles the name once.</summary>
    public void Apply(Entity<WFCrewComponent> ent)
    {
        var (uid, crew) = ent;
        EnsureComp<AccessComponent>(uid);
        // Nobody plays an NPC, so its body never shows the disconnected-player sleep icon, alive or dead.
        RemComp<SSDIndicatorComponent>(uid);
        // Crews work and fight beside reactors with nothing but their jumpsuits; radiation passes them by.
        RemComp<RadiationReceiverComponent>(uid);
        // Their suit sensors are switched off, so a crew monitor shows players and not a sector's worth of NPCs. A
        // player who takes the suit can switch them back on.
        foreach (var worn in _inventory.GetHandOrInventoryEntities(uid))
        {
            if (TryComp<SuitSensorComponent>(worn, out var sensor) && sensor.Mode != SuitSensorMode.SensorOff)
                _suitSensors.SetSensor((worn, sensor), SuitSensorMode.SensorOff);
        }
        EntityManager.System<WFCrewEscortSystem>().Invalidate();

        if (TryComp<HTNComponent>(uid, out var htn))
        {
            _npc.SetBlackboard(uid, "NavAccess", true, htn);
            _npc.SetBlackboard(uid, "NavShove", true, htn);
            _npc.SetBlackboard(uid, DutyKey, crew.Duty, htn);
            _npc.SetBlackboard(uid, PostRangeKey, crew.PostRange, htn);
            if (crew.Post is { } post)
                _npc.SetBlackboard(uid, PostKey, post, htn);
            else
                htn.Blackboard.Remove<EntityCoordinates>(PostKey);

            _htn.Replan(htn);
        }

        if (crew.Titled || Title((uid, crew)) is not { } title)
            return;

        _meta.SetEntityName(uid, Loc.GetString("wf-crew-name-format",
            ("title", title),
            ("name", Name(uid))));
        crew.Titled = true;
    }

    /// <summary>A crewman's title: the profile's for the role when it names one, else the role's own; null without a role.</summary>
    public string? Title(Entity<WFCrewComponent> ent)
    {
        var crew = ent.Comp;
        if (crew.Role is not { } roleId || !_prototypes.TryIndex(roleId, out var role))
            return null;

        if (crew.Profile is { } profileId && _prototypes.TryIndex(profileId, out var profile) && profile.Titles.TryGetValue(roleId, out var custom))
            return Loc.GetString(custom);

        return Loc.GetString(role.Title);
    }
}
