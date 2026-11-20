using Content.Server.Body.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>Radiation's marrow route: stops blood regeneration, then drains blood, and downs the patient.</summary>
// Past wolfmed.rad_marrow_stop no blood regenerates; past wolfmed.rad_marrow_bleed the body also loses
// wolfmed.rad_marrow_rate a second, so it kills through the blood route medics already know. Past
// wolfmed.consc_rad_down the patient is Downed, cause Radiation. Machines have no marrow: radiation stays their
// slowdown and pain.
public sealed class WolfmedRadiationSystem : EntitySystem
{
    public static readonly ProtoId<DamageTypePrototype> Radiation = "Radiation";

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!;

    /// <summary>Systemic Radiation on the body.</summary>
    public float GetRadiation(EntityUid body) =>
        CompOrNull<SystemicDamageComponent>(body)?.Damage.DamageDict.GetValueOrDefault(Radiation).Float() ?? 0f;

    /// <summary>The marrow has stopped: 0 from wolfmed.rad_marrow_stop, else 1. The bloodstream's regeneration reads it.</summary>
    public float RegenFactor(EntityUid body)
    {
        var line = _cfg.GetCVar(WolfmedCVars.RadiationMarrowStop);
        return line > 0f && GetRadiation(body) >= line && !_shutdown.IsMechanical(body) ? 0f : 1f;
    }

    /// <summary>Units of blood a second the failing marrow costs, 0 under wolfmed.rad_marrow_bleed.</summary>
    public float GetMarrowLossRate(EntityUid body)
    {
        var line = _cfg.GetCVar(WolfmedCVars.RadiationMarrowBleed);
        if (line <= 0f || GetRadiation(body) < line || _mobState.IsDead(body) || _shutdown.IsMechanical(body) ||
            !HasComp<BloodstreamComponent>(body))
            return 0f;

        return MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.RadiationMarrowRate));
    }

    /// <summary>Radiation against the Downed line: 1 at the line. Never unconscious; nothing for a machine.</summary>
    public float GetDownLevel(EntityUid body)
    {
        var line = _cfg.GetCVar(WolfmedCVars.ConsciousnessRadiationDown);
        var radiation = GetRadiation(body);
        return line > 0f && radiation > 0f ? radiation / line : 0f;
    }

    /// <summary>
    /// One marrow step from the life tick: the blood the failing marrow costs, straight out of the solution. Nothing
    /// reaches the floor; a sub-hundredth remainder waits for the next step.
    /// </summary>
    public void Tick(EntityUid body, float seconds)
    {
        var rate = GetMarrowLossRate(body);
        if (rate <= 0f || seconds <= 0f || !TryComp(body, out BloodstreamComponent? bloodstream) ||
            !_solutions.ResolveSolution(body, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out _))
            return;

        var marrow = EnsureComp<WolfmedMarrowLossComponent>(body);
        marrow.Owed += rate * seconds;
        var take = FixedPoint2.New(marrow.Owed);
        if (take <= FixedPoint2.Zero)
            return;

        marrow.Owed -= take.Float();
        _solutions.SplitSolution(bloodstream.BloodSolution.Value, take);
    }
}

/// <summary>M5: the part of a unit of blood the failing marrow still owes, so short steps are not rounded away.</summary>
[RegisterComponent, Access(typeof(WolfmedRadiationSystem))]
public sealed partial class WolfmedMarrowLossComponent : Component
{
    [ViewVariables]
    public float Owed;
}
