// using Content.Shared.Actions; // WOLFGATE(Xenoarch): the self-activate event moved with the xenoarch rework
using Robust.Shared.Serialization;

namespace Content.Shared.Xenoarchaeology.XenoArtifacts;

[Serializable, NetSerializable]
public enum SharedArtifactsVisuals : byte
{
    SpriteIndex,
    IsActivated,
    IsUnlocking // WOLFGATE(Xenoarch): wizden's unlocking state
}

// WOLFGATE(Xenoarch) START: moved to XenoArtifactComponent.cs by the wizden xenoarch rework
// /// <summary>
// ///     Raised as an instant action event when a sentient artifact activates itself using an action.
// /// </summary>
// public sealed partial class ArtifactSelfActivateEvent : InstantActionEvent
// {
// }
// WOLFGATE END
