using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Shuttles.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShuttleNavMarkerComponent : Component
{
    public const int MaxMarkers = 5;

    [DataField, AutoNetworkedField]
    public List<ShuttleNavMarker> Markers = new();

    [DataField, AutoNetworkedField]
    public int NextId = 1;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class ShuttleNavMarker
{
    [DataField]
    public int Id;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public ShuttleNavMarkerKind Kind;

    [DataField]
    public Vector2 Coordinates;

    [DataField]
    public int MapId;

    [DataField]
    public NetEntity? Target;

    [DataField]
    public Color Color;
}

[Serializable, NetSerializable]
public enum ShuttleNavMarkerKind : byte
{
    Coordinates,
    Entity,
}
