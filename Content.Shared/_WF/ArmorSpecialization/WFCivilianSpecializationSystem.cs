using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Actions;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Clothing.Components;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._WF.ArmorSpecialization;

/// <summary>
/// The civilian suit passives that answer events at the moment they happen: steady hands, sterile weave, servo
/// assist, rad sink, wildlife padding, haul frame, blast liner, the mime's vow, suit thrusters, EMP hardening and
/// decon coating.
/// </summary>
public sealed class WFCivilianSpecializationSystem : EntitySystem
{
    private const string Radiation = "Radiation";

    [Dependency] private HeldSpeedModifierSystem _heldSpeed = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSteadyHandsComponent, InventoryRelayedEvent<WolfmedSteadyHandsEvent>>(OnSteadyHands);
        SubscribeLocalEvent<WFSteadyHandsComponent, ExaminedEvent>(OnSteadyHandsExamined);

        SubscribeLocalEvent<WFSterileWeaveComponent, InventoryRelayedEvent<WolfmedSterileEvent>>(OnSterile);
        SubscribeLocalEvent<WFSterileWeaveComponent, ExaminedEvent>(OnSterileExamined);

        SubscribeLocalEvent<WFServoAssistComponent, InventoryRelayedEvent<WFToolSpeedEvent>>(OnToolSpeed);
        SubscribeLocalEvent<WFServoAssistComponent, ExaminedEvent>(OnServoExamined);

        SubscribeLocalEvent<WFRadSinkComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnRadSinkDamage);
        SubscribeLocalEvent<WFRadSinkComponent, ExaminedEvent>(OnRadSinkExamined);

        SubscribeLocalEvent<WFWildlifePaddingComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnWildlifeDamage);
        SubscribeLocalEvent<WFWildlifePaddingComponent, ExaminedEvent>(OnWildlifeExamined);

        SubscribeLocalEvent<WFHaulFrameComponent, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnHaulSpeed);
        SubscribeLocalEvent<WFHaulFrameComponent, GotEquippedEvent>(OnHaulEquipped);
        SubscribeLocalEvent<WFHaulFrameComponent, GotUnequippedEvent>(OnHaulUnequipped);
        SubscribeLocalEvent<WFHaulFrameComponent, ExaminedEvent>(OnHaulExamined);

        SubscribeLocalEvent<WFBlastLinerComponent, InventoryRelayedEvent<WolfmedSeverAttemptEvent>>(OnBlastSever);
        SubscribeLocalEvent<WFBlastLinerComponent, InventoryRelayedEvent<WolfmedWoundRuleAttemptEvent>>(OnBlastWound);
        SubscribeLocalEvent<WFBlastLinerComponent, InventoryRelayedEvent<WolfmedOrganBlastAttemptEvent>>(OnBlastOrgans);
        SubscribeLocalEvent<WFBlastLinerComponent, ExaminedEvent>(OnBlastExamined);

        SubscribeLocalEvent<WFVowOfSilenceComponent, InventoryRelayedEvent<WolfmedPainNoiseAttemptEvent>>(OnPainNoise);
        SubscribeLocalEvent<WFVowOfSilenceComponent, ExaminedEvent>(OnVowExamined);

        SubscribeLocalEvent<WFSuitThrustersComponent, GetItemActionsEvent>(OnThrusterActions);
        SubscribeLocalEvent<WFSuitThrustersComponent, WFSuitThrustersEvent>(OnThrust);
        SubscribeLocalEvent<WFSuitThrustersComponent, ExaminedEvent>(OnThrustersExamined);

        SubscribeLocalEvent<WFEmpHardenedComponent, ExaminedEvent>(OnEmpHardenedExamined);
        SubscribeLocalEvent<WFDeconCoatingComponent, ExaminedEvent>(OnDeconExamined);
    }

    private void OnSteadyHands(Entity<WFSteadyHandsComponent> ent, ref InventoryRelayedEvent<WolfmedSteadyHandsEvent> args)
    {
        args.Args.Multiplier = MathF.Max(args.Args.Multiplier, ent.Comp.Multiplier);
    }

    private void OnSterile(Entity<WFSterileWeaveComponent> ent, ref InventoryRelayedEvent<WolfmedSterileEvent> args)
    {
        if (args.Args.Part is not { } part || WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, part))
            args.Args.Sterile = true;
    }

    private void OnToolSpeed(Entity<WFServoAssistComponent> ent, ref InventoryRelayedEvent<WFToolSpeedEvent> args)
    {
        args.Args.Multiplier = MathF.Max(args.Args.Multiplier, ent.Comp.Multiplier);
    }

    private void OnRadSinkDamage(Entity<WFRadSinkComponent> ent, ref InventoryRelayedEvent<DamageModifyEvent> args)
    {
        if (!args.Args.Damage.DamageDict.TryGetValue(Radiation, out var amount) || amount <= 0)
            return;

        var damage = new DamageSpecifier(args.Args.Damage);
        damage.DamageDict[Radiation] = amount * (1f - Math.Clamp(ent.Comp.Reduction, 0f, 1f));
        args.Args.Damage = damage;
    }

    private void OnWildlifeDamage(Entity<WFWildlifePaddingComponent> ent, ref InventoryRelayedEvent<DamageModifyEvent> args)
    {
        if (args.Args.Origin is not { } attacker || !IsCreature(attacker) || args.Args.Damage.GetTotal() <= 0)
            return;

        args.Args.Damage = args.Args.Damage * ent.Comp.Multiplier;
    }

    /// <summary>
    /// Whether <paramref name="uid"/> wears an EMP-hardened suit or is carried, at any depth, by someone who does.
    /// The EMP system asks this before it pulses anything.
    /// </summary>
    public bool ShieldsFromEmp(EntityUid uid)
    {
        var current = uid;
        for (var depth = 0; depth < 16; depth++)
        {
            if (_inventory.TryGetSlotEntity(current, "outerClothing", out var suit) && HasComp<WFEmpHardenedComponent>(suit))
                return true;

            if (!_containers.TryGetContainingContainer((current, null, null), out var container))
                return false;

            current = container.Owner;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="reagent"/> touching <paramref name="target"/> is kept off by a decon-coated suit. The
    /// reaction system asks this for every touch, except inside <see cref="BypassDecon"/>. Anything that puts out fires
    /// still gets through, so an extinguisher works on the wearer.
    /// </summary>
    public bool BlocksTouch(EntityUid target, ReagentPrototype reagent) =>
        !_deconBypass && reagent.ReactiveEffects?.ContainsKey(ExtinguishGroup) != true &&
        _inventory.TryGetSlotEntity(target, "outerClothing", out var suit) && HasComp<WFDeconCoatingComponent>(suit);

    private const string ExtinguishGroup = "Extinguish";

    /// <summary>Runs a deliberate wash (a shower, antiseptic) that a decon coating lets through.</summary>
    public void BypassDecon(Action action)
    {
        _deconBypass = true;
        try
        {
            action();
        }
        finally
        {
            _deconBypass = false;
        }
    }

    private bool _deconBypass;

    /// <summary>A living thing that isn't a person or a borg: carp, spiders, goliaths and the like.</summary>
    private bool IsCreature(EntityUid uid) =>
        HasComp<MobStateComponent>(uid) && !HasComp<HumanoidAppearanceComponent>(uid) && !HasComp<BorgChassisComponent>(uid);

    #region Haul frame

    /// <summary>Undoes the pull slowdown the pulling system applies to the wearer.</summary>
    private void OnHaulSpeed(Entity<WFHaulFrameComponent> ent, ref InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        if (!_inventory.TryGetContainingEntity(ent.Owner, out var wearer) ||
            !TryComp(wearer, out PullerComponent? puller) || puller.Pulling is not { } pulled)
            return;

        var (walk, sprint) = TryComp(pulled, out HeldSpeedModifierComponent? held)
            ? _heldSpeed.GetHeldMovementSpeedModifiers(pulled, held)
            : (puller.WalkSpeedModifier, puller.SprintSpeedModifier);

        if (walk > 0f && sprint > 0f)
            args.Args.ModifySpeed(MathF.Max(1f, 1f / walk), MathF.Max(1f, 1f / sprint));
    }

    private void OnHaulEquipped(Entity<WFHaulFrameComponent> ent, ref GotEquippedEvent args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(args.Equipee);
    }

    private void OnHaulUnequipped(Entity<WFHaulFrameComponent> ent, ref GotUnequippedEvent args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(args.Equipee);
    }

    #endregion

    private void OnBlastSever(Entity<WFBlastLinerComponent> ent, ref InventoryRelayedEvent<WolfmedSeverAttemptEvent> args)
    {
        if (args.Args.IsExplosion && WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, args.Args.Part))
            args.Args.Cancelled = true;
    }

    private void OnBlastWound(Entity<WFBlastLinerComponent> ent, ref InventoryRelayedEvent<WolfmedWoundRuleAttemptEvent> args)
    {
        if ((args.Args.Cause & WolfmedWoundCause.Explosion) != 0 && ent.Comp.Blocks.Contains(args.Args.Wound) &&
            WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, args.Args.Part))
            args.Args.Cancelled = true;
    }

    private void OnBlastOrgans(Entity<WFBlastLinerComponent> ent, ref InventoryRelayedEvent<WolfmedOrganBlastAttemptEvent> args)
    {
        if (WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, args.Args.Part))
            args.Args.Cancelled = true;
    }

    private void OnPainNoise(Entity<WFVowOfSilenceComponent> ent, ref InventoryRelayedEvent<WolfmedPainNoiseAttemptEvent> args)
    {
        args.Args.Cancelled = true;
    }

    #region Thrusters

    private void OnThrusterActions(Entity<WFSuitThrustersComponent> ent, ref GetItemActionsEvent args)
    {
        if (!TryComp<ClothingComponent>(ent, out var clothing) || args.SlotFlags is not { } slot ||
            (clothing.Slots & slot) == 0)
            return;

        args.AddAction(ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    /// <summary>One burst the way the wearer faces. Only in zero gravity; a wasted press doesn't spend the charge.</summary>
    private void OnThrust(Entity<WFSuitThrustersComponent> ent, ref WFSuitThrustersEvent args)
    {
        var wearer = args.Performer;
        if (!_gravity.IsWeightless(wearer))
        {
            _popup.PopupClient(Loc.GetString("wf-armor-spec-thrusters-gravity"), wearer, wearer);
            return;
        }

        if (!TryComp(wearer, out PhysicsComponent? physics))
            return;

        var direction = _xform.GetWorldRotation(wearer).ToWorldVec();
        _physics.SetLinearVelocity(wearer, physics.LinearVelocity + direction * ent.Comp.Speed, body: physics);
        _popup.PopupClient(Loc.GetString("wf-armor-spec-thrusters-fired"), wearer, wearer);
        args.Handled = true;
    }

    #endregion

    #region Examine

    private void OnSteadyHandsExamined(Entity<WFSteadyHandsComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wf-armor-spec-steady-hands-examine"));

    private void OnSterileExamined(Entity<WFSterileWeaveComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wf-armor-spec-sterile-examine"));

    private void OnServoExamined(Entity<WFServoAssistComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wf-armor-spec-servo-examine",
            ("percent", (int) MathF.Round((ent.Comp.Multiplier - 1f) * 100f))));

    private void OnRadSinkExamined(Entity<WFRadSinkComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wf-armor-spec-rad-sink-examine",
            ("percent", (int) MathF.Round(ent.Comp.Reduction * 100f))));

    private void OnWildlifeExamined(Entity<WFWildlifePaddingComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wf-armor-spec-wildlife-examine",
            ("percent", (int) MathF.Round((1f - ent.Comp.Multiplier) * 100f))));

    private void OnHaulExamined(Entity<WFHaulFrameComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wf-armor-spec-haul-frame-examine"));

    private void OnBlastExamined(Entity<WFBlastLinerComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wf-armor-spec-blast-liner-examine"));

    private void OnVowExamined(Entity<WFVowOfSilenceComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wf-armor-spec-vow-examine"));

    private void OnThrustersExamined(Entity<WFSuitThrustersComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wf-armor-spec-thrusters-examine"));

    private void OnEmpHardenedExamined(Entity<WFEmpHardenedComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wf-armor-spec-emp-hardened-examine"));

    private void OnDeconExamined(Entity<WFDeconCoatingComponent> ent, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("wf-armor-spec-decon-examine"));

    #endregion
}

/// <summary>The suit thrusters' action.</summary>
public sealed partial class WFSuitThrustersEvent : InstantActionEvent;
