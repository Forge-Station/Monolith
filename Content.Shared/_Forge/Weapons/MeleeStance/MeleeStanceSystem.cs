using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Actions;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Reflect;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Forge.Weapons.MeleeStance;

/// <summary>
/// Handles <see cref="MeleeStanceComponent"/>: grants the switch action and applies stance stats.
/// Visuals (world sprite, transition animation) are handled on the client by MeleeStanceVisualsSystem.
/// </summary>
public sealed partial class MeleeStanceSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private ClothingSystem _clothing = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPointLightSystem _light = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MeleeStanceComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MeleeStanceComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MeleeStanceComponent, AfterAutoHandleStateEvent>(OnAfterState);
        SubscribeLocalEvent<MeleeStanceComponent, GetItemActionsEvent>(OnGetItemActions);
        SubscribeLocalEvent<MeleeStanceComponent, MeleeStanceSwitchActionEvent>(OnSwitchAction);
        SubscribeLocalEvent<MeleeStanceComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<MeleeStanceComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<MeleeStanceComponent, ItemWieldedEvent>(OnWielded, after: [typeof(SharedWieldableSystem)]);
        SubscribeLocalEvent<MeleeStanceComponent, ItemUnwieldedEvent>(OnUnwielded, after: [typeof(SharedWieldableSystem)]);
    }

    #region Public API

    /// <summary>
    /// Tries to get the currently active stance.
    /// </summary>
    public bool TryGetMode(Entity<MeleeStanceComponent?> ent, [NotNullWhen(true)] out MeleeStanceMode? mode)
    {
        mode = null;
        if (!Resolve(ent, ref ent.Comp, false))
            return false;

        if (ent.Comp.CurrentMode < 0 || ent.Comp.CurrentMode >= ent.Comp.Modes.Count)
            return false;

        mode = ent.Comp.Modes[ent.Comp.CurrentMode];
        return true;
    }

    /// <summary>
    /// Switches to the next stance (wraps around).
    /// </summary>
    public bool TryCycle(Entity<MeleeStanceComponent?> ent, EntityUid? user)
    {
        if (!Resolve(ent, ref ent.Comp) || ent.Comp.Modes.Count < 2)
            return false;

        return TrySetMode(ent, (ent.Comp.CurrentMode + 1) % ent.Comp.Modes.Count, user);
    }

    /// <summary>
    /// Switches to a specific stance.
    /// </summary>
    public bool TrySetMode(Entity<MeleeStanceComponent?> ent, int index, EntityUid? user)
    {
        if (!Resolve(ent, ref ent.Comp))
            return false;

        var comp = ent.Comp;
        if (index < 0 || index >= comp.Modes.Count || index == comp.CurrentMode)
            return false;

        var attempt = new MeleeStanceSwitchAttemptEvent(user, comp.CurrentMode, index);
        RaiseLocalEvent(ent.Owner, ref attempt);
        if (attempt.Cancelled)
            return false;

        comp.CurrentMode = index;
        Dirty(ent.Owner, comp);

        var mode = comp.Modes[index];
        ApplyMode((ent.Owner, comp), user);

        _audio.PlayPredicted(mode.Sound ?? comp.SwitchSound, ent.Owner, user);

        if (comp.ShowPopup && user != null)
        {
            _popup.PopupClient(Loc.GetString("melee-stance-switched", ("stance", Loc.GetString(mode.Name))),
                ent.Owner,
                user.Value);
        }

        if (comp.AttackDelayOnSwitch > TimeSpan.Zero && TryComp<MeleeWeaponComponent>(ent.Owner, out var melee))
        {
            var next = _timing.CurTime + comp.AttackDelayOnSwitch;
            if (melee.NextAttack < next)
            {
                melee.NextAttack = next;
                Dirty(ent.Owner, melee);
            }
        }

        return true;
    }

    #endregion

    #region Event handlers

    private void OnMapInit(Entity<MeleeStanceComponent> ent, ref MapInitEvent args)
    {
        _actionContainer.EnsureAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);

        if (ent.Comp.Modes.Count == 0)
        {
            Log.Error($"{ToPrettyString(ent)} has {nameof(MeleeStanceComponent)} without any modes!");
            return;
        }

        ent.Comp.CurrentMode = Math.Clamp(ent.Comp.CurrentMode, 0, ent.Comp.Modes.Count - 1);
        Dirty(ent);

        ApplyMode(ent, null);
    }

    /// <summary>
    /// Client-side: entities that came from the server never get MapInit, so apply non-networked stats here.
    /// </summary>
    private void OnStartup(Entity<MeleeStanceComponent> ent, ref ComponentStartup args)
    {
        if (_net.IsClient)
            SyncFromState(ent);
    }

    private void OnAfterState(Entity<MeleeStanceComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        SyncFromState(ent);
    }

    private void OnGetItemActions(Entity<MeleeStanceComponent> ent, ref GetItemActionsEvent args)
    {
        if (ent.Comp.RequireInHand && !args.InHands)
            return;

        if (ent.Comp.Modes.Count < 2)
            return;

        args.AddAction(ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnSwitchAction(Entity<MeleeStanceComponent> ent, ref MeleeStanceSwitchActionEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.RequireInHand && !_hands.IsHolding(args.Performer, ent))
            return;

        args.Handled = TryCycle(ent.AsNullable(), args.Performer);
    }

    private void OnUseInHand(Entity<MeleeStanceComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || !ent.Comp.SwitchOnUseInHand)
            return;

        args.Handled = TryCycle(ent.AsNullable(), args.User);
    }

    private void OnExamined(Entity<MeleeStanceComponent> ent, ref ExaminedEvent args)
    {
        if (!TryGetMode(ent.AsNullable(), out var mode))
            return;

        using (args.PushGroup(nameof(MeleeStanceComponent)))
        {
            args.PushMarkup(Loc.GetString("melee-stance-examine", ("stance", Loc.GetString(mode.Name))));

            if (mode.Description != null)
                args.PushMarkup(Loc.GetString(mode.Description.Value));
        }
    }

    private void OnWielded(Entity<MeleeStanceComponent> ent, ref ItemWieldedEvent args)
    {
        if (TryGetMode(ent.AsNullable(), out var mode))
            ApplyHeldPrefix(ent, mode);
    }

    private void OnUnwielded(Entity<MeleeStanceComponent> ent, ref ItemUnwieldedEvent args)
    {
        if (TryGetMode(ent.AsNullable(), out var mode))
            ApplyHeldPrefix(ent, mode);
    }

    #endregion

    #region Applying

    /// <summary>
    /// Re-applies the stance received from the server state if it differs from what we have locally.
    /// </summary>
    private void SyncFromState(Entity<MeleeStanceComponent> ent)
    {
        if (ent.Comp.AppliedMode == ent.Comp.CurrentMode)
            return;

        if (!TryGetMode(ent.AsNullable(), out _))
            return;

        ApplyMode(ent, null);
    }

    private void ApplyMode(Entity<MeleeStanceComponent> ent, EntityUid? user)
    {
        if (!TryGetMode(ent.AsNullable(), out var mode))
            return;

        var previous = ent.Comp.AppliedMode;

        ApplyMelee(ent, mode);
        ApplyReflect(ent, mode);
        ApplyStamina(ent, mode);
        ApplyItem(ent, mode);
        ApplyLight(ent, mode);
        ApplyAction(ent, mode);
        ApplyComponents(ent, previous, mode);

        ent.Comp.AppliedMode = ent.Comp.CurrentMode;

        var ev = new MeleeStanceChangedEvent(previous, ent.Comp.CurrentMode, user);
        RaiseLocalEvent(ent, ref ev);
    }

    private void ApplyMelee(EntityUid uid, MeleeStanceMode mode)
    {
        if (!TryComp<MeleeWeaponComponent>(uid, out var melee))
            return;

        var proto = GetPrototypeComp<MeleeWeaponComponent>(uid) ?? melee;

        var damage = mode.Damage ?? proto.Damage;
        melee.Damage = new DamageSpecifier(damage);
        melee.AttackRate = mode.AttackRate ?? proto.AttackRate;
        melee.Range = mode.Range ?? proto.Range;
        melee.Angle = mode.Angle ?? proto.Angle;
        melee.ArmorPenetration = mode.ArmorPenetration ?? proto.ArmorPenetration;
        melee.ResistanceBypass = mode.ResistanceBypass ?? proto.ResistanceBypass;
        melee.CanWideSwing = mode.CanWideSwing ?? proto.CanWideSwing;
        melee.AltDisarm = mode.AltDisarm ?? proto.AltDisarm;
        melee.HeavyStaminaCost = mode.HeavyStaminaCost ?? proto.HeavyStaminaCost;
        melee.ClickDamageModifier = mode.ClickDamageModifier ?? proto.ClickDamageModifier;
        melee.ClickPartDamageMultiplier = mode.ClickPartDamageMultiplier ?? proto.ClickPartDamageMultiplier;
        melee.HeavyPartDamageMultiplier = mode.HeavyPartDamageMultiplier ?? proto.HeavyPartDamageMultiplier;
        melee.Hidden = mode.Hidden ?? proto.Hidden;
        melee.HitSound = mode.SoundHit ?? proto.HitSound;
        melee.SwingSound = mode.SoundSwing ?? proto.SwingSound;
        melee.NoDamageSound = mode.SoundNoDamage ?? proto.NoDamageSound;
        melee.Animation = mode.Animation ?? proto.Animation;
        melee.WideAnimation = mode.WideAnimation ?? proto.WideAnimation;
        melee.WideAnimationRotation = mode.WideAnimationRotation ?? proto.WideAnimationRotation;

        Dirty(uid, melee);
    }

    private void ApplyReflect(EntityUid uid, MeleeStanceMode mode)
    {
        if (!TryComp<ReflectComponent>(uid, out var reflect))
        {
            if (mode.ReflectProb is > 0)
                Log.Warning($"{ToPrettyString(uid)} has a reflecting stance but no {nameof(ReflectComponent)}.");
            return;
        }

        var proto = GetPrototypeComp<ReflectComponent>(uid) ?? reflect;

        reflect.ReflectProb = mode.ReflectProb ?? proto.ReflectProb;
        reflect.Spread = mode.ReflectSpread ?? proto.Spread;
        reflect.SoundOnReflect = mode.SoundOnReflect ?? proto.SoundOnReflect;
        reflect.DamageOnReflectModifier = mode.DamageOnReflectModifier ?? proto.DamageOnReflectModifier;

        if (mode.Reflects != null)
        {
            var flags = ReflectType.None;
            foreach (var type in mode.Reflects)
            {
                flags |= type;
            }

            reflect.Reflects = flags;
        }
        else
        {
            reflect.Reflects = proto.Reflects;
        }

        Dirty(uid, reflect);
    }

    private void ApplyStamina(EntityUid uid, MeleeStanceMode mode)
    {
        if (!TryComp<StaminaDamageOnHitComponent>(uid, out var stamina))
            return;

        var proto = GetPrototypeComp<StaminaDamageOnHitComponent>(uid) ?? stamina;
        stamina.Damage = mode.StaminaDamage ?? proto.Damage;
    }

    private void ApplyItem(Entity<MeleeStanceComponent> ent, MeleeStanceMode mode)
    {
        if (TryComp<ItemComponent>(ent, out var item))
        {
            var proto = GetPrototypeComp<ItemComponent>(ent);
            var size = mode.Size ?? proto?.Size;
            if (size != null && size.Value != item.Size)
                _item.SetSize(ent, size.Value, item);
        }

        ApplyHeldPrefix(ent, mode);

        if (TryComp<ClothingComponent>(ent, out var clothing)
            && ent.Comp.Modes.Any(m => m.EquippedPrefix != null))
        {
            _clothing.SetEquippedPrefix(ent, mode.EquippedPrefix, clothing);
        }
    }

    private void ApplyHeldPrefix(Entity<MeleeStanceComponent> ent, MeleeStanceMode mode)
    {
        if (!TryComp<ItemComponent>(ent, out var item))
            return;

        var wielded = TryComp<WieldableComponent>(ent, out var wieldable) && wieldable.Wielded;

        if (wielded)
        {
            // Keep the Wieldable prefix unless the stance has its own wielded sprites.
            if (mode.WieldedHeldPrefix != null)
                _item.SetHeldPrefix(ent, mode.WieldedHeldPrefix, component: item);
            return;
        }

        if (ent.Comp.Modes.Any(m => m.HeldPrefix != null))
            _item.SetHeldPrefix(ent, mode.HeldPrefix, component: item);
    }

    private void ApplyLight(EntityUid uid, MeleeStanceMode mode)
    {
        SharedPointLightComponent? light = null;
        if (!_light.ResolveLight(uid, ref light))
            return;

        if (mode.LightColor != null)
            _light.SetColor(uid, mode.LightColor.Value, light);

        if (mode.LightRadius != null)
            _light.SetRadius(uid, mode.LightRadius.Value, light);

        if (mode.LightEnergy != null)
            _light.SetEnergy(uid, mode.LightEnergy.Value, light);

        if (mode.LightEnabled != null)
            _light.SetEnabled(uid, mode.LightEnabled.Value, light);
    }

    private void ApplyAction(Entity<MeleeStanceComponent> ent, MeleeStanceMode mode)
    {
        if (ent.Comp.ActionEntity is not { } actionId)
            return;

        if (!_actions.TryGetActionData(actionId, out var action, false))
            return;

        // Highlight the action while in any non-default stance.
        _actions.SetToggled(actionId, ent.Comp.CurrentMode != 0);

        if (mode.ActionIcon != null && action.Icon != mode.ActionIcon)
        {
            action.Icon = mode.ActionIcon;
            Dirty(actionId, action);
        }
    }

    private void ApplyComponents(Entity<MeleeStanceComponent> ent, int? previous, MeleeStanceMode mode)
    {
        // Component add/remove is server-authoritative, the result is networked to clients.
        if (_net.IsClient)
            return;

        if (previous is { } prev
            && prev != ent.Comp.CurrentMode
            && prev >= 0
            && prev < ent.Comp.Modes.Count
            && ent.Comp.Modes[prev].Components is { } oldComps)
        {
            EntityManager.RemoveComponents(ent, oldComps);
        }

        if (mode.Components != null)
            EntityManager.AddComponents(ent, mode.Components);
    }

    /// <summary>
    /// Gets a component as defined in the entity's prototype (read-only!). Used as the "base" for stance values.
    /// </summary>
    private T? GetPrototypeComp<T>(EntityUid uid) where T : class, IComponent, new()
    {
        var proto = MetaData(uid).EntityPrototype;
        if (proto == null)
            return null;

        return proto.TryComp<T>(out var comp, _factory) ? comp : null;
    }

    #endregion
}