// WOLFGATE(Wolfmed): StatusEffectNew framework vendored from Space Onyx (2f5bab9), Onyx's copy of upstream SS14's, for the wound port.
using Content.Shared.Body.Events;
// WOLFGATE(Wolfmed): Onyx's _Onyx.MartialArts is not ported; the melee target modifier relay below is dropped.
using Content.Shared.Atmos;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Events;
using Content.Shared.Climbing.Events;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Damage; // WOLFGATE(Wolfmed): Wolfgate keeps DamageModifyEvent and ModifySlowOnDamageSpeedEvent in Content.Shared.Damage.
using Content.Shared.Examine;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Flash;
using Content.Shared.Medical;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Events;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Rejuvenate;
using Content.Shared.Speech;
using Content.Shared.Speech.EntitySystems;
using Content.Shared.StatusEffectNew.Components;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Collections;
using Robust.Shared.Player;

namespace Content.Shared.StatusEffectNew;

public sealed partial class StatusEffectsSystem
{
    private void InitializeRelay()
    {
        SubscribeLocalEvent<StatusEffectContainerComponent, LocalPlayerAttachedEvent>(RelayStatusEffectEvent);
        SubscribeLocalEvent<StatusEffectContainerComponent, LocalPlayerDetachedEvent>(RelayStatusEffectEvent);
        SubscribeLocalEvent<StatusEffectContainerComponent, RejuvenateEvent>(RelayStatusEffectEvent);

        SubscribeLocalEvent<StatusEffectContainerComponent, RefreshMovementSpeedModifiersEvent>(RelayStatusEffectEvent);
        SubscribeLocalEvent<StatusEffectContainerComponent, ModifySlowOnDamageSpeedEvent>(RefRelayStatusEffectEvent);
        SubscribeLocalEvent<StatusEffectContainerComponent, UpdateCanMoveEvent>(RelayStatusEffectEvent);

        // By Ref Events
        SubscribeLocalEvent<StatusEffectContainerComponent, MobStateChangedEvent>(RefRelayStatusEffectEvent);

        SubscribeLocalEvent<StatusEffectContainerComponent, RefreshFrictionModifiersEvent>(RefRelayStatusEffectEvent);
        SubscribeLocalEvent<StatusEffectContainerComponent, TileFrictionEvent>(RefRelayStatusEffectEvent);

        // WOLFGATE(Wolfmed): StandUpAttemptEvent, StunEndAttemptEvent and RefreshStaminaCritThresholdEvent do not exist in Wolfgate.
        SubscribeLocalEvent<StatusEffectContainerComponent, CanSeeAttemptEvent>(RelayStatusEffectEvent);
        SubscribeLocalEvent<StatusEffectContainerComponent, GetBlurEvent>(RelayStatusEffectEvent); // <Onyx-MartialArts>
        // WOLFGATE(Wolfmed): the melee target modifier relay belongs to Onyx's MartialArts, which is not ported.
        SubscribeLocalEvent<StatusEffectContainerComponent, FlashAttemptEvent>(RefRelayStatusEffectEvent);
        SubscribeLocalEvent<StatusEffectContainerComponent, SelfBeforeClimbEvent>(RelayStatusEffectEvent);

        SubscribeLocalEvent<StatusEffectContainerComponent, BeforeForceSayEvent>(RelayStatusEffectEvent);
        SubscribeLocalEvent<StatusEffectContainerComponent, BeforeAlertSeverityCheckEvent>(RelayStatusEffectEvent);
        SubscribeLocalEvent<StatusEffectContainerComponent, SpeakAttemptEvent>(RelayStatusEffectEvent);
        SubscribeLocalEvent<StatusEffectContainerComponent, ExaminedEvent>(RelayStatusEffectEvent);

        // WOLFGATE(Wolfmed): EmoteActionEvent does not exist; EmoteEvent, AccentGetEvent, VocalSystem and MumbleAccentSystem are server-side in Wolfgate.

        // WOLFGATE(Wolfmed): BleedModifierEvent does not exist; Wolfgate bleeding is a server-side BleedAmount.
        SubscribeLocalEvent<StatusEffectContainerComponent, DamageModifyEvent>(RelayStatusEffectEvent);
        // WOLFGATE(Wolfmed): RefreshPressureImmunityEvent does not exist in Wolfgate.
        SubscribeLocalEvent<StatusEffectContainerComponent, SelfBeforeDefibrillatorZapsEvent>(RelayStatusEffectEvent);
        // WOLFGATE(Wolfmed): SelfBeforeInjectEvent does not exist in Wolfgate.

        // WOLFGATE(Wolfmed): CatchAttemptEvent does not exist in Wolfgate.
        SubscribeLocalEvent<StatusEffectContainerComponent, SelfBeforeGunShotEvent>(RelayStatusEffectEvent);
    }

    private void RefRelayStatusEffectEvent<T>(EntityUid uid, StatusEffectContainerComponent component, ref T args) where T : struct
    {
        RelayEvent((uid, component), ref args);
    }

    private void RelayStatusEffectEvent<T>(EntityUid uid, StatusEffectContainerComponent component, T args) where T : class
    {
        RelayEvent((uid, component), args);
    }

    public void RelayEvent<T>(Entity<StatusEffectContainerComponent> statusEffect, ref T args) where T : struct
    {
        if(statusEffect.Comp.ActiveStatusEffects?.ContainedEntities is not {} originalCollection || originalCollection.Count == 0)
            return;

        // this copies the by-ref event if it is a struct
        var ev = new StatusEffectRelayedEvent<T>(args, statusEffect);

        // Prevent a collection modified enumeration error by copying the list in case a status adds another status
        var list = new ValueList<EntityUid>(originalCollection);
        foreach (var activeEffect in list)
        {
            RaiseLocalEvent(activeEffect, ref ev);
        }
        // and now we copy it back
        args = ev.Args;
    }

    public void RelayEvent<T>(Entity<StatusEffectContainerComponent> statusEffect, T args) where T : class
    {
        if (statusEffect.Comp.ActiveStatusEffects?.ContainedEntities is not { } originalCollection || originalCollection.Count == 0)
            return;

        // this copies the by-ref event if it is a struct
        var ev = new StatusEffectRelayedEvent<T>(args, statusEffect);

        // Prevent a collection modified enumeration error by copying the list in case a status adds another status
        var list = new ValueList<EntityUid>(originalCollection);
        foreach (var activeEffect in list)
        {
            RaiseLocalEvent(activeEffect, ref ev);
        }
    }
}

/// <summary>
/// Event wrapper for relayed events.
/// </summary>
[ByRefEvent]
public record struct StatusEffectRelayedEvent<TEvent>(TEvent Args, EntityUid AppliedTo);
