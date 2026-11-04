using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Server.Stack;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.Planets;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.CavernEncounters;

/// <summary>
/// The lost surveyor follows whoever offers to lead him (activate him), comes along when they climb or drop a level,
/// and pays whoever brought him once he stands alive on the ground above his cavern.
/// </summary>
public sealed partial class WFCavernSurveyorSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private WFCavernWalkerSystem _walker = default!;

    /// <summary>Whoever is paid must stand within this many tiles of him.</summary>
    private const float PayRange = 10f;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    private static readonly ProtoId<DamageTypePrototype> WoundType = "Blunt";

    private TimeSpan _nextCheck;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFCavernSurveyorComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnActivate(Entity<WFCavernSurveyorComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || ent.Comp.Paid || !_mobState.IsAlive(ent) || args.User == ent.Owner)
            return;

        args.Handled = true;
        Lead(ent, args.User);
    }

    /// <summary>Has the surveyor follow someone out.</summary>
    public void Lead(Entity<WFCavernSurveyorComponent> ent, EntityUid leader)
    {
        if (ent.Comp.Leader == leader)
            return;

        ent.Comp.Leader = leader;
        _walker.Follow(ent, leader);
        _popup.PopupEntity(Loc.GetString("wf-cavern-surveyor-follows", ("surveyor", ent.Owner)), ent, leader);
        _chat.TrySendInGameICMessage(ent, Loc.GetString("wf-cavern-surveyor-led"), InGameICChatType.Speak,
            hideChat: false, hideLog: true, ignoreActionBlocker: true);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;

        var query = EntityQueryEnumerator<WFCavernSurveyorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var surveyor, out var xform))
        {
            if (!surveyor.Hurt)
            {
                surveyor.Hurt = true;
                if (HasComp<WFCavernLayerComponent>(xform.MapUid))
                    surveyor.Cavern = xform.MapUid;
                if (surveyor.Wounds > 0f)
                    _damage.TryChangeDamage(uid, new DamageSpecifier(_proto.Index(WoundType), surveyor.Wounds), true);
                continue;
            }

            if (surveyor.Paid || !_mobState.IsAlive(uid))
                continue;

            Bring((uid, surveyor), xform);

            if (surveyor.Cavern is { } cavern
                && TryComp<WFCavernLayerComponent>(cavern, out var layer)
                && xform.MapUid == layer.Ground)
                TryPay((uid, surveyor), xform);
        }
    }

    /// <summary>Puts him beside his leader when the leader has climbed or dropped to another level of the same world close by.</summary>
    private void Bring(Entity<WFCavernSurveyorComponent> ent, TransformComponent xform)
    {
        if (ent.Comp.Leader is not { } leader || TerminatingOrDeleted(leader))
            return;

        var leaderXform = Transform(leader);
        if (leaderXform.MapUid is not { } leaderMap || leaderMap == xform.MapUid
            || !TryComp<WFPlanetLayerComponent>(leaderMap, out var leaderLayer)
            || !TryComp<WFPlanetLayerComponent>(xform.MapUid, out var ownLayer)
            || leaderLayer.Network != ownLayer.Network)
            return;

        // The levels of a world share one frame, so a level change keeps x and y.
        var leaderPosition = _transform.GetWorldPosition(leaderXform);
        if (Vector2.Distance(leaderPosition, _transform.GetWorldPosition(xform)) > ent.Comp.BringRange)
            return;

        _transform.SetMapCoordinates(ent, new MapCoordinates(leaderPosition, leaderXform.MapID));
        _walker.Follow(ent, leader);
    }

    /// <summary>Pays his leader, or the nearest player if the leader isn't by him, once he is on the surface.</summary>
    private void TryPay(Entity<WFCavernSurveyorComponent> ent, TransformComponent xform)
    {
        var here = _transform.GetWorldPosition(xform);
        EntityUid? payee = null;

        if (ent.Comp.Leader is { } leader && !TerminatingOrDeleted(leader) && _mobState.IsAlive(leader)
            && Transform(leader).MapUid == xform.MapUid
            && Vector2.Distance(_transform.GetWorldPosition(leader), here) <= PayRange)
        {
            payee = leader;
        }
        else
        {
            var best = PayRange;
            var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
            while (actors.MoveNext(out var actor, out _, out var other))
            {
                if (other.MapUid != xform.MapUid || !_mobState.IsAlive(actor))
                    continue;

                var distance = Vector2.Distance(_transform.GetWorldPosition(other), here);
                if (distance > best)
                    continue;

                best = distance;
                payee = actor;
            }
        }

        if (payee is not { } who)
            return;

        ent.Comp.Paid = true;
        _chat.TrySendInGameICMessage(ent, Loc.GetString("wf-cavern-surveyor-thanks"), InGameICChatType.Speak,
            hideChat: false, hideLog: true, ignoreActionBlocker: true);
        if (ent.Comp.Reward > 0)
            _stack.SpawnMultiple("SpaceCash", ent.Comp.Reward, who);

        _walker.Stop(ent);
        var angle = _random.NextFloat(MathF.Tau);
        _walker.WalkTo(ent, xform.Coordinates.Offset(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 15f));
    }
}
