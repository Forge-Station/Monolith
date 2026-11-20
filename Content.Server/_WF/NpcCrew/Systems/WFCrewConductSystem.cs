using Content.Server._WF.NpcCrew.Components;
using Content.Server.Chat.Systems;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Chat;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// How an NPC crew takes a visitor's conduct aboard its ship. A player seen with a gun in hand is told to put it away
/// and has <see cref="BrandishTime"/> to do it before the crew turn on him; a player seen by one of its fighters picking
/// up something of the ship's is a thief and is fought at once. Crews already hostile to strangers are left to that.
/// </summary>
public sealed partial class WFCrewConductSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private WFCrewWeaponSystem _weapons = default!;

    /// <summary>How long a player told to put his gun away has to do it.</summary>
    public static readonly TimeSpan BrandishTime = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _next;
    [Dependency] private Robust.Shared.Random.IRobustRandom _random = default!;
    private readonly Stack<EntityUid> _hullWalk = new();

    /// <summary>Players told to put a gun away, by ship and crew: when their time runs out, and whether it has.</summary>
    private readonly Dictionary<(EntityUid Grid, string Group, EntityUid Player), (TimeSpan Deadline, bool Turned)> _warned = new();

    private readonly HashSet<(EntityUid Grid, string Group, EntityUid Player)> _armed = new();
    private readonly Dictionary<(EntityUid Grid, string Group, EntityUid Player), EntityUid> _seen = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewPropertyComponent, GotEquippedHandEvent>(OnPickedUp);
        _next = _timing.CurTime + TimeSpan.FromSeconds(0.5 * _random.NextFloat()); // random phase so the 1 Hz crew sweeps don't share a tick
    }

    /// <summary>Marks every item aboard a ship, stowed or carried, as its crew's property.</summary>
    public void MarkProperty(EntityUid grid, string group)
    {
        // Walks the hull's descendants, containers included, rather than every item in the world.
        _hullWalk.Clear();
        _hullWalk.Push(grid);
        while (_hullWalk.Count > 0)
        {
            var parent = _hullWalk.Pop();
            var children = Transform(parent).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                _hullWalk.Push(child);
                if (!HasComp<ItemComponent>(child))
                    continue;

                var property = EnsureComp<WFCrewPropertyComponent>(child);
                property.Grid = grid;
                property.Group = group;
            }
        }
    }

    /// <summary>Whether a player has been told to put his gun away aboard a crew's ship.</summary>
    public bool IsWarned(EntityUid grid, string group, EntityUid player) => _warned.ContainsKey((grid, group, player));

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Interval;
        _armed.Clear();
        _seen.Clear();

        // Players holding a gun, by the ship they stand on.
        var armed = new Dictionary<EntityUid, List<EntityUid>>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var player, out _, out var xform))
        {
            if (xform.GridUid is not { } grid || !_mobs.IsAlive(player) || !HoldsGun(player))
                continue;

            if (!armed.TryGetValue(grid, out var list))
                armed[grid] = list = new List<EntityUid>();
            list.Add(player);
        }

        if (armed.Count > 0)
        {
            var crew = EntityQueryEnumerator<WFCrewComponent, WFCrewSecurityComponent, TransformComponent>();
            while (crew.MoveNext(out var uid, out var member, out var rules, out var xform))
            {
                if (xform.GridUid is not { } grid || !armed.TryGetValue(grid, out var list) || !_mobs.IsAlive(uid)
                    || HasComp<ActorComponent>(uid) || _crew.HomeGrid(uid, member) != grid)
                    continue;

                foreach (var player in list)
                {
                    if (_crew.SameCrew(uid, player) || !Minds(grid, member.Group, rules, player))
                        continue;

                    var key = (grid, member.Group, player);
                    _armed.Add(key);
                    if (_weapons.CanSee(uid, player))
                        _seen.TryAdd(key, uid);
                }
            }
        }

        foreach (var (key, speaker) in _seen)
        {
            if (_warned.ContainsKey(key))
                continue;

            _warned[key] = (now + BrandishTime, false);
            Say(speaker, "wf-crew-conduct-brandish-warn");
        }

        foreach (var key in new List<(EntityUid Grid, string Group, EntityUid Player)>(_warned.Keys))
        {
            var warning = _warned[key];
            if (warning.Turned)
            {
                // A player they turned on stays their enemy until he leaves the ship.
                if (TerminatingOrDeleted(key.Player) || Transform(key.Player).GridUid != key.Grid)
                    _warned.Remove(key);
                continue;
            }

            if (!_armed.Contains(key))
            {
                // Put away while still in sight: the crew say so.
                if (Transform(key.Player).GridUid == key.Grid && Seer(key) is { } seer)
                    Say(seer, "wf-crew-conduct-brandish-complied");
                _warned.Remove(key);
                continue;
            }

            if (now < warning.Deadline || !_seen.TryGetValue(key, out var speaker))
                continue;

            _warned[key] = (warning.Deadline, true);
            Say(speaker, "wf-crew-conduct-brandish-hostile");
            _crew.AnswerAttack(key.Grid, key.Group, key.Player);
            Log.Info($"{ToPrettyString(key.Player)} kept a gun out aboard {ToPrettyString(key.Grid)}; crew {key.Group} turned hostile.");
        }
    }

    /// <summary>A stranger seen by one of the crew's fighters picking up the ship's property is fought at once.</summary>
    private void OnPickedUp(Entity<WFCrewPropertyComponent> ent, ref GotEquippedHandEvent args)
    {
        var thief = args.User;
        var grid = ent.Comp.Grid;
        if (!HasComp<ActorComponent>(thief) || Transform(thief).GridUid != grid || TerminatingOrDeleted(grid))
            return;

        var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out var member, out var xform))
        {
            if (member.Group != ent.Comp.Group || xform.GridUid != grid || !WFCrewSystem.IsFighter(member)
                || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid) || _crew.SameCrew(uid, thief)
                || !_weapons.CanSee(uid, thief, ignoreStealth: true))
                continue;

            Say(uid, "wf-crew-conduct-theft");
            _crew.AnswerAttack(grid, member.Group, thief);
            Log.Info($"{ToPrettyString(thief)} was seen taking {ToPrettyString(ent)} aboard {ToPrettyString(grid)}; crew {member.Group} turned hostile.");
            return;
        }
    }

    /// <summary>
    /// Whether the crew mind a player's gun: not when they already fight every stranger or are in a fight of their own,
    /// unless they have already told him.
    /// </summary>
    private bool Minds(EntityUid grid, string group, WFCrewSecurityComponent rules, EntityUid player)
    {
        return _warned.ContainsKey((grid, group, player))
               || rules.Boarding != WFCrewSecurityResponse.Hostile && !_alerts.IsAlerted(grid, group);
    }

    private bool HoldsGun(EntityUid player)
    {
        foreach (var held in _hands.EnumerateHeld(player))
        {
            if (HasComp<GunComponent>(held))
                return true;
        }

        return false;
    }

    /// <summary>A living crewman of the ship who can see the player.</summary>
    private EntityUid? Seer((EntityUid Grid, string Group, EntityUid Player) key)
    {
        var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out var member, out var xform))
        {
            if (member.Group == key.Group && xform.GridUid == key.Grid && _mobs.IsAlive(uid)
                && !HasComp<ActorComponent>(uid) && _weapons.CanSee(uid, key.Player))
                return uid;
        }

        return null;
    }

    private void Say(EntityUid uid, string line)
    {
        _chat.TrySendInGameICMessage(uid, Loc.GetString(line), InGameICChatType.Speak, hideChat: false,
            checkRadioPrefix: false);
    }
}
