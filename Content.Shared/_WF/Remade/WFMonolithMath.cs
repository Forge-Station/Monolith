namespace Content.Shared._WF.Remade;

/// <summary>What a Monolith has become, from its resonance.</summary>
public enum WFMonolithStage : byte
{
    Quiet,
    Hymn,
    Growth,
    Harvest,
    Rapture,
    Crescendo,
}

/// <summary>What a Monolith gives. Each has its own dial on the Tap.</summary>
public enum WFGift : byte
{
    Power,
    Matter,
    Flesh,
    Reach,
    Ward,
}

/// <summary>The sums behind a Monolith: how fast resonance climbs and what the power gift pays.</summary>
public static class WFMonolithMath
{
    public const float MaxResonance = 100f;

    /// <summary>Resonance per second before acceleration: an idle Scion, and the extra at full draw.</summary>
    public const float IdleRate = 0.0004f;
    public const float DrawRate = 0.0186f;

    /// <summary>Resonance that doubles the climb rate.</summary>
    public const float Acceleration = 40f;

    /// <summary>Extra rate per corpse near the Monolith, and how many corpses count.</summary>
    public const float CorpseBonus = 0.1f;
    public const int MaxCorpses = 10;

    /// <summary>Power at full draw: at no resonance, and the gain per point of resonance.</summary>
    public const float BaseOutput = 2_000_000f;
    public const float OutputPerResonance = 30_000f;

    /// <summary>Most the dials can add up to. One dial at full is 1, so every Gift at full climbs faster than Power alone.</summary>
    public const float MaxEffectiveDraw = 1.5f;

    /// <summary>The draw all five dials add up to: the root of the sum of their squares, never past <see cref="MaxEffectiveDraw"/>.</summary>
    public static float EffectiveDraw(float power, float matter, float flesh, float reach, float ward = 0f)
    {
        power = Math.Clamp(power, 0f, 1f);
        matter = Math.Clamp(matter, 0f, 1f);
        flesh = Math.Clamp(flesh, 0f, 1f);
        reach = Math.Clamp(reach, 0f, 1f);
        ward = Math.Clamp(ward, 0f, 1f);
        return Math.Min(MathF.Sqrt(power * power + matter * matter + flesh * flesh + reach * reach + ward * ward), MaxEffectiveDraw);
    }

    /// <summary>The dial a Gift really runs at: a Prime Monolith gives everything it has, whatever the Tap says.</summary>
    public static float Dial(WFMonolithStage stage, float dial)
    {
        return stage == WFMonolithStage.Crescendo ? 1f : Math.Clamp(dial, 0f, 1f);
    }

    /// <summary>Resonance per second at an effective draw from 0 to <see cref="MaxEffectiveDraw"/>.</summary>
    public static float Rate(float draw, float resonance, int corpses)
    {
        draw = Math.Clamp(draw, 0f, MaxEffectiveDraw);
        var counted = Math.Clamp(corpses, 0, MaxCorpses);
        return (IdleRate + DrawRate * draw * draw) * (1f + resonance / Acceleration) * (1f + CorpseBonus * counted);
    }

    public static WFMonolithStage StageFor(float resonance)
    {
        return resonance switch
        {
            >= 100f => WFMonolithStage.Crescendo,
            >= 80f => WFMonolithStage.Rapture,
            >= 55f => WFMonolithStage.Harvest,
            >= 35f => WFMonolithStage.Growth,
            >= 15f => WFMonolithStage.Hymn,
            _ => WFMonolithStage.Quiet,
        };
    }

    /// <summary>The resonance a stage starts at.</summary>
    public static float StageStart(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Crescendo => 100f,
            WFMonolithStage.Rapture => 80f,
            WFMonolithStage.Harvest => 55f,
            WFMonolithStage.Growth => 35f,
            WFMonolithStage.Hymn => 15f,
            _ => 0f,
        };
    }

    /// <summary>The most resonance a stone held to a stage can reach: just short of the next stage.</summary>
    public static float Cap(WFMonolithStage maxStage)
    {
        return maxStage >= WFMonolithStage.Crescendo ? MaxResonance : StageStart(maxStage + 1) - 0.01f;
    }

    /// <summary>Watts the power gift supplies; it grows with the resonance that pays for it.</summary>
    public static float Output(float draw, float resonance)
    {
        return Math.Clamp(draw, 0f, 1f) * (BaseOutput + OutputPerResonance * Math.Clamp(resonance, 0f, MaxResonance));
    }

    /// <summary>Matter: how often it pays out, units of each material per payout at full dial and no resonance, and how much of each it may hold.</summary>
    public const float MatterPeriod = 10f;
    public const float MatterBaseUnits = 40f;
    public const int MatterCap = 20_000;

    /// <summary>Flesh: reach, healing a second and seconds to raise a body, at full dial and no resonance, and what a point of resonance adds.</summary>
    public const float FleshRadius = 6f;
    public const float FleshRadiusPerResonance = 0.04f;
    public const float FleshHeal = 0.5f;
    public const float FleshHealPerResonance = 0.015f;
    public const float FleshReviveSeconds = 60f;

    /// <summary>Reach: the travel time a jump loses and the sensor range it gains, at full dial and no resonance, and what a point of resonance adds.</summary>
    public const float ReachTravelCut = 0.3f;
    public const float ReachTravelCutPerResonance = 0.003f;
    public const float ReachTravelCutMax = 0.6f;
    public const float ReachSensors = 0.5f;
    public const float ReachSensorsPerResonance = 0.01f;

    /// <summary>Resonance a jump adds with a Scion aboard: the base, and the extra at full dial.</summary>
    public const float JumpResonance = 0.5f;
    public const float JumpResonanceDial = 2.5f;

    /// <summary>Shards: resonance a chip adds, and the most that can be cut off one Scion at a time.</summary>
    public const float ChipResonance = 2f;
    public const int MaxShards = 8;

    /// <summary>
    /// Ward: the damage the shield takes before it overloads and the damage it mends a second, at full dial and no resonance, and what
    /// a point of resonance adds. A ship generator holds 60,000 to 500,000.
    /// </summary>
    public const float WardCapacity = 1_000_000f;
    public const float WardCapacityPerResonance = 30_000f;
    public const float WardMend = 10_000f;
    public const float WardMendPerResonance = 250f;
    public const float WardOverloadSeconds = 20f;

    /// <summary>A rooted stone's shield: a capacity and mend no ship weapon comes near.</summary>
    public const float WardRootedCapacity = 1_000_000_000f;
    public const float WardRootedMend = 100_000_000f;

    /// <summary>Units of each material Matter makes in one payout.</summary>
    public static int MatterUnits(float dial, float resonance)
    {
        return (int) MathF.Round(MatterBaseUnits * Math.Clamp(dial, 0f, 1f) * (1f + Math.Clamp(resonance, 0f, MaxResonance) / Acceleration));
    }

    public static float FleshReach(float resonance)
    {
        return FleshRadius + FleshRadiusPerResonance * Math.Clamp(resonance, 0f, MaxResonance);
    }

    /// <summary>Damage a second Flesh heals on each person in reach.</summary>
    public static float FleshHealRate(float dial, float resonance)
    {
        return Math.Clamp(dial, 0f, 1f) * (FleshHeal + FleshHealPerResonance * Math.Clamp(resonance, 0f, MaxResonance));
    }

    /// <summary>Seconds Flesh takes to raise a body in reach; infinite with the dial off.</summary>
    public static float FleshReviveTime(float dial, float resonance)
    {
        dial = Math.Clamp(dial, 0f, 1f);
        return dial <= 0f ? float.PositiveInfinity : FleshReviveSeconds / (dial * (1f + Math.Clamp(resonance, 0f, MaxResonance) / Acceleration));
    }

    /// <summary>What is left of a jump's travel time, from 1 (no cut) down.</summary>
    public static float ReachTravelFactor(float dial, float resonance)
    {
        var cut = Math.Clamp(dial, 0f, 1f) * (ReachTravelCut + ReachTravelCutPerResonance * Math.Clamp(resonance, 0f, MaxResonance));
        return 1f - Math.Min(cut, ReachTravelCutMax);
    }

    /// <summary>What radar range is multiplied by, from 1 (no change) up.</summary>
    public static float ReachSensorFactor(float dial, float resonance)
    {
        return 1f + Math.Clamp(dial, 0f, 1f) * (ReachSensors + ReachSensorsPerResonance * Math.Clamp(resonance, 0f, MaxResonance));
    }

    /// <summary>Resonance one jump adds, with the dial up or not.</summary>
    public static float JumpGain(float dial)
    {
        return JumpResonance + JumpResonanceDial * Math.Clamp(dial, 0f, 1f);
    }

    /// <summary>Damage the Ward shield takes before it overloads; nothing with the dial off.</summary>
    public static float WardStrength(float dial, float resonance)
    {
        return Math.Clamp(dial, 0f, 1f) * (WardCapacity + WardCapacityPerResonance * Math.Clamp(resonance, 0f, MaxResonance));
    }

    /// <summary>Damage the Ward shield mends a second.</summary>
    public static float WardMendRate(float dial, float resonance)
    {
        return Math.Clamp(dial, 0f, 1f) * (WardMend + WardMendPerResonance * Math.Clamp(resonance, 0f, MaxResonance));
    }

    /// <summary>The Ward shield's capacity at a stage: a rooted stone is past any weapon, whatever the dial.</summary>
    public static float WardStrength(WFMonolithStage stage, float dial, float resonance)
    {
        return stage == WFMonolithStage.Crescendo ? WardRootedCapacity : WardStrength(dial, resonance);
    }

    public static float WardMendRate(WFMonolithStage stage, float dial, float resonance)
    {
        return stage == WFMonolithStage.Crescendo ? WardRootedMend : WardMendRate(dial, resonance);
    }
}
