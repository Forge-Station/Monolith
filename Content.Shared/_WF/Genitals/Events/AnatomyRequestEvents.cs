using Robust.Shared.Serialization;

namespace Content.Shared._WF.Genitals;

// Owner requests from the Anatomy panel, sent with RaisePredictiveEvent and validated on both sides.

/// <summary>Sets the sender's arousal (0-100).</summary>
[Serializable, NetSerializable]
public sealed class AnatomySetArousalRequestEvent(byte value) : EntityEventArgs
{
    public readonly byte Value = value;
}

/// <summary>Sets the sender's reveal mode.</summary>
[Serializable, NetSerializable]
public sealed class AnatomySetRevealModeRequestEvent(GenitalRevealMode mode) : EntityEventArgs
{
    public readonly GenitalRevealMode Mode = mode;
}

/// <summary>Sets the sender's visibility for one organ slot.</summary>
[Serializable, NetSerializable]
public sealed class AnatomySetVisibilityRequestEvent(GenitalSlot slot, GenitalVisibility visibility) : EntityEventArgs
{
    public readonly GenitalSlot Slot = slot;
    public readonly GenitalVisibility Visibility = visibility;
}

/// <summary>Removes (worn = false) or puts back (worn = true) one of the sender's undergarments.</summary>
[Serializable, NetSerializable]
public sealed class AnatomySetUndergarmentRequestEvent(UndergarmentSlot slot, bool worn) : EntityEventArgs
{
    public readonly UndergarmentSlot Slot = slot;
    public readonly bool Worn = worn;
}
