using Robust.Shared.GameStates;

namespace Content.Shared._WF.CargoLoad;

/// <summary>
/// A loaded ship's cargo as its helm shows it, kept by the server's cargo sweep. Absent on a ship carrying less than
/// <see cref="ShownLoad"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFCargoReadoutComponent : Component
{
    /// <summary>Tonnes aboard at which the helm shows the cargo gauge, and the server keeps this readout.</summary>
    public const float ShownLoad = 0.5f;

    /// <summary>Tonnes aboard the ship's docked set.</summary>
    [DataField, AutoNetworkedField]
    public float Load;

    /// <summary>Tonnes of <see cref="Load"/> held as materials in silos, lathes and other material storage.</summary>
    [DataField, AutoNetworkedField]
    public float Stored;

    /// <summary>Share of thrust the set keeps in space.</summary>
    [DataField, AutoNetworkedField]
    public float Thrust = 1f;

    /// <summary>The load at which thrust bottoms out, the gauge's full scale.</summary>
    [DataField, AutoNetworkedField]
    public float FullScale;

    /// <summary>Seconds the load adds to an FTL spool.</summary>
    [DataField, AutoNetworkedField]
    public float Spool;
}
