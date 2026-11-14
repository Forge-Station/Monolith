namespace Content.Server._WF.Planets.Flight;

/// <summary>A grid under a planet layer's drag, holding its own damping until it leaves.</summary>
// Unsaved so a map saved mid-flight doesn't record the layer's damping as the grid's own.
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetDragComponent : Component
{
    /// <summary>The grid's own linear damping before planet drag applied.</summary>
    [DataField]
    public float OriginalDamping;
}
