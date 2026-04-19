using Content.Shared.Actions;
using Content.Shared._EE.CCVar; // EE
using Content.Shared.Gravity;
using Content.Shared.Inventory; // Forge-Change
using Content.Shared.Inventory.Events;  // Forge-Change
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Systems; // Forge-Change
using Robust.Shared.Configuration; // EE
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Serialization;
using Robust.Shared.Timing; // Forge-Change

namespace Content.Shared.Movement.Systems;

public abstract class SharedJetpackSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifier = default!;
    [Dependency] protected readonly SharedAppearanceSystem Appearance = default!;
    [Dependency] protected readonly SharedContainerSystem Container = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly IConfigurationManager _config = default!; // EE
    // Forge-Change-start
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedGravitySystem _gravity = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float BaseCombatControlPenalty = 0.15f;
    private const float BaseCombatModifierPenalty = 0.08f;
    private const float MinCombatPenalty = 0.02f;
    private static readonly TimeSpan CombatPenaltyDuration = TimeSpan.FromSeconds(0.65f);
    // Forge-Change-end

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JetpackComponent, GetItemActionsEvent>(OnJetpackGetAction);
        SubscribeLocalEvent<JetpackComponent, DroppedEvent>(OnJetpackDropped);
        SubscribeLocalEvent<JetpackComponent, ToggleJetpackEvent>(OnJetpackToggle);

        SubscribeLocalEvent<JetpackUserComponent, RefreshWeightlessModifiersEvent>(OnJetpackUserWeightlessMovement);
        SubscribeLocalEvent<JetpackUserComponent, CanWeightlessMoveEvent>(OnJetpackUserCanWeightless);
        SubscribeLocalEvent<JetpackUserComponent, EntParentChangedMessage>(OnJetpackUserEntParentChanged);
        SubscribeLocalEvent<JetpackUserComponent, DidEquipEvent>(OnJetpackUserDidEquip); // Forge-Change
        SubscribeLocalEvent<JetpackUserComponent, DidUnequipEvent>(OnJetpackUserDidUnequip); // Forge-Change
        SubscribeLocalEvent<JetpackComponent, EntGotInsertedIntoContainerMessage>(OnJetpackMoved);

        SubscribeLocalEvent<GravityChangedEvent>(OnJetpackUserGravityChanged);
        SubscribeLocalEvent<GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<JetpackComponent, MapInitEvent>(OnMapInit);
    }
    // Forge-Change-start
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<JetpackUserComponent>();
        while (query.MoveNext(out var uid, out var userComp))
        {
            var changed = false; // Forge-Change

            if (_timing.CurTime >= userComp.CombatPenaltyEndTime &&
                (!MathHelper.CloseTo(userComp.CombatControlPenaltyMultiplier, 1f) ||
                 !MathHelper.CloseTo(userComp.CombatModifierPenaltyMultiplier, 1f)))
            {
                userComp.CombatControlPenaltyMultiplier = 1f;
                userComp.CombatModifierPenaltyMultiplier = 1f;
                ApplyCurrentModifiers(uid, userComp);
                changed = true;
            }

            if (changed)
                Dirty(uid, userComp);
        }
    }
    // Forge-Change-end
    private void OnJetpackUserWeightlessMovement(Entity<JetpackUserComponent> ent, ref RefreshWeightlessModifiersEvent args)
    {
        // Yes this bulldozes the values but primarily for backwards compat atm.
        args.WeightlessAcceleration = ent.Comp.WeightlessAcceleration;
        args.WeightlessModifier = ent.Comp.WeightlessModifier;
        args.WeightlessFriction = ent.Comp.WeightlessFriction;
        args.WeightlessFrictionNoInput = ent.Comp.WeightlessFrictionNoInput;
    }

    private void OnMapInit(EntityUid uid, JetpackComponent component, MapInitEvent args)
    {
        _actionContainer.EnsureAction(uid, ref component.ToggleActionEntity, component.ToggleAction);
        Dirty(uid, component);
    }

    private void OnJetpackUserGravityChanged(ref GravityChangedEvent ev)
    {
        if (_config.GetCVar(EECCVars.JetpackEnableAnywhere)) // EE
            return; // EE

        var gridUid = ev.ChangedGridIndex;
        var jetpackQuery = GetEntityQuery<JetpackComponent>();

        // First, disable jetpacks on users
        var query = EntityQueryEnumerator<JetpackUserComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var user, out var transform))
        {
            if (transform.GridUid == gridUid && ev.HasGravity &&
                jetpackQuery.TryGetComponent(user.Jetpack, out var jetpack))
            {
                _popup.PopupClient(Loc.GetString("jetpack-to-grid"), uid, uid);

                SetEnabled(user.Jetpack, jetpack, false, uid);
            }
        }

        // Additionally, find any active jetpacks without users on the grid that need to be disabled
        if (ev.HasGravity)
        {
            var activeJetpackQuery = EntityQueryEnumerator<ActiveJetpackComponent, JetpackComponent, TransformComponent>();

            while (activeJetpackQuery.MoveNext(out var jetpackUid, out _, out var jetpackComponent, out var jetpackTransform))
            {
                // If the jetpack is on this grid and has no user, disable it
                if (jetpackTransform.GridUid == gridUid && !HasComp<JetpackUserComponent>(jetpackUid))
                {
                    // Check if the jetpack is being held/worn by someone
                    EntityUid? user = null;
                    Container.TryGetContainingContainer((jetpackUid, null, null), out var container);
                    user = container?.Owner;

                    SetEnabled(jetpackUid, jetpackComponent, false, user);
                }
            }
        }
    }

    private void OnJetpackDropped(EntityUid uid, JetpackComponent component, DroppedEvent args)
    {
        SetEnabled(uid, component, false, args.User);
    }

    private void OnJetpackMoved(Entity<JetpackComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        if (args.Container.Owner != ent.Comp.JetpackUser)
            SetEnabled(ent, ent.Comp, false, ent.Comp.JetpackUser);
    }

    private void OnJetpackUserCanWeightless(EntityUid uid, JetpackUserComponent component, ref CanWeightlessMoveEvent args)
    {
        args.CanMove = true;
    }

    // Forge-Change-start
    private void OnJetpackUserDidEquip(EntityUid uid, JetpackUserComponent component, DidEquipEvent args)
    {
        if (args.Slot != "outerClothing")
            return;

        if (UpdateSuitProfile(uid, component))
            Dirty(uid, component);
    }

    private void OnJetpackUserDidUnequip(EntityUid uid, JetpackUserComponent component, DidUnequipEvent args)
    {
        if (args.Slot != "outerClothing")
            return;

        if (UpdateSuitProfile(uid, component))
            Dirty(uid, component);
    }
    // Forge-Change-end
    private void OnJetpackUserEntParentChanged(EntityUid uid, JetpackUserComponent component, ref EntParentChangedMessage args)
    {
        // Frontier: note - comment from upstream, dead men tell no tales
        // No and no again! Do not attempt to activate the jetpack on a grid with gravity disabled. You will not be the first or the last to try this.
        // https://discord.com/channels/310555209753690112/310555209753690112/1270067921682694234
        if (TryComp<JetpackComponent>(component.Jetpack, out var jetpack)
            && (!CanEnableOnGrid(args.Transform.GridUid)
            || !UserNotParented(uid, jetpack))) // EE
        {
            SetEnabled(component.Jetpack, jetpack, false, uid);

            _popup.PopupClient(Loc.GetString("jetpack-to-grid"), uid, uid);
        }
    }

    private void SetupUser(EntityUid user, EntityUid jetpackUid, JetpackComponent component)
    {
        EnsureComp<JetpackUserComponent>(user, out var userComp);
        component.JetpackUser = user;

        if (TryComp<PhysicsComponent>(user, out var physics))
            _physics.SetBodyStatus(user, physics, BodyStatus.InAir);

        userComp.Jetpack = jetpackUid;
        // Forge-Change-start
        userComp.CombatControlPenaltyMultiplier = 1f;
        userComp.CombatModifierPenaltyMultiplier = 1f;
        userComp.CombatPenaltyEndTime = TimeSpan.Zero;
        UpdateSuitProfile(user, userComp, component);
        // Forge-Change-end
    }

    private void RemoveUser(EntityUid uid, JetpackComponent component)
    {
        if (!RemComp<JetpackUserComponent>(uid))
            return;

        component.JetpackUser = null;

        if (TryComp<PhysicsComponent>(uid, out var physics))
            _physics.SetBodyStatus(uid, physics, BodyStatus.OnGround);

        _movementSpeedModifier.RefreshWeightlessModifiers(uid);
    }

    private void OnJetpackToggle(EntityUid uid, JetpackComponent component, ToggleJetpackEvent args)
    {
        if (args.Handled)
            return;

        if (TryComp(uid, out TransformComponent? xform) && !CanEnableOnGrid(xform.GridUid))
        {
            _popup.PopupClient(Loc.GetString("jetpack-no-station"), uid, args.Performer);

            return;
        }

        SetEnabled(uid, component, !IsEnabled(uid));
    }

    private bool CanEnableOnGrid(EntityUid? gridUid)
    {
        // No and no again! Do not attempt to activate the jetpack on a grid with gravity disabled. You will not be the first or the last to try this.
        // https://discord.com/channels/310555209753690112/310555209753690112/1270067921682694234
        return gridUid == null // EE
        //||(!HasComp<GravityComponent>(gridUid)); // EE
            || _config.GetCVar(EECCVars.JetpackEnableAnywhere) // EE
            || _config.GetCVar(EECCVars.JetpackEnableInNoGravity) // EE
            && TryComp<GravityComponent>(gridUid, out var comp) // EE
            && !comp.Enabled; // EE
    }

    private void OnJetpackGetAction(EntityUid uid, JetpackComponent component, GetItemActionsEvent args)
    {
        args.AddAction(ref component.ToggleActionEntity, component.ToggleAction);
    }
    // Forge-Change-start
    private void OnGunShot(ref GunShotEvent args)
    {
        if (!TryComp<JetpackUserComponent>(args.User, out var userComp) ||
            !TryComp<JetpackComponent>(userComp.Jetpack, out _))
        {
            return;
        }

        if (!_gravity.IsWeightless(args.User))
            return;

        var controlPenalty = Math.Max(MinCombatPenalty, BaseCombatControlPenalty - userComp.SuitCombatStabilityBonus);
        var modifierPenalty = Math.Max(MinCombatPenalty, BaseCombatModifierPenalty - (userComp.SuitCombatStabilityBonus * 0.5f));

        userComp.CombatControlPenaltyMultiplier = 1f - controlPenalty;
        userComp.CombatModifierPenaltyMultiplier = 1f - modifierPenalty;
        userComp.CombatPenaltyEndTime = _timing.CurTime + CombatPenaltyDuration;
        ApplyCurrentModifiers(args.User, userComp);
        Dirty(args.User, userComp);
    }
    // Forge-Change-end
    private bool IsEnabled(EntityUid uid)
    {
        return HasComp<ActiveJetpackComponent>(uid);
    }

    public void SetEnabled(EntityUid uid, JetpackComponent component, bool enabled, EntityUid? user = null)
    {
        if (IsEnabled(uid) == enabled ||
            enabled && !CanEnable(uid, component))
            return;

        if (user == null)
        {
            if (!Container.TryGetContainingContainer((uid, null, null), out var container))
                return;
            user = container.Owner;
        }

        // EE: check if user has a parent (e.g. vehicle, duffelbag, bed)
        if (enabled && !UserNotParented(user, component))
            return;
        // End EE

        if (enabled)
        {
            SetupUser(user.Value, uid, component);
            EnsureComp<ActiveJetpackComponent>(uid);
        }
        else
        {
            RemoveUser(user.Value, component);
            RemComp<ActiveJetpackComponent>(uid);
        }


        Appearance.SetData(uid, JetpackVisuals.Enabled, enabled);
        Dirty(uid, component);
    }

    public bool IsUserFlying(EntityUid uid)
    {
        return HasComp<JetpackUserComponent>(uid);
    }

    protected virtual bool CanEnable(EntityUid uid, JetpackComponent component)
    {
        return true;
    }
    // Forge-Change-start
    private bool UpdateSuitProfile(EntityUid user, JetpackUserComponent userComp, JetpackComponent? jetpackComp = null)
    {
        if (!Resolve(userComp.Jetpack, ref jetpackComp, false))
            return false;

        var profile = GetSuitFlightProfile(user);
        var changed = !MathHelper.CloseTo(userComp.SuitThrustMultiplier, profile.ThrustMultiplier) ||
                      !MathHelper.CloseTo(userComp.SuitControlMultiplier, profile.ControlMultiplier) ||
                      !MathHelper.CloseTo(userComp.SuitFuelUsageMultiplier, profile.FuelUsageMultiplier) ||
                      !MathHelper.CloseTo(userComp.SuitCombatStabilityBonus, profile.CombatStabilityBonus) ||
                      !MathHelper.CloseTo(userComp.BaseWeightlessAcceleration, jetpackComp.Acceleration * profile.ThrustMultiplier) ||
                      !MathHelper.CloseTo(userComp.BaseWeightlessFriction, jetpackComp.Friction * profile.ControlMultiplier) ||
                      !MathHelper.CloseTo(userComp.BaseWeightlessModifier, jetpackComp.WeightlessModifier * profile.ThrustMultiplier);

        if (!changed)
            return false;

        userComp.SuitThrustMultiplier = profile.ThrustMultiplier;
        userComp.SuitControlMultiplier = profile.ControlMultiplier;
        userComp.SuitFuelUsageMultiplier = profile.FuelUsageMultiplier;
        userComp.SuitCombatStabilityBonus = profile.CombatStabilityBonus;
        userComp.BaseWeightlessAcceleration = jetpackComp.Acceleration * profile.ThrustMultiplier;
        userComp.BaseWeightlessFriction = jetpackComp.Friction * profile.ControlMultiplier;
        userComp.BaseWeightlessModifier = jetpackComp.WeightlessModifier * profile.ThrustMultiplier;
        ApplyCurrentModifiers(user, userComp);
        return true;
    }

    private void ApplyCurrentModifiers(EntityUid user, JetpackUserComponent userComp)
    {
        userComp.WeightlessAcceleration = userComp.BaseWeightlessAcceleration;
        userComp.WeightlessFriction = userComp.BaseWeightlessFriction * userComp.CombatControlPenaltyMultiplier;
        userComp.WeightlessFrictionNoInput = userComp.WeightlessFriction;
        userComp.WeightlessModifier = userComp.BaseWeightlessModifier * userComp.CombatModifierPenaltyMultiplier;
        _movementSpeedModifier.RefreshWeightlessModifiers(user);
    }

    private (float ThrustMultiplier, float ControlMultiplier, float FuelUsageMultiplier, float CombatStabilityBonus) GetSuitFlightProfile(EntityUid user)
    {
        if (_inventory.TryGetSlotEntity(user, "outerClothing", out var outerClothing) &&
            TryComp<SpaceFlightProfileComponent>(outerClothing, out var profile))
        {
            return (profile.ThrustMultiplier, profile.ControlMultiplier, profile.FuelUsageMultiplier, profile.CombatStabilityBonus);
        }

        return (1f, 1f, 1f, 0f);
    }
    // Forge-Change-end
    // EE: check parent
    protected virtual bool UserNotParented(EntityUid? user, JetpackComponent component)
    {
        return !TryComp(user, out TransformComponent? xform)
            || xform.ParentUid == xform.GridUid
            || xform.ParentUid == xform.MapUid;
    }
    // End EE
}

[Serializable, NetSerializable]
public enum JetpackVisuals : byte
{
    Enabled,
}
