using Content.Shared.Atmos;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>
/// The pod's own air (playtest 3). While the pod is sealed its occupant breathes and is exposed to this mix instead of
/// the tile, and the pod puts the mix back to these figures every time anything reads it, so exhaled gas is scrubbed
/// and a hot or cold room never reaches the patient. Server state only; nothing here is networked.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedAutodocAtmosphereComponent : Component
{
    /// <summary>Litres of air in the pod: about a coffin's worth.</summary>
    [DataField]
    public float Volume = 400f;

    /// <summary>The pressure the pod holds, in kPa.</summary>
    [DataField]
    public float Pressure = Atmospherics.OneAtmosphere;

    /// <summary>The temperature the pod holds its air at, in kelvin.</summary>
    [DataField]
    public float Temperature = Atmospherics.T20C;

    /// <summary>Share of the moles that are oxygen.</summary>
    [DataField]
    public float Oxygen = 0.21f;

    /// <summary>Share of the moles that are nitrogen.</summary>
    [DataField]
    public float Nitrogen = 0.79f;

    /// <summary>The mix itself. Regenerated from the fields above whenever the sealed pod hands it out.</summary>
    [ViewVariables]
    public GasMixture Air = new();

    /// <summary>
    /// The hull is breached: the pod's breakage threshold was crossed and nobody has welded it since. Outside air
    /// gets in until the damage is repaired below that threshold.
    /// </summary>
    [ViewVariables]
    public bool Broken;
}

/// <summary>Whether the pod keeps its own air around the occupant, and if not why not. Worst first after Sealed.</summary>
[Serializable, NetSerializable]
public enum WolfmedAutodocSeal : byte
{
    /// <summary>Nobody inside: the lid is up and there is nothing to seal.</summary>
    Open,

    /// <summary>Lid down on an occupant, powered, whole: the occupant breathes the pod's air.</summary>
    Sealed,

    /// <summary>No power, so no fans and slack seals: the occupant breathes the room.</summary>
    Unpowered,

    /// <summary>Damaged past its breakage threshold: outside air gets in until it is welded.</summary>
    Breached,

    /// <summary>Emagged: the pod opens its vents on purpose.</summary>
    Vented,
}

/// <summary>Appearance keys for the pod's atmosphere.</summary>
[Serializable, NetSerializable]
public enum WolfmedAutodocAtmosphereVisuals : byte
{
    /// <summary>True while the hull is breached, so the sprite can show it.</summary>
    Breached,
}

/// <summary>The locale keys both ends use for the seal.</summary>
public static class WolfmedAutodocSealText
{
    /// <summary>The window's readout, beside the status: SEALED, UNSEALED: NO POWER, HULL BREACH and so on.</summary>
    public static string Readout(WolfmedAutodocSeal seal) =>
        $"wolfmed-autodoc-seal-{seal.ToString().ToLowerInvariant()}";

    /// <summary>The examine line for the seal, or null when there is nothing to say (an empty, intact pod).</summary>
    public static string? Examine(WolfmedAutodocSeal seal) => seal == WolfmedAutodocSeal.Open
        ? null
        : $"wolfmed-autodoc-examine-seal-{seal.ToString().ToLowerInvariant()}";
}
