using Content.Server.Body.Components;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;

namespace Content.Server.Medical;

/// <summary>
/// M1a: the analyzer's vitals block (plan §5.5): the state and cause off the consciousness component, the
/// breathing and circulation read fresh from the breathing and life systems. The pod mounts the same panel,
/// so it gets the block too.
/// </summary>
public sealed partial class HealthAnalyzerSystem
{
    [Dependency] private IConfigurationManager _vitalsCfg = default!;
    [Dependency] private MobStateSystem _vitalsMobState = default!;
    [Dependency] private WolfmedConsciousnessSystem _vitalsConsciousness = default!;
    [Dependency] private WolfmedRevivalSystem _vitalsRevival = default!;
    [Dependency] private WolfmedBreathingSystem _vitalsBreathing = default!;
    [Dependency] private Content.Server._WF.Wolfmed.Wounds.WolfmedFluidLossSystem _vitalsFluidLoss = default!; // M1b
    [Dependency] private WolfmedCardSystem _vitalsCard = default!; // M2
    [Dependency] private WolfmedOverheatSystem _vitalsOverheat = default!; // M4

    [Dependency] private WolfmedToxinSystem _vitalsToxin = default!; // M5
    [Dependency] private WolfmedRadiationSystem _vitalsRadiation = default!;
    [Dependency] private WolfmedBodyTemperatureSystem _vitalsTemperature = default!;

    /// <summary>State and cause, breathing, circulation and the defib verdict, or null when consciousness does not run this body.</summary>
    public WolfmedVitalsReport? BuildVitals(EntityUid body)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? consciousness) ||
            !_vitalsConsciousness.OwnsMobState(body))
            return null;

        var mechanical = _shutdown.IsMechanical(body);
        var report = new WolfmedVitalsReport
        {
            State = GetVitalsState(body, consciousness, mechanical),
            Cause = consciousness.Cause,
            Source = consciousness.CauseSource,
            Blockers = consciousness.Blockers,
            Mechanical = mechanical,
            Sedation = CompOrNull<WolfmedPainReliefComponent>(body)?.Sedation ?? 0f,
            PumpRunning = mechanical && _shutdown.HasPump(body),
            // Read fresh rather than off the networked copy the life tick writes once a second, so the pulse
            // words always agree with the blood % on the same line.
            BloodBand = _life.GetBloodBand(body),
            Heartless = consciousness.Heartless, // M4 (OD16)
        };
        (report.Breathing, report.BreathingSource) = _vitalsBreathing.Assess(body);
        if (report.State == WolfmedVitalsState.Faint)
            report.FaintSeconds = _vitalsConsciousness.GetFaintSecondsLeft(body) ?? -1;

        if (report.State == WolfmedVitalsState.Dead)
        {
            report.BrainDestroyed = _life.GetBrainOrgan(body) is { } brain && brain.Comp.Health <= FixedPoint2.Zero;
            report.Cause = WolfmedCause.None;
        }

        if (TryComp(body, out BloodstreamComponent? bloodstream))
        {
            report.Blood = _life.GetBlood(body);
            report.Trend = GetBloodTrend(body, bloodstream, report.Blood);
            report.UnitsToLine = _life.GetTransfusionGuidance(body).ToBrainSafe;
            report.Line = _vitalsCfg.GetCVar(WolfmedCVars.BrainBloodStart) * 100f;
            // M1b (plan §3.7): a burn patient reads as a fluids patient.
            report.BurnFluid = _vitalsFluidLoss.GetRate(body);
            report.BurnFluidFast = report.BurnFluid >= _vitalsCfg.GetCVar(WolfmedCVars.AnalyzerBurnFast);
        }

        SetVerdict(body, report);
        SetRoutesAndRestart(body, report);
        SetOrganReadings(body, report);
        SetTemperatures(body, report);

        SetRemainingCauses(body, report); // M5
        return report;
    }

    /// <summary>M4 (plan §3.11): a machine's core and chassis temperatures, while either is past the warning line.</summary>
    private void SetTemperatures(EntityUid body, WolfmedVitalsReport report)
    {
        if (!report.Mechanical || !TryComp(body, out WolfmedCoreHeatComponent? heat) || !heat.Hot)
            return;

        report.CoreTemperature = heat.CoreTemperature;
        report.ChassisTemperature = heat.ChassisTemperature;
    }

    /// <summary>
    /// M5 (plan §3.8-3.10): the toxin load in words with what the liver is doing, radiation with the marrow's stage,
    /// and the core temperature while it is cold or hot enough to count. Flesh only.
    /// </summary>
    private void SetRemainingCauses(EntityUid body, WolfmedVitalsReport report)
    {
        if (report.Mechanical)
            return;

        var toxin = _vitalsToxin.GetLoad(body);
        if (toxin > 0f)
        {
            var (down, outLevel) = _vitalsToxin.GetLevels(body);
            report.Toxin = toxin;
            report.ToxinBand = outLevel >= 1f ? WolfmedToxinBand.Coma
                : down >= 1f ? WolfmedToxinBand.High
                : WolfmedToxinBand.Low;
            report.Liver = _vitalsToxin.GetLiver(body);
        }

        var radiation = _vitalsRadiation.GetRadiation(body);
        if (radiation > 0f)
        {
            report.Radiation = radiation;
            report.MarrowLoss = _vitalsRadiation.GetMarrowLossRate(body);
            report.RadiationBand = report.MarrowLoss > 0f ? WolfmedRadiationBand.Failing
                : _vitalsRadiation.RegenFactor(body) <= 0f ? WolfmedRadiationBand.Suppressed
                : WolfmedRadiationBand.Low;
        }

        var (coldDown, _, heatDown, _) = _vitalsTemperature.GetLevels(body);
        if ((coldDown > 0f || heatDown > 0f) && _vitalsTemperature.GetCore(body) is { } core)
        {
            report.Core = core;
            report.CoreCold = coldDown > 0f;
        }
    }

    /// <summary>
    /// M2 (plan §5.5): the routes making the patient worse, the "After a restart" memory with its transfusion numbers
    /// while blood is the cause still present, and a dead chassis's restart verdict. Also tells the patient's card a
    /// medic is reading them.
    /// </summary>
    private void SetRoutesAndRestart(EntityUid body, WolfmedVitalsReport report)
    {
        report.Routes = _life.GetActiveRoutes(body);
        report.SepticShock = _infection.InSepticShock(body); // INFECTION
        _vitalsCard.MarkExamined(body);

        if (_life.GetRestartMemory(body) is { } memory)
        {
            report.RestartCause = memory.Cause;
            report.RestartPresent = memory.Present;
            if (memory.Present && memory.Cause == WolfmedCauseSource.ArrestBlood)
            {
                var (units, safe) = _life.GetTransfusionGuidance(body);
                report.RestartUnits = units;
                report.RestartSafeUnits = safe;
                report.RestartGraceSeconds = _life.GetPostShockGraceSeconds(body);
                report.RestartSafeLine = _vitalsCfg.GetCVar(WolfmedCVars.BrainBloodStart) * 100f;
            }
        }

        if (!report.Mechanical || report.State != WolfmedVitalsState.Dead)
            return;

        report.Restart = _vitalsRevival.GetRestartRefusal(body) switch
        {
            null => WolfmedRestartVerdict.Ready,
            WolfmedRevivalSystem.NotMonitored => WolfmedRestartVerdict.Hidden,
            WolfmedRevivalSystem.RestartNoHead => WolfmedRestartVerdict.NoHead,
            WolfmedRevivalSystem.RestartNoCore => WolfmedRestartVerdict.NoCore,
            WolfmedRevivalSystem.RestartCoreDestroyed => WolfmedRestartVerdict.CoreDestroyed,
            WolfmedRevivalSystem.RestartNoPump => WolfmedRestartVerdict.NoPump,
            WolfmedRevivalSystem.RestartNoPower => WolfmedRestartVerdict.NoPower,
            _ => WolfmedRestartVerdict.Refused,
        };
    }

    /// <summary>M3 (plan §8): the organs that are impaired or failed, in the organ tab's reading order.</summary>
    private void SetOrganReadings(EntityUid body, WolfmedVitalsReport report)
    {
        var found = new List<(int Order, WolfmedOrganReading Reading)>();
        foreach (var (organ, component) in _bodySystem.GetBodyOrgans(body))
        {
            if (!TryComp(organ, out WolfmedOrganComponent? health) || health.Band == WolfmedOrganBand.Ok)
                continue;

            var slot = component.SlotId ?? string.Empty;
            found.Add((OrganOrder(slot), new WolfmedOrganReading(slot, health.Band)));
        }

        found.Sort((left, right) => left.Order.CompareTo(right.Order));
        foreach (var (_, reading) in found)
            report.Organs.Add(reading);
    }

    private WolfmedVitalsState GetVitalsState(EntityUid body, WolfmedConsciousnessComponent consciousness, bool mechanical)
    {
        if (_vitalsMobState.IsDead(body))
            return WolfmedVitalsState.Dead;

        if (_life.InArrest(body))
            return WolfmedVitalsState.Arrest;

        if (_vitalsOverheat.InThermalShutdown(body))
            return WolfmedVitalsState.ThermalShutdown; // M4

        return consciousness.State switch
        {
            WolfmedConsciousness.Up => WolfmedVitalsState.Up,
            WolfmedConsciousness.Downed => WolfmedVitalsState.Downed,
            _ when mechanical || consciousness.Cause == WolfmedCause.Shutdown => WolfmedVitalsState.Shutdown,
            _ when WolfmedCauses.IsFaint(consciousness.Cause) => WolfmedVitalsState.Faint,
            _ => WolfmedVitalsState.Unconscious,
        };
    }

    /// <summary>
    /// Net flow: regeneration against every bleed. "Falling fast" at a net loss of
    /// <see cref="WolfmedCVars.AnalyzerBloodFast"/> units a second or more.
    /// </summary>
    private WolfmedBloodTrend GetBloodTrend(EntityUid body, BloodstreamComponent bloodstream, float blood)
    {
        var interval = (float) bloodstream.UpdateInterval.TotalSeconds;
        FixedPoint2 refresh = bloodstream.BloodRefreshAmount;
        var regeneration = blood < 1f && interval > 0f && !_vitalsMobState.IsDead(body)
            ? refresh.Float() / interval * _life.BloodRegenFactor(body) // M3: an impaired heart regenerates less
            : 0f;
        var net = regeneration - _life.GetVolumeLossRate(body); // M1b: burn fluid loss empties the same pool

        if (-net >= _vitalsCfg.GetCVar(WolfmedCVars.AnalyzerBloodFast))
            return WolfmedBloodTrend.FallingFast;

        return net < 0f ? WolfmedBloodTrend.Falling
            : net > 0f ? WolfmedBloodTrend.Rising
            : WolfmedBloodTrend.Steady;
    }

    /// <summary>
    /// The paddles' answer in the analyzer's words, from the refusal the hand defibrillator and the pod share.
    /// Shown only for a body that could need a shock: out cold, arrested or dead, and not a machine.
    /// </summary>
    private void SetVerdict(EntityUid body, WolfmedVitalsReport report)
    {
        if (report.Mechanical || report.State is WolfmedVitalsState.Up or WolfmedVitalsState.Downed)
            return;

        var refusal = _vitalsRevival.GetRefusal(body);
        report.Verdict = refusal switch
        {
            null => WolfmedDefibVerdict.Indicated,
            WolfmedRevivalSystem.NotMonitored => WolfmedDefibVerdict.Hidden,
            WolfmedRevivalSystem.Rotten => WolfmedDefibVerdict.Rotten,
            WolfmedRevivalSystem.NoBrain => WolfmedDefibVerdict.NoBrain,
            WolfmedRevivalSystem.BrainDead => WolfmedDefibVerdict.BrainDead,
            WolfmedRevivalSystem.NoHeart => WolfmedDefibVerdict.NoHeart,
            WolfmedRevivalSystem.PulsePresent => WolfmedDefibVerdict.PulsePresent,
            WolfmedRevivalSystem.NoBlood => WolfmedDefibVerdict.NoBlood,
            WolfmedRevivalSystem.TooCold => WolfmedDefibVerdict.TooCold, // M5
            _ => WolfmedDefibVerdict.Unrevivable,
        };

        if (report.Verdict == WolfmedDefibVerdict.Unrevivable)
            report.VerdictReason = refusal;

        if (report.Verdict == WolfmedDefibVerdict.TooCold)
        {
            report.VerdictCore = _vitalsTemperature.GetCore(body) ?? 0f;
            report.VerdictRewarm = _vitalsTemperature.GetLines(body)?.ColdArrest ?? 0f;
        }

        if (report.Verdict != WolfmedDefibVerdict.NoBlood)
            return;

        var (units, safe) = _life.GetTransfusionGuidance(body);
        report.VerdictUnits = units;
        report.VerdictSafeUnits = safe;
        report.VerdictSafeLine = _vitalsCfg.GetCVar(WolfmedCVars.BrainBloodStart) * 100f;
    }
}
