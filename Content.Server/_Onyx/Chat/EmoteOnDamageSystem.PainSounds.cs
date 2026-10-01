using Content.Shared.Chat;
using Content.Shared._Onyx.Wounds; // WOLFGATE(Wolfmed): P2-D22 wound-host gate.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): D12 damage facade.
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Damage.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;
using Content.Shared.StatusEffectNew;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Random;

namespace Content.Server.Chat.Systems;

public sealed partial class EmoteOnDamageSystem
{
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private WolfmedDamageableSystem _damageable = default!; // WOLFGATE(Wolfmed): D12, vendored files bind the facade, never DamageableSystem.

    private void HandlePainDamageEmote(EntityUid uid, EmoteOnDamageComponent component, DamageChangedEvent args)
    {
        // WOLFGATE(Wolfmed): P2-D22: D32 strips only WoundHostComponent from Protogen, so without this gate the
        // D32-excluded species would still get Wolfmed's pain screams - a D2 breach. Onyx needs no such
        // check because every mob there is a wound host.
        if (!HasComp<WoundHostComponent>(uid))
            return;

        var totalDamage = _damageable.GetTotalDamage(uid).Float();
        var totalDelta = totalDamage - component.LastTotalDamage;
        component.LastTotalDamage = totalDamage;

        if (component.EmotesThreshold.Count == 0 || totalDelta <= 0 ||
            component.LastEmoteTime + component.EmoteCooldown > _gameTiming.CurTime ||
            !_random.Prob(component.EmoteChance) ||
            TryComp<MobStateComponent>(uid, out var mob) && mob.CurrentState is MobState.Critical or MobState.Dead ||
            _statusEffects.TryEffectsWithComp<PainNumbnessStatusEffectComponent>(uid, out _) ||
            HasComp<PainNumbnessComponent>(uid)) // WOLFGATE(Wolfmed): P2-D8 parity - Wolfgate's PainNumbness trait grants the legacy component.
            return;

        var pain = args.DamageDelta is null ? totalDelta : 0f;
        if (args.DamageDelta is { } delta)
        {
            foreach (var (type, amount) in delta.DamageDict)
            {
                if (component.AllowedDamageType.Contains(type))
                    pain += amount.Float();
            }
        }

        if (pain < component.PainThreshold)
            return;

        float? threshold = null;
        foreach (var candidate in component.EmotesThreshold.Keys)
        {
            if (totalDamage >= candidate && (threshold == null || candidate > threshold))
                threshold = candidate;
        }

        if (threshold == null || !component.EmotesThreshold.TryGetValue(threshold.Value, out var emotes) || emotes.Count == 0)
            return;

        var emote = _random.Pick(emotes);
        if (component.WithChat)
            _chatSystem.TryEmoteWithChat(uid, emote, component.HiddenFromChatWindow ? ChatTransmitRange.HideChat : ChatTransmitRange.Normal);
        else
            _chatSystem.TryEmoteWithoutChat(uid, emote);

        component.LastEmoteTime = _gameTiming.CurTime;
    }
}
