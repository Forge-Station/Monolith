using Robust.Shared.GameStates;

namespace Content.Shared._WF.Lattice;

/// <summary>On a ship, or a person off one: how far out of the Lattice's hold it is and how long it has been there.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFDriftComponent : Component
{
    /// <summary>Index of the band it is in, 0 the innermost.</summary>
    [DataField, AutoNetworkedField]
    public int Band;

    /// <summary>Exposure built up out of anchored space. Server only.</summary>
    [DataField]
    public float Drift;

    /// <summary>Drift in steps of <see cref="LevelStep"/>, for clients' screen warping.</summary>
    [DataField, AutoNetworkedField]
    public int Level;

    public const float LevelStep = 5f;

    /// <summary>Depth into its band, 0 to 1, for clients that can't work it out themselves, such as on a planet.</summary>
    [DataField, AutoNetworkedField]
    public float Depth;

    /// <summary>Whether its band kills FTL drives, so a ship here can't jump.</summary>
    [DataField, AutoNetworkedField]
    public bool NoFtl;

    /// <summary>On a person: Drift they carry in their own head, from a Monolith or another source, wherever they are. Server only.</summary>
    [DataField]
    public float Mind;

    /// <summary><see cref="Mind"/> in steps of <see cref="LevelStep"/>.</summary>
    [DataField, AutoNetworkedField]
    public int MindLevel;

    /// <summary>What last put Drift in their head.</summary>
    [DataField, AutoNetworkedField]
    public WFDriftFlavour Flavour;

    /// <summary>Drift this person feels from where they are, after charm, grounding, sleep and company. Server only.</summary>
    [DataField]
    public float Felt;

    /// <summary><see cref="Felt"/> in steps of <see cref="LevelStep"/>.</summary>
    [DataField, AutoNetworkedField]
    public int FeltLevel;

    /// <summary>When <see cref="Felt"/> was last worked out. Server only.</summary>
    [DataField]
    public TimeSpan FeltAt;

    /// <summary>Drift soaked into a ship's hull. Drift never falls below it until a drydock cleans it.</summary>
    [DataField]
    public float Residue;
}

/// <summary>Where a person's Drift came from, for how it feels.</summary>
public enum WFDriftFlavour : byte
{
    Unmooring,
    Monolith,
}
