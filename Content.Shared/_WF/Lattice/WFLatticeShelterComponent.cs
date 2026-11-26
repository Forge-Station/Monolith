using Robust.Shared.GameStates;

namespace Content.Shared._WF.Lattice;

/// <summary>On a ship or a person: a working stabiliser or charm holds a little anchored space round it.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFLatticeShelterComponent : Component
{
    /// <summary>Share of Drift gain it keeps off, 0 to 1.</summary>
    [DataField, AutoNetworkedField]
    public float Shelter = 0.8f;

    /// <summary>Whether it stops the Unmooring taking the ship's animals.</summary>
    [DataField, AutoNetworkedField]
    public bool StopsTaking = true;
}
