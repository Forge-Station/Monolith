namespace Content.Server._WF.NpcCrew.Components;

/// <summary>
/// On a ship's grid: its crew open fire on any faction enemy they see, aboard or off the ship, instead of only
/// boarders and attackers. Helm, guns and radio stay manned against anyone not aboard.
/// </summary>
[RegisterComponent]
public sealed partial class WFCrewOnSightComponent : Component
{
    /// <summary>Companies whose people are left alone all the same.</summary>
    [DataField]
    public List<string> Spares = new();
}
