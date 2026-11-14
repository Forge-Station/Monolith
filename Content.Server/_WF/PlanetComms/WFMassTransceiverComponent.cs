namespace Content.Server._WF.PlanetComms;

/// <summary>A mass transceiver: while powered and whole, it carries the comms of the world it stands on.</summary>
[RegisterComponent, Access(typeof(WFPlanetCommsSystem))]
public sealed partial class WFMassTransceiverComponent : Component
{
    /// <summary>A wreck: never carries comms, whatever its power.</summary>
    [DataField]
    public bool Broken;
}
