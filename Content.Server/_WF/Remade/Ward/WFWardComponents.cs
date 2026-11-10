using System.Numerics;

namespace Content.Server._WF.Remade.Ward;

/// <summary>A Monolith that is also a ship shield generator, run through the stone's Ward dial.</summary>
[RegisterComponent]
public sealed partial class WFWardComponent : Component
{
    /// <summary>The capacity the shield was last set to, so a change in the dial keeps the same share of damage.</summary>
    [ViewVariables]
    public float Capacity;

    /// <summary>True while a rooted stone's field is up and other ships are being held out of it.</summary>
    [ViewVariables]
    public bool Walled;

    /// <summary>Ships that may go in: the stone's own and anything docked to it.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Friendly = new();

    /// <summary>The field's outline in its ship's own frame, as a centre and two radii, and the outline it came from.</summary>
    public Vector2 Centre;
    public Vector2 Radii;
    public Vector2[][]? Contours;

    /// <summary>How many times a ship has been turned back at the wall. Tests read it.</summary>
    [ViewVariables]
    public int Turned;

    [ViewVariables]
    public TimeSpan Next;
}
