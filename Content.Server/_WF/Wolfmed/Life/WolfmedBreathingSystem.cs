using Content.Server.Body.Components;
using Content.Shared._Shitmed.Body.Components;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>
/// Who breathes (M1a, plan §4). A wound host breathes in every state but a stopped heart and death; pain,
/// blood loss and hypoxia have routes of their own and never stop the respirator. Everything else keeps the
/// upstream rule that an incapacitated body does not inhale.
/// </summary>
public sealed class WolfmedBreathingSystem : EntitySystem
{
    private static readonly ProtoId<DamageTypePrototype> Asphyxiation = "Asphyxiation";

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedConsciousnessSystem _consciousness = default!;
    [Dependency] private WolfmedPainReliefSystem _relief = default!;

    /// <summary>
    /// The respirator's "does not inhale" rule. The one marked hook in <c>RespiratorSystem.Update</c> asks
    /// this instead of <see cref="MobStateSystem.IsIncapacitated"/>.
    /// </summary>
    public bool BreathingSuppressed(EntityUid uid)
    {
        if (!_consciousness.OwnsMobState(uid))
            return _mobState.IsIncapacitated(uid);

        return _mobState.IsDead(uid) || HasComp<WolfmedCardiacArrestComponent>(uid);
    }

    /// <summary>
    /// The respirator is suffocating right now: as many short cycles in a row as it takes to raise its own
    /// suffocation alert. One short cycle is not suffocation; a body getting its breath back after a long
    /// spell without air dips under the line for a single cycle while its saturation refills.
    /// </summary>
    public bool IsSuffocating(EntityUid body) =>
        TryComp(body, out RespiratorComponent? respirator) && IsSuffocating(respirator);

    private static bool IsSuffocating(RespiratorComponent respirator) =>
        respirator.SuffocationCycles >= Math.Max(1, respirator.SuffocationCycleThreshold);

    /// <summary>
    /// How far from breathing the body is, 0 to 1: nothing unless the respirator is suffocating right now,
    /// then Asphyxiation against <see cref="WolfmedCVars.AirlossFull"/>. Bloodloss is never read.
    /// </summary>
    public float SuffocationLevel(EntityUid body)
    {
        if (!TryComp(body, out RespiratorComponent? respirator) || !IsSuffocating(respirator))
            return 0f;

        var full = _cfg.GetCVar(WolfmedCVars.AirlossFull);
        if (full <= 0f)
            return 1f;

        if (!TryComp(body, out DamageableComponent? damageable) ||
            !damageable.Damage.DamageDict.TryGetValue(Asphyxiation, out var asphyxiation))
            return 0f;

        return Math.Clamp(asphyxiation.Float() / full, 0f, 1f);
    }

    /// <summary>
    /// M3 (plan §3.3): damaged lungs as a breathing input, 0 to 1. Under their impaired line the lungs give
    /// (line - health fraction) / line, times <see cref="WolfmedCVars.LungDamageFactor"/>; several lungs are
    /// averaged. No lungs at all is the respirator's suffocation, not this.
    /// </summary>
    public float LungDamageLevel(EntityUid body)
    {
        var factor = _cfg.GetCVar(WolfmedCVars.LungDamageFactor);
        if (factor <= 0f || !TryComp(body, out BodyComponent? bodyComp))
            return 0f;

        var total = 0f;
        var count = 0;
        foreach (var lung in _body.GetBodyOrganEntityComps<LungComponent>((body, bodyComp)))
        {
            if (!TryComp(lung.Owner, out WolfmedOrganComponent? health) || health.ImpairedBelow <= 0f)
                continue;

            total += Math.Clamp((health.ImpairedBelow - health.Fraction) / health.ImpairedBelow, 0f, 1f);
            count++;
        }

        return count == 0 ? 0f : Math.Clamp(total / count * factor, 0f, 1f);
    }

    /// <summary>What the chest is doing and why, in the order a medic would rule things out.</summary>
    public (WolfmedBreathing Breathing, WolfmedBreathingSource Source) Assess(EntityUid body)
    {
        if (_mobState.IsDead(body))
            return (WolfmedBreathing.None, WolfmedBreathingSource.Dead);

        if (HasComp<WolfmedCardiacArrestComponent>(body))
            return (WolfmedBreathing.None, WolfmedBreathingSource.Arrest);

        if (!TryComp(body, out RespiratorComponent? respirator) || HasComp<BreathingImmunityComponent>(body))
            return (WolfmedBreathing.Normal, WolfmedBreathingSource.None);

        if (HasComp<DebrainedComponent>(body))
            return (WolfmedBreathing.None, WolfmedBreathingSource.NoBrain);

        if (TryComp(body, out BodyComponent? bodyComp) &&
            _body.GetBodyOrganEntityComps<LungComponent>((body, bodyComp)).Count == 0)
            return (WolfmedBreathing.None, WolfmedBreathingSource.Lungs);

        if (IsSuffocating(respirator))
            return (WolfmedBreathing.Gasping, WolfmedBreathingSource.NoAir);

        // M3: impaired lungs breathe, but not enough (plan §3.3, §8).
        if (LungDamageLevel(body) > 0f)
            return (WolfmedBreathing.Laboured, WolfmedBreathingSource.LungsDamaged);

        if (_relief.GetRespiratoryDepression(body) > 0f)
            return (WolfmedBreathing.Depressed, WolfmedBreathingSource.Sedation);

        return (WolfmedBreathing.Normal, WolfmedBreathingSource.None);
    }
}
