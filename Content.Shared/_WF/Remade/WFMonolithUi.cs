using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Remade;

[Serializable, NetSerializable]
public enum WFMonolithVisuals : byte
{
    Stage,
}

[Serializable, NetSerializable]
public enum WFMonolithTapUiKey : byte
{
    Key,
}

/// <summary>What the Tap's window shows. Null resonance means no Scion is in reach.</summary>
[Serializable, NetSerializable]
public sealed class WFMonolithTapState(
    float? resonance,
    WFMonolithStage stage,
    float[] dials,
    float output,
    float climb,
    bool barred,
    bool hasSilo,
    int shards)
    : BoundUserInterfaceState
{
    public readonly float? Resonance = resonance;
    public readonly WFMonolithStage Stage = stage;

    /// <summary>The dial of each Gift, indexed by <see cref="WFGift"/>, as the stone runs it: locked at full once it has rooted.</summary>
    public readonly float[] Dials = dials;

    public readonly float Output = output;

    /// <summary>Resonance gained a minute at the current dials.</summary>
    public readonly float Climb = climb;

    public readonly bool Barred = barred;

    /// <summary>Whether a material silo on the Scion's ship can take what Matter makes.</summary>
    public readonly bool HasSilo = hasSilo;

    public readonly int Shards = shards;

    public float Dial(WFGift gift) => (int) gift < Dials.Length ? Dials[(int) gift] : 0f;
}

/// <summary>Sets one Gift's dial on the Tap, from 0 to 1.</summary>
[Serializable, NetSerializable]
public sealed class WFMonolithTapSetDrawMessage(WFGift gift, float draw) : BoundUserInterfaceMessage
{
    public readonly WFGift Gift = gift;
    public readonly float Draw = draw;
}

/// <summary>Finishes feeding a corpse to the Tap.</summary>
[Serializable, NetSerializable]
public sealed partial class WFMonolithFeedDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Finishes chipping a shard off a Scion.</summary>
[Serializable, NetSerializable]
public sealed partial class WFMonolithChipDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Finishes cutting a shard free of the growth that holds it.</summary>
[Serializable, NetSerializable]
public sealed partial class WFMonolithShardCutDoAfterEvent : SimpleDoAfterEvent;
