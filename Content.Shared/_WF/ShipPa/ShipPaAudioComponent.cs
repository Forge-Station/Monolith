namespace Content.Shared._WF.ShipPa;

/// <summary>A client-local PA voice. Never created or replicated by the server.</summary>
[RegisterComponent]
public sealed partial class ShipPaAudioComponent : Component
{
    [DataField] public int BroadcastId;
    [DataField] public EntityUid? Speaker;
}
