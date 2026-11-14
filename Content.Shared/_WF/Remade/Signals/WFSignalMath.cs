namespace Content.Shared._WF.Remade.Signals;

/// <summary>The psychic tricks a Signal can play on the living.</summary>
public enum WFSignalTrick : byte
{
    /// <summary>The lights near the target flicker.</summary>
    Flicker,

    /// <summary>A line in one player's head.</summary>
    Whisper,

    /// <summary>A Remade's voice somewhere near one player.</summary>
    FalseSound,

    /// <summary>Words in blood on the floor near the target.</summary>
    Scrawl,

    /// <summary>A door opens or shuts by itself.</summary>
    Door,

    /// <summary>A door bolts shut for a while.</summary>
    Bolt,
}

/// <summary>What a trick needs: the lowest stage it works at, its Psi cost and its cooldown in seconds.</summary>
public readonly record struct WFSignalTrickInfo(WFMonolithStage Min, float Cost, float Cooldown);

/// <summary>The numbers behind the Signals: how many a stone holds, how fast their Psi comes back, what the tricks cost and how far they reach.</summary>
public static class WFSignalMath
{
    public const float MaxPsi = 100f;
    public const float StartPsi = 50f;

    /// <summary>Psi and cooldown for taking over a Remade, and how near it must be.</summary>
    public const float PossessCost = 20f;
    public const float PossessCooldown = 10f;
    public const float PossessRange = 8f;

    /// <summary>How far from a Signal its tricks reach.</summary>
    public const float TrickRange = 14f;

    /// <summary>Seconds a bolted door stays shut.</summary>
    public const float BoltSeconds = 30f;

    /// <summary>A Signal may drift this far past the hull of the stone's ship or any docked to it.</summary>
    public const float ShipMargin = 4f;

    /// <summary>How far a Signal may go from a stone that sits off any grid.</summary>
    public const float FreeRadius = 12f;

    /// <summary>How near a Signal must be to corruption or a rooted shard of its stone.</summary>
    public const float CorruptionReach = 4f;
    public const float ShardReach = 8f;

    /// <summary>The most Signals a stone holds at a stage.</summary>
    public static int Cap(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Hymn => 1,
            WFMonolithStage.Growth => 2,
            WFMonolithStage.Harvest => 3,
            WFMonolithStage.Rapture => 4,
            WFMonolithStage.Crescendo => 5,
            _ => 0,
        };
    }

    /// <summary>Psi a second.</summary>
    public static float PsiRate(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Hymn => 0.4f,
            WFMonolithStage.Growth => 0.7f,
            WFMonolithStage.Harvest => 1.1f,
            WFMonolithStage.Rapture => 1.6f,
            WFMonolithStage.Crescendo => 2.2f,
            _ => 0f,
        };
    }

    public static WFSignalTrickInfo Info(WFSignalTrick trick)
    {
        return trick switch
        {
            WFSignalTrick.Flicker => new(WFMonolithStage.Hymn, 2f, 5f),
            WFSignalTrick.Whisper => new(WFMonolithStage.Hymn, 8f, 25f),
            WFSignalTrick.FalseSound => new(WFMonolithStage.Hymn, 10f, 15f),
            WFSignalTrick.Scrawl => new(WFMonolithStage.Hymn, 15f, 45f),
            WFSignalTrick.Door => new(WFMonolithStage.Growth, 6f, 10f),
            _ => new(WFMonolithStage.Growth, 25f, 60f),
        };
    }
}
