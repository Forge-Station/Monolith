using System.Numerics;
using Content.Shared.FixedPoint;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Throttle state for one body's wound feedback. Ensured on the body the first time it is wounded and
/// never read anywhere else, so the dictionary a per-body throttle would otherwise need cannot outlive
/// the entity.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedWoundSfxComponent : Component
{
    /// <summary>Earliest time the next wound sound may play.</summary>
    [DataField]
    public TimeSpan NextSound;

    /// <summary>Earliest time the next debris effect may spawn.</summary>
    [DataField]
    public TimeSpan NextDebris;

    /// <summary>
    /// When this body last took a hit. A wound that lands or worsens while this is the current tick came
    /// from damage; anything else is an infection, a necrosis clock or a heal, and stays silent.
    /// </summary>
    [DataField]
    public TimeSpan LastHit = TimeSpan.MinValue;

    /// <summary>
    /// World-space direction the last hit was travelling in, which is the way its spray goes (G1). Stored
    /// as a vector rather than the attacker's entity because a projectile is usually deleted in the same
    /// tick as the wound it caused. Null when nothing usable was behind the hit.
    /// </summary>
    public Vector2? LastDirection;

    /// <summary>
    /// FIX1: the body's total bleeding severity as the current hit found it. The spray is triggered by a
    /// hit making the body bleed more, so the figure has to be taken before the wounds are created and
    /// compared once they all are.
    /// </summary>
    [DataField]
    public FixedPoint2 BleedBefore;

    /// <summary>FIX1: start of the window <see cref="SpraysInWindow"/> is counted over.</summary>
    [DataField]
    public TimeSpan BurstWindowStart = TimeSpan.MinValue;

    /// <summary>FIX1: sprays this body has thrown since <see cref="BurstWindowStart"/>.</summary>
    [DataField]
    public int SpraysInWindow;
}
