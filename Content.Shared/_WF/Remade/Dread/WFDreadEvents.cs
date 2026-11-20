using Robust.Shared.Serialization;

namespace Content.Shared._WF.Remade.Dread;

/// <summary>Shows one player a Remade at the edge of their sight for a moment. The client picks the dark spot.</summary>
[Serializable, NetSerializable]
public sealed class WFDreadGlimpseEvent(int sprite, float seconds) : EntityEventArgs
{
    /// <summary>Index into <see cref="WFDreadGlimpses.All"/>.</summary>
    public readonly int Sprite = sprite;

    public readonly float Seconds = seconds;
}

/// <summary>Tells a client every stone is gone: stop the bed, the score and any Remade sound it is playing, at once.</summary>
[Serializable, NetSerializable]
public sealed class WFRemadeClearEvent : EntityEventArgs;

/// <summary>
/// Sets how far the stone has warped one player's sight, from 0 to 1, and how loud the whisper bed is in their head, for a few
/// seconds. A spike is a pulse on top of it that fades at once: the vignette throbs and the colour drains.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFDreadVisionEvent(float level, float spike, float seconds, float bed = 0f) : EntityEventArgs
{
    public readonly float Level = level;
    public readonly float Spike = spike;

    /// <summary>How loud the whisper bed is, from 0 to 1. It holds and fades like the level.</summary>
    public readonly float Bed = bed;

    /// <summary>How long the level holds before it fades with nothing to renew it.</summary>
    public readonly float Seconds = seconds;
}
