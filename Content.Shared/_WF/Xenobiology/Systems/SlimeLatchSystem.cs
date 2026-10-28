using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Xenobiology.Systems;

/// <summary>
/// Slimes latching onto mobs and feeding on them.
/// </summary>
public sealed partial class SlimeLatchSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    private static readonly TimeSpan UpdateDelay = TimeSpan.FromSeconds(1);
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeDamageOvertimeComponent, MobStateChangedEvent>(OnTargetStateChanged);
        SubscribeLocalEvent<SlimeComponent, MobStateChangedEvent>(OnSlimeStateChanged);
        SubscribeLocalEvent<SlimeComponent, PullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<SlimeComponent, UpdateCanMoveEvent>(OnUpdateCanMove);
        SubscribeLocalEvent<SlimeComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SlimeComponent, SlimeLatchEvent>(OnLatchAction);
        SubscribeLocalEvent<SlimeComponent, SlimeLatchDoAfterEvent>(OnLatchDoAfter);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_net.IsClient)
            return;

        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateDelay;
        var query = EntityQueryEnumerator<SlimeDamageOvertimeComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.SourceEntityUid is not { } source
                || !TryComp<SlimeComponent>(source, out var slime)
                || slime.LatchedTarget != uid)
            {
                RemCompDeferred(uid, comp);
                continue;
            }

            if (_mobState.IsDead(uid))
            {
                Unlatch((source, slime));
                continue;
            }

            _damageable.TryChangeDamage(uid, comp.Damage, ignoreResistances: true, origin: source,
                targetPart: TargetBodyPart.All);
            if (TryComp<HungerComponent>(source, out var hunger))
                _hunger.ModifyHunger(source, comp.Damage.GetTotal().Float(), hunger);
        }
    }

    private void OnTargetStateChanged(Entity<SlimeDamageOvertimeComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead
            && ent.Comp.SourceEntityUid is { } source
            && TryComp<SlimeComponent>(source, out var slime))
            Unlatch((source, slime));
    }

    private void OnSlimeStateChanged(Entity<SlimeComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            Unlatch(ent);
    }

    private void OnPullAttempt(Entity<SlimeComponent> ent, ref PullAttemptEvent args)
    {
        // A latched slime can't be dragged off; the target has to shake it off.
        if (IsLatched(ent) && args.PullerUid != ent.Owner)
        {
            args.Cancelled = true;
            return;
        }

        Unlatch(ent);
    }

    private void OnUpdateCanMove(Entity<SlimeComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (IsLatched(ent))
            args.Cancel();
    }

    private void OnInserted(Entity<SlimeComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        Unlatch(ent);
    }

    private void OnLatchAction(Entity<SlimeComponent> ent, ref SlimeLatchEvent args)
    {
        if (args.Handled || TerminatingOrDeleted(args.Target))
            return;

        args.Handled = true;
        if (IsLatched(ent))
        {
            Unlatch(ent);
            return;
        }

        if (CanLatch(ent, args.Target))
            StartLatchDoAfter(ent, args.Target);
    }

    private void OnLatchDoAfter(Entity<SlimeComponent> ent, ref SlimeLatchDoAfterEvent args)
    {
        if (args.Target is not { } target)
            return;

        if (args.Handled || args.Cancelled || !CanLatch(ent, target))
        {
            RemCompDeferred<BeingLatchedComponent>(target);
            return;
        }

        args.Handled = true;
        Latch(ent, target);
    }

    private bool StartLatchDoAfter(Entity<SlimeComponent> ent, EntityUid target)
    {
        if (ent.Comp.Stomach.ContainedEntities.Count >= ent.Comp.MaxContainedEntities)
        {
            _popup.PopupEntity(Loc.GetString("slime-latch-fail-max-entities", ("ent", target)), ent, ent);
            return false;
        }

        _popup.PopupEntity(Loc.GetString("slime-latch-attempt", ("slime", ent), ("ent", target)), ent, ent,
            PopupType.MediumCaution);
        var doAfter = new DoAfterArgs(EntityManager, ent, ent.Comp.LatchDoAfterDuration, new SlimeLatchDoAfterEvent(), ent, target)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return false;

        EnsureComp<BeingLatchedComponent>(target);
        return true;
    }

    public bool IsLatched(Entity<SlimeComponent> ent)
    {
        return ent.Comp.LatchedTarget.HasValue;
    }

    public bool IsLatched(Entity<SlimeComponent> ent, EntityUid target)
    {
        return ent.Comp.LatchedTarget == target;
    }

    public bool CanLatch(Entity<SlimeComponent> ent, EntityUid target)
    {
        return !IsLatched(ent)
            && HasComp<MobStateComponent>(target)
            && !_mobState.IsDead(target)
            && _actionBlocker.CanInteract(ent, target);
    }

    /// <summary>
    /// Starts latching for an NPC slime; false if it can't.
    /// </summary>
    public bool NpcTryLatch(Entity<SlimeComponent> ent, EntityUid target)
    {
        return CanLatch(ent, target) && StartLatchDoAfter(ent, target);
    }

    public void Latch(Entity<SlimeComponent> ent, EntityUid target)
    {
        if (IsLatched(ent))
            Unlatch(ent);

        _xform.SetCoordinates(ent, Transform(target).Coordinates);
        _xform.SetParent(ent, target);
        ent.Comp.LatchedTarget = target;
        Dirty(ent);
        _actionBlocker.UpdateCanMove(ent.Owner);

        var dot = EnsureComp<SlimeDamageOvertimeComponent>(target);
        dot.SourceEntityUid = ent;
        Dirty(target, dot);

        _audio.PlayPredicted(ent.Comp.EatSound, ent, ent);
        _popup.PopupEntity(Loc.GetString("slime-action-latch-success", ("slime", ent), ("target", target)), ent, ent,
            PopupType.SmallCaution);
    }

    public void Unlatch(Entity<SlimeComponent> ent)
    {
        if (ent.Comp.LatchedTarget is not { } target)
            return;

        RemCompDeferred<BeingLatchedComponent>(target);
        RemCompDeferred<SlimeDamageOvertimeComponent>(target);
        if (!TerminatingOrDeleted(target) && !TerminatingOrDeleted(ent))
            _xform.SetParent(ent, _xform.GetParentUid(target));

        ent.Comp.LatchedTarget = null;
        Dirty(ent);
        _actionBlocker.UpdateCanMove(ent.Owner);
    }
}
