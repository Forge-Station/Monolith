using Content.Shared._WF.RMCCore.Actions;
using Content.Shared._WF.RMCCore.Damage;
using Content.Shared._WF.RMCCore.Damage.ObstacleSlamming;
using Content.Shared._WF.RMCCore.Pulling;
using Content.Shared._WF.RMCCore.Slow;
using Content.Shared._WF.RMCCore.Stun;
using Content.Shared._WF.RMCCore.Melee;
using Content.Shared._WF.Xenonids.Abduct;
using Content.Shared._WF.Xenonids.Tail_Lash;
using Content.Shared.Actions;
using Content.Shared.Coordinates;
using Content.Shared.Damage;
using Content.Shared.Effects;
using Content.Shared.FixedPoint;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._WF.Xenonids.Dislocate;

public sealed partial class XenoDislocateSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedColorFlashEffectSystem _colorFlash = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private RMCPullingSystem _rmcPulling = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private RMCSlowSystem _slow = default!;
    [Dependency] private SharedRMCMeleeWeaponSystem _rmcMelee = default!;
    [Dependency] private SharedRMCDamageableSystem _rmcDamageable = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedRMCActionsSystem _rmcActions = default!;
    [Dependency] private XenoSystem _xeno = default!;
    [Dependency] private RMCSizeStunSystem _sizeStun = default!;
    [Dependency] private RMCObstacleSlammingSystem _obstacleSlamming = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoDislocateComponent, XenoDislocateActionEvent>(OnDislocateAction);
    }

    private void OnDislocateAction(Entity<XenoDislocateComponent> xeno, ref XenoDislocateActionEvent args)
    {
        if (args.Handled)
            return;

        if (!_rmcActions.TryUseAction(args))
            return;

        args.Handled = true;

        if (_net.IsServer)
            _audio.PlayPvs(xeno.Comp.Sound, xeno);

        var targetId = args.Target;
        _rmcPulling.TryStopAllPullsFromAndOn(targetId);

        var isDebuffed = HasComp<RMCSlowdownComponent>(targetId) ||
                         HasComp<RMCSuperSlowdownComponent>(targetId) ||
                         HasComp<RMCRootedComponent>(targetId) ||
                         HasComp<StunnedComponent>(targetId) ||
                         _standing.IsDown(targetId);

        var damage = _damageable.TryChangeDamage(targetId, xeno.Comp.Damage, ignoreResistances: isDebuffed, origin: xeno, tool: xeno);
        if (damage?.GetTotal() > FixedPoint2.Zero)
        {
            var filter = Filter.Pvs(targetId, entityManager: EntityManager).RemoveWhereAttachedEntity(o => o == xeno.Owner);
            _colorFlash.RaiseEffect(Color.Red, new List<EntityUid> { targetId }, filter);
        }

        if (isDebuffed)
        {
            _slow.TryRoot(targetId, _xeno.TryApplyXenoDebuffMultiplier(targetId, xeno.Comp.RootTime), true);
        }
        else
        {
            if (_net.IsServer)
            {
                var origin = _transform.GetMapCoordinates(xeno);
                _rmcMelee.DoLunge(xeno, targetId);
                _obstacleSlamming.MakeImmune(targetId);
                _sizeStun.KnockBack(targetId, origin, xeno.Comp.FlingRange, xeno.Comp.FlingRange, 10);
                _rmcDamageable.DoLethalDamage(targetId, origin: xeno);

                SpawnAttachedTo(xeno.Comp.Effect, targetId.ToCoordinates());
            }
        }

        RefreshCooldowns(xeno);
    }

    private void RefreshCooldowns(Entity<XenoDislocateComponent> xeno)
    {
        foreach (var action in _actions.GetActions(xeno))
        {
            var actionEvent = _rmcActions.GetEvent(action.Id);
            if ((actionEvent is XenoAbductActionEvent || actionEvent is XenoTailLashActionEvent)
                && action.Comp.Cooldown != null)
            {
                var cooldownEnd = action.Comp.Cooldown.Value.End - xeno.Comp.CooldownReductionTime;
                if (cooldownEnd < action.Comp.Cooldown.Value.Start)
                    _actions.ClearCooldown(action.Id);
                else
                    _actions.SetCooldown(action.Id, action.Comp.Cooldown.Value.Start, cooldownEnd);
            }
        }
    }
}
