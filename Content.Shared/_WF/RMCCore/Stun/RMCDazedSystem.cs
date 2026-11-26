using Content.Shared._WF.RMCCore.Actions;
using Content.Shared.Actions;
using Content.Shared.Speech.EntitySystems;
using Content.Shared.StatusEffect;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.RMCCore.Stun;

public sealed partial class RMCDazedSystem : EntitySystem
{
    [Dependency] private Content.Shared.StatusEffectNew.StatusEffectsSystem _statusEffect = default!; // WOLFGATE: RMC's SharedStatusEffectsSystem
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedStutteringSystem _stutter = default!;

    public static readonly EntProtoId StatusEffectDazed = "Dazed";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RMCDazedComponent, StatusEffectAppliedEvent>(OnDazed);
        SubscribeLocalEvent<RMCDazedComponent, StatusEffectRemovedEvent>(OnDazedEnd);
    }

    /// <summary>
    ///     Put actions with the RMCDazeableActionComponent on cooldown for the given duration, only if the current
    ///     cooldown isn't higher already.
    /// </summary>
    /// <seealso cref="RMCDazeableActionComponent"/>
    private void OnDazed(Entity<RMCDazedComponent> ent, ref StatusEffectAppliedEvent args)
    {
        foreach (var (actionId, _) in _actions.GetActions(args.Target))
        {
            if (TryComp(actionId, out RMCDazeableActionComponent? _))
            {
                _actions.SetEnabled(actionId, false);

                // WOLFGATE: action charges live on the action component here, not LimitedCharges
                if (_actions.GetCharges(actionId) != null)
                    _actions.SetCharges(actionId, 0);
            }
        }
    }

    private void OnDazedEnd(Entity<RMCDazedComponent> ent, ref StatusEffectRemovedEvent args)
    {
        foreach (var (actionId, _) in _actions.GetActions(args.Target))
        {
            if (TryComp(actionId, out RMCDazeableActionComponent? _))
            {
                _actions.SetEnabled(actionId, true);
                _actions.ResetCharges(actionId, update: true, dirty: true);
            }
        }
    }

    public bool TryDaze(EntityUid uid, TimeSpan time, bool refresh = false, StatusEffectsComponent? status = null, bool stutter = false)
    {
        if (!Resolve(uid, ref status, false))
            return false;

        if (time <= TimeSpan.Zero)
            return false;

        var appliedEffect = false;
        if (refresh)
        {
            _statusEffect.TryUpdateStatusEffectDuration(uid, StatusEffectDazed, time);
            appliedEffect = true;
        }
        else if (_statusEffect.TryAddStatusEffectDuration(uid, StatusEffectDazed, time))
            appliedEffect = true;

        if (appliedEffect && stutter)
            _stutter.DoStutter(uid, time, true, status);

        return false;
    }
}
