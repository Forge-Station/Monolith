using System.Linq;
using Content.Server._WF.Remade.Conversion;
using Content.Shared._WF.Remade;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Robust.Shared.Map;

namespace Content.Server._WF.Remade.Director;

/// <summary>Tension: how frightened each crewman is, and what the Director does about it.</summary>
public sealed partial class WFRemadeDirectorSystem
{
    private const float StressDecay = 2f;
    private const float StressSightRange = 12f;
    private const float StressPerSight = 2.5f;
    private const float StressSightCap = 6f;
    private const float StressDeath = 25f;
    private const float StressDeathRange = 15f;
    private const float StressDamageScale = 0.5f;
    private const float StressDamageCap = 30f;
    private const float ScreamDamage = 10f;
    private const float ScreamRange = 14f;
    private const float WillCap = 300f;

    private readonly List<CrewDeath> _deaths = new();

    private sealed class CrewDeath(MapCoordinates at, TimeSpan time)
    {
        public readonly MapCoordinates At = at;
        public readonly TimeSpan Time = time;
        public bool Applied;
    }

    private void InitializeTension()
    {
        SubscribeLocalEvent<WFRemadeStressComponent, DamageChangedEvent>(OnCrewDamaged);
        SubscribeLocalEvent<WFRemadeStressComponent, MobStateChangedEvent>(OnCrewState);
    }

    private void OnCrewDamaged(Entity<WFRemadeStressComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null)
            return;

        var total = (float) args.DamageDelta.GetTotal();
        if (total <= 0f)
            return;

        ent.Comp.Stress = Math.Min(100f, ent.Comp.Stress + Math.Min(total * StressDamageScale, StressDamageCap));
        if (total >= ScreamDamage)
            Noise(_transform.GetMapCoordinates(ent), ScreamRange);
    }

    private void OnCrewState(Entity<WFRemadeStressComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            _deaths.Add(new CrewDeath(_transform.GetMapCoordinates(ent), _timing.CurTime));
    }

    /// <summary>Lets stress bleed off, then adds what the crew have just been through.</summary>
    private void UpdateStress(WFRemadeDirectorComponent director, TimeSpan now)
    {
        var dt = (float) Math.Clamp((now - director.LastStress).TotalSeconds, 0.0, 3.0);
        director.LastStress = now;
        _deaths.RemoveAll(death => (now - death.Time).TotalSeconds > 10.0);

        foreach (var crewman in _crew)
        {
            if (!TryComp<WFRemadeStressComponent>(crewman, out var stress))
                continue;

            var sight = 0;
            var here = _transform.GetMapCoordinates(crewman);
            foreach (var remade in director.Horde)
            {
                if (!TryComp<WFRemadeBehaviourComponent>(remade, out var behaviour)
                    || behaviour.Mode == WFRemadeMode.Hidden
                    || HasComp<WFRemadePlayingDeadComponent>(remade))
                {
                    continue;
                }

                var there = _transform.GetMapCoordinates(remade);
                if (there.MapId != here.MapId || (there.Position - here.Position).LengthSquared() > StressSightRange * StressSightRange)
                    continue;

                if (_examine.InRangeUnOccluded(crewman, there, StressSightRange))
                    sight++;
            }

            var gain = Math.Min(sight * StressPerSight, StressSightCap) * dt;
            foreach (var death in _deaths)
            {
                if (!death.Applied
                    && death.At.MapId == here.MapId
                    && (death.At.Position - here.Position).LengthSquared() <= StressDeathRange * StressDeathRange)
                {
                    gain += StressDeath;
                }
            }

            stress.Stress = Math.Clamp(stress.Stress + gain - StressDecay * dt, 0f, 100f);
        }

        foreach (var death in _deaths)
        {
            death.Applied = true;
        }
    }

    private float StressOf(EntityUid crewman)
    {
        return CompOrNull<WFRemadeStressComponent>(crewman)?.Stress ?? 0f;
    }

    /// <summary>How long the Director will hold a wave back for a shaken crewman before it goes anyway.</summary>
    private static float HoldLimit(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Growth => 600f,
            WFMonolithStage.Harvest => 300f,
            WFMonolithStage.Rapture => 60f,
            _ => 15f,
        };
    }

    /// <summary>True while a wave should wait because its prey is too shaken for it.</summary>
    private bool IsHeld(WFRemadeDirectorComponent director, EntityUid prey, WFMonolithStage stage, TimeSpan now)
    {
        if (StressOf(prey) < director.StressHigh)
            return false;

        director.HeldSince ??= now;
        return (now - director.HeldSince.Value).TotalSeconds < HoldLimit(stage);
    }

    /// <summary>
    /// Picks the crewman to attack next. Anyone shaken is passed over, and so is anyone who has only just come through a
    /// wave, unless the Monolith is at Rapture or beyond. Lone crew are likelier prey.
    /// </summary>
    private EntityUid? PickPrey(WFRemadeDirectorComponent director, WFMonolithStage stage, TimeSpan now)
    {
        var rested = stage >= WFMonolithStage.Rapture;
        var options = new List<(EntityUid Crewman, float Weight)>();
        foreach (var crewman in _crew)
        {
            if (!TryComp<WFRemadeStressComponent>(crewman, out var stress))
                continue;

            if (stress.Stress >= director.StressHigh && !HeldTooLong(director, stage, now))
                continue;

            if (!rested && now < stress.NoWaveUntil)
                continue;

            options.Add((crewman, IsAlone(crewman) ? 2f : 1f));
        }

        if (options.Count == 0)
            return null;

        var roll = _random.NextFloat(0f, options.Sum(option => option.Weight));
        foreach (var option in options)
        {
            roll -= option.Weight;
            if (roll <= 0f)
                return option.Crewman;
        }

        return options[^1].Crewman;
    }

    private bool HeldTooLong(WFRemadeDirectorComponent director, WFMonolithStage stage, TimeSpan now)
    {
        return director.HeldSince is { } since && (now - since).TotalSeconds >= HoldLimit(stage);
    }

    /// <summary>No other crewman within a few steps.</summary>
    private bool IsAlone(EntityUid crewman)
    {
        var here = WorldPos(crewman);
        foreach (var other in _crew)
        {
            if (other != crewman && (WorldPos(other) - here).LengthSquared() < 100f)
                return false;
        }

        return true;
    }
}
