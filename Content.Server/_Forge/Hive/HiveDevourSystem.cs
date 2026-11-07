using Content.Server.Administration.Logs;
using Content.Shared._Forge.Hive;
using Content.Shared._Forge.Hive.Components;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server._Forge.Hive;


public sealed class HiveDevourSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly HiveSystem _hive = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HiveMemberComponent, HiveDevourActionEvent>(OnDevourAction);
        SubscribeLocalEvent<HiveMemberComponent, HiveDevourDoAfterEvent>(OnDevourDoAfter);
    }

    private void OnDevourAction(Entity<HiveMemberComponent> ent, ref HiveDevourActionEvent args)
    {
        if (args.Handled)
            return;

        var target = args.Target;

        if (!CanDevour(ent, target, out var reason))
        {
            if (reason != null)
                _popup.PopupEntity(reason, ent, ent);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, ent.Owner, ent.Comp.DevourTime, new HiveDevourDoAfterEvent(), ent.Owner, target: target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            DistanceThreshold = 1.5f,
            AttemptFrequency = AttemptFrequency.EveryTick,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        args.Handled = true;

        _audio.PlayPvs(ent.Comp.DevourStartSound, target);

        var userIdentity = Identity.Entity(ent, EntityManager);
        var targetIdentity = Identity.Entity(target, EntityManager);

        _popup.PopupEntity(Loc.GetString("hive-devour-start-self", ("target", targetIdentity)), ent, ent, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("hive-devour-start-others", ("user", userIdentity), ("target", targetIdentity)),
            ent, Filter.PvsExcept(ent), true, PopupType.MediumCaution);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(ent):user} started devouring {ToPrettyString(target):target}");
    }

    private void OnDevourDoAfter(Entity<HiveMemberComponent> ent, ref HiveDevourDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = true;


        if (!CanDevour(ent, target, out var reason))
        {
            if (reason != null)
                _popup.PopupEntity(reason, ent, ent);
            return;
        }

        var hadMind = _mind.TryGetMind(target, out _, out _);


        if (TryComp<MobStateComponent>(target, out var mobState) && !_mobState.IsDead(target, mobState))
            _mobState.ChangeMobState(target, MobState.Dead, mobState, ent.Owner);

        _damageable.TryChangeDamage(target, ent.Comp.DevourDamage, ignoreResistances: true, origin: ent.Owner);

        var devoured = EnsureComp<HiveDevouredComponent>(target);
        devoured.DevouredBy = ent.Owner;
        Dirty(target, devoured);

        if (!HasComp<UnrevivableComponent>(target))
        {
            var unrevivable = AddComp<UnrevivableComponent>(target);
            unrevivable.ReasonMessage = ent.Comp.UnrevivableReason;
            Dirty(target, unrevivable);
        }

        var dna = hadMind ? ent.Comp.DnaReward : ent.Comp.MindlessDnaReward;
        if (dna > 0)
            _hive.AddDna(ent.Owner, dna);

        ent.Comp.DevouredCount++;
        Dirty(ent);

        _audio.PlayPvs(ent.Comp.DevourEndSound, target);

        var targetIdentity = Identity.Entity(target, EntityManager);
        var selfMessage = dna > 0
            ? Loc.GetString("hive-devour-end-self", ("target", targetIdentity), ("dna", dna))
            : Loc.GetString("hive-devour-end-self-nodna", ("target", targetIdentity));

        _popup.PopupEntity(selfMessage, ent, ent, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("hive-devour-end-others", ("user", Identity.Entity(ent, EntityManager)), ("target", targetIdentity)),
            ent, Filter.PvsExcept(ent), true, PopupType.LargeCaution);

        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(ent):user} devoured {ToPrettyString(target):target} (had mind: {hadMind}) and gained {dna} DNA");

        var ev = new HiveBodyDevouredEvent(target, dna, hadMind);
        RaiseLocalEvent(ent, ref ev);
    }


    public bool CanDevour(Entity<HiveMemberComponent> ent, EntityUid target, out string? reason)
    {
        reason = null;

        if (target == ent.Owner)
            return false;

        var targetIdentity = Identity.Entity(target, EntityManager);

        if (!HasComp<HumanoidAppearanceComponent>(target) || !TryComp<MobStateComponent>(target, out var mobState))
        {
            reason = Loc.GetString("hive-devour-fail-invalid", ("target", targetIdentity));
            return false;
        }

        if (HasComp<HiveDevouredComponent>(target))
        {
            reason = Loc.GetString("hive-devour-fail-already", ("target", targetIdentity));
            return false;
        }

        if (!ent.Comp.CanDevourHiveMembers && HasComp<HiveMemberComponent>(target))
        {
            reason = Loc.GetString("hive-devour-fail-hive", ("target", targetIdentity));
            return false;
        }

        var dead = _mobState.IsDead(target, mobState);
        var crit = _mobState.IsCritical(target, mobState);

        if (!dead && !(ent.Comp.AllowCritical && crit))
        {
            reason = Loc.GetString(ent.Comp.AllowCritical ? "hive-devour-fail-alive" : "hive-devour-fail-not-dead",
                ("target", targetIdentity));
            return false;
        }

        return true;
    }
}
