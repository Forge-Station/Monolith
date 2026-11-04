using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.Strangers;
using Content.Server._WF.Traders;
using Content.Server.Chat.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._White.Standing;
using Content.Shared._WF.Traders;
using Content.Shared.ActionBlocker;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Cowardice;

/// <summary>
/// Civilians lose their nerve (<see cref="WFCowardiceComponent"/>): a gun drawn near one, or a few hits on him, may make
/// him cower where he is or run, and until he calms down he moves no more than that and attacks nobody.
/// </summary>
public sealed class WFCowardiceSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedLayingDownSystem _laying = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TraderFleeSystem _traderFlee = default!;
    [Dependency] private TraderSystem _trader = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFStrangerSystem _strangers = default!;

    /// <summary>Within this a drawn gun is a threat whichever way it points.</summary>
    private const float PointBlank = 3f;

    /// <summary>The widest angle off the gunman's facing that still counts as pointed at him: cos of 75 degrees.</summary>
    private const float AimCosine = 0.26f;

    /// <summary>How far he runs from the one who scared him.</summary>
    private const float FleeDistance = 14f;

    private const int Cries = 5;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan Reweigh = TimeSpan.FromSeconds(30);

    private readonly HashSet<Entity<ActorComponent>> _actors = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCowardiceComponent, BeforeDamageChangedEvent>(OnDamaged,
            before: [typeof(WoundDamageRoutingSystem), typeof(WFStrangerSystem)]);
        SubscribeLocalEvent<WFCowardiceComponent, UpdateCanMoveEvent>(OnCanMove);
        SubscribeLocalEvent<WFCowardiceComponent, AttackAttemptEvent>(OnAttack);
    }

    /// <summary>Whether he is frightened right now: cowering or running.</summary>
    public bool IsScared(EntityUid uid)
    {
        return TryComp(uid, out WFCowardiceComponent? coward) && coward.State != WFCowardState.None;
    }

    private void OnCanMove(Entity<WFCowardiceComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.State == WFCowardState.Cowering)
            args.Cancel();
    }

    private void OnAttack(Entity<WFCowardiceComponent> ent, ref AttackAttemptEvent args)
    {
        if (ent.Comp.State != WFCowardState.None)
            args.Cancel();
    }

    /// <summary>Counts hits on him from a mob; the last of a few within the window is a threat.</summary>
    private void OnDamaged(Entity<WFCowardiceComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive() || args.Origin is not { } origin
            || ent.Comp.State != WFCowardState.None || !_mobs.IsAlive(ent))
            return;

        var attacker = _crew.Wielder(origin);
        if (attacker == ent.Owner || TerminatingOrDeleted(attacker) || !HasComp<MobStateComponent>(attacker))
            return;

        var now = _timing.CurTime;
        var times = ent.Comp.HitTimes;
        // One hit can reach him twice in a tick, as body-part routing applies it again.
        if (times.Count > 0 && times[^1] == now)
            return;
        times.RemoveAll(time => now - time > ent.Comp.HitWindow);
        times.Add(now);
        if (times.Count < ent.Comp.Hits)
            return;

        times.Clear();
        Threatened(ent, attacker);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFCowardiceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var coward, out var xform))
        {
            if (!_mobs.IsAlive(uid))
            {
                if (coward.State != WFCowardState.None)
                    Calm((uid, coward));
                continue;
            }

            if (coward.State != WFCowardState.None)
            {
                if (now >= coward.Until)
                    Calm((uid, coward));
                continue;
            }

            if (now < coward.NextThink || xform.MapUid is not { } map || Paused(map))
                continue;

            coward.NextThink = now + Interval;
            if (now < coward.RestUntil)
                continue;

            if (DrawnAgainst((uid, coward), xform, now) is { } gunman)
                Threatened((uid, coward), gunman);
        }
    }

    /// <summary>A gunman within sight who has a gun in his hands and is pointing it his way, not weighed up lately.</summary>
    private EntityUid? DrawnAgainst(Entity<WFCowardiceComponent> ent, TransformComponent xform, TimeSpan now)
    {
        var weighed = ent.Comp.Weighed;
        foreach (var key in weighed.Keys.ToList())
        {
            if (now >= weighed[key] || TerminatingOrDeleted(key))
                weighed.Remove(key);
        }

        var here = _transform.GetMapCoordinates(ent, xform);
        _actors.Clear();
        _lookup.GetEntitiesInRange(here, ent.Comp.GunRange, _actors);
        foreach (var actor in _actors)
        {
            if (actor.Owner == ent.Owner || weighed.ContainsKey(actor.Owner) || !_mobs.IsAlive(actor.Owner))
                continue;

            var there = _transform.GetMapCoordinates(actor.Owner);
            if (!PointsGun(actor.Owner, there, here)
                || !_examine.InRangeUnOccluded(here, there, ent.Comp.GunRange, null))
                continue;

            weighed[actor.Owner] = now + Reweigh;
            return actor.Owner;
        }

        return null;
    }

    /// <summary>Whether <paramref name="holder"/> holds a gun that is wielded, close, or turned towards <paramref name="target"/>.</summary>
    public bool PointsGun(EntityUid holder, MapCoordinates holderAt, MapCoordinates target)
    {
        EntityUid? gun = null;
        foreach (var held in _hands.EnumerateHeld(holder))
        {
            if (HasComp<GunComponent>(held))
            {
                gun = held;
                break;
            }
        }

        if (gun is not { } drawn || holderAt.MapId != target.MapId)
            return false;

        var offset = target.Position - holderAt.Position;
        if (offset.Length() <= PointBlank || TryComp(drawn, out WieldableComponent? wieldable) && wieldable.Wielded)
            return true;

        var facing = _transform.GetWorldRotation(holder).ToWorldVec();
        return Vector2.Dot(facing, Vector2.Normalize(offset)) >= AimCosine;
    }

    /// <summary>
    /// He is threatened by <paramref name="threat"/>: he rolls his cowardice, and breaks or holds firm. True when he broke.
    /// </summary>
    public bool Threatened(Entity<WFCowardiceComponent> ent, EntityUid threat)
    {
        var coward = ent.Comp;
        var now = _timing.CurTime;
        if (coward.State != WFCowardState.None || now < coward.RestUntil || !_mobs.IsAlive(ent)
            || !_random.Prob(Math.Clamp(coward.Cowardice, 0f, 1f)))
            return false;

        var trader = TryComp(ent, out TraderComponent? serving) ? serving : null;
        var strangers = TryComp(ent, out WFStrangerComponent? stranger) ? stranger : null;
        if (trader?.Customer != null)
            _trader.EndConversation((ent, trader), false);
        if (strangers != null)
            _strangers.Frighten((ent, strangers), threat);

        coward.Threat = threat;
        coward.Until = now + TimeSpan.FromSeconds(_random.NextFloat(coward.MinSeconds, Math.Max(coward.MinSeconds, coward.MaxSeconds)));
        var runs = _random.Prob(Math.Clamp(coward.Flee, 0f, 1f));
        if (runs && trader is { Killable: true })
        {
            // A trader runs the way he does when hurt.
            _traderFlee.Spook((ent, trader), threat, false);
            coward.State = WFCowardState.Fleeing;
        }
        else if (runs && trader == null && RunFrom(ent, threat))
        {
            coward.State = WFCowardState.Fleeing;
        }
        else
        {
            coward.State = WFCowardState.Cowering;
            _steering.Unregister(ent);
            _laying.TryLieDown(ent);
        }

        foreach (var held in _hands.EnumerateHeld(ent.Owner).ToList())
        {
            if (_random.Prob(Math.Clamp(coward.DropChance, 0f, 1f)))
                _hands.TryDrop(ent.Owner, held);
        }

        _blocker.UpdateCanMove(ent);
        Cry(ent);
        return true;
    }

    /// <summary>Sends him off across the ground away from <paramref name="threat"/>; false when there is nowhere to go.</summary>
    private bool RunFrom(Entity<WFCowardiceComponent> ent, EntityUid threat)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp)
            || xform.MapUid is not { } map || TerminatingOrDeleted(threat))
            return false;

        var here = _transform.GetWorldPosition(xform);
        var away = here - _transform.GetWorldPosition(threat);
        var heading = away.LengthSquared() > 0.01f ? new Angle(away) : _random.NextAngle();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var angle = heading + Angle.FromDegrees(_random.NextFloat(-50f, 50f));
            var world = here + angle.ToVec() * FleeDistance;
            var coordinates = _transform.ToCoordinates(grid, new MapCoordinates(world, xform.MapID));
            if (!_map.TryGetTileRef(grid, gridComp, coordinates, out var tile) || tile.Tile.IsEmpty)
                continue;

            if (!HasComp<Content.Shared.NPC.ActiveNPCComponent>(ent))
                _npc.WakeNPC(ent);
            _steering.Register(ent, coordinates);
            return true;
        }

        return false;
    }

    /// <summary>He calms down: back on his feet, free to move, and rested for a while before he can break again.</summary>
    private void Calm(Entity<WFCowardiceComponent> ent)
    {
        var coward = ent.Comp;
        coward.State = WFCowardState.None;
        coward.Threat = null;
        coward.RestUntil = _timing.CurTime + TimeSpan.FromSeconds(coward.RestSeconds);
        _steering.Unregister(ent);
        _blocker.UpdateCanMove(ent);
        if (_mobs.IsAlive(ent))
            _laying.TryStandUp(ent);
    }

    /// <summary>A frightened NPC cries out aloud, never over the radio.</summary>
    private void Cry(EntityUid uid)
    {
        if (HasComp<ActorComponent>(uid))
            return;

        if (_random.Prob(0.4f))
            _chat.TryEmoteWithChat(uid, "Scream", ChatTransmitRange.HideChat, hideLog: true);
        else
            _chat.TrySendInGameICMessage(uid, Loc.GetString($"wf-cowardice-cry-{_random.Next(1, Cries + 1)}"),
                InGameICChatType.Speak, hideChat: true, hideLog: true, checkRadioPrefix: false);
    }
}
