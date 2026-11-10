using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._WF.Traders;

/// <summary>
/// A killable trader's memory of who hurt him and where he is running to. Added when he is first hurt.
/// </summary>
[RegisterComponent]
public sealed partial class TraderFleeComponent : Component
{
    /// <summary>
    /// Everyone who has hurt him. He will not trade with any of them again.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> Attackers = new();

    /// <summary>
    /// The attacker he is keeping away from. Null once things have calmed down.
    /// </summary>
    [ViewVariables]
    public EntityUid? Threat;

    /// <summary>
    /// When he was last hurt.
    /// </summary>
    [ViewVariables]
    public TimeSpan HurtAt;

    /// <summary>
    /// The tile on his ship he is running to, if he has one.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates? Target;

    /// <summary>
    /// Grid tile of <see cref="Target"/>.
    /// </summary>
    [ViewVariables]
    public Vector2i TargetTile;

    /// <summary>
    /// When the run to <see cref="Target"/> last made progress.
    /// </summary>
    [ViewVariables]
    public TimeSpan TargetSince;

    /// <summary>
    /// Where on his grid he was at <see cref="TargetSince"/>.
    /// </summary>
    [ViewVariables]
    public Vector2 TargetFrom;

    /// <summary>
    /// Tiles he could not reach or got stuck on, so they are not picked again.
    /// </summary>
    [ViewVariables]
    public HashSet<Vector2i> Rejected = new();

    /// <summary>
    /// When he next works out where to run.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextThink;

    /// <summary>
    /// When he stops running, if the threat stays far off until then.
    /// </summary>
    [ViewVariables]
    public TimeSpan? CalmAt;
}
