namespace Content.Server._WF.Drydock;

/// <summary>
/// A flatpack of hardware stripped off a broken hull: it unpacks damaged, and sells for a share of the hardware's price.
/// </summary>
[RegisterComponent]
public sealed partial class WFSalvagedHardwareComponent : Component
{
    /// <summary>Share of the hardware's destruction damage it unpacks with.</summary>
    [DataField]
    public float Damage = 0.5f;

    /// <summary>Share of the hardware's estimated price the flatpack sells for.</summary>
    [DataField]
    public float Resale = 0.25f;
}
