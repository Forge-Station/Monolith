using Content.Shared.Atmos;
using Robust.Shared.Serialization;

namespace Content.Shared._NF.Atmos.BUI;

[NetSerializable, Serializable]
public sealed class GasSaleConsoleBoundUserInterfaceState(int appraisal, GasMixture mixture, bool enabled)
    : BoundUserInterfaceState
{
    /// <summary>
    /// Estimated appraisal value of the gas mixture.
    /// </summary>
    public int Appraisal = appraisal;

    /// <summary>
    /// The mixture in the linked sale entity.
    /// </summary>
    public GasMixture Mixture = mixture;

    /// <summary>
    /// Whether or not the buttons on the interface are enabled.
    /// </summary>
    public bool Enabled = enabled;

    // WOLFGATE(Mining) START: the appraisal shows the payout currency
    /// <summary>Short name of the currency the console pays in, such as MIC; null when it pays credits.</summary>
    public string? Currency;
    // WOLFGATE END
}

[Serializable, NetSerializable]
public enum GasSaleConsoleUiKey : byte
{
    Key,
}
