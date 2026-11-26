namespace Content.Server._WF.CavernEncounters;

/// <summary>
/// A lost, wounded surveyor: he follows whoever offers to lead him out, comes up with them when they climb, and pays
/// them once he stands on the surface alive.
/// </summary>
[RegisterComponent, Access(typeof(WFCavernSurveyorSystem))]
public sealed partial class WFCavernSurveyorComponent : Component
{
    /// <summary>Spesos he pays on reaching the surface.</summary>
    [DataField]
    public int Reward = 1500;

    /// <summary>Brute damage he spawns with.</summary>
    [DataField]
    public float Wounds = 20f;

    /// <summary>Tiles from his leader within which he comes along when the leader climbs or drops a level.</summary>
    [DataField]
    public float BringRange = 4f;

    /// <summary>Whoever is leading him.</summary>
    [ViewVariables]
    public EntityUid? Leader;

    /// <summary>The cavern he was lost in; its ground is the surface he wants.</summary>
    [ViewVariables]
    public EntityUid? Cavern;

    /// <summary>Whether his wounds have been dealt; once his body is whole, after he spawns.</summary>
    [ViewVariables]
    public bool Hurt;

    /// <summary>Whether he has paid out.</summary>
    [ViewVariables]
    public bool Paid;
}
