// Author: @lenta313. Все права не защищены / No rights reserved.
using System.Linq;
using Content.Shared._Forge.EmergencyLimb;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.EmergencyLimb;

/// <summary>
/// Emergency-limb mechanic with two independent paths:
///
/// 1. HANDHELD (click): an activated implant used on a humanoid welds a replacement for the missing
///    arm/leg selected on the targeting doll, dealing a cauterization burn. The implant is consumed.
///
/// 2. IMPLANTED (surgery): the implant can instead be surgically installed into the torso (see
///    <see cref="EmergencyLimbRegrowerComponent"/>), storing a charge and granting the regrow action.
///    The action regrows the doll-selected missing limb, spending a charge. Wiring the surgery to call
///    <see cref="AddCharge"/> is handled by the surgery step.
/// </summary>
public sealed class EmergencyLimbImplantSystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmergencyLimbImplantComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<BodyComponent, InteractUsingEvent>(OnBodyInteractUsing);

        SubscribeLocalEvent<EmergencyLimbRegrowerComponent, RegrowEmergencyLimbEvent>(OnRegrow);

        // Surgery: implant the device into the torso for the regrow mechanic.
        SubscribeLocalEvent<SurgeryImplantEmergencyLimbStepComponent, SurgeryStepEvent>(OnImplantSurgeryStep);
    }

    private void OnImplantSurgeryStep(Entity<SurgeryImplantEmergencyLimbStepComponent> ent, ref SurgeryStepEvent args)
    {
        foreach (var tool in args.Tools)
        {
            if (!TryComp<EmergencyLimbImplantComponent>(tool, out var implant))
                continue;

            if (AddCharge(args.Body, implant))
            {
                _popup.PopupEntity(Loc.GetString("emergency-limb-surgery-implanted"), args.Body, args.User);
                QueueDel(tool);
            }
            else
            {
                _popup.PopupEntity(Loc.GetString("emergency-limb-implant-full"), args.Body, args.User);
            }

            return;
        }
    }

    // --- Handheld: weld a limb directly on click ---

    private void OnAfterInteract(Entity<EmergencyLimbImplantComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (!HasComp<BodyComponent>(target))
            return;

        args.Handled = TryWeld(ent, target, args.User);
    }

    private void OnBodyInteractUsing(Entity<BodyComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<EmergencyLimbImplantComponent>(args.Used, out var implant))
            return;

        args.Handled = TryWeld((args.Used, implant), ent, args.User);
    }

    private bool TryWeld(Entity<EmergencyLimbImplantComponent> ent, EntityUid target, EntityUid user)
    {
        if (!_toggle.IsActivated(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("emergency-limb-not-active"), ent, user);
            return false;
        }

        var selected = CompOrNull<TargetingComponent>(user)?.Target ?? TargetBodyPart.Torso;

        if (!TryMap(selected, ent.Comp.LeftArm, ent.Comp.RightArm, ent.Comp.LeftLeg, ent.Comp.RightLeg, out var proto, out var type, out var sym, out var slot))
        {
            _popup.PopupEntity(Loc.GetString("emergency-limb-select-limb"), ent, user);
            return false;
        }

        if (!TryGrowLimb(target, proto, type, sym, slot, ent.Comp.Damage))
        {
            _popup.PopupEntity(Loc.GetString("emergency-limb-already-present"), target, user);
            return false;
        }

        _popup.PopupEntity(Loc.GetString("emergency-limb-installed"), target, user);

        if (ent.Comp.ConsumeOnUse)
            QueueDel(ent);

        return true;
    }

    // --- Implanted: surgery stores charges, the action regrows ---

    /// <summary>
    /// Called by the implant surgery to install the regrow system into the torso: grants a single-use
    /// regrow action. Returns false (and does nothing) if the torso already has one installed, so a
    /// second implant surgery can't be used to stack the action.
    /// </summary>
    public bool AddCharge(EntityUid body, EmergencyLimbImplantComponent implant)
    {
        if (HasComp<EmergencyLimbRegrowerComponent>(body))
            return false;

        var regrower = AddComp<EmergencyLimbRegrowerComponent>(body);

        regrower.Damage = implant.Damage;
        regrower.LeftArm = implant.LeftArm;
        regrower.RightArm = implant.RightArm;
        regrower.LeftLeg = implant.LeftLeg;
        regrower.RightLeg = implant.RightLeg;
        _actions.AddAction(body, ref regrower.ActionEntity, regrower.Action);
        return true;
    }

    private void OnRegrow(Entity<EmergencyLimbRegrowerComponent> ent, ref RegrowEmergencyLimbEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var selected = CompOrNull<TargetingComponent>(ent)?.Target ?? TargetBodyPart.Torso;

        if (!TryMap(selected, ent.Comp.LeftArm, ent.Comp.RightArm, ent.Comp.LeftLeg, ent.Comp.RightLeg, out var proto, out var type, out var sym, out var slot))
        {
            _popup.PopupEntity(Loc.GetString("emergency-limb-select-limb"), ent, ent);
            return;
        }

        if (!TryGrowLimb(ent, proto, type, sym, slot, ent.Comp.Damage))
        {
            _popup.PopupEntity(Loc.GetString("emergency-limb-already-present"), ent, ent);
            return;
        }

        _popup.PopupEntity(Loc.GetString("emergency-limb-installed"), ent, ent);

        // Single-use: remove the action and the regrower itself, so the torso can be re-implanted.
        if (ent.Comp.ActionEntity != null)
            _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);

        RemComp<EmergencyLimbRegrowerComponent>(ent.Owner);
    }

    // --- Shared limb-growing logic ---

    /// <summary>
    /// Spawns and attaches the limb to the target's torso if that slot is empty, dealing the burn.
    /// Returns false if the limb is already present or the body has no torso.
    /// </summary>
    private bool TryGrowLimb(EntityUid target, EntProtoId proto, BodyPartType type, BodyPartSymmetry sym, string slot, DamageSpecifier damage)
    {
        if (_body.GetRootPartOrNull(target) is not { } root)
            return false;

        if (_body.GetBodyChildrenOfType(target, type, symmetry: sym).Any())
            return false;

        var limb = Spawn(proto, Transform(target).Coordinates);

        _body.TryCreatePartSlot(root.Entity, slot, type, out _);
        if (!_body.AttachPart(root.Entity, slot, limb))
        {
            Del(limb);
            return false;
        }

        _damageable.TryChangeDamage(target, damage, true, origin: target);
        return true;
    }

    private bool TryMap(
        TargetBodyPart selected,
        EntProtoId leftArm,
        EntProtoId rightArm,
        EntProtoId leftLeg,
        EntProtoId rightLeg,
        out EntProtoId proto,
        out BodyPartType type,
        out BodyPartSymmetry sym,
        out string slot)
    {
        switch (selected)
        {
            case TargetBodyPart.LeftArm:
            case TargetBodyPart.LeftHand:
                proto = leftArm; type = BodyPartType.Arm; sym = BodyPartSymmetry.Left; slot = "left arm";
                return true;
            case TargetBodyPart.RightArm:
            case TargetBodyPart.RightHand:
                proto = rightArm; type = BodyPartType.Arm; sym = BodyPartSymmetry.Right; slot = "right arm";
                return true;
            case TargetBodyPart.LeftLeg:
            case TargetBodyPart.LeftFoot:
                proto = leftLeg; type = BodyPartType.Leg; sym = BodyPartSymmetry.Left; slot = "left leg";
                return true;
            case TargetBodyPart.RightLeg:
            case TargetBodyPart.RightFoot:
                proto = rightLeg; type = BodyPartType.Leg; sym = BodyPartSymmetry.Right; slot = "right leg";
                return true;
            default:
                proto = default;
                type = default;
                sym = default;
                slot = string.Empty;
                return false;
        }
    }
}
