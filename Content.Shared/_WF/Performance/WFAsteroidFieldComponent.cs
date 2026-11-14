using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Performance;

/// <summary>
/// A compact list of one worldgen chunk's asteroid rocks, sent to every player. Rock grids themselves only reach
/// players near them, so radar and the far overlay draw distant rocks from this instead.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFAsteroidFieldComponent : Component
{
    /// <summary>Rock prototype IDs the entries index into.</summary>
    [DataField, AutoNetworkedField]
    public List<string> Protos = new();

    /// <summary>Look names the entries index into.</summary>
    [DataField, AutoNetworkedField]
    public List<string> Looks = new();

    [DataField, AutoNetworkedField]
    public List<WFAsteroidFieldRock> Rocks = new();
}

/// <summary>
/// One rock in a field: its grid, where its centre is on the map, and how it looks. <see cref="Look"/> is
/// <see cref="NoLook"/> for the default look.
/// </summary>
[Serializable, NetSerializable]
public record struct WFAsteroidFieldRock(
    NetEntity Grid,
    Vector2 Position,
    float Rotation,
    byte Proto,
    byte Variant,
    byte Look,
    Color RockColor,
    Color VeinColor)
{
    public const byte NoLook = byte.MaxValue;
}
