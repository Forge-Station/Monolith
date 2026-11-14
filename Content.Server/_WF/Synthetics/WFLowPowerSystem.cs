using System.Linq;
using Content.Server._EinsteinEngines.Silicon.Death;
using Content.Shared._WF.Synthetics;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Timing;

namespace Content.Server._WF.Synthetics;

/// <summary>
/// Low power: a synthetic chassis with an empty cell, or none, stays up and conscious but crawls and runs one arm.
/// The shutdown system reports the power state here; this keeps the one powered arm and lets the owner switch it.
/// </summary>
public sealed class WFLowPowerSystem : SharedWFSyntheticsSystem
{
    /// <summary>How long after a switch the powered arm can be switched again.</summary>
    public static readonly TimeSpan SwitchCooldown = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(0.5);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WFChassisIsolationSystem _isolation = default!;

    private TimeSpan _nextSweep;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFLowPowerComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<SiliconDownOnDeadComponent, SiliconChargeDyingEvent>(OnChargeDying);
    }

    /// <summary>
    /// An IPC with no cell is reported dead again every tick. Only the first goes through: a repeat would save the
    /// weakened damage set as the one to give back, and the chassis would stay weakened once the cell is back.
    /// </summary>
    private void OnChargeDying(EntityUid uid, SiliconDownOnDeadComponent comp, SiliconChargeDyingEvent args)
    {
        if (comp.Dead)
            args.Cancel();
    }

    /// <summary>
    /// Puts a synthetic body on low power or takes it off. False, and nothing done, for a body that is not synthetic.
    /// </summary>
    public bool SetLowPower(EntityUid body, bool low)
    {
        if (!low)
        {
            Exit(body);
            return true;
        }

        if (TerminatingOrDeleted(body) || !IsSynthetic(body))
            return false;

        if (HasComp<WFLowPowerComponent>(body))
            return true;

        var comp = AddComp<WFLowPowerComponent>(body);
        comp.PoweredArm = PickArm(body);
        Dirty(body, comp);
        Reconcile((body, comp));
        _speed.RefreshMovementSpeedModifiers(body);
        _popup.PopupEntity(Loc.GetString("wf-low-power-enter"), body, body, PopupType.LargeCaution);
        return true;
    }

    private void Exit(EntityUid body)
    {
        if (!HasComp<WFLowPowerComponent>(body))
            return;

        if (TerminatingOrDeleted(body))
        {
            RemComp<WFLowPowerComponent>(body);
            return;
        }

        foreach (var (arm, _) in _body.GetBodyChildrenOfType(body, BodyPartType.Arm).ToList())
            _isolation.TryRestorePower(arm);

        RemComp<WFLowPowerComponent>(body);
        _speed.RefreshMovementSpeedModifiers(body);
        _popup.PopupEntity(Loc.GetString("wf-low-power-exit"), body, body);
    }

    /// <summary>Routes the power to another arm, once the last switch has settled.</summary>
    public bool TrySwitchArm(Entity<WFLowPowerComponent?> body, EntityUid arm)
    {
        if (!Resolve(body, ref body.Comp, false) || body.Comp.PoweredArm == arm ||
            CompOrNull<BodyPartComponent>(arm) is not { PartType: BodyPartType.Arm } part || part.Body != body.Owner)
            return false;

        if (_timing.CurTime < body.Comp.NextSwitch)
        {
            _popup.PopupEntity(Loc.GetString("wf-low-power-switch-cooldown"), body, body);
            return false;
        }

        body.Comp.PoweredArm = arm;
        body.Comp.NextSwitch = _timing.CurTime + SwitchCooldown;
        Dirty(body);
        Reconcile((body, body.Comp));
        _popup.PopupEntity(Loc.GetString("wf-low-power-switched", ("part", arm)), body, body);
        return true;
    }

    /// <summary>The arm whose hand is active, else the first arm with a hand, else the first arm.</summary>
    private EntityUid? PickArm(EntityUid body)
    {
        var arms = _body.GetBodyChildrenOfType(body, BodyPartType.Arm).ToList();
        if (arms.Count == 0)
            return null;

        // Review: an arm the player isolated on purpose is the last choice, not the first.
        var free = arms.Where(arm => !_isolation.IsUserIsolated(arm.Id)).ToList();
        if (free.Count > 0)
            arms = free;

        if (CompOrNull<HandsComponent>(body)?.ActiveHand is { } active)
        {
            var side = active.Location switch
            {
                HandLocation.Left => BodyPartSymmetry.Left,
                HandLocation.Right => BodyPartSymmetry.Right,
                _ => BodyPartSymmetry.None,
            };

            foreach (var (arm, part) in arms)
            {
                if (part.Symmetry == side)
                    return arm;
            }
        }

        foreach (var (arm, part) in arms)
        {
            if (_body.GetBodyPartChildren(arm, part).Any(child => child.Component.PartType == BodyPartType.Hand))
                return arm;
        }

        return arms[0].Id;
    }

    /// <summary>Every arm but the powered one is cut; the powered one is not. A powered arm that came off is replaced.</summary>
    private void Reconcile(Entity<WFLowPowerComponent> ent)
    {
        var arms = _body.GetBodyChildrenOfType(ent, BodyPartType.Arm).ToList();
        if (ent.Comp.PoweredArm is not { } powered || arms.All(arm => arm.Id != powered))
        {
            ent.Comp.PoweredArm = PickArm(ent);
            Dirty(ent);
        }

        var changed = false;
        foreach (var (arm, _) in arms)
        {
            changed |= arm == ent.Comp.PoweredArm
                ? _isolation.TryRestorePower(arm)
                : _isolation.TryCutPower(arm);
        }

        if (changed)
            _speed.RefreshMovementSpeedModifiers(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;
        var stale = new List<EntityUid>();
        var live = new List<Entity<WFLowPowerComponent>>();
        var query = EntityQueryEnumerator<WFLowPowerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (TerminatingOrDeleted(uid) || !IsSynthetic(uid))
                stale.Add(uid);
            else
                live.Add((uid, comp));
        }

        foreach (var uid in stale)
            Exit(uid);

        // A limb put back on while unpowered is cut like the rest.
        foreach (var ent in live)
            Reconcile(ent);
    }

    private void OnGetVerbs(Entity<WFLowPowerComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (args.User != ent.Owner || !args.CanInteract)
            return;

        foreach (var (arm, _) in _body.GetBodyChildrenOfType(ent, BodyPartType.Arm))
        {
            if (arm == ent.Comp.PoweredArm)
                continue;

            var target = arm;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("wf-low-power-verb-switch", ("part", target)),
                Act = () => TrySwitchArm(ent.Owner, target),
            });
        }
    }
}
