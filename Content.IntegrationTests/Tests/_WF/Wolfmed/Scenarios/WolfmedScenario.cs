#nullable enable
using System;
using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Medical;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Atmos;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using NUnit.Framework;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Drives a scenario through the public seams (plan §12.0): the life tick for time on the brain clock, the
/// bloodstream for volume, and the map's atmosphere for air. Server thread only.
/// </summary>
public sealed class WolfmedScenario
{
    public readonly IEntityManager Entities;
    public readonly WolfmedLifeSystem Life;
    public readonly WolfmedConsciousnessSystem Consciousness;
    public readonly WolfmedRevivalSystem Revival;
    public readonly WolfmedBreathingSystem Breathing;
    public readonly BloodstreamSystem Bloodstream;

    public WolfmedScenario(IEntityManager entities)
    {
        Entities = entities;
        Life = entities.System<WolfmedLifeSystem>();
        Consciousness = entities.System<WolfmedConsciousnessSystem>();
        Revival = entities.System<WolfmedRevivalSystem>();
        Breathing = entities.System<WolfmedBreathingSystem>();
        Bloodstream = entities.System<BloodstreamSystem>();
    }

    /// <summary>Station air: the mix the interaction tests give their map.</summary>
    public static GasMixture Air()
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = 21.824779f;
        moles[(int) Gas.Nitrogen] = 82.10312f;
        return new GasMixture(moles, Atmospherics.T20C);
    }

    /// <summary>Nothing to breathe at station pressure: pure nitrogen, so no barotrauma muddies the oxygen route.</summary>
    public static GasMixture Nitrogen()
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Nitrogen] = 21.824779f + 82.10312f;
        return new GasMixture(moles, Atmospherics.T20C);
    }

    /// <summary>
    /// Keeps Mono's grid cleanup off the test grid. A scenario runs for minutes of game time with no player
    /// nearby, and the cleanup then deletes the grid and the patient on it.
    /// </summary>
    public void KeepGrid(EntityUid grid) =>
        Entities.EnsureComponent<Content.Server._Mono.Cleanup.CleanupImmuneComponent>(grid);

    /// <summary>Gives the test map's tiles air, or an unbreathable mix at the same pressure.</summary>
    public void SetAir(EntityUid map, bool air)
    {
        Entities.System<AtmosphereSystem>().SetMapAtmosphere(map, false, air ? Air() : Nitrogen());
    }

    public WolfmedConsciousness State(EntityUid body) =>
        Entities.GetComponent<WolfmedConsciousnessComponent>(body).State;

    public WolfmedConsciousnessComponent Vitals(EntityUid body) =>
        Entities.GetComponent<WolfmedConsciousnessComponent>(body);

    /// <summary>The analyzer's vitals block for the body (M1a, plan §5.5), built fresh.</summary>
    public WolfmedVitalsReport Report(EntityUid body)
    {
        var report = Entities.System<HealthAnalyzerSystem>().BuildVitals(body);
        Assert.That(report, Is.Not.Null, "the analyzer has no vitals block for this body.");
        return report!;
    }

    /// <summary>The analyzer's vitals block as the panel shows it, one line each.</summary>
    public string[] AnalyzerLines(EntityUid body) => WolfmedVitalsText.Lines(Report(body)).ToArray();

    /// <summary>All of the vitals block as one string, for Does.Contain assertions and the test output.</summary>
    public string Analyzer(EntityUid body) => string.Join(" | ", AnalyzerLines(body));

    public RespiratorComponent Respirator(EntityUid body) => Entities.GetComponent<RespiratorComponent>(body);

    public float Blood(EntityUid body) => Bloodstream.GetBloodLevelPercentage(body);

    /// <summary>The bloodstream's pool in units.</summary>
    public float Pool(EntityUid body)
    {
        FixedPoint2 max = Entities.GetComponent<BloodstreamComponent>(body).BloodMaxVolume;
        return max.Float();
    }

    /// <summary>
    /// Sets blood volume to a fraction of the pool and hands the level to consciousness at once, the way
    /// the bloodstream's own tick would.
    /// </summary>
    public void SetBlood(EntityUid body, float fraction)
    {
        var stream = Entities.GetComponent<BloodstreamComponent>(body);
        Assert.That(Entities.System<SharedSolutionContainerSystem>()
            .TryGetSolution(body, stream.BloodSolutionName, out _, out var solution), Is.True);

        var delta = FixedPoint2.New(fraction * Pool(body)) - solution!.Volume;
        if (delta != FixedPoint2.Zero)
            Bloodstream.TryModifyBloodLevel(body, delta);

        Consciousness.OnBloodLevelChanged(body, Blood(body));
    }

    /// <summary>Adds units of blood, as a transfusion would, and tells consciousness.</summary>
    public void Transfuse(EntityUid body, float units)
    {
        Bloodstream.TryModifyBloodLevel(body, FixedPoint2.New(units));
        Consciousness.OnBloodLevelChanged(body, Blood(body));
    }

    /// <summary>
    /// Fast-forwards the life tick one second at a time. The callback sees each second after its tick and
    /// can stop the run by returning true. Returns the seconds run.
    /// </summary>
    public int Advance(EntityUid body, int seconds, Func<int, bool>? each = null)
    {
        for (var second = 1; second <= seconds; second++)
        {
            Life.Tick(body, 1f);
            Consciousness.Refresh(body);
            if (each != null && each(second))
                return second;
        }

        return seconds;
    }

    /// <summary>A forced shock: the roll always succeeds, every gate still applies.</summary>
    public bool Shock(EntityUid body, out string line)
    {
        Revival.ForcedRoll = 0f;
        try
        {
            return Revival.TryDefibrillate(body, out line);
        }
        finally
        {
            Revival.ForcedRoll = null;
        }
    }

    public EntityUid Part(EntityUid body, BodyPartType type, BodyPartSymmetry symmetry = BodyPartSymmetry.None) =>
        Entities.System<SharedBodySystem>().GetBodyChildren(body)
            .First(part => part.Component.PartType == type && part.Component.Symmetry == symmetry).Id;

    public static DamageSpecifier Spec(string type, float amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };

    public FixedPoint2 Damage(EntityUid body, string type) =>
        Entities.GetComponent<DamageableComponent>(body).Damage.DamageDict.TryGetValue(type, out var amount)
            ? amount
            : FixedPoint2.Zero;
}
