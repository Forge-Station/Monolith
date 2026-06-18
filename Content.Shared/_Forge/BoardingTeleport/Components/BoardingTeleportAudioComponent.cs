using Robust.Shared.GameStates;

namespace Content.Shared._Forge.BoardingTeleport.Components;

/// <summary>
/// Marks PVS audio spawned by boarding teleport systems so the client can apply a user volume slider.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BoardingTeleportAudioComponent : Component
{
    /// <summary>
    /// Server-authored volume captured on the client when playback starts.
    /// </summary>
    public float BaseVolume;
}
