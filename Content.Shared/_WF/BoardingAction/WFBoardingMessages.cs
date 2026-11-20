using Robust.Shared.Serialization;

namespace Content.Shared._WF.BoardingAction;

/// <summary>Helm request to let go of the ship's hacking cable.</summary>
[Serializable, NetSerializable]
public sealed class WFBoardingReleaseCableMessage : BoundUserInterfaceMessage;

/// <summary>What the helm banner shows about hacks and disruption on this ship.</summary>
[Serializable, NetSerializable]
public sealed class WFBoardingHelmState
{
    /// <summary>Ships with a hacking cable in this hull.</summary>
    public string[] Hackers = Array.Empty<string>();

    /// <summary>Whether a hack has finished and this ship is locked down.</summary>
    public bool Lockdown;

    /// <summary>The ship this one's hacking cable is latched to.</summary>
    public string? CableTarget;

    /// <summary>Whether there is nothing to show.</summary>
    public bool Empty => Hackers.Length == 0 && CableTarget == null;
}
