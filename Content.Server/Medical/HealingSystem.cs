using Content.Server.Administration.Logs;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Medical.Components;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems; // WOLFGATE(Wolfmed): ordering before CableSystem
using Content.Server.Stack;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Medical;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Body.Systems; // Shitmed Change
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Audio;

// Shitmed Change
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Components;
using System.Linq;

using Content.Shared._Onyx.Wounds; // WOLFGATE(Wolfmed): HOOK 8, wound-host call sites below

namespace Content.Server.Medical;

public sealed partial class HealingSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedTargetingSystem _targetingSystem = default!; // Shitmed Change
    [Dependency] private BloodstreamSystem _bloodstreamSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private MobThresholdSystem _mobThresholdSystem = default!;
    [Dependency] private PopupSystem _popupSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private SharedBodySystem _bodySystem = default!; // Shitmed Change

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HealingComponent, UseInHandEvent>(OnHealingUse);
        // WOLFGATE(Wolfmed) START: HOOK 8, the heal is offered before a cable coil places cable.
        // A cable coil is both a healing item for a chassis and a cable placer. Only a heal that actually happens
        // sets Handled, so a coil used on anything that is not a wounded machine still places cable as before.
        // SubscribeLocalEvent<HealingComponent, AfterInteractEvent>(OnHealingAfterInteract);
        SubscribeLocalEvent<HealingComponent, AfterInteractEvent>(OnHealingAfterInteract,
            before: [typeof(CableSystem)]);
        // WOLFGATE END
        SubscribeLocalEvent<DamageableComponent, HealingDoAfterEvent>(OnDoAfter);
    }

    private void OnDoAfter(Entity<DamageableComponent> entity, ref HealingDoAfterEvent args)
    {
        var dontRepeat = false;

        if (!TryComp(args.Used, out HealingComponent? healing))
            return;

        if (args.Handled || args.Cancelled)
            return;

        // WOLFGATE(Wolfmed) START: HOOK 8, wound hosts heal through WoundHealingSystem, never the flat DamageableComponent path.
        if (HasComp<WoundHostComponent>(entity))
        {
            OnWoundHostDoAfter(entity, ref args, healing);
            return;
        }
        // WOLFGATE END

        if (healing.DamageContainers is not null &&
            entity.Comp.DamageContainerID is not null &&
            !healing.DamageContainers.Contains(entity.Comp.DamageContainerID))
        {
            return;
        }

        // Heal some bloodloss damage.
        if (healing.BloodlossModifier != 0)
        {
            if (!TryComp<BloodstreamComponent>(entity.Owner, out var bloodstream))
                return;
            var isBleeding = bloodstream.BleedAmount > 0;
            _bloodstreamSystem.TryModifyBleedAmount(entity.Owner, healing.BloodlossModifier);
            if (isBleeding != bloodstream.BleedAmount > 0)
            {
                if (entity.Owner == args.User)
                    _popupSystem.PopupEntity(Loc.GetString("medical-item-stop-bleeding-self"), entity, args.User);
                else
                    _popupSystem.PopupEntity(Loc.GetString("medical-item-stop-bleeding", ("target", Identity.Entity(entity.Owner, EntityManager))), entity, args.User);
            }
        }

        // Restores missing blood
        if (healing.ModifyBloodLevel != 0)
            _bloodstreamSystem.TryModifyBloodLevel(entity.Owner, healing.ModifyBloodLevel);

        var healed = _damageable.TryChangeDamage(entity.Owner, healing.Damage, true, origin: args.User, canSever: false); // Shitmed Change

        if (healed == null && healing.BloodlossModifier != 0)
            return;

        var total = healed?.GetTotal() ?? FixedPoint2.Zero;

        // Re-verify that we can heal the damage.

        if (TryComp<StackComponent>(args.Used.Value, out var stackComp))
        {
            _stacks.Use(args.Used.Value, 1, stackComp);

            if (_stacks.GetCount(args.Used.Value, stackComp) <= 0)
                dontRepeat = true;
        }
        else
        {
            QueueDel(args.Used.Value);
        }

        if (entity.Owner != args.User)
        {
            _adminLogger.Add(LogType.Healed,
                $"{EntityManager.ToPrettyString(args.User):user} healed {EntityManager.ToPrettyString(entity.Owner):target} for {total:damage} damage");
        }
        else
        {
            _adminLogger.Add(LogType.Healed,
                $"{EntityManager.ToPrettyString(args.User):user} healed themselves for {total:damage} damage");
        }

        _audio.PlayPvs(healing.HealingEndSound, entity.Owner);

        // Logic to determine the whether or not to repeat the healing action
        args.Repeat = HasDamage(entity.Comp, healing) && !dontRepeat || IsPartDamaged(args.User, entity); // Shitmed Change
        if (!args.Repeat && !dontRepeat)
            _popupSystem.PopupEntity(Loc.GetString("medical-item-finished-using", ("item", args.Used)), entity.Owner, args.User);
        args.Handled = true;
    }

    // WOLFGATE(Wolfmed): HOOK 8, wound-host healing body moved to HealingSystem.Wolfmed.cs (OnWoundHostDoAfter,
    // GetHealingContainers, IsWoundDamaged) - this partial shares its private fields.

    private bool HasDamage(DamageableComponent component, HealingComponent healing)
    {
        var damageableDict = component.Damage.DamageDict;
        var healingDict = healing.Damage.DamageDict;
        foreach (var type in healingDict)
        {
            if (damageableDict[type.Key].Value > 0)
            {
                return true;
            }
        }

        return false;
    }

    // Shitmed Change Start
    private bool IsPartDamaged(EntityUid user, EntityUid target)
    {
        if (!TryComp(user, out TargetingComponent? targeting))
            return false;

        var (targetType, targetSymmetry) = _bodySystem.ConvertTargetBodyPart(targeting.Target);
        foreach (var part in _bodySystem.GetBodyChildrenOfType(target, targetType, symmetry: targetSymmetry))
            if (TryComp<DamageableComponent>(part.Id, out var damageable)
                && damageable.TotalDamage > part.Component.MinIntegrity)
                return true;

        return false;
    }

    // Shitmed Change End

    private void OnHealingUse(Entity<HealingComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (TryHeal(entity, args.User, args.User, entity.Comp))
            args.Handled = true;
    }

    private void OnHealingAfterInteract(Entity<HealingComponent> entity, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null)
            return;

        if (TryHeal(entity, args.User, args.Target.Value, entity.Comp))
            args.Handled = true;
    }

    private bool TryHeal(EntityUid uid, EntityUid user, EntityUid target, HealingComponent component)
    {
        if (!TryComp<DamageableComponent>(target, out var targetDamage))
            return false;

        // WOLFGATE(Wolfmed) START: HOOK 8, a wound host resolves a body part, so the body's own damage container does not gate it.
        var woundHost = HasComp<WoundHostComponent>(target);

        // if (component.DamageContainers is not null &&
        if (!woundHost &&
            component.DamageContainers is not null &&
        // WOLFGATE END
            targetDamage.DamageContainerID is not null &&
            !component.DamageContainers.Contains(targetDamage.DamageContainerID))
        {
            return false;
        }

        if (user != target && !_interactionSystem.InRangeUnobstructed(user, target, popup: true))
            return false;

        if (TryComp<StackComponent>(uid, out var stack) && stack.Count < 1)
            return false;

        // WOLFGATE(Wolfmed) START: HOOK 8, a wound host is treated on the part the heal resolves.
        EntityUid? requestedPart = null;
        if (woundHost)
        {
            if (!_woundHealing.TryGetTargetedPart(target, user, out requestedPart))
            {
                _popupSystem.PopupEntity(Loc.GetString("targeting-selected-part-missing"), target, user);
                return false;
            }

            // Also retain the resolved site for healers without a targeting selector.
            var resolved = _woundHealing.ResolveHealingPart(target, requestedPart,
                _woundHealing.GetTreatableDamage(component), // WOLFGATE(Wolfmed): W0, TreatedDamageTypes narrows the spec.
                GetHealingContainers(component), component.TreatmentCapabilities, component.AllowedWoundStages,
                component.BloodlossModifier, component.HealWounds);
            if (requestedPart != null && resolved == null)
            {
                _popupSystem.PopupEntity(Loc.GetString("targeting-selected-part-incompatible"), target, user);
                return false;
            }
            requestedPart = resolved;
        }

        // var anythingToDo =
        //     HasDamage(targetDamage, component) ||
        var anythingToDo = woundHost // WOLFGATE(Wolfmed): HOOK 8
            ? IsWoundDamaged((target, targetDamage), component, requestedPart)
            : HasDamage(targetDamage, component) ||
        // WOLFGATE END
            IsPartDamaged(user, target) || // Shitmed Change
            component.ModifyBloodLevel > 0 // Special case if healing item can restore lost blood...
                && TryComp<BloodstreamComponent>(target, out var bloodstream)
                && _solutionContainerSystem.ResolveSolution(target, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var bloodSolution)
                && bloodSolution.Volume < bloodSolution.MaxVolume; // ...and there is lost blood to restore.

        if (!anythingToDo)
        {
            // WOLFGATE(Wolfmed) START: W0, on a wound host the refusal is about the selected part, not the whole patient.
            // _popupSystem.PopupEntity(Loc.GetString("medical-item-cant-use", ("item", uid)), uid, user);
            _popupSystem.PopupEntity(Loc.GetString(woundHost ? "wolfmed-item-cant-treat-part" : "medical-item-cant-use",
                ("item", uid)), uid, user);
            // WOLFGATE END
            return false;
        }

        _audio.PlayPvs(component.HealingBeginSound, uid);

        var isNotSelf = user != target;

        if (isNotSelf)
        {
            var msg = Loc.GetString("medical-item-popup-target", ("user", Identity.Entity(user, EntityManager)), ("item", uid));
            _popupSystem.PopupEntity(msg, target, target, PopupType.Medium);
        }

        var delay = isNotSelf
            ? component.Delay
            : component.Delay * GetScaledHealingPenalty(user, component);

        var doAfterEventArgs =
            // WOLFGATE(Wolfmed) START: HOOK 8, the do-after carries the part the heal resolved.
            // new DoAfterArgs(EntityManager, user, delay, new HealingDoAfterEvent(), target, target: target, used: uid)
            new DoAfterArgs(EntityManager, user, delay,
                new HealingDoAfterEvent { RequestedPart = GetNetEntity(requestedPart) }, target, target: target, used: uid)
            // WOLFGATE END
            {
                // Didn't break on damage as they may be trying to prevent it and
                // not being able to heal your own ticking damage would be frustrating.
                NeedHand = true,
                BreakOnMove = true,
                BreakOnWeightlessMove = false,
            };

        _doAfter.TryStartDoAfter(doAfterEventArgs);
        return true;
    }

    /// <summary>
    /// Scales the self-heal penalty based on the amount of damage taken
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="component"></param>
    /// <returns></returns>
    public float GetScaledHealingPenalty(EntityUid uid, HealingComponent component)
    {
        var output = component.Delay;
        if (!TryComp<MobThresholdsComponent>(uid, out var mobThreshold) ||
            !TryComp<DamageableComponent>(uid, out var damageable))
            return output;
        if (!_mobThresholdSystem.TryGetThresholdForState(uid, MobState.Critical, out var amount, mobThreshold))
            return 1;

        var percentDamage = (float) (damageable.TotalDamage / amount);
        //basically make it scale from 1 to the multiplier.
        var modifier = percentDamage * (component.SelfHealPenaltyMultiplier - 1) + 1;
        return Math.Max(modifier, 1);
    }
}
