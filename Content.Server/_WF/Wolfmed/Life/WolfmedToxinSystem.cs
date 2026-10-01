using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>Toxin load from the body's systemic Poison: downs, then a toxic coma, cleared by a working liver.</summary>
// Past wolfmed.consc_toxin_down the patient is Downed, past wolfmed.consc_toxin_out in a toxic coma that keeps
// breathing and drains the brain on its own clock; the heart stops through the oxygen trigger, named "toxin". A
// working liver clears wolfmed.toxin_clearance a second, the one passive healing a wound host gets. Machines take
// no Poison.
public sealed class WolfmedToxinSystem : EntitySystem
{
    public static readonly ProtoId<DamageTypePrototype> Poison = "Poison";

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedDamageableSystem _damageable = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!;

    /// <summary>Systemic Poison: the toxin load.</summary>
    public float GetLoad(EntityUid body) =>
        CompOrNull<SystemicDamageComponent>(body)?.Damage.DamageDict.GetValueOrDefault(Poison).Float() ?? 0f;

    /// <summary>The load against the Downed and the coma lines: 1 at each line.</summary>
    public (float Down, float Out) GetLevels(EntityUid body)
    {
        var load = GetLoad(body);
        if (load <= 0f)
            return (0f, 0f);

        var down = _cfg.GetCVar(WolfmedCVars.ConsciousnessToxinDown);
        var outLine = _cfg.GetCVar(WolfmedCVars.ConsciousnessToxinOut);
        return (down > 0f ? load / down : 0f, outLine > 0f ? load / outLine : 0f);
    }

    /// <summary>A toxic coma: the load at or past the coma line, where the brain drains.</summary>
    public bool InComa(EntityUid body)
    {
        var line = _cfg.GetCVar(WolfmedCVars.ConsciousnessToxinOut);
        return line > 0f && GetLoad(body) >= line && !_mobState.IsDead(body);
    }

    /// <summary>The toxic coma's brain drain, oxygenation a second.</summary>
    public float DrainRate(EntityUid body)
    {
        var seconds = _cfg.GetCVar(WolfmedCVars.BrainToxinSeconds);
        return seconds > 0f && InComa(body) ? 1f / seconds : 0f;
    }

    /// <summary>
    /// The liver as clearance sees it: none, or its band. A liver carrying no Wolfmed organ data counts as working.
    /// </summary>
    public WolfmedLiverState GetLiver(EntityUid body)
    {
        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (!HasComp<LiverComponent>(organ))
                continue;

            if (!TryComp(organ, out WolfmedOrganComponent? health))
                return WolfmedLiverState.Working;

            return health.Band switch
            {
                WolfmedOrganBand.Failed => WolfmedLiverState.Failed,
                WolfmedOrganBand.Impaired => WolfmedLiverState.Impaired,
                _ => WolfmedLiverState.Working,
            };
        }

        return WolfmedLiverState.None;
    }

    /// <summary>
    /// Poison cleared a second: wolfmed.toxin_clearance through a working liver, times the organ's
    /// impairedClearanceFactor while it is impaired, nothing through a failed or missing one. Nothing for the dead
    /// or a machine.
    /// </summary>
    public float GetClearanceRate(EntityUid body)
    {
        if (_mobState.IsDead(body) || _shutdown.IsMechanical(body))
            return 0f;

        var rate = MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.ToxinClearance));
        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (!HasComp<LiverComponent>(organ))
                continue;

            if (!TryComp(organ, out WolfmedOrganComponent? health))
                return rate;

            return health.Band switch
            {
                WolfmedOrganBand.Failed => 0f,
                WolfmedOrganBand.Impaired => rate * Math.Clamp(health.ImpairedClearanceFactor, 0f, 1f),
                _ => rate,
            };
        }

        return 0f;
    }

    /// <summary>One clearance step: the liver takes Poison out of the body. Called from the life tick.</summary>
    public void Clear(EntityUid body, float seconds)
    {
        var load = GetLoad(body);
        if (load <= 0f || seconds <= 0f)
            return;

        var amount = MathF.Min(load, GetClearanceRate(body) * seconds);
        if (amount < 0.01f)
            return;

        _damageable.ChangeDamage(body,
            new DamageSpecifier { DamageDict = { [Poison] = FixedPoint2.New(-amount) } },
            ignoreResistances: true,
            interruptsDoAfters: false);
    }
}
