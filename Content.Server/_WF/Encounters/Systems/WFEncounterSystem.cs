using System.Linq;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Chat.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Server.Radio.EntitySystems;
using Content.Server.StationEvents.Events;
using Content.Server.Worldgen.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Runs encounters: spawns each prototype's ships, loads their cargo, crews them and gives them their orders
/// through the NpcCrew module, decides when the encounter is over and removes its ships once players have left them.
/// </summary>
public sealed partial class WFEncounterSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private AdminVesselSpawnSystem _vessels = default!;
    [Dependency] private WFEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private WFCrewSetupSystem _setup = default!;
    [Dependency] private WFCrewWorkSystem _work = default!;
    [Dependency] private WFCrewObjectiveSystem _objectives = default!;
    [Dependency] private WFCrewPlannerSystem _planner = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private GridPowerSystem _power = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private Content.Server.Shuttles.Systems.DockingSystem _docking = default!;
    [Dependency] private Content.Server.Shuttles.Systems.ShuttleSystem _shuttles = default!;
    [Dependency] private LinkedLifecycleGridSystem _lifecycle = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private NPCRetaliationSystem _retaliation = default!;
    [Dependency] private NpcFactionSystem _factions = default!;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>How long after its orders are flown a transient encounter jumps out.</summary>
    private static readonly TimeSpan JumpDelay = TimeSpan.FromSeconds(20);

    private static readonly ProtoId<RadioChannelPrototype> AnnounceChannel = "Common";

    /// <summary>How long a ship must be without thrust, and out of any fight, before it calls for help.</summary>
    private static readonly TimeSpan AdriftDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DistressRepeat = TimeSpan.FromMinutes(5);

    /// <summary>How often a living crew top up their ship's batteries. Encounter ships don't burn fuel.</summary>
    private static readonly TimeSpan PowerInterval = TimeSpan.FromMinutes(2);

    private TimeSpan _nextPoll;
    private TimeSpan _heldAt;
    private float _cleanupRange;
    private TimeSpan _cleanupDelay;
    private TimeSpan _cleanupLinger;
    private readonly List<MapCoordinates> _players = new();
    private readonly HashSet<EntityUid> _playerGrids = new();
    private readonly List<Entity<WFEncounterComponent>> _running = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, EncountersCVars.CleanupRange, value => _cleanupRange = value, true);
        Subs.CVar(_config, EncountersCVars.CleanupDelay, value => _cleanupDelay = TimeSpan.FromSeconds(value), true);
        Subs.CVar(_config, EncountersCVars.CleanupLinger, value => _cleanupLinger = TimeSpan.FromSeconds(value), true);
        SubscribeLocalEvent<WFCrewAlertEvent>(OnCrewAlert);
        // Before the damage lands: Wolfmed routes a body's damage through its parts, and the body's own
        // DamageChangedEvent then has no origin.
        SubscribeLocalEvent<WFEncounterPassengerComponent, BeforeDamageChangedEvent>(OnPassengerDamaged,
            before: [typeof(Content.Shared._Onyx.Wounds.WoundDamageRoutingSystem)]);
    }

    /// <summary>A hidden encounter shows itself once one of its crews raises the alarm.</summary>
    private void OnCrewAlert(ref WFCrewAlertEvent args)
    {
        // A patrol zone's warning is not an attack: it reveals nothing and calls for no help.
        if (_alerts.InZoneReport || !TryComp<WFEncounterGridComponent>(args.Grid, out var marker))
            return;

        Reveal(marker.Encounter);
        if (TryComp<WFEncounterComponent>(marker.Encounter, out var encounter) && encounter.Ships.TryGetValue(marker.Key, out var ship))
        {
            // A ship lying in wait that is found and fired on shows itself.
            Unmask(ship);
            CallForHelp(encounter, ship);
        }
    }

    /// <summary>A passenger attacked by someone outside the crew: the ship calls for help and its guards turn on the attacker.</summary>
    private void OnPassengerDamaged(Entity<WFEncounterPassengerComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive() || args.Origin is not { } source)
            return;

        // A beam names the gun as its origin; the attacker is whoever holds it.
        var origin = _crew.Wielder(source);
        if (origin == ent.Owner || TerminatingOrDeleted(origin)
            || !TryComp<WFEncounterComponent>(ent.Comp.Encounter, out var encounter)
            || !encounter.Ships.TryGetValue(ent.Comp.Key, out var ship)
            || TryComp<WFCrewComponent>(origin, out var attacker) && attacker.Group == ship.Group)
            return;

        Reveal((ent.Comp.Encounter, encounter));
        CallForHelp(encounter, ship);
        if (HasComp<MobStateComponent>(origin))
            Defend(ship, origin);
    }

    /// <summary>Whoever attacks a passenger is the crew's attacker: its fighters take him on and its cowards take shelter.</summary>
    private void Defend(WFEncounterShipState ship, EntityUid attacker)
    {
        _crew.AnswerAttack(ship.Grid, ship.Group, attacker);
    }

    /// <summary>
    /// A ship that calls for help radios when it or its passengers are attacked, at most every few minutes. Most
    /// crews have no radio officer to do it, so the ship does, unless one already has.
    /// </summary>
    private void CallForHelp(WFEncounterComponent encounter, WFEncounterShipState ship)
    {
        var now = _timing.CurTime;
        if (!ship.Distress || encounter.Resolution != null || now < ship.NextAttackCall
            || TerminatingOrDeleted(ship.Grid) || !ship.Passengers && MaydaySent(ship))
            return;

        var position = _transform.GetMapCoordinates(ship.Grid).Position;
        if (!TrySay(ship, HelpChannel(ship), Loc.GetString("wf-encounter-distress-attacked",
                ("name", MetaData(ship.Grid).EntityName), ("x", (int) position.X), ("y", (int) position.Y))))
            return;

        ship.NextAttackCall = now + AttackCallRepeat;
        encounter.Category = WFEncounterCategory.Distress;
    }

    private static readonly TimeSpan AttackCallRepeat = TimeSpan.FromMinutes(3);

    /// <summary>Where a ship calls for help: its faction's channel if its company has one, else the common one.</summary>
    private ProtoId<RadioChannelPrototype> HelpChannel(WFEncounterShipState ship)
    {
        var company = CompOrNull<Content.Shared._Mono.Company.CompanyComponent>(ship.Grid)?.CompanyName;
        return WFCrewCommsSystem.FactionChannel(company) ?? AnnounceChannel.Id;
    }

    /// <summary>Whether the ship's own radio officer has already put its mayday on the air.</summary>
    private bool MaydaySent(WFEncounterShipState ship)
    {
        var operators = EntityQueryEnumerator<WFRadioOperatorComponent, WFCrewComponent>();
        while (operators.MoveNext(out var uid, out var radio, out var member))
        {
            if (member.Group == ship.Group && radio.MaydaySent && _mobs.IsAlive(uid))
                return true;
        }

        return false;
    }

    /// <summary>Puts a hidden encounter on the sector markers and lets it make its announcement.</summary>
    public void Reveal(Entity<WFEncounterComponent?> encounter)
    {
        if (!Resolve(encounter, ref encounter.Comp, false) || !encounter.Comp.Hidden)
            return;

        encounter.Comp.Hidden = false;
        if (encounter.Comp.Resolution == null)
            Announce(encounter.Comp);
    }

    private void Announce(WFEncounterComponent encounter)
    {
        if (encounter.Announcement is not { } text)
            return;

        encounter.Announcement = null;
        // Only the announcing ship speaks for the encounter: another, such as the pirate it names, never does.
        if (encounter.AnnounceOnRadio && encounter.Ships.TryGetValue(encounter.Announcer, out var announcer)
            && TrySay(announcer, AnnounceChannel, text))
            return;

        _chat.DispatchGlobalAnnouncement(text, encounter.AnnouncementSender, announcementSound: encounter.AnnouncementSound,
            colorOverride: encounter.AnnouncementColor);
    }

    /// <summary>
    /// Has someone aboard say something on a channel for the ship: its radio officer, else its captain, else its
    /// pilot, else any living crewman. False if nobody is left to.
    /// </summary>
    public bool TrySay(WFEncounterShipState ship, ProtoId<RadioChannelPrototype> channel, string text)
    {
        if (TerminatingOrDeleted(ship.Grid))
            return false;

        EntityUid? speaker = null;
        var rank = 0;
        var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out var member, out var xform))
        {
            if (member.Group != ship.Group || xform.GridUid != ship.Grid || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid))
                continue;

            var standing = member.Role == WFCrewRoles.RadioOperator ? 4
                : HasComp<WFRadioOperatorComponent>(uid) ? 3
                : member.Role == WFCrewRoles.Pilot ? 2
                : 1;
            if (standing <= rank)
                continue;

            rank = standing;
            speaker = uid;
        }

        if (speaker is not { } voice)
            return false;

        // Whoever makes the call is heard without a telecom server on the map, as a radio officer is.
        EnsureComp<TelecomExemptComponent>(voice);
        _radio.SendRadioMessage(voice, text, channel, voice);
        return true;
    }

    /// <summary>
    /// Encounters that have not resolved yet and count against the storyteller's cap. A persistent one, such as
    /// the patrol that stays all round, does not.
    /// </summary>
    public int ActiveCount()
    {
        var count = 0;
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out _, out var encounter))
        {
            if (encounter.Resolution == null && !encounter.OffBudget)
                count++;
        }

        return count;
    }

    /// <summary>The summed cost of the encounters <see cref="ActiveCount"/> counts.</summary>
    public int ActiveCost()
    {
        var cost = 0;
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out _, out var encounter))
        {
            if (encounter.Resolution == null && !encounter.OffBudget)
                cost += encounter.Cost;
        }

        return cost;
    }

    /// <summary>Whether an encounter of this prototype has not resolved yet.</summary>
    public bool IsRunning(string prototype)
    {
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out _, out var encounter))
        {
            if (encounter.Resolution == null && encounter.Prototype.Id == prototype)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Spawns an encounter with its origin at a point in space. <paramref name="stops"/> are the stations its
    /// placement chose: orders can aim at the first and last, and a route calls at each in turn. Fails, leaving
    /// nothing behind, when a ship cannot be loaded or crewed or its orders are invalid.
    /// </summary>
    public bool TrySpawn(WFEncounterPrototype prototype, MapCoordinates origin, out EntityUid encounter, EntityUid? spawner = null,
        IReadOnlyList<EntityUid>? stops = null, bool pinned = false, bool offBudget = false)
    {
        encounter = default;
        if (origin.MapId == MapId.Nullspace || prototype.Ships.Count == 0 || !_mapSystem.TryGetMap(origin.MapId, out var map))
            return false;

        // On the map itself: parented to a grid under the origin, it would go with that grid and orphan its ships.
        var uid = Spawn(null, new EntityCoordinates(map.Value, origin.Position));
        var comp = AddComp<WFEncounterComponent>(uid);
        var designation = $"{(char) ('A' + _random.Next(26))}{(char) ('A' + _random.Next(26))}-{_random.Next(100, 1000)}";
        comp.Prototype = prototype.ID;
        comp.Name = Loc.GetString(prototype.Name, ("designation", designation));
        comp.Origin = origin;
        comp.Started = _timing.CurTime;
        comp.Expires = prototype.Duration > 0f && prototype.Lifetime == WFEncounterLifetime.Transient
            ? _timing.CurTime + TimeSpan.FromSeconds(prototype.Duration)
            : null;
        comp.Category = prototype.Category;
        comp.Icon = prototype.Icon;
        comp.Cost = prototype.Cost;
        comp.Lifetime = prototype.Lifetime;
        comp.OffBudget = prototype.Lifetime == WFEncounterLifetime.Persistent || prototype.Start == WFEncounterStart.RoundStart;
        comp.OffBudget |= offBudget;
        // A pinned encounter is the admin's to end: no clock, no slot under the cap, and its ships stay however it goes.
        comp.Pinned = pinned;
        if (comp.Pinned)
        {
            comp.Expires = null;
            comp.OffBudget = true;
        }
        comp.Hidden = prototype.Hidden;
        comp.AnnounceOnRadio = prototype.AnnounceOnRadio;
        comp.AnnouncementSound = prototype.AnnouncementSound;
        comp.AnnouncementColor = prototype.AnnouncementColor;
        comp.Announcer = prototype.Announcer ?? prototype.Ships[0].Key;
        comp.StartRadius = prototype.StartRadius;
        comp.Leash = prototype.Leash;
        if (stops != null)
            comp.Stops.AddRange(stops);
        _meta.SetEntityName(uid, comp.Name);

        WFEncounterManifestPrototype? manifest = null;
        if (prototype.Manifests.Count > 0)
            _prototypes.TryIndex(_random.Pick(prototype.Manifests), out manifest);
        foreach (var ship in prototype.Ships)
        {
            if (comp.Ships.ContainsKey(ship.Key) || !TrySpawnShip((uid, comp), ship, designation, manifest, spawner))
            {
                Log.Error($"Encounter {prototype.ID} could not spawn ship '{ship.Key}'.");
                Remove((uid, comp));
                return false;
            }
        }

        ApplySideColors(comp, prototype);
        comp.Begun = prototype.StartRadius <= 0f;
        if (comp.Begun && !IssueOrders(comp, prototype))
        {
            Remove((uid, comp));
            return false;
        }

        if (prototype.Announcement is { } announcement)
        {
            comp.Announcement = Loc.GetString(announcement,
                ("name", comp.Name),
                ("origin", PlaceName(comp.Stops.Count > 0 ? comp.Stops[0] : null)),
                ("destination", PlaceName(prototype.Route != null && comp.Stops.Count > 0 ? comp.Stops[0] : comp.Stops.Count > 0 ? comp.Stops[^1] : null)),
                ("stops", comp.Stops.Count),
                ("cargo", Loc.GetString(manifest?.Name ?? "wf-encounter-cargo-none")));
            comp.AnnouncementSender = prototype.AnnouncementSender is { } sender ? Loc.GetString(sender) : null;
            // A hidden encounter announces itself when it is revealed.
            if (!comp.Hidden)
                Announce(comp);
        }

        Log.Info($"Encounter {prototype.ID} started as {ToPrettyString(uid)} at {origin} with {comp.Ships.Count} ships and {comp.Stops.Count} stops.");
        encounter = uid;
        var ev = new WFEncounterStartedEvent(uid);
        RaiseLocalEvent(uid, ref ev, true);
        return true;
    }

    /// <summary>
    /// Gives every ship its queue: its route if it flies one, else its prototype's orders. A stranded ship gets its
    /// own once it is rescued.
    /// </summary>
    private bool IssueOrders(WFEncounterComponent comp, WFEncounterPrototype prototype)
    {
        foreach (var ship in prototype.Ships)
        {
            if (!comp.Ships.TryGetValue(ship.Key, out var state) || TerminatingOrDeleted(state.Grid) || IsStranded(state))
                continue;

            if (!IssueShipOrders(comp, prototype, ship, state))
            {
                Log.Error($"Encounter {prototype.ID} has invalid orders for ship '{ship.Key}'.");
                return false;
            }
        }

        return true;
    }

    /// <summary>Gives one ship its queue. False if the orders are invalid; a ship with none to give is left without.</summary>
    private bool IssueShipOrders(WFEncounterComponent comp, WFEncounterPrototype prototype, WFEncounterShip ship,
        WFEncounterShipState state)
    {
        var queue = ship.FlyRoute && prototype.Route is { } route && comp.Stops.Count > 0
            ? BuildRoute(comp, route)
            : BuildQueue(comp, ship, state.Grid);
        if (queue.Count == 0)
            return true;

        if (!_objectives.SetQueue(state.Grid, state.Group, queue))
            return false;

        state.HasOrders = true;
        state.Flown = false;
        return true;
    }

    /// <summary>Gives one ship of a running encounter a new queue as its own orders; false if the encounter, ship or queue is invalid.</summary>
    public bool SetOrders(Entity<WFEncounterComponent?> encounter, string key, List<WFCrewObjective> queue)
    {
        if (!Resolve(encounter, ref encounter.Comp, false) || !encounter.Comp.Ships.TryGetValue(key, out var state)
            || TerminatingOrDeleted(state.Grid) || queue.Count == 0 || !_objectives.SetQueue(state.Grid, state.Group, queue))
            return false;

        state.HasOrders = true;
        state.Flown = false;
        return true;
    }

    private const int SprungLines = 3;

    /// <summary>
    /// Starts an encounter that waited for a player: its ships lying in wait show themselves and every ship gets its
    /// orders. False if its orders are invalid.
    /// </summary>
    private bool Begin(WFEncounterComponent encounter)
    {
        encounter.Begun = true;
        var sprung = false;
        foreach (var ship in encounter.Ships.Values)
        {
            sprung |= ship.Lurking;
            Unmask(ship);
        }

        // The bait drops the act as its friends show themselves.
        if (sprung && encounter.Ships.TryGetValue(encounter.Announcer, out var bait) && !bait.Lurking)
            TrySay(bait, AnnounceChannel, Loc.GetString($"wf-encounter-ambush-sprung-{_random.Next(1, SprungLines + 1)}"));

        if (!_prototypes.TryIndex(encounter.Prototype, out var prototype))
            return false;

        // It waited for a player; its time runs from now, not from when it was placed.
        if (encounter.Expires != null && prototype.Duration > 0f)
            encounter.Expires = _timing.CurTime + TimeSpan.FromSeconds(prototype.Duration);

        return IssueOrders(encounter, prototype);
    }

    /// <summary>Whether a ship waits for players aboard or docked with it before it flies on or jumps out.</summary>
    private static bool HoldsForVisitors(WFEncounterShipState ship)
    {
        return ship.Passengers || ship.Stranding != WFEncounterStranding.None;
    }

    /// <summary>Whether every ship ordered against "the nearest player ship" has one to go for.</summary>
    private bool HasPlayerTargets(WFEncounterComponent encounter)
    {
        if (!_prototypes.TryIndex(encounter.Prototype, out var prototype))
            return true;

        foreach (var ship in prototype.Ships)
        {
            if (!ship.Objectives.Any(objective => objective.Target == PlayerTarget)
                || !encounter.Ships.TryGetValue(ship.Key, out var state) || TerminatingOrDeleted(state.Grid))
                continue;

            if (NearestCrewedShip(state.Grid, MathF.Max(encounter.StartRadius * 2f, PlayerTargetRange)) == null)
                return false;
        }

        return true;
    }

    /// <summary>Whether a living player is within a distance of any of the encounter's ships.</summary>
    private bool PlayerWithin(WFEncounterComponent encounter, float range)
    {
        foreach (var ship in encounter.Ships.Values)
        {
            if (TerminatingOrDeleted(ship.Grid))
                continue;

            var here = _transform.GetMapCoordinates(ship.Grid);
            foreach (var player in _players)
            {
                if (player.MapId == here.MapId && (player.Position - here.Position).LengthSquared() <= range * range)
                    return true;
            }
        }

        return false;
    }

    /// <summary>The orders a ship's prototype gives it. <paramref name="grid"/> is the ship's own, for @player.</summary>
    private List<WFCrewObjective> BuildQueue(WFEncounterComponent comp, WFEncounterShip ship, EntityUid grid)
    {
        var queue = new List<WFCrewObjective>();
        // Meandering: one random point after another around where it arrived.
        for (var leg = 0; leg < ship.Wander; leg++)
        {
            queue.Add(new WFCrewObjective
            {
                Kind = WFCrewObjectiveKind.GoTo,
                Position = comp.Origin.Position + _random.NextAngle().ToVec() * _random.NextFloat(ship.WanderRadius * 0.3f, ship.WanderRadius),
                Range = 200f,
            });
            if (ship.WanderPause > 0f)
                queue.Add(new WFCrewObjective { Kind = WFCrewObjectiveKind.Hold, Duration = ship.WanderPause });
        }

        foreach (var objective in ship.Objectives)
        {
            NetEntity? target = objective.Target switch
            {
                "@origin" => comp.Stops.Count > 0 ? GetNetEntity(comp.Stops[0]) : null,
                "@destination" => comp.Stops.Count > 0 ? GetNetEntity(comp.Stops[^1]) : null,
                PlayerTarget => NearestCrewedShip(grid, MathF.Max(comp.StartRadius * 2f, PlayerTargetRange)) is { } prey
                    ? GetNetEntity(prey)
                    : null,
                { } key when comp.Ships.TryGetValue(key, out var other) => GetNetEntity(other.Grid),
                _ => null,
            };
            // With no player ship about, a task aimed at one is left out rather than failing the encounter.
            if (objective.Target == PlayerTarget && target == null)
                continue;

            queue.Add(new WFCrewObjective
            {
                Kind = objective.Kind,
                Target = target,
                Position = comp.Origin.Position + objective.Offset,
                Range = objective.Range,
                Duration = objective.Duration,
            });
        }

        return queue;
    }

    /// <summary>A haul: dock at each stop, wait there, cast off, and after the last fly clear of it.</summary>
    private List<WFCrewObjective> BuildRoute(WFEncounterComponent comp, WFEncounterRoute route)
    {
        var queue = new List<WFCrewObjective>();
        foreach (var stop in comp.Stops)
        {
            queue.Add(new WFCrewObjective { Kind = WFCrewObjectiveKind.Dock, Target = GetNetEntity(stop) });
            queue.Add(new WFCrewObjective
            {
                Kind = WFCrewObjectiveKind.Hold,
                Duration = _random.NextFloat(route.DwellMin, MathF.Max(route.DwellMin, route.DwellMax)),
            });
            queue.Add(new WFCrewObjective { Kind = WFCrewObjectiveKind.Undock });
        }

        var last = _transform.GetMapCoordinates(comp.Stops[^1]).Position;
        queue.Add(new WFCrewObjective
        {
            Kind = WFCrewObjectiveKind.GoTo,
            Position = last + _random.NextAngle().ToVec() * route.ExitDistance,
            Range = 150f,
        });
        return queue;
    }

    private bool TrySpawnShip(Entity<WFEncounterComponent> encounter, WFEncounterShip ship, string designation,
        WFEncounterManifestPrototype? manifest, EntityUid? spawner)
    {
        ProtoId<VesselPrototype>? vesselId = ship.Vessels.Count > 0 ? _random.Pick(ship.Vessels) : ship.Vessel;
        if (vesselId is not { } id || !_prototypes.TryIndex(id, out var vessel)
            || !_vessels.TrySpawnVessel(vessel, encounter.Comp.Origin.MapId, encounter.Comp.Origin.Position + ship.Offset, spawner, out var spawned))
            return false;

        var grid = spawned.Value;
        var marker = AddComp<WFEncounterGridComponent>(grid);
        marker.Encounter = encounter;
        marker.Key = ship.Key;
        Unload(grid);

        var name = Loc.GetString("wf-encounter-ship-name", ("vessel", vessel.Name), ("designation", designation));
        _meta.SetEntityName(grid, name);
        // Warship designs come off the line with their IFF switched off. A crew flies with it on, so a scope shows
        // who is who; one lying in wait hides it again below.
        _shuttles.RemoveIFFFlag(grid, Content.Shared.Shuttles.Components.IFFFlags.HideLabel | Content.Shared.Shuttles.Components.IFFFlags.Hide);

        var state = new WFEncounterShipState
        {
            Grid = grid,
            Group = Capped($"enc{encounter.Owner.Id}-{ship.Key}", WFCrewLimits.MaxGroup),
            Side = ship.Side.Length > 0 ? ship.Side : ship.Key,
            WarnRange = ship.WarnRange,
            AttackRange = ship.AttackRange,
            ZoneLines = ship.ZoneLines,
            ZoneTargets = ship.ZoneTargets,
            Icon = ship.Icon,
            Hunt = ship.Hunt,
            Distress = ship.Distress,
            Passengers = ship.Passengers.Count > 0,
        };
        encounter.Comp.Ships.Add(ship.Key, state);
        // Boarders come for the ship, not to shoot it up: their guns answer ship guns only.
        if (ship.Hunt)
            EnsureComp<WFHoldFireComponent>(grid);

        // Cargo goes aboard first, so the crew are posted around it and not on it.
        if (manifest != null && manifest.Crates.Count > 0)
        {
            foreach (var tile in _planner.HoldTiles(grid, ship.Cargo))
            {
                Spawn(_random.Pick(manifest.Crates), tile);
            }
        }

        if (ship.Derelict is { } derelict)
        {
            Wreck(state, derelict, vessel.ID);
            return true;
        }

        var mission = new WFCrewMission
        {
            Group = state.Group,
            Callsign = Capped(name, WFCrewLimits.MaxCallsign),
            Battlegroup = ship.Side.Length > 0 ? Capped($"enc{encounter.Owner.Id}-{ship.Side}", WFCrewLimits.MaxBattlegroup) : string.Empty,
            Profile = ship.Profile?.Id ?? string.Empty,
            Company = ship.Company?.Id ?? string.Empty,
            Faction = ship.Faction.Id,
            BoardingResponse = ship.Boarding,
            DockingResponse = ship.Docking,
            HeaveTo = ship.Evades,
            FaceThreats = ship.Platform,
            CallsForHelp = ship.Distress && ship.Passengers.Count == 0,
            Disengage = ship.Disengage,
            DisengageRange = ship.DisengageRange,
            Skill = ship.Skill ?? WFCrewSkill.Veteran,
        };
        if (ship.Navigation is { } navigation && _prototypes.TryIndex(navigation, out var profile))
            mission.Navigation = profile.Settings.Clone();

        var posts = _setup.Plan(grid, ship.Deckhands + ship.Guards, ship.Captain);
        if (ship.Roles.Count > 0)
            posts.RemoveAll(post => !ship.Roles.Any(role => role.Id == post.Role));
        // The last deckhands of the plan are the ship's guards; a ship meant to have hands keeps one to work.
        var guards = ship.Deckhands > 0
            ? Math.Min(ship.Guards, posts.Count(post => post.Role == WFCrewRoles.Deckhand.Id) - 1)
            : ship.Guards;
        for (var i = posts.Count - 1; i >= 0 && guards > 0; i--)
        {
            if (posts[i].Role != WFCrewRoles.Deckhand.Id)
                continue;

            posts[i].Role = WFCrewRoles.Marine.Id;
            guards--;
        }

        // No deckhands asked for: the planner's posts inside the airlocks go unmanned too.
        if (ship.Deckhands <= 0)
            posts.RemoveAll(post => post.Role == WFCrewRoles.Deckhand.Id);

        // An engineer keeps the plant fuelled from the stores, where the ship's roles allow one: posted beside the plant,
        // or failing a safe tile there, in a deckhand's place or alongside the last of the crew.
        var engineer = false;
        if (ship.Engineer && (ship.Roles.Count == 0 || ship.Roles.Any(role => role == WFCrewRoles.Engineer)) && _work.HasPlant(grid))
        {
            if (_work.TryFindPost(grid, out var plant))
                posts.Add(new WFCrewSetupPost { Role = WFCrewRoles.Engineer.Id, Position = plant.Position });
            else if (posts.FindLastIndex(post => post.Role == WFCrewRoles.Deckhand.Id) is var hand && hand >= 0)
                posts[hand].Role = WFCrewRoles.Engineer.Id;
            else if (posts.Count > 0)
                posts.Add(new WFCrewSetupPost { Role = WFCrewRoles.Engineer.Id, Position = posts[^1].Position });
            engineer = posts.Any(post => post.Role == WFCrewRoles.Engineer.Id);
        }

        // A skill set on the ship is the crew's; left unset, each takes one from the profile's pool.
        if (posts.Count == 0 || !_setup.TrySpawn(grid, posts, mission, ship.Skill == null, out _))
            return false;

        // Shipyard hulls come with empty reactors and flat batteries; the crew arrive with theirs charged, unless the
        // ship is out of fuel. Stranding comes after the crew, whose arrival records the hull for repair devices.
        state.Stranding = ship.Stranded == WFEncounterStranding.Random
            ? (_random.Prob(0.5f) ? WFEncounterStranding.Fuel : WFEncounterStranding.Thrusters)
            : ship.Stranded;
        if (state.Stranding != WFEncounterStranding.Fuel)
        {
            // The crew arrive on a running ship: plant fuelled and lit, batteries charged, and fuel for the trip stowed
            // for the engineer. From here the plant burns what it has; nobody tops it up but him, from those stores.
            _power.SetPower(true, grid, false);
            _work.Commission(grid);
            if (engineer)
                _work.StockFuel(grid);
        }
        // A fighter with nobody to post by the plant keeps itself fuelled instead.
        state.AutoRefuel = !engineer;
        if (state.Stranding != WFEncounterStranding.None)
            LeaveStranded(grid, state.Stranding);
        state.NextPower = _timing.CurTime + PowerInterval;

        if (ship.Lurks && encounter.Comp.StartRadius > 0f)
            Mask(state);

        var berths = _planner.HoldTiles(grid, ship.Passengers.Count);
        for (var i = 0; i < ship.Passengers.Count && berths.Count > 0; i++)
        {
            var passenger = Spawn(ship.Passengers[i], berths[i % berths.Count]);
            var aboard = AddComp<WFEncounterPassengerComponent>(passenger);
            aboard.Encounter = encounter;
            aboard.Key = ship.Key;
        }

        return true;
    }

    /// <summary>
    /// Strips the world loaders from a ship's consoles. A helm or gunnery console generates the world for 700 m around
    /// it, meant for a crew of players; a crew of NPCs would seed asteroid fields and drones wherever it wandered, for
    /// the cleanup to sweep away again. Players bring the world with them when they come to look.
    /// </summary>
    private void Unload(EntityUid grid)
    {
        var loaders = new List<EntityUid>();
        var query = EntityQueryEnumerator<WorldLoaderComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid)
                loaders.Add(uid);
        }

        foreach (var uid in loaders)
        {
            RemComp<WorldLoaderComponent>(uid);
        }
    }

    private string PlaceName(EntityUid? place)
    {
        return place is { } uid && !TerminatingOrDeleted(uid) ? MetaData(uid).EntityName : Loc.GetString("wf-encounter-open-space");
    }

    private static string Capped(string text, int length)
    {
        return text.Length <= length ? text : text[..length];
    }

    /// <summary>Marks an encounter as over. Its ships stay until players have left them, or jump out if it is transient.</summary>
    public void Resolve(Entity<WFEncounterComponent?> encounter, WFEncounterResolution resolution)
    {
        if (!Resolve(encounter, ref encounter.Comp, false) || encounter.Comp.Resolution != null)
            return;

        encounter.Comp.Resolution = resolution;
        encounter.Comp.ResolvedAt = _timing.CurTime;
        if (resolution is WFEncounterResolution.Completed or WFEncounterResolution.Expired
            && encounter.Comp.Lifetime == WFEncounterLifetime.Transient && !encounter.Comp.Pinned)
            encounter.Comp.JumpAt = _timing.CurTime + JumpDelay;
        Log.Info($"Encounter {encounter.Comp.Prototype} {ToPrettyString(encounter)} resolved: {resolution}.");
        var ev = new WFEncounterResolvedEvent(encounter, resolution);
        RaiseLocalEvent(encounter, ref ev, true);
    }

    /// <summary>Ends an encounter now and removes its ships and crews, wherever players are.</summary>
    public void End(Entity<WFEncounterComponent?> encounter)
    {
        if (!Resolve(encounter, ref encounter.Comp, false))
            return;

        Resolve(encounter, WFEncounterResolution.Ended);
        // Ending is final: a boarding party or stranded raiders posted on another ship go too.
        foreach (var ship in encounter.Comp.Ships.Values)
        {
            _setup.ClearGroup(ship.Group, keepCorpses: true);
        }

        Remove((encounter, encounter.Comp));
    }

    private void Remove(Entity<WFEncounterComponent> encounter)
    {
        foreach (var ship in encounter.Comp.Ships.Values)
        {
            RemoveShip(ship);
        }

        QueueDel(encounter);
    }

    private void RemoveShip(WFEncounterShipState ship)
    {
        if (TerminatingOrDeleted(ship.Grid))
            return;

        _setup.ClearCrew(ship.Grid, ship.Group, keepCorpses: true);
        // Anyone still aboard is left in space, not deleted with the hull.
        _lifecycle.UnparentPlayersFromGrid(ship.Grid, true);
    }

    /// <summary>Pushes an encounter's expiry and jump-out time forward by a span the event runner sat paused, so paused time does not count against it.</summary>
    public void Hold(Entity<WFEncounterComponent> encounter, TimeSpan span)
    {
        encounter.Comp.Expires += span;
        encounter.Comp.JumpAt += span;
    }

    /// <summary>While the event runner is paused, holds the timers of every encounter by the time since the last frame.</summary>
    private void HoldTimers()
    {
        if (!_scheduler.Paused)
        {
            _heldAt = TimeSpan.Zero;
            return;
        }

        var now = _timing.CurTime;
        var span = _heldAt == TimeSpan.Zero ? TimeSpan.Zero : now - _heldAt;
        _heldAt = now;
        if (span <= TimeSpan.Zero)
            return;

        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            Hold((uid, encounter), span);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        HoldTimers();
        if (_timing.CurTime < _nextPoll)
            return;

        _nextPoll = _timing.CurTime + PollInterval;
        _running.Clear();
        var playersKnown = false;
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            if (!playersKnown)
            {
                FindPlayers();
                playersKnown = true;
            }

            if (encounter.Resolution == null)
            {
                if (!encounter.Begun && PlayerWithin(encounter, encounter.StartRadius) && HasPlayerTargets(encounter)
                    && !Begin(encounter))
                {
                    Remove((uid, encounter));
                    continue;
                }

                SkipBlockedOrders(encounter);
                Tend((uid, encounter));
                Retarget((uid, encounter));
                _running.Add((uid, encounter));
                continue;
            }

            // An admin's ships are never swept up, whoever is near; he removes them with the encounter.
            if (encounter.Pinned)
                continue;

            if (!CleanUp(encounter))
                QueueDel(uid);
        }

        if (_running.Count == 0)
            return;

        // Judged once every encounter has been tended, against the crews as this poll left them.
        var crews = _objectives.Snapshot();
        foreach (var (uid, encounter) in _running)
        {
            if (encounter.Resolution == null && !TerminatingOrDeleted(uid) && Judge(encounter, crews) is { } resolution)
                Resolve((uid, encounter), resolution);
        }

        _running.Clear();
    }

    /// <summary>How long a ship waits off a stop it could not dock at before it flies on.</summary>
    private const float DockFailedLoiter = 75f;

    /// <summary>
    /// A stop the ship could not dock at is not passed over at once: it loiters off it a while in place of the call,
    /// then flies on with the rest of its orders.
    /// </summary>
    private void LoiterInstead(WFEncounterShipState ship, NetEntity net)
    {
        Log.Info($"Encounter ship {ToPrettyString(ship.Grid)} could not dock; it loiters a while and moves on.");
        _objectives.Control(ship.Grid, ship.Group, WFCrewSetupAction.Skip);
        var rest = _objectives.Snapshot().FirstOrDefault(crew => crew.Grid == net && crew.Group == ship.Group)?.Objectives
                   ?? new List<WFCrewObjective>();
        var queue = new List<WFCrewObjective>(rest);
        if (queue.Count == 0 || queue[0].Kind != WFCrewObjectiveKind.Hold)
            queue.Insert(0, new WFCrewObjective { Kind = WFCrewObjectiveKind.Hold, Duration = DockFailedLoiter });
        // The wait it would have spent alongside is cut short; a stay with no end set is left as it is.
        else if (queue[0].Duration > DockFailedLoiter)
            queue[0] = new WFCrewObjective { Kind = WFCrewObjectiveKind.Hold, Duration = DockFailedLoiter };

        if (!_objectives.SetQueue(ship.Grid, ship.Group, queue) && rest.Count == 0)
            ship.Flown = true;
    }

    /// <summary>
    /// Nobody is there to unstick an encounter ship. A stop it cannot dock at is traded for a short wait off it; a
    /// target that is gone is passed over.
    /// </summary>
    private void SkipBlockedOrders(WFEncounterComponent encounter)
    {
        foreach (var ship in encounter.Ships.Values)
        {
            if (!ship.HasOrders || TerminatingOrDeleted(ship.Grid))
                continue;

            var status = _objectives.QueueStatus(ship.Grid, ship.Group);
            if (status is not ("dock-failed" or "target-lost"))
                continue;

            var net = GetNetEntity(ship.Grid);
            var head = _objectives.Snapshot().FirstOrDefault(crew => crew.Grid == net && crew.Group == ship.Group)
                ?.Objectives.FirstOrDefault()?.Kind;
            var stop = head is WFCrewObjectiveKind.Dock or WFCrewObjectiveKind.Loot or WFCrewObjectiveKind.Resupply
                or WFCrewObjectiveKind.Salvage;
            if (stop && status == "dock-failed")
            {
                LoiterInstead(ship, net);
                continue;
            }

            Log.Info($"Encounter ship {ToPrettyString(ship.Grid)} skips an order it cannot carry out.");
            _objectives.Control(ship.Grid, ship.Group, WFCrewSetupAction.Skip);
            // A stop that is gone is passed over whole: the wait there and the casting off go with it.
            for (var i = 0; stop && i < 2; i++)
            {
                var crew = _objectives.Snapshot().FirstOrDefault(crew => crew.Grid == net && crew.Group == ship.Group);
                if (crew == null || crew.Objectives.Count == 0
                    || crew.Objectives[0].Kind is not (WFCrewObjectiveKind.Hold or WFCrewObjectiveKind.Undock))
                    break;

                _objectives.Control(ship.Grid, ship.Group, WFCrewSetupAction.Skip);
            }

            // Skipped to the end: a skip leaves the queue reading "pending", never "complete".
            var rest = _objectives.Snapshot().FirstOrDefault(crew => crew.Grid == net && crew.Group == ship.Group);
            if (rest == null || rest.Objectives.Count == 0)
                ship.Flown = true;
        }
    }

    /// <summary>
    /// What a living crew does for its ship between orders: keeps the batteries charged, and once the ship has been
    /// without thrust for a while with no fight going on, calls for help, saying what it needs, and marks the
    /// encounter as a distress. A stranded ship is not topped up, and is rescued once it has kept its thrust a while.
    /// </summary>
    private void Tend(Entity<WFEncounterComponent> encounter)
    {
        var now = _timing.CurTime;
        foreach (var (key, ship) in encounter.Comp.Ships)
        {
            if (TerminatingOrDeleted(ship.Grid))
                continue;

            if (ship.Hunt && encounter.Comp.Begun)
                Hunt(ship);
            else if (encounter.Comp.Leash > 0f && ship.HasOrders)
                Recall(encounter.Comp, ship);

            if (HoldsForVisitors(ship) && ship.HasOrders)
                Serve(ship);

            if (ship.AutoRefuel && !IsStranded(ship) && HasLivingCrew(ship))
                _work.Refuel(ship.Grid);

            var adrift = _status.IsAdrift(ship.Grid);
            if (IsStranded(ship))
            {
                // Under way means able to drive ahead: a side thruster alone nudges a wrecked ship, it doesn't rescue it.
                var driving = !adrift && (ship.Stranding != WFEncounterStranding.Thrusters || HasForwardThrust(ship.Grid));
                ship.UnderwaySince = driving ? ship.UnderwaySince ?? now : null;
                if (ship.UnderwaySince is { } underway && now - underway >= RescueDelay)
                    Rescue(encounter, key, ship);
            }

            var fighting = _alerts.IsAlerted(ship.Grid, ship.Group);
            if (!adrift || fighting)
            {
                ship.AdriftSince = null;
                continue;
            }

            ship.AdriftSince ??= now;
            if (!ship.Distress || now - ship.AdriftSince < AdriftDelay || now < ship.NextDistress)
                continue;

            var position = _transform.GetMapCoordinates(ship.Grid).Position;
            if (!TrySay(ship, AnnounceChannel, Loc.GetString("wf-encounter-distress-adrift",
                    ("name", MetaData(ship.Grid).EntityName), ("x", (int) position.X), ("y", (int) position.Y),
                    ("need", Need(ship)))))
                continue;

            ship.NextDistress = now + DistressRepeat;
            encounter.Comp.Category = WFEncounterCategory.Distress;
            encounter.Comp.Hidden = false;
        }
    }

    /// <summary>
    /// A ship with passengers stays put while players are aboard or docked with it: its next leg would cast off
    /// with them. Its orders carry on once they have gone.
    /// </summary>
    private void Serve(WFEncounterShipState ship)
    {
        var customers = PlayersAboard(ship.Grid);
        if (customers == ship.Serving)
            return;

        // A queue already flown has no leg left to hold back, and pausing it would hide that it is done.
        if (customers && _objectives.QueueStatus(ship.Grid, ship.Group) == "complete")
            return;

        ship.Serving = customers;
        _objectives.Control(ship.Grid, ship.Group, customers ? WFCrewSetupAction.Pause : WFCrewSetupAction.Resume);
    }

    /// <summary>Whether any of a ship's crew is alive: aboard it, or with <paramref name="anywhere"/> wherever they are.</summary>
    public bool HasLivingCrew(WFEncounterShipState ship, bool anywhere = false)
    {
        var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out var member, out var xform))
        {
            if (member.Group == ship.Group && (anywhere || xform.GridUid == ship.Grid) && _mobs.IsAlive(uid))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a ship is still in the fight: its hull is there and not disabled or wrecked, any of its crew live,
    /// aboard or not, and its helm has not gone unmanned for two minutes. A stranded ship waiting for rescue is not
    /// out of it for being unable to move or shoot. A side is in the fight while any of its ships is.
    /// </summary>
    public bool InFight(WFEncounterShipState ship)
    {
        // A hulk has no crew to lose: it is there until somebody claims it or its time runs out.
        if (ship.Derelict)
            return !TerminatingOrDeleted(ship.Grid);

        return !TerminatingOrDeleted(ship.Grid) && (IsStranded(ship) || !_status.IsDisabled(ship.Grid))
            && HasLivingCrew(ship, anywhere: true)
            && (ship.NoPilotSince is not { } since || _timing.CurTime - since < NoPilotGrace);
    }

    /// <summary>Whether a ship is out of the fight only because nobody has manned its helm for two minutes: hull sound and crew alive.</summary>
    public bool OutForWantOfPilot(WFEncounterShipState ship)
    {
        return !ship.Derelict && !TerminatingOrDeleted(ship.Grid) && (IsStranded(ship) || !_status.IsDisabled(ship.Grid))
            && HasLivingCrew(ship, anywhere: true) && !InFight(ship);
    }

    /// <summary>How long a ship with orders may have nobody at its helm before it is out of the encounter.</summary>
    private static readonly TimeSpan NoPilotGrace = TimeSpan.FromMinutes(2);

    /// <summary>Whether a queue's status says its ship has no pilot to fly it: none alive, none aboard or none at the helm.</summary>
    private static bool Unpiloted(string? status)
    {
        return status is "no-pilot" or "pilot-away" or "awaiting-helm";
    }

    /// <summary>A task that never finishes by itself: a hold, loiter, circle, follow or escort with no duration.</summary>
    private static bool Endless(WFCrewObjective objective)
    {
        return objective.Kind is (WFCrewObjectiveKind.Hold or WFCrewObjectiveKind.Loiter or WFCrewObjectiveKind.Circle
            or WFCrewObjectiveKind.Follow or WFCrewObjectiveKind.Escort) && objective.Duration <= 0f;
    }

    /// <summary>
    /// Whether a ship has flown the queue it was last given. Only its crew's own report counts, and it is kept once
    /// seen: an empty row read before the queue was issued, or a queue paused or resumed after it ran out, means nothing.
    /// </summary>
    private bool Flown(WFEncounterShipState ship, WFCrewSetupCrew? row)
    {
        if (ship.Flown)
            return true;

        // A queue that is gone, cleared by an admin, has nothing left to fly either.
        var status = _objectives.QueueStatus(ship.Grid, ship.Group);
        ship.Flown = status == null || (status == "complete" && (row == null || row.Objectives.Count == 0));
        return ship.Flown;
    }

    /// <summary>
    /// How the encounter has ended, if it has. Its orders are done once every ship with orders that can finish has
    /// flown them or is gone; a ship left on an endless task, such as an escort, doesn't hold the rest up.
    /// </summary>
    private WFEncounterResolution? Judge(WFEncounterComponent encounter, List<WFCrewSetupCrew> crews)
    {
        var sides = new HashSet<string>();
        var fighting = new HashSet<string>();
        var ordered = 0;
        var done = 0;
        foreach (var ship in encounter.Ships.Values)
        {
            sides.Add(ship.Side);
            // The crew hand the helm on when a pilot falls; a ship none can fly, or whose helm none reaches, ages out.
            if (ship.HasOrders && !TerminatingOrDeleted(ship.Grid)
                && Unpiloted(_objectives.QueueStatus(ship.Grid, ship.Group)))
                ship.NoPilotSince ??= _timing.CurTime;
            else
                ship.NoPilotSince = null;

            if (InFight(ship))
                fighting.Add(ship.Side);
            if (!ship.HasOrders)
                continue;

            if (TerminatingOrDeleted(ship.Grid))
            {
                ordered++;
                done++;
                continue;
            }

            var net = GetNetEntity(ship.Grid);
            var row = crews.FirstOrDefault(crew => crew.Grid == net && crew.Group == ship.Group);
            if (row != null && row.Objectives.Count > 0 && Endless(row.Objectives[0]))
                continue;

            ordered++;
            if (Flown(ship, row))
                done++;
        }

        if (fighting.Count == 0)
            return WFEncounterResolution.Destroyed;
        if (sides.Count > 1 && fighting.Count == 1)
            return WFEncounterResolution.Decided;
        // A persistent encounter stays for the round, an admin's until he ends it: finished orders and the clock don't
        // end either.
        if (encounter.Lifetime == WFEncounterLifetime.Persistent || encounter.Pinned)
            return null;
        if (ordered > 0 && done == ordered)
            return WFEncounterResolution.Completed;
        if (encounter.Expires is { } expires && _timing.CurTime >= expires)
            return WFEncounterResolution.Expired;
        return null;
    }

    private void FindPlayers()
    {
        _players.Clear();
        _playerGrids.Clear();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            // A body without a mob state still counts; ghosts and the dead don't.
            if (HasComp<Content.Shared.Ghost.GhostComponent>(uid) || _mobs.IsDead(uid))
                continue;

            _players.Add(_transform.GetMapCoordinates(uid, xform));
            if (xform.GridUid is { } grid)
                _playerGrids.Add(grid);
        }
    }

    /// <summary>Whether a living player is aboard a ship or on a grid docked with it, as of the last poll.</summary>
    private bool PlayersAboard(EntityUid grid)
    {
        if (_playerGrids.Contains(grid))
            return true;

        foreach (var dock in _docking.GetDocks(grid))
        {
            // A station it is docked at always has people on it; only a player's ship alongside holds it back.
            if (dock.Comp.DockedWith is { } other && Transform(other).GridUid is { } docked && _playerGrids.Contains(docked)
                && (!HasComp<Content.Shared.Station.Components.StationMemberComponent>(docked)
                    || HasComp<Content.Shared._NF.Shipyard.Components.ShuttleDeedComponent>(docked)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Removes the ships of a resolved encounter that players have left alone, or that have lingered long enough
    /// with nobody aboard or docked, however near players are. False once none is left.
    /// </summary>
    private bool CleanUp(WFEncounterComponent encounter)
    {
        var now = _timing.CurTime;
        var overdue = encounter.ResolvedAt is { } resolved && now - resolved >= _cleanupLinger;
        var remaining = false;
        foreach (var ship in encounter.Ships.Values)
        {
            if (TerminatingOrDeleted(ship.Grid))
            {
                // Raiders left aboard their prey fight on for a while after their ship is gone, then go too.
                if (ship.Hunt && HasLivingCrew(ship, anywhere: true))
                {
                    if (overdue)
                        _setup.ClearGroup(ship.Group, keepCorpses: true);
                    else
                        remaining = true;
                }

                continue;
            }

            if (encounter.JumpAt is { } jump)
            {
                // A ship jumps out only once nobody is aboard or docked with it: a trader waits for its customers, and
                // no hull vanishes from under a player who is working on it.
                if (now >= jump && !PlayersAboard(ship.Grid))
                    RemoveShip(ship);
                else
                    remaining = true;
                continue;
            }

            if (overdue && !PlayersAboard(ship.Grid))
            {
                RemoveShip(ship);
                continue;
            }

            var here = _transform.GetMapCoordinates(ship.Grid);
            var near = false;
            foreach (var player in _players)
            {
                if (player.MapId == here.MapId && (player.Position - here.Position).LengthSquared() <= _cleanupRange * _cleanupRange)
                {
                    near = true;
                    break;
                }
            }

            if (near)
                ship.Quiet = null;
            else
                ship.Quiet ??= _timing.CurTime;

            if (ship.Quiet is { } quiet && _timing.CurTime - quiet >= _cleanupDelay)
                RemoveShip(ship);
            else
                remaining = true;
        }

        return remaining;
    }
}
