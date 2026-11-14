namespace Content.Server._WF.Encounters.Components;

/// <summary>
/// A raider's docking clamps. A ship it docks with cannot cast off or jump until the clamps are released at the
/// raider's helm or the raider loses power. The raider itself casts off when it likes.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFDockingClampComponent : Component
{
    /// <summary>The ships the clamps have hold of.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Held = new();

    /// <summary>Released by hand, the clamps stay open until then, whatever docks.</summary>
    [ViewVariables]
    public TimeSpan OpenUntil;
}
