using Robust.Shared.Maths;

namespace Content.Server._WF.Drydock;

/// <summary>
/// A hull a drydock has broken part of. Rebuilding its broken tiles costs their full materials, and encounters leave it
/// in place for a while after its last broken tile.
/// </summary>
[RegisterComponent]
public sealed partial class WFHulkComponent : Component
{
    /// <summary>Tiles a break lifted, whose rebuild costs its full materials so breaking it again never pays.</summary>
    [DataField]
    public HashSet<Vector2i> Broken = new();

    /// <summary>Last time a crane broke part of the hull.</summary>
    [ViewVariables]
    public TimeSpan LastWork;
}
