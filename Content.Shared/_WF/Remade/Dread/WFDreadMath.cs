using System.Numerics;

namespace Content.Shared._WF.Remade.Dread;

/// <summary>A Remade the stone can show a player for a moment, and the first stage it appears at.</summary>
public readonly record struct WFDreadGlimpse(string Rsi, int Height, WFMonolithStage Min);

/// <summary>The Remade the stone shows players at the edge of their sight. All have a four-direction <c>full</c> state.</summary>
public static class WFDreadGlimpses
{
    public static readonly WFDreadGlimpse[] All =
    {
        new("_WF/Remade/Mobs/hopper.rsi", 32, WFMonolithStage.Hymn),
        new("_WF/Remade/Mobs/slasher.rsi", 48, WFMonolithStage.Hymn),
        new("_WF/Remade/Mobs/twitcher.rsi", 48, WFMonolithStage.Growth),
        new("_WF/Remade/Mobs/lurker.rsi", 48, WFMonolithStage.Growth),
        new("_WF/Remade/Mobs/puker.rsi", 48, WFMonolithStage.Growth),
        new("_WF/Remade/Mobs/spitter.rsi", 40, WFMonolithStage.Growth),
        new("_WF/Remade/Mobs/infector.rsi", 48, WFMonolithStage.Harvest),
        new("_WF/Remade/Mobs/exploder.rsi", 48, WFMonolithStage.Harvest),
        new("_WF/Remade/Mobs/slasher_enhanced.rsi", 48, WFMonolithStage.Harvest),
        new("_WF/Remade/Mobs/leaper.rsi", 64, WFMonolithStage.Harvest),
        new("_WF/Remade/Mobs/hunter.rsi", 64, WFMonolithStage.Rapture),
        new("_WF/Remade/Mobs/brute.rsi", 64, WFMonolithStage.Rapture),
    };

    public const string State = "full";

    /// <summary>The sprite offset that stands a taller-than-a-tile sprite on its tile, in tiles.</summary>
    public static float Offset(int heightPixels) => (heightPixels / 32f - 1f) / 2f;

    /// <summary>The indices of the glimpses a stage can show.</summary>
    public static IEnumerable<int> Available(WFMonolithStage stage)
    {
        for (var i = 0; i < All.Length; i++)
        {
            if (All[i].Min <= stage)
                yield return i;
        }
    }
}

/// <summary>A light a player's client knows of, for judging whether a spot is dark.</summary>
public readonly record struct WFDreadLight(Vector2 Position, float Radius, float Energy);

/// <summary>
/// How much of the stone's dread a player feels. Everything scales with the stone's stage and the player's stress: quiet is
/// nothing, the Hymn is rare, and Rapture and the Crescendo are constant. Rapture and the Crescendo also reach everyone in the
/// sector, lightly, and not only the crew near the stone.
/// </summary>
public static class WFDreadMath
{
    /// <summary>Whispers, glimpses and pulses a minute felt at the stone, by stage, before stress.</summary>
    private static readonly float[] WhisperRate = { 0f, 1f, 2f, 3.5f, 5f, 7f };
    private static readonly float[] GlimpseRate = { 0f, 0.4f, 1f, 2f, 4f, 7f };
    private static readonly float[] PulseRate = { 0f, 0.5f, 1f, 2f, 4f, 8f };

    /// <summary>Murmurs a minute: whispers heard with no words to read, so they can come far more often than a line can.</summary>
    private static readonly float[] MurmurRate = { 0f, 2f, 4f, 7f, 10f, 15f };

    /// <summary>The steady warp of sight at the stone, by stage, from 0 to 1.</summary>
    private static readonly float[] Warp = { 0f, 0.04f, 0.1f, 0.2f, 0.4f, 0.6f };

    /// <summary>How far, in metres, a stage reaches on the stone's own map.</summary>
    private static readonly float[] Radius = { 0f, 30f, 60f, 100f, 160f, 220f };

    /// <summary>The share of full dread everyone else in the sector feels, by stage.</summary>
    private static readonly float[] Sector = { 0f, 0f, 0f, 0f, 0.1f, 0.25f };

    /// <summary>The warp everyone else in the sector feels, by stage.</summary>
    private static readonly float[] SectorWarp = { 0f, 0f, 0f, 0f, 0.05f, 0.12f };

    public static float WhispersPerMinute(WFMonolithStage stage) => WhisperRate[(int) stage];

    public static float GlimpsesPerMinute(WFMonolithStage stage) => GlimpseRate[(int) stage];

    public static float PulsesPerMinute(WFMonolithStage stage) => PulseRate[(int) stage];

    public static float MurmursPerMinute(WFMonolithStage stage) => MurmurRate[(int) stage];

    /// <summary>The whisper bed's level by stage, before distance and stress: nothing until Harvest, then it climbs.</summary>
    private static readonly float[] BedBase = { 0f, 0f, 0f, 0.4f, 0.75f, 1f };

    /// <summary>The least bed anyone under a stone feels, however far: at Rapture and the Crescendo there is no escaping it.</summary>
    private static readonly float[] BedFloor = { 0f, 0f, 0f, 0f, 0.3f, 0.5f };

    /// <summary>Share of the stone's full dread that a shard gives, by stage. A stone beside you gives 1.</summary>
    private static readonly float[] ShardShare = { 0f, 0.15f, 0.25f, 0.35f, 0.5f, 0.6f };

    /// <summary>How far, in metres, a shard troubles people standing near it.</summary>
    public const float ShardReachLoose = 4f;

    public const float ShardReachRooted = 6f;

    /// <summary>
    /// How much of the stone's dread a shard gives someone <paramref name="distance"/> metres off, by its parent's stage. The one
    /// carrying it feels the whole share, a rooted shard reaches further and hits harder than a loose one, and none of it
    /// ever comes near the stone's own.
    /// </summary>
    public static float ShardInfluence(WFMonolithStage stage, float distance, bool carried, bool rooted)
    {
        var share = ShardShare[(int) stage];
        if (carried)
            return share;

        var reach = rooted ? ShardReachRooted : ShardReachLoose;
        if (distance >= reach)
            return 0f;

        return share * (rooted ? 0.8f : 0.55f) * (1f - distance * distance / (reach * reach));
    }

    /// <summary>The steady warp of sight near a shard, from 0 to 1: the stage's warp scaled like the shard's dread.</summary>
    public static float ShardLevel(WFMonolithStage stage, float influence, float stress)
    {
        return Math.Clamp(Warp[(int) stage] * influence * (1f + Math.Clamp(stress, 0f, 100f) / 100f), 0f, 1f);
    }

    /// <summary>
    /// How loud the whisper bed is in someone's head, from 0 to 1. It starts at Harvest, falls off with the influence reaching
    /// them, rises with their stress, and at Rapture and the Crescendo never drops below a floor. Shards give the bed with no floor.
    /// </summary>
    public static float Bed(WFMonolithStage stage, float influence, float stress, bool floor = true)
    {
        var level = BedBase[(int) stage] * (0.35f + 0.65f * Math.Clamp(influence, 0f, 1f));
        if (floor)
            level = Math.Max(level, BedFloor[(int) stage]);

        return Math.Clamp(level * (1f + Math.Clamp(stress, 0f, 100f) / 200f), 0f, 1f);
    }

    /// <summary>The loudness, from 0 to 1, of each layer of the bed at a bed level: whispers, then the heartbeat and the drone.</summary>
    public static (float WhisperA, float WhisperB, float WhisperC, float Heartbeat, float Drone) BedLayers(float bed)
    {
        bed = Math.Clamp(bed, 0f, 1f);
        return (
            Ramp(bed, 0.15f, 0.65f) * 0.55f,
            Ramp(bed, 0.45f, 0.85f) * 0.45f,
            Ramp(bed, 0.6f, 1f) * 0.4f,
            Ramp(bed, 0.5f, 0.9f) * 0.5f,
            Ramp(bed, 0.4f, 0.9f) * 0.5f);
    }

    private static float Ramp(float value, float from, float to) => Math.Clamp((value - from) / (to - from), 0f, 1f);

    /// <summary>How much more a frightened player gets: 1 at calm, 3 at the edge of breaking.</summary>
    public static float StressFactor(float stress) => 1f + Math.Clamp(stress, 0f, 100f) / 50f;

    /// <summary>
    /// How much of a stone's full dread reaches someone <paramref name="distance"/> metres off: 1 beside it, falling away to
    /// nothing at the stage's radius on its own map, and never below the sector share at Rapture and the Crescendo.
    /// </summary>
    public static float Influence(WFMonolithStage stage, float distance, bool sameMap, bool sectorWide = true)
    {
        var sector = sectorWide ? Sector[(int) stage] : 0f;
        if (!sameMap || stage == WFMonolithStage.Quiet)
            return sector;

        var reach = Radius[(int) stage];
        var near = distance >= reach ? 0f : 1f - distance * distance / (reach * reach);
        return Math.Max(near, sector);
    }

    /// <summary>The steady warp of sight for someone under a stone's influence, from 0 to 1.</summary>
    public static float Level(WFMonolithStage stage, float distance, bool sameMap, float stress, bool sectorWide = true)
    {
        var near = sameMap && stage != WFMonolithStage.Quiet && distance < Radius[(int) stage]
            ? Warp[(int) stage] * (1f - distance * distance / (Radius[(int) stage] * Radius[(int) stage]))
            : 0f;
        var level = Math.Max(near, sectorWide ? SectorWarp[(int) stage] : 0f);
        return Math.Clamp(level * (1f + Math.Clamp(stress, 0f, 100f) / 100f), 0f, 1f);
    }

    /// <summary>The chance that something happens in a span of seconds, for an event that comes some number of times a minute.</summary>
    public static float Chance(float perMinute, float seconds)
    {
        return perMinute <= 0f || seconds <= 0f ? 0f : 1f - MathF.Exp(-perMinute * seconds / 60f);
    }

    /// <summary>The share of a light's radius it lights brightly; past it, the light has faded to a dim edge.</summary>
    public const float BrightShare = 0.6f;

    /// <summary>True if no light known to the client lights a spot brightly, and the ground there isn't lit by day.</summary>
    public static bool IsDark(Vector2 spot, IEnumerable<WFDreadLight> lights, float ambient, float ambientLimit = 0.3f)
    {
        if (ambient > ambientLimit)
            return false;

        foreach (var light in lights)
        {
            var bright = light.Radius * BrightShare;
            if (light.Energy > 0.05f && Vector2.DistanceSquared(spot, light.Position) < bright * bright)
                return false;
        }

        return true;
    }

    /// <summary>The look of the warp on screen at a level: wobble, colour split, vignette and drain, each from 0 to 1.</summary>
    public static (float Wobble, float Split, float Vignette, float Drain) Look(float level, float spike)
    {
        level = Math.Clamp(level, 0f, 1f);
        spike = Math.Clamp(spike, 0f, 1f);
        return (
            Math.Clamp(level * 0.8f + spike * 0.5f, 0f, 1f),
            Math.Clamp(level * 0.7f + spike * 0.7f, 0f, 1f),
            Math.Clamp(level * 0.9f + spike * 0.6f, 0f, 1f),
            Math.Clamp(level * 0.25f + spike * 0.6f, 0f, 1f));
    }
}
