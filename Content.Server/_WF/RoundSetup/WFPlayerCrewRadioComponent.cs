namespace Content.Server._WF.RoundSetup;

/// <summary>Tracks only the intrinsic radio components installed by crew membership.</summary>
[RegisterComponent]
public sealed partial class WFPlayerCrewRadioComponent : Component
{
    public bool AddedTransmitter;
    public bool AddedReceiver;
    public bool AddedActive;
}
